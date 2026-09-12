using Microsoft.Extensions.Configuration;
using MindAttic.Vault.Configuration;

namespace ThinkTank.Core.Services;

/// <summary>
/// Extension that records <see cref="IConfiguration"/>-backed apiKey values into
/// <see cref="ThinkTankSettingsService.RuntimeApiKeyOverrides"/> — a process-lifetime side map
/// consulted by <see cref="ThinkTankSettingsService.GetKeyForProvider"/>. Sources walk in
/// standard <c>IConfigurationBuilder</c> order — when called from <c>Program.cs</c> the chain is:
/// <list type="number">
///   <item><description>App Service Application Settings (incl. Key Vault references) via <c>AddEnvironmentVariables</c></description></item>
///   <item><description>Shared dev secrets at id <c>mindattic-vault-shared</c></description></item>
///   <item><description>This project's own dev secrets (existing GUID) — preserves the current
///         <c>ProviderDefaults:&lt;providerId&gt;:apiKey</c> values, which are read by the
///         existing factory in <c>Program.cs</c> before this overlay runs</description></item>
///   <item><description><c>%APPDATA%\MindAttic\LLM\providers.json</c> via <c>AddMindAtticVaultFiles</c></description></item>
///   <item><description><c>appsettings.json</c></description></item>
/// </list>
///
/// <para>The overlay deliberately does NOT mutate <see cref="ThinkTankSettingsService.ProviderAuth"/>
/// — that map is the on-disk projection serialized by every <see cref="ThinkTankSettingsService.Save"/>
/// (and pushed to the shared store by <c>SyncLocalToSharedStore</c>). Routing cloud-resolved keys
/// through the side map instead keeps secrets out of <c>Settings.json</c> and
/// <c>%APPDATA%\MindAttic\LLM\providers.json</c>, even after later UI edits trigger a Save.</para>
/// </summary>
public static class SettingsServiceVaultOverlay
{
    /// <summary>
    /// For every providerId in <see cref="ThinkTankSettingsService.ProviderAuth"/>, if
    /// <c>MindAttic:Vault:LLM:&lt;providerId&gt;:apiKey</c> is set in <paramref name="config"/>,
    /// record the trimmed value into <see cref="ThinkTankSettingsService.RuntimeApiKeyOverrides"/>.
    /// The on-disk JSON is left untouched.
    /// </summary>
    public static void OverlayFromConfiguration(this ThinkTankSettingsService self, IConfiguration config)
    {
        if (self is null) throw new ArgumentNullException(nameof(self));
        if (config is null) return;

        var bucket = config.GetSection(VaultConfigurationKeys.LlmSection);
        if (!bucket.Exists()) return;

        foreach (var providerId in self.ProviderAuth.Keys.ToList())
        {
            var key = bucket[$"{providerId}:{VaultConfigurationKeys.ApiKeyProperty}"];
            if (string.IsNullOrWhiteSpace(key)) continue;

            self.RuntimeApiKeyOverrides[providerId] = key.Trim();
        }
    }
}
