using System.Reflection;
using NUnit.Framework;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

[TestFixture]
public class VotingServiceTests
{
    // VotingService.ExtractField is a private static helper that parses a single
    // field out of an AuthOverrideJson blob. Testing it directly via reflection
    // validates the JSON extraction logic used in ChatParticipant → VoterProfile mapping.

    private static string? InvokeExtractField(string? json, string field)
    {
        var method = typeof(VotingService)
            .GetMethod("ExtractField", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string?)method.Invoke(null, new object?[] { json, field });
    }

    // ── Null / empty inputs ─────────────────────────────────────────────

    [Test]
    public void ExtractField_NullJson_ReturnsNull()
    {
        Assert.That(InvokeExtractField(null, "apiKey"), Is.Null);
    }

    [Test]
    public void ExtractField_EmptyString_ReturnsNull()
    {
        Assert.That(InvokeExtractField("", "apiKey"), Is.Null);
    }

    [Test]
    public void ExtractField_WhitespaceOnly_ReturnsNull()
    {
        Assert.That(InvokeExtractField("   ", "apiKey"), Is.Null);
    }

    // ── Malformed JSON ──────────────────────────────────────────────────

    [Test]
    public void ExtractField_MalformedJson_ReturnsNull()
    {
        Assert.That(InvokeExtractField("not json at all", "apiKey"), Is.Null);
    }

    [Test]
    public void ExtractField_TruncatedJson_ReturnsNull()
    {
        Assert.That(InvokeExtractField("{\"apiKey\":", "apiKey"), Is.Null);
    }

    // ── Valid JSON — field present ───────────────────────────────────────

    [Test]
    public void ExtractField_ApiKeyPresent_ReturnsKey()
    {
        var json = "{\"apiKey\":\"sk-test-abc\"}";
        Assert.That(InvokeExtractField(json, "apiKey"), Is.EqualTo("sk-test-abc"));
    }

    [Test]
    public void ExtractField_ModelPresent_ReturnsModel()
    {
        var json = "{\"model\":\"gpt-4.1\"}";
        Assert.That(InvokeExtractField(json, "model"), Is.EqualTo("gpt-4.1"));
    }

    [Test]
    public void ExtractField_EmptyStringValue_ReturnsEmptyString()
    {
        var json = "{\"apiKey\":\"\"}";
        Assert.That(InvokeExtractField(json, "apiKey"), Is.EqualTo(""));
    }

    // ── Valid JSON — field absent ────────────────────────────────────────

    [Test]
    public void ExtractField_FieldMissing_ReturnsNull()
    {
        var json = "{\"type\":\"bearer\",\"maxTokens\":2048}";
        Assert.That(InvokeExtractField(json, "apiKey"), Is.Null);
    }

    [Test]
    public void ExtractField_WrongFieldName_ReturnsNull()
    {
        var json = "{\"API_KEY\":\"sk-xyz\"}";
        Assert.That(InvokeExtractField(json, "apiKey"), Is.Null);
    }

    // ── Realistic AuthOverrideJson blobs (same format as SettingsService) ─

    [Test]
    public void ExtractField_BearerAuthBlob_ExtractsApiKey()
    {
        var json = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"sk-live-123\",\n  \"model\": \"gpt-4.1-mini\",\n  \"maxTokens\": 2048\n}";
        Assert.That(InvokeExtractField(json, "apiKey"), Is.EqualTo("sk-live-123"));
    }

    [Test]
    public void ExtractField_BearerAuthBlob_ExtractsModel()
    {
        var json = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"sk-live-123\",\n  \"model\": \"gpt-4.1-mini\",\n  \"maxTokens\": 2048\n}";
        Assert.That(InvokeExtractField(json, "model"), Is.EqualTo("gpt-4.1-mini"));
    }

    [Test]
    public void ExtractField_AnthropicAuthBlob_ExtractsApiKey()
    {
        var json = "{\n  \"type\": \"anthropic\",\n  \"apiKey\": \"sk-ant-abc\",\n  \"model\": \"claude-sonnet-4-6\",\n  \"maxTokens\": 2048\n}";
        Assert.That(InvokeExtractField(json, "apiKey"), Is.EqualTo("sk-ant-abc"));
    }

    [Test]
    public void ExtractField_AuthBlobWithoutModel_ModelReturnsNull()
    {
        var json = "{\n  \"type\": \"bearer\",\n  \"apiKey\": \"sk-xyz\",\n  \"maxTokens\": 2048\n}";
        Assert.That(InvokeExtractField(json, "model"), Is.Null);
    }

    [Test]
    public void ExtractField_NullAuthOverride_BothFieldsNull()
    {
        Assert.That(InvokeExtractField(null, "apiKey"), Is.Null);
        Assert.That(InvokeExtractField(null, "model"), Is.Null);
    }

    // ── Field name is case-sensitive (JSON property names) ──────────────

    [Test]
    public void ExtractField_FieldNameCaseSensitive_NoMatch()
    {
        var json = "{\"ApiKey\":\"sk-xyz\"}";
        Assert.That(InvokeExtractField(json, "apiKey"), Is.Null);
    }
}
