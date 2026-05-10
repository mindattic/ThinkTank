using Bunit;
using NUnit.Framework;
using ThinkTank.Shared.Components.Pages;

namespace ThinkTank.UnitTests;

/// <summary>
/// Home is two anchors — but the hrefs are load-bearing for the Cypress
/// navigation smoke (which clicks "Open Think Tank" → /thinktank). Pin them.
/// </summary>
[TestFixture]
public class HomePageComponentTests
{
    private Bunit.TestContext ctx = null!;

    [SetUp] public void SetUp() => ctx = new Bunit.TestContext();
    [TearDown] public void TearDown() => ctx.Dispose();

    [Test]
    public void RendersOpenThinkTankLink_PointingAtThinktank()
    {
        var cut = ctx.RenderComponent<Home>();
        var openLink = cut.Find("a.start-btn");
        Assert.That(openLink.TextContent, Does.Contain("Open Think Tank"));
        Assert.That(openLink.GetAttribute("href"), Is.EqualTo("thinktank"));
    }

    [Test]
    public void RendersSettingsLink_PointingAtSettings()
    {
        var cut = ctx.RenderComponent<Home>();
        var settingsLink = cut.Find("a.reset-btn");
        Assert.That(settingsLink.TextContent, Does.Contain("Settings"));
        Assert.That(settingsLink.GetAttribute("href"), Is.EqualTo("settings"));
    }
}
