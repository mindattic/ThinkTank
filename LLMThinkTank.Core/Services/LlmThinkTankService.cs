using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LLMThinkTank.Core.Models;

namespace LLMThinkTank.Core.Services;

/// <summary>
/// Core orchestration service that dispatches chat completions to 11+ LLM providers.
/// Acts as a unified API gateway, translating a common conversation format into each
/// provider's native request schema (OpenAI, Anthropic Messages, Gemini GenerateContent,
/// Cohere Chat v2, and the OpenAI-compatible family).
/// <para>
/// <b>Architecture:</b> Each provider call follows the same pipeline:
/// <list type="number">
///   <item>Build provider-specific message payload from shared conversation history</item>
///   <item>Resolve API key, model, and max tokens from settings (with per-participant overrides)</item>
///   <item>Execute the HTTP request and emit diagnostics (redacted for security)</item>
///   <item>Parse the response and sanitize output (strip self-referencing prefixes)</item>
/// </list>
/// </para>
/// </summary>
public class LlmThinkTankService
{
    private readonly HttpClient http;
    private readonly LlmThinkTankSettingsService settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmThinkTankService"/> class.
    /// </summary>
    /// <param name="http">Shared HTTP client for all outbound API requests.</param>
    /// <param name="settings">Settings service providing API keys, model selections, and token limits.</param>
    public LlmThinkTankService(HttpClient http, LlmThinkTankSettingsService settings)
    {
        this.http = http;
        this.settings = settings;
    }

    /// <summary>
    /// Registry of all supported LLM providers with their default identities,
    /// avatars, personality prompts, and API key registration URLs.
    /// </summary>
    public List<LlmModel> Models { get; } = new()
    {
        // ── OpenAI ──
        new LlmModel
        {
            Id = "openai",
            Name = "ChatGPT",
            Avatar = "⬡",
            ApiKeyUrl = "https://platform.openai.com/api-keys",
            Personality = "You are ChatGPT, made by OpenAI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be conversational and curious. 2-3 sentences max."
        },
        // ── Anthropic ──
        new LlmModel
        {
            Id = "claude",
            Name = "Claude",
            Avatar = "◈",
            ApiKeyUrl = "https://console.anthropic.com/settings/keys",
            Personality = "You are Claude, made by Anthropic. You are in a live roundtable with other AI systems. Read what they said and engage directly. Be thoughtful and honest. 2-3 sentences max."
        },
        // ── Google ──
        new LlmModel
        {
            Id = "gemini",
            Name = "Gemini",
            Avatar = "✦",
            ApiKeyUrl = "https://aistudio.google.com/apikey",
            Personality = "You are Gemini, made by Google. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be analytical and creative. 2-3 sentences max."
        },
        // ── DeepSeek ──
        new LlmModel
        {
            Id = "deepseek",
            Name = "DeepSeek",
            Avatar = "◉",
            ApiKeyUrl = "https://platform.deepseek.com/api_keys",
            Personality = "You are DeepSeek, made by DeepSeek AI. You are in a live roundtable with other AI systems. Read what they said and engage directly. Be precise and insightful. 2-3 sentences max."
        },
        // ── Mistral AI ──
        new LlmModel
        {
            Id = "mistral",
            Name = "Mistral",
            Avatar = "▲",
            ApiKeyUrl = "https://console.mistral.ai/api-keys",
            Personality = "You are Mistral, made by Mistral AI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be sharp and efficient. 2-3 sentences max."
        },
        // ── xAI ──
        new LlmModel
        {
            Id = "xai",
            Name = "Grok",
            Avatar = "✕",
            ApiKeyUrl = "https://console.x.ai/",
            Personality = "You are Grok, made by xAI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be witty and bold. 2-3 sentences max."
        },
        // ── Groq ──
        new LlmModel
        {
            Id = "groq",
            Name = "Groq",
            Avatar = "⚡",
            ApiKeyUrl = "https://console.groq.com/keys",
            Personality = "You are an AI running on Groq's ultra-fast inference engine. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be quick and insightful. 2-3 sentences max."
        },
        // ── Together AI ──
        new LlmModel
        {
            Id = "together",
            Name = "Together",
            Avatar = "⊕",
            ApiKeyUrl = "https://api.together.ai/settings/api-keys",
            Personality = "You are an AI running on Together AI's platform. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be collaborative and thoughtful. 2-3 sentences max."
        },
        // ── OpenRouter ──
        new LlmModel
        {
            Id = "openrouter",
            Name = "OpenRouter",
            Avatar = "⬢",
            ApiKeyUrl = "https://openrouter.ai/settings/keys",
            Personality = "You are an AI accessed through OpenRouter. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be versatile and engaging. 2-3 sentences max."
        },
        // ── Fireworks AI ──
        new LlmModel
        {
            Id = "fireworks",
            Name = "Fireworks",
            Avatar = "🔥",
            ApiKeyUrl = "https://fireworks.ai/account/api-keys",
            Personality = "You are an AI running on Fireworks AI. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be fast and perceptive. 2-3 sentences max."
        },
        // ── Cohere ──
        new LlmModel
        {
            Id = "cohere",
            Name = "Cohere",
            Avatar = "◇",
            ApiKeyUrl = "https://dashboard.cohere.com/api-keys",
            Personality = "You are Command, made by Cohere. You are in a live roundtable with other AI systems. Read what they said and respond directly. Be grounded and clear. 2-3 sentences max."
        },
    };

