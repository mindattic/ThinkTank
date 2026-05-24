using System.Text.Json;
using ThinkTank.Core.Models;

namespace ThinkTank.Core.Services;

/// <summary>
/// In-memory log of application events and API diagnostics displayed in the Log panel.
/// Provides a chronological feed of provider interactions, errors, and system events
/// that helps users troubleshoot connectivity and monitor API call activity.
/// </summary>
public class ChatLogService
{
    private readonly List<ChatLogEntry> entries = new();

    /// <summary>Raised when an entry is added or the log is cleared, triggering UI re-render.</summary>
    public event Action? Changed;

    /// <summary>Read-only view of all log entries in chronological order.</summary>
    public IReadOnlyList<ChatLogEntry> Entries => entries;

    /// <summary>
    /// Appends a new log entry and notifies subscribers.
    /// </summary>
    /// <param name="source">Originating component or provider ID (e.g., "openai", "system").</param>
    /// <param name="text">Human-readable log message.</param>
    /// <param name="isError">Whether this entry represents an error condition.</param>
    public void Add(string source, string text, bool isError = false)
    {
        entries.Add(new ChatLogEntry(DateTimeOffset.UtcNow, source, text, isError));
        Changed?.Invoke();
    }

    /// <summary>Removes all log entries and notifies subscribers.</summary>
    public void Clear()
    {
        entries.Clear();
        Changed?.Invoke();
    }
}

/// <summary>
/// Provides file-based persistence for individual conversation data, stored separately
/// from the main Settings.json. Each conversation gets its own folder under
/// <c>LocalApplicationData/MindAttic/ThinkTank/Conversations/{chatId}/</c> containing:
/// <list type="bullet">
///   <item><c>chat.jsonl</c> - Newline-delimited JSON entries (append-only)</item>
///   <item><c>chat.json</c> - Legacy array format; transparently migrated to <c>chat.jsonl</c> on first append/load</item>
///   <item><c>{modelId}.md</c> - Per-model perspective markdown files for extended context</item>
/// </list>
/// </summary>
public static class ChatStorage
{
    /// <summary>
    /// Per-chat append-serialization lock. Guarantees the append-then-flush sequence in
    /// <see cref="AppendChatJsonAsync"/> can't interleave with another concurrent append
    /// for the same chat (round loop racing with a manual user send). One semaphore per
    /// <c>chatId</c>, kept for process lifetime — chats are bounded in count so this won't
    /// leak meaningfully.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _chatLocks = new();

