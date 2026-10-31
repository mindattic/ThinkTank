using MindAttic.Legion;
using MindAttic.Vault.Paths;
using NUnit.Framework;
using ThinkTank.Core.Services;

namespace ThinkTank.UnitTests;

/// <summary>
/// Assembly-level fixture that keeps every test out of the user's real AppData: the shared
/// MindAttic.Legion credentials store goes to a per-run sandbox in <c>%TEMP%</c>, and so do the MindAttic
/// Vault roots (<c>MINDATTIC_VAULT_ROAMING_ROOT</c> / <c>_LOCAL_ROOT</c>), which hold ThinkTank's Vault settings,
/// its Settings.json content file and the named keys (LAMP, TT-T27; M.VALT-LAW-6).
/// </summary>
[SetUpFixture]
public class TestAssemblySetup
{
    private const string EnvVar = "MINDATTIC_LLM_CREDENTIALS";

    private string sharedCredentialsSandbox = null!;
    private string? originalSharedRoot;

    /// <summary>The temp folder holding this run's Vault roots.</summary>
    public static string VaultRoot { get; private set; } = "";

    [OneTimeSetUp]
    public void RedirectSharedCredentialsToSandbox()
    {
        sharedCredentialsSandbox = Path.Combine(Path.GetTempPath(), $"mindattic-creds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sharedCredentialsSandbox);
        originalSharedRoot = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, sharedCredentialsSandbox);

        VaultRoot = Path.Combine(Path.GetTempPath(), $"ThinkTankVault-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, Path.Combine(VaultRoot, "roaming"));
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, Path.Combine(VaultRoot, "local"));
    }

    [OneTimeTearDown]
    public void RestoreSharedCredentialsRoot()
    {
        Environment.SetEnvironmentVariable(EnvVar, originalSharedRoot);
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, null);
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, null);
        foreach (var dir in new[] { sharedCredentialsSandbox, VaultRoot })
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { }
        }
    }
}
