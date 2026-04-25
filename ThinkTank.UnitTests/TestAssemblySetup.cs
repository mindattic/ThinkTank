using NUnit.Framework;
using LLMThinkTank.Core.Services;

namespace LLMThinkTank.UnitTests;

/// <summary>
/// Assembly-level fixture that redirects the shared MindAttic LLM credentials store to a
/// per-run sandbox directory in <c>%TEMP%</c>. Without this, every test that constructs a
/// <see cref="SettingsService"/> would read from and write to the user's real
/// <c>%APPDATA%\MindAttic\LLM\providers.json</c>, polluting their actual API keys.
/// </summary>
[SetUpFixture]
public class TestAssemblySetup
{
    private string sharedCredentialsSandbox = null!;
    private string? originalSharedRoot;

    [OneTimeSetUp]
    public void RedirectSharedCredentialsToSandbox()
    {
        sharedCredentialsSandbox = Path.Combine(Path.GetTempPath(), $"mindattic-creds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sharedCredentialsSandbox);
        originalSharedRoot = Environment.GetEnvironmentVariable(MindAtticLlmCredentialsStore.RootEnvVar);
        Environment.SetEnvironmentVariable(MindAtticLlmCredentialsStore.RootEnvVar, sharedCredentialsSandbox);
    }

    [OneTimeTearDown]
    public void RestoreSharedCredentialsRoot()
    {
        Environment.SetEnvironmentVariable(MindAtticLlmCredentialsStore.RootEnvVar, originalSharedRoot);
        try
        {
            if (Directory.Exists(sharedCredentialsSandbox))
                Directory.Delete(sharedCredentialsSandbox, recursive: true);
        }
        catch { }
    }
}
