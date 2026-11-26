using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MindAttic.Authentication;
using NUnit.Framework;
using ThinkTank.Blazor.Hosting;
using ThinkTank.Core.Data;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>TT-T46: the sign-in gate's rule, the allow-list, and where the database connection comes from.</summary>
[TestFixture]
public class SignInTests
{
    private static ThinkTankHosting Hosting(bool isPublic, string users = "ryan", bool requireMfa = true)
    {
        var values = new Dictionary<string, string?>();
        if (isPublic)
        {
            values["Hosting:Mode"] = "Public";
            values["Hosting:AllowedHosts"] = "a.example";
            values["Hosting:DataProtection:KeyRingPath"] = Path.GetTempPath();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return ThinkTankHosting.Read(config, new ThinkTankSettings { AllowedUsers = users, RequireTwoFactor = requireMfa }, _ => null);
    }

    private static ClaimsPrincipal User(string name, bool mfa) =>
        new(new ClaimsIdentity(
            mfa ? [new Claim(ClaimTypes.Name, name), new Claim(MaClaims.Amr, "mfa")] : [new Claim(ClaimTypes.Name, name)],
            "test"));

    [TestCase("GET", "/")]
    [TestCase("GET", "/thinktank")]
    [TestCase("GET", "/personas")]
    [TestCase("POST", "/_blazor/negotiate")]
    [TestCase("GET", "/_blazor")]
    [TestCase("GET", "/_framework/blazor.web.js")]
    [TestCase("GET", "/account")]
    [TestCase("POST", "/_ma-auth/change-password")]
    public void Anonymous_IsChallenged_Everywhere_ButTheSignInSurface(string method, string path)
    {
        Assert.That(SignInGate.Decide(method, new PathString(path), new ClaimsPrincipal(new ClaimsIdentity()), Hosting(isPublic: true)),
            Is.EqualTo(GateDecision.Challenge));
    }

    [TestCase("GET", "/login")]
    [TestCase("GET", "/mfa")]
    [TestCase("GET", "/healthz")]
    [TestCase("GET", "/healthz/db")]
    [TestCase("GET", "/Error")]
    [TestCase("POST", "/_ma-auth/login")]
    [TestCase("POST", "/_ma-auth/mfa-challenge")]
    public void TheSignInSurface_IsOpen(string method, string path)
    {
        Assert.That(SignInGate.Decide(method, new PathString(path), null, Hosting(isPublic: true)), Is.EqualTo(GateDecision.Allow));
    }

    [Test]
    public void AnAccountNotOnTheAllowList_IsRefused_EvenWithTheRightPassword()
    {
        Assert.That(SignInGate.Decide("GET", "/thinktank", User("mallory", mfa: true), Hosting(isPublic: true)), Is.EqualTo(GateDecision.Forbidden));
        Assert.That(SignInGate.Decide("GET", "/thinktank", User("RYAN", mfa: true), Hosting(isPublic: true)), Is.EqualTo(GateDecision.Allow));
    }

    [Test]
    public void WithoutTheTotpStep_OnlyTheAccountPagesAreReachable()
    {
        var hosting = Hosting(isPublic: true);
        Assert.That(SignInGate.Decide("GET", "/thinktank", User("ryan", mfa: false), hosting), Is.EqualTo(GateDecision.EnrollMfa));
        Assert.That(SignInGate.Decide("GET", "/account/mfa/setup", User("ryan", mfa: false), hosting), Is.EqualTo(GateDecision.Allow));
        Assert.That(SignInGate.Decide("GET", "/thinktank", User("ryan", mfa: false), Hosting(isPublic: true, requireMfa: false)), Is.EqualTo(GateDecision.Allow));
    }

    [Test]
    public void Local_WithNoAllowList_LetsAnyAccountIn_ButPublicNeverDoes()
    {
        Assert.That(Hosting(isPublic: false, users: "").IsUserAllowed("anyone"), Is.True);
        Assert.That(Hosting(isPublic: false, users: "").FirstAccountName, Is.EqualTo("admin"));
        Assert.That(() => Hosting(isPublic: true, users: ""), Throws.InstanceOf<ThinkTankHostingException>());
    }

    [Test]
    public void TheAllowListSetting_OverriddenByConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hosting:Auth:AllowedUsers"] = "erin",
        }).Build();
        var hosting = ThinkTankHosting.Read(config, new ThinkTankSettings { AllowedUsers = "ryan" }, _ => null);
        Assert.That(hosting.AllowedUsers, Is.EqualTo(new[] { "erin" }));
    }

    [TestCase("/thinktank", true)]
    [TestCase("//evil.example/x", false)]
    [TestCase("/\\evil.example", false)]
    [TestCase("https://evil.example", false)]
    [TestCase("", false)]
    public void ReturnUrls_AreSameSitePathsOnly(string url, bool safe)
    {
        Assert.That(SignInGate.IsSafeReturnUrl(url), Is.EqualTo(safe));
    }

    // ---- database connection --------------------------------------------------------------

    [Test]
    public void Hosted_WithoutAConfiguredDatabase_GetsNone()
    {
        var empty = new ConfigurationBuilder().Build();
        Assert.That(ThinkTankDatabase.Resolve(empty, hosted: true, appDataRoot: Path.GetTempPath(), env: _ => null), Is.Null);
    }

    [Test]
    public void TheEnvironmentVariable_WinsOverConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ThinkTank"] = "Server=config",
        }).Build();
        Assert.That(ThinkTankDatabase.Resolve(config, hosted: true, env: n => n == "ConnectionStrings__ThinkTank" ? "Server=env" : null),
            Is.EqualTo("Server=env"));
        Assert.That(ThinkTankDatabase.Resolve(config, hosted: true, env: _ => null), Is.EqualTo("Server=config"));
    }

    [Test]
    public void Local_UsesTheServicePrincipalFile_ThenTheDevelopersOwnSignIn()
    {
        var root = Path.Combine(Path.GetTempPath(), "tt-db-" + Guid.NewGuid().ToString("N"));
        try
        {
            var empty = new ConfigurationBuilder().Build();
            Assert.That(ThinkTankDatabase.Resolve(empty, hosted: false, appDataRoot: root, env: _ => null),
                Is.EqualTo(ThinkTankDatabase.DeveloperConnectionString));

            var dir = Directory.CreateDirectory(Path.Combine(root, "MindAttic", "Database"));
            File.WriteAllText(Path.Combine(dir.FullName, "providers.json"),
                new JsonObject { ["AzureSql"] = new JsonObject { ["ClientId"] = "client-id", ["ClientSecret"] = "not-a-real-secret" } }.ToJsonString());
            var sp = ThinkTankDatabase.Resolve(empty, hosted: false, appDataRoot: root, env: _ => null)!;
            // SqlClient writes the method as "Active Directory Service Principal" or "ActiveDirectoryServicePrincipal"
            // depending on its version, so read it back through the builder rather than matching text.
            var parsed = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sp);
            Assert.That(parsed.Authentication, Is.EqualTo(Microsoft.Data.SqlClient.SqlAuthenticationMethod.ActiveDirectoryServicePrincipal));
            Assert.That(parsed.UserID, Is.EqualTo("client-id"));
            Assert.That(parsed.InitialCatalog, Is.EqualTo("ThinkTank"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
