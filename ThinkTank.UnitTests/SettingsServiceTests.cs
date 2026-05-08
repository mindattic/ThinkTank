using MindAttic.Legion;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

[TestFixture]
public class SettingsServiceTests
{
    private SettingsService sut = null!;

    [SetUp]
    public void SetUp()
    {
        // Wipe the assembly-level sandbox between tests so each starts with a fresh shared store.
        var sandbox = MindAtticCredentialStore.CredentialDirectory;
        if (Directory.Exists(sandbox))
        {
            foreach (var f in Directory.EnumerateFiles(sandbox))
                File.Delete(f);
        }

        sut = new SettingsService();
    }

    // ── GetAuthJson ─────────────────────────────────────────────────────

    [Test]
    public void GetAuthJson_KnownProvider_ReturnsJson()
    {
        var json = sut.GetAuthJson("openai");
        Assert.That(json, Does.Contain("apiKey"));
    }

    [Test]
    public void GetAuthJson_UnknownProvider_ReturnsEmptyObject()
    {
        var json = sut.GetAuthJson("nonexistent_provider");
        Assert.That(json, Is.EqualTo("{}"));
    }

    [Test]
    public void GetAuthJson_AllDefaultProviders_ReturnValidJson()
    {
        var providers = new[] { "openai", "claude", "gemini", "deepseek" };
        foreach (var p in providers)
        {
            var json = sut.GetAuthJson(p);
            Assert.DoesNotThrow(() => System.Text.Json.JsonDocument.Parse(json), $"Invalid JSON for provider {p}");
        }
    }

    // ── GetKeyForProvider ───────────────────────────────────────────────

    [Test]
    public void GetKeyForProvider_WithOverride_ReturnsOverride()
    {
        var key = sut.GetKeyForProvider("openai", "my-override-key");
        Assert.That(key, Is.EqualTo("my-override-key"));
    }

    [Test]
    public void GetKeyForProvider_NullOverride_ExtractsFromAuthJson()
    {
        sut.ProviderAuth["testprov"] = new ProviderAuthConfig("testprov",
            "{\"type\":\"bearer\",\"apiKey\":\"sk-test-123\"}");

        var key = sut.GetKeyForProvider("testprov", null);
        Assert.That(key, Is.EqualTo("sk-test-123"));
    }

    [Test]
    public void GetKeyForProvider_EmptyOverride_ExtractsFromAuthJson()
    {
        sut.ProviderAuth["testprov"] = new ProviderAuthConfig("testprov",
            "{\"type\":\"bearer\",\"apiKey\":\"sk-test-456\"}");

        var key = sut.GetKeyForProvider("testprov", "");
        Assert.That(key, Is.EqualTo("sk-test-456"));
    }

    [Test]
    public void GetKeyForProvider_MissingProvider_ReturnsEmpty()
    {
        var key = sut.GetKeyForProvider("nonexistent", null);
        Assert.That(key, Is.EqualTo(""));
    }

    [Test]
    public void GetKeyForProvider_MalformedJson_ReturnsEmpty()
    {
        sut.ProviderAuth["bad"] = new ProviderAuthConfig("bad", "not json");
        var key = sut.GetKeyForProvider("bad", null);
        Assert.That(key, Is.EqualTo(""));
    }

    // ── ProviderAuth initialization ─────────────────────────────────────

    [Test]
    public void ProviderAuth_Has4Providers()
    {
        Assert.That(sut.ProviderAuth.Count, Is.EqualTo(4));
    }

    [TestCase("openai")]
    [TestCase("claude")]
    [TestCase("gemini")]
    [TestCase("deepseek")]
    public void ProviderAuth_ContainsExpectedProvider(string providerId)
    {
        Assert.That(sut.ProviderAuth.ContainsKey(providerId), Is.True);
    }

    [Test]
    public void ProviderAuth_AllContainMaxTokens()
    {
        foreach (var (providerId, cfg) in sut.ProviderAuth)
        {
            var doc = System.Text.Json.JsonDocument.Parse(cfg.Json);
            Assert.That(doc.RootElement.TryGetProperty("maxTokens", out _), Is.True,
                $"Provider {providerId} missing maxTokens");
        }
    }

