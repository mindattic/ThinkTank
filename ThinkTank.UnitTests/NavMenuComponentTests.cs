using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ThinkTank.Core.Services;
using ThinkTank.Shared.Components.Layout;

namespace ThinkTank.UnitTests;

/// <summary>
/// NavMenu hosts the three top-level routes the Cypress navigation smoke
/// asserts on. bUnit auto-registers a fake NavigationManager; the only
/// service it needs is <see cref="ThinkTankHostMode"/> (local by default).
/// </summary>
[TestFixture]
public class NavMenuComponentTests
{
    private Bunit.TestContext ctx = null!;

    [SetUp]
    public void SetUp()
    {
        ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(ThinkTankHostMode.Local);
    }
    [TearDown] public void TearDown() => ctx.Dispose();

    [Test]
    public void RendersBrand_ThreePageLinks_TheSettingsCog_AndTheVaultLink()
    {
        var cut = ctx.RenderComponent<NavMenu>();
        Assert.That(cut.Find("a.nav-brand").TextContent, Is.EqualTo("Think Tank"));

        Assert.That(cut.FindAll("nav .nav-tabs a:not(.vault-link)").Count, Is.EqualTo(3));
        Assert.That(cut.Find("nav .nav-tabs button").TextContent, Does.Contain("Settings"));
        Assert.That(cut.Find("nav .nav-tabs a.vault-link").GetAttribute("href"), Is.EqualTo("mindattic-vault://open?app=ThinkTank"));
    }

    [Test]
    public void Hosted_HidesTheSettingsCog_AndTheVaultLink()
    {
        using var hosted = new Bunit.TestContext();
        hosted.Services.AddSingleton(new ThinkTankHostMode(isHosted: true));
        var cut = hosted.RenderComponent<NavMenu>();

        Assert.That(cut.FindAll("nav .nav-tabs button"), Is.Empty, "no button may start a process on a hosted server (TT-T42)");
        Assert.That(cut.FindAll("a.vault-link"), Is.Empty);
        Assert.That(cut.Markup, Does.Not.Contain("mindattic-vault://"));
        Assert.That(cut.FindAll("nav .nav-tabs a").Count, Is.EqualTo(3));
    }

    [Test]
    public void HostMode_IsHosted_OnAppService_OrWhenPublic()
    {
        var empty = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        Assert.That(ThinkTankHostMode.From(empty, _ => null).IsHosted, Is.False);
        Assert.That(ThinkTankHostMode.From(empty, n => n == "WEBSITE_SITE_NAME" ? "mindattic-thinktank" : null).IsHosted, Is.True);
        var publicMode = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hosting:Mode"] = "Public" }).Build();
        Assert.That(ThinkTankHostMode.From(publicMode, _ => null).IsHosted, Is.True);
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
        Assert.That(links, Has.Some.Matches<(string text, string? href)>(l => l.text == "Personas" && l.href == "personas"));
        Assert.That(links.Select(l => l.href), Has.None.EqualTo("settings"), "no in-app settings page (LAMP)");
    }
}
