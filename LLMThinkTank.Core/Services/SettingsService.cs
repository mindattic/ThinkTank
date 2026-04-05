using LLMThinkTank.Core.Models;

namespace LLMThinkTank.Core.Services;

/// <summary>
/// Concrete settings service registered in the DI container for production use.
/// Inherits all persistence and configuration logic from <see cref="LlmThinkTankSettingsService"/>.
/// </summary>
public class SettingsService : LlmThinkTankSettingsService
{
}

/// <summary>
/// Manages all persistent application state: provider authentication credentials, participant
/// templates, conversation history, and appearance settings. Data is stored as a single
/// <c>Settings.json</c> file in the user's <c>LocalApplicationData/MindAttic/LLMThinkTank</c> folder.
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
public class LlmThinkTankSettingsService
{
    /// <summary>Per-provider authentication and model configuration, keyed by provider ID.</summary>
    public Dictionary<string, ProviderAuthConfig> ProviderAuth { get; } = new();

    /// <summary>
    /// Default provider auth configs (keyed by provider ID) loaded from an external source
    /// such as User Secrets. Used by <see cref="ResetProvidersToDefaults"/> to restore credentials.
    /// </summary>
    public Dictionary<string, ProviderAuthConfig> ProviderDefaults { get; } = new();

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
    /// Initializes the settings service by loading from disk or creating default configuration.
    /// </summary>
    public LlmThinkTankSettingsService()
    {
        LoadOrInit();
    }

