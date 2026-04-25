using System.Text.Json;
using LLMThinkTank.Core.Models;

namespace LLMThinkTank.Core.Services;

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
/// <c>LocalApplicationData/MindAttic/LLMThinkTank/Conversations/{chatId}/</c> containing:
/// <list type="bullet">
///   <item><c>chat.json</c> - Array of turn entries with participant IDs, text, round numbers</item>
///   <item><c>{modelId}.md</c> - Per-model perspective markdown files for extended context</item>
/// </list>
/// </summary>
public static class ChatStorage
{
    /// <summary>Returns the folder path for a specific conversation's persistent data.</summary>
    public static string GetChatFolder(string chatId)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "LLMThinkTank", "Conversations", chatId);

    /// <summary>Returns the path to a conversation's <c>chat.json</c> turn history file.</summary>
    public static string GetChatJsonPath(string chatId)
        => Path.Combine(GetChatFolder(chatId), "chat.json");

    /// <summary>Returns the path to a model's perspective markdown file within a conversation folder.</summary>
    public static string GetPerspectivePath(string chatId, string modelId)
        => Path.Combine(GetChatFolder(chatId), $"{modelId}.md");

    /// <summary>
    /// Appends a new entry to the conversation's <c>chat.json</c> file. Creates the file
    /// and parent directory if they don't exist. Reads the existing array, appends, and rewrites.
    /// </summary>
    public static async Task AppendChatJsonAsync(string chatId, object entry, CancellationToken cancellationToken = default)
    {
        var folder = GetChatFolder(chatId);
        Directory.CreateDirectory(folder);

        var path = GetChatJsonPath(chatId);

        List<JsonElement> items;
        if (File.Exists(path))
        {
            await using var read = File.OpenRead(path);
            items = await JsonSerializer.DeserializeAsync<List<JsonElement>>(read, cancellationToken: cancellationToken) ?? new();
        }
        else
        {
            items = new();
        }

        var elem = JsonSerializer.SerializeToElement(entry);
        items.Add(elem);

        await using var write = File.Create(path);
        await JsonSerializer.SerializeAsync(write, items, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
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
    /// Loads all conversation turns from the <c>chat.json</c> file, filtering for entries
    /// of type "turn" and parsing participant IDs, text, round numbers, and error flags.
    /// Returns an empty list if the file doesn't exist.
    /// </summary>
    public static async Task<List<PersistedTurn>> LoadTurnsAsync(string chatId, CancellationToken cancellationToken = default)
    {
        var path = GetChatJsonPath(chatId);
        if (!File.Exists(path))
            return new();

        await using var read = File.OpenRead(path);
        var items = await JsonSerializer.DeserializeAsync<List<JsonElement>>(read, cancellationToken: cancellationToken) ?? new();

        var turns = new List<PersistedTurn>();
        foreach (var item in items)
        {
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
}
