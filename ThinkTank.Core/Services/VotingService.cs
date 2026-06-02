using ThinkTank.Core.Models;
using MindAttic.Legion;

namespace ThinkTank.Core.Services;

/// <summary>
/// Thin adapter that maps a chat's <see cref="ChatParticipant"/> roster into the
/// <see cref="VoterProfile"/> shape MindAttic.Legion expects, then delegates to
/// <see cref="LlmVotingService.VoteWithProfilesAsync"/>. Used by the "Call Vote"
/// flow in <c>Chat.razor</c> and by the auto-vote path triggered by participants
/// emitting <c>[REQUEST_VOTE: ...]</c> mid-discussion.
/// <para>
/// Before each call, refreshes the singleton <see cref="VotingConfiguration"/>'s
/// <see cref="VotingConfiguration.ApiKeys"/> and <see cref="VotingConfiguration.ModelOverrides"/>
/// from the live <see cref="ThinkTankSettingsService"/>. That way keys added/rotated via
/// the Settings UI after startup are visible to the singleton vote-time, not just at
/// DI construction.
/// </para>
/// </summary>
public class VotingService(LlmVotingService llmVoting, ThinkTankSettingsService settings, VotingConfiguration votingConfig, PsychometricProfileService psychometrics)
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
        RefreshVotingConfigFromSettings();

        var voters = MapToVoterProfilesWithPsychometrics(participants);

        var request = new VoteRequest
        {
            Question = question,
            Context = context,
            Options = options
        };

        return llmVoting.VoteWithProfilesAsync(request, quorum, voters, ct);
    }

    /// <summary>
    /// Overwrites <see cref="VotingConfiguration.ApiKeys"/> and
    /// <see cref="VotingConfiguration.ModelOverrides"/> with the values currently
    /// known to <see cref="ThinkTankSettingsService"/>. The singleton instance is
    /// also held by <c>LlmVotingProvider</c> and the <c>LegionClient</c> key-resolver
    /// closure, so mutating it here propagates to both.
    /// </summary>
    private void RefreshVotingConfigFromSettings()
    {
        var apiKeys = new Dictionary<string, string>();
        var modelOverrides = new Dictionary<string, string>();
        foreach (var providerId in settings.ProviderAuth.Keys)
        {
            var key = settings.GetKeyForProvider(providerId, null);
            if (!string.IsNullOrWhiteSpace(key))
                apiKeys[providerId] = key;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(settings.GetAuthJson(providerId));
                if (doc.RootElement.TryGetProperty("model", out var m) && m.GetString() is { Length: > 0 } model)
                    modelOverrides[providerId] = model;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ThinkTank.Voting] failed to read model override for '{providerId}': {ex.Message}");
            }
        }
        votingConfig.ApiKeys = apiKeys;
        votingConfig.ModelOverrides = modelOverrides;
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

    /// <summary>
    /// Like <see cref="MapToVoterProfiles"/>, but additionally folds each persona's
    /// psychometric profile into the vote: the prose brief is appended to
    /// <see cref="VoterProfile.PersonalityMarkdown"/> (so the voter reasons in character),
    /// and the raw <see cref="VoterProfile.Psychometrics"/> is set (so Legion's
    /// <c>PsychometricVoteAnalysis</c> can segment the result by trait composition).
    /// Personas without a scored profile are mapped exactly as before.
    /// </summary>
    public List<VoterProfile> MapToVoterProfilesWithPsychometrics(IEnumerable<ChatParticipant> participants)
        => participants.Select(p =>
        {
            // Resolve the persona's profile once and render from it, rather than looking it
            // up twice (once for the prose, once for the raw object).
            var profile = psychometrics.GetProfile(p.EffectivePersonaId);
            return new VoterProfile
            {
                VoterId = p.ParticipantId,
                Name = p.DisplayName,
                ProviderId = p.ProviderId,
                PersonalityMarkdown = p.PersonalityMarkdown + PsychometricNarrator.Describe(profile),
                ApiKeyOverride = ExtractField(p.AuthOverrideJson, "apiKey"),
                ModelOverride = ExtractField(p.AuthOverrideJson, "model"),
                Psychometrics = profile
            };
        }).ToList();

    private static string? ExtractField(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var el) ? el.GetString() : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ThinkTank.Voting] malformed auth override (field '{field}'): {ex.Message}");
            return null;
        }
    }
}
