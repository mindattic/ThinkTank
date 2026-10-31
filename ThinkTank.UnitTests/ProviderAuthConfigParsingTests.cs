using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// Credential and provider-config resolution through SettingsService (LAMP, TT-T27). API keys are never
/// stored in-app: <see cref="ThinkTankSettingsService.GetKeyForProvider"/> resolves an explicit persona
/// override, else ThinkTank's active key from Vault's shared named lists. Provider config JSON
/// (<c>type</c>, <c>model</c>, <c>maxTokens</c>) is built from ThinkTank's Vault settings and never carries a key.
/// </summary>
[TestFixture]
public class ProviderAuthConfigParsingTests
{
    [Test]
    public void ProviderAuthConfig_IsValueRecord()
    {
        var a = new ProviderAuthConfig("openai", "{}");
        var b = new ProviderAuthConfig("openai", "{}");
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void GetKey_ReturnsTheVaultKey_WhenSet()
    {
        var settings = TestLamp.Service(keys: TestLamp.KeysWith(("openai", "sk-abc")));
        Assert.That(settings.GetKeyForProvider("openai", null), Is.EqualTo("sk-abc"));
    }

    [Test]
    public void GetKey_ReturnsEmpty_WhenVaultHasNoKey()
    {
        Assert.That(TestLamp.Service().GetKeyForProvider("openai", null), Is.Empty);
    }

    [Test]
    public void GetKey_OverridePrecedesVault()
    {
        var settings = TestLamp.Service(keys: TestLamp.KeysWith(("openai", "vault-key")));
        Assert.That(settings.GetKeyForProvider("openai", "override-key"), Is.EqualTo("override-key"));
    }

    [Test]
    public void GetKey_BlankOverrideFallsBackToVault()
    {
        var settings = TestLamp.Service(keys: TestLamp.KeysWith(("openai", "vault-key")));
        Assert.That(settings.GetKeyForProvider("openai", "   "), Is.EqualTo("vault-key"));
    }

    [Test]
    public void GetAuthJson_CarriesModelAndMaxTokens_AndNeverAKey()
    {
        var settings = TestLamp.Service(s => { s.Providers.OpenAI.Model = "gpt-5"; s.Providers.OpenAI.MaxTokens = 4096; },
            TestLamp.KeysWith(("openai", "sk-secret")));

        using var doc = System.Text.Json.JsonDocument.Parse(settings.GetAuthJson("openai"));
        Assert.That(doc.RootElement.TryGetProperty("apiKey", out _), Is.False, "no key in provider config");
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("bearer"));
        Assert.That(doc.RootElement.GetProperty("model").GetString(), Is.EqualTo("gpt-5"));
        Assert.That(doc.RootElement.GetProperty("maxTokens").GetInt32(), Is.EqualTo(4096));
    }

    [TestCase("claude", "anthropic")]
    [TestCase("gemini", "google")]
    [TestCase("deepseek", "bearer")]
    [TestCase("kimi", "bearer")]
    public void GetAuthJson_KeepsEachProvidersWireType(string provider, string type)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(TestLamp.Service().GetAuthJson(provider));
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo(type));
    }

    [Test]
    public void GetAuthJson_UnknownProvider_ReturnsEmptyObject()
    {
        Assert.That(TestLamp.Service().GetAuthJson("nope"), Is.EqualTo("{}"));
    }
}
