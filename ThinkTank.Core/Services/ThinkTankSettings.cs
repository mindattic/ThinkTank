using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using ThinkTank.Core.Models;

namespace ThinkTank.Core.Services;

/// <summary>
/// ThinkTank's app-level settings (LAMP, TT-T27), kept in <c>%APPDATA%\MindAttic\ThinkTank\settings.json</c>
/// and edited only in MindAttic.Vault.Desktop. Property initializers are exactly what ThinkTank used before
/// LAMP when nothing was saved. Personas (templates) and conversations are content and stay in
/// <c>%LOCALAPPDATA%\MindAttic\ThinkTank\Settings.json</c>; LLM keys live in Vault's shared named lists.
/// <para>
/// Roaming, not <c>Local</c>: the per-machine folder already holds <c>Settings.json</c>, which on Windows is
/// the same file as <c>settings.json</c>, so the Vault file can't live beside it.
/// </para>
/// </summary>
[SettingsApp(AppId, "ThinkTank", Icon = "chat", Description = "AI roundtable: several LLM personas discuss a topic and vote.")]
[UsesCredential("claude", Purpose = "Claude participants, and every participant in Claude fallback mode")]
[UsesCredential("openai", Purpose = "ChatGPT participants")]
[UsesCredential("gemini", Purpose = "Gemini participants")]
[UsesCredential("deepseek", Purpose = "DeepSeek participants")]
[UsesCredential("kimi", Purpose = "Kimi participants")]
[UsesCredential("grok", Purpose = "Grok participants")]
[UsesCredential("cohere", Purpose = "Cohere participants")]
[UsesCredential("mistral", Purpose = "Mistral participants")]
[UsesCredential("together", Purpose = "Together participants")]
[UsesCredential("fireworks", Purpose = "Fireworks participants")]
public sealed class ThinkTankSettings
{
    /// <summary>Settings app id: ThinkTank's MindAttic folder name.</summary>
    public const string AppId = "ThinkTank";

    // ---- Appearance ---------------------------------------------------------------------

    [Setting("Theme", Group = "Appearance", Order = 1, Help = "Color theme for the whole app.")]
    [SettingChoices("dark", "light", "spring", "summer", "autumn", "winter", "matrix", "ice", "sunset", "neon",
        "dracula", "solarized", "midnight", "aurora", "ember", "ocean", "forest", "mono")]
    public string Theme { get; set; } = "dark";

    [Setting("Control height (px)", Group = "Appearance", Order = 2, Help = "Height of buttons and inputs.")]
    [SettingRange(28, 60)]
    public int ControlHeight { get; set; } = 40;

    [Setting("Gutter (px)", Group = "Appearance", Order = 3, Help = "Spacing between panels and controls.")]
    [SettingRange(0, 30)]
    public int Gutter { get; set; } = 10;

    [Setting("Border radius (px)", Group = "Appearance", Order = 4, Help = "Corner rounding of cards, panels and buttons.")]
    [SettingRange(0, 24)]
    public int BorderRadius { get; set; } = 10;

    // ---- Conversation defaults ---------------------------------------------------------

    [Setting("Max tokens per response", Group = "Conversation defaults", Order = 1,
        Help = "Default token ceiling for each reply. A conversation can override it.")]
    [SettingRange(64, 32768)]
    public int GlobalMaxTokens { get; set; } = 2048;

    [Setting("Max rounds", Group = "Conversation defaults", Order = 2,
        Help = "Default number of rounds per conversation. A conversation can override it.")]
    [SettingRange(1, 999)]
    public int GlobalMaxRounds { get; set; } = 10;

    [Setting("Context turns", Group = "Conversation defaults", Order = 3,
        Help = "How many recent turns each participant sees (at least 2); older turns are dropped from the prompt.")]
    [SettingRange(2, 1000)]
    public int GlobalMaxContextTurns { get; set; } = 8;

    [Setting("Response length", Group = "Conversation defaults", Order = 4,
        Help = "How much each participant writes per turn: concise (~2-3 sentences), balanced (~1 paragraph), detailed (~2-3 paragraphs).")]
    [SettingChoices(ResponseLengthPreset.Concise, ResponseLengthPreset.Balanced, ResponseLengthPreset.Detailed)]
    public string GlobalResponseLength { get; set; } = ResponseLengthPreset.Default;

    [Setting("Claude fallback mode", Group = "Conversation defaults", Order = 5,
        Help = "Route every participant through Claude, which roleplays each persona. Use when other providers are down. Also switchable on the chat page.")]
    public bool ClaudeFallbackMode { get; set; }

    // ---- Providers ----------------------------------------------------------------------

    [Setting("Provider models", Group = "Providers", Order = 1,
        Help = "Model and token cap each provider uses unless a persona overrides them. Keys are on Vault's Keys tab.")]
    public ProviderModels Providers { get; set; } = new();

    // ---- Loading ------------------------------------------------------------------------

    /// <summary>The pre-LAMP file: <c>%LOCALAPPDATA%\MindAttic\ThinkTank\Settings.json</c> (still the content store).</summary>
    public static string LegacySettingsPath => Path.Combine(VaultPaths.LocalApp(AppId), "Settings.json");

