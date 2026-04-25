namespace LLMThinkTank.Core.Models;

/// <summary>
/// Serializable snapshot of a conversation for JSON persistence.
/// Stored in the application's Settings.json file alongside provider auth and templates.
/// On app launch, each <see cref="PersistedConversation"/> is rehydrated into a
/// <see cref="ChatConversation"/> with full participant and message state.
/// </summary>
/// <param name="ChatId">Unique conversation identifier (GUID hex string).</param>
/// <param name="Title">Display title for the conversation tab.</param>
/// <param name="Participants">Ordered list of participants in this conversation.</param>
/// <param name="Topic">The discussion topic, or <c>null</c> if not yet set.</param>
/// <param name="Messages">Message history, or <c>null</c> if the conversation has no messages yet.</param>
public sealed record PersistedConversation(
    string ChatId,
    string Title,
    List<PersistedParticipant> Participants,
    string? Topic,
    List<PersistedMessage>? Messages)
{
    /// <summary>Timestamped status events (round transitions, connectivity notices), or <c>null</c> if none.</summary>
    public List<PersistedStatusEvent>? StatusEvents { get; init; }

    /// <summary>Timestamped diagnostic entries (redacted API responses, errors), or <c>null</c> if none.</summary>
    public List<PersistedStatusEvent>? Diagnostics { get; init; }

    /// <summary>Per-conversation max token limit override, or <c>null</c> to use global default.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Per-conversation max rounds override, or <c>null</c> to use global default.</summary>
    public int? MaxRounds { get; init; }
}

/// <summary>
/// A single persisted chat message capturing one AI participant's response or error.
/// </summary>
/// <param name="ParticipantId">Identifier of the participant who produced this message.</param>
/// <param name="Text">Response text content, or error description if <paramref name="IsError"/> is true.</param>
/// <param name="Round">Zero-based discussion round number.</param>
/// <param name="IsError">Whether this message represents a failed API call.</param>
public sealed record PersistedMessage(
    string ParticipantId,
    string Text,
    int Round,
    bool IsError);

/// <summary>
/// A timestamped event entry used for both status updates and diagnostic logs.
/// </summary>
/// <param name="Timestamp">UTC timestamp when the event occurred.</param>
/// <param name="Text">Human-readable event description.</param>
public sealed record PersistedStatusEvent(
    DateTimeOffset Timestamp,
    string Text);

/// <summary>
/// Serializable snapshot of a conversation participant for JSON persistence.
/// Contains all fields needed to reconstruct a <see cref="ChatParticipant"/> on load.
/// </summary>
/// <param name="ParticipantId">Unique identifier for this participant within its conversation.</param>
/// <param name="TemplateId">The template this participant was created from.</param>
/// <param name="ProviderId">LLM provider identifier for API dispatch.</param>
/// <param name="DisplayName">Display name shown in chat bubbles.</param>
/// <param name="PersonalityMarkdown">System prompt defining the participant's roundtable persona.</param>
/// <param name="AuthOverrideJson">Optional auth override JSON, or <c>null</c> for provider defaults.</param>
public sealed record PersistedParticipant(
    string ParticipantId,
    string TemplateId,
    string ProviderId,
    string DisplayName,
    string PersonalityMarkdown,
    string? AuthOverrideJson);
