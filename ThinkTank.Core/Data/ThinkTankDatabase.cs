using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ThinkTank.Core.Data;

/// <summary>
/// Where ThinkTank's SQL database is (TT-T46): the <c>ThinkTank</c> database on the MindAttic Azure SQL server
/// (Basic tier). There is no local SQL Server fallback; development uses the same Azure database.
///
/// <para>Resolution order:</para>
/// <list type="number">
/// <item>env <c>ConnectionStrings__ThinkTank</c>, then configuration <c>ConnectionStrings:ThinkTank</c>. A hosted
/// ThinkTank must be told its database this way (it refuses to start otherwise).</item>
/// <item>On the owner's machine only: the MindAttic SQL service principal in
/// <c>%APPDATA%\MindAttic\Database\providers.json</c> (<c>AzureSql.ClientId</c> / <c>ClientSecret</c>), the same
/// one the workspace's unattended jobs use, so ThinkTank starts without an <c>az login</c>.</item>
/// <item>Otherwise the developer's own Entra sign-in (<c>Active Directory Default</c>: az CLI, Visual Studio).</item>
/// </list>
/// The string is never logged or printed; it may carry the service principal's secret.
/// </summary>
public static class ThinkTankDatabase
{
    public const string ConnectionStringName = "ThinkTank";
    public const string EnvironmentVariable = "ConnectionStrings__ThinkTank";
    public const string Server = "sql-mindattic-f65744.database.windows.net";
    public const string DatabaseName = "ThinkTank";

    /// <summary>The developer's own Entra identity. Holds no secret.</summary>
    public const string DeveloperConnectionString =
        "Server=tcp:" + Server + ",1433;Database=" + DatabaseName + ";Authentication=Active Directory Default;" +
        "Encrypt=True;Connect Timeout=60;MultipleActiveResultSets=True;Application Name=ThinkTank;";

    /// <summary>The configured connection string, or null when neither the environment nor configuration names one.</summary>
    public static string? Configured(IConfiguration? configuration = null, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var fromEnvironment = env(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;
        var fromConfiguration = configuration?.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(fromConfiguration) ? null : fromConfiguration;
    }

    /// <summary>
    /// The connection string to use. <paramref name="hosted"/> = true never falls back to a machine credential:
    /// it returns null when nothing is configured, and the caller refuses to start.
    /// </summary>
    public static string? Resolve(IConfiguration? configuration, bool hosted, string? appDataRoot = null, Func<string, string?>? env = null)
    {
        if (Configured(configuration, env) is { } configured) return configured;
        if (hosted) return null;
        return ServicePrincipalConnectionString(appDataRoot) ?? DeveloperConnectionString;
    }

    /// <summary>
    /// A service-principal connection string built from <c>%APPDATA%\MindAttic\Database\providers.json</c>, or null
    /// when the file or its <c>AzureSql</c> entry is missing or incomplete.
    /// </summary>
    public static string? ServicePrincipalConnectionString(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var file = Path.Combine(root, "MindAttic", "Database", "providers.json");
        if (!File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file).TrimStart('﻿'));
            if (!doc.RootElement.TryGetProperty("AzureSql", out var sql)) return null;
            string? Field(string name) => sql.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var clientId = Field("ClientId");
            var secret = Field("ClientSecret");
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret)) return null;
            var server = Field("Server") is { Length: > 0 } s ? s : Server;
            return new SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{server},1433",
                InitialCatalog = DatabaseName,
                Authentication = SqlAuthenticationMethod.ActiveDirectoryServicePrincipal,
                UserID = clientId,
                Password = secret,
                Encrypt = SqlConnectionEncryptOption.Mandatory,
                ConnectTimeout = 60,
                MultipleActiveResultSets = true,
                ApplicationName = "ThinkTank",
            }.ConnectionString;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
