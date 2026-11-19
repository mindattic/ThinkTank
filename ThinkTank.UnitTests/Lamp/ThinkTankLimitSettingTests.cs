using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests.Lamp;

/// <summary>
/// TT-T44: the input-limit settings. Each one is stored under its manifest name, reloads unchanged and is the
/// number <see cref="ThinkTankSettingsService.Limits"/> hands the pages (which clip on the server with it).
/// </summary>
public partial class ThinkTankSettingTests
{
    private static InputLimits LimitsAfter(Action<ThinkTankSettings> change, string path, JsonNode expected)
    {
        var store = TestLamp.Store();
        store.Update(change);
        AssertStoredUnderManifestName(store, path, expected);
        return Service(store).Limits;
    }

    [Test]
    public void Setting_MaxTopicLength_RoundTripsAndIsTheTopicLimit()
    {
        Assert.That(new ThinkTankSettings().MaxTopicLength, Is.EqualTo(2000));
        Assert.That(LimitsAfter(s => s.MaxTopicLength = 321, "maxTopicLength", 321).Topic, Is.EqualTo(321));
    }

    [Test]
    public void Setting_MaxMessageLength_RoundTripsAndIsTheMessageLimit()
    {
        Assert.That(new ThinkTankSettings().MaxMessageLength, Is.EqualTo(4000));
        Assert.That(LimitsAfter(s => s.MaxMessageLength = 654, "maxMessageLength", 654).Message, Is.EqualTo(654));
    }

    [Test]
    public void Setting_MaxPersonaLength_RoundTripsAndIsThePersonaLimit()
    {
        Assert.That(new ThinkTankSettings().MaxPersonaLength, Is.EqualTo(8000));
        Assert.That(LimitsAfter(s => s.MaxPersonaLength = 987, "maxPersonaLength", 987).Persona, Is.EqualTo(987));
    }

    [Test]
    public void Setting_MaxTitleLength_RoundTripsAndIsTheTitleLimit()
    {
        Assert.That(new ThinkTankSettings().MaxTitleLength, Is.EqualTo(100));
        Assert.That(LimitsAfter(s => s.MaxTitleLength = 42, "maxTitleLength", 42).Title, Is.EqualTo(42));
    }

    [Test]
    public void Setting_MaxVoteTextLength_RoundTripsAndIsTheVoteTextLimit()
    {
        Assert.That(new ThinkTankSettings().MaxVoteTextLength, Is.EqualTo(500));
        Assert.That(LimitsAfter(s => s.MaxVoteTextLength = 77, "maxVoteTextLength", 77).VoteText, Is.EqualTo(77));
    }

    // ---- Hosting (TT-T45) ---------------------------------------------------------------

    private static ThinkTank.Blazor.Hosting.ThinkTankHosting HostingAfter(Action<ThinkTankSettings> change, string path, JsonNode expected)
    {
        var store = TestLamp.Store();
        store.Update(change);
        var loaded = AssertStoredUnderManifestName(store, path, expected);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hosting:Mode"] = "Public", ["Hosting:AllowedHosts"] = "a.example" })
            .Build();
        return ThinkTank.Blazor.Hosting.ThinkTankHosting.Read(config, loaded, _ => null);
    }

    [Test]
    public void Setting_HstsMaxAgeDays_RoundTripsAndIsTheHstsAge()
    {
        Assert.That(new ThinkTankSettings().HstsMaxAgeDays, Is.EqualTo(365));
        Assert.That(HostingAfter(s => s.HstsMaxAgeDays = 90, "hstsMaxAgeDays", 90).HstsMaxAgeDays, Is.EqualTo(90));
    }

    [Test]
    public void Setting_ForwardLimit_RoundTripsAndIsTheTrustedHopCount()
    {
        Assert.That(new ThinkTankSettings().ForwardLimit, Is.EqualTo(1));
        Assert.That(HostingAfter(s => s.ForwardLimit = 3, "forwardLimit", 3).ForwardLimit, Is.EqualTo(3));
    }

    // ---- Sign-in (TT-T46) ---------------------------------------------------------------

    private static ThinkTank.Blazor.Hosting.ThinkTankHosting LocalHostingAfter(Action<ThinkTankSettings> change, string path, JsonNode expected)
    {
        var store = TestLamp.Store();
        store.Update(change);
        var loaded = AssertStoredUnderManifestName(store, path, expected);
        return ThinkTank.Blazor.Hosting.ThinkTankHosting.Read(new ConfigurationBuilder().Build(), loaded, _ => null);
    }

