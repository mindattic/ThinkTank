using ThinkTank.Core.Models;
using MindAttic.Legion;

namespace ThinkTank.Core.Services;

/// <summary>
/// Thin adapter that maps a chat's <see cref="ChatParticipant"/> roster into the
/// <see cref="VoterProfile"/> shape MindAttic.Legion expects, then delegates to
/// <see cref="LlmVotingService.VoteWithProfilesAsync"/>. Used by the "Call Vote"
/// flow in <c>Chat.razor</c> and by the auto-vote path triggered by participants
/// emitting <c>[REQUEST_VOTE: ...]</c> mid-discussion.
/// </summary>
public class VotingService(LlmVotingService llmVoting)
{
    /// <summary>
    /// Polls every participant on <paramref name="question"/> using their existing
    /// persona + per-participant auth/model overrides (if any). Returns the
    /// aggregated <see cref="VotingResult"/> from Legion.
    /// </summary>
    public Task<VotingResult> VoteAsync(
        IEnumerable<ChatParticipant> participants,
        string question,
        string context,
        List<string> options,
        Quorum quorum,
        CancellationToken ct = default)
    {
        var voters = MapToVoterProfiles(participants);

        var request = new VoteRequest
        {
            Question = question,
            Context = context,
            Options = options
        };

        return llmVoting.VoteWithProfilesAsync(request, quorum, voters, ct);
    }

    /// <summary>
    /// Pure mapping from chat participants to Legion voter profiles. Per-participant
    /// API key and model overrides come from <see cref="ChatParticipant.AuthOverrideJson"/>;
    /// the global-default keys live elsewhere (in <c>VotingConfiguration.ApiKeys</c>) and
    /// Legion picks them up when <see cref="VoterProfile.ApiKeyOverride"/> is null.
    /// </summary>
    public static List<VoterProfile> MapToVoterProfiles(IEnumerable<ChatParticipant> participants)
        => participants.Select(p => new VoterProfile
        {
            VoterId = p.ParticipantId,
            Name = p.DisplayName,
            ProviderId = p.ProviderId,
            PersonalityMarkdown = p.PersonalityMarkdown,
            ApiKeyOverride = ExtractField(p.AuthOverrideJson, "apiKey"),
            ModelOverride = ExtractField(p.AuthOverrideJson, "model")
        }).ToList();

    private static string? ExtractField(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var el) ? el.GetString() : null;
        }
        catch { return null; }
    }
}
