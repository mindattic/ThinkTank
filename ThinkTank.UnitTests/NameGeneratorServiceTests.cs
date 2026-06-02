using System.Net;
using System.Text;
using MindAttic.Legion;
using NUnit.Framework;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// NameGeneratorService is the only ThinkTank-side service that calls Legion
/// for non-conversation traffic. These tests run the full dispatch path
/// (NameGen → ThinkTankService → LegionClient → HTTP) so that the routing
/// rule "every LLM call answers to Legion on high" is exercised end-to-end —
/// the stub handler intercepts at the HTTP boundary, not at LegionClient
/// itself, so any future bypass of Legion would break these tests.
/// </summary>
[TestFixture]
public class NameGeneratorServiceTests
{
    private SettingsService settings = null!;
    private ScriptedHandler handler = null!;
    private NameGeneratorService sut = null!;

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
        settings.RuntimeApiKeyOverrides["openai"] = "test-api-key"; // keys come from Vault overlay

        handler = new ScriptedHandler();
        var legion = new LegionClient(new HttpClient(handler));
        var psychometrics = new PsychometricProfileService(Path.Combine(Path.GetTempPath(), "tt-psy-" + Guid.NewGuid().ToString("N")));
        var thinkTank = new ThinkTankService(legion, settings, psychometrics);
        sut = new NameGeneratorService(thinkTank, settings);
    }

    [Test]
    public async Task GenerateFirstName_ReturnsCleanName_WhenLLMRespondsCleanly()
    {
        handler.Reply = "Mateo";
        var name = await sut.GenerateFirstNameAsync("openai");
        Assert.That(name, Is.EqualTo("Mateo"));
    }

    [Test]
    public async Task GenerateFirstName_StripsNonLetters()
    {
        // Models often add quotes / punctuation despite the prompt instruction.
        handler.Reply = "\"Aoife\".";
        var name = await sut.GenerateFirstNameAsync("openai");
        Assert.That(name, Is.EqualTo("Aoife"));
    }

    [Test]
    public async Task GenerateFirstName_TruncatesTo32Characters()
    {
        var longName = new string('A', 100);
        handler.Reply = longName;
        var name = await sut.GenerateFirstNameAsync("openai");
        Assert.That(name.Length, Is.LessThanOrEqualTo(32));
    }

    [Test]
    public async Task GenerateFirstName_FallsBackToAlex_WhenResponseIsEmpty()
    {
        handler.Reply = "";
        var name = await sut.GenerateFirstNameAsync("openai");
        Assert.That(name, Is.EqualTo("Alex"));
    }

    [Test]
    public async Task GenerateFirstName_FallsBackToAlex_WhenResponseIsAllPunctuation()
    {
        handler.Reply = "!!! ??? ...";
        var name = await sut.GenerateFirstNameAsync("openai");
        Assert.That(name, Is.EqualTo("Alex"));
    }

    [Test]
    public async Task GenerateFirstName_RoutesThroughLegion_NotDirectly()
    {
        // Proves the request actually flowed through Legion: Legion adds the
        // "Authorization: Bearer test-api-key" header for OpenAI-compatible
        // providers. If anyone reintroduces a direct HttpClient call from
        // ThinkTank that bypasses Legion, this assertion fails.
        handler.Reply = "Sora";
        await sut.GenerateFirstNameAsync("openai");

        Assert.That(handler.LastRequest, Is.Not.Null);
        Assert.That(handler.LastRequest!.Headers.Authorization?.Scheme, Is.EqualTo("Bearer"));
        Assert.That(handler.LastRequest.Headers.Authorization?.Parameter, Is.EqualTo("test-api-key"));
        Assert.That(handler.LastRequest.RequestUri!.Host, Is.EqualTo("api.openai.com"));
    }

    /// <summary>
    /// Stub HttpMessageHandler that returns an OpenAI Chat Completions shape
    /// with whatever <see cref="Reply"/> is set to. Captures the last request
    /// so tests can inspect the wire-level payload Legion produced.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public string Reply { get; set; } = "ok";
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            // Quote-escape so a Reply like `"Aoife".` round-trips through JSON cleanly.
            var escaped = System.Text.Json.JsonEncodedText.Encode(Reply).ToString();
            var body = $"{{\"choices\":[{{\"message\":{{\"content\":\"{escaped}\"}}}}]}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
