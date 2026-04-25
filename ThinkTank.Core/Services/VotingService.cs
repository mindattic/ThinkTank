using ThinkTank.Core.Models;
using MindAttic.Legion;

namespace ThinkTank.Core.Services;

public class VotingService(LLMVotingService llmVoting)
{
    public Task<VotingResult> VoteAsync(
        IEnumerable<ChatParticipant> participants,
        string question,
        string context,
        List<string> options,
        Quorum quorum,
        CancellationToken ct = default)
    {
        var voters = participants.Select(p => new VoterProfile
        {
            VoterId = p.ParticipantId,
            Name = p.DisplayName,
            ProviderId = p.ProviderId,
            PersonalityMarkdown = p.PersonalityMarkdown,
            ApiKeyOverride = ExtractField(p.AuthOverrideJson, "apiKey"),
            ModelOverride = ExtractField(p.AuthOverrideJson, "model")
        }).ToList();

        var request = new VoteRequest
        {
            Question = question,
            Context = context,
            Options = options
        };

        return llmVoting.VoteWithProfilesAsync(request, quorum, voters, ct);
    }

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