    /// <summary>Deployed values (<c>MindAttic:Settings:ThinkTank</c> in configuration), or null.</summary>
    public static ThinkTankSettings? FromConfiguration(IConfiguration? configuration)
    {
        var section = configuration?.GetSection(SettingsManifest.PrefixFor(AppId));
        return section is not null && section.Exists() ? section.Get<ThinkTankSettings>() ?? new ThinkTankSettings() : null;
    }

    /// <summary>
    /// The deployed configuration when present; otherwise the Vault file, importing today's values from
    /// <paramref name="legacyPath"/> (default <see cref="LegacySettingsPath"/>) once when it doesn't exist
    /// yet. The old file is only read here.
    /// </summary>
    public static ThinkTankSettings Load(IConfiguration? configuration = null, ISettingsStore<ThinkTankSettings>? store = null,
        string? legacyPath = null)
    {
        if (FromConfiguration(configuration) is { } deployed) return deployed;
        store ??= JsonSettingsStore<ThinkTankSettings>.ForSettingsApp();
        if (!store.Exists() && ImportLegacy(legacyPath ?? LegacySettingsPath) is { } imported)
            store.Save(imported);
        return store.LoadOrSeed();
    }

    /// <summary>
    /// Today's values from the pre-LAMP Settings.json, read exactly as ThinkTank's loader did (blank theme =
    /// dark, missing numbers = their defaults, context turns floored at 2, response length normalized, and a
    /// provider entry or field the file lacks gets the value ThinkTank backfilled on load). Null when the file
    /// doesn't exist or can't be read.
    /// </summary>
    public static ThinkTankSettings? ImportLegacy(string legacyPath)
    {
        if (!File.Exists(legacyPath)) return null;
        JsonObject root;
        try { root = JsonNode.Parse(File.ReadAllText(legacyPath))?.AsObject() ?? throw new JsonException(); }
        catch { return null; }

        int? Int(string name) => root[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
        string? Text(string name) => root[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        var s = new ThinkTankSettings
        {
            Theme = string.IsNullOrWhiteSpace(Text("AppearanceTheme")) ? "dark" : Text("AppearanceTheme")!,
            ControlHeight = Int("ControlHeight") ?? 40,
            Gutter = Int("Gutter") ?? 10,
            BorderRadius = Int("BorderRadius") ?? 10,
            GlobalMaxTokens = Int("GlobalMaxTokens") ?? 2048,
            GlobalMaxRounds = Int("GlobalMaxRounds") ?? 10,
            GlobalMaxContextTurns = Math.Max(2, Int("GlobalMaxContextTurns") ?? 8),
            GlobalResponseLength = ResponseLengthPreset.Normalize(Text("GlobalResponseLength")),
            ClaudeFallbackMode = root["ClaudeFallbackMode"] is JsonValue b && b.TryGetValue<bool>(out var on) && on,
            Providers = new ProviderModels(),
        };

        var auth = root["ProviderAuth"] as JsonObject;
        foreach (var id in ProviderModels.Ids)
        {
            // What ThinkTank's EnsureDefaultsIfMissing backfilled for an entry or field the file lacked.
            var backfill = ProviderModels.Backfill(id);
            var target = s.Providers.For(id)!;
            target.Model = backfill.Model;
            target.MaxTokens = backfill.MaxTokens;
            var entry = auth is null ? null
                : auth.FirstOrDefault(kv => string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase)).Value;
            if (entry is not JsonValue raw || !raw.TryGetValue<string>(out var json)) continue;
            try
            {
                var o = JsonNode.Parse(json)?.AsObject();
                if (o is null) continue;
                if (o.ContainsKey("model")) target.Model = o["model"] is JsonValue m && m.TryGetValue<string>(out var model) ? model : "";
                if (o.ContainsKey("maxTokens") && o["maxTokens"] is JsonValue t && t.TryGetValue<int>(out var max)) target.MaxTokens = max;
            }
            catch
            {
                // A malformed entry was replaced by the backfill defaults on load; same here.
            }
        }
        return s;
    }
}

/// <summary>One provider's model and token cap.</summary>
public sealed class ProviderModelSettings
{
    [Setting("Model", Help = "Model id sent to the provider. Empty uses the provider's catalog default.")]
    public string Model { get; set; } = "";

    [Setting("Max tokens", Help = "Token cap for this provider's replies unless a conversation overrides it.")]
    [SettingRange(1, 1_000_000)]
    public int MaxTokens { get; set; } = 2048;
}

/// <summary>The providers ThinkTank keeps a model setting for (the ones its Settings.json carried).</summary>
public sealed class ProviderModels
{
    /// <summary>Provider ids, in the order ThinkTank stored them.</summary>
    public static readonly string[] Ids = ["openai", "claude", "gemini", "deepseek", "kimi"];

    [Setting("ChatGPT (OpenAI)", Order = 1, Help = "Model and token cap for OpenAI participants.")]
    public ProviderModelSettings OpenAI { get; set; } = new() { Model = "gpt-4.1-mini" };

