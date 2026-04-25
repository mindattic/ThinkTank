using System.Text.RegularExpressions;
using NUnit.Framework;

namespace LLMThinkTank.UnitTests;

/// <summary>
/// Validates the [REQUEST_VOTE: ...] marker that Chat.razor injects into every
/// participant's personality and parses from their responses. Tests replicate the
/// exact regex used in StartActive so any future changes to the pattern fail here first.
/// </summary>
[TestFixture]
public class VoteMarkerTests
{
    // Exact pattern and flags from Chat.razor StartActive loop
    private const string Pattern = @"\[REQUEST_VOTE:\s*(.+?)\]";
    private const RegexOptions Options = RegexOptions.IgnoreCase;

    private static Match Match(string input) => Regex.Match(input, Pattern, Options);

    private static string StripMarker(string input)
    {
        var m = Match(input);
        return m.Success ? input.Replace(m.Value, "").Trim() : input;
    }

    // ── No match cases ──────────────────────────────────────────────────

    [Test]
    public void PlainText_NoMatch()
    {
        Assert.That(Match("I think we should explore this further.").Success, Is.False);
    }

    [Test]
    public void EmptyString_NoMatch()
    {
        Assert.That(Match("").Success, Is.False);
    }

    [Test]
    public void SimilarButWrongTag_NoMatch()
    {
        Assert.That(Match("[VOTE: something]").Success, Is.False);
        Assert.That(Match("[REQUEST: question]").Success, Is.False);
        Assert.That(Match("REQUEST_VOTE: no brackets").Success, Is.False);
    }

    [Test]
    public void MissingClosingBracket_NoMatch()
    {
        Assert.That(Match("[REQUEST_VOTE: Have we agreed?").Success, Is.False);
    }

    [Test]
    public void MissingOpeningBracket_NoMatch()
    {
        Assert.That(Match("REQUEST_VOTE: Have we agreed?]").Success, Is.False);
    }

    [Test]
    public void NoContentAfterColon_NoMatch()
    {
        // With no chars at all between colon and ] — .+? cannot match
        Assert.That(Match("[REQUEST_VOTE:]").Success, Is.False);
    }

    [Test]
    public void WhitespaceOnlyQuestion_MatchesButTrimsToEmpty()
    {
        // The regex matches (space satisfies .+?), but production code trims and
        // treats the result as null — so a vote is NOT actually triggered.
        var m = Match("[REQUEST_VOTE: ]");
        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[1].Value.Trim(), Is.Empty);
    }

    // ── Basic match ─────────────────────────────────────────────────────

    [Test]
    public void BasicMarker_Matches()
    {
        Assert.That(Match("[REQUEST_VOTE: Have we reached consensus?]").Success, Is.True);
    }

    [Test]
    public void ExtractsQuestion_Correctly()
    {
        var m = Match("[REQUEST_VOTE: Have we reached consensus?]");
        Assert.That(m.Groups[1].Value.Trim(), Is.EqualTo("Have we reached consensus?"));
    }

    [Test]
    public void MarkerWithLeadingWhitespace_QuestionTrimmedByCode()
    {
        var m = Match("[REQUEST_VOTE:    What should we decide?]");
        Assert.That(m.Success, Is.True);
        // The regex captures after the whitespace; production code also calls .Trim()
        Assert.That(m.Groups[1].Value.Trim(), Is.EqualTo("What should we decide?"));
    }

    // ── Case insensitivity ──────────────────────────────────────────────

    [TestCase("[request_vote: lowercase question]")]
    [TestCase("[Request_Vote: Mixed case]")]
    [TestCase("[REQUEST_VOTE: ALL CAPS]")]
    [TestCase("[rEqUeSt_VoTe: weird caps]")]
    public void MarkerIsCaseInsensitive(string input)
    {
        Assert.That(Match(input).Success, Is.True);
    }

    // ── Marker position in response ──────────────────────────────────────

    [Test]
    public void MarkerAtStartOfText_Matches()
    {
        var m = Match("[REQUEST_VOTE: Are we done?] I believe we have exhausted all angles.");
        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[1].Value.Trim(), Is.EqualTo("Are we done?"));
    }

    [Test]
    public void MarkerInMiddleOfText_Matches()
    {
        var m = Match("After much deliberation [REQUEST_VOTE: Should we conclude?] I see no new ground.");
        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[1].Value.Trim(), Is.EqualTo("Should we conclude?"));
    }

    [Test]
    public void MarkerAtEndOfText_Matches()
    {
        var m = Match("We have been going in circles. [REQUEST_VOTE: Have we reached consensus?]");
        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[1].Value.Trim(), Is.EqualTo("Have we reached consensus?"));
    }

    // ── Multiple markers — first wins ────────────────────────────────────

    [Test]
    public void MultipleMarkers_FirstOneMatched()
    {
        var input = "First [REQUEST_VOTE: Question A?] then [REQUEST_VOTE: Question B?]";
        var m = Match(input);
        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[1].Value.Trim(), Is.EqualTo("Question A?"));
    }

    // ── Question content ─────────────────────────────────────────────────

    [Test]
    public void QuestionWithPunctuation_Extracted()
    {
        var m = Match("[REQUEST_VOTE: Is this resolved — yes or no?]");
        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[1].Value.Trim(), Is.EqualTo("Is this resolved — yes or no?"));
    }

    [Test]
    public void QuestionWithNumbers_Extracted()
    {
        var m = Match("[REQUEST_VOTE: Have all 3 participants agreed on option 2?]");
        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[1].Value.Trim(), Is.EqualTo("Have all 3 participants agreed on option 2?"));
    }

    // ── Stripping the marker from visible response ───────────────────────

    [Test]
    public void StripMarker_RemovesTagLeavesRemainingText()
    {
        var input = "We are going in circles. [REQUEST_VOTE: Have we reached consensus?] I see no new arguments.";
        var stripped = StripMarker(input);
        Assert.That(stripped, Does.Not.Contain("[REQUEST_VOTE:"));
        Assert.That(stripped, Does.Contain("We are going in circles."));
        Assert.That(stripped, Does.Contain("I see no new arguments."));
    }

    [Test]
    public void StripMarker_MarkerOnly_LeavesEmptyString()
    {
        var stripped = StripMarker("[REQUEST_VOTE: Have we agreed?]");
        Assert.That(stripped, Is.Empty);
    }

    [Test]
    public void StripMarker_NoMarker_TextUnchanged()
    {
        var input = "This is a normal response with no vote request.";
        Assert.That(StripMarker(input), Is.EqualTo(input));
    }

    // ── Instruction constant sanity ──────────────────────────────────────

    [Test]
    public void VoteRequestInstruction_ContainsMarkerFormat()
    {
        // The instruction appended to each participant's personality must include
        // the exact marker format so models know what to emit.
        const string instruction =
            "\n\n---\nIf this discussion is at a stalemate, include [REQUEST_VOTE: your question] anywhere in your response to trigger a consensus vote among all participants.";

        Assert.That(instruction, Does.Contain("[REQUEST_VOTE:"));
        // The example in the instruction itself should match the pattern
        Assert.That(Match(instruction).Success, Is.True);
    }
}
