using System.Collections.ObjectModel;
using LLMThinkTank.Core.Models;

namespace LLMThinkTank.Core.Services;

/// <summary>
/// Manages the lifecycle of conversation tabs: creation, activation, closure, and persistence.
/// Serves as the single source of truth for all open conversations and the currently active tab.
/// On construction, rehydrates persisted conversations from <see cref="LlmThinkTankSettingsService"/>.
/// Every state change is immediately persisted and broadcasts a <see cref="Changed"/> event
/// for UI components to re-render.
/// </summary>
public class ChatConversationsService
{
    private readonly LlmThinkTankSettingsService settings;

    /// <summary>Observable collection of all open conversation tabs, bound to the tab bar UI.</summary>
    public ObservableCollection<ChatConversation> Conversations { get; } = new();

    /// <summary>The currently selected conversation tab, or <c>null</c> if no conversations exist.</summary>
    public ChatConversation? ActiveConversation { get; private set; }

    /// <summary>Raised whenever conversations are created, closed, reordered, or messages are added.</summary>
    public event Action? Changed;

    /// <summary>
    /// Initializes the service by rehydrating all persisted conversations from settings.
    /// Sets the last conversation as the active tab.
    /// </summary>
    public ChatConversationsService(LlmThinkTankSettingsService settings)
    {
        this.settings = settings;

        if (this.settings is SettingsService s)
        {
            foreach (var c in s.Conversations)
            {
                var convo = new ChatConversation(c.ChatId, c.Title);
                convo.Topic = c.Topic;
                convo.MaxTokens = c.MaxTokens;
                convo.MaxRounds = c.MaxRounds;

                if (c.Messages is not null)
                    convo.Messages.AddRange(c.Messages);

                if (c.StatusEvents is not null)
                    convo.StatusEvents.AddRange(c.StatusEvents);

                if (c.Diagnostics is not null)
                    convo.Diagnostics.AddRange(c.Diagnostics);

                foreach (var p in c.Participants)
                {
                    convo.Participants.Add(new ChatParticipant(
                        p.ParticipantId,
                        p.TemplateId,
                        p.ProviderId,
                        p.DisplayName,
                        p.PersonalityMarkdown,
                        p.AuthOverrideJson));
                }

                Conversations.Add(convo);
            }

            ActiveConversation = Conversations.LastOrDefault();
        }
    }

    /// <summary>Generates a new unique identifier as a 32-character hex GUID string.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Switches the active conversation tab and persists the change.
    /// </summary>
    /// <param name="chatId">The conversation ID to activate.</param>
    public void SetActive(string chatId)
    {
        ActiveConversation = Conversations.FirstOrDefault(c => c.ChatId == chatId);
        Persist();
        Changed?.Invoke();
    }

    /// <summary>
    /// Creates a new conversation tab with the given title, adds it to the collection,
    /// sets it as active, and persists the updated state.
    /// </summary>
    /// <param name="title">Display title for the new conversation tab.</param>
    /// <returns>The newly created conversation instance.</returns>
    public ChatConversation CreateConversation(string title)
    {
        var chatId = NewId();
        var convo = new ChatConversation(chatId, title);
        Conversations.Add(convo);
        ActiveConversation = convo;
        Persist();
        Changed?.Invoke();
        return convo;
    }

    /// <summary>
    /// Removes a conversation tab from the collection. If the closed tab was active,
    /// activates the last remaining conversation (or sets active to <c>null</c>).
    /// </summary>
    /// <param name="chatId">The conversation ID to close and remove.</param>
    public void CloseConversation(string chatId)
    {
        var convo = Conversations.FirstOrDefault(c => c.ChatId == chatId);
        if (convo is null)
            return;

        Conversations.Remove(convo);
        if (ActiveConversation?.ChatId == chatId)
            ActiveConversation = Conversations.LastOrDefault();
        Persist();
        Changed?.Invoke();
    }

    /// <summary>
    /// Persists current state and notifies subscribers. Call after modifying conversation
    /// content (e.g., adding messages, changing participants) outside of this service.
    /// </summary>
    public void NotifyChanged()
    {
        Persist();
        Changed?.Invoke();
    }

    /// <summary>
    /// Serializes all conversations (participants, messages, diagnostics) into
    /// <see cref="PersistedConversation"/> records and writes to the settings service.
    /// </summary>
    private void Persist()
    {
        if (settings is not SettingsService s)
            return;

        s.SetConversations(Conversations.Select(c => new PersistedConversation(
            c.ChatId,
            c.Title,
            c.Participants.Select(p => new PersistedParticipant(
                p.ParticipantId,
                p.TemplateId,
                p.ProviderId,
                p.DisplayName,
                p.PersonalityMarkdown,
                p.AuthOverrideJson)).ToList(),
            Topic: c.Topic,
            Messages: c.Messages.Count == 0 ? null : new List<PersistedMessage>(c.Messages))
        {
            StatusEvents = c.StatusEvents.Count == 0 ? null : new List<PersistedStatusEvent>(c.StatusEvents),
            Diagnostics = c.Diagnostics.Count == 0 ? null : new List<PersistedStatusEvent>(c.Diagnostics),
            MaxTokens = c.MaxTokens,
            MaxRounds = c.MaxRounds
        }));
    }
}
