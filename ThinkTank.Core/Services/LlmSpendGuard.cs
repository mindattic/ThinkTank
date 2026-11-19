using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ThinkTank.Core.Data;

namespace ThinkTank.Core.Services;

/// <summary>The server-side spend caps (TT-T47), from ThinkTank's Vault settings group "Spend limits".</summary>
public sealed record SpendLimits(
    int MaxParticipants, int MaxRoundsPerRun, int MaxTokensPerReply, int MaxConcurrentRuns,
    int DailyCallBudget, int ActionPermitLimit, int ActionWindowSeconds);

/// <summary>
/// Who an LLM call is made for (TT-T47). The page that starts a run, vote or generation opens a scope with the
/// signed-in account and the conversation; every call made inside that async flow (including work it starts in the
/// background) is charged to it. A call with no scope is refused when spend limits are on: nothing spends a key
/// anonymously.
/// </summary>
public sealed record LlmCaller(string UserName, string? ConversationId, string Kind)
{
    private static readonly AsyncLocal<LlmCaller?> current = new();

    /// <summary>The caller for the code running now, or null.</summary>
    public static LlmCaller? Current => current.Value;

    /// <summary>Charges calls made until the returned scope is disposed to <paramref name="userName"/>.</summary>
    public static IDisposable Begin(string? userName, string? conversationId, string kind)
    {
        var previous = current.Value;
        current.Value = string.IsNullOrWhiteSpace(userName) ? null : new LlmCaller(userName.Trim(), conversationId, kind);
        return new Scope(previous);
    }

    private sealed class Scope(LlmCaller? previous) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            current.Value = previous;
        }
    }
}

/// <summary>Where usage rows live: the <c>app.LlmUsage</c> table (a seam for tests).</summary>
public interface ILlmUsageStore
{
    Task<int> CountSinceAsync(string userName, DateTime sinceUtc, CancellationToken ct = default);
    Task AddAsync(IReadOnlyList<LlmUsage> rows, CancellationToken ct = default);
    Task CompleteAsync(LlmUsage row, CancellationToken ct = default);
    /// <summary>One account's calls in the UTC day that starts at <paramref name="dayStartUtc"/>, oldest first.</summary>
    Task<IReadOnlyList<LlmUsage>> ListDayAsync(string userName, DateTime dayStartUtc, CancellationToken ct = default);
}

/// <summary><see cref="ILlmUsageStore"/> over <see cref="ThinkTankDbContext"/>.</summary>
public sealed class EfLlmUsageStore(IDbContextFactory<ThinkTankDbContext> contexts) : ILlmUsageStore
{
    public async Task<int> CountSinceAsync(string userName, DateTime sinceUtc, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        return await db.LlmUsage.CountAsync(u => u.UserName == userName && u.StartedUtc >= sinceUtc, ct);
    }

    public async Task AddAsync(IReadOnlyList<LlmUsage> rows, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        db.LlmUsage.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(LlmUsage row, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await db.LlmUsage.Where(u => u.Id == row.Id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.DurationMs, row.DurationMs)
            .SetProperty(u => u.ResponseChars, row.ResponseChars)
            .SetProperty(u => u.TokensIn, row.TokensIn)
            .SetProperty(u => u.TokensOut, row.TokensOut)
            .SetProperty(u => u.Error, row.Error)
            .SetProperty(u => u.Cancelled, row.Cancelled), ct);
    }

    public async Task<IReadOnlyList<LlmUsage>> ListDayAsync(string userName, DateTime dayStartUtc, CancellationToken ct = default)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var end = dayStartUtc.AddDays(1);
        return await db.LlmUsage.AsNoTracking()
            .Where(u => u.UserName == userName && u.StartedUtc >= dayStartUtc && u.StartedUtc < end)
            .OrderBy(u => u.StartedUtc)
            .ToListAsync(ct);
    }
}