    private static SemaphoreSlim GetChatLock(string chatId)
        => _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Returns the folder path for a specific conversation's persistent data.</summary>
    public static string GetChatFolder(string chatId)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "ThinkTank", "Conversations", chatId);

    /// <summary>Returns the path to a conversation's append-only JSONL log.</summary>
    public static string GetChatJsonlPath(string chatId)
        => Path.Combine(GetChatFolder(chatId), "chat.jsonl");

    /// <summary>Returns the legacy <c>chat.json</c> array-format path (migrated on first read/append).</summary>
    public static string GetChatJsonPath(string chatId)
        => Path.Combine(GetChatFolder(chatId), "chat.json");

    /// <summary>Returns the path to a model's perspective markdown file within a conversation folder.</summary>
    public static string GetPerspectivePath(string chatId, string modelId)
        => Path.Combine(GetChatFolder(chatId), $"{modelId}.md");

    /// <summary>
    /// Appends a new entry to the conversation's <c>chat.jsonl</c> file as a single JSON line.
    /// <para>
    /// Append-only: previous behaviour read-modified-rewrote the entire array on every turn,
    /// which was O(n²) for long conversations and could lose data when two concurrent writes
    /// raced. This version writes one line under a per-chat <see cref="SemaphoreSlim"/> so
    /// two appends serialize cleanly and previous turns never get re-serialized.
    /// </para>
    /// <para>
    /// If a legacy <c>chat.json</c> array file exists, it is migrated to <c>chat.jsonl</c>
    /// in-place (one line per element) before the new entry is appended.
    /// </para>
    /// </summary>
    public static async Task AppendChatJsonAsync(string chatId, object entry, CancellationToken cancellationToken = default)
    {
        var folder = GetChatFolder(chatId);
        Directory.CreateDirectory(folder);

        var sem = GetChatLock(chatId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyChatJsonIfPresent(chatId, cancellationToken).ConfigureAwait(false);

            // Serialize without indentation so the file stays one-record-per-line.
            var line = JsonSerializer.Serialize(entry);
            await using var writer = new StreamWriter(GetChatJsonlPath(chatId), append: true, System.Text.Encoding.UTF8);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// One-shot migration of legacy array-format <c>chat.json</c> to <c>chat.jsonl</c>.
    /// Idempotent: if the JSONL file already exists, the legacy file is left alone
    /// (so a partially-migrated state can't double-count entries). Caller must hold
    /// the per-chat lock.
    /// </summary>
    private static async Task MigrateLegacyChatJsonIfPresent(string chatId, CancellationToken cancellationToken)
    {
        var jsonlPath = GetChatJsonlPath(chatId);
        var legacyPath = GetChatJsonPath(chatId);
        if (File.Exists(jsonlPath) || !File.Exists(legacyPath))
            return;

        try
        {
            await using var read = File.OpenRead(legacyPath);
            var items = await JsonSerializer.DeserializeAsync<List<JsonElement>>(read, cancellationToken: cancellationToken).ConfigureAwait(false) ?? new();
            await using var write = new StreamWriter(jsonlPath, append: false, System.Text.Encoding.UTF8);
            foreach (var item in items)
                await write.WriteLineAsync(item.GetRawText().AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ThinkTank.ChatStorage] failed to migrate legacy chat.json for '{chatId}': {ex.Message}");
            return;
        }

        try
        {
            File.Move(legacyPath, legacyPath + ".migrated", overwrite: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ThinkTank.ChatStorage] migrated but failed to rename legacy chat.json for '{chatId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Reads a model's perspective markdown file, returning empty string if not yet created.
    /// </summary>
    public static async Task<string> ReadPerspectiveAsync(string chatId, string modelId, CancellationToken cancellationToken = default)
    {
        var path = GetPerspectivePath(chatId, modelId);
        if (!File.Exists(path))
            return "";

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    /// <summary>
    /// Writes or overwrites a model's perspective markdown file within the conversation folder.
    /// </summary>
    public static Task WritePerspectiveAsync(string chatId, string modelId, string markdown, CancellationToken cancellationToken = default)
    {
        var folder = GetChatFolder(chatId);
        Directory.CreateDirectory(folder);

        var path = GetPerspectivePath(chatId, modelId);
        return File.WriteAllTextAsync(path, markdown, cancellationToken);
    }

    /// <summary>
    /// Loads all conversation turns from the chat log, filtering for entries of type
    /// "turn" and parsing participant IDs, text, round numbers, and error flags.
    /// Prefers the new <c>chat.jsonl</c> format; if only the legacy <c>chat.json</c>
    /// array exists, migrates it on the fly. Returns an empty list if neither exists.
    /// </summary>
    public static async Task<List<PersistedTurn>> LoadTurnsAsync(string chatId, CancellationToken cancellationToken = default)
    {
        var sem = GetChatLock(chatId);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyChatJsonIfPresent(chatId, cancellationToken).ConfigureAwait(false);

            var jsonlPath = GetChatJsonlPath(chatId);
            if (!File.Exists(jsonlPath))
                return new();

            var turns = new List<PersistedTurn>();
            await foreach (var line in File.ReadLinesAsync(jsonlPath, cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JsonElement item;
                try
                {
                    item = JsonSerializer.Deserialize<JsonElement>(line);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ThinkTank.ChatStorage] skipping malformed line in chat '{chatId}': {ex.Message}");
                    continue;
                }

                if (!item.TryGetProperty("type", out var type) || type.GetString() != "turn")
                    continue;

                var participantId = item.TryGetProperty("participantId", out var pid) ? pid.GetString() ?? "" : "";
                var text = item.TryGetProperty("text", out var textElem) ? textElem.GetString() ?? "" : "";
                var round = item.TryGetProperty("round", out var roundElem) ? roundElem.GetInt32() : 0;
                var isError = item.TryGetProperty("isError", out var errElem) && errElem.ValueKind == JsonValueKind.True;

                turns.Add(new PersistedTurn(participantId, text, round, isError));
            }
            return turns;
        }
        finally
        {
            sem.Release();
        }
    }
}
