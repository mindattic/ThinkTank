using NUnit.Framework;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests.Lamp;

/// <summary>
/// TT-T47: the spend-limit settings. Each is stored under its manifest name, reloads unchanged and is the number
/// <see cref="ThinkTankSettingsService.Spend"/> hands the guard and the services.
/// </summary>
public partial class ThinkTankSettingTests
{
    private static SpendLimits SpendAfter(Action<ThinkTankSettings> change, string path, int expected)
    {
        var store = TestLamp.Store();
        store.Update(change);
        AssertStoredUnderManifestName(store, path, expected);
        return Service(store).Spend;
    }

    [Test]
    public void Setting_MaxParticipants_RoundTripsAndIsTheSeatCap()
    {
        Assert.That(new ThinkTankSettings().MaxParticipants, Is.EqualTo(6));
        Assert.That(SpendAfter(s => s.MaxParticipants = 4, "maxParticipants", 4).MaxParticipants, Is.EqualTo(4));
    }

    [Test]
    public void Setting_MaxRoundsPerRun_RoundTripsAndIsTheRoundCap()
    {
        Assert.That(new ThinkTankSettings().MaxRoundsPerRun, Is.EqualTo(20));
        Assert.That(SpendAfter(s => s.MaxRoundsPerRun = 7, "maxRoundsPerRun", 7).MaxRoundsPerRun, Is.EqualTo(7));
    }

    [Test]
    public async Task Setting_MaxTokensPerReply_RoundTripsAndCapsTheRequest()
    {
        Assert.That(new ThinkTankSettings().MaxTokensPerReply, Is.EqualTo(4096));
        var store = TestLamp.Store();
        store.Update(s => s.MaxTokensPerReply = 512);
        AssertStoredUnderManifestName(store, "maxTokensPerReply", 512);

        // The conversation asks for 8000; the request carries the cap.
        var (svc, http) = Wire(Service(store, ("openai", "sk-o")));
        await CallQuietly(() => svc.CallProvider("openai", "", null, "topic", new List<ThinkTank.Core.Models.SharedTurn>(), 8000));
        Assert.That(http.Requests.Single().Body, Does.Contain("512").And.Not.Contain("8000"));
    }

    [Test]
    public void Setting_MaxConcurrentRuns_RoundTripsAndIsTheRunCap()
    {
        Assert.That(new ThinkTankSettings().MaxConcurrentRuns, Is.EqualTo(2));
        Assert.That(SpendAfter(s => s.MaxConcurrentRuns = 1, "maxConcurrentRuns", 1).MaxConcurrentRuns, Is.EqualTo(1));
    }

    [Test]
    public void Setting_DailyCallBudget_RoundTripsAndIsTheBudget()
    {
        Assert.That(new ThinkTankSettings().DailyCallBudget, Is.EqualTo(300));
        Assert.That(SpendAfter(s => s.DailyCallBudget = 50, "dailyCallBudget", 50).DailyCallBudget, Is.EqualTo(50));
    }

    [Test]
    public void Setting_ActionPermitLimit_RoundTripsAndIsTheActionLimit()
    {
        Assert.That(new ThinkTankSettings().ActionPermitLimit, Is.EqualTo(10));
        Assert.That(SpendAfter(s => s.ActionPermitLimit = 3, "actionPermitLimit", 3).ActionPermitLimit, Is.EqualTo(3));
    }

    [Test]
    public void Setting_ActionWindowSeconds_RoundTripsAndIsTheActionWindow()
    {
        Assert.That(new ThinkTankSettings().ActionWindowSeconds, Is.EqualTo(60));
        Assert.That(SpendAfter(s => s.ActionWindowSeconds = 30, "actionWindowSeconds", 30).ActionWindowSeconds, Is.EqualTo(30));
    }
}
