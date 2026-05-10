using Bunit;
using NUnit.Framework;
using ThinkTank.Shared.Components.Pages;

namespace ThinkTank.UnitTests;

[TestFixture]
public class NotFoundPageComponentTests
{
    private Bunit.TestContext ctx = null!;

    [SetUp] public void SetUp() => ctx = new Bunit.TestContext();
    [TearDown] public void TearDown() => ctx.Dispose();

    [Test]
    public void Renders_HeadingAndAlertMessage()
    {
        var cut = ctx.RenderComponent<NotFound>();
        Assert.That(cut.Find("h1").TextContent, Is.EqualTo("Not Found"));
        // The Cypress nav smoke greps for /not found|404/i — keep this in sync
        // so a rename here forces the e2e expectation to update.
        var alert = cut.Find("p[role='alert']");
        Assert.That(alert.TextContent, Does.Match("(?i)not exist"));
    }
}
