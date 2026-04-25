using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using LLMThinkTank.Core.Models;
using LLMThinkTank.Core.Services;

namespace LLMThinkTank.UnitTests;

[TestFixture]
public class LlmThinkTankServiceTests
{
    private SettingsService settings = null!;
    private LlmThinkTankService sut = null!;

    [SetUp]
    public void SetUp()
    {
        var sandbox = MindAtticLlmCredentialsStore.Root;
        if (Directory.Exists(sandbox))
        {
            foreach (var f in Directory.EnumerateFiles(sandbox))
                File.Delete(f);
        }

        settings = new SettingsService();
        sut = new LlmThinkTankService(new HttpClient(), settings);
    }

    // ── Models registry ─────────────────────────────────────────────────

    [Test]
    public void Models_Has11Providers()
    {
        Assert.That(sut.Models, Has.Count.EqualTo(11));
    }

    [Test]
    public void Models_AllHaveRequiredFields()
    {
        foreach (var model in sut.Models)
        {
            Assert.That(model.Id, Is.Not.Null.And.Not.Empty, $"Model missing Id");
            Assert.That(model.Name, Is.Not.Null.And.Not.Empty, $"Model {model.Id} missing Name");
            Assert.That(model.Avatar, Is.Not.Null.And.Not.Empty, $"Model {model.Id} missing Avatar");
            Assert.That(model.Personality, Is.Not.Null.And.Not.Empty, $"Model {model.Id} missing Personality");
            Assert.That(model.ApiKeyUrl, Is.Not.Null.And.Not.Empty, $"Model {model.Id} missing ApiKeyUrl");
        }
    }

    [Test]
    public void Models_IdsAreUnique()
    {
        var ids = sut.Models.Select(m => m.Id).ToHashSet();
        Assert.That(ids.Count, Is.EqualTo(sut.Models.Count));
    }

    [TestCase("openai")]
    [TestCase("claude")]
    [TestCase("gemini")]
    [TestCase("deepseek")]
    [TestCase("mistral")]
    [TestCase("xai")]
    [TestCase("groq")]
    [TestCase("together")]
    [TestCase("openrouter")]
    [TestCase("fireworks")]
    [TestCase("cohere")]
    public void Models_ContainsExpectedProvider(string providerId)
    {
        Assert.That(sut.Models.Any(m => m.Id == providerId), Is.True, $"Missing provider: {providerId}");
    }

    // ── CallProvider routing ────────────────────────────────────────────

