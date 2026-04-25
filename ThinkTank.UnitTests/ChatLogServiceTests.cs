using NUnit.Framework;
using LLMThinkTank.Core.Services;

namespace LLMThinkTank.UnitTests;

[TestFixture]
public class ChatLogServiceTests
{
    private ChatLogService sut = null!;

    [SetUp]
    public void SetUp()
    {
        sut = new ChatLogService();
    }

    [Test]
    public void Entries_InitiallyEmpty()
    {
        Assert.That(sut.Entries, Is.Empty);
    }

    [Test]
    public void Add_AppendsEntry()
    {
        sut.Add("openai", "test message");
        Assert.That(sut.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void Add_SetsCorrectProperties()
    {
        sut.Add("claude", "some text", isError: true);

        var entry = sut.Entries[0];
        Assert.That(entry.Source, Is.EqualTo("claude"));
        Assert.That(entry.Text, Is.EqualTo("some text"));
        Assert.That(entry.IsError, Is.True);
        Assert.That(entry.Timestamp, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public void Add_DefaultIsErrorFalse()
    {
        sut.Add("system", "info");
        Assert.That(sut.Entries[0].IsError, Is.False);
    }

    [Test]
    public void Add_FiresChangedEvent()
    {
        var fired = false;
        sut.Changed += () => fired = true;
        sut.Add("test", "msg");
        Assert.That(fired, Is.True);
    }

    [Test]
    public void Add_MultipleEntries_PreservesOrder()
    {
        sut.Add("a", "first");
        sut.Add("b", "second");
        sut.Add("c", "third");

        Assert.That(sut.Entries, Has.Count.EqualTo(3));
        Assert.That(sut.Entries[0].Source, Is.EqualTo("a"));
        Assert.That(sut.Entries[1].Source, Is.EqualTo("b"));
        Assert.That(sut.Entries[2].Source, Is.EqualTo("c"));
    }

    [Test]
    public void Clear_RemovesAllEntries()
    {
        sut.Add("a", "1");
        sut.Add("b", "2");
        sut.Clear();

        Assert.That(sut.Entries, Is.Empty);
    }

    [Test]
    public void Clear_FiresChangedEvent()
    {
        sut.Add("x", "y");
        var fired = false;
        sut.Changed += () => fired = true;
        sut.Clear();
        Assert.That(fired, Is.True);
    }

    [Test]
    public void Clear_OnEmpty_StillFiresChanged()
    {
        var fired = false;
        sut.Changed += () => fired = true;
        sut.Clear();
        Assert.That(fired, Is.True);
    }

    [Test]
    public void Add_SetsTimestampNearNow()
    {
        var before = DateTimeOffset.UtcNow;
        sut.Add("test", "msg");
        var after = DateTimeOffset.UtcNow;

        Assert.That(sut.Entries[0].Timestamp, Is.GreaterThanOrEqualTo(before));
        Assert.That(sut.Entries[0].Timestamp, Is.LessThanOrEqualTo(after));
    }
}