    [Test]
    public void Setting_AllowedUsers_RoundTripsAndIsTheAllowList()
    {
        Assert.That(new ThinkTankSettings().AllowedUsers, Is.EqualTo(""));
        var hosting = LocalHostingAfter(s => s.AllowedUsers = "ryan; erin", "allowedUsers", "ryan; erin");
        Assert.That(hosting.AllowedUsers, Is.EqualTo(new[] { "ryan", "erin" }));
        Assert.That(hosting.FirstAccountName, Is.EqualTo("ryan"));
        Assert.That(hosting.IsUserAllowed("mallory"), Is.False);
    }

    [Test]
    public void Setting_RequireTwoFactor_RoundTripsAndIsTheGatesMfaRule()
    {
        Assert.That(new ThinkTankSettings().RequireTwoFactor, Is.True);
        Assert.That(LocalHostingAfter(s => s.RequireTwoFactor = false, "requireTwoFactor", false).RequireMfa, Is.False);
    }

    [Test]
    public void Setting_LoginPermitLimit_RoundTripsAndIsTheSignInRateLimit()
    {
        Assert.That(new ThinkTankSettings().LoginPermitLimit, Is.EqualTo(10));
        Assert.That(LocalHostingAfter(s => s.LoginPermitLimit = 5, "loginPermitLimit", 5).LoginPermitLimit, Is.EqualTo(5));
    }

    [Test]
    public void Setting_LoginWindowSeconds_RoundTripsAndIsTheSignInWindow()
    {
        Assert.That(new ThinkTankSettings().LoginWindowSeconds, Is.EqualTo(60));
        Assert.That(LocalHostingAfter(s => s.LoginWindowSeconds = 120, "loginWindowSeconds", 120).LoginWindowSeconds, Is.EqualTo(120));
    }
}

/// <summary>TT-T44: clipping and the friendly provider-error text.</summary>
[TestFixture]
public class InputLimitsAndErrorTextTests
{
    [Test]
    public void Clip_CutsToTheLimit_AndNeverSplitsASurrogatePair()
    {
        Assert.That(InputLimits.Clip(null, 5), Is.EqualTo(""));
        Assert.That(InputLimits.Clip("abc", 5), Is.EqualTo("abc"));
        Assert.That(InputLimits.Clip("abcdef", 5), Is.EqualTo("abcde"));
        Assert.That(InputLimits.Clip("abcd\U0001F600", 5), Is.EqualTo("abcd"));
    }

    [Test]
    public void A401_ReadsAsUnavailable_WithoutTheProvidersText()
    {
        var ex = new InvalidOperationException("outer", new HttpRequestException("secret-org-id says invalid x-api-key sk-abc", null, HttpStatusCode.Unauthorized));
        var text = ProviderErrorText.Describe(ex, "Claude", "ab12cd34");
        Assert.That(text, Does.Contain("Claude is unavailable"));
        Assert.That(text, Does.Contain("(ref ab12cd34)"));
        Assert.That(text, Does.Not.Contain("secret-org-id").And.Not.Contain("sk-abc"));
    }

    [TestCase(HttpStatusCode.TooManyRequests, "busy")]
    [TestCase(HttpStatusCode.PaymentRequired, "out of credit")]
    [TestCase(HttpStatusCode.BadGateway, "provider's side")]
    public void StatusCodes_MapToPlainSentences(HttpStatusCode status, string expected)
    {
        var text = ProviderErrorText.Describe(new HttpRequestException("raw", null, status));
        Assert.That(text, Does.Contain(expected));
        Assert.That(text, Does.Not.Contain("raw"));
    }

    [Test]
    public void AnUnknownError_SaysNothingAboutIt()
    {
        var text = ProviderErrorText.Describe(new Exception("Stack trace with C:\\secret\\path"));
        Assert.That(text, Does.Contain("unexpected error"));
        Assert.That(text, Does.Not.Contain("secret"));
    }

    [Test]
    public void ThinkTanksOwnLimitMessages_AreShownAsWritten()
    {
        var text = ProviderErrorText.Describe(new UserFacingException("You have used today's 300 calls."));
        Assert.That(text, Is.EqualTo("You have used today's 300 calls."));
    }
}
