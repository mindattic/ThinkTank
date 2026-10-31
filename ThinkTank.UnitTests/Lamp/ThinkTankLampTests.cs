using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using MindAttic.Legion;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Launch;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests.Lamp;

/// <summary>
/// LAMP (TT-T27) app-level checks: manifest, first-run defaults, the one-time import (old file untouched,
/// its content still loaded), deployed configuration, LLM key failover through Vault's shared named lists,
/// the app-scoped key import, the Settings cog, and the old settings screens being gone.
/// </summary>
[TestFixture, NonParallelizable]
public class ThinkTankLampTests
{
    private static readonly string Prefix = SettingsManifest.PrefixFor(ThinkTankSettings.AppId);
    private static string RepoRoot => ThinkTankSettingTests.RepoRoot;

    private static string NewDir(string kind) =>
        Directory.CreateDirectory(Path.Combine(TestAssemblySetup.VaultRoot, kind + "-" + Guid.NewGuid().ToString("N"))).FullName;

    // ---- Manifest / defaults --------------------------------------------------------------

    [Test]
    public void Manifest_builds_with_every_field_labeled_grouped_and_helped()
    {
        var m = SettingsManifestBuilder.Build<ThinkTankSettings>();
        Assert.Multiple(() =>
        {
            Assert.That(m.AppId, Is.EqualTo("ThinkTank"));
            Assert.That(m.ConfigurationPrefix, Is.EqualTo(Prefix));
            foreach (var f in m.Fields)
            {
                Assert.That(f.Help, Is.Not.Null.And.Not.Empty, f.Name);
                Assert.That(f.Name, Is.EqualTo($"{Prefix}:{f.Property}"));
            }
            foreach (var f in m.AllFields())
            {
                Assert.That(f.Label, Is.Not.Empty, f.Name);
                Assert.That(f.Kind, Is.Not.EqualTo(SettingKind.Unknown), f.Name);
            }
        });
        Assert.That(m.Fields.Select(f => f.Group).Distinct(), Is.EquivalentTo(new[] { "Appearance", "Conversation defaults", "Providers" }));
    }

    [Test]
    public void Manifest_has_no_secret_fields_and_declares_the_providers_ThinkTank_calls()
    {
        var m = SettingsManifestBuilder.Build<ThinkTankSettings>();
        Assert.That(m.AllFields().Where(f => new[] { "key", "secret", "token", "password" }
            .Any(w => f.Property.Contains(w, StringComparison.OrdinalIgnoreCase) && !f.Property.EndsWith("Tokens", StringComparison.OrdinalIgnoreCase))), Is.Empty);
        Assert.That(m.Credentials.Select(c => c.ProviderId), Is.SupersetOf(new[] { "claude", "openai", "gemini", "deepseek", "kimi", "grok" }));
    }

    [Test]
    public void LoadOrSeed_on_an_empty_folder_seeds_exactly_todays_defaults()
    {
        var store = TestLamp.Store();
        ThinkTankSettings.Load(null, store, Path.Combine(NewDir("none"), "Settings.json"));
        var r = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
        Assert.Multiple(() =>
        {
            Assert.That((string)r["theme"]!, Is.EqualTo("dark"));
            Assert.That((int)r["controlHeight"]!, Is.EqualTo(40));
            Assert.That((int)r["gutter"]!, Is.EqualTo(10));
            Assert.That((int)r["borderRadius"]!, Is.EqualTo(10));
            Assert.That((int)r["globalMaxTokens"]!, Is.EqualTo(2048));
            Assert.That((int)r["globalMaxRounds"]!, Is.EqualTo(10));
            Assert.That((int)r["globalMaxContextTurns"]!, Is.EqualTo(8));
            Assert.That((string)r["globalResponseLength"]!, Is.EqualTo("concise"));
            Assert.That((bool)r["claudeFallbackMode"]!, Is.False);
            // What a fresh Settings.json carried (LoadOrInit), plus kimi as the next load backfilled it.
            Assert.That((string)r["providers"]!["openAI"]!["model"]!, Is.EqualTo("gpt-4.1-mini"));
            Assert.That((string)r["providers"]!["claude"]!["model"]!, Is.EqualTo("claude-sonnet-4-6"));
            Assert.That((string)r["providers"]!["gemini"]!["model"]!, Is.EqualTo("gemini-2.5-flash"));
            Assert.That((string)r["providers"]!["deepSeek"]!["model"]!, Is.EqualTo("deepseek-chat"));
            Assert.That((string)r["providers"]!["kimi"]!["model"]!, Is.EqualTo("kimi-k2"));
            Assert.That((int)r["providers"]!["openAI"]!["maxTokens"]!, Is.EqualTo(2048));
        });
        Assert.That(File.Exists(Path.Combine(store.Directory, SettingsManifest.FileName)), Is.True);
    }

