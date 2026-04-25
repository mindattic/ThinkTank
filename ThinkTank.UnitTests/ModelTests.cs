using NUnit.Framework;
using LLMThinkTank.Core.Models;

namespace LLMThinkTank.UnitTests;

[TestFixture]
public class ModelTests
{
    // ── AppearanceMode enum ─────────────────────────────────────────────

    [Test]
    public void AppearanceMode_Has18Values()
    {
        var values = Enum.GetValues<AppearanceMode>();
        Assert.That(values, Has.Length.EqualTo(18));
    }

    [TestCase("Dark")]
    [TestCase("Light")]
    [TestCase("Matrix")]
    [TestCase("Neon")]
    [TestCase("Dracula")]
    [TestCase("Mono")]
    [TestCase("Aurora")]
    [TestCase("Ember")]
    [TestCase("Ocean")]
    [TestCase("Forest")]
    public void AppearanceMode_ContainsExpectedTheme(string themeName)
    {
        Assert.That(Enum.TryParse<AppearanceMode>(themeName, out _), Is.True);
    }

    // ── ChatLogEntry record ─────────────────────────────────────────────

    [Test]
    public void ChatLogEntry_DefaultIsErrorFalse()
    {
        var entry = new ChatLogEntry(DateTimeOffset.UtcNow, "source", "text");
        Assert.That(entry.IsError, Is.False);
    }

    [Test]
    public void ChatLogEntry_RecordEquality()
    {
        var ts = DateTimeOffset.UtcNow;
        var a = new ChatLogEntry(ts, "src", "msg", true);
        var b = new ChatLogEntry(ts, "src", "msg", true);
        Assert.That(a, Is.EqualTo(b));
    }

    // ── ProviderAuthConfig record ───────────────────────────────────────

    [Test]
    public void ProviderAuthConfig_RecordEquality()
    {
        var a = new ProviderAuthConfig("openai", "{\"apiKey\":\"key\"}");
        var b = new ProviderAuthConfig("openai", "{\"apiKey\":\"key\"}");
        Assert.That(a, Is.EqualTo(b));
    }

    // ── PersistedTurn record ────────────────────────────────────────────

    [Test]
    public void PersistedTurn_StoresAllFields()
    {
        var turn = new PersistedTurn("p1", "Hello", 3, true);
        Assert.That(turn.ParticipantId, Is.EqualTo("p1"));
        Assert.That(turn.Text, Is.EqualTo("Hello"));
        Assert.That(turn.Round, Is.EqualTo(3));
        Assert.That(turn.IsError, Is.True);
    }

    // ── ParticipantTemplate record ──────────────────────────────────────

    [Test]
    public void ParticipantTemplate_IsDefaultDefaultsFalse()
    {
        var t = new ParticipantTemplate("id", "prov", "name", "personality", null);
        Assert.That(t.IsDefault, Is.False);
    }

    [Test]
    public void ParticipantTemplate_WithExpression()
    {
        var original = new ParticipantTemplate("id", "prov", "name", "personality", null, true);
        var modified = original with { DisplayName = "New Name" };
        Assert.That(modified.DisplayName, Is.EqualTo("New Name"));
        Assert.That(modified.IsDefault, Is.True);
    }

    // ── ChatParticipant record ──────────────────────────────────────────

    [Test]
    public void ChatParticipant_StoresAllFields()
    {
        var p = new ChatParticipant("pid", "tid", "openai", "ChatGPT", "Be curious", null);
        Assert.That(p.ParticipantId, Is.EqualTo("pid"));
        Assert.That(p.TemplateId, Is.EqualTo("tid"));
        Assert.That(p.ProviderId, Is.EqualTo("openai"));
        Assert.That(p.DisplayName, Is.EqualTo("ChatGPT"));
        Assert.That(p.PersonalityMarkdown, Is.EqualTo("Be curious"));
        Assert.That(p.AuthOverrideJson, Is.Null);
    }

    // ── ChatConversation class ──────────────────────────────────────────

