using ThinkTank.Core.Models;
using ThinkTank.Core.Services;
using ThinkTank.Blazor.Components;
using MindAttic.Legion;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<HttpClient>();
// MindAttic.Legion is the gateway for all LLM communication. Register before
// any service that calls into LLMs (ThinkTankService, VotingService).
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
        var json = string.IsNullOrWhiteSpace(model)
            ? $"{{\n  \"type\": \"{type}\",\n  \"apiKey\": \"{apiKey}\",\n  \"maxTokens\": 2048\n}}"
            : $"{{\n  \"type\": \"{type}\",\n  \"apiKey\": \"{apiKey}\",\n  \"model\": \"{model}\",\n  \"maxTokens\": 2048\n}}";
        settings.ProviderDefaults[providerId] = new ProviderAuthConfig(providerId, json);
    }

    return settings;
});
builder.Services.AddSingleton<ThinkTankSettingsService>(sp => sp.GetRequiredService<SettingsService>());
builder.Services.AddSingleton<ChatLogService>();
builder.Services.AddSingleton<AppearanceService>();
builder.Services.AddSingleton<ChatConversationsService>();
builder.Services.AddSingleton<HumanNameService>();
builder.Services.AddSingleton<NameGeneratorService>();
builder.Services.AddSingleton<ThinkTankService>();
builder.Services.AddLLMVoting(sp =>
{
    var settings = sp.GetRequiredService<SettingsService>();
    var apiKeys = new Dictionary<string, string>();
    var modelOverrides = new Dictionary<string, string>();
    foreach (var (id, _) in settings.ProviderAuth)
    {
        var key = settings.GetKeyForProvider(id, null);
        if (!string.IsNullOrWhiteSpace(key))
            apiKeys[id] = key;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(settings.GetAuthJson(id));
            if (doc.RootElement.TryGetProperty("model", out var m) && m.GetString() is { Length: > 0 } model)
                modelOverrides[id] = model;
        }
        catch { }
    }
    return new VotingConfiguration
    {
        ApiKeys = apiKeys,
        ModelOverrides = modelOverrides,
        AllowedProviderIds = LlmProviderCatalog.DefaultIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
    };
});
builder.Services.AddSingleton<VotingService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ThinkTank.Shared.Components.Pages.Home).Assembly);

app.Run();