    // ── Main dispatch ────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a chat completion request to the specified model using its default personality and auth.
    /// </summary>
    /// <param name="model">The LLM model definition to call.</param>
    /// <param name="topic">The discussion topic injected into the system prompt.</param>
    /// <param name="history">Shared conversation history visible to all participants.</param>
    /// <returns>The model's sanitized response text.</returns>
    public Task<string> CallModel(LlmModel model, string topic, List<SharedTurn> history)
        => CallProvider(model.Id, model.Personality, authOverrideJson: null, topic, history);

    /// <summary>
    /// Dispatches a chat completion request to any supported provider by ID.
    /// Routes to the appropriate provider-specific method based on <paramref name="providerId"/>.
    /// </summary>
    /// <param name="providerId">Provider identifier (e.g., "openai", "claude", "gemini").</param>
    /// <param name="personalityMarkdown">System prompt defining the AI's persona for this call.</param>
    /// <param name="authOverrideJson">Optional JSON blob overriding default API key/model/maxTokens.</param>
    /// <param name="topic">The discussion topic injected into the system prompt.</param>
    /// <param name="history">Shared conversation history visible to all participants.</param>
    /// <param name="maxTokensOverride">Optional per-conversation token limit that overrides all other defaults.</param>
    /// <returns>The model's sanitized response text.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="providerId"/> is not recognized.</exception>
    /// <exception cref="Exception">Thrown when the provider API returns a non-success status code.</exception>
    public Task<string> CallProvider(string providerId, string personalityMarkdown, string? authOverrideJson, string topic, List<SharedTurn> history, int? maxTokensOverride = null)
    {
        this.maxTokensOverride = maxTokensOverride;
        return providerId switch
        {
            "openai"     => CallOpenAI(providerId, personalityMarkdown, authOverrideJson, topic, history),
            "claude"     => CallClaude(providerId, personalityMarkdown, authOverrideJson, topic, history),
            "gemini"     => CallGemini(providerId, personalityMarkdown, authOverrideJson, topic, history),
            "deepseek"   => CallOpenAiCompatible(providerId, "DeepSeek", "https://api.deepseek.com/chat/completions", "deepseek-chat", personalityMarkdown, authOverrideJson, topic, history),
            "mistral"    => CallOpenAiCompatible(providerId, "Mistral", "https://api.mistral.ai/v1/chat/completions", "mistral-small-latest", personalityMarkdown, authOverrideJson, topic, history),
            "xai"        => CallOpenAiCompatible(providerId, "Grok", "https://api.x.ai/v1/chat/completions", "grok-3-mini-fast", personalityMarkdown, authOverrideJson, topic, history),
            "groq"       => CallOpenAiCompatible(providerId, "Groq", "https://api.groq.com/openai/v1/chat/completions", "llama-4-scout-17b-16e-instruct", personalityMarkdown, authOverrideJson, topic, history),
            "together"   => CallOpenAiCompatible(providerId, "Together", "https://api.together.xyz/v1/chat/completions", "meta-llama/Llama-4-Scout-17B-16E-Instruct", personalityMarkdown, authOverrideJson, topic, history),
            "openrouter" => CallOpenAiCompatible(providerId, "OpenRouter", "https://openrouter.ai/api/v1/chat/completions", "meta-llama/llama-3.1-8b-instruct", personalityMarkdown, authOverrideJson, topic, history),
            "fireworks"  => CallOpenAiCompatible(providerId, "Fireworks", "https://api.fireworks.ai/inference/v1/chat/completions", "accounts/fireworks/models/llama-v3p3-70b-instruct", personalityMarkdown, authOverrideJson, topic, history),
            "cohere"     => CallCohere(providerId, personalityMarkdown, authOverrideJson, topic, history),
            _            => throw new ArgumentException($"Unknown provider: {providerId}")
        };
    }