    [Test]
    public void ChatConversation_InitializesEmpty()
    {
        var convo = new ChatConversation("abc", "Test");
        Assert.That(convo.ChatId, Is.EqualTo("abc"));
        Assert.That(convo.Title, Is.EqualTo("Test"));
        Assert.That(convo.Participants, Is.Empty);
        Assert.That(convo.Messages, Is.Empty);
        Assert.That(convo.StatusEvents, Is.Empty);
        Assert.That(convo.Diagnostics, Is.Empty);
        Assert.That(convo.Topic, Is.Null);
        Assert.That(convo.MaxTokens, Is.Null);
        Assert.That(convo.MaxRounds, Is.Null);
    }

    [Test]
    public void ChatConversation_CanAddParticipants()
    {
        var convo = new ChatConversation("abc", "Test");
        convo.Participants.Add(new ChatParticipant("p1", "t1", "openai", "GPT", "Be nice", null));
        Assert.That(convo.Participants, Has.Count.EqualTo(1));
    }

    [Test]
    public void ChatConversation_TitleIsMutable()
    {
        var convo = new ChatConversation("abc", "Original");
        convo.Title = "Updated";
        Assert.That(convo.Title, Is.EqualTo("Updated"));
    }

    // ── LlmModel class ─────────────────────────────────────────────────

    [Test]
    public void LlmModel_DefaultsToEmpty()
    {
        var model = new LlmModel();
        Assert.That(model.Id, Is.EqualTo(""));
        Assert.That(model.Name, Is.EqualTo(""));
        Assert.That(model.Avatar, Is.EqualTo(""));
        Assert.That(model.Personality, Is.EqualTo(""));
        Assert.That(model.ApiKeyUrl, Is.EqualTo(""));
    }

    // ── SharedTurn class ────────────────────────────────────────────────

    [Test]
    public void SharedTurn_DefaultsToEmpty()
    {
        var turn = new SharedTurn();
        Assert.That(turn.ModelId, Is.EqualTo(""));
        Assert.That(turn.ModelName, Is.EqualTo(""));
        Assert.That(turn.Text, Is.EqualTo(""));
        Assert.That(turn.Round, Is.EqualTo(0));
    }

    // ── ConversationMessage class ───────────────────────────────────────

    [Test]
    public void ConversationMessage_DefaultsToEmpty()
    {
        var msg = new ConversationMessage();
        Assert.That(msg.ModelId, Is.EqualTo(""));
        Assert.That(msg.Text, Is.EqualTo(""));
        Assert.That(msg.Round, Is.EqualTo(0));
        Assert.That(msg.IsError, Is.False);
    }

    // ── PersistedConversation record ────────────────────────────────────

    [Test]
    public void PersistedConversation_StoresFields()
    {
        var participants = new List<PersistedParticipant>
        {
            new("p1", "t1", "openai", "GPT", "Be nice", null)
        };
        var messages = new List<PersistedMessage>
        {
            new("p1", "Hello", 0, false)
        };

        var convo = new PersistedConversation("chat1", "Title", participants, "Topic", messages)
        {
            MaxTokens = 1024,
            MaxRounds = 5
        };

        Assert.That(convo.ChatId, Is.EqualTo("chat1"));
        Assert.That(convo.Title, Is.EqualTo("Title"));
        Assert.That(convo.Topic, Is.EqualTo("Topic"));
        Assert.That(convo.Participants, Has.Count.EqualTo(1));
        Assert.That(convo.Messages, Has.Count.EqualTo(1));
        Assert.That(convo.MaxTokens, Is.EqualTo(1024));
        Assert.That(convo.MaxRounds, Is.EqualTo(5));
    }

    [Test]
    public void PersistedMessage_StoresFields()
    {
        var msg = new PersistedMessage("p1", "Error!", 2, true);
        Assert.That(msg.ParticipantId, Is.EqualTo("p1"));
        Assert.That(msg.Text, Is.EqualTo("Error!"));
        Assert.That(msg.Round, Is.EqualTo(2));
        Assert.That(msg.IsError, Is.True);
    }

    [Test]
    public void PersistedStatusEvent_StoresFields()
    {
        var ts = DateTimeOffset.UtcNow;
        var evt = new PersistedStatusEvent(ts, "Round started");
        Assert.That(evt.Timestamp, Is.EqualTo(ts));
        Assert.That(evt.Text, Is.EqualTo("Round started"));
    }
}
