using ThinkTank.Core.Models;
using MindAttic.Legion;

namespace ThinkTank.Core.Services;

/// <summary>
/// Concrete settings service registered in the DI container for production use.
/// Inherits all persistence and configuration logic from <see cref="ThinkTankSettingsService"/>.
/// </summary>
public class SettingsService : ThinkTankSettingsService
{
}

/// <summary>
/// Manages all persistent application state: provider authentication credentials, participant
/// templates, conversation history, and appearance settings. Data is stored as a single
/// <c>Settings.json</c> file in the user's <c>LocalApplicationData/MindAttic/ThinkTank</c> folder.
/// <para>
/// <b>Initialization:</b> On first launch, seeds default auth configs for all 11 providers and
/// creates default personality templates. On subsequent launches, loads existing settings and
/// backfills any new providers added since the last update.
/// </para>
/// <para>
/// <b>Persistence:</b> Every mutation method (SetAuthJson, SetAppearanceTheme, SaveTemplates, etc.)
/// immediately writes the full state to disk, ensuring crash-safe persistence.
/// </para>
/// </summary>
public class ThinkTankSettingsService
{
    /// <summary>Per-provider authentication and model configuration, keyed by provider ID.</summary>
    public Dictionary<string, ProviderAuthConfig> ProviderAuth { get; } = new();

    /// <summary>
    /// Default provider auth configs (keyed by provider ID) loaded from an external source
    /// such as User Secrets. Used by <see cref="ResetProvidersToDefaults"/> to restore credentials.
    /// </summary>
    public Dictionary<string, ProviderAuthConfig> ProviderDefaults { get; } = new();

    /// <summary>
    /// Runtime-only apiKey overrides resolved from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// (e.g. Azure Key Vault references via App Service Application Settings, or shared dev user-secrets).
    /// Populated by <see cref="SettingsServiceVaultOverlay.OverlayFromConfiguration"/>.
    /// <para>
    /// Read-through only: <see cref="GetKeyForProvider"/> falls back to this map when the on-disk
    /// auth JSON has no apiKey set. Never serialized, never written back to <see cref="ProviderAuth"/>,
    /// so cloud-resolved secrets cannot leak into <c>Settings.json</c> or the shared
    /// <c>%APPDATA%\MindAttic\LLM\providers.json</c> store via <see cref="Save"/>.
    /// </para>
    /// </summary>
    public Dictionary<string, string> RuntimeApiKeyOverrides { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reusable participant templates (both built-in defaults and user-created customs).</summary>
    public List<ParticipantTemplate> Templates { get; } = new();

    /// <summary>Persisted conversation snapshots restored on app launch.</summary>
    public List<PersistedConversation> Conversations { get; } = new();

    private const string SettingsFileName = "Settings.json";

    /// <summary>Current UI theme name (e.g., "dark", "neon", "dracula").</summary>
    public string? AppearanceTheme { get; private set; } = "dark";

    /// <summary>Height in pixels for UI control elements (buttons, inputs). Range: 28-60.</summary>
    public int? ControlHeight { get; private set; } = 40;

    /// <summary>Gutter spacing in pixels between UI elements. Range: 0-30.</summary>
    public int? Gutter { get; private set; } = 10;

    /// <summary>Border radius in pixels for rounded UI elements. Range: 0-24.</summary>
    public int? BorderRadius { get; private set; } = 10;

    /// <summary>Global default max tokens per response. Conversations inherit this unless overridden.</summary>
    public int? GlobalMaxTokens { get; private set; } = 2048;

    /// <summary>Global default max rounds per conversation. Conversations inherit this unless overridden.</summary>
    public int? GlobalMaxRounds { get; private set; } = 10;

    /// <summary>
    /// When <c>true</c>, every non-Claude participant routes through the Anthropic API, with the
    /// Claude model roleplaying as that participant's persona. Used as a fallback when other
    /// providers are rate-limited or down. Each participant retains its unique persona prompt.
    /// </summary>
    public bool ClaudeFallbackMode { get; private set; }

    /// <summary>
    /// Initializes the settings service by loading from disk or creating default configuration.
    /// </summary>
    public ThinkTankSettingsService()
    {
        MigrateLegacyStorageFolder();
        LoadOrInit();
    }

    private static string SettingsRoot
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "ThinkTank");