    [Test]
    public void ProviderAuth_AllContainModel()
    {
        foreach (var (providerId, cfg) in sut.ProviderAuth)
        {
            var doc = System.Text.Json.JsonDocument.Parse(cfg.Json);
            Assert.That(doc.RootElement.TryGetProperty("model", out _), Is.True,
                $"Provider {providerId} missing model");
        }
    }

    // ── Templates ───────────────────────────────────────────────────────

    [Test]
    public void Templates_HasAtLeast4Templates()
    {
        Assert.That(sut.Templates.Count, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void Templates_ContainsDefaultTemplatesForAllProviders()
    {
        Assert.That(sut.Templates.Count(t => t.IsDefault), Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void Templates_AllHaveUniqueIds()
    {
        var ids = sut.Templates.Select(t => t.TemplateId).ToHashSet();
        Assert.That(ids.Count, Is.EqualTo(sut.Templates.Count));
    }

    [Test]
    public void Templates_CoverAllProviders()
    {
        var providers = sut.Templates.Select(t => t.ProviderId).ToHashSet();
        var expected = new[] { "openai", "claude", "gemini", "deepseek" };
        foreach (var p in expected)
            Assert.That(providers.Contains(p), Is.True, $"Missing template for provider: {p}");
    }

    // ── Default settings (loaded from disk or initialized) ────────────

    [Test]
    public void AppearanceTheme_IsNotNullOrEmpty()
    {
        Assert.That(sut.AppearanceTheme, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GlobalMaxTokens_HasValue()
    {
        Assert.That(sut.GlobalMaxTokens, Is.Not.Null);
        Assert.That(sut.GlobalMaxTokens, Is.GreaterThan(0));
    }

    [Test]
    public void GlobalMaxRounds_HasValue()
    {
        Assert.That(sut.GlobalMaxRounds, Is.Not.Null);
        Assert.That(sut.GlobalMaxRounds, Is.GreaterThan(0));
    }

    [Test]
    public void ControlHeight_HasValue()
    {
        Assert.That(sut.ControlHeight, Is.Not.Null);
        Assert.That(sut.ControlHeight, Is.GreaterThanOrEqualTo(28));
    }

    [Test]
    public void Gutter_HasValue()
    {
        Assert.That(sut.Gutter, Is.Not.Null);
        Assert.That(sut.Gutter, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void BorderRadius_HasValue()
    {
        Assert.That(sut.BorderRadius, Is.Not.Null);
        Assert.That(sut.BorderRadius, Is.GreaterThanOrEqualTo(0));
    }

    // ── SetAuthJson / GetAuthJson roundtrip ─────────────────────────────

    [Test]
    public void SetAuthJson_ThenGetAuthJson_Roundtrips()
    {
        var json = "{\"type\":\"bearer\",\"apiKey\":\"test-key\",\"model\":\"gpt-4\"}";
        sut.SetAuthJson("testprov_roundtrip", json);

        var result = sut.GetAuthJson("testprov_roundtrip");
        Assert.That(result, Is.EqualTo(json));

        // Cleanup
        sut.ProviderAuth.Remove("testprov_roundtrip");
    }

    // ── SetKey ──────────────────────────────────────────────────────────

    [Test]
    public void SetKey_UpdatesApiKeyInAuthJson()
    {
        sut.SetKey("openai", "sk-new-key-123");

        var json = sut.GetAuthJson("openai");
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var apiKey = doc.RootElement.GetProperty("apiKey").GetString();
        Assert.That(apiKey, Is.EqualTo("sk-new-key-123"));
    }

    [Test]
    public void SetKey_PreservesModelSetting()
    {
        var originalJson = sut.GetAuthJson("openai");
        var originalDoc = System.Text.Json.JsonDocument.Parse(originalJson);
        var originalModel = originalDoc.RootElement.GetProperty("model").GetString();

        sut.SetKey("openai", "new-key");

        var updatedJson = sut.GetAuthJson("openai");
        var updatedDoc = System.Text.Json.JsonDocument.Parse(updatedJson);
        var updatedModel = updatedDoc.RootElement.GetProperty("model").GetString();

        Assert.That(updatedModel, Is.EqualTo(originalModel));
    }

    [Test]
    public void SetKey_PreservesTypeSetting()
    {
        sut.SetKey("claude", "new-key");

        var json = sut.GetAuthJson("claude");
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString();
        Assert.That(type, Is.EqualTo("anthropic"));
    }

    // ── ResetProvidersToDefaults ────────────────────────────────────────

    [Test]
    public void ResetProvidersToDefaults_EmptyDefaults_IsNoOp()
    {
        Assert.That(sut.ProviderDefaults.Count, Is.EqualTo(0));
        var beforeCount = sut.ProviderAuth.Count;

        sut.ResetProvidersToDefaults();

        Assert.That(sut.ProviderAuth.Count, Is.EqualTo(beforeCount));
    }

    [Test]
    public void ResetProvidersToDefaults_AppliesDefaults()
    {
        sut.ProviderDefaults["openai"] = new ProviderAuthConfig("openai",
            "{\"type\":\"bearer\",\"apiKey\":\"default-key\",\"model\":\"gpt-4\"}");

        sut.ResetProvidersToDefaults();

        var json = sut.GetAuthJson("openai");
        Assert.That(json, Does.Contain("default-key"));
    }

    // ── SetConversations ────────────────────────────────────────────────

    [Test]
    public void SetConversations_ReplacesAll()
    {
        var convos = new List<PersistedConversation>
        {
            new("id1", "Title1", new List<PersistedParticipant>(), null, null),
            new("id2", "Title2", new List<PersistedParticipant>(), null, null)
        };

        sut.SetConversations(convos);
        Assert.That(sut.Conversations, Has.Count.EqualTo(2));
    }

    [Test]
    public void SetConversations_EmptyList_ClearsAll()
    {
        sut.SetConversations(new[]
        {
            new PersistedConversation("id1", "Title1", new List<PersistedParticipant>(), null, null)
        });

        sut.SetConversations(Enumerable.Empty<PersistedConversation>());
        Assert.That(sut.Conversations, Is.Empty);
    }

    // ── SaveTemplates ───────────────────────────────────────────────────

    [Test]
    public void SaveTemplates_ReplacesAll()
    {
        var templates = new List<ParticipantTemplate>
        {
            new("t1", "openai", "Custom GPT", "Custom personality", null, false)
        };

        sut.SaveTemplates(templates);
        Assert.That(sut.Templates, Has.Count.EqualTo(1));
        Assert.That(sut.Templates[0].DisplayName, Is.EqualTo("Custom GPT"));
    }

    // ── Appearance settings mutations ───────────────────────────────────

    [Test]
    public void SetAppearanceTheme_Updates()
    {
        sut.SetAppearanceTheme("neon");
        Assert.That(sut.AppearanceTheme, Is.EqualTo("neon"));
    }

    [Test]
    public void SetAppearanceTheme_BlankDefaultsToDark()
    {
        sut.SetAppearanceTheme("");
        Assert.That(sut.AppearanceTheme, Is.EqualTo("dark"));
    }

    [Test]
    public void SetAppearanceTheme_WhitespaceDefaultsToDark()
    {
        sut.SetAppearanceTheme("   ");
        Assert.That(sut.AppearanceTheme, Is.EqualTo("dark"));
    }

    [Test]
    public void SetControlHeight_Updates()
    {
        sut.SetControlHeight(50);
        Assert.That(sut.ControlHeight, Is.EqualTo(50));
    }

    [Test]
    public void SetGutter_Updates()
    {
        sut.SetGutter(20);
        Assert.That(sut.Gutter, Is.EqualTo(20));
    }

    [Test]
    public void SetBorderRadius_Updates()
    {
        sut.SetBorderRadius(16);
        Assert.That(sut.BorderRadius, Is.EqualTo(16));
    }

    [Test]
    public void SetGlobalMaxTokens_Updates()
    {
        sut.SetGlobalMaxTokens(4096);
        Assert.That(sut.GlobalMaxTokens, Is.EqualTo(4096));
    }

    [Test]
    public void SetGlobalMaxRounds_Updates()
    {
        sut.SetGlobalMaxRounds(5);
        Assert.That(sut.GlobalMaxRounds, Is.EqualTo(5));
    }

    // ── ClaudeFallbackMode ──────────────────────────────────────────────

    [Test]
    public void ClaudeFallbackMode_DefaultsToFalse()
    {
        Assert.That(sut.ClaudeFallbackMode, Is.False);
    }

    [Test]
    public void SetClaudeFallbackMode_TogglesValue()
    {
        sut.SetClaudeFallbackMode(true);
        Assert.That(sut.ClaudeFallbackMode, Is.True);

        sut.SetClaudeFallbackMode(false);
        Assert.That(sut.ClaudeFallbackMode, Is.False);
    }

    // ── Shared credentials store ────────────────────────────────────────

    [Test]
    public void LegionCredentialStore_DefaultPath_IsUnderRoamingAppData()
    {
        // Temporarily clear the test sandbox override so we observe the real default path.
        const string envVar = "MINDATTIC_LLM_CREDENTIALS";
        var saved = Environment.GetEnvironmentVariable(envVar);
        try
        {
            Environment.SetEnvironmentVariable(envVar, null);

            var expectedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MindAttic", "LLM");

            Assert.That(MindAtticCredentialStore.CredentialDirectory, Is.EqualTo(expectedRoot));
            Assert.That(MindAtticCredentialStore.ProvidersFilePath,
                Is.EqualTo(Path.Combine(expectedRoot, "providers.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, saved);
        }
    }

    [Test]
    public void Construction_SyncsEveryKnownProviderToSharedStore()
    {
        // After constructing a fresh SettingsService against an empty sandbox, every
        // provider this app supports should be present in the shared store so sibling
        // MindAttic apps can see the same credentials.
        var shared = MindAtticCredentialStore.LoadAllRaw();
        var standardProviders = new[] { "openai", "claude", "gemini", "deepseek" };

        foreach (var providerId in standardProviders)
            Assert.That(shared.ContainsKey(providerId), Is.True, $"{providerId} missing from shared store");
    }

    [Test]
    public void SetAuthJson_PropagatesToSharedStore()
    {
        var json = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"sk-propagation-test\",\n  \"model\": \"gpt-4.1-mini\",\n  \"maxTokens\": 2048\n}";
        sut.SetAuthJson("openai", json);

        var fromShared = MindAtticCredentialStore.LoadAllRaw().TryGetValue("openai", out var raw) ? raw : null;
        Assert.That(fromShared, Is.Not.Null);
        Assert.That(fromShared, Does.Contain("sk-propagation-test"));
    }

    [Test]
    public void Overlay_SharedStoreValuesTakePrecedenceOverLocal()
    {
        // Pre-seed shared store with a known apiKey, then spin up a fresh SettingsService
        // and verify it reads the shared value rather than the default stub.
        var sharedJson = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"sk-from-shared-store\",\n  \"model\": \"gpt-4.1-mini\",\n  \"maxTokens\": 2048\n}";
        MindAtticCredentialStore.SaveRaw("openai", sharedJson);

        var fresh = new SettingsService();
        Assert.That(fresh.GetKeyForProvider("openai", null), Is.EqualTo("sk-from-shared-store"));
    }

    [Test]
    public void LegionCredentialStore_EnvVarOverride_IsHonored()
    {
        const string envVar = "MINDATTIC_LLM_CREDENTIALS";
        var saved = Environment.GetEnvironmentVariable(envVar);
        var custom = Path.Combine(Path.GetTempPath(), $"override-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable(envVar, custom);
            Assert.That(MindAtticCredentialStore.CredentialDirectory, Is.EqualTo(custom));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, saved);
        }
    }
}
