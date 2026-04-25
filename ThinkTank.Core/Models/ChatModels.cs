using System.Collections.ObjectModel;

namespace LLMThinkTank.Core.Models;

/// <summary>
/// A reusable template for creating roundtable participants. Templates define which
/// LLM provider to use, what personality prompt to inject, and optional auth overrides.
/// Default templates (one per provider) are auto-created on first launch; users can
/// create custom templates with alternate personalities or model configurations.
/// </summary>
/// <param name="TemplateId">Unique identifier for this template.</param>
/// <param name="ProviderId">LLM provider identifier (e.g., "openai", "claude") used for API dispatch.</param>
/// <param name="DisplayName">Name shown in the UI participant list (e.g., "ChatGPT", "Claude").</param>
/// <param name="PersonalityMarkdown">System prompt defining the AI's roundtable persona and response constraints.</param>
/// <param name="AuthOverrideJson">Optional JSON blob to override the default provider auth (API key, model, max tokens).</param>
/// <param name="IsDefault">Whether this is a built-in default template that should not be deleted by the user.</param>
public record ParticipantTemplate(
    string TemplateId,
    string ProviderId,
    string DisplayName,
    string PersonalityMarkdown,
    string? AuthOverrideJson,
    bool IsDefault = false);

/// <summary>
/// An active participant in a specific conversation. Created by instantiating a
/// <see cref="ParticipantTemplate"/> into a conversation, with its own unique
/// <see cref="ParticipantId"/> to track turns and responses within that conversation.
/// </summary>
/// <param name="ParticipantId">Unique identifier for this participant instance within its conversation.</param>
/// <param name="TemplateId">The <see cref="ParticipantTemplate.TemplateId"/> this participant was created from.</param>
/// <param name="ProviderId">LLM provider identifier used for API dispatch.</param>
/// <param name="DisplayName">Display name shown in the chat bubble header.</param>
/// <param name="PersonalityMarkdown">System prompt injected as the personality for this participant's API calls.</param>
/// <param name="AuthOverrideJson">Optional auth override JSON, or <c>null</c> to use the provider's default credentials.</param>
public record ChatParticipant(
    string ParticipantId,
    string TemplateId,
    string ProviderId,
    string DisplayName,
    string PersonalityMarkdown,
    string? AuthOverrideJson);

/// <summary>
/// Represents a single conversation tab in the UI. Each conversation has its own topic,
/// set of AI participants, message history, and diagnostic log. Conversations are
/// persisted to disk via <see cref="PersistenceModels"/> and restored on app launch.
/// </summary>
public class ChatConversation
{
    /// <summary>Unique identifier for this conversation, generated as a GUID hex string.</summary>
    public string ChatId { get; }

    /// <summary>User-visible conversation title displayed on the tab header.</summary>
    public string Title { get; set; }

    /// <summary>Observable collection of AI participants in this roundtable discussion.</summary>
    public ObservableCollection<ChatParticipant> Participants { get; } = new();

    /// <summary>The discussion topic that all participants are prompted to respond to.</summary>
    public string? Topic { get; set; }

    /// <summary>Per-conversation max token limit override. When set, overrides the provider default for all participants.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Per-conversation max rounds override. When set, the conversation auto-pauses after this many rounds.</summary>
    public int? MaxRounds { get; set; }

    /// <summary>Ordered list of all messages (both successful responses and errors) in this conversation.</summary>
    public List<PersistedMessage> Messages { get; } = new();

    /// <summary>Timestamped status events (e.g., "Round 1 started", "Claude is responding...").</summary>
    public List<PersistedStatusEvent> StatusEvents { get; } = new();

    /// <summary>Timestamped diagnostic entries from API calls (redacted responses, errors).</summary>
    public List<PersistedStatusEvent> Diagnostics { get; } = new();

    /// <summary>
    /// Initializes a new conversation with the specified identifier and title.
    /// </summary>
    /// <param name="chatId">Unique conversation identifier.</param>
    /// <param name="title">Display title for the conversation tab.</param>
    public ChatConversation(string chatId, string title)
    {
        ChatId = chatId;
        Title = title;
    }
}