    [Test]
    public void CallProvider_UnknownProvider_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.CallProvider("unknown_provider", "personality", null, "topic", new List<SharedTurn>()));
    }

    // ── Claude fallback wrapping ────────────────────────────────────────

    [TestCase("openai",   "ChatGPT (OpenAI)")]
    [TestCase("gemini",   "Gemini (Google)")]
    [TestCase("xai",      "Grok (xAI)")]
    [TestCase("cohere",   "Command (Cohere)")]
    [TestCase("deepseek", "DeepSeek")]
    [TestCase("mistral",  "Mistral")]
    public void WrapPersonaForClaudeFallback_AnchorsPersonaToOriginalProvider(string providerId, string expectedLabel)
    {
        var wrapped = LlmThinkTankService.WrapPersonaForClaudeFallback(providerId, "You are friendly.");

        Assert.That(wrapped, Does.Contain(expectedLabel));
        Assert.That(wrapped, Does.Contain("You are friendly."));
        Assert.That(wrapped, Does.Contain("Do not mention Anthropic"));
        Assert.That(wrapped, Does.Contain("Do not break character"));
    }

    [Test]
    public void WrapPersonaForClaudeFallback_DistinctProviders_ProduceDistinctWrappers()
    {
        var openai = LlmThinkTankService.WrapPersonaForClaudeFallback("openai", "shared persona");
        var gemini = LlmThinkTankService.WrapPersonaForClaudeFallback("gemini", "shared persona");

        Assert.That(openai, Is.Not.EqualTo(gemini));
    }

    // ── SanitizeModelOutput (private static, tested via reflection) ─────

    private static string InvokeSanitize(string providerId, string text)
    {
        var method = typeof(LlmThinkTankService)
            .GetMethod("SanitizeModelOutput", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { providerId, text })!;
    }

    [Test]
    public void SanitizeModelOutput_PlainText_Unchanged()
    {
        Assert.That(InvokeSanitize("openai", "Hello world"), Is.EqualTo("Hello world"));
    }

    [Test]
    public void SanitizeModelOutput_StripsChatGptPrefix()
    {
        Assert.That(InvokeSanitize("openai", "[ChatGPT]: Hello"), Is.EqualTo("Hello"));
    }

    [Test]
    public void SanitizeModelOutput_StripsClaudePrefix()
    {
        Assert.That(InvokeSanitize("claude", "Claude: I think"), Is.EqualTo("I think"));
    }

    [Test]
    public void SanitizeModelOutput_StripsAssistantPrefix()
    {
        Assert.That(InvokeSanitize("openai", "[Assistant]: Reply"), Is.EqualTo("Reply"));
    }

    [Test]
    public void SanitizeModelOutput_NullInput_ReturnsNull()
    {
        Assert.That(InvokeSanitize("openai", null!), Is.Null);
    }

    [Test]
    public void SanitizeModelOutput_EmptyInput_ReturnsEmpty()
    {
        Assert.That(InvokeSanitize("openai", ""), Is.EqualTo(""));
    }

    [Test]
    public void SanitizeModelOutput_WhitespaceOnly_ReturnsWhitespace()
    {
        Assert.That(InvokeSanitize("openai", "   "), Is.EqualTo("   "));
    }

    [Test]
    public void SanitizeModelOutput_CaseInsensitive()
    {
        Assert.That(InvokeSanitize("openai", "[CHATGPT]: response"), Is.EqualTo("response"));
    }

    [TestCase("gemini", "Gemini: text here", "text here")]
    [TestCase("deepseek", "DeepSeek: reply", "reply")]
    [TestCase("mistral", "[Mistral]: answer", "answer")]
    [TestCase("xai", "Grok: witty reply", "witty reply")]
    [TestCase("cohere", "Command: grounded", "grounded")]
    public void SanitizeModelOutput_StripsProviderPrefixes(string providerId, string input, string expected)
    {
        Assert.That(InvokeSanitize(providerId, input), Is.EqualTo(expected));
    }

    // ── ExtractError (private static, tested via reflection) ────────────

    private static string InvokeExtractError(string json)
    {
        var method = typeof(LlmThinkTankService)
            .GetMethod("ExtractError", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { json })!;
    }

    [Test]
    public void ExtractError_WithErrorMessage_ReturnsMessage()
    {
        var json = JsonSerializer.Serialize(new { error = new { message = "Rate limit exceeded" } });
        Assert.That(InvokeExtractError(json), Is.EqualTo("Rate limit exceeded"));
    }

    [Test]
    public void ExtractError_NoErrorField_ReturnsRawJson()
    {
        var json = "{\"status\":\"fail\"}";
        Assert.That(InvokeExtractError(json), Is.EqualTo(json));
    }

    [Test]
    public void ExtractError_InvalidJson_ReturnsRawInput()
    {
        Assert.That(InvokeExtractError("not json"), Is.EqualTo("not json"));
    }

    [Test]
    public void ExtractError_LongInput_TruncatesTo200Chars()
    {
        var longString = new string('x', 500);
        Assert.That(InvokeExtractError(longString), Has.Length.EqualTo(200));
    }

    // ── TrimHistory (private static, tested via reflection) ─────────────

    private static List<SharedTurn> InvokeTrimHistory(List<SharedTurn> history)
    {
        var method = typeof(LlmThinkTankService)
            .GetMethod("TrimHistory", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (List<SharedTurn>)method.Invoke(null, new object[] { history })!;
    }

    [Test]
    public void TrimHistory_Under8_ReturnsAll()
    {
        var history = Enumerable.Range(0, 5)
            .Select(i => new SharedTurn { ModelId = "test", Text = $"turn {i}" })
            .ToList();

        var result = InvokeTrimHistory(history);
        Assert.That(result, Has.Count.EqualTo(5));
    }

    [Test]
    public void TrimHistory_Exactly8_ReturnsAll()
    {
        var history = Enumerable.Range(0, 8)
            .Select(i => new SharedTurn { ModelId = "test", Text = $"turn {i}" })
            .ToList();

        var result = InvokeTrimHistory(history);
        Assert.That(result, Has.Count.EqualTo(8));
    }

    [Test]
    public void TrimHistory_Over8_KeepsLast8()
    {
        var history = Enumerable.Range(0, 12)
            .Select(i => new SharedTurn { ModelId = "test", Text = $"turn {i}" })
            .ToList();

        var result = InvokeTrimHistory(history);
        Assert.That(result, Has.Count.EqualTo(8));
        Assert.That(result[0].Text, Is.EqualTo("turn 4"));
        Assert.That(result[7].Text, Is.EqualTo("turn 11"));
    }

    [Test]
    public void TrimHistory_Empty_ReturnsEmpty()
    {
        var result = InvokeTrimHistory(new List<SharedTurn>());
        Assert.That(result, Is.Empty);
    }

    // ── BuildOpenAiStyleMessages (private static, tested via reflection) ─

    private static List<object> InvokeBuildMessages(string providerId, string personality, string topic, List<SharedTurn> history)
    {
        var method = typeof(LlmThinkTankService)
            .GetMethod("BuildOpenAiStyleMessages", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (List<object>)method.Invoke(null, new object[] { providerId, personality, topic, history })!;
    }

    private static string GetRole(object msg)
        => (string)msg.GetType().GetProperty("role")!.GetValue(msg)!;

    private static string GetContent(object msg)
        => (string)msg.GetType().GetProperty("content")!.GetValue(msg)!;

    [Test]
    public void BuildMessages_EmptyHistory_SystemAndOpeningPrompt()
    {
        var messages = InvokeBuildMessages("openai", "You are ChatGPT", "AI safety", new List<SharedTurn>());

        Assert.That(messages, Has.Count.EqualTo(2));
        Assert.That(GetRole(messages[0]), Is.EqualTo("system"));
        Assert.That(GetContent(messages[0]), Does.Contain("AI safety"));
        Assert.That(GetRole(messages[1]), Is.EqualTo("user"));
        Assert.That(GetContent(messages[1]), Does.Contain("opening thoughts"));
    }

    [Test]
    public void BuildMessages_OwnTurnsAreAssistant()
    {
        var history = new List<SharedTurn>
        {
            new() { ModelId = "openai", ModelName = "ChatGPT", Text = "My response", Round = 0 }
        };

        var messages = InvokeBuildMessages("openai", "You are ChatGPT", "topic", history);

        // system, assistant turn, then "please continue" user message
        var assistantMsg = messages.First(m => GetRole(m) == "assistant");
        Assert.That(GetContent(assistantMsg), Is.EqualTo("My response"));
    }

    [Test]
    public void BuildMessages_OtherTurnsAreUser()
    {
        var history = new List<SharedTurn>
        {
            new() { ModelId = "claude", ModelName = "Claude", Text = "Claude's thought", Round = 0 }
        };

        var messages = InvokeBuildMessages("openai", "You are ChatGPT", "topic", history);
        var userMsg = messages.Where(m => GetRole(m) == "user").First();
        Assert.That(GetContent(userMsg), Does.Contain("[Claude]"));
        Assert.That(GetContent(userMsg), Does.Contain("Claude's thought"));
    }

    [Test]
    public void BuildMessages_LastTurnIsSelf_AddsContinuePrompt()
    {
        var history = new List<SharedTurn>
        {
            new() { ModelId = "openai", ModelName = "ChatGPT", Text = "I said something", Round = 0 }
        };

        var messages = InvokeBuildMessages("openai", "You are ChatGPT", "topic", history);
        var lastMsg = messages.Last();
        Assert.That(GetRole(lastMsg), Is.EqualTo("user"));
        Assert.That(GetContent(lastMsg), Does.Contain("continue the discussion"));
    }

    [Test]
    public void BuildMessages_SystemPromptContainsPersonalityAndTopic()
    {
        var messages = InvokeBuildMessages("openai", "Test personality", "Test topic", new List<SharedTurn>());
        var system = GetContent(messages[0]);
        Assert.That(system, Does.Contain("Test personality"));
        Assert.That(system, Does.Contain("Test topic"));
    }

    // ── RedactResponseText (private static, tested via reflection) ──────

    private static string InvokeRedact(string providerId, string raw)
    {
        var method = typeof(LlmThinkTankService)
            .GetMethod("RedactResponseText", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { providerId, raw })!;
    }

    [Test]
    public void Redact_NullOrEmpty_ReturnsSame()
    {
        Assert.That(InvokeRedact("openai", ""), Is.EqualTo(""));
        Assert.That(InvokeRedact("openai", null!), Is.Null);
    }

    [Test]
    public void Redact_InvalidJson_ReturnsSame()
    {
        Assert.That(InvokeRedact("openai", "not json"), Is.EqualTo("not json"));
    }

    [Test]
    public void Redact_OpenAiFormat_RedactsContent()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-123",
            model = "gpt-4",
            choices = new[]
            {
                new { index = 0, finish_reason = "stop", message = new { role = "assistant", content = "secret content" } }
            }
        });

        var redacted = InvokeRedact("openai", json);
        Assert.That(redacted, Does.Not.Contain("secret content"));
        Assert.That(redacted, Does.Contain("..."));
        Assert.That(redacted, Does.Contain("gpt-4"));
    }

    [Test]
    public void Redact_ClaudeFormat_RedactsContent()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "msg_123",
            model = "claude-3-opus",
            type = "message",
            stop_reason = "end_turn",
            content = new[] { new { type = "text", text = "sensitive answer" } }
        });

        var redacted = InvokeRedact("claude", json);
        Assert.That(redacted, Does.Not.Contain("sensitive answer"));
        Assert.That(redacted, Does.Contain("..."));
        Assert.That(redacted, Does.Contain("claude-3-opus"));
    }

    [Test]
    public void Redact_GeminiFormat_RedactsContent()
    {
        var json = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { finishReason = "STOP", content = new { role = "model", parts = new[] { new { text = "gemini secret" } } } }
            }
        });

        var redacted = InvokeRedact("gemini", json);
        Assert.That(redacted, Does.Not.Contain("gemini secret"));
        Assert.That(redacted, Does.Contain("..."));
    }

    [Test]
    public void Redact_CohereFormat_RedactsContent()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "gen-123",
            model = "command-r-plus",
            finish_reason = "COMPLETE",
            message = new { role = "assistant", content = new[] { new { type = "text", text = "cohere secret" } } }
        });

        var redacted = InvokeRedact("cohere", json);
        Assert.That(redacted, Does.Not.Contain("cohere secret"));
        Assert.That(redacted, Does.Contain("..."));
        Assert.That(redacted, Does.Contain("command-r-plus"));
    }

    [Test]
    public void Redact_OpenAiCompatibleProviders_UseOpenAiRedaction()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "gen-123",
            model = "deepseek-chat",
            choices = new[]
            {
                new { index = 0, finish_reason = "stop", message = new { role = "assistant", content = "private" } }
            }
        });

        foreach (var provider in new[] { "deepseek", "mistral", "xai", "groq", "together", "openrouter", "fireworks" })
        {
            var redacted = InvokeRedact(provider, json);
            Assert.That(redacted, Does.Not.Contain("private"), $"Provider {provider} did not redact");
        }
    }

    // ── Diagnostics event ───────────────────────────────────────────────

    [Test]
    public void DiagnosticsEvent_CanSubscribe()
    {
        string? capturedProvider = null;
        sut.Diagnostics += (provider, body, isError) => capturedProvider = provider;

        // Just verify subscription doesn't throw - actual firing requires HTTP calls
        Assert.That(capturedProvider, Is.Null);
    }
}
