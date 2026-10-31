using ThinkTank.Core.Models;
using MindAttic.Legion;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;

namespace ThinkTank.Core.Services;

/// <summary>
/// Concrete settings service registered in the DI container for production use.
/// Inherits all persistence and configuration logic from <see cref="ThinkTankSettingsService"/>.
/// </summary>
public class SettingsService : ThinkTankSettingsService
{
    /// <summary>ThinkTank's Vault settings for this user and the local key files.</summary>
    public SettingsService() { }

    /// <summary>Explicit settings source and key selector (the host's, or a test's temp ones).</summary>
    public SettingsService(ThinkTankSettingsSource? settings, KeySelector? keys) : base(settings, keys) { }
}

/// <summary>
/// ThinkTank's persistent state. App-level settings (appearance, conversation defaults, provider models)
/// are <see cref="ThinkTankSettings"/>, edited only in MindAttic.Vault.Desktop and read here live through
/// <see cref="ThinkTankSettingsSource"/> (LAMP, TT-T27). Content (participant templates and conversation
/// snapshots) stays in <c>%LOCALAPPDATA%\MindAttic\ThinkTank\Settings.json</c>; the pre-LAMP setting fields
/// in that file are carried through every save untouched, as the rollback. Keys come from Vault's shared
/// named lists through <see cref="KeySelector"/>.
/// </summary>
public class ThinkTankSettingsService
{
    private readonly ThinkTankSettingsSource settings;
    private readonly KeySelector keys;

    /// <summary>Reusable participant templates (both built-in defaults and user-created customs).</summary>
    public List<ParticipantTemplate> Templates { get; } = new();

    /// <summary>Persisted conversation snapshots restored on app launch.</summary>
    public List<PersistedConversation> Conversations { get; } = new();

    /// <summary>Fields of Settings.json this service doesn't own (the pre-LAMP settings), written back as read.</summary>
    private Dictionary<string, System.Text.Json.JsonElement>? legacyFields;

    private const string SettingsFileName = "Settings.json";

    /// <summary>
    /// Guards every <see cref="Save"/> invocation so concurrent mutations (Blazor circuits,
    /// chat persistence) can't interleave their writes and corrupt Settings.json.
    /// </summary>
    private readonly object _saveLock = new();

    private ThinkTankSettings S => settings.Current;

    /// <summary>The settings source in use.</summary>
    public ThinkTankSettingsSource Source => settings;

    /// <summary>Current UI theme name (e.g., "dark", "neon", "dracula").</summary>
    public string? AppearanceTheme => string.IsNullOrWhiteSpace(S.Theme) ? "dark" : S.Theme;

    /// <summary>Height in pixels for UI control elements (buttons, inputs). Range: 28-60.</summary>
    public int? ControlHeight => S.ControlHeight;

    /// <summary>Gutter spacing in pixels between UI elements. Range: 0-30.</summary>
    public int? Gutter => S.Gutter;

    /// <summary>Border radius in pixels for rounded UI elements. Range: 0-24.</summary>
    public int? BorderRadius => S.BorderRadius;

    /// <summary>Global default max tokens per response. Conversations inherit this unless overridden.</summary>
    public int? GlobalMaxTokens => S.GlobalMaxTokens;

    /// <summary>Global default max rounds per conversation. Conversations inherit this unless overridden.</summary>
    public int? GlobalMaxRounds => S.GlobalMaxRounds;

    /// <summary>
    /// Global limit on how many prior <see cref="MindAttic.Legion.SharedTurn"/> entries are passed to
    /// each participant (at least 2).
    /// </summary>
    public int? GlobalMaxContextTurns => Math.Max(2, S.GlobalMaxContextTurns);

    /// <summary>Global default response-length preset (<see cref="ResponseLengthPreset"/>).</summary>
    public string? GlobalResponseLength => ResponseLengthPreset.Normalize(S.GlobalResponseLength);

    /// <summary>
    /// When <c>true</c>, every non-Claude participant routes through the Anthropic API, with the
    /// Claude model roleplaying as that participant's persona.
    /// </summary>
    public bool ClaudeFallbackMode => S.ClaudeFallbackMode;