    private static string SettingsRoot
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "LLMThinkTank");

    private static string PersonalitiesRoot
        => Path.Combine(SettingsRoot, "Personalities");

    private static string SettingsPath
        => Path.Combine(SettingsRoot, SettingsFileName);

    /// <summary>
    /// Loads settings from disk if available, otherwise initializes with defaults for all providers.
    /// After loading, ensures any newly added providers are backfilled into the configuration.
    /// </summary>
    private void LoadOrInit()
    {
        if (TryLoad())
        {
            EnsureDefaultsIfMissing();
            EnsurePersonalityFiles();
            return;
        }

        ProviderAuth["openai"] = new ProviderAuthConfig("openai", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"gpt-4.1-mini\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["claude"] = new ProviderAuthConfig("claude", "{\n  \"type\": \"anthropic\",\n  \"apiKey\": \"\",\n  \"model\": \"claude-sonnet-4-6\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["gemini"] = new ProviderAuthConfig("gemini", "{\n  \"type\": \"google\",\n  \"apiKey\": \"\",\n  \"model\": \"gemini-2.5-flash\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["deepseek"] = new ProviderAuthConfig("deepseek", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"deepseek-chat\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["mistral"] = new ProviderAuthConfig("mistral", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"mistral-small-latest\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["xai"] = new ProviderAuthConfig("xai", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"grok-3-mini-fast\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["groq"] = new ProviderAuthConfig("groq", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"llama-4-scout-17b-16e-instruct\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["together"] = new ProviderAuthConfig("together", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"meta-llama/Llama-4-Scout-17B-16E-Instruct\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["openrouter"] = new ProviderAuthConfig("openrouter", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"meta-llama/llama-3.1-8b-instruct\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["fireworks"] = new ProviderAuthConfig("fireworks", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"accounts/fireworks/models/llama-v3p3-70b-instruct\",\n  \"maxTokens\": 2048\n}");
        ProviderAuth["cohere"] = new ProviderAuthConfig("cohere", "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"command-a-03-2025\",\n  \"maxTokens\": 2048\n}");

        Templates.AddRange(CreateDefaultTemplates());

        AppearanceTheme = "dark";
        EnsurePersonalityFiles();
        Save();
    }

    /// <summary>
    /// Creates the built-in default participant templates (one per supported provider).
    /// Each template uses the provider's standard personality prompt and default model.
    /// </summary>
    private static List<ParticipantTemplate> CreateDefaultTemplates() => new()
    {
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "openai",
            DisplayName: "ChatGPT",
            PersonalityMarkdown: "You are ChatGPT, made by OpenAI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be conversational and curious. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "claude",
            DisplayName: "Claude",
            PersonalityMarkdown: "You are Claude, made by Anthropic. You are in a live roundtable with other AI systems. Read what they said and engage directly. Be thoughtful and honest. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "gemini",
            DisplayName: "Gemini",
            PersonalityMarkdown: "You are Gemini, made by Google. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be analytical and creative. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "deepseek",
            DisplayName: "DeepSeek",
            PersonalityMarkdown: "You are DeepSeek, made by DeepSeek AI. You are in a live roundtable with other AI systems. Read what they said and engage directly. Be precise and insightful. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "mistral",
            DisplayName: "Mistral",
            PersonalityMarkdown: "You are Mistral, made by Mistral AI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be sharp and efficient. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "xai",
            DisplayName: "Grok",
            PersonalityMarkdown: "You are Grok, made by xAI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be witty and bold. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "groq",
            DisplayName: "Groq",
            PersonalityMarkdown: "You are an AI running on Groq's ultra-fast inference engine. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be quick and insightful. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "together",
            DisplayName: "Together",
            PersonalityMarkdown: "You are an AI running on Together AI's platform. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be collaborative and thoughtful. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "openrouter",
            DisplayName: "OpenRouter",
            PersonalityMarkdown: "You are an AI accessed through OpenRouter. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be versatile and engaging. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "fireworks",
            DisplayName: "Fireworks",
            PersonalityMarkdown: "You are an AI running on Fireworks AI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be fast and perceptive. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true),
        new ParticipantTemplate(
            TemplateId: ChatConversationsService.NewId(),
            ProviderId: "cohere",
            DisplayName: "Cohere",
            PersonalityMarkdown: "You are Command, made by Cohere. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be grounded and clear. 2-3 sentences max.",
            AuthOverrideJson: null,
            IsDefault: true)
    };

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

            // Remove providers that are no longer supported
            var supportedProviders = new HashSet<string>(defaults.Select(d => d.ProviderId));
            var removedTemplates = Templates.RemoveAll(t => t.IsDefault && !supportedProviders.Contains(t.ProviderId));
            if (removedTemplates > 0)
                changed = true;

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
                ["deepseek"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"deepseek-chat\",\n  \"maxTokens\": 2048\n}",
                ["mistral"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"mistral-small-latest\",\n  \"maxTokens\": 2048\n}",
                ["xai"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"grok-3-mini-fast\",\n  \"maxTokens\": 2048\n}",
                ["groq"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"llama-4-scout-17b-16e-instruct\",\n  \"maxTokens\": 2048\n}",
                ["together"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"meta-llama/Llama-4-Scout-17B-16E-Instruct\",\n  \"maxTokens\": 2048\n}",
                ["openrouter"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"meta-llama/llama-3.1-8b-instruct\",\n  \"maxTokens\": 2048\n}",
                ["fireworks"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"accounts/fireworks/models/llama-v3p3-70b-instruct\",\n  \"maxTokens\": 2048\n}",
                ["cohere"] = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"\",\n  \"model\": \"command-a-03-2025\",\n  \"maxTokens\": 2048\n}"
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
                    using var doc = System.Text.Json.JsonDocument.Parse(cfg.Json);
                    if (!doc.RootElement.TryGetProperty("maxTokens", out _))
                    {
                        var json = cfg.Json.TrimEnd().TrimEnd('}').TrimEnd();
                        json += ",\n  \"maxTokens\": 2048\n}";
                        ProviderAuth[providerId] = new ProviderAuthConfig(providerId, json);
                        changed = true;
                    }
                }
                catch { }
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

            WriteIfMissing("Mistral.md",
                "# Mistral\n\nYou are Mistral, made by Mistral AI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be sharp and efficient. 2-3 sentences max.\n");

            WriteIfMissing("Grok.md",
                "# Grok\n\nYou are Grok, made by xAI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be witty and bold. 2-3 sentences max.\n");

            WriteIfMissing("Groq.md",
                "# Groq\n\nYou are an AI running on Groq's ultra-fast inference engine. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be quick and insightful. 2-3 sentences max.\n");

            WriteIfMissing("Together.md",
                "# Together\n\nYou are an AI running on Together AI's platform. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be collaborative and thoughtful. 2-3 sentences max.\n");

            WriteIfMissing("OpenRouter.md",
                "# OpenRouter\n\nYou are an AI accessed through OpenRouter. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be versatile and engaging. 2-3 sentences max.\n");

            WriteIfMissing("Fireworks.md",
                "# Fireworks\n\nYou are an AI running on Fireworks AI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be fast and perceptive. 2-3 sentences max.\n");

            WriteIfMissing("Cohere.md",
                "# Cohere\n\nYou are Command, made by Cohere. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be grounded and clear. 2-3 sentences max.\n");
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
                GlobalMaxRounds = GlobalMaxRounds
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
    /// Updates the authentication JSON for a provider and persists to disk.
    /// </summary>
    public void SetAuthJson(string providerId, string json)
    {
        ProviderAuth[providerId] = new ProviderAuthConfig(providerId, json);
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
    /// Resolves the API key for a provider. Returns the override if provided,
    /// otherwise extracts the <c>apiKey</c> field from the provider's auth JSON.
    /// </summary>
    public string GetKeyForProvider(string providerId, string? apiKeyOverride)
    {
        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
            return apiKeyOverride;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(GetAuthJson(providerId));
            if (doc.RootElement.TryGetProperty("apiKey", out var apiKey))
                return apiKey.GetString() ?? "";
        }
        catch { }

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
            ProviderAuth[providerId] = cfg;

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
    }
}
