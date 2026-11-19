using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ThinkTank.Blazor.Hosting;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>TT-T45: hosted mode's configuration fails closed, and the CSP fits Blazor Server.</summary>
[TestFixture]
public class HostingTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static readonly Func<string, string?> NoEnv = _ => null;

    [Test]
    public void Default_IsLocal()
    {
        var hosting = ThinkTankHosting.Read(Config(), new ThinkTankSettings(), NoEnv);
        Assert.That(hosting.IsPublic, Is.False);
    }

    [Test]
    public void Public_WithoutAllowedHosts_RefusesToStart()
    {
        Assert.That(() => ThinkTankHosting.Read(Config(("Hosting:Mode", "Public")), new ThinkTankSettings(), NoEnv),
            Throws.InstanceOf<ThinkTankHostingException>().With.Message.Contains("Hosting:AllowedHosts"));
    }

    [Test]
    public void AppService_IsPublic_EvenWithoutTheSetting_AndRefusesLocal()
    {
        Func<string, string?> appService = n => n == "WEBSITE_SITE_NAME" ? "mindattic-thinktank" : null;
        Assert.That(() => ThinkTankHosting.Read(Config(), new ThinkTankSettings(), appService),
            Throws.InstanceOf<ThinkTankHostingException>(), "public needs its allowed hosts");
        Assert.That(() => ThinkTankHosting.Read(Config(("Hosting:Mode", "Local"), ("Hosting:AllowedHosts", "x.example")), new ThinkTankSettings(), appService),
            Throws.InstanceOf<ThinkTankHostingException>().With.Message.Contains("App Service"));
    }

    [Test]
    public void UnknownMode_RefusesToStart()
    {
        Assert.That(() => ThinkTankHosting.Read(Config(("Hosting:Mode", "Open")), new ThinkTankSettings(), NoEnv),
            Throws.InstanceOf<ThinkTankHostingException>());
    }

    [Test]
    public void Public_ReadsHostsAndTheVaultNumbers()
    {
        var settings = new ThinkTankSettings { HstsMaxAgeDays = 30, ForwardLimit = 2 };
        var hosting = ThinkTankHosting.Read(
            Config(("Hosting:Mode", "Public"), ("Hosting:AllowedHosts", "a.example; b.example")), settings, NoEnv);
        Assert.That(hosting.IsPublic, Is.True);
        Assert.That(hosting.AllowedHosts, Is.EqualTo(new[] { "a.example", "b.example" }));
        Assert.That(hosting.HstsMaxAgeDays, Is.EqualTo(30));
        Assert.That(hosting.ForwardLimit, Is.EqualTo(2));
    }

    [Test]
    public void AllowedHosts_RejectWildcardsAndSchemes()
    {
        Assert.That(() => ThinkTankHosting.Read(Config(("Hosting:Mode", "Public"), ("Hosting:AllowedHosts", "*.example")), new ThinkTankSettings(), NoEnv),
            Throws.InstanceOf<ThinkTankHostingException>());
        Assert.That(() => ThinkTankHosting.Read(Config(("Hosting:Mode", "Public"), ("Hosting:AllowedHosts", "https://a.example")), new ThinkTankSettings(), NoEnv),
            Throws.InstanceOf<ThinkTankHostingException>());
    }

    [Test]
    public void HostCheck_IsExactAndCaseInsensitive()
    {
        Assert.That(HostedTransport.IsHostAllowed("A.Example", ["a.example"]), Is.True);
        Assert.That(HostedTransport.IsHostAllowed("evil.example", ["a.example"]), Is.False);
        Assert.That(HostedTransport.IsHostAllowed(null, ["a.example"]), Is.False);
    }

    [Test]
    public void Csp_AllowsOnlyThisHostsSocket_AndNoInlineOrEvalScript()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("a.example");
        var csp = HostedTransport.ContentSecurityPolicy(http.Request);
        Assert.That(csp, Does.Contain("script-src 'self';"));
        Assert.That(csp, Does.Not.Contain("unsafe-eval"));
        Assert.That(csp, Does.Contain("connect-src 'self' wss://a.example;"));
        Assert.That(csp, Does.Contain("frame-ancestors 'none'"));
    }
}
