using NUnit.Framework;
using ThinkTank.Core.Data;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>TT-T47: the server-side spend limits and the usage record.</summary>
[TestFixture, NonParallelizable]
public class SpendGuardTests
{
    private sealed class MemoryUsageStore : ILlmUsageStore
    {
        private long nextId;
        public List<LlmUsage> Rows { get; } = new();
        public bool Broken { get; set; }

        public Task<int> CountSinceAsync(string userName, DateTime sinceUtc, CancellationToken ct = default)
        {
            if (Broken) throw new InvalidOperationException("database down");
            return Task.FromResult(Rows.Count(r => r.UserName == userName && r.StartedUtc >= sinceUtc));
        }

        public Task AddAsync(IReadOnlyList<LlmUsage> rows, CancellationToken ct = default)
        {
            foreach (var r in rows) { r.Id = ++nextId; Rows.Add(r); }
            return Task.CompletedTask;
        }

        public Task CompleteAsync(LlmUsage row, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LlmUsage>> ListDayAsync(string userName, DateTime dayStartUtc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LlmUsage>>(Rows.Where(r => r.UserName == userName && r.StartedUtc >= dayStartUtc
                && r.StartedUtc < dayStartUtc.AddDays(1)).ToList());
    }

    private static (LlmSpendGuard Guard, MemoryUsageStore Store) Guard(Action<ThinkTankSettings>? change = null)
    {
        var settings = new SettingsService(TestLamp.Settings(change), TestLamp.KeysWith(("openai", "sk-test-not-a-key")));
        var store = new MemoryUsageStore();
        return (new LlmSpendGuard(settings, store), store);
    }

    [Test]
    public async Task TheDailyBudget_BlocksTheNextCall_WithAClearMessage()
    {
        var (guard, store) = Guard(s => s.DailyCallBudget = 2);
        using var _ = LlmCaller.Begin("ryan", "chat-1", "turn");
        await guard.BeginCallAsync("openai", "m", 10);
        await guard.BeginCallAsync("openai", "m", 10);

        Assert.That(async () => await guard.BeginCallAsync("openai", "m", 10),
            Throws.InstanceOf<UserFacingException>().With.Message.Contains("2 of today's 2"));
        Assert.That(store.Rows, Has.Count.EqualTo(2));
        Assert.That(store.Rows.All(r => r.UserName == "ryan" && r.ConversationId == "chat-1" && r.Kind == "turn"), Is.True);
    }

    [Test]
    public void TheBudget_IsPerAccount()
    {
        var (guard, _) = Guard(s => s.DailyCallBudget = 1);
        using (LlmCaller.Begin("ryan", null, "turn"))
            Assert.That(async () => await guard.BeginCallAsync("openai", "m", 1), Throws.Nothing);
        using (LlmCaller.Begin("erin", null, "turn"))
            Assert.That(async () => await guard.BeginCallAsync("openai", "m", 1), Throws.Nothing);
    }

    [Test]
    public void AVote_NeedsRoomForEveryVoter()
    {
        var (guard, store) = Guard(s => s.DailyCallBudget = 3);
        using var _ = LlmCaller.Begin("ryan", null, "vote");
        Assert.That(async () => await guard.BeginCallsAsync([("openai", ""), ("claude", ""), ("gemini", ""), ("deepseek", "")], 5),
            Throws.InstanceOf<UserFacingException>());
        Assert.That(store.Rows, Is.Empty, "all or none");
    }

    [Test]
    public void NoSignedInAccount_NoCall()
    {
        var (guard, _) = Guard();
        Assert.That(async () => await guard.BeginCallAsync("openai", "m", 1), Throws.InstanceOf<UserFacingException>());
    }

    [Test]
    public void AnUnrecordableCall_IsNotMade()
    {
        var (guard, store) = Guard();
        store.Broken = true;
        using var _ = LlmCaller.Begin("ryan", null, "turn");
        Assert.That(async () => await guard.BeginCallAsync("openai", "m", 1),
            Throws.InstanceOf<UserFacingException>().With.Message.Contains("can't record usage"));
    }

    [Test]
    public void Runs_AreCappedPerAccount_AndRoundsAreClamped()
    {
        var (guard, _) = Guard(s => { s.MaxConcurrentRuns = 1; s.MaxRoundsPerRun = 5; s.MaxParticipants = 3; });
        using (var lease = guard.BeginRun("ryan", participants: 2, requestedRounds: 999))
        {
            Assert.That(lease.MaxRounds, Is.EqualTo(5), "raising rounds in the browser has no effect past the cap");
            Assert.That(() => guard.BeginRun("ryan", 2, 3), Throws.InstanceOf<UserFacingException>().With.Message.Contains("running"));
            Assert.That(() => guard.BeginRun("erin", 2, 3).Dispose(), Throws.Nothing);
        }
        Assert.That(() => guard.BeginRun("ryan", 2, 3).Dispose(), Throws.Nothing, "the lease was released");
        Assert.That(() => guard.BeginRun("ryan", 4, 3), Throws.InstanceOf<UserFacingException>().With.Message.Contains("at most 3"));
    }

    [Test]
    public void Actions_AreRateLimited_PerAccountAndAction()
    {
        var (guard, _) = Guard(s => { s.ActionPermitLimit = 2; s.ActionWindowSeconds = 3600; });
        guard.CheckAction("ryan", "Start");
        guard.CheckAction("ryan", "Start");
        Assert.That(() => guard.CheckAction("ryan", "Start"), Throws.InstanceOf<UserFacingException>());
        Assert.That(() => guard.CheckAction("ryan", "Vote"), Throws.Nothing);
        Assert.That(() => guard.CheckAction("erin", "Start"), Throws.Nothing);
        Assert.That(() => guard.CheckAction(null, "Start"), Throws.InstanceOf<UserFacingException>());
    }

    [Test]
    public void Tokens_AreClampedToTheCap()
    {
        var (guard, _) = Guard(s => s.MaxTokensPerReply = 1000);
        Assert.That(guard.ClampTokens(32768), Is.EqualTo(1000));
        Assert.That(guard.ClampTokens(200), Is.EqualTo(200));
    }

    [Test]
    public async Task TodaysCalls_CanBeListed()
    {
        var (guard, _) = Guard();
        using (LlmCaller.Begin("ryan", "c", "turn"))
            await guard.BeginCallAsync("claude", "m", 3);
        var today = await guard.TodayAsync("ryan");
        Assert.That(today.Single().Provider, Is.EqualTo("claude"));
    }
}
