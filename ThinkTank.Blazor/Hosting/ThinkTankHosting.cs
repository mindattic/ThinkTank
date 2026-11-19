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
/// <item><term>Hosting:Auth:AllowedUsers</term><description>Optional; overrides the Vault setting "Allowed accounts". Public: the
/// resulting list must not be empty. Local: empty lets any active account in.</description></item>
/// <item><term>Hosting:DataProtection:KeyRingPath</term><description>Public: folder for the Data Protection key ring (login
/// cookie, antiforgery, stored TOTP secrets); must survive restarts. Default on App Service:
/// <c>%HOME%\data\ThinkTank\DataProtection-Keys</c>. Required elsewhere. Not used when the blob ring below is set.</description></item>
/// <item><term>DataProtection:BlobUri</term> + <term>DataProtection:KeyVaultKeyId</term><description>Public, optional (TT-T48, the
/// Tutor way): keep the key ring in an Azure blob, wrapped by a Key Vault key, reached with the site's managed identity. Set
/// both or neither; when set they win over the key-ring folder.</description></item>
/// </list>
/// <para>The numbers (HSTS max age, trusted proxy hops, sign-in rate limit) and "Require two-factor" are ThinkTank
/// Vault settings (groups Hosting and Sign-in). A sign-in is required in both modes (TT-T46).</para>
/// </summary>
public sealed class ThinkTankHosting
{
    public const string ModeKey = ThinkTankHostMode.ModeKey;
    public const string AllowedHostsKey = "Hosting:AllowedHosts";
    public const string RequireHttpsKey = "Hosting:RequireHttps";
    public const string TrustForwardedHeadersKey = "Hosting:TrustForwardedHeaders";
    public const string AllowedUsersKey = "Hosting:Auth:AllowedUsers";
    public const string KeyRingPathKey = "Hosting:DataProtection:KeyRingPath";
    public const string BlobUriKey = "DataProtection:BlobUri";
    public const string KeyVaultKeyIdKey = "DataProtection:KeyVaultKeyId";

    public bool IsPublic { get; private init; }
    public IReadOnlyList<string> AllowedHosts { get; private init; } = [];
    public bool RequireHttps { get; private init; } = true;
    public bool TrustForwardedHeaders { get; private init; } = true;
    public int HstsMaxAgeDays { get; private init; } = 365;
    public int ForwardLimit { get; private init; } = 1;
    /// <summary>Accounts allowed in. Empty (Local only) lets any active account in.</summary>
    public IReadOnlyList<string> AllowedUsers { get; private init; } = [];
    public bool RequireMfa { get; private init; } = true;
    public int LoginPermitLimit { get; private init; } = 10;
    public int LoginWindowSeconds { get; private init; } = 60;
    /// <summary>Public: the Data Protection key-ring folder. Local: empty (the auth library's per-user default).</summary>
    public string KeyRingPath { get; private init; } = "";
    /// <summary>Public, optional: the blob that holds the key ring (with <see cref="KeyRingKeyVaultKeyId"/>). Null: the folder.</summary>
    public Uri? KeyRingBlobUri { get; private init; }
    /// <summary>The Key Vault key that wraps the key ring kept in <see cref="KeyRingBlobUri"/>.</summary>
    public Uri? KeyRingKeyVaultKeyId { get; private init; }
    /// <summary>True when the key ring lives in Azure blob storage under a Key Vault key, not in a folder.</summary>
    public bool KeyRingInAzure => KeyRingBlobUri is not null && KeyRingKeyVaultKeyId is not null;

    /// <summary>The account created from the bootstrap token when the auth tables hold no user.</summary>
    public string FirstAccountName => AllowedUsers.Count > 0 ? AllowedUsers[0] : "admin";

    public bool IsUserAllowed(string? userName) =>
        !string.IsNullOrWhiteSpace(userName)
        && (AllowedUsers.Count == 0
            ? !IsPublic
            : AllowedUsers.Any(u => string.Equals(u, userName.Trim(), StringComparison.OrdinalIgnoreCase)));

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

        var users = SplitList(config, AllowedUsersKey);
        if (users.Count == 0)
            users = (settings.AllowedUsers ?? "").Split(';', ',').Select(u => u.Trim()).Where(u => u.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (isPublic && users.Count == 0)
            problems.Add($"No account is allowed to sign in. Set {AllowedUsersKey} (or ThinkTank's Vault setting \"Allowed accounts\").");

        var blobUri = ReadHttpsUri(config, BlobUriKey, problems);
        var kvKeyId = ReadHttpsUri(config, KeyVaultKeyIdKey, problems);
        if ((blobUri is null) != (kvKeyId is null) && !problems.Any(p => p.Contains("DataProtection:")))
            problems.Add($"Set both {BlobUriKey} and {KeyVaultKeyIdKey} (the key-ring blob and the Key Vault key that wraps it), or neither.");
        var ringInAzure = isPublic && blobUri is not null && kvKeyId is not null;

        var keyRing = (config[KeyRingPathKey] ?? "").Trim();
        if (isPublic && !ringInAzure && keyRing.Length == 0)
        {
            // App Service: HOME is the persistent, instance-shared content share (C:\home, /home).
            var home = env("HOME");
            if (onAppService && !string.IsNullOrWhiteSpace(home))
                keyRing = Path.Combine(home, "data", "ThinkTank", "DataProtection-Keys");
            else
                problems.Add($"{KeyRingPathKey} is not set. Give a folder that survives restarts: it holds the keys behind the " +
                             "login cookie and the stored TOTP secrets.");
        }

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
            AllowedUsers = users,
            RequireMfa = settings.RequireTwoFactor,
            LoginPermitLimit = Math.Clamp(settings.LoginPermitLimit, 1, 10_000),
            LoginWindowSeconds = Math.Clamp(settings.LoginWindowSeconds, 1, 86_400),
            KeyRingPath = ringInAzure ? "" : keyRing,
            KeyRingBlobUri = ringInAzure ? blobUri : null,
            KeyRingKeyVaultKeyId = ringInAzure ? kvKeyId : null,
        };
    }

    /// <summary>
    /// Public: creates the key-ring folder and proves it is writable. A ring that cannot be written falls back to
    /// in-memory keys, which silently signs everyone out and orphans the TOTP secrets on every restart.
    /// </summary>
    public void EnsureKeyRingWritable()
    {
        if (!IsPublic || KeyRingInAzure) return;
        try
        {
            Directory.CreateDirectory(KeyRingPath);
            var probe = Path.Combine(KeyRingPath, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new ThinkTankHostingException(
                $"[thinktank] The Data Protection key ring folder '{KeyRingPath}' ({KeyRingPathKey}) cannot be written: {ex.Message} Not starting.");
        }
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

    internal static Uri? ReadHttpsUri(IConfiguration config, string key, List<string> problems)
    {
        var raw = (config[key] ?? "").Trim();
        if (raw.Length == 0) return null;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return uri;
        problems.Add($"{key} must be an absolute https URL.");
        return null;
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
