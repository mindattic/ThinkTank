using NUnit.Framework;
using LLMThinkTank.Core.Services;

namespace LLMThinkTank.UnitTests;

[TestFixture]
public class ChatStorageTests
{
    private string testChatId = null!;
    private string chatFolder = null!;

    [SetUp]
    public void SetUp()
    {
        testChatId = $"test_{Guid.NewGuid():N}";
        chatFolder = ChatStorage.GetChatFolder(testChatId);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(chatFolder))
            Directory.Delete(chatFolder, recursive: true);
    }

    // ── Path helpers ────────────────────────────────────────────────────

    [Test]
    public void GetChatFolder_ContainsChatId()
    {
        var folder = ChatStorage.GetChatFolder("abc123");
        Assert.That(folder, Does.EndWith(Path.Combine("Conversations", "abc123")));
    }

    [Test]
    public void GetChatJsonPath_EndsWith_ChatJson()
    {
        var path = ChatStorage.GetChatJsonPath("abc123");
        Assert.That(path, Does.EndWith("chat.json"));
    }

    [Test]
    public void GetChatJsonPath_InsideChatFolder()
    {
        var folder = ChatStorage.GetChatFolder("abc123");
        var jsonPath = ChatStorage.GetChatJsonPath("abc123");
        Assert.That(jsonPath, Does.StartWith(folder));
    }

    [Test]
    public void GetPerspectivePath_ContainsModelId()
    {
        var path = ChatStorage.GetPerspectivePath("abc123", "openai");
        Assert.That(path, Does.EndWith("openai.md"));
    }

    // ── AppendChatJsonAsync ─────────────────────────────────────────────

    [Test]
    public async Task AppendChatJsonAsync_CreatesFileAndFolder()
    {
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "turn", text = "hello" });

        Assert.That(File.Exists(ChatStorage.GetChatJsonPath(testChatId)), Is.True);
    }

    [Test]
    public async Task AppendChatJsonAsync_AppendsMultipleEntries()
    {
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "turn", text = "first" });
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "turn", text = "second" });

        var json = await File.ReadAllTextAsync(ChatStorage.GetChatJsonPath(testChatId));
        Assert.That(json, Does.Contain("first"));
        Assert.That(json, Does.Contain("second"));
    }

    // ── ReadPerspectiveAsync / WritePerspectiveAsync ────────────────────

    [Test]
    public async Task ReadPerspectiveAsync_MissingFile_ReturnsEmpty()
    {
        var result = await ChatStorage.ReadPerspectiveAsync(testChatId, "openai");
        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public async Task WriteThenReadPerspective_Roundtrips()
    {
        var markdown = "# Test Perspective\n\nSome analysis here.";
        await ChatStorage.WritePerspectiveAsync(testChatId, "claude", markdown);

        var result = await ChatStorage.ReadPerspectiveAsync(testChatId, "claude");
        Assert.That(result, Is.EqualTo(markdown));
    }

    [Test]
    public async Task WritePerspectiveAsync_OverwritesExisting()
    {
        await ChatStorage.WritePerspectiveAsync(testChatId, "gemini", "version1");
        await ChatStorage.WritePerspectiveAsync(testChatId, "gemini", "version2");

        var result = await ChatStorage.ReadPerspectiveAsync(testChatId, "gemini");
        Assert.That(result, Is.EqualTo("version2"));
    }

    // ── LoadTurnsAsync ──────────────────────────────────────────────────

    [Test]
    public async Task LoadTurnsAsync_MissingFile_ReturnsEmpty()
    {
        var turns = await ChatStorage.LoadTurnsAsync(testChatId);
        Assert.That(turns, Is.Empty);
    }

    [Test]
    public async Task LoadTurnsAsync_ParsesTurnEntries()
    {
        await ChatStorage.AppendChatJsonAsync(testChatId, new
        {
            type = "turn",
            participantId = "p1",
            text = "Hello world",
            round = 1,
            isError = false
        });

        var turns = await ChatStorage.LoadTurnsAsync(testChatId);
        Assert.That(turns, Has.Count.EqualTo(1));
        Assert.That(turns[0].ParticipantId, Is.EqualTo("p1"));
        Assert.That(turns[0].Text, Is.EqualTo("Hello world"));
        Assert.That(turns[0].Round, Is.EqualTo(1));
        Assert.That(turns[0].IsError, Is.False);
    }

    [Test]
    public async Task LoadTurnsAsync_IgnoresNonTurnEntries()
    {
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "status", text = "Round started" });
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "turn", participantId = "p1", text = "Hi", round = 0 });

        var turns = await ChatStorage.LoadTurnsAsync(testChatId);
        Assert.That(turns, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task LoadTurnsAsync_MultipleTurns_PreservesOrder()
    {
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "turn", participantId = "p1", text = "First", round = 0 });
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "turn", participantId = "p2", text = "Second", round = 0 });
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "turn", participantId = "p1", text = "Third", round = 1 });

        var turns = await ChatStorage.LoadTurnsAsync(testChatId);
        Assert.That(turns, Has.Count.EqualTo(3));
        Assert.That(turns[0].Text, Is.EqualTo("First"));
        Assert.That(turns[1].Text, Is.EqualTo("Second"));
        Assert.That(turns[2].Text, Is.EqualTo("Third"));
    }

    [Test]
    public async Task LoadTurnsAsync_ErrorTurnParsed()
    {
        await ChatStorage.AppendChatJsonAsync(testChatId, new
        {
            type = "turn",
            participantId = "p1",
            text = "API error",
            round = 0,
            isError = true
        });

        var turns = await ChatStorage.LoadTurnsAsync(testChatId);
        Assert.That(turns[0].IsError, Is.True);
    }

    [Test]
    public async Task LoadTurnsAsync_MissingFields_DefaultsGracefully()
    {
        await ChatStorage.AppendChatJsonAsync(testChatId, new { type = "turn" });

        var turns = await ChatStorage.LoadTurnsAsync(testChatId);
        Assert.That(turns, Has.Count.EqualTo(1));
        Assert.That(turns[0].ParticipantId, Is.EqualTo(""));
        Assert.That(turns[0].Text, Is.EqualTo(""));
        Assert.That(turns[0].Round, Is.EqualTo(0));
        Assert.That(turns[0].IsError, Is.False);
    }
}
