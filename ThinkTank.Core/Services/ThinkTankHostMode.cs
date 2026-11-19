using Microsoft.Extensions.Configuration;

namespace ThinkTank.Core.Services;

/// <summary>
/// Whether ThinkTank runs on the owner's own machine or on a server that browsers reach over the internet
/// (TT-T42). Hosted when <c>Hosting:Mode</c> (env <c>Hosting__Mode</c>) is <c>Public</c>, or when the process
/// runs on Azure App Service (<c>WEBSITE_SITE_NAME</c> is set) whatever the setting says, so a deployed site
/// can never act as if it were the owner's desktop.
/// <para>
/// Hosted, nothing a browser does may start a process on the server: the Settings cog that launches
/// MindAttic.Vault.Desktop and the <c>mindattic-vault://</c> link (which only works on a machine with Vault
/// installed) are not shown.
/// </para>
/// </summary>
public sealed class ThinkTankHostMode
{
    public const string ModeKey = "Hosting:Mode";

    public ThinkTankHostMode(bool isHosted) => IsHosted = isHosted;

    /// <summary>True when browsers reach this process over a network (App Service or <c>Hosting:Mode=Public</c>).</summary>
    public bool IsHosted { get; }

    /// <summary>The owner's own machine: Vault.Desktop can be launched from the Settings cog.</summary>
    public static ThinkTankHostMode Local { get; } = new(false);

    /// <param name="env">Environment lookup; defaults to the process environment. A seam for tests.</param>
    public static ThinkTankHostMode From(IConfiguration? configuration, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var mode = (configuration?[ModeKey] ?? env("Hosting__Mode") ?? "").Trim();
        var onAppService = !string.IsNullOrWhiteSpace(env("WEBSITE_SITE_NAME"));
        return new ThinkTankHostMode(onAppService || mode.Equals("Public", StringComparison.OrdinalIgnoreCase));
    }
}
