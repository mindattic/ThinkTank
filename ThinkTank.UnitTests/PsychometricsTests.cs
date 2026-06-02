using MindAttic.Legion;
using NUnit.Framework;
using ThinkTank.Core.Models;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// Covers the psychometrics integration: rendering a Legion <see cref="PsychometricProfile"/>
/// into prompt prose (<see cref="PsychometricNarrator"/>), resolving profiles from a file-backed
/// store (<see cref="PsychometricProfileService"/>), and the persona-id linkage on
/// <see cref="ChatParticipant"/>.
/// </summary>
[TestFixture]
public class PsychometricsTests
{
    private static PsychometricProfile SampleProfile(string personaId = "persona-test") => new(
        PersonaId: personaId,
        Ocean: new OceanScores(Openness: 85, Conscientiousness: 30, Extraversion: 18, Agreeableness: 75, Neuroticism: 25),
        Hexaco: new HexacoScores(HonestyHumility: 82, Emotionality: 35, Extraversion: 30, Agreeableness: 70, Conscientiousness: 60, Openness: 85),
        Mbti: new MbtiResult("INTJ", ExtraversionPct: 25, SensingPct: 25, ThinkingPct: 75, JudgingPct: 75),
        Enneagram: new EnneagramResult(Type: 5, Wing: 6, Triad: "Head"),
        Disc: new DiscResult(Dominance: 40, Influence: 30, Steadiness: 55, Conscientiousness: 72, PrimaryStyle: "C"),
        AdministeredByProvider: "claude",
        AdministeredByModel: "claude-opus-4-8",
        InstrumentSetVersion: "1.0.0",
        ScoredAtUtc: DateTime.UtcNow);

    // ── Narrator ─────────────────────────────────────────────────────────────

    [Test]
    public void Narrator_NullProfile_ReturnsEmpty()
    {
        Assert.That(PsychometricNarrator.Describe(null), Is.Empty);
    }

    [Test]
    public void Narrator_RendersAllFiveInstruments()
    {
        var text = PsychometricNarrator.Describe(SampleProfile());

        Assert.Multiple(() =>
        {
            // Big Five band selection: very-high Openness, very-low Extraversion.
            Assert.That(text, Does.Contain("Openness (very high"));
            Assert.That(text, Does.Contain("imaginative"));
            Assert.That(text, Does.Contain("Extraversion (very low"));
            Assert.That(text, Does.Contain("reserved"));
            // Neuroticism 25 is rendered as Emotional stability 75 (high).
            Assert.That(text, Does.Contain("Emotional stability (high"));
            // HEXACO distinctive factors.
            Assert.That(text, Does.Contain("Honesty-Humility (very high"));
            Assert.That(text, Does.Contain("Emotionality (low"));
            // Type instruments.
            Assert.That(text, Does.Contain("type INTJ"));
            Assert.That(text, Does.Contain("Enneagram 5w6, Head triad"));
            Assert.That(text, Does.Contain("DISC-C"));
            Assert.That(text, Does.Contain("precise"));
        });
    }

    [Test]
    public void Narrator_InstructsToEmbodyNotRecite()
    {
        var text = PsychometricNarrator.Describe(SampleProfile());
        Assert.That(text, Does.Contain("never name or recite them"));
    }

    // ── Service (file-backed store) ────────────────────────────────────────────

    [Test]
    public void Service_ResolvesAndRendersStoredProfile()
    {
        var dir = NewTempStore();
        var store = new PersonaStore(dir);
        store.SaveAssessment("persona-0001", runId: 1, profile: SampleProfile("persona-0001"));

        var sut = new PsychometricProfileService(store);

        Assert.Multiple(() =>
        {
            Assert.That(sut.HasProfile("persona-0001"), Is.True);
            Assert.That(sut.GetProfile("persona-0001")!.Mbti.Type, Is.EqualTo("INTJ"));
            Assert.That(sut.DescribeForPrompt("persona-0001"), Does.Contain("type INTJ"));
        });
    }

    [Test]
    public void Service_UnknownPersona_YieldsNullAndEmpty()
    {
        var sut = new PsychometricProfileService(new PersonaStore(NewTempStore()));
        Assert.Multiple(() =>
        {
            Assert.That(sut.GetProfile("nope"), Is.Null);
            Assert.That(sut.HasProfile("nope"), Is.False);
            Assert.That(sut.DescribeForPrompt("nope"), Is.Empty);
        });
    }

    [Test]
    public void Service_NullOrBlankPersonaId_YieldsEmpty()
    {
        var sut = new PsychometricProfileService(new PersonaStore(NewTempStore()));
        Assert.Multiple(() =>
        {
            Assert.That(sut.DescribeForPrompt(null), Is.Empty);
            Assert.That(sut.DescribeForPrompt("   "), Is.Empty);
            Assert.That(sut.GetProfile(null), Is.Null);
        });
    }

    [Test]
    public void Service_MissingStoreDirectory_DoesNotThrow()
    {
        // Directory never created — store reads must degrade to null, not blow up.
        var sut = new PsychometricProfileService(Path.Combine(Path.GetTempPath(), "tt-psy-missing-" + Guid.NewGuid().ToString("N")));
        Assert.That(sut.DescribeForPrompt("persona-0001"), Is.Empty);
    }

    // ── EffectivePersonaId linkage ─────────────────────────────────────────────

    [Test]
    public void EffectivePersonaId_PrefersExplicitField()
    {
        var p = new ChatParticipant("pid", "legion-default-claude", "claude", "Claude", "", null)
        {
            PersonaId = "persona-0042"
        };
        Assert.That(p.EffectivePersonaId, Is.EqualTo("persona-0042"));
    }

    [Test]
    public void EffectivePersonaId_FallsBackToLegionTemplateIdConvention()
    {
        var p = new ChatParticipant("pid", "legion-default-claude", "claude", "Claude", "", null);
        Assert.That(p.EffectivePersonaId, Is.EqualTo("default-claude"));
    }

    [Test]
    public void EffectivePersonaId_CustomTemplate_IsNull()
    {
        var p = new ChatParticipant("pid", "deadbeefcafef00d", "claude", "Custom", "Be terse", null);
        Assert.That(p.EffectivePersonaId, Is.Null);
    }

    // ── Source-site population (SettingsService) ───────────────────────────────

    [TestCase("persona-0042-a1b2c3d4", "persona-0042")]
    [TestCase("default-claude-deadbeef", "default-claude")]
    [TestCase("persona-0042", "persona-0042")]   // no 8-hex suffix → unchanged
    [TestCase("", null)]
    public void PersonaIdFromVoterId_StripsGuidSuffix(string voterId, string? expected)
    {
        Assert.That(SettingsService.PersonaIdFromVoterId(voterId), Is.EqualTo(expected));
    }

    private static string NewTempStore()
        => Path.Combine(Path.GetTempPath(), "tt-psy-" + Guid.NewGuid().ToString("N"));
}
