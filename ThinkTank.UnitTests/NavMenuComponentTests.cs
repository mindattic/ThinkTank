using Bunit;
using NUnit.Framework;
using ThinkTank.Shared.Components.Layout;

namespace ThinkTank.UnitTests;

/// <summary>
/// NavMenu hosts the three top-level routes the Cypress navigation smoke
/// asserts on. bUnit auto-registers a fake NavigationManager, so this
/// renders without any service wiring.
/// </summary>
[TestFixture]
public class NavMenuComponentTests
{
    private Bunit.TestContext ctx = null!;

    [SetUp] public void SetUp() => ctx = new Bunit.TestContext();
    [TearDown] public void TearDown() => ctx.Dispose();

    [Test]
    public void RendersBrand_AndThreeNavLinks()
    {
        var cut = ctx.RenderComponent<NavMenu>();
        Assert.That(cut.Find("a.nav-brand").TextContent, Is.EqualTo("Think Tank"));

        var tabs = cut.FindAll("nav .nav-tabs a");
        Assert.That(tabs.Count, Is.EqualTo(3));
    }

    [Test]
    public void NavLinks_PointAtCorrectRoutes()
    {
        var cut = ctx.RenderComponent<NavMenu>();
        var links = cut.FindAll("nav .nav-tabs a")
            .Select(a => (text: a.TextContent.Trim(), href: a.GetAttribute("href")))
            .ToList();

        Assert.That(links, Has.Some.Matches<(string text, string? href)>(l => l.text == "Home" && l.href == ""));
        Assert.That(links, Has.Some.Matches<(string text, string? href)>(l => l.text == "Conversations" && l.href == "thinktank"));
        Assert.That(links, Has.Some.Matches<(string text, string? href)>(l => l.text == "Settings" && l.href == "settings"));
    }
}