/// <summary>
/// ThinkTank's spend limits, enforced on the server (TT-T47): the per-account daily call budget, runs at once per
/// account, most participants, rounds and tokens, and the Start / Vote / Generate rate limit. Every LLM call is
/// recorded through <see cref="ILlmUsageStore"/> before it is made, so it counts against the budget at once, and
/// completed when it returns. Every number is a Vault setting, read live.
/// </summary>
public sealed class LlmSpendGuard
{
    private readonly ThinkTankSettingsService settings;
    private readonly ILlmUsageStore store;
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> budgetLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> runningRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly object limiterLock = new();
    private (int Permits, int Window, PartitionedRateLimiter<string> Limiter)? actionLimiter;

    public LlmSpendGuard(ThinkTankSettingsService settings, ILlmUsageStore store, TimeProvider? clock = null, ILogger<LlmSpendGuard>? logger = null)
    {
        this.settings = settings;
        this.store = store;
        this.clock = clock ?? TimeProvider.System;
        this.logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public SpendLimits Limits => settings.Spend;

    /// <summary>The token cap for one reply: the request, never above "Most tokens per reply".</summary>
    public int ClampTokens(int requested) => Math.Clamp(requested, 1, Limits.MaxTokensPerReply);

    /// <summary>Refuses a run or vote with more seats than "Most participants per conversation".</summary>
    public void CheckParticipants(int count)
    {
        var max = Limits.MaxParticipants;
        if (count > max)
            throw new UserFacingException($"A conversation can have at most {max} participants. Remove {count - max} to continue.");
    }

    /// <summary>
    /// Start / Vote / Generate rate limit per account (System.Threading.RateLimiting, fixed window). Blazor actions
    /// arrive over the circuit, not as HTTP requests, so ASP.NET's middleware can't see them; the same limiter runs here.
    /// </summary>
    public void CheckAction(string? userName, string action)
    {
        var user = RequireUser(userName);
        var limits = Limits;
        using var lease = ActionLimiter(limits).AttemptAcquire($"{user.ToLowerInvariant()}|{action}");
        if (!lease.IsAcquired)
            throw new UserFacingException(
                $"Too many {action} actions: at most {limits.ActionPermitLimit} every {limits.ActionWindowSeconds} seconds. Wait a moment and try again.");
    }

    /// <summary>
    /// Starts a run for <paramref name="userName"/>: checks the participant cap and runs-at-once, and returns a lease
    /// whose <see cref="RunLease.MaxRounds"/> is the rounds the run may take. Dispose it when the run ends.
    /// </summary>
    public RunLease BeginRun(string? userName, int participants, int requestedRounds)
    {
        var user = RequireUser(userName);
        CheckParticipants(participants);
        var limits = Limits;
        var max = limits.MaxConcurrentRuns;
        var admitted = false;
        runningRuns.AddOrUpdate(user, _ => { admitted = true; return 1; }, (_, n) =>
        {
            if (n >= max) { admitted = false; return n; }
            admitted = true;
            return n + 1;
        });
        if (!admitted)
            throw new UserFacingException($"You already have {max} conversation(s) running. Pause one before starting another.");
        return new RunLease(this, user, Math.Clamp(requestedRounds, 1, limits.MaxRoundsPerRun));
    }

    /// <summary>Runs this account has going now.</summary>
    public int RunningCount(string userName) => runningRuns.TryGetValue(userName, out var n) ? n : 0;

    private void EndRun(string user) =>
        runningRuns.AddOrUpdate(user, 0, (_, n) => Math.Max(0, n - 1));

    /// <summary>
    /// Charges one LLM call to the current <see cref="LlmCaller"/>: refuses it past the daily budget, otherwise
    /// records it (it counts at once) and returns the record to complete with <see cref="CompleteAsync"/>.
    /// </summary>
    public async Task<LlmCallRecord> BeginCallAsync(string provider, string model, int promptChars, CancellationToken ct = default)
        => (await BeginCallsAsync([(provider, model)], promptChars, ct))[0];

    /// <summary>Charges several calls at once (a vote asks every voter), all or none.</summary>
    public async Task<IReadOnlyList<LlmCallRecord>> BeginCallsAsync(IReadOnlyList<(string Provider, string Model)> calls, int promptChars,
        CancellationToken ct = default)
    {
        var caller = LlmCaller.Current
            ?? throw new UserFacingException("Your session has no signed-in account for this action. Sign in again to continue.");
        var budget = Limits.DailyCallBudget;
        var now = clock.GetUtcNow().UtcDateTime;
        var dayStart = now.Date;

        var gate = budgetLocks.GetOrAdd(caller.UserName, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            int used;
            List<LlmUsage> rows;
            try
            {
                used = await store.CountSinceAsync(caller.UserName, dayStart, ct);
                if (used + calls.Count > budget)
                    throw new UserFacingException(
                        $"You have used {used} of today's {budget} LLM calls{(calls.Count > 1 ? $" and this needs {calls.Count}" : "")}. " +
                        "The budget resets at midnight UTC.");
                rows = calls.Select(c => new LlmUsage
                {
                    UserName = caller.UserName,
                    ConversationId = caller.ConversationId,
                    Kind = caller.Kind,
                    Provider = Truncate(c.Provider, 64),
                    Model = Truncate(c.Model, 128),
                    StartedUtc = now,
                    PromptChars = promptChars,
                }).ToList();
                await store.AddAsync(rows, ct);
            }
            catch (Exception ex) when (ex is not UserFacingException and not OperationCanceledException)
            {
                // Fail closed: a call that can't be counted is not made.
                logger.LogError(ex, "LLM usage could not be recorded for {User}; refusing the call", caller.UserName);
                throw new UserFacingException("ThinkTank can't record usage right now, so it won't make the call. Try again shortly.");
            }
            return rows.Select(r => new LlmCallRecord(r, Stopwatch.StartNew())).ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Completes a call's usage row. Never throws: the call already happened.</summary>
    public async Task CompleteAsync(LlmCallRecord record, string? response, string? error, bool cancelled)
    {
        var row = record.Row;
        row.DurationMs = (int)Math.Min(int.MaxValue, record.Clock.ElapsedMilliseconds);
        row.ResponseChars = response?.Length;
        row.Error = error is null ? null : Truncate(error, 500);
        row.Cancelled = cancelled;
        try { await store.CompleteAsync(row); }
        catch (Exception ex) { logger.LogWarning(ex, "LLM usage row {Id} could not be completed", row.Id); }
    }

    /// <summary>One account's calls today (UTC), oldest first.</summary>
    public Task<IReadOnlyList<LlmUsage>> TodayAsync(string userName, CancellationToken ct = default) =>
        store.ListDayAsync(userName, clock.GetUtcNow().UtcDateTime.Date, ct);

    private PartitionedRateLimiter<string> ActionLimiter(SpendLimits limits)
    {
        lock (limiterLock)
        {
            if (actionLimiter is { } existing && existing.Permits == limits.ActionPermitLimit && existing.Window == limits.ActionWindowSeconds)
                return existing.Limiter;
            // The numbers changed in Vault (or first use): start a fresh limiter with them.
            actionLimiter?.Limiter.Dispose();
            var permits = limits.ActionPermitLimit;
            var window = TimeSpan.FromSeconds(limits.ActionWindowSeconds);
            var limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));
            actionLimiter = (limits.ActionPermitLimit, limits.ActionWindowSeconds, limiter);
            return limiter;
        }
    }

    private static string RequireUser(string? userName) =>
        string.IsNullOrWhiteSpace(userName)
            ? throw new UserFacingException("Your session has no signed-in account for this action. Sign in again to continue.")
            : userName.Trim();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>A running conversation's admission (see <see cref="BeginRun"/>). Dispose when the run ends.</summary>
    public sealed class RunLease : IDisposable
    {
        private readonly LlmSpendGuard owner;
        private readonly string user;
        private int disposed;

        internal RunLease(LlmSpendGuard owner, string user, int maxRounds)
        {
            this.owner = owner;
            this.user = user;
            MaxRounds = maxRounds;
        }

        /// <summary>The rounds this run may take: the conversation's request, capped by "Most rounds per run".</summary>
        public int MaxRounds { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.EndRun(user);
        }
    }
}

/// <summary>A started call's usage row and timer.</summary>
public sealed record LlmCallRecord(LlmUsage Row, Stopwatch Clock);
