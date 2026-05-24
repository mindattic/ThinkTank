using ThinkTank.Core.Models;
using ThinkTank.Core.Services;
using ThinkTank.Blazor.Components;
using MindAttic.Legion;
using MindAttic.Vault.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Cloud-native configuration chain. Layered so existing dev workflows keep working:
//   - AddJsonFile (already added by WebApplicationBuilder for appsettings.json).
//   - AddMindAtticVaultFiles surfaces %APPDATA%\MindAttic\LLM\providers.json on dev machines.
//   - AddUserSecrets<Program>() reads the existing project-specific store (kept so
//     the migrated ProviderDefaults:* keys keep flowing into the existing factory below).
//   - AddUserSecrets("mindattic-vault-shared") layers the shared family store so a
//     single `dotnet user-secrets --id mindattic-vault-shared set ...` populates every
//     MindAttic project at once.
//   - AddEnvironmentVariables (already present) picks up App Service Application Settings
//     and Key Vault references in production.
builder.Configuration
    .AddMindAtticVaultFiles()
    .AddUserSecrets("mindattic-vault-shared");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MindAttic.Legion is the gateway for all LLM communication. Register before
// any service that calls into LLMs (ThinkTankService, VotingService). Legion
// owns its own IHttpClientFactory registration internally, so no app-level
// HttpClient singleton is needed.
builder.Services.AddLegionClient();
builder.Services.AddSingleton(sp =>
{
    var settings = new SettingsService();

    var config = sp.GetRequiredService<IConfiguration>();
    var section = config.GetSection("ProviderDefaults");
    foreach (var provider in section.GetChildren())
    {
        var providerId = provider.Key;
        var apiKey = provider["apiKey"] ?? "";
        var model = provider["model"] ?? "";
        var type = providerId is "claude" ? "anthropic" : providerId is "gemini" ? "google" : "bearer";
        var json = ThinkTankSettingsService.BuildAuthJson(
            type,
            apiKey,
            string.IsNullOrWhiteSpace(model) ? null : model,
            maxTokens: 2048);
        settings.ProviderDefaults[providerId] = new ProviderAuthConfig(providerId, json);
    }

    // Layer Vault on top: any provider with MindAttic:Vault:LLM:<id>:apiKey set in
    // IConfiguration (User Secrets / App Service / Key Vault) wins over the value
    // loaded from disk, in-memory only — secrets are never written back to Settings.json.
    settings.OverlayFromConfiguration(config);

    return settings;
});
builder.Services.AddSingleton<ThinkTankSettingsService>(sp => sp.GetRequiredService<SettingsService>());
builder.Services.AddSingleton<ChatLogService>();
builder.Services.AddSingleton<AppearanceService>();
builder.Services.AddSingleton<ChatConversationsService>();
builder.Services.AddSingleton<HumanNameService>();
builder.Services.AddSingleton<NameGeneratorService>();
builder.Services.AddSingleton<ThinkTankService>();
// VotingConfiguration is a long-lived singleton; VotingService.RefreshVotingConfigFromSettings
// repopulates ApiKeys/ModelOverrides from SettingsService before every vote, so keys added or
// rotated via the Settings UI after startup are visible at call time. The DI factory only
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
