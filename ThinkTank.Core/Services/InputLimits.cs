namespace ThinkTank.Core.Services;

/// <summary>
/// The longest text accepted per input field (TT-T44), from ThinkTank's Vault settings. The pages put the same
/// numbers in <c>maxlength</c> for the user's sake, but the cut is made here on the server, because a browser
/// can send anything.
/// </summary>
public sealed record InputLimits(int Topic, int Message, int Persona, int Title, int VoteText)
{
    /// <summary>The text cut to at most <paramref name="max"/> characters (null becomes empty).</summary>
    public static string Clip(string? text, int max)
    {
        text ??= "";
        if (max <= 0 || text.Length <= max) return text;
        // Never split a surrogate pair.
        var end = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..end];
    }
}