    /// <summary>
    /// Per-provider non-secret config (type, model, maxTokens) as JSON, keyed by provider id, built from
    /// <see cref="ThinkTankSettings.Providers"/> on each access.
    /// </summary>
    public IReadOnlyDictionary<string, ProviderAuthConfig> ProviderAuth
    {
        get
        {
            var current = S;
            var map = new Dictionary<string, ProviderAuthConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ProviderModels.Ids)
                map[id] = new ProviderAuthConfig(id, AuthJsonFor(id, current));
            return map;
        }
    }

    /// <summary>Production: ThinkTank's Vault settings for this user and the local key files.</summary>
    public ThinkTankSettingsService() : this(null, null) { }

    /// <summary>Loads content from Settings.json; app settings and keys come from <paramref name="settings"/> / <paramref name="keys"/>.</summary>
    public ThinkTankSettingsService(ThinkTankSettingsSource? settings, KeySelector? keys)
    {
        this.settings = settings ?? new ThinkTankSettingsSource(MindAttic.Vault.Settings.JsonSettingsStore<ThinkTankSettings>.ForSettingsApp());
        this.keys = keys ?? ThinkTankKeys.ForCurrentUser();
        MigrateLegacyStorageFolder();
        LoadOrInit();
    }

    // Through VaultPaths so the MINDATTIC_VAULT_LOCAL_ROOT override keeps tests out of the real AppData.
    private static string SettingsRoot => VaultPaths.LocalApp(ThinkTankSettings.AppId);

    private static string LegacySettingsRoot => VaultPaths.LocalApp("LLMThinkTank");

    /// <summary>
    /// One-shot rename of the legacy app-data folder from
    /// <c>%LOCALAPPDATA%\MindAttic\LLMThinkTank</c> to <c>%LOCALAPPDATA%\MindAttic\ThinkTank</c>
    /// after the app rename. Idempotent: only runs when the new folder is missing
    /// and the legacy one exists.
    /// </summary>
    private static void MigrateLegacyStorageFolder()
    {
        try
        {
            var newRoot = SettingsRoot;
            var oldRoot = LegacySettingsRoot;
            if (Directory.Exists(newRoot)) return;
            if (!Directory.Exists(oldRoot)) return;
            Directory.Move(oldRoot, newRoot);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ThinkTank.Settings] legacy folder migration failed: {ex.Message}");
        }
    }

    private static string PersonalitiesRoot
        => Path.Combine(SettingsRoot, "Personalities");

    private static string SettingsPath
        => Path.Combine(SettingsRoot, SettingsFileName);

    /// <summary>Loads templates and conversations from disk, or seeds the default templates.</summary>
    private void LoadOrInit()
    {
        if (TryLoad())
        {
            EnsureDefaultsIfMissing();
            EnsurePersonalityFiles();
            return;
        }

        Templates.AddRange(CreateDefaultTemplates());
        EnsurePersonalityFiles();
        Save();
    }

    /// <summary>
    /// Creates the built-in default participant templates — one raw, instruction-free
    /// seat per default provider in <see cref="LlmProviderCatalog"/>. (Legion's library
    /// no longer carries per-provider "default" personas: a bare LLM has no persona, so
    /// the raw roundtable seats are derived from the provider catalog here.) The empty
    /// PersonalityMarkdown is wrapped with a generic roundtable framing by the call layer
    /// in <see cref="ThinkTankService"/> so the LLM still has context about the format.
    /// Template/persona ids keep the historical <c>default-&lt;provider&gt;</c> shape so
    /// existing settings files continue to resolve.
    /// </summary>
    private static List<ParticipantTemplate> CreateDefaultTemplates()
    {
        var allowedProviders = LlmProviderCatalog.DefaultIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<ParticipantTemplate>();
        foreach (var provider in LlmProviderCatalog.All)
        {
            if (!allowedProviders.Contains(provider.Id))
                continue;

            var personaId = $"default-{provider.Id}";
            result.Add(new ParticipantTemplate(
                TemplateId: $"legion-{personaId}",
                ProviderId: provider.Id,
                DisplayName: provider.DisplayName,
                PersonalityMarkdown: "",
                AuthOverrideJson: null,
                IsDefault: true)
            {
                PersonaId = personaId
            });
        }
        return result;
    }

    /// <summary>
    /// Backfills default templates and provider auth entries for any providers that were
    /// added to the application after the user's settings file was originally created.
    /// Also ensures existing providers have the <c>maxTokens</c> field if it was added later.
    /// </summary>
    private void EnsureDefaultsIfMissing()
    {
        try
        {
            var defaults = CreateDefaultTemplates();
            var changed = false;

            // Remove default templates pointing at providers no longer supported by this app.
            var supportedProviders = new HashSet<string>(defaults.Select(d => d.ProviderId));
            var removedTemplates = Templates.RemoveAll(t => t.IsDefault && !supportedProviders.Contains(t.ProviderId));
            if (removedTemplates > 0)
                changed = true;

            foreach (var d in defaults)
            {
                if (!Templates.Any(t => t.ProviderId == d.ProviderId && t.IsDefault))
                {
                    var insertIdx = Templates.Count(t => t.IsDefault);
                    Templates.Insert(insertIdx, d);
                    changed = true;
                }
            }

            var defaultNames = new HashSet<string>(defaults.Select(d => d.DisplayName));
            for (var i = 0; i < Templates.Count; i++)
            {
                if (!Templates[i].IsDefault && defaultNames.Contains(Templates[i].DisplayName))
                {
                    Templates[i] = Templates[i] with { IsDefault = true };
                    changed = true;
                }
            }

            if (changed)
                Save();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ThinkTank.Settings] EnsureDefaultsIfMissing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates default personality markdown files on disk for each provider.
    /// These files serve as user-editable personality templates that can be customized
    /// without modifying application settings directly.
    /// </summary>
    private void EnsurePersonalityFiles()
    {
        try
        {
            Directory.CreateDirectory(PersonalitiesRoot);

            WriteIfMissing("ChatGPT.md",
                "# ChatGPT\n\nYou are ChatGPT, made by OpenAI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be conversational and curious. 2-3 sentences max.\n");

            WriteIfMissing("Claude.md",
                "# Claude\n\nYou are Claude, made by Anthropic. You are in a live roundtable with other AI systems. Read what they said and engage directly. Be thoughtful and honest. 2-3 sentences max.\n");

            WriteIfMissing("Gemini.md",
                "# Gemini\n\nYou are Gemini, made by Google. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be analytical and creative. 2-3 sentences max.\n");

            WriteIfMissing("DeepSeek.md",
                "# DeepSeek\n\nYou are DeepSeek, made by DeepSeek AI. You are in a live roundtable with other AI systems. Read what they said and engage directly. Be precise and insightful. 2-3 sentences max.\n");

        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ThinkTank.Settings] EnsurePersonalityFiles failed: {ex.Message}");
        }

        void WriteIfMissing(string fileName, string markdown)
        {
            var path = Path.Combine(PersonalitiesRoot, fileName);
            if (!File.Exists(path))
                File.WriteAllText(path, markdown);
        }
    }

    /// <summary>
    /// Attempts to deserialize Settings.json from disk into the current instance.
    /// Returns <c>false</c> if the file doesn't exist or deserialization fails.
    /// </summary>
    private bool TryLoad()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return false;

            var json = File.ReadAllText(SettingsPath);
            var dto = System.Text.Json.JsonSerializer.Deserialize<PersistedSettings>(json);
            if (dto is null)
                return false;

            Templates.Clear();
            if (dto.Templates is not null)
                Templates.AddRange(dto.Templates);

            Conversations.Clear();
            if (dto.Conversations is not null)
                Conversations.AddRange(dto.Conversations);

            legacyFields = dto.Other;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ThinkTank.Settings] failed to load Settings.json (will reinitialize with defaults): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes templates and conversations to <c>Settings.json</c> (the pre-LAMP setting fields go back
    /// exactly as they were read). Locked, and written to a sibling <c>.tmp</c> then moved, so a crash
    /// mid-serialize can't truncate the file.
    /// </summary>
    private void Save()
    {
        lock (_saveLock)
        {
            try
            {
                var dto = new PersistedSettings
                {
                    Templates = Templates,
                    Conversations = Conversations,
                    Other = legacyFields,
                };

                var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                Directory.CreateDirectory(SettingsRoot);
                var tmpPath = SettingsPath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, SettingsPath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ThinkTank.Settings] Save failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Returns the non-secret config JSON (<c>type</c>, <c>model</c>, <c>maxTokens</c>) for a provider ThinkTank
    /// keeps a model setting for, or <c>"{}"</c> otherwise.
    /// </summary>
    public string GetAuthJson(string providerId)
        => ProviderModels.Ids.Contains(providerId, StringComparer.OrdinalIgnoreCase) ? AuthJsonFor(providerId, S) : "{}";

    private static string AuthJsonFor(string providerId, ThinkTankSettings current)
    {
        var p = current.Providers?.For(providerId) ?? new ProviderModelSettings();
        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = ProviderModels.TypeFor(providerId),
            ["model"] = p.Model ?? "",
            ["maxTokens"] = p.MaxTokens,
        };
        return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Switches Claude fallback mode (a run control on the chat page) and saves it to ThinkTank's Vault
    /// settings. When enabled, every non-Claude participant routes through the Anthropic API while keeping
    /// its unique persona prompt.
    /// </summary>
    public void SetClaudeFallbackMode(bool enabled) => settings.Update(s => s.ClaudeFallbackMode = enabled);

    /// <summary>Replaces all persisted conversations and writes to disk.</summary>
    public void SetConversations(IEnumerable<PersistedConversation> convos)
    {
        var snapshot = convos.ToList();
        Conversations.Clear();
        Conversations.AddRange(snapshot);
        Save();
    }

    /// <summary>Replaces all participant templates and writes to disk.</summary>
    public void SaveTemplates(IEnumerable<ParticipantTemplate> templates)
    {
        var snapshot = templates.ToList();
        Templates.Clear();
        Templates.AddRange(snapshot);
        Save();
    }

    /// <summary>
    /// Opt-in: replace the participant templates with <paramref name="count"/> personas
    /// drawn from MindAttic.Legion's <see cref="PersonaLibrary"/>. Each persona is paired
    /// with one of the active providers (round-robin); when more personas are requested
    /// than there are providers with keys, the remaining slots fall back to the
    /// supplied <paramref name="fallbackProviderId"/> (default "claude").
    ///
    /// Personas are guaranteed unique within a single call (no-replacement sampling)
    /// so a roundtable never has two identical voices.
    /// </summary>
    public void LoadLegionPersonaTemplates(int count, string fallbackProviderId = "claude", Random? rng = null)
    {
        if (count <= 0) return;

        // Active providers = those with a non-empty apiKey resolvable via the standard
        // precedence chain (disk auth JSON, then runtime / Vault overrides). Going through
        // GetKeyForProvider ensures cloud-only providers (key in Vault, empty on disk)
        // still register as active.
        var available = ProviderAuth.Keys
            .Where(id => !string.IsNullOrWhiteSpace(GetKeyForProvider(id, null)))
            .ToList();

        var voters = VoterFactory.GenerateUniqueVoters(count, available, fallbackProviderId, rng);

        Templates.Clear();
        foreach (var v in voters)
        {
            var personaId = PersonaIdFromVoterId(v.VoterId);
            Templates.Add(new ParticipantTemplate(
                TemplateId: ChatConversationsService.NewId(),
                ProviderId: v.ProviderId,
                DisplayName: v.Name,
                PersonalityMarkdown: v.PersonalityMarkdown,
                AuthOverrideJson: null,
                IsDefault: false)
            {
                PersonaId = personaId
            });
        }
        Save();
    }

    /// <summary>
    /// Recovers the Legion <c>Persona.Id</c> from a <see cref="VoterProfile.VoterId"/> produced by
    /// <see cref="VoterFactory.GenerateUniqueVoters"/>, which mints ids as
    /// <c>{personaId}-{8 hex chars}</c>. Strips that trailing disambiguator so the persona id
    /// can key into the psychometric store; returns the input unchanged if it doesn't match.
    /// </summary>
    public static string? PersonaIdFromVoterId(string voterId)
    {
        if (string.IsNullOrWhiteSpace(voterId)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(voterId, "^(?<id>.+)-[0-9a-fA-F]{8}$");
        return m.Success ? m.Groups["id"].Value : voterId;
    }

    /// <summary>
    /// The key a participant uses: an explicit per-persona override first, else ThinkTank's active key for
    /// the provider from Vault's shared named lists (its assigned key, or the shared order), skipping keys
    /// Vault has flagged. Empty when there is none. For call paths that can't be wrapped (Legion voting);
    /// chat calls go through <see cref="ExecuteWithKeyAsync"/> so they fail over.
    /// </summary>
    public string GetKeyForProvider(string providerId, string? apiKeyOverride)
    {
        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
            return apiKeyOverride;
        return keys.GetActiveKey(providerId, ThinkTankSettings.AppId)?.Value ?? "";
    }

    /// <summary>
    /// Runs <paramref name="call"/> with ThinkTank's keys for <paramref name="providerId"/>, failing over to the
    /// next when one is refused (401/403/quota/429) and flagging it in Vault. With no usable key throws
    /// <see cref="InvalidOperationException"/> naming the provider (never a key value).
    /// </summary>
    public async Task<T> ExecuteWithKeyAsync<T>(string providerId, Func<string, CancellationToken, Task<T>> call, CancellationToken ct = default)
    {
        try
        {
            return await keys.ExecuteAsync(providerId, ThinkTankSettings.AppId, call, ct).ConfigureAwait(false);
        }
        catch (AllKeysFailedException ex)
        {
            throw new InvalidOperationException(ex.NoKeys
                ? $"No API key configured for provider '{providerId}'. Add one in MindAttic Vault."
                : ex.Message + " Fix or replace the key in MindAttic Vault.", ex);
        }
    }

    /// <summary>Settings.json: the content ThinkTank owns, plus every other field carried through unchanged.</summary>
    private sealed class PersistedSettings
    {
        public List<ParticipantTemplate>? Templates { get; set; }
        public List<PersistedConversation>? Conversations { get; set; }

        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement>? Other { get; set; }
    }
}
