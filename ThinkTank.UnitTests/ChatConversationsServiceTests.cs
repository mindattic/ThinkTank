using NUnit.Framework;
using LLMThinkTank.Core.Models;
using LLMThinkTank.Core.Services;

namespace LLMThinkTank.UnitTests;

[TestFixture]
public class ChatConversationsServiceTests
{
    private SettingsService settings = null!;
    private ChatConversationsService sut = null!;

    [SetUp]
    public void SetUp()
    {
        settings = new SettingsService();
        settings.SetConversations(Enumerable.Empty<PersistedConversation>());
        sut = new ChatConversationsService(settings);
    }

    // ── NewId ───────────────────────────────────────────────────────────

    [Test]
    public void NewId_Returns32CharHexString()
    {
        var id = ChatConversationsService.NewId();
        Assert.That(id, Has.Length.EqualTo(32));
        Assert.That(id.All(c => "0123456789abcdef".Contains(c)), Is.True, $"'{id}' is not hex");
    }

    [Test]
    public void NewId_ReturnsUniqueValues()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => ChatConversationsService.NewId()).ToHashSet();
        Assert.That(ids.Count, Is.EqualTo(100));
    }

    // ── CreateConversation ──────────────────────────────────────────────

    [Test]
    public void CreateConversation_AddsToCollection()
    {
        sut.CreateConversation("Test");
        Assert.That(sut.Conversations, Has.Count.EqualTo(1));
    }

    [Test]
    public void CreateConversation_SetsTitle()
    {
        var convo = sut.CreateConversation("My Topic");
        Assert.That(convo.Title, Is.EqualTo("My Topic"));
    }

    [Test]
    public void CreateConversation_SetsAsActive()
    {
        var convo = sut.CreateConversation("Active");
        Assert.That(sut.ActiveConversation, Is.SameAs(convo));
    }

    [Test]
    public void CreateConversation_FiresChangedEvent()
    {
        var fired = false;
        sut.Changed += () => fired = true;
        sut.CreateConversation("Test");
        Assert.That(fired, Is.True);
    }

    [Test]
    public void CreateConversation_AssignsChatId()
    {
        var convo = sut.CreateConversation("Test");
        Assert.That(convo.ChatId, Is.Not.Null.And.Not.Empty);
        Assert.That(convo.ChatId, Has.Length.EqualTo(32));
    }

    [Test]
    public void CreateConversation_Multiple_LastIsActive()
    {
        sut.CreateConversation("First");
        var second = sut.CreateConversation("Second");
        Assert.That(sut.ActiveConversation, Is.SameAs(second));
        Assert.That(sut.Conversations, Has.Count.EqualTo(2));
    }

    // ── SetActive ───────────────────────────────────────────────────────

    [Test]
    public void SetActive_SwitchesToCorrectConversation()
    {
        var first = sut.CreateConversation("First");
        sut.CreateConversation("Second");

        sut.SetActive(first.ChatId);
        Assert.That(sut.ActiveConversation, Is.SameAs(first));
    }

    [Test]
    public void SetActive_UnknownId_SetsActiveToNull()
    {
        sut.CreateConversation("Test");
        sut.SetActive("nonexistent");
        Assert.That(sut.ActiveConversation, Is.Null);
    }

    [Test]
    public void SetActive_FiresChangedEvent()
    {
        var convo = sut.CreateConversation("Test");
        var fired = false;
        sut.Changed += () => fired = true;
        sut.SetActive(convo.ChatId);
        Assert.That(fired, Is.True);
    }

    // ── CloseConversation ───────────────────────────────────────────────

    [Test]
    public void CloseConversation_RemovesFromCollection()
    {
        var convo = sut.CreateConversation("ToClose");
        sut.CloseConversation(convo.ChatId);
        Assert.That(sut.Conversations, Is.Empty);
    }

    [Test]
    public void CloseConversation_ActiveClosedUpdatesToLast()
    {
        var first = sut.CreateConversation("First");
        var second = sut.CreateConversation("Second");

        sut.CloseConversation(second.ChatId);
        Assert.That(sut.ActiveConversation, Is.SameAs(first));
    }

    [Test]
    public void CloseConversation_ActiveClosed_NoRemaining_SetsNull()
    {
        var convo = sut.CreateConversation("Only");
        sut.CloseConversation(convo.ChatId);
        Assert.That(sut.ActiveConversation, Is.Null);
    }

    [Test]
    public void CloseConversation_NonActive_DoesNotChangeActive()
    {
        var first = sut.CreateConversation("First");
        var second = sut.CreateConversation("Second");

        sut.CloseConversation(first.ChatId);
        Assert.That(sut.ActiveConversation, Is.SameAs(second));
    }

    [Test]
    public void CloseConversation_UnknownId_IsNoOp()
    {
        sut.CreateConversation("Keep");
        sut.CloseConversation("bogus");
        Assert.That(sut.Conversations, Has.Count.EqualTo(1));
    }

    [Test]
    public void CloseConversation_FiresChangedEvent()
    {
        var convo = sut.CreateConversation("Test");
        var fired = false;
        sut.Changed += () => fired = true;
        sut.CloseConversation(convo.ChatId);
        Assert.That(fired, Is.True);
    }

    // ── NotifyChanged ───────────────────────────────────────────────────

    [Test]
    public void NotifyChanged_FiresChangedEvent()
    {
        var fired = false;
        sut.Changed += () => fired = true;
        sut.NotifyChanged();
        Assert.That(fired, Is.True);
    }

    // ── Rehydration from settings ───────────────────────────────────────

    [Test]
    public void Constructor_EmptySettings_NoConversations()
    {
        Assert.That(sut.Conversations, Is.Empty);
        Assert.That(sut.ActiveConversation, Is.Null);
    }
}