    private int? maxTokensOverride;

    /// <summary>
    /// Raised after every API call with the provider ID, redacted response body, and error flag.
    /// Subscribers (e.g., the diagnostics panel) use this for real-time API monitoring.
    /// </summary>
    public event Action<string, string, bool>? Diagnostics;

    /// <summary>
    /// Safely invokes the <see cref="Diagnostics"/> event with a redacted version of the raw API response.
    /// Swallows subscriber exceptions to prevent diagnostic failures from breaking the call pipeline.
    /// </summary>
    private void EmitDiagnostics(string providerId, string raw, bool isError)
    {
        try { Diagnostics?.Invoke(providerId, RedactResponseText(providerId, raw), isError); }
        catch { }
    }

    /// <summary>
    /// Strips actual response content from API JSON, preserving only metadata (model, usage, finish reason).
    /// This prevents sensitive or lengthy content from appearing in diagnostic logs.
    /// </summary>
    private static string RedactResponseText(string providerId, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        try
        {
            using var doc = JsonDocument.Parse(raw);

            object? redacted = providerId switch
            {
                "openai" or "deepseek" or "mistral" or "xai" or "groq" or "together" or "openrouter" or "fireworks"
                    => RedactOpenAi(doc.RootElement),
                "claude" => RedactClaude(doc.RootElement),
                "gemini" => RedactGemini(doc.RootElement),
                "cohere" => RedactCohere(doc.RootElement),
                _ => null
            };

            if (redacted is null)
                return raw;

            return JsonSerializer.Serialize(redacted, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }

    private static object? RedactOpenAi(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return null;

        var newChoices = new List<object>();
        foreach (var c in choices.EnumerateArray())
        {
            var messageRole = c.TryGetProperty("message", out var msg) && msg.TryGetProperty("role", out var role)
                ? role.GetString()
                : null;

            newChoices.Add(new
            {
                index = c.TryGetProperty("index", out var idx) ? idx.GetInt32() : (int?)null,
                finish_reason = c.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null,
                message = new { role = messageRole, content = "..." }
            });
        }

        return new
        {
            id = root.TryGetProperty("id", out var id) ? id.GetString() : null,
            model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
            created = root.TryGetProperty("created", out var created) ? created.GetInt64() : (long?)null,
            usage = root.TryGetProperty("usage", out var usage) ? usage : (JsonElement?)null,
            choices = newChoices
        };
    }

    private static object? RedactClaude(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;

        return new
        {
            id = root.TryGetProperty("id", out var id) ? id.GetString() : null,
            model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
            type = root.TryGetProperty("type", out var type) ? type.GetString() : null,
            stop_reason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null,
            usage = root.TryGetProperty("usage", out var usage) ? usage : (JsonElement?)null,
            content = new[] { new { type = "text", text = "..." } }
        };
    }

    private static object? RedactGemini(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var cands) || cands.ValueKind != JsonValueKind.Array)
            return null;

        var newCands = new List<object>();
        foreach (var c in cands.EnumerateArray())
        {
            newCands.Add(new
            {
                finishReason = c.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null,
                safetyRatings = c.TryGetProperty("safetyRatings", out var sr) ? sr : (JsonElement?)null,
                content = new { role = "model", parts = new[] { new { text = "..." } } }
            });
        }

        return new
        {
            candidates = newCands,
            promptFeedback = root.TryGetProperty("promptFeedback", out var pf) ? pf : (JsonElement?)null,
            usageMetadata = root.TryGetProperty("usageMetadata", out var um) ? um : (JsonElement?)null
        };
    }