    private static string LegacySettingsRoot
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "ThinkTank");

    /// <summary>
    /// One-shot rename of the legacy app-data folder from
    /// <c>%LOCALAPPDATA%\MindAttic\ThinkTank</c> to <c>%LOCALAPPDATA%\MindAttic\ThinkTank</c>
    /// after the app rename. Idempotent: only runs when the new folder is missing
    /// and the legacy one exists, so existing users keep every persisted
    /// conversation, template, and personality file. Failures are silent —
    /// worst case the user starts with a fresh folder, which is exactly what
    /// would have happened without this migration.
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
        catch { }
    }

    private static string PersonalitiesRoot
        => Path.Combine(SettingsRoot, "Personalities");

    private static string SettingsPath
        => Path.Combine(SettingsRoot, SettingsFileName);

    /// <summary>
    /// Loads settings from disk if available, otherwise initializes with defaults for all providers.
    /// <para>
    /// Credential precedence (highest to lowest):
    /// <list type="number">
    ///   <item>Shared MindAttic store at <c>%APPDATA%\MindAttic\LLM\providers.json</c></item>
    ///   <item>Local <c>Settings.json</c> at <c>%LOCALAPPDATA%\MindAttic\ThinkTank</c></item>
    ///   <item>Hardcoded defaults (empty API keys, provider-specific model ids and maxTokens)</item>
    ///   <item><see cref="ProviderDefaults"/> from appsettings + user secrets (only used by <see cref="ResetProvidersToDefaults"/>)</item>
    /// </list>
    /// After loading, any normalizations (e.g., backfilled <c>maxTokens</c>) and any
    /// this-app providers not yet in the shared store are synced upward so sibling
    /// MindAttic apps immediately see the same credentials.
    /// </para>
    /// </summary>
    private void LoadOrInit()
    {
        if (TryLoad())
        {
            OverlaySharedCredentials();
            EnsureDefaultsIfMissing();
            SyncLocalToSharedStore();
            EnsurePersonalityFiles();
            return;
        }

        ProviderAuth["openai"] = new ProviderAuthConfig("openai", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"gpt-4.1-mini\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["claude"] = new ProviderAuthConfig("claude", "{\n  \"type\": \"anthropic\",\n  \"apiKey\": \"\",\n  \"model\": \"claude-sonnet-4-6\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["gemini"] = new ProviderAuthConfig("gemini", "{\n  \"type\": \"google\",\n  \"apiKey\": \"\",\n  \"model\": \"gemini-2.5-flash\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["deepseek"] = new ProviderAuthConfig("deepseek", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"deepseek-chat\",\n  \"maxTokens\": 2048\n}");

        Templates.AddRange(CreateDefaultTemplates());

        AppearanceTheme = "dark";
        OverlaySharedCredentials();
        SyncLocalToSharedStore();
        EnsurePersonalityFiles();
        Save();
    }

    /// <summary>
    /// Pushes every in-memory provider auth entry up to the shared MindAttic store,
    /// via per-key upsert so sibling-app entries already on disk are preserved. Makes
    /// the shared file a complete superset for every provider this app supports, and
    /// propagates any normalizations (like <c>maxTokens</c> backfill) so the next
    /// launch does not have to re-normalize.
    /// </summary>
    private void SyncLocalToSharedStore()
    {
        try
        {
            var shared = MindAtticCredentialStore.ProvidersFileExists()
                ? MindAtticCredentialStore.LoadAllRaw()
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (providerId, cfg) in ProviderAuth)
            {
                if (!shared.TryGetValue(providerId, out var existing) || !string.Equals(existing, cfg.Json, StringComparison.Ordinal))
                    MindAtticCredentialStore.SaveRaw(providerId, cfg.Json);
            }
        }
        catch { }
    }

    /// <summary>
    /// Overlays the shared MindAttic credentials store on top of the local provider auth.
    /// The shared store at <c>%APPDATA%\MindAttic\LLM\providers.json</c> is the canonical
    /// source for API keys/models so every MindAttic app shares the same configuration.
    /// On first run, if the shared store is missing, the current local credentials are
    /// migrated up to it so other apps can pick them up.
    /// </summary>
    private void OverlaySharedCredentials()
    {
        try
        {
            if (!MindAtticCredentialStore.ProvidersFileExists())
            {
                // Migrate: seed the shared store from whatever local credentials exist now,
                // upserting per-key so any sibling-app entries already on disk are preserved.
                foreach (var (providerId, cfg) in ProviderAuth)
                    MindAtticCredentialStore.SaveRaw(providerId, cfg.Json);
                return;
            }

            // Overlay shared values, but only for providers this app supports. Cross-app
            // entries (providers another MindAttic app uses but we don't) stay in the file
            // untouched and never enter our in-memory ProviderAuth.
            var shared = MindAtticCredentialStore.LoadAllRaw();
            foreach (var (providerId, json) in shared)
            {
                if (ProviderAuth.ContainsKey(providerId))
                    ProviderAuth[providerId] = new ProviderAuthConfig(providerId, json);
            }
        }
        catch { }
    }

    /// <summary>
    /// Creates the built-in default participant templates by sourcing one persona per
    /// provider from <see cref="PersonaLibrary.Defaults"/>. Each persona has an empty
    /// PersonalityMarkdown by Legion convention — the call layer in
    /// <see cref="ThinkTankService"/> wraps empty prompts with a generic roundtable
    /// framing so the LLM still has context about the format.
    /// </summary>
    private static List<ParticipantTemplate> CreateDefaultTemplates()
    {
        const string defaultPrefix = "default-";
        var allowedProviders = LlmProviderCatalog.DefaultIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<ParticipantTemplate>(PersonaLibrary.Defaults.Count);
        foreach (var persona in PersonaLibrary.Defaults)
        {
            var providerId = persona.Id.StartsWith(defaultPrefix, StringComparison.Ordinal)
                ? persona.Id[defaultPrefix.Length..]
                : persona.Id;

            if (!allowedProviders.Contains(providerId))
                continue;

            result.Add(new ParticipantTemplate(
                TemplateId: $"legion-{persona.Id}",
                ProviderId: providerId,
                DisplayName: persona.Name,
                PersonalityMarkdown: persona.PersonalityMarkdown,
                AuthOverrideJson: null,
                IsDefault: true));
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

            // Drop any provider auth entry that isn't supported by this app. Cross-app entries
            // live only in the shared %APPDATA%\MindAttic\LLM\providers.json store; they are
            // never carried in local Settings.json or this app's in-memory ProviderAuth.
            foreach (var key in ProviderAuth.Keys.Where(k => !supportedProviders.Contains(k)).ToList())
            {
                ProviderAuth.Remove(key);
                changed = true;
            }

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

            // Seed missing ProviderAuth entries for new providers
            var defaultAuths = new Dictionary<string, string>
            {
                ["openai"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"gpt-4.1-mini\",\n  \"maxTokens\": 2048\n}",
                ["claude"] = "{\n  \"type\": \"anthropic\",\n  \"apiKey\": \"\",\n  \"model\": \"claude-sonnet-4-6\",\n  \"maxTokens\": 2048\n}",
                ["gemini"] = "{\n  \"type\": \"google\",\n  \"apiKey\": \"\",\n  \"model\": \"gemini-2.5-flash\",\n  \"maxTokens\": 2048\n}",
                ["deepseek"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"deepseek-chat\",\n  \"maxTokens\": 2048\n}"
            };

            foreach (var (providerId, defaultJson) in defaultAuths)
            {
                if (!ProviderAuth.ContainsKey(providerId))
                {
                    ProviderAuth[providerId] = new ProviderAuthConfig(providerId, defaultJson);
                    changed = true;
                    continue;
                }

                var cfg = ProviderAuth[providerId];
                try
                {
                    var existing = System.Text.Json.Nodes.JsonNode.Parse(cfg.Json)?.AsObject();
                    var defaultsJson = System.Text.Json.Nodes.JsonNode.Parse(defaultJson)?.AsObject();
                    if (existing is null || defaultsJson is null)
                        continue;

                    var providerChanged = false;
                    foreach (var prop in defaultsJson)
                    {
                        if (existing.ContainsKey(prop.Key))
                            continue;

                        existing[prop.Key] = prop.Value?.DeepClone();
                        providerChanged = true;
                    }

                    if (providerChanged)
                    {
                        ProviderAuth[providerId] = new ProviderAuthConfig(
                            providerId,
                            existing.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        changed = true;
                    }
                }
                catch
                {
                    ProviderAuth[providerId] = new ProviderAuthConfig(providerId, defaultJson);
                    changed = true;
                }
            }

            if (changed)
                Save();
        }
        catch { }
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
        catch { }

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

            ProviderAuth.Clear();
            foreach (var kvp in dto.ProviderAuth)
                ProviderAuth[kvp.Key] = new ProviderAuthConfig(kvp.Key, kvp.Value ?? "{}");

            Templates.Clear();
            if (dto.Templates is not null)
                Templates.AddRange(dto.Templates);

            Conversations.Clear();
            if (dto.Conversations is not null)
                Conversations.AddRange(dto.Conversations);

            AppearanceTheme = string.IsNullOrWhiteSpace(dto.AppearanceTheme) ? "dark" : dto.AppearanceTheme;
            ControlHeight = dto.ControlHeight ?? 40;
            Gutter = dto.Gutter ?? 10;
            BorderRadius = dto.BorderRadius ?? 10;
            GlobalMaxTokens = dto.GlobalMaxTokens ?? 2048;
            GlobalMaxRounds = dto.GlobalMaxRounds ?? 10;
            ClaudeFallbackMode = dto.ClaudeFallbackMode ?? false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Serializes the complete application state to <c>Settings.json</c> on disk.
    /// Called after every mutation to ensure crash-safe persistence.
    /// </summary>
    private void Save()
    {
        try
        {
            var dto = new PersistedSettings
            {
                ProviderAuth = ProviderAuth.ToDictionary(k => k.Key, v => (string?)v.Value.Json),
                Templates = Templates,
                Conversations = Conversations,
                AppearanceTheme = AppearanceTheme,
                ControlHeight = ControlHeight,
                Gutter = Gutter,
                BorderRadius = BorderRadius,
                GlobalMaxTokens = GlobalMaxTokens,
                GlobalMaxRounds = GlobalMaxRounds,
                ClaudeFallbackMode = ClaudeFallbackMode
            };

            var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            Directory.CreateDirectory(SettingsRoot);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    /// <summary>
    /// Returns the raw authentication JSON for a provider, or <c>"{}"</c> if not configured.
    /// </summary>
    public string GetAuthJson(string providerId)
        => ProviderAuth.TryGetValue(providerId, out var cfg) ? cfg.Json : "{}";

    /// <summary>
    /// Updates the authentication JSON for a provider and persists it to both the local
    /// settings file and the shared <c>%APPDATA%\MindAttic\LLM\providers.json</c> store so
    /// every MindAttic app picks up the change.
    /// </summary>
    public void SetAuthJson(string providerId, string json)
    {
        ProviderAuth[providerId] = new ProviderAuthConfig(providerId, json);
        MindAtticCredentialStore.SaveRaw(providerId, json);
        Save();
    }

    /// <summary>Sets the active appearance theme and persists to disk. Defaults to "dark" if blank.</summary>
    public void SetAppearanceTheme(string theme)
    {
        AppearanceTheme = string.IsNullOrWhiteSpace(theme) ? "dark" : theme;
        Save();
    }

    /// <summary>Sets the UI control height in pixels and persists to disk.</summary>
    public void SetControlHeight(int height)
    {
        ControlHeight = height;
        Save();
    }

    /// <summary>Sets the gutter spacing in pixels and persists to disk.</summary>
    public void SetGutter(int px)
    {
        Gutter = px;
        Save();
    }

    /// <summary>Sets the border radius in pixels and persists to disk.</summary>
    public void SetBorderRadius(int px)
    {
        BorderRadius = px;
        Save();
    }

    /// <summary>Sets the global default max tokens and persists to disk.</summary>
    public void SetGlobalMaxTokens(int tokens)
    {
        GlobalMaxTokens = tokens;
        Save();
    }

    /// <summary>Sets the global default max rounds and persists to disk.</summary>
    public void SetGlobalMaxRounds(int rounds)
    {
        GlobalMaxRounds = rounds;
        Save();
    }

    /// <summary>
    /// Toggles Claude fallback mode and persists to disk. When enabled, every non-Claude
    /// participant routes through the Anthropic API while keeping its unique persona prompt.
    /// </summary>
    public void SetClaudeFallbackMode(bool enabled)
    {
        ClaudeFallbackMode = enabled;
        Save();
    }

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
            Templates.Add(new ParticipantTemplate(
                TemplateId: ChatConversationsService.NewId(),
                ProviderId: v.ProviderId,
                DisplayName: v.Name,
                PersonalityMarkdown: v.PersonalityMarkdown,
                AuthOverrideJson: null,
                IsDefault: false));
        }
        Save();
    }

    /// <summary>
    /// Resolves the API key for a provider. Precedence: explicit per-call override,
    /// then the on-disk auth JSON's <c>apiKey</c> field (if non-empty), then any
    /// runtime override resolved from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// via <see cref="RuntimeApiKeyOverrides"/>. The runtime fallback lets cloud-deployed
    /// instances run without any apiKey on disk while still allowing a developer's local
    /// key to win when explicitly set.
    /// </summary>
    public string GetKeyForProvider(string providerId, string? apiKeyOverride)
    {
        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
            return apiKeyOverride;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(GetAuthJson(providerId));
            if (doc.RootElement.TryGetProperty("apiKey", out var apiKey))
            {
                var diskKey = apiKey.GetString();
                if (!string.IsNullOrWhiteSpace(diskKey))
                    return diskKey;
            }
        }
        catch { }

        if (RuntimeApiKeyOverrides.TryGetValue(providerId, out var runtimeKey)
            && !string.IsNullOrWhiteSpace(runtimeKey))
            return runtimeKey;

        return "";
    }

    /// <summary>
    /// Updates just the API key for a provider while preserving its existing type, model, and maxTokens settings.
    /// </summary>
    public void SetKey(string providerId, string apiKey)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(GetAuthJson(providerId));
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            var model = doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : null;
            var maxTokens = doc.RootElement.TryGetProperty("maxTokens", out var mt) && mt.ValueKind == System.Text.Json.JsonValueKind.Number ? mt.GetInt32() : (int?)null;
            if (string.IsNullOrWhiteSpace(type))
                type = providerId is "claude" ? "anthropic" : providerId is "gemini" ? "google" : "bearer";

            var maxTokensPart = maxTokens.HasValue ? $",\n  \"maxTokens\": {maxTokens.Value}" : "";
            if (string.IsNullOrWhiteSpace(model))
                SetAuthJson(providerId, $"{{\n  \"type\": \"{type}\",\n  \"apiKey\": \"{apiKey}\"{maxTokensPart}\n}}");
            else
                SetAuthJson(providerId, $"{{\n  \"type\": \"{type}\",\n  \"apiKey\": \"{apiKey}\",\n  \"model\": \"{model}\"{maxTokensPart}\n}}");
        }
        catch
        {
            SetAuthJson(providerId, $"{{\n  \"type\": \"{providerId}\",\n  \"apiKey\": \"{apiKey}\"\n}}");
        }
    }

    /// <summary>
    /// Resets all provider auth configs to the values in <see cref="ProviderDefaults"/>.
    /// Providers without a default entry are left unchanged.
    /// </summary>
    public void ResetProvidersToDefaults()
    {
        if (ProviderDefaults.Count == 0)
            return;

        foreach (var (providerId, cfg) in ProviderDefaults)
        {
            ProviderAuth[providerId] = cfg;
            MindAtticCredentialStore.SaveRaw(providerId, cfg.Json);
        }

        Save();
    }

    /// <summary>Internal DTO that mirrors the on-disk Settings.json structure for serialization.</summary>
    private sealed class PersistedSettings
    {
        public Dictionary<string, string?> ProviderAuth { get; set; } = new();
        public List<ParticipantTemplate>? Templates { get; set; }
        public List<PersistedConversation>? Conversations { get; set; }
        public string? AppearanceTheme { get; set; }
        public int? ControlHeight { get; set; }
        public int? Gutter { get; set; }
        public int? BorderRadius { get; set; }
        public int? GlobalMaxTokens { get; set; }
        public int? GlobalMaxRounds { get; set; }
        public bool? ClaudeFallbackMode { get; set; }
    }
}
