using ThinkTank.Core.Services;

namespace ThinkTank.Blazor.Hosting;

/// <summary>Hosted mode was asked for and its configuration is missing or wrong. ThinkTank must not start.</summary>
public sealed class ThinkTankHostingException(string message) : Exception(message);

/// <summary>
/// How ThinkTank is exposed (TT-T45, after Prose.Hub's <c>HubHosting</c>). Two modes, chosen by configuration:
///
/// <para><b>Local</b> (the default): run on the owner's machine, as before. No host allow-list; outside
/// Development, HTTPS redirect and HSTS as ASP.NET's defaults.</para>
///
/// <para><b>Public</b> (<c>Hosting:Mode=Public</c>, env <c>Hosting__Mode</c>, or any App Service site): reachable
/// from the internet. Reads the platform proxy's forwarded headers, answers only the configured host names,
/// HTTPS only with HSTS, and refuses to start when any of that is not configured (fail closed).</para>
///
/// <para>Configuration keys (a <c>:</c> becomes <c>__</c> in an environment variable or App Service setting):</para>
/// <list type="table">
/// <item><term>Hosting:Mode</term><description><c>Local</c> (default) or <c>Public</c>. Anything else stops ThinkTank.</description></item>
/// <item><term>Hosting:AllowedHosts</term><description>Public: required. Host names served, separated by <c>;</c> or <c>,</c>
/// (for example <c>mindattic-thinktank.azurewebsites.net</c>). No wildcard, scheme or port.</description></item>
/// <item><term>Hosting:RequireHttps</term><description>Default <c>true</c>.</description></item>
/// <item><term>Hosting:TrustForwardedHeaders</term><description>Default <c>true</c>: read the proxy's <c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c>.</description></item>
/// </list>
/// <para>The numbers (HSTS max age, trusted proxy hops) are ThinkTank Vault settings, group Hosting.</para>
/// </summary>
public sealed class ThinkTankHosting
{
    public const string ModeKey = ThinkTankHostMode.ModeKey;
    public const string AllowedHostsKey = "Hosting:AllowedHosts";
    public const string RequireHttpsKey = "Hosting:RequireHttps";
    public const string TrustForwardedHeadersKey = "Hosting:TrustForwardedHeaders";

    public bool IsPublic { get; private init; }
    public IReadOnlyList<string> AllowedHosts { get; private init; } = [];
    public bool RequireHttps { get; private init; } = true;
    public bool TrustForwardedHeaders { get; private init; } = true;
    public int HstsMaxAgeDays { get; private init; } = 365;
    public int ForwardLimit { get; private init; } = 1;

    /// <summary>
    /// Reads the mode. Local validates nothing it does not use. Public validates every key and throws
    /// <see cref="ThinkTankHostingException"/> listing all problems at once.
    /// </summary>
    public static ThinkTankHosting Read(IConfiguration config, ThinkTankSettings settings, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var problems = new List<string>();
        var mode = (config[ModeKey] ?? "").Trim();
        var onAppService = !string.IsNullOrWhiteSpace(env("WEBSITE_SITE_NAME"));

        bool isPublic;
        if (mode.Length == 0) isPublic = onAppService;
        else if (mode.Equals("Public", StringComparison.OrdinalIgnoreCase)) isPublic = true;
        else if (mode.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            // A deployed site is never the owner's desktop, whatever the setting says.
            if (onAppService) problems.Add($"{ModeKey} is 'Local' but this is an App Service site. Hosted ThinkTank must run with {ModeKey}=Public.");
            isPublic = false;
        }
        else
        {
            problems.Add($"{ModeKey} is '{mode}'. It must be 'Local' (the default) or 'Public'.");
            isPublic = false;
        }

        var hosts = SplitList(config, AllowedHostsKey);
        if (hosts.Any(h => h.Contains('*') || h.Contains('/') || h.Contains(':')))
            problems.Add($"{AllowedHostsKey} takes bare host names (no scheme, port, path or wildcard): '{string.Join(";", hosts)}'.");
        if (isPublic && hosts.Count == 0)
            problems.Add($"{AllowedHostsKey} is empty. Set the host name(s) ThinkTank is served on, e.g. mindattic-thinktank.azurewebsites.net.");

        var requireHttps = ReadBool(config, RequireHttpsKey, true, problems);
        var trustForwarded = ReadBool(config, TrustForwardedHeadersKey, true, problems);

        if (problems.Count > 0)
            throw new ThinkTankHostingException(
                "[thinktank] Hosting is not configured correctly; not starting." + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));

        return new ThinkTankHosting
        {
            IsPublic = isPublic,
            AllowedHosts = hosts,
            RequireHttps = requireHttps,
            TrustForwardedHeaders = trustForwarded,
            HstsMaxAgeDays = Math.Clamp(settings.HstsMaxAgeDays, 0, 3650),
            ForwardLimit = Math.Clamp(settings.ForwardLimit, 1, 10),
        };
    }

    /// <summary>A list given as one "a;b,c" string or as an indexed section (Key:0, Key:1).</summary>
    internal static List<string> SplitList(IConfiguration config, string key)
    {
        var raw = new List<string>();
        if (config[key] is { } single) raw.Add(single);
        raw.AddRange(config.GetSection(key).GetChildren().Select(c => c.Value ?? ""));
        return raw.SelectMany(v => v.Split(';', ','))
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool ReadBool(IConfiguration config, string key, bool fallback, List<string> problems)
    {
        var raw = (config[key] ?? "").Trim();
        if (raw.Length == 0) return fallback;
        if (bool.TryParse(raw, out var value)) return value;
        problems.Add($"{key} must be true or false: '{raw}'.");
        return fallback;
    }
}
