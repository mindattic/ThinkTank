using ThinkTank.Core.Models;
using ThinkTank.Core.Services;
using ThinkTank.Blazor.Components;
using ThinkTank.Blazor.Hosting;
using MindAttic.Legion;
using MindAttic.Authentication.Web;
using MindAttic.Vault.Configuration;
using Microsoft.AspNetCore.DataProtection;
using ThinkTank.Core.Data;

var builder = WebApplication.CreateBuilder(args);

// LAMP (TT-T27): `--write-settings-manifest` seeds ThinkTank's Vault settings (importing today's values
// from Settings.json once) and writes the manifest MindAttic.Vault.Desktop renders, then exits.
if (args.Any(a => string.Equals(a, MindAttic.Vault.Settings.Manifest.SettingsManifestCommand.Switch, StringComparison.OrdinalIgnoreCase)))
{
    ThinkTankSettingsSource.Open(builder.Configuration);
    if (MindAttic.Vault.Settings.Manifest.SettingsManifestCommand.TryRun<ThinkTankSettings>(args)) return;
}

// Cloud-native configuration chain. Layered so existing dev workflows keep working:
//   - AddJsonFile (already added by WebApplicationBuilder for appsettings.json).
//   - AddMindAtticVaultFiles surfaces %APPDATA%\MindAttic\LLM\providers.json on dev machines
//     — the single credential source (.NET User Secrets is not used).
//   - AddEnvironmentVariables (already present) picks up App Service Application Settings
//     and Key Vault references in production.
// TT-T46: "Security" is the MindAttic.Authentication trust domain (pepper.v1, bootstrap-token, reset-token-key);
// it is not in Vault's default bucket list, so it is named here. "Notifications" carries the auth email settings.
builder.Configuration
    .AddMindAtticVaultFiles(o => o.Buckets = new[] { "LLM", "Security", "Notifications" });
// Environment variables (App Service settings) win over anything the Vault files above supplied.
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MindAttic.Legion is the gateway for all LLM communication. Register before
// any service that calls into LLMs (ThinkTankService, VotingService). Legion
// owns its own IHttpClientFactory registration internally, so no app-level
// HttpClient singleton is needed.
builder.Services.AddLegionClient();
// LAMP (TT-T27): app-level settings live in MindAttic Vault (edited only in MindAttic.Vault.Desktop),
// imported once from Settings.json; a deployed host reads MindAttic:Settings:ThinkTank from configuration.
// Keys come from Vault's shared named lists (configuration first, then %APPDATA%\MindAttic\LLM), and a
// per-app "thinktank-*" key copy left from before moves into the shared list as ThinkTank's active key.
var thinkTankSettings = ThinkTankSettingsSource.Open(builder.Configuration);
MindAttic.Vault.Credentials.KeyImporter.ImportAppScopedFor(ThinkTankSettings.AppId);
builder.Services.AddSingleton(thinkTankSettings);
// TT-T42: hosted (Hosting:Mode=Public, or on App Service) hides the Settings cog that starts Vault.Desktop on the server.
builder.Services.AddSingleton(ThinkTankHostMode.From(builder.Configuration));
builder.Services.AddSingleton(_ => new SettingsService(
    thinkTankSettings, builder.Configuration.CreateKeySelector(ThinkTankKeys.ClassifyLegionFailure)));
