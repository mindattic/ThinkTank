using System.Text;
using MindAttic.Legion;

namespace ThinkTank.Core.Services;

/// <summary>
/// Translates a MindAttic.Legion <see cref="PsychometricProfile"/> (raw 0–100 trait
/// scores plus type labels) into a natural-language behavioral brief that can be
/// appended to a participant's system prompt. Legion only stores compact fingerprints
/// (<c>Summary()</c> / <c>ShortCode()</c>) meant for logs; an LLM reasons far better
/// from prose like "reserved and measured; you speak selectively" than from "E41".
///
/// The brief covers all five instruments. To avoid drowning the model in redundant
/// signal, HEXACO is rendered only through its two distinctive factors
/// (Honesty-Humility and Emotionality) — the other four HEXACO domains mirror the
/// Big Five and are already captured by the OCEAN section.
/// </summary>
public static class PsychometricNarrator
{
    /// <summary>
    /// Renders the full behavioral brief for <paramref name="profile"/> as a markdown
    /// block, headed and closed with instructions to live the traits rather than recite
    /// them. Returns an empty string if <paramref name="profile"/> is null.
    /// </summary>
    public static string Describe(PsychometricProfile? profile)
    {
        if (profile is null) return "";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Your measured personality");
        sb.AppendLine(
            "Your character has been psychometrically profiled. Let these traits drive *how* you think, " +
            "argue, and engage — your tone, what you notice first, what you push back on, how much room you take — " +
            "not just what you conclude. Embody them; never name or recite them.");
        sb.AppendLine();

        // ── Big Five / OCEAN ────────────────────────────────────────────────
        var o = profile.Ocean;
        sb.AppendLine("**Temperament (Big Five):**");
        sb.AppendLine($"- {OceanLine("Openness", o.Openness, "imaginative and intellectually curious; you welcome novel, abstract, and unconventional ideas", "practical and concrete; you are skeptical of untested novelty and prefer the proven", "curious but grounded, weighing new ideas against what already works")}");
        sb.AppendLine($"- {OceanLine("Conscientiousness", o.Conscientiousness, "organized, disciplined, and methodical; you plan and follow through", "spontaneous and flexible; you improvise and resist rigid structure", "reasonably organized yet adaptable")}");
        sb.AppendLine($"- {OceanLine("Extraversion", o.Extraversion, "outgoing, energetic, and assertive; you readily take the floor and think out loud", "reserved and measured; you speak selectively rather than dominate", "sociable without crowding others out")}");
        sb.AppendLine($"- {OceanLine("Agreeableness", o.Agreeableness, "warm, cooperative, and trusting; you seek common ground and soften conflict", "skeptical, competitive, and blunt; you challenge claims readily and rarely flatter", "cooperative but willing to push back when it matters")}");
        sb.AppendLine($"- {OceanLine("Emotional stability", 100 - o.Neuroticism, "calm, even-keeled, and resilient under pressure", "emotionally reactive and stress-sensitive; tension and urgency surface in your tone", "generally steady with occasional reactivity")}");
        sb.AppendLine();

        // ── HEXACO distinctive factors ──────────────────────────────────────
        var h = profile.Hexaco;
        sb.AppendLine("**Integrity & affect (HEXACO):**");
        sb.AppendLine($"- {Line("Honesty-Humility", h.HonestyHumility, "sincere, fair, and modest; you avoid manipulation and resist status games", "self-promoting and entitled; you will bend things to your advantage and angle for status", "fair-minded but pragmatic about your own interests")}");
        sb.AppendLine($"- {Line("Emotionality", h.Emotionality, "sentimental and empathetic; you feel risks keenly and seek connection", "tough-minded and unsentimental; you stay detached from fear and others' distress", "emotionally responsive without being overwhelmed")}");
        sb.AppendLine();

        // ── MBTI-style cognitive style ──────────────────────────────────────
        sb.AppendLine($"**Cognitive style (type {profile.Mbti.Type}):** {MbtiDescription(profile.Mbti)}.");
        sb.AppendLine();

        // ── Enneagram motivation ────────────────────────────────────────────
        var e = profile.Enneagram;
        sb.AppendLine($"**Core motivation (Enneagram {e.Notation()}, {e.Triad} triad):** {EnneagramDescription(e)}");
        sb.AppendLine();

        // ── DISC behavioral lean ────────────────────────────────────────────
        sb.AppendLine($"**Behavioral lean (DISC-{profile.Disc.PrimaryStyle}):** {DiscDescription(profile.Disc.PrimaryStyle)}.");

        return sb.ToString();
    }

    /// <summary>A single compact line "MBTI · Enneagram · DISC · OCEAN" for tooltips/UI; never sent to the model.</summary>
    public static string OneLine(PsychometricProfile? profile) => profile?.Summary() ?? "";

    // ── Trait-band rendering ────────────────────────────────────────────────

    private static string OceanLine(string name, double score, string high, string low, string mid)
        => Line(name, score, high, low, mid);

    /// <summary>"Name (band, NN/100): descriptor." choosing high/low/mid by where the score falls.</summary>
    private static string Line(string name, double score, string high, string low, string mid)
    {
        var (band, descriptor) = score >= 60 ? (BandWord(score), high)
                               : score <= 40 ? (BandWord(score), low)
                               : (BandWord(score), mid);
        return $"{name} ({band}, {score:0}/100): you are {descriptor}.";
    }

    private static string BandWord(double score) => score switch
    {
        >= 80 => "very high",
        >= 60 => "high",
        > 40  => "moderate",
        >= 20 => "low",
        _     => "very low",
    };

    // ── MBTI ────────────────────────────────────────────────────────────────

    private static string MbtiDescription(MbtiResult m)
    {
        var t = (m.Type ?? "").ToUpperInvariant();
        char Axis(int i, char fallback) => t.Length > i ? t[i] : fallback;

        var parts = new[]
        {
            Axis(0, 'I') == 'E' ? "energized by interaction and external debate" : "energized by reflection before you speak",
            Axis(1, 'N') == 'S' ? "anchored in concrete facts and detail"        : "drawn to patterns, theory, and the big picture",
            Axis(2, 'T') == 'T' ? "deciding by logic and internal consistency"   : "deciding by values and human impact",
            Axis(3, 'P') == 'J' ? "preferring closure, plans, and decisions"      : "preferring openness, options, and improvisation",
        };
        return string.Join("; ", parts);
    }

    // ── Enneagram ─────────────────────────────────────────────────────────────

    private static string EnneagramDescription(EnneagramResult e) => e.Type switch
    {
        1 => "principled and improvement-driven — you want to be right and to do things properly, and you notice what's flawed.",
        2 => "warm and giving — you want to be needed and helpful, and you read the room for what others want.",
        3 => "driven and image-aware — you want to succeed and be seen to, and you optimize for results that show.",
        4 => "introspective and identity-seeking — you want to be authentic and distinct, and you feel things deeply.",
        5 => "analytical and self-contained — you want competence and understanding, and you conserve energy and detach to think.",
        6 => "loyal and risk-aware — you want security and to be prepared, and you scan for what could go wrong.",
        7 => "enthusiastic and possibility-seeking — you want stimulation and to avoid being trapped, and you reframe toward the upside.",
        8 => "assertive and protective — you want control and directness, and you confront rather than tiptoe.",
        9 => "easygoing and harmonizing — you want peace and to avoid conflict, and you see all sides and merge with the group.",
        _ => "guided by a stable core motivation that colors what you seek and avoid.",
    };

    // ── DISC ────────────────────────────────────────────────────────────────

    private static string DiscDescription(string primaryStyle) => (primaryStyle ?? "").ToUpperInvariant() switch
    {
        "D" => "direct, results-driven, and decisive — you get to the point and drive toward an outcome",
        "I" => "enthusiastic, persuasive, and people-oriented — you engage warmly and sell ideas",
        "S" => "steady, patient, and supportive — you keep things calm, consistent, and collaborative",
        "C" => "precise, analytical, and detail-focused — you want accuracy, rigor, and the rules followed",
        _   => "a balance of directness, sociability, steadiness, and precision",
    };
}
