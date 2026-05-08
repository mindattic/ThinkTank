using MindAttic.Legion;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

[TestFixture]
public class ThinkTankServiceTests
{
    private SettingsService settings = null!;
    private ThinkTankService sut = null!;

    [SetUp]
    public void SetUp()
    {
        var sandbox = MindAtticCredentialStore.CredentialDirectory;
        if (Directory.Exists(sandbox))
        {
            foreach (var f in Directory.EnumerateFiles(sandbox))
                File.Delete(f);
        }

        settings = new SettingsService();
        sut = new ThinkTankService(new LegionClient(new HttpClient()), settings);
    }

    // ── Models registry ─────────────────────────────────────────────────

    [Test]
    public void Models_Has4Providers()
    {
        Assert.That(sut.Models, Has.Count.EqualTo(4));
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
    [TestCase("deepseek", "DeepSeek")]
    public void WrapPersonaForClaudeFallback_AnchorsPersonaToOriginalProvider(string providerId, string expectedLabel)
    {
        var wrapped = ThinkTankService.WrapPersonaForClaudeFallback(providerId, "You are friendly.");

        Assert.That(wrapped, Does.Contain(expectedLabel));
        Assert.That(wrapped, Does.Contain("You are friendly."));
        Assert.That(wrapped, Does.Contain("Do not mention Anthropic"));
        Assert.That(wrapped, Does.Contain("Do not break character"));
    }

    [Test]
    public void WrapPersonaForClaudeFallback_DistinctProviders_ProduceDistinctWrappers()
    {
        var openai = ThinkTankService.WrapPersonaForClaudeFallback("openai", "shared persona");
        var gemini = ThinkTankService.WrapPersonaForClaudeFallback("gemini", "shared persona");

        Assert.That(openai, Is.Not.EqualTo(gemini));
    }

    // ── SanitizeModelOutput (private static, tested via reflection) ─────

    private static string InvokeSanitize(string providerId, string text)
    {
        var method = typeof(ThinkTankService)
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
    public void SanitizeModelOutput_StripsProviderPrefixes(string providerId, string input, string expected)
    {
        Assert.That(InvokeSanitize(providerId, input), Is.EqualTo(expected));
    }

    // ── TrimHistory (private static, tested via reflection) ─────────────

    private static List<SharedTurn> InvokeTrimHistory(List<SharedTurn> history)
    {
        var method = typeof(ThinkTankService)
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

    // ── Diagnostics event ───────────────────────────────────────────────

    [Test]
    public void DiagnosticsEvent_CanSubscribe()
    {
        string? capturedProvider = null;
        sut.Diagnostics += (provider, body, isError) => capturedProvider = provider;

        // Just verify subscription doesn't throw - actual firing requires HTTP calls
        Assert.That(capturedProvider, Is.Null);
    }

    private sealed class StubChatHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            var body = "{\"choices\":[{\"message\":{\"content\":\"local ok\"}}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
