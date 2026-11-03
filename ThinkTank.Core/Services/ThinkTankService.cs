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
    private readonly PsychometricProfileService psychometrics;

    public ThinkTankService(LegionClient legion, ThinkTankSettingsService settings, PsychometricProfileService psychometrics)
    {
        this.legion = legion;
        this.settings = settings;
        this.psychometrics = psychometrics;
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

    /// <summary>
    /// The same diagnostics, tagged with the chat (and the caller that opened the scope) the
    /// call was made for, via <see cref="BeginDiagnosticsScope"/>. This service is a singleton
    /// shared by every Blazor circuit, so a page subscribes here and keeps only the events
    /// for its own calls; <see cref="Diagnostics"/> carries no chat id, so a subscriber can't
    /// tell which tab (or browser tab) a call belonged to. Untagged calls have a null ChatId.
    /// </summary>
    public event Action<DiagnosticsEvent>? DiagnosticsRaised;

    private static readonly AsyncLocal<DiagnosticsScope?> currentScope = new();

    /// <summary>
    /// Tags every diagnostic raised by calls made inside the returned scope (on this async
    /// flow) with <paramref name="chatId"/> and <paramref name="origin"/>. Dispose to end it.
    /// </summary>
    public static IDisposable BeginDiagnosticsScope(string chatId, object? origin = null)
    {
        var previous = currentScope.Value;
        var scope = new DiagnosticsScope(chatId, origin, previous);
        currentScope.Value = scope;
        return scope;
    }

    private sealed class DiagnosticsScope(string chatId, object? origin, DiagnosticsScope? previous) : IDisposable
    {
        public string ChatId { get; } = chatId;
        public object? Origin { get; } = origin;
        public void Dispose()
        {
            if (ReferenceEquals(currentScope.Value, this)) currentScope.Value = previous;
        }
    }

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
            var scope = currentScope.Value;
            DiagnosticsRaised?.Invoke(new DiagnosticsEvent(scope?.ChatId, scope?.Origin, providerId, payload, isError));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ThinkTank.Diagnostics] emit failed for '{providerId}': {ex.Message}");
        }
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
        int? maxTokensOverride = null,
        string? speakerId = null,
        string? responseLength = null,
        string? personaId = null)
    {
        // Append the persona's psychometric brief (OCEAN/HEXACO/MBTI/Enneagram/DISC) when one
        // has been scored for this persona, so the model embodies the measured traits. No-op
        // when personaId is null (auxiliary callers) or the persona is unscored.
        var persona = personalityMarkdown + psychometrics.DescribeForPrompt(personaId);

        // Claude fallback: route everyone through Anthropic with the original
        // provider's persona wrapped in a roleplay frame.
        var actualProviderId = providerId;
        var actualPersona    = persona;
        var actualAuthOverride = authOverrideJson;
        if (settings.ClaudeFallbackMode && providerId != "claude")
        {
            actualProviderId   = "claude";
            actualPersona      = WrapPersonaForClaudeFallback(providerId, persona);
            actualAuthOverride = null;  // per-template override points at non-Claude key
        }

        var providerInfo = LlmProviderCatalog.Get(actualProviderId);
        if (providerInfo is null)
            throw new ArgumentException($"Unknown provider: {actualProviderId}");

        // A persona's own auth override key is used as-is; otherwise ThinkTank's keys from Vault's shared
        // named lists, failing over when one is refused (LAMP, TT-T27).
        var overrideKey = GetOverrideApiKey(actualProviderId, actualAuthOverride);
        var apiKey = overrideKey ?? settings.GetKeyForProvider(actualProviderId, null);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key configured for provider '{actualProviderId}'. Add one in MindAttic Vault.");

        var defaultModel = providerInfo.DefaultModel ?? "";
        var model        = GetModel(actualProviderId, actualAuthOverride, defaultModel);
        var maxTokens    = GetMaxTokens(actualProviderId, actualAuthOverride, maxTokensOverride);

        var maxContextTurns = ResolveMaxContextTurns();
        if (history.Count > maxContextTurns)
            EmitDiagnostics(providerId, model, $"context truncated: {history.Count} turns → {maxContextTurns}", isError: false);

        // Self-recognition matches the speaker's roundtable identity against SharedTurn.ModelId.
        // History turns are keyed by ParticipantId, so prefer the caller-supplied speakerId;
        // fall back to providerId for legacy single-participant-per-provider callers.
        var (systemPrompt, turns) = BuildPrompt(actualProviderId, speakerId ?? providerId, actualPersona, topic, history, maxContextTurns);

        // Roundtable callers pass a response-length preset so participants give brief,
        // complete answers instead of rambling up to the token ceiling. Auxiliary callers
        // (name/topic/personality generation) leave it null and are not constrained.
        if (!string.IsNullOrWhiteSpace(responseLength))
            systemPrompt += ResponseLengthPreset.InstructionFor(responseLength);

        try
        {
            Task<string> Call(string key, CancellationToken ct) => legion.CallChatAsync(
                providerId: actualProviderId,
                apiKey: key,
                model: model,
                messages: turns,
                systemPrompt: systemPrompt,
                maxTokens: maxTokens,
                temperature: 0.7,
                ct: ct);

            var raw = overrideKey is not null
                ? await Call(overrideKey, default)
                : await settings.ExecuteWithKeyAsync(actualProviderId, Call);

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

    /// <summary>The <c>apiKey</c> a persona's auth override carries, or null when it carries none.</summary>
    private string? GetOverrideApiKey(string providerId, string? authOverrideJson)
    {
        if (!string.IsNullOrWhiteSpace(authOverrideJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(authOverrideJson);
                if (doc.RootElement.TryGetProperty("apiKey", out var apiKey))
                    return apiKey.GetString() ?? "";
            }
            catch (Exception ex)
            {
                // Malformed per-template override silently falling back to the global key
                // used to hide real misconfig — emit a diagnostic so the user sees it.
                EmitDiagnostics(providerId, model: "", text: $"malformed auth override (apiKey lookup): {ex.Message}", isError: true, errorMessage: ex.Message);
            }
        }
        return null;
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
            catch (Exception ex)
            {
                EmitDiagnostics(providerId, model: "", text: $"malformed auth override (model lookup): {ex.Message}", isError: true, errorMessage: ex.Message);
            }
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
        catch (Exception ex)
        {
            EmitDiagnostics(providerId, model: "", text: $"malformed disk auth (model lookup): {ex.Message}", isError: true, errorMessage: ex.Message);
        }

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
            catch (Exception ex)
            {
                EmitDiagnostics(providerId, model: "", text: $"malformed auth override (maxTokens lookup): {ex.Message}", isError: true, errorMessage: ex.Message);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(settings.GetAuthJson(providerId));
            if (doc.RootElement.TryGetProperty("maxTokens", out var mt) && mt.ValueKind == JsonValueKind.Number)
                return mt.GetInt32();
        }
        catch (Exception ex)
        {
            EmitDiagnostics(providerId, model: "", text: $"malformed disk auth (maxTokens lookup): {ex.Message}", isError: true, errorMessage: ex.Message);
        }

        return defaultMaxTokens;
    }

    // ── History → ChatTurn[] conversion ─────────────────────────────────────

    private const int DefaultMaxContextTurns = 8;

    private int ResolveMaxContextTurns()
    {
        var configured = settings.GlobalMaxContextTurns ?? DefaultMaxContextTurns;
        return configured < 2 ? 2 : configured;
    }

    private static List<SharedTurn> TrimHistory(List<SharedTurn> history, int maxContextTurns)
        => history.Count <= maxContextTurns
            ? history
            : history.Skip(history.Count - maxContextTurns).ToList();

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
        List<SharedTurn> history,
        int maxContextTurns)
    {
        var personality = string.IsNullOrWhiteSpace(personalityMarkdown)
            ? DefaultRoundtableFraming
            : personalityMarkdown;
        var systemPrompt = $"{personality}\n\nTopic: \"{topic}\"";
        var recent = TrimHistory(history, maxContextTurns);
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

/// <summary>
/// One diagnostics snapshot from <see cref="ThinkTankService.DiagnosticsRaised"/>: the chat and
/// caller it belongs to (null when the call was made outside a diagnostics scope), the provider,
/// the JSON payload, and the error flag.
/// </summary>
public sealed record DiagnosticsEvent(string? ChatId, object? Origin, string ProviderId, string Payload, bool IsError);
