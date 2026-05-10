using System.Text.Json;
using MindAttic.Legion;
using ThinkTank.Core.Models;

namespace ThinkTank.Core.Services;

/// <summary>
/// Roundtable orchestration service. Translates a shared <see cref="SharedTurn"/>
/// conversation history into per-participant prompts and delegates the actual
/// LLM call to MindAttic.Legion's <see cref="LegionClient"/> — Legion owns
/// every wire-level concern (endpoints, auth headers, payload shapes per
/// provider, response parsing, retries with backoff, circuit breaker).
///
/// What stays here:
///   - Provider catalog overlay (display name, avatar, roundtable persona)
///   - History → multi-turn ChatTurn[] conversion with speaker tagging
///   - ClaudeFallbackMode (route everyone through Claude with a roleplay wrapper)
///   - Per-call max-tokens override and per-template auth override
///   - Output sanitization (strip self-referencing prefixes like "[Claude]:")
///   - Diagnostics event for the live API monitor
/// </summary>
public class ThinkTankService
{
    private readonly LegionClient legion;
    private readonly ThinkTankSettingsService settings;

    public ThinkTankService(LegionClient legion, ThinkTankSettingsService settings)
    {
        this.legion = legion;
        this.settings = settings;
    }

    /// <summary>
    /// ThinkTank-specific roundtable decorations for the providers Legion knows about.
    /// </summary>
    private static readonly Dictionary<string, (string Avatar, string Personality)> RoundtableDecorations = new()
    {
        ["openai"]     = ("⬡", "You are ChatGPT, made by OpenAI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be conversational and curious. 2-3 sentences max."),
        ["claude"]     = ("◈", "You are Claude, made by Anthropic. You are in a live roundtable with other AI systems. Read what they said and engage directly. Be thoughtful and honest. 2-3 sentences max."),
        ["gemini"]     = ("✦", "You are Gemini, made by Google. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be analytical and creative. 2-3 sentences max."),
        ["deepseek"]   = ("◉", "You are DeepSeek, made by DeepSeek AI. You are in a live roundtable with other AI systems. Read what they said and engage directly. Be precise and insightful. 2-3 sentences max."),
    };

    /// <summary>
    /// Registry of all supported LLM providers, sourced from Legion's catalog
    /// (display names + key URLs) with ThinkTank's avatar + roundtable persona overlaid.
    /// </summary>
    public List<LlmModel> Models { get; } = LlmProviderCatalog.Default
        .Where(p => RoundtableDecorations.ContainsKey(p.Id))
        .Select(p =>
        {
            var (avatar, personality) = RoundtableDecorations[p.Id];
            return new LlmModel
            {
                Id          = p.Id,
                Name        = p.DisplayName,
                Avatar      = avatar,
                ApiKeyUrl   = p.KeysUrl,
                Personality = personality,
            };
        })
        .ToList();


    /// <summary>
    /// Raised after every API call with the provider ID, a small synthesized
    /// JSON snapshot, and an error flag. Subscribers (e.g., the diagnostics
    /// panel) use this for real-time API monitoring.
    /// </summary>
    public event Action<string, string, bool>? Diagnostics;