    private static object? RedactCohere(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message))
            return null;

        return new
        {
            id = root.TryGetProperty("id", out var id) ? id.GetString() : null,
            model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
            finish_reason = root.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null,
            usage = root.TryGetProperty("usage", out var usage) ? usage : (JsonElement?)null,
            message = new { role = "assistant", content = new[] { new { type = "text", text = "..." } } }
        };
    }

    /// <summary>
    /// Resolves the API key for a provider, checking the per-participant auth override first,
    /// then falling back to the global provider auth in settings.
    /// </summary>
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

    /// <summary>
    /// Resolves the model ID for a provider. Priority: auth override JSON > global settings > default.
    /// </summary>
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
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
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
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
        }
        catch { }

        return defaultModel;
    }

    /// <summary>
    /// Resolves the max output tokens for a provider. Priority: auth override JSON > global settings > default (2048).
    /// </summary>
    private int GetMaxTokens(string providerId, string? authOverrideJson, int defaultMaxTokens = 2048)
    {
        if (this.maxTokensOverride.HasValue)
            return this.maxTokensOverride.Value;

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

    // ── Shared history builder ───────────────────────────────────────────────

    /// <summary>Maximum number of recent turns included in the context window to manage token usage.</summary>
    private const int MaxContextTurns = 8;

    /// <summary>
    /// Returns the most recent <see cref="MaxContextTurns"/> turns from conversation history,
    /// keeping context windows manageable across all providers.
    /// </summary>
    private static List<SharedTurn> TrimHistory(List<SharedTurn> history)
    {
        if (history.Count <= MaxContextTurns)
            return history;
        return history.Skip(history.Count - MaxContextTurns).ToList();
    }

    /// <summary>
    /// Converts shared conversation history into the OpenAI-style messages array format.
    /// The calling model's own prior turns become "assistant" messages; all other participants'
    /// turns become "user" messages prefixed with the speaker name (e.g., "[Claude]: ...").
    /// If history is empty, generates an opening prompt asking the model for initial thoughts.
    /// </summary>
    private static List<object> BuildOpenAiStyleMessages(string providerId, string personalityMarkdown, string topic, List<SharedTurn> history)
    {
        var recent = TrimHistory(history);

        var messages = new List<object>
        {
            new { role = "system", content = $"{personalityMarkdown}\n\nTopic: \"{topic}\"" }
        };

        if (recent.Count == 0)
        {
            messages.Add(new { role = "user", content = $"The topic is: \"{topic}\". Please give your opening thoughts." });
            return messages;
        }

        foreach (var turn in recent)
        {
            if (turn.ModelId == providerId)
                messages.Add(new { role = "assistant", content = turn.Text });
            else
                messages.Add(new { role = "user", content = $"[{turn.ModelName}]: {turn.Text}" });
        }

        if (recent.Last().ModelId == providerId)
            messages.Add(new { role = "user", content = "Please continue the discussion." });

        return messages;
    }

    // ── OpenAI ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the OpenAI Chat Completions API (<c>POST /v1/chat/completions</c>).
    /// Uses Bearer token authentication and the standard OpenAI message format.
    /// </summary>
    private async Task<string> CallOpenAI(string providerId, string personalityMarkdown, string? authOverrideJson, string topic, List<SharedTurn> history)
    {
        var messages = BuildOpenAiStyleMessages(providerId, personalityMarkdown, topic, history);
        var model = GetModel("openai", authOverrideJson, defaultModel: "gpt-4.1-mini");

        var payload = new
        {
            model,
            max_tokens = GetMaxTokens("openai", authOverrideJson),
            messages
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey("openai", authOverrideJson));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        EmitDiagnostics("openai", json, isError: !response.IsSuccessStatusCode);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"OpenAI {response.StatusCode}: {ExtractError(json)}");

        var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
        return SanitizeModelOutput(providerId, text);
    }

    // ── Claude ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the Anthropic Messages API (<c>POST /v1/messages</c>).
    /// Converts OpenAI-style messages to Anthropic's format: system prompt as a top-level field,
    /// and only user/assistant role messages in the messages array. Uses <c>x-api-key</c> header auth.
    /// <para>
    /// <b>Prompt caching:</b> The system prompt and the last user message are marked with
    /// <c>cache_control: {"type": "ephemeral"}</c> so that repeated calls with the same
    /// personality + topic pay only 10% of normal input cost on cache hits.
    /// </para>
    /// </summary>
    private async Task<string> CallClaude(string providerId, string personalityMarkdown, string? authOverrideJson, string topic, List<SharedTurn> history)
    {
        var openAiMessages = BuildOpenAiStyleMessages(providerId, personalityMarkdown, topic, history);

        // System prompt as a cacheable content block array (up to 90% savings on cache hits)
        var system = new object[]
        {
            new
            {
                type = "text",
                text = $"{personalityMarkdown}\n\nTopic: \"{topic}\"",
                cache_control = new { type = "ephemeral" }
            }
        };

        var filtered = openAiMessages
            .Where(m => (string)m.GetType().GetProperty("role")!.GetValue(m)! != "system")
            .Select(m => new
            {
                role = (string)m.GetType().GetProperty("role")!.GetValue(m)!,
                content = (string)m.GetType().GetProperty("content")!.GetValue(m)!
            })
            .ToList();

        // Mark the last user message for caching so the full conversation prefix is cached
        var claudeMessages = filtered.Select((m, i) =>
            i == filtered.Count - 1 && m.role == "user"
                ? (object)new
                {
                    m.role,
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = m.content,
                            cache_control = new { type = "ephemeral" }
                        }
                    }
                }
                : m
        ).ToList();

        var model = GetModel("claude", authOverrideJson, defaultModel: "claude-sonnet-4-6");

        var payload = new
        {
            model,
            max_tokens = GetMaxTokens("claude", authOverrideJson),
            system,
            messages = claudeMessages
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", GetApiKey("claude", authOverrideJson));
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", "prompt-caching-2024-07-31");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        EmitDiagnostics("claude", json, isError: !response.IsSuccessStatusCode);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Claude {response.StatusCode}: {ExtractError(json)}");

        var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";
        return SanitizeModelOutput(providerId, text);
    }

    // ── Gemini ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the Google Gemini GenerateContent API.
    /// Handles Gemini's unique constraints: no consecutive same-role turns (merged automatically),
    /// first turn must be "user" role, and multi-part responses are concatenated into a single string.
    /// API key is passed as a query parameter rather than a header.
    /// </summary>
    private async Task<string> CallGemini(string providerId, string personalityMarkdown, string? authOverrideJson, string topic, List<SharedTurn> history)
    {
        var recent = TrimHistory(history);
        var rawTurns = new List<(string role, string text)>();

        if (recent.Count == 0)
        {
            rawTurns.Add(("user", $"The topic is: \"{topic}\". Please give your opening thoughts."));
        }
        else
        {
            foreach (var turn in recent)
            {
                var role = turn.ModelId == providerId ? "model" : "user";
                var text = turn.ModelId == providerId ? turn.Text : $"[{turn.ModelName}]: {turn.Text}";
                rawTurns.Add((role, text));
            }

            if (recent.Last().ModelId == providerId)
                rawTurns.Add(("user", "Please continue the discussion."));
        }

        var merged = new List<(string role, string text)>();
        foreach (var (role, text) in rawTurns)
        {
            if (merged.Count > 0 && merged.Last().role == role)
                merged[^1] = (role, merged.Last().text + "\n" + text);
            else
                merged.Add((role, text));
        }

        if (merged.Count > 0 && merged[0].role == "model")
            merged.Insert(0, ("user", $"Topic: \"{topic}\""));

        var contents = merged.Select(t => new
        {
            role = t.role,
            parts = new[] { new { text = t.text } }
        }).ToList();

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = $"{personalityMarkdown}\n\nTopic: \"{topic}\"\n\nRespond with a single cohesive message. Do not split your answer into separate parts." } } },
            contents,
            generationConfig = new { maxOutputTokens = GetMaxTokens("gemini", authOverrideJson) }
        };

        var model = GetModel("gemini", authOverrideJson, defaultModel: "gemini-2.5-flash");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={GetApiKey("gemini", authOverrideJson)}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        EmitDiagnostics("gemini", json, isError: !response.IsSuccessStatusCode);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Gemini {response.StatusCode}: {ExtractError(json)}");

        var doc = JsonDocument.Parse(json);
        var parts = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts");

        if (parts.ValueKind != JsonValueKind.Array)
            return "";

        var sb = new StringBuilder();
        foreach (var p in parts.EnumerateArray())
        {
            if (!p.TryGetProperty("text", out var t))
                continue;

            var partText = t.GetString();
            if (string.IsNullOrEmpty(partText))
                continue;

            if (sb.Length > 0)
                sb.Append("\n");
            sb.Append(partText);
        }

        return SanitizeModelOutput(providerId, sb.ToString());
    }

    // ── OpenAI-Compatible (DeepSeek, Mistral, xAI/Grok, Groq, Together, OpenRouter, Fireworks) ──

    /// <summary>
    /// Generic handler for providers that implement the OpenAI Chat Completions API contract.
    /// Supports DeepSeek, Mistral, xAI/Grok, Groq, Together AI, OpenRouter, and Fireworks AI.
    /// Each provider differs only in endpoint URL and default model; the request/response format is identical.
    /// </summary>
    private async Task<string> CallOpenAiCompatible(
        string providerId, string displayName, string endpoint, string defaultModel,
        string personalityMarkdown, string? authOverrideJson, string topic, List<SharedTurn> history)
    {
        var messages = BuildOpenAiStyleMessages(providerId, personalityMarkdown, topic, history);
        var model = GetModel(providerId, authOverrideJson, defaultModel);

        var payload = new
        {
            model,
            max_tokens = GetMaxTokens(providerId, authOverrideJson),
            messages
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey(providerId, authOverrideJson));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        EmitDiagnostics(providerId, json, isError: !response.IsSuccessStatusCode);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{displayName} {response.StatusCode}: {ExtractError(json)}");

        var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
        return SanitizeModelOutput(providerId, text);
    }

    // ── Cohere ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the Cohere Chat v2 API (<c>POST /v2/chat</c>).
    /// Uses OpenAI-style message format but parses the Cohere-specific response structure
    /// where content is nested under <c>message.content[0].text</c>.
    /// </summary>
    private async Task<string> CallCohere(string providerId, string personalityMarkdown, string? authOverrideJson, string topic, List<SharedTurn> history)
    {
        var messages = BuildOpenAiStyleMessages(providerId, personalityMarkdown, topic, history);
        var model = GetModel("cohere", authOverrideJson, defaultModel: "command-a-03-2025");

        var payload = new
        {
            model,
            max_tokens = GetMaxTokens("cohere", authOverrideJson),
            messages
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.cohere.com/v2/chat");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey("cohere", authOverrideJson));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        EmitDiagnostics("cohere", json, isError: !response.IsSuccessStatusCode);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Cohere {response.StatusCode}: {ExtractError(json)}");

        var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("message")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";
        return SanitizeModelOutput(providerId, text);
    }

    // ── Sanitization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Strips self-referencing prefixes that models sometimes prepend to their responses
    /// (e.g., "[ChatGPT]:", "Claude:", "[Assistant]:"). This keeps the displayed output
    /// clean since participant identity is already shown in the chat bubble header.
    /// </summary>
    private static string SanitizeModelOutput(string providerId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = text.TrimStart();

        var pattern = "^(?:\\s*(?:\\[(?:chatgpt|gpt|assistant|openai|claude|gemini|deepseek|mistral|grok|xai|groq|together|openrouter|fireworks|cohere|command)\\]\\s*:|(?:chatgpt|gpt|assistant|openai|claude|gemini|deepseek|mistral|grok|xai|groq|together|openrouter|fireworks|cohere|command)\\s*:))+\\s*";
        text = System.Text.RegularExpressions.Regex.Replace(text, pattern, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return text.TrimStart();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to extract a human-readable error message from a provider's JSON error response.
    /// Falls back to the raw JSON (truncated to 200 chars) if parsing fails.
    /// </summary>
    private static string ExtractError(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? json;
            }
        }
        catch { }
        return json.Length > 200 ? json[..200] : json;
    }
}
