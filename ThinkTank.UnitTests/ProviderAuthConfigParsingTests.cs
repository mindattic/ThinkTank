using MindAttic.Legion;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// Covers the JSON parsing paths exercised by <see cref="ProviderAuthConfig"/>
/// flowing through SettingsService.SetAuthJson / GetKeyForProvider. The record
/// itself is just a (ProviderId, Json) tuple — the parsing happens at read
/// time inside SettingsService and ThinkTankService.
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
        // The record's value-equality is what lets DI / settings dedupe
        // identical configs. If someone converts it to a class, equality
        // breaks silently — pin the contract.
        var a = new ProviderAuthConfig("openai", "{}");
        var b = new ProviderAuthConfig("openai", "{}");
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void GetKey_ReturnsApiKey_FromValidJson()
    {
        settings.SetAuthJson("openai", "{\"type\":\"bearer\",\"apiKey\":\"sk-abc\"}");
        Assert.That(settings.GetKeyForProvider("openai", null), Is.EqualTo("sk-abc"));
    }

    [Test]
    public void GetKey_ReturnsEmpty_WhenApiKeyFieldMissing()
    {
        settings.SetAuthJson("openai", "{\"type\":\"bearer\",\"model\":\"gpt-4\"}");
        Assert.That(settings.GetKeyForProvider("openai", null), Is.Empty);
    }

    [Test]
    public void GetKey_ReturnsEmpty_WhenJsonIsMalformed()
    {
        settings.SetAuthJson("openai", "{not valid json");
        Assert.That(settings.GetKeyForProvider("openai", null), Is.Empty);
    }

    [Test]
    public void GetKey_OverridePrecedesStoredJson()
    {
        settings.SetAuthJson("openai", "{\"apiKey\":\"stored\"}");
        Assert.That(settings.GetKeyForProvider("openai", "override-key"), Is.EqualTo("override-key"));
    }

    [Test]
    public void GetKey_BlankOverrideFallsBackToStoredJson()
    {
        settings.SetAuthJson("openai", "{\"apiKey\":\"stored\"}");
        Assert.That(settings.GetKeyForProvider("openai", "   "), Is.EqualTo("stored"));
    }

    [Test]
    public void SetKey_PreservesExistingModelAndMaxTokens()
    {
        settings.SetAuthJson("openai", "{\"type\":\"bearer\",\"apiKey\":\"old\",\"model\":\"gpt-5\",\"maxTokens\":4096}");
        settings.SetKey("openai", "new-key");

        var json = settings.GetAuthJson("openai");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("apiKey").GetString(), Is.EqualTo("new-key"));
        Assert.That(doc.RootElement.GetProperty("model").GetString(), Is.EqualTo("gpt-5"));
        Assert.That(doc.RootElement.GetProperty("maxTokens").GetInt32(), Is.EqualTo(4096));
    }

    [Test]
    public void SetKey_RecoversWithBearerType_WhenStoredJsonIsMalformed()
    {
        settings.SetAuthJson("openai", "{garbage");
        settings.SetKey("openai", "fresh");

        using var doc = System.Text.Json.JsonDocument.Parse(settings.GetAuthJson("openai"));
        Assert.That(doc.RootElement.GetProperty("apiKey").GetString(), Is.EqualTo("fresh"));
        // Recovery path infers the Legion auth type from the providerId, the same
        // way SetKey_DefaultsTypeFromProviderId_WhenMissing does for a non-malformed
        // payload that simply omits the `type` field.
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("bearer"));
    }

    [TestCase("claude",   "anthropic")]
    [TestCase("gemini",   "google")]
    [TestCase("openai",   "bearer")]
    [TestCase("deepseek", "bearer")]
    public void SetKey_DefaultsTypeFromProviderId_WhenMissing(string providerId, string expectedType)
    {
        // Stored JSON has no `type` field — SetKey infers from providerId.
        settings.SetAuthJson(providerId, "{\"apiKey\":\"stub\"}");
        settings.SetKey(providerId, "rotated");

        using var doc = System.Text.Json.JsonDocument.Parse(settings.GetAuthJson(providerId));
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo(expectedType));
    }

    [Test]
    public void GetAuthJson_UnknownProvider_ReturnsEmptyObject()
    {
        Assert.That(settings.GetAuthJson("nope"), Is.EqualTo("{}"));
    }
}
