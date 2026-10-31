using ThinkTank.Core.Models;
using ThinkTank.Core.Services;
using ThinkTank.Blazor.Components;
using MindAttic.Legion;
using MindAttic.Vault.Configuration;

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
builder.Configuration
    .AddMindAtticVaultFiles();

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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ThinkTank.Shared.Components.Pages.Home).Assembly);

app.Run();
