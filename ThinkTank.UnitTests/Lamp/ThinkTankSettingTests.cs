using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MindAttic.Legion;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests.Lamp;

/// <summary>
/// LAMP (TT-T27): one test per ThinkTank setting, each end to end — the value in the pre-LAMP
/// Settings.json imports into the right property, is saved in the Vault settings.json under its manifest
/// name and reloads unchanged, and is the value the code that consumes it actually uses (down to the
/// HTTP request sent to the provider where the setting shapes it).
/// </summary>
[TestFixture, NonParallelizable]
public class ThinkTankSettingTests
{
    private static readonly string Prefix = SettingsManifest.PrefixFor(ThinkTankSettings.AppId);

    // ---- helpers ------------------------------------------------------------------------

    /// <summary>Writes a pre-LAMP Settings.json with <paramref name="fields"/>, imports it into a fresh Vault store.</summary>
    private static JsonSettingsStore<ThinkTankSettings> Import(JsonObject fields)
    {
        var legacy = Path.Combine(TestAssemblySetup.VaultRoot, "legacy-" + Guid.NewGuid().ToString("N"), "Settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        fields["Templates"] ??= new JsonArray();
        fields["Conversations"] ??= new JsonArray();
        File.WriteAllText(legacy, fields.ToJsonString());
        var store = TestLamp.Store();
        ThinkTankSettings.Load(configuration: null, store: store, legacyPath: legacy);
        return store;
    }

    /// <summary>The value sits in settings.json at <paramref name="path"/>, its manifest name matches, and a reload returns it.</summary>
    private static ThinkTankSettings AssertStoredUnderManifestName(JsonSettingsStore<ThinkTankSettings> store, string path, JsonNode expected)
    {
        var name = $"{Prefix}:{path}";
        Assert.That(SettingsManifestBuilder.Build<ThinkTankSettings>().Find(name), Is.Not.Null, $"manifest has {name}");
        JsonNode? node = JsonNode.Parse(File.ReadAllText(store.FilePath));
        foreach (var part in path.Split(':')) node = node?[part];
        Assert.That(node?.ToJsonString(), Is.EqualTo(expected.ToJsonString()), $"settings.json {path}");
        return store.Load();
    }

    private static SettingsService Service(JsonSettingsStore<ThinkTankSettings> store, params (string Provider, string Key)[] keys) =>
        new(new ThinkTankSettingsSource(store), TestLamp.KeysWith(keys));

    /// <summary>Records every request (URL + body) and answers in OpenAI's shape.</summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<(Uri Url, string Body, string? Key)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var key = request.Headers.TryGetValues("x-api-key", out var k) ? k.Single()
                : request.Headers.Authorization?.Parameter
                ?? (request.Headers.TryGetValues("x-goog-api-key", out var g) ? g.Single() : null);
            Requests.Add((request.RequestUri!, body, key));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"ok"}}],"content":[{"type":"text","text":"ok"}],"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (ThinkTankService Service, CaptureHandler Http) Wire(SettingsService settings)
    {
        var http = new CaptureHandler();
        var psychometrics = new PsychometricProfileService(Path.Combine(TestAssemblySetup.VaultRoot, "psy-" + Guid.NewGuid().ToString("N")));
        return (new ThinkTankService(new LegionClient(new HttpClient(http)), settings, psychometrics), http);
    }

    private static async Task CallQuietly(Func<Task> call)
    {
        try { await call(); }
        catch { /* only the outgoing request matters here */ }
    }

    // ---- guard --------------------------------------------------------------------------

    [Test]
    public void EverySettingHasItsOwnTest()
    {
        var manifest = SettingsManifestBuilder.Build<ThinkTankSettings>();
        var tested = GetType().GetMethods().Select(m => m.Name).Where(n => n.StartsWith("Setting_")).ToList();
        foreach (var field in manifest.Fields.Where(f => f.Kind != SettingKind.Object))
        {
            var pascal = char.ToUpperInvariant(field.Property[0]) + field.Property[1..];
            Assert.That(tested.Count(t => t.StartsWith("Setting_" + pascal + "_")), Is.EqualTo(1), field.Property);
        }
        Assert.That(manifest.Fields.Count(f => f.Kind != SettingKind.Object), Is.EqualTo(9));

        // Provider leaves each get their own case of ProviderSetting_ImportsRoundTripsAndShapesTheRequest.
        var leaves = manifest.AllFields().Where(f => f.Name.StartsWith($"{Prefix}:providers:") && f.Kind != SettingKind.Object).Select(f => f.Name).ToList();
        Assert.That(leaves, Has.Count.EqualTo(10));
        Assert.That(ProviderLeaves().Select(c => (string)c.Arguments[0]!), Is.EquivalentTo(leaves));
    }

    // ---- Appearance ---------------------------------------------------------------------

    [Test]
    public void Setting_Theme_ImportsRoundTripsAndIsTheAppliedTheme()
    {
        var store = Import(new JsonObject { ["AppearanceTheme"] = "dracula" });
        Assert.That(AssertStoredUnderManifestName(store, "theme", "dracula").Theme, Is.EqualTo("dracula"));
        Assert.That(new AppearanceService(Service(store)).Mode, Is.EqualTo(AppearanceMode.Dracula));
    }

    [Test]
    public void Setting_ControlHeight_ImportsRoundTripsAndIsTheAppliedHeight()
    {
        var store = Import(new JsonObject { ["ControlHeight"] = 50 });
        Assert.That(AssertStoredUnderManifestName(store, "controlHeight", 50).ControlHeight, Is.EqualTo(50));
        Assert.That(new AppearanceService(Service(store)).ControlHeight, Is.EqualTo(50));
    }

    [Test]
    public void Setting_Gutter_ImportsRoundTripsAndIsTheAppliedGutter()
    {
        var store = Import(new JsonObject { ["Gutter"] = 20 });
        Assert.That(AssertStoredUnderManifestName(store, "gutter", 20).Gutter, Is.EqualTo(20));
        Assert.That(new AppearanceService(Service(store)).Gutter, Is.EqualTo(20));
    }

    [Test]
    public void Setting_BorderRadius_ImportsRoundTripsAndIsTheAppliedRadius()
    {
        var store = Import(new JsonObject { ["BorderRadius"] = 16 });
        Assert.That(AssertStoredUnderManifestName(store, "borderRadius", 16).BorderRadius, Is.EqualTo(16));
        Assert.That(new AppearanceService(Service(store)).BorderRadius, Is.EqualTo(16));
    }

    // ---- Conversation defaults ---------------------------------------------------------

    [Test]
    public async Task Setting_GlobalMaxTokens_ImportsRoundTripsAndCapsTheRequest()
    {
        var store = Import(new JsonObject { ["GlobalMaxTokens"] = 4096 });
        Assert.That(AssertStoredUnderManifestName(store, "globalMaxTokens", 4096).GlobalMaxTokens, Is.EqualTo(4096));

        var settings = Service(store, ("openai", "sk-o"));
        var (svc, http) = Wire(settings);
        // Chat.razor passes `active.MaxTokens ?? Settings.GlobalMaxTokens` as the per-call cap.
        await CallQuietly(() => svc.CallProvider("openai", "", null, "topic", new List<SharedTurn>(), settings.GlobalMaxTokens));
        Assert.That(http.Requests.Single().Body, Does.Contain("4096"));
    }

    [Test]
    public void Setting_GlobalMaxRounds_ImportsRoundTripsAndIsTheConversationDefault()
    {
        var store = Import(new JsonObject { ["GlobalMaxRounds"] = 5 });
        Assert.That(AssertStoredUnderManifestName(store, "globalMaxRounds", 5).GlobalMaxRounds, Is.EqualTo(5));
        Assert.That(Service(store).GlobalMaxRounds, Is.EqualTo(5));
        Assert.That(File.ReadAllText(Path.Combine(RepoRoot, "ThinkTank.Shared", "Components", "Pages", "Chat.razor")),
            Does.Contain("active.MaxRounds ?? Settings.GlobalMaxRounds"), "the chat loop runs that many rounds unless the conversation overrides it");
    }

    [Test]
    public async Task Setting_GlobalMaxContextTurns_ImportsRoundTripsAndTrimsTheHistory()
    {
        var store = Import(new JsonObject { ["GlobalMaxContextTurns"] = 3 });
        Assert.That(AssertStoredUnderManifestName(store, "globalMaxContextTurns", 3).GlobalMaxContextTurns, Is.EqualTo(3));

        var (svc, _) = Wire(Service(store, ("openai", "sk-o")));
        var diagnostics = new List<string>();
        svc.Diagnostics += (_, payload, _) => diagnostics.Add(payload);
        var history = Enumerable.Range(0, 6).Select(i => new SharedTurn { ModelId = "x", Text = $"turn {i}" }).ToList();
        await CallQuietly(() => svc.CallProvider("openai", "", null, "topic", history));
        Assert.That(diagnostics, Has.Some.Contains("context truncated: 6 turns").And.Some.Matches<string>(d => d.Contains("context truncated") && d.Contains(" 3")));
    }

    [Test]
    public async Task Setting_GlobalResponseLength_ImportsRoundTripsAndInstructsEveryParticipant()
    {
        var store = Import(new JsonObject { ["GlobalResponseLength"] = "Detailed" });
        Assert.That(AssertStoredUnderManifestName(store, "globalResponseLength", "detailed").GlobalResponseLength, Is.EqualTo("detailed"));

        var settings = Service(store, ("openai", "sk-o"));
        var (svc, http) = Wire(settings);
        await CallQuietly(() => svc.CallProvider("openai", "", null, "topic", new List<SharedTurn>(), responseLength: settings.GlobalResponseLength));
        var instruction = JsonEncodedText.Encode(ResponseLengthPreset.InstructionFor("detailed").Trim()).ToString();
        Assert.That(http.Requests.Single().Body, Does.Contain(instruction[..Math.Min(40, instruction.Length)]));
    }

    [Test]
    public async Task Setting_ClaudeFallbackMode_ImportsRoundTripsAndRoutesEveryoneThroughClaude()
    {
        var store = Import(new JsonObject { ["ClaudeFallbackMode"] = true });
        Assert.That(AssertStoredUnderManifestName(store, "claudeFallbackMode", true).ClaudeFallbackMode, Is.True);

        var settings = Service(store, ("claude", "sk-ant"), ("openai", "sk-o"));
        var (svc, http) = Wire(settings);
        await CallQuietly(() => svc.CallProvider("openai", "persona", null, "topic", new List<SharedTurn>()));
        Assert.That(http.Requests.Single().Url.Host, Is.EqualTo("api.anthropic.com"));
        Assert.That(http.Requests.Single().Key, Is.EqualTo("sk-ant"));

        settings.SetClaudeFallbackMode(false);   // the chat page's switch writes through the store
        Assert.That(store.Load().ClaudeFallbackMode, Is.False);
    }

    // ---- Providers (one case per leaf) ---------------------------------------------------

    public static IEnumerable<TestCaseData> ProviderLeaves() =>
        from provider in new[] { ("openAI", "openai"), ("claude", "claude"), ("gemini", "gemini"), ("deepSeek", "deepseek"), ("kimi", "kimi") }
        from leaf in new[] { "model", "maxTokens" }
        let name = $"{Prefix}:providers:{provider.Item1}:{leaf}"
        select new TestCaseData(name, provider.Item2).SetName($"ProviderSetting_ImportsRoundTripsAndShapesTheRequest(providers:{provider.Item1}:{leaf})");

    [TestCaseSource(nameof(ProviderLeaves))]
    public async Task ProviderSetting_ImportsRoundTripsAndShapesTheRequest(string fullName, string providerId)
    {
        var leaf = fullName.Split(':')[^1];
        var oldJson = leaf == "model"
            ? $$"""{"type":"{{ProviderModels.TypeFor(providerId)}}","model":"{{providerId}}-test-model","maxTokens":2048}"""
            : $$"""{"type":"{{ProviderModels.TypeFor(providerId)}}","model":"m","maxTokens":3333}""";
        var store = Import(new JsonObject { ["ProviderAuth"] = new JsonObject { [providerId] = oldJson } });
        JsonNode expected = leaf == "model" ? $"{providerId}-test-model" : 3333;
        var loaded = AssertStoredUnderManifestName(store, fullName[(Prefix.Length + 1)..], expected);
        var p = loaded.Providers.For(providerId)!;
        Assert.That(leaf == "model" ? (object)p.Model : p.MaxTokens, Is.EqualTo(leaf == "model" ? (object)$"{providerId}-test-model" : 3333));

        // The provider config ThinkTankService and VotingService read, and the request actually sent.
        var settings = Service(store, (providerId, "sk-" + providerId));
        using (var doc = JsonDocument.Parse(settings.GetAuthJson(providerId)))
            Assert.That(doc.RootElement.GetProperty(leaf).ToString(), Is.EqualTo(expected.ToString()));
        var (svc, http) = Wire(settings);
        await CallQuietly(() => svc.CallProvider(providerId, "", null, "topic", new List<SharedTurn>()));
        var sent = http.Requests.Single();
        Assert.That(sent.Url + "\n" + sent.Body, Does.Contain(expected.ToString()), "the request carries the setting");
    }

    // ---- repo root ------------------------------------------------------------------------

    internal static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThinkTank.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("ThinkTank.slnx not found above the test output.");
        }
    }
}