    [Setting("Claude (Anthropic)", Order = 2, Help = "Model and token cap for Claude participants.")]
    public ProviderModelSettings Claude { get; set; } = new() { Model = "claude-sonnet-4-6" };

    [Setting("Gemini (Google)", Order = 3, Help = "Model and token cap for Gemini participants.")]
    public ProviderModelSettings Gemini { get; set; } = new() { Model = "gemini-2.5-flash" };

    [Setting("DeepSeek", Order = 4, Help = "Model and token cap for DeepSeek participants.")]
    public ProviderModelSettings DeepSeek { get; set; } = new() { Model = "deepseek-chat" };

    [Setting("Kimi (Moonshot)", Order = 5, Help = "Model and token cap for Kimi participants.")]
    public ProviderModelSettings Kimi { get; set; } = new() { Model = "kimi-k2" };

    /// <summary>The settings for <paramref name="providerId"/>, or null for a provider ThinkTank keeps none for.</summary>
    public ProviderModelSettings? For(string providerId) => providerId.ToLowerInvariant() switch
    {
        "openai" => OpenAI,
        "claude" => Claude,
        "gemini" => Gemini,
        "deepseek" => DeepSeek,
        "kimi" => Kimi,
        _ => null,
    };

    /// <summary>The value ThinkTank's loader backfilled when Settings.json lacked a provider or one of its fields.</summary>
    internal static ProviderModelSettings Backfill(string providerId) => providerId switch
    {
        "openai" => new() { Model = "gpt-5.4-mini" },
        "claude" => new() { Model = "claude-sonnet-5" },
        "gemini" => new() { Model = "gemini-3.5-flash" },
        "deepseek" => new() { Model = "deepseek-v4-flash" },
        _ => new() { Model = "kimi-k2" },
    };

    /// <summary>The wire auth type each provider's JSON carried.</summary>
    public static string TypeFor(string providerId) => providerId.ToLowerInvariant() switch
    {
        "claude" => "anthropic",
        "gemini" => "google",
        _ => "bearer",
    };
}

/// <summary>Where ThinkTank reads its settings at run time (re-read on each access, so Vault edits apply live).</summary>
public sealed class ThinkTankSettingsSource
{
    private readonly ISettingsStore<ThinkTankSettings> store;
    private readonly IConfiguration? configuration;

    public ThinkTankSettingsSource(ISettingsStore<ThinkTankSettings> store, IConfiguration? configuration = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.configuration = configuration;
    }

    public ThinkTankSettings Current => ThinkTankSettings.FromConfiguration(configuration) ?? store.Load();

    public ISettingsStore<ThinkTankSettings> Store => store;

    /// <summary>
    /// Writes a run control that lives on the chat page (Claude fallback mode) through the store; ignored
    /// when the values come from deployed configuration (read-only).
    /// </summary>
    public void Update(Action<ThinkTankSettings> change)
    {
        if (ThinkTankSettings.FromConfiguration(configuration) is not null) return;
        store.Update(change);
    }

    /// <summary>What hosts call at startup: one-time import, seed, manifest.</summary>
    public static ThinkTankSettingsSource Open(IConfiguration? configuration = null, string? legacyPath = null)
    {
        var store = JsonSettingsStore<ThinkTankSettings>.ForSettingsApp();
        ThinkTankSettings.Load(configuration, store, legacyPath);
        return new ThinkTankSettingsSource(store, configuration);
    }
}

/// <summary>
/// How ThinkTank's LLM calls get keys (LAMP, TT-T27): Vault's shared named lists through
/// <see cref="KeySelector"/>, failing over when a key is refused and flagging it in Vault.Desktop.
/// </summary>
public static class ThinkTankKeys
{
    /// <summary>The local key files and health (tests, and hosts without configuration).</summary>
    public static KeySelector ForCurrentUser() => KeySelector.ForCurrentUser(ClassifyLegionFailure);

    /// <summary>A 401/403/402/429 anywhere in Legion's exception chain means the key was refused.</summary>
    public static KeyFailureException? ClassifyLegionFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is HttpRequestException { StatusCode: { } status } && KeySelector.Classify(status) is { } kind)
                return new KeyFailureException(kind, $"HTTP {(int)status}");
        return null;
    }
}

/// <summary>ThinkTank's Settings cog: opens MindAttic.Vault.Desktop at the ThinkTank tab (LAMP, TT-T27).</summary>
public static class SettingsCog
{
    public sealed record Result(bool Opened, string Message);

    public static Result Open(string? setting = null) =>
        MindAttic.Vault.Launch.VaultSettings.Open(ThinkTankSettings.AppId, setting)
            ? new Result(true, "Opened ThinkTank's settings in MindAttic Vault.")
            : new Result(false, "MindAttic Vault isn't installed or couldn't start, so settings can't be opened here. Install " +
                                "MindAttic.Vault.Desktop (or set MINDATTIC_VAULT_DESKTOP_EXE), or use the Vault link.");

    /// <summary>The <c>mindattic-vault://</c> fallback link for a browser on another machine.</summary>
    public static string Link(string? setting = null) => MindAttic.Vault.Launch.VaultSettings.Link(ThinkTankSettings.AppId, setting);
}
