using System.Text.Json;

namespace LLMThinkTank.Core.Services;

/// <summary>
/// Shared LLM credentials store at <c>%APPDATA%\MindAttic\LLM\providers.json</c>, used as the
/// canonical source of API keys/models across every MindAttic application. Any MindAttic app
/// (LLMThinkTank, LLMVoting, StreetSamurai, future tools) can read and write this file to share
/// the same provider credentials, so the user only configures keys once.
/// <para>
/// File format is a flat object keyed by provider id, with each value being the per-provider
/// auth JSON used by <see cref="LlmThinkTankSettingsService"/>:
/// <code>
/// {
///   "openai":  { "type": "bearer",    "apiKey": "...", "model": "gpt-4.1-mini",     "maxTokens": 2048 },
///   "claude":  { "type": "anthropic", "apiKey": "...", "model": "claude-sonnet-4-6","maxTokens": 2048 },
///   "gemini":  { "type": "google",    "apiKey": "...", "model": "gemini-2.5-flash", "maxTokens": 2048 }
/// }
/// </code>
/// </para>
/// </summary>
public static class MindAtticLlmCredentialsStore
{
    private static readonly object writeLock = new();

    /// <summary>
    /// Environment variable name. When set, overrides <see cref="Root"/> with the supplied
    /// folder path. Used by tests to redirect the shared store to a sandbox.
    /// </summary>
    public const string RootEnvVar = "MINDATTIC_LLM_CREDENTIALS_DIR";

    /// <summary>
    /// Returns the shared credentials root folder. Defaults to Roaming
    /// <c>%APPDATA%\MindAttic\LLM</c>; can be overridden via the
    /// <see cref="RootEnvVar"/> environment variable for testing.
    /// </summary>
    public static string Root
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable(RootEnvVar);
            if (!string.IsNullOrWhiteSpace(overrideDir))
                return overrideDir;

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindAttic", "LLM");
        }
    }

    /// <summary>Returns the path to the shared <c>providers.json</c> credentials file.</summary>
    public static string ProvidersFilePath
        => Path.Combine(Root, "providers.json");

    /// <summary>Returns <c>true</c> if the shared credentials file exists on disk.</summary>
    public static bool Exists() => File.Exists(ProvidersFilePath);

    /// <summary>
    /// Loads every provider's auth JSON from the shared store. Returns an empty dictionary if
    /// the file is missing or unparseable. Each value is the raw JSON object for that provider.
    /// </summary>
    public static Dictionary<string, string> LoadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(ProvidersFilePath))
                return result;

            var raw = File.ReadAllText(ProvidersFilePath);
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                result[prop.Name] = prop.Value.GetRawText();
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Loads a single provider's auth JSON, or <c>null</c> if absent.
    /// </summary>
    public static string? Load(string providerId)
        => LoadAll().TryGetValue(providerId, out var json) ? json : null;

    /// <summary>
    /// Replaces the entire shared providers file with the supplied map. Performs a single
    /// pretty-printed write under a lock so concurrent saves never produce a partial file.
    /// </summary>
    public static void SaveAll(IDictionary<string, string> providers)
    {
        lock (writeLock)
        {
            try
            {
                Directory.CreateDirectory(Root);

                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    foreach (var (providerId, json) in providers.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        writer.WritePropertyName(providerId);
                        try
                        {
                            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                            doc.RootElement.WriteTo(writer);
                        }
                        catch
                        {
                            writer.WriteStartObject();
                            writer.WriteEndObject();
                        }
                    }
                    writer.WriteEndObject();
                }

                File.WriteAllBytes(ProvidersFilePath, stream.ToArray());
            }
            catch { }
        }
    }

    /// <summary>
    /// Upserts a single provider's auth JSON in the shared store, preserving every other provider.
    /// </summary>
    public static void Save(string providerId, string json)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return;

        lock (writeLock)
        {
            var all = LoadAll();
            all[providerId] = string.IsNullOrWhiteSpace(json) ? "{}" : json;
            SaveAll(all);
        }
    }
}
