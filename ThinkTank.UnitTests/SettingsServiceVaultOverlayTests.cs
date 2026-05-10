using Microsoft.Extensions.Configuration;
using MindAttic.Legion;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// Covers <see cref="SettingsServiceVaultOverlay.OverlayFromConfiguration"/> and the
/// runtime-override precedence rules in <see cref="ThinkTankSettingsService.GetKeyForProvider"/>.
/// The critical invariant is that a Vault-resolved apiKey is held in
/// <see cref="ThinkTankSettingsService.RuntimeApiKeyOverrides"/> only — never written into
/// <see cref="ThinkTankSettingsService.ProviderAuth"/> — so it cannot leak to disk via
/// <see cref="ThinkTankSettingsService.Save"/> or <c>SyncLocalToSharedStore</c>.
/// </summary>
[TestFixture]
public class SettingsServiceVaultOverlayTests
{
    private SettingsService sut = null!;

    [SetUp]
    public void SetUp()
    {
        var sandbox = MindAtticCredentialStore.CredentialDirectory;
        if (Directory.Exists(sandbox))
        {
            foreach (var f in Directory.EnumerateFiles(sandbox))
                File.Delete(f);
        }
        sut = new SettingsService();
    }

    private static IConfiguration BuildConfig(IDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IConfiguration BuildVaultConfig(IDictionary<string, string?> apiKeysByProvider)
    {
        var values = apiKeysByProvider
            .ToDictionary(kv => $"MindAttic:Vault:LLM:{kv.Key}:apiKey", kv => kv.Value);
        return BuildConfig(values);
    }

    // ── Overlay does nothing when there's no Vault section ───────────────

    [Test]
    public void OverlayFromConfiguration_NoVaultSection_LeavesOverridesEmpty()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["UnrelatedKey"] = "value" });

        sut.OverlayFromConfiguration(config);

        Assert.That(sut.RuntimeApiKeyOverrides, Is.Empty);
    }

    [Test]
    public void OverlayFromConfiguration_NullConfig_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => sut.OverlayFromConfiguration(null!));
        Assert.That(sut.RuntimeApiKeyOverrides, Is.Empty);
    }

    // ── Overlay populates the runtime side map, NOT ProviderAuth ─────────

    [Test]
    public void OverlayFromConfiguration_VaultKeySet_PopulatesRuntimeOverride()
    {
        var config = BuildVaultConfig(new Dictionary<string, string?>
        {
            ["openai"] = "sk-vault-openai"
        });

        sut.OverlayFromConfiguration(config);

        Assert.That(sut.RuntimeApiKeyOverrides.TryGetValue("openai", out var key), Is.True);
        Assert.That(key, Is.EqualTo("sk-vault-openai"));
    }

    [Test]
    public void OverlayFromConfiguration_TrimsWhitespace()
    {
        var config = BuildVaultConfig(new Dictionary<string, string?>
        {
            ["openai"] = "  sk-padded  "
        });

        sut.OverlayFromConfiguration(config);

        Assert.That(sut.RuntimeApiKeyOverrides["openai"], Is.EqualTo("sk-padded"));
    }

    [Test]
    public void OverlayFromConfiguration_DoesNotMutateProviderAuth()
    {
        var originalJson = sut.ProviderAuth["openai"].Json;
        var config = BuildVaultConfig(new Dictionary<string, string?>
        {
            ["openai"] = "sk-vault-leak-test"
        });

        sut.OverlayFromConfiguration(config);

        Assert.That(sut.ProviderAuth["openai"].Json, Is.EqualTo(originalJson),
            "Vault overlay must NEVER mutate ProviderAuth — that map is the on-disk projection.");
        Assert.That(sut.ProviderAuth["openai"].Json, Does.Not.Contain("sk-vault-leak-test"));
    }

    [Test]
    public void OverlayFromConfiguration_WhitespaceOnlyValue_Skipped()
    {
        var config = BuildVaultConfig(new Dictionary<string, string?>
        {
            ["openai"] = "   "
        });

        sut.OverlayFromConfiguration(config);

        Assert.That(sut.RuntimeApiKeyOverrides.ContainsKey("openai"), Is.False);
    }

    [Test]
    public void OverlayFromConfiguration_OnlyAppliesToKnownProviders()
    {
        var config = BuildVaultConfig(new Dictionary<string, string?>
        {
            ["openai"] = "sk-known",
            ["unknown_provider"] = "sk-stranger"
        });

        sut.OverlayFromConfiguration(config);

        Assert.That(sut.RuntimeApiKeyOverrides.ContainsKey("openai"), Is.True);
        Assert.That(sut.RuntimeApiKeyOverrides.ContainsKey("unknown_provider"), Is.False,
            "Unknown providers must not enter the runtime override map.");
    }

    // ── GetKeyForProvider precedence ────────────────────────────────────

    [Test]
    public void GetKeyForProvider_ExplicitOverride_WinsOverEverything()
    {
        sut.ProviderAuth["openai"] = new ProviderAuthConfig("openai",
            "{\"apiKey\":\"sk-disk\"}");
        sut.RuntimeApiKeyOverrides["openai"] = "sk-runtime";

        var key = sut.GetKeyForProvider("openai", "sk-explicit");

        Assert.That(key, Is.EqualTo("sk-explicit"));
    }

    [Test]
    public void GetKeyForProvider_DiskKey_WinsOverRuntime()
    {
        sut.ProviderAuth["openai"] = new ProviderAuthConfig("openai",
            "{\"apiKey\":\"sk-disk\"}");
        sut.RuntimeApiKeyOverrides["openai"] = "sk-runtime";

        var key = sut.GetKeyForProvider("openai", null);

        Assert.That(key, Is.EqualTo("sk-disk"),
            "When the user has explicitly set a key on disk, it wins over the cloud overlay.");
    }

    [Test]
    public void GetKeyForProvider_EmptyDiskKey_FallsBackToRuntime()
    {
        sut.ProviderAuth["openai"] = new ProviderAuthConfig("openai",
            "{\"type\":\"bearer\",\"apiKey\":\"\",\"model\":\"gpt-4.1-mini\"}");
        sut.RuntimeApiKeyOverrides["openai"] = "sk-from-vault";

        var key = sut.GetKeyForProvider("openai", null);

        Assert.That(key, Is.EqualTo("sk-from-vault"),
            "Cloud-deployed instances run with empty disk keys — Vault must fill the gap.");
    }

    [Test]
    public void GetKeyForProvider_WhitespaceDiskKey_FallsBackToRuntime()
    {
        sut.ProviderAuth["openai"] = new ProviderAuthConfig("openai",
            "{\"apiKey\":\"   \"}");
        sut.RuntimeApiKeyOverrides["openai"] = "sk-from-vault";

        var key = sut.GetKeyForProvider("openai", null);

        Assert.That(key, Is.EqualTo("sk-from-vault"));
    }

    [Test]
    public void GetKeyForProvider_NoDiskKeyField_FallsBackToRuntime()
    {
        sut.ProviderAuth["openai"] = new ProviderAuthConfig("openai",
            "{\"type\":\"bearer\"}");
        sut.RuntimeApiKeyOverrides["openai"] = "sk-from-vault";

        var key = sut.GetKeyForProvider("openai", null);

        Assert.That(key, Is.EqualTo("sk-from-vault"));
    }

    [Test]
    public void GetKeyForProvider_MalformedDiskJson_FallsBackToRuntime()
    {
        sut.ProviderAuth["openai"] = new ProviderAuthConfig("openai", "not-valid-json");
        sut.RuntimeApiKeyOverrides["openai"] = "sk-from-vault";

        var key = sut.GetKeyForProvider("openai", null);

        Assert.That(key, Is.EqualTo("sk-from-vault"));
    }

    [Test]
    public void GetKeyForProvider_NoDiskOrRuntime_ReturnsEmpty()
    {
        sut.ProviderAuth["openai"] = new ProviderAuthConfig("openai", "{\"apiKey\":\"\"}");

        var key = sut.GetKeyForProvider("openai", null);

        Assert.That(key, Is.EqualTo(""));
    }

    // ── No-leak invariant: Save() does not persist runtime overrides ────

    [Test]
    public void Save_AfterOverlay_DoesNotPersistRuntimeKey()
    {
        var config = BuildVaultConfig(new Dictionary<string, string?>
        {
            ["openai"] = "sk-must-not-leak"
        });
        sut.OverlayFromConfiguration(config);

        // SetAuthJson triggers Save() — exactly the path that previously leaked.
        sut.SetAuthJson("claude", sut.GetAuthJson("claude"));

        // Reload and confirm the cloud key is NOT on disk for openai.
        var fresh = new SettingsService();
        var freshOpenAi = fresh.ProviderAuth["openai"].Json;

        Assert.That(freshOpenAi, Does.Not.Contain("sk-must-not-leak"),
            "Vault-resolved keys must never appear in the persisted Settings.json or shared providers.json.");
    }

    // ── Active-provider check honors runtime-only providers ─────────────

    [Test]
    public void LoadLegionPersonaTemplates_VaultOnlyProvider_CountsAsActive()
    {
        // Empty disk apiKey for openai (default), Vault supplies a real key.
        var config = BuildVaultConfig(new Dictionary<string, string?>
        {
            ["openai"] = "sk-vault-only"
        });
        sut.OverlayFromConfiguration(config);

        sut.LoadLegionPersonaTemplates(count: 1, fallbackProviderId: "openai");

        Assert.That(sut.Templates, Is.Not.Empty,
            "A provider with only a Vault-resolved key must register as active so Legion can pick it.");
    }
}