builder.Services.AddSingleton<ThinkTankSettingsService>(sp => sp.GetRequiredService<SettingsService>());
builder.Services.AddSingleton<ChatLogService>();
builder.Services.AddSingleton<AppearanceService>();
builder.Services.AddSingleton<ChatConversationsService>();
builder.Services.AddSingleton<HumanNameService>();
builder.Services.AddSingleton<NameGeneratorService>();
// Bridges to Legion's psychometric store (one JSON file per persona under
// %APPDATA%/MindAttic/Legion/personas/). Resolves + caches each persona's trait profile and
// renders it into the behavioral brief that ThinkTankService and VotingService inject into
// system prompts. Default ctor uses Legion's standard store resolution.
builder.Services.AddSingleton<PsychometricProfileService>(_ => new PsychometricProfileService());
builder.Services.AddSingleton<ThinkTankService>();
// VotingConfiguration is a long-lived singleton; VotingService.RefreshVotingConfigFromSettings
// repopulates ApiKeys/ModelOverrides from SettingsService before every vote, so keys added or
// rotated in MindAttic Vault after startup are visible at call time. The DI factory only
// supplies the static AllowedProviderIds whitelist.
builder.Services.AddLLMVoting(sp => new VotingConfiguration
{
    AllowedProviderIds = LlmProviderCatalog.DefaultIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
});
builder.Services.AddSingleton<VotingService>();

// TT-T45: Local (the owner's machine, the default) or Public (served to the internet; fail closed when its
// configuration is incomplete). See Hosting/ThinkTankHosting.cs for every key.
ThinkTankHosting hosting;
try { hosting = ThinkTankHosting.Read(builder.Configuration, thinkTankSettings.Current); }
catch (ThinkTankHostingException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.Exit(78); // EX_CONFIG: the platform should see a failed start at once
    return;
}
builder.Services.AddSingleton(hosting);

// TT-T46: sign-in. MindAttic.Authentication over ThinkTank's Azure SQL database (schema auth). Required in both
// modes: nothing that spends an API key is reachable without a signed-in, allowed account. A hosted ThinkTank
// must be told its database (ConnectionStrings__ThinkTank); on this PC the MindAttic SQL service principal is used.
var connectionString = ThinkTankDatabase.Resolve(builder.Configuration, hosted: hosting.IsPublic);
if (connectionString is null)
{
    Console.Error.WriteLine("[thinktank] Hosted mode: no database is configured (ConnectionStrings:ThinkTank, env ConnectionStrings__ThinkTank). Not starting.");
    Environment.Exit(78);
    return;
}
builder.Services.AddDbContext<ThinkTankAuthDbContext>(o => ThinkTankAuthDbContext.Configure(o, connectionString));
try
{
    hosting.EnsureKeyRingWritable();
    builder.Services.AddMindAtticAuthentication<ThinkTankAuthDbContext>(builder.Configuration, o =>
    {
        o.AppName = "ThinkTank";   // per-app Data Protection trust boundary
        o.IsProduction = hosting.IsPublic;
        // Hosted: the ring must outlive the process (login cookie, antiforgery, circuit state, stored TOTP
        // secrets). Local: the library keeps it under %APPDATA%\MindAttic\DataProtection\ThinkTank.
        if (hosting.IsPublic)
            o.ConfigureDataProtection = dp => dp.PersistKeysToFileSystem(new DirectoryInfo(hosting.KeyRingPath));
    });
    builder.Services.AddSignInGate(hosting);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex is ThinkTankHostingException ? ex.Message
        : $"[thinktank] MindAttic.Authentication could not be registered, so there would be no login. Not starting. ({ex.Message})");
    Environment.Exit(78);
    return;
}

var app = builder.Build();

// Prove a sign-in is possible before listening: pepper present, auth schema current (migrated here),
// first account seeded from the bootstrap token when there is none.
try { await SignInStartup.EnsureReadyAsync(app.Services, hosting, Console.WriteLine); }
catch (ThinkTankHostingException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.Exit(78);
    return;
}

// Forwarded headers, host allow-list, HTTPS + HSTS, security headers and the CSP, before everything else.
app.UseThinkTankTransport(hosting);
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");
// Rate limiter, authentication, the sign-in gate, then authorization and the forced first-sign-in steps
// (TOTP enrolment, password change). Before static files: nothing is served to an anonymous visitor but the
// sign-in pages and the health checks.
app.UseSignInGate(hosting);
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ThinkTank.Shared.Components.Pages.Home).Assembly);

// /login, /mfa, /account*, /_ma-auth/*, /healthz and /healthz/db.
app.MapSignInPages(hosting);

app.Run();