    [Test]
    public void Settings_live_in_the_roaming_ThinkTank_folder_not_beside_the_local_Settings_json()
    {
        var store = JsonSettingsStore<ThinkTankSettings>.ForSettingsApp();
        Assert.That(store.Directory, Is.EqualTo(VaultPaths.RoamingBucket("ThinkTank")));
        Assert.That(ThinkTankSettings.LegacySettingsPath, Is.EqualTo(Path.Combine(VaultPaths.LocalApp("ThinkTank"), "Settings.json")));
        Assert.That(string.Equals(store.FilePath, ThinkTankSettings.LegacySettingsPath, StringComparison.OrdinalIgnoreCase), Is.False,
            "settings.json must never be the content file (same name on Windows)");
    }

    [Test]
    public void Write_settings_manifest_switch_seeds_and_exits()
    {
        var output = new StringWriter();
        Assert.That(SettingsManifestCommand.TryRun<ThinkTankSettings>(new[] { "--write-settings-manifest" }, output), Is.True);
        Assert.That(File.Exists(output.ToString().Trim()), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(RepoRoot, "ThinkTank.Blazor", "Program.cs")),
            Does.Contain("SettingsManifestCommand.TryRun<ThinkTankSettings>(args)"));
    }

    // ---- Import / content file -------------------------------------------------------------

    [Test]
    public void Import_never_changes_the_old_file_and_content_saves_carry_its_old_settings_through()
    {
        var legacy = ThinkTankSettings.LegacySettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, """
            {"ProviderAuth":{"openai":"{\"type\":\"bearer\",\"model\":\"gpt-x\",\"maxTokens\":2048}"},
             "Templates":[],"Conversations":[],"AppearanceTheme":"neon","ControlHeight":50,"GlobalMaxRounds":5}
            """);
        var before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacy)));

        var store = TestLamp.Store();
        var imported = ThinkTankSettings.Load(null, store, legacy);
        Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(legacy))), Is.EqualTo(before), "import only reads");
        Assert.That(imported.Theme, Is.EqualTo("neon"));
        Assert.That(imported.Providers.OpenAI.Model, Is.EqualTo("gpt-x"));

        store.Update(s => s.Theme = "mono");
        Assert.That(ThinkTankSettings.Load(null, store, legacy).Theme, Is.EqualTo("mono"), "the import runs once");

        // ThinkTank still owns templates/conversations in that file; saving them keeps the old fields as they were.
        var svc = new SettingsService(new ThinkTankSettingsSource(store), TestLamp.Keys());
        svc.SetConversations(Array.Empty<ThinkTank.Core.Models.PersistedConversation>());
        var after = JsonNode.Parse(File.ReadAllText(legacy))!;
        Assert.That((string)after["AppearanceTheme"]!, Is.EqualTo("neon"));
        Assert.That((int)after["ControlHeight"]!, Is.EqualTo(50));
        Assert.That((int)after["GlobalMaxRounds"]!, Is.EqualTo(5));
        Assert.That((string)after["ProviderAuth"]!["openai"]!, Does.Contain("gpt-x"));
        Assert.That(svc.Templates, Is.Not.Empty, "default persona templates still seeded");
        File.Delete(legacy);
    }

    [Test]
    public void Deployed_configuration_wins_over_the_local_file()
    {
        var store = TestLamp.Store();
        store.Save(new ThinkTankSettings { Theme = "dark" });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{Prefix}:theme"] = "ocean",
            [$"{Prefix}:providers:claude:model"] = "claude-x",
        }).Build();
        var source = new ThinkTankSettingsSource(store, config);
        Assert.That(source.Current.Theme, Is.EqualTo("ocean"));
        Assert.That(source.Current.Providers.Claude.Model, Is.EqualTo("claude-x"));
        source.Update(s => s.ClaudeFallbackMode = true);
        Assert.That(store.Load().ClaudeFallbackMode, Is.False, "deployed values are read-only");
    }

    // ---- Keys --------------------------------------------------------------------------------

    /// <summary>Answers OpenAI's endpoint by bearer key: "sk-bad" gets 401, anything else 200.</summary>
    private sealed class KeyedHandler : HttpMessageHandler
    {
        public List<string> KeysSeen { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = request.Headers.Authorization?.Parameter ?? "";
            KeysSeen.Add(key);
            return Task.FromResult(key == "sk-bad"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"error":{"message":"bad key"}}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"choices":[{"message":{"content":"hi"}}]}""", Encoding.UTF8, "application/json") });
        }
    }

    private static ThinkTankService Wire(SettingsService settings, HttpMessageHandler http) =>
        new(new LegionClient(new HttpClient(http)), settings,
            new PsychometricProfileService(Path.Combine(TestAssemblySetup.VaultRoot, "psy-" + Guid.NewGuid().ToString("N"))));

    [Test]
    public async Task Llm_call_fails_over_to_the_second_key_when_the_first_is_rejected_and_flags_the_first()
    {
        var store = TestLamp.KeyStore();
        store.Add("openai", "first", "sk-bad");
        store.Add("openai", "second", "sk-good");
        var health = new KeyHealthStore(NewDir("health"));
        var settings = new SettingsService(TestLamp.Settings(), TestLamp.Keys(store, health));
        var http = new KeyedHandler();

        var reply = await Wire(settings, http).CallProvider("openai", "", null, "hello", new List<SharedTurn>());

        Assert.That(reply, Is.EqualTo("hi"));
        Assert.That(http.KeysSeen.Distinct(), Is.EqualTo(new[] { "sk-bad", "sk-good" }));
        Assert.That(health.Get("openai", "first").Failed, Is.True, "the refused key is flagged for Vault.Desktop");
        Assert.That(settings.GetKeyForProvider("openai", null), Is.EqualTo("sk-good"), "voting skips the flagged key too");
    }

    [Test]
    public async Task Llm_call_uses_ThinkTanks_assigned_key_first()
    {
        var store = TestLamp.KeyStore();
        store.Add("openai", "shared", "sk-shared");
        store.Add("openai", "mine", "sk-mine");
        store.Assignments.Set("ThinkTank", "openai", new[] { "mine", "shared" });
        var http = new KeyedHandler();

        await Wire(new SettingsService(TestLamp.Settings(), TestLamp.Keys(store)), http).CallProvider("openai", "", null, "hello", new List<SharedTurn>());

        Assert.That(http.KeysSeen, Is.EqualTo(new[] { "sk-mine" }));
    }

    [Test]
    public async Task A_personas_own_override_key_is_used_as_is()
    {
        var http = new KeyedHandler();
        await Wire(TestLamp.Service(keys: TestLamp.KeysWith(("openai", "sk-vault"))), http)
            .CallProvider("openai", "", """{"apiKey":"sk-persona"}""", "hello", new List<SharedTurn>());
        Assert.That(http.KeysSeen, Is.EqualTo(new[] { "sk-persona" }));
    }

    [Test]
    public void Llm_call_with_no_key_says_to_add_one_in_Vault()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
            Wire(TestLamp.Service(), new KeyedHandler()).CallProvider("openai", "", null, "hello", new List<SharedTurn>()));
        Assert.That(ex!.Message, Does.Contain("No API key configured for provider 'openai'").And.Contain("MindAttic Vault"));
    }

    [Test]
    public void Legion_errors_wrapping_a_401_still_count_as_a_failed_key()
    {
        var wrapped = new InvalidOperationException("breaker", new HttpRequestException("401", null, HttpStatusCode.Unauthorized));
        Assert.That(ThinkTankKeys.ClassifyLegionFailure(wrapped)?.Kind, Is.EqualTo(KeyFailureKind.Auth));
        Assert.That(ThinkTankKeys.ClassifyLegionFailure(new InvalidOperationException("other")), Is.Null);
    }

    [Test]
    public void Startup_import_moves_an_old_thinktank_scoped_key_into_the_shared_list_as_ThinkTanks_active_key()
    {
        var root = NewDir("import");
        NamedKeyStore Keys() => new(root, new KeyAssignmentStore(Path.Combine(root, KeyAssignmentStore.Bucket)));
#pragma warning disable CS0618 // the pre-LAMP per-app copy is exactly what this test recreates
        new AppScopedCredentialStore("thinktank", new LlmCredentialStore(Path.Combine(root, LlmCredentialStore.Bucket)))
            .SetKey("claude", "sk-thinktank-own");
#pragma warning restore CS0618
        Keys().Add("claude", "shared", "sk-shared");

        KeyImporter.ImportAppScopedFor(ThinkTankSettings.AppId, Keys());
        var second = KeyImporter.ImportAppScopedFor(ThinkTankSettings.AppId, Keys());

        Assert.That(TestLamp.Keys(Keys()).GetActiveKey("claude", "ThinkTank")?.Value, Is.EqualTo("sk-thinktank-own"));
        Assert.That(second.Added, Is.Empty);
        Assert.That(File.ReadAllText(Path.Combine(RepoRoot, "ThinkTank.Blazor", "Program.cs")),
            Does.Contain("KeyImporter.ImportAppScopedFor(ThinkTankSettings.AppId)"));
    }

    [Test]
    public void No_per_app_key_store_or_key_editor_is_left_in_ThinkTank()
    {
        var offenders = new[] { "ThinkTank.Core", "ThinkTank.Blazor", "ThinkTank.Shared" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(RepoRoot, p), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f) is var t && (t.Contains("AppScopedCredentialStore") || t.Contains("SetProviderKey")
                        || t.Contains("RuntimeApiKeyOverrides") || t.Contains("type=\"password\"")))
            .Select(Path.GetFileName);
        Assert.That(offenders, Is.Empty);
    }

    // ---- Cog / old UI --------------------------------------------------------------------------

    private sealed class FakeLauncher : IVaultDesktopLauncher
    {
        public bool Running { get; init; } = true;
        public List<VaultOpenRequest> Requests { get; } = new();
        public bool TrySend(VaultOpenRequest request) { Requests.Add(request); return Running; }
        public bool TryStart(VaultOpenRequest request) => false;
    }

    private static T WithLauncher<T>(FakeLauncher fake, Func<T> body)
    {
        var original = VaultSettings.Launcher;
        VaultSettings.Launcher = fake;
        try { return body(); }
        finally { VaultSettings.Launcher = original; }
    }

    [Test]
    public void Settings_cog_opens_Vault_at_the_ThinkTank_tab()
    {
        var fake = new FakeLauncher();
        var result = WithLauncher(fake, () => SettingsCog.Open());
        Assert.That(result.Opened, Is.True);
        Assert.That(fake.Requests.Single().AppId, Is.EqualTo("ThinkTank"));
    }

    [Test]
    public void Settings_cog_can_deep_link_to_one_setting()
    {
        var fake = new FakeLauncher();
        WithLauncher(fake, () => SettingsCog.Open($"{Prefix}:globalMaxRounds"));
        Assert.That(fake.Requests.Single().Setting, Is.EqualTo($"{Prefix}:globalMaxRounds"));
    }

    [Test]
    public void Settings_cog_explains_when_Vault_is_not_installed()
    {
        var result = WithLauncher(new FakeLauncher { Running = false }, () => SettingsCog.Open());
        Assert.That(result.Opened, Is.False);
        Assert.That(result.Message, Does.Contain("isn't installed"));
    }

    [Test]
    public void Nav_menu_cog_calls_VaultSettings_from_the_server()
    {
        var markup = File.ReadAllText(Path.Combine(RepoRoot, "ThinkTank.Shared", "Components", "Layout", "NavMenu.razor"));
        Assert.That(markup, Does.Contain("SettingsCog.Open()"));
        Assert.That(markup, Does.Contain("SettingsCog.Link()"));
        Assert.That(markup, Does.Not.Contain("href=\"settings\""));
    }

    [Test]
    public void Old_settings_screens_are_gone()
    {
        var shared = typeof(ThinkTank.Shared.Components.Pages.Home).Assembly;
        var routes = shared.GetTypes().SelectMany(t => t.GetCustomAttributes<RouteAttribute>()).Select(r => r.Template).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(routes, Does.Not.Contain("/settings"));
            Assert.That(routes, Does.Contain("/personas"), "persona editing (content) stays in ThinkTank");
            Assert.That(shared.GetType("ThinkTank.Shared.Components.Pages.SettingsAppearance"), Is.Null);
            Assert.That(shared.GetType("ThinkTank.Shared.Components.Pages.Settings"), Is.Null);
            Assert.That(File.Exists(Path.Combine(RepoRoot, "ThinkTank.Shared", "Components", "Pages", "SettingsAppearance.razor")), Is.False);
        });
        var personas = File.ReadAllText(Path.Combine(RepoRoot, "ThinkTank.Shared", "Components", "Pages", "Personas.razor"));
        Assert.That(personas, Does.Not.Contain("panel-defaults").And.Not.Contain("panel-appearance").And.Not.Contain("SaveProviderKey"));
    }
}
