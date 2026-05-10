using System.Reflection;
using NUnit.Framework;
using ThinkTank.Core.Models;
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

    // ── ChatParticipant → VoterProfile mapping ──────────────────────────
    // Mirrors the spec table in CLAUDE.md ("Participant → VoterProfile Mapping").
    // The ApiKeyOverride / ModelOverride fields are nullable because Legion falls
    // back to its global VotingConfiguration when null — so a participant without
    // an AuthOverrideJson must produce null overrides, not empty strings.

    private static ChatParticipant Participant(
        string participantId = "p1",
        string templateId = "t1",
        string providerId = "openai",
        string displayName = "ChatGPT",
        string personalityMarkdown = "Be curious",
        string? authOverrideJson = null)
        => new(participantId, templateId, providerId, displayName, personalityMarkdown, authOverrideJson);

    [Test]
    public void MapToVoterProfiles_PreservesIdNameProviderPersonality()
    {
        var p = Participant(
            participantId: "vid-1",
            providerId: "claude",
            displayName: "Skeptic",
            personalityMarkdown: "Doubt every premise.");

        var voters = VotingService.MapToVoterProfiles(new[] { p });

        Assert.That(voters, Has.Count.EqualTo(1));
        Assert.That(voters[0].VoterId, Is.EqualTo("vid-1"));
        Assert.That(voters[0].Name, Is.EqualTo("Skeptic"));
        Assert.That(voters[0].ProviderId, Is.EqualTo("claude"));
        Assert.That(voters[0].PersonalityMarkdown, Is.EqualTo("Doubt every premise."));
    }

    [Test]
    public void MapToVoterProfiles_NoAuthOverride_OverridesAreNull()
    {
        var p = Participant(authOverrideJson: null);

        var voters = VotingService.MapToVoterProfiles(new[] { p });

        Assert.That(voters[0].ApiKeyOverride, Is.Null);
        Assert.That(voters[0].ModelOverride, Is.Null);
    }

    [Test]
    public void MapToVoterProfiles_AuthOverrideWithApiKey_PopulatesApiKeyOverride()
    {
        var p = Participant(authOverrideJson: "{\"apiKey\":\"sk-per-participant\"}");

        var voters = VotingService.MapToVoterProfiles(new[] { p });

        Assert.That(voters[0].ApiKeyOverride, Is.EqualTo("sk-per-participant"));
    }

    [Test]
    public void MapToVoterProfiles_AuthOverrideWithModel_PopulatesModelOverride()
    {
        var p = Participant(authOverrideJson: "{\"model\":\"gpt-4.1\"}");

        var voters = VotingService.MapToVoterProfiles(new[] { p });

        Assert.That(voters[0].ModelOverride, Is.EqualTo("gpt-4.1"));
    }

    [Test]
    public void MapToVoterProfiles_FullAuthBlob_PopulatesBoth()
    {
        var p = Participant(authOverrideJson:
            "{\"type\":\"bearer\",\"apiKey\":\"sk-x\",\"model\":\"gpt-4.1-mini\",\"maxTokens\":2048}");

        var voters = VotingService.MapToVoterProfiles(new[] { p });

        Assert.That(voters[0].ApiKeyOverride, Is.EqualTo("sk-x"));
        Assert.That(voters[0].ModelOverride, Is.EqualTo("gpt-4.1-mini"));
    }

    [Test]
    public void MapToVoterProfiles_AuthOverrideMissingFields_OverridesAreNull()
    {
        // Per-participant auth that only carries `type` should leave both override
        // fields null so Legion falls back to the global VotingConfiguration.
        var p = Participant(authOverrideJson: "{\"type\":\"bearer\"}");

        var voters = VotingService.MapToVoterProfiles(new[] { p });

        Assert.That(voters[0].ApiKeyOverride, Is.Null);
        Assert.That(voters[0].ModelOverride, Is.Null);
    }

    [Test]
    public void MapToVoterProfiles_MalformedAuthOverride_OverridesAreNull()
    {
        var p = Participant(authOverrideJson: "not-json");

        var voters = VotingService.MapToVoterProfiles(new[] { p });

        Assert.That(voters[0].ApiKeyOverride, Is.Null);
        Assert.That(voters[0].ModelOverride, Is.Null);
    }

    [Test]
    public void MapToVoterProfiles_MultipleParticipants_PreservesOrder()
    {
        var p1 = Participant(participantId: "a", displayName: "Alpha", providerId: "openai");
        var p2 = Participant(participantId: "b", displayName: "Bravo", providerId: "claude");
        var p3 = Participant(participantId: "c", displayName: "Charlie", providerId: "gemini");

        var voters = VotingService.MapToVoterProfiles(new[] { p1, p2, p3 });

        Assert.That(voters.Select(v => v.VoterId), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(voters.Select(v => v.Name), Is.EqualTo(new[] { "Alpha", "Bravo", "Charlie" }));
        Assert.That(voters.Select(v => v.ProviderId), Is.EqualTo(new[] { "openai", "claude", "gemini" }));
    }

    [Test]
    public void MapToVoterProfiles_EmptyRoster_ReturnsEmptyList()
    {
        var voters = VotingService.MapToVoterProfiles(Array.Empty<ChatParticipant>());

        Assert.That(voters, Is.Empty);
    }

    [Test]
    public void MapToVoterProfiles_EmptyApiKeyValue_PreservedAsEmptyString()
    {
        // The mapping is faithful — if the per-participant blob deliberately sets an
        // empty apiKey (to suppress the global default), it round-trips as "" not null.
        var p = Participant(authOverrideJson: "{\"apiKey\":\"\"}");

        var voters = VotingService.MapToVoterProfiles(new[] { p });

        Assert.That(voters[0].ApiKeyOverride, Is.EqualTo(""));
    }
}
