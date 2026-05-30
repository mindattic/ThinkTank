using MindAttic.Legion;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// Covers credential resolution through SettingsService. API keys are no longer stored
/// in-app — <see cref="ThinkTankSettingsService.GetKeyForProvider"/> resolves from an
/// explicit override or the Vault-populated <see cref="ThinkTankSettingsService.RuntimeApiKeyOverrides"/>.
/// <see cref="ThinkTankSettingsService.SetAuthJson"/> persists only non-secret config and
/// strips any <c>apiKey</c>.
/// </summary>
[TestFixture]
public class ProviderAuthConfigParsingTests
{
    private SettingsService settings = null!;

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
    }

    [Test]
    public void ProviderAuthConfig_IsValueRecord()
    {
        var a = new ProviderAuthConfig("openai", "{}");
        var b = new ProviderAuthConfig("openai", "{}");
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void GetKey_ReturnsVaultOverride_WhenSet()
    {
        settings.RuntimeApiKeyOverrides["openai"] = "sk-abc";
        Assert.That(settings.GetKeyForProvider("openai", null), Is.EqualTo("sk-abc"));
    }

    [Test]
    public void GetKey_ReturnsEmpty_WhenNoVaultOverride()
    {
        Assert.That(settings.GetKeyForProvider("openai", null), Is.Empty);
    }

    [Test]
    public void GetKey_IgnoresApiKeyOnDisk()
    {
        // Even if a key somehow lands in the on-disk auth JSON, it must NOT be used —
        // credentials come from Vault only.
        settings.SetAuthJson("openai", "{\"type\":\"bearer\",\"apiKey\":\"sk-on-disk\"}");
        Assert.That(settings.GetKeyForProvider("openai", null), Is.Empty);
    }

    [Test]
    public void GetKey_OverridePrecedesVault()
    {
        settings.RuntimeApiKeyOverrides["openai"] = "vault-key";
        Assert.That(settings.GetKeyForProvider("openai", "override-key"), Is.EqualTo("override-key"));
    }

    [Test]
    public void GetKey_BlankOverrideFallsBackToVault()
    {
        settings.RuntimeApiKeyOverrides["openai"] = "vault-key";
        Assert.That(settings.GetKeyForProvider("openai", "   "), Is.EqualTo("vault-key"));
    }

    [Test]
    public void SetAuthJson_StripsApiKey_PreservesModelAndMaxTokens()
    {
        settings.SetAuthJson("openai", "{\"type\":\"bearer\",\"apiKey\":\"should-be-dropped\",\"model\":\"gpt-5\",\"maxTokens\":4096}");

        using var doc = System.Text.Json.JsonDocument.Parse(settings.GetAuthJson("openai"));
        Assert.That(doc.RootElement.TryGetProperty("apiKey", out _), Is.False, "apiKey must be stripped");
        Assert.That(doc.RootElement.GetProperty("model").GetString(), Is.EqualTo("gpt-5"));
        Assert.That(doc.RootElement.GetProperty("maxTokens").GetInt32(), Is.EqualTo(4096));
    }

    [Test]
    public void SetAuthJson_MalformedJson_YieldsEmptyObject()
    {
        settings.SetAuthJson("openai", "{not valid json");
        Assert.That(settings.GetAuthJson("openai"), Is.EqualTo("{}"));
    }

    [Test]
    public void GetAuthJson_UnknownProvider_ReturnsEmptyObject()
    {
        Assert.That(settings.GetAuthJson("nope"), Is.EqualTo("{}"));
    }
}