    private void EmitDiagnostics(string providerId, string model, string text, bool isError, string? errorMessage = null)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                provider = providerId,
                model,
                isError,
                text = string.IsNullOrEmpty(text) ? null : (text.Length > 200 ? text[..200] + "…" : text),
                error = errorMessage,
            }, new JsonSerializerOptions { WriteIndented = true });
            Diagnostics?.Invoke(providerId, payload, isError);
        }
        catch { }
    }

    // ── Main dispatch ────────────────────────────────────────────────────────

    /// <summary>Sends a chat completion request to the specified model using its default personality and auth.</summary>
    public Task<string> CallModel(LlmModel model, string topic, List<SharedTurn> history)
        => CallProvider(model.Id, model.Personality, authOverrideJson: null, topic, history);

    /// <summary>
    /// Dispatches a chat completion request to any supported provider by ID, via
    /// MindAttic.Legion. Honours <see cref="ThinkTankSettingsService.ClaudeFallbackMode"/>
    /// (everyone routes through Claude with a roleplay wrapper) and per-template
    /// auth overrides.
    /// </summary>
    public async Task<string> CallProvider(
        string providerId,
        string personalityMarkdown,
        string? authOverrideJson,
        string topic,
        List<SharedTurn> history,
        int? maxTokensOverride = null)
    {
        // Claude fallback: route everyone through Anthropic with the original
        // provider's persona wrapped in a roleplay frame.
        var actualProviderId = providerId;
        var actualPersona    = personalityMarkdown;
        var actualAuthOverride = authOverrideJson;
        if (settings.ClaudeFallbackMode && providerId != "claude")
        {
            actualProviderId   = "claude";
            actualPersona      = WrapPersonaForClaudeFallback(providerId, personalityMarkdown);
            actualAuthOverride = null;  // per-template override points at non-Claude key
        }

        var providerInfo = LlmProviderCatalog.Get(actualProviderId);
        if (providerInfo is null)
            throw new ArgumentException($"Unknown provider: {actualProviderId}");

        var apiKey = GetApiKey(actualProviderId, actualAuthOverride);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key configured for provider '{actualProviderId}'.");

        var defaultModel = providerInfo.DefaultModel ?? "";
        var model        = GetModel(actualProviderId, actualAuthOverride, defaultModel);
        var maxTokens    = GetMaxTokens(actualProviderId, actualAuthOverride, maxTokensOverride);

        var (systemPrompt, turns) = BuildPrompt(actualProviderId, providerId, actualPersona, topic, history);

        try
        {
            var raw = await legion.CallChatAsync(
                providerId: actualProviderId,
                apiKey: apiKey,
                model: model,
                messages: turns,
                systemPrompt: systemPrompt,
                maxTokens: maxTokens,
                temperature: 0.7,
                ct: default);

            var clean = SanitizeModelOutput(providerId, raw);
            EmitDiagnostics(providerId, model, clean, isError: false);
            return clean;
        }
        catch (Exception ex)
        {
            EmitDiagnostics(providerId, model, "", isError: true, errorMessage: ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Wraps a participant's existing persona prompt with explicit roleplay instructions so
    /// Claude voices each participant as a clearly distinct character when fallback mode is on.
    /// </summary>
    public static string WrapPersonaForClaudeFallback(string originalProviderId, string personalityMarkdown)
    {
        var providerLabel = originalProviderId switch
        {
            "openai"     => "ChatGPT (OpenAI)",
            "gemini"     => "Gemini (Google)",
            "deepseek"   => "DeepSeek",
            _            => originalProviderId
        };

        return
            $"You are roleplaying as {providerLabel} for an AI roundtable. Stay fully in character for the entire conversation.\n" +
            "Adopt a distinct voice, vocabulary, sentence rhythm, and intellectual reflexes that match the persona below. " +
            "Do not mention Anthropic or that you are Claude. Do not break character. Do not preface responses with your name.\n\n" +
            "── Persona ──\n" +
            personalityMarkdown;
    }

    // ── Auth / model / token resolution ─────────────────────────────────────

    private string GetApiKey(string providerId, string? authOverrideJson)
    {
        if (!string.IsNullOrWhiteSpace(authOverrideJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(authOverrideJson);
                if (doc.RootElement.TryGetProperty("apiKey", out var apiKey))
                    return apiKey.GetString() ?? "";
            }
            catch { }
        }
        return settings.GetKeyForProvider(providerId, null);
    }

    private string GetModel(string providerId, string? authOverrideJson, string defaultModel)
    {
        if (!string.IsNullOrWhiteSpace(authOverrideJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(authOverrideJson);
                if (doc.RootElement.TryGetProperty("model", out var model))
                {
                    var v = model.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { }
        }

        try
        {
            using var doc = JsonDocument.Parse(settings.GetAuthJson(providerId));
            if (doc.RootElement.TryGetProperty("model", out var model))
            {
                var v = model.GetString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { }

        return defaultModel;
    }

    private int GetMaxTokens(string providerId, string? authOverrideJson, int? maxTokensOverride = null, int defaultMaxTokens = 2048)
    {
        if (maxTokensOverride.HasValue)
            return maxTokensOverride.Value;

        if (!string.IsNullOrWhiteSpace(authOverrideJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(authOverrideJson);
                if (doc.RootElement.TryGetProperty("maxTokens", out var mt) && mt.ValueKind == JsonValueKind.Number)
                    return mt.GetInt32();
            }
            catch { }
        }

        try
        {
            using var doc = JsonDocument.Parse(settings.GetAuthJson(providerId));
            if (doc.RootElement.TryGetProperty("maxTokens", out var mt) && mt.ValueKind == JsonValueKind.Number)
                return mt.GetInt32();
        }
        catch { }

        return defaultMaxTokens;
    }

    // ── History → ChatTurn[] conversion ─────────────────────────────────────

    private const int MaxContextTurns = 8;

    private static List<SharedTurn> TrimHistory(List<SharedTurn> history)
        => history.Count <= MaxContextTurns
            ? history
            : history.Skip(history.Count - MaxContextTurns).ToList();

    /// <summary>
    /// Builds the system prompt + multi-turn history for a participant. The
    /// "calling participant" (identified by <paramref name="speakerProviderId"/>) sees
    /// its own prior turns as <c>assistant</c> and everyone else's turns as
    /// <c>user</c> messages tagged with the speaker's display name.
    /// </summary>
    private const string DefaultRoundtableFraming =
        "You are in a live roundtable with other AI systems. Read what they said and respond directly. Be conversational. 2-3 sentences max.";

    private static (string systemPrompt, IReadOnlyList<ChatTurn> turns) BuildPrompt(
        string actualProviderId,
        string speakerProviderId,
        string personalityMarkdown,
        string topic,
        List<SharedTurn> history)
    {
        var personality = string.IsNullOrWhiteSpace(personalityMarkdown)
            ? DefaultRoundtableFraming
            : personalityMarkdown;
        var systemPrompt = $"{personality}\n\nTopic: \"{topic}\"";
        var recent = TrimHistory(history);
        var turns = new List<ChatTurn>();

        if (recent.Count == 0)
        {
            turns.Add(new ChatTurn("user", $"The topic is: \"{topic}\". Please give your opening thoughts."));
            return (systemPrompt, turns);
        }

        foreach (var turn in recent)
        {
            // Speaker self-recognition uses the *original* speaker id (the participant's
            // identity in the roundtable), not the wire provider id (which may be Claude
            // when ClaudeFallbackMode is on).
            if (turn.ModelId == speakerProviderId)
                turns.Add(new ChatTurn("assistant", turn.Text));
            else
                turns.Add(new ChatTurn("user", $"[{turn.ModelName}]: {turn.Text}"));
        }

        // If the last turn was the speaker's own, nudge them to continue.
        if (recent.Last().ModelId == speakerProviderId)
            turns.Add(new ChatTurn("user", "Please continue the discussion."));

        return (systemPrompt, turns);
    }

    // ── Output sanitization ─────────────────────────────────────────────────

    /// <summary>
    /// Strips self-referencing prefixes that models sometimes prepend to their responses
    /// (e.g., "[ChatGPT]:", "Claude:", "[Assistant]:"). Keeps the displayed output clean
    /// since participant identity is already shown in the chat bubble header.
    /// </summary>
    private static string SanitizeModelOutput(string providerId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        text = text.TrimStart();
        var pattern = "^(?:\\s*(?:\\[(?:chatgpt|gpt|assistant|openai|claude|gemini|deepseek)\\]\\s*:|(?:chatgpt|gpt|assistant|openai|claude|gemini|deepseek)\\s*:))+\\s*";
        text = System.Text.RegularExpressions.Regex.Replace(
            text, pattern, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text.TrimStart();
    }
}
