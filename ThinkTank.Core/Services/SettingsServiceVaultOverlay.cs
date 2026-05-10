using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MindAttic.Vault.Configuration;
using ThinkTank.Core.Models;

namespace ThinkTank.Core.Services;

/// <summary>
/// Extension that layers <see cref="IConfiguration"/>-backed apiKey values on top of
/// the in-memory <see cref="ThinkTankSettingsService.ProviderAuth"/> map. Sources walk
/// in standard <c>IConfigurationBuilder</c> order — when called from <c>Program.cs</c>
/// the chain is:
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
/// <para>The overlay updates each provider's auth JSON in-memory only — it does NOT call
/// <see cref="ThinkTankSettingsService.SetAuthJson"/>, so the new apiKey values never
/// land on disk. Production deployments keep their secrets in Application Settings or
/// Key Vault; the local <c>Settings.json</c> stays free of cloud-resolved secrets.</para>
/// </summary>
public static class SettingsServiceVaultOverlay
{
    /// <summary>
    /// For every providerId already in <see cref="ThinkTankSettingsService.ProviderAuth"/>,
    /// if <c>MindAttic:Vault:LLM:&lt;providerId&gt;:apiKey</c> is set in <paramref name="config"/>,
    /// rewrite that provider's auth JSON to use the configured value while preserving every
    /// other field (<c>type</c>, <c>model</c>, <c>maxTokens</c>, ...).
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

            var existingJson = self.ProviderAuth[providerId].Json;
            var updatedJson  = ReplaceApiKey(existingJson, key.Trim());
            self.ProviderAuth[providerId] = new ProviderAuthConfig(providerId, updatedJson);
        }
    }

    /// <summary>
    /// Returns a copy of <paramref name="existingJson"/> with its <c>apiKey</c> property
    /// replaced by <paramref name="newApiKey"/>. Preserves every other field. Falls back
    /// to a minimal <c>{ "apiKey": ... }</c> object if <paramref name="existingJson"/>
    /// is unparseable.
    /// </summary>
    private static string ReplaceApiKey(string existingJson, string newApiKey)
    {
        var preserved = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(existingJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "apiKey", StringComparison.Ordinal)) continue;
                        preserved[prop.Name] = prop.Value.Clone();
                    }
                }
            }
            catch { /* fall through to minimal payload */ }
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            // Preserve type first if present so the JSON ordering matches the existing convention.
            if (preserved.TryGetValue("type", out var t)) { w.WritePropertyName("type"); t.WriteTo(w); }
            w.WriteString("apiKey", newApiKey);
            foreach (var (name, value) in preserved)
            {
                if (name is "type") continue;
                w.WritePropertyName(name);
                value.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
