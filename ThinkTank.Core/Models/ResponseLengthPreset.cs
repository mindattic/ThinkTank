namespace ThinkTank.Core.Models;

/// <summary>
/// Single source of truth for the roundtable "response length" presets. A preset
/// constrains how much each participant writes on its turn so discussions stay
/// conversational instead of devolving into multi-page essays.
/// <para>
/// The constraint is a <i>prompt-level</i> instruction (see <see cref="InstructionFor"/>)
/// appended to every participant's system prompt — independent of the participant's
/// own personality markdown. This produces naturally short, complete answers rather
/// than responses truncated mid-sentence by the <c>maxTokens</c> hard cap.
/// </para>
/// </summary>
public static class ResponseLengthPreset
{
    public const string Concise = "concise";
    public const string Balanced = "balanced";
    public const string Detailed = "detailed";

    /// <summary>Preset applied when nothing else is configured.</summary>
    public const string Default = Concise;

    /// <summary>UI-facing presets in display order: the stored value, its label, and a short hint.</summary>
    public static readonly IReadOnlyList<(string Value, string Label, string Hint)> Options = new[]
    {
        (Concise, "Concise", "~2-3 sentences"),
        (Balanced, "Balanced", "~1 short paragraph"),
        (Detailed, "Detailed", "~2-3 paragraphs"),
    };

    /// <summary>
    /// Coerces an arbitrary stored/loaded value to a known preset, falling back to
    /// <see cref="Default"/> for null, blank, or unrecognized input.
    /// </summary>
    public static string Normalize(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            Concise => Concise,
            Balanced => Balanced,
            Detailed => Detailed,
            _ => Default,
        };

    /// <summary>
    /// The brevity instruction appended to a participant's system prompt for the given
    /// preset. Always returns a leading-newline-separated block so it can be concatenated
    /// directly onto an existing prompt.
    /// </summary>
    public static string InstructionFor(string? value) => Normalize(value) switch
    {
        Balanced =>
            "\n\nKeep your contribution to a single short paragraph (roughly 3-5 sentences). " +
            "Make your point and pass the floor — do not write essays, long lists, or multi-section answers.",
        Detailed =>
            "\n\nKeep your contribution focused — at most two or three short paragraphs. " +
            "Do not write long essays, exhaustive lists, or multi-section chapters.",
        _ /* Concise */ =>
            "\n\nKeep your contribution to about 2-3 sentences. Make a single clear point and pass the floor — " +
            "do not write essays, lists, or multi-paragraph answers.",
    };
}
