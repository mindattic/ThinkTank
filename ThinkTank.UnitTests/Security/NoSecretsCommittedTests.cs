//using NUnit.Framework;

namespace LLMThinkTank.UnitTests.Security;

public class NoSecretsCommittedTests
{
    //[Test]
    //public void ProviderAuthConfigs_ShouldNotContainRealLookingKeys_InRepoFiles()
    //{
    //    // This test only checks repository files, not the user's local app data.
    //    // It acts as a guardrail against accidentally committing secrets.
    //    var root = TestContext.CurrentContext.TestDirectory;
    //    while (!Directory.EnumerateFiles(root, "*.csproj").Any() && Directory.GetParent(root) is not null)
    //        root = Directory.GetParent(root)!.FullName;

    //    foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
    //    {
    //        var name = Path.GetFileName(file).ToLowerInvariant();
    //        if (name is "settings.json")
    //            continue;

    //        var text = File.ReadAllText(file);
    //        Assert.That(text, Does.Not.Contain("sk-"), $"Potential OpenAI key leaked in {file}");
    //        Assert.That(text, Does.Not.Contain("AIza"), $"Potential Google API key leaked in {file}");
    //    }
    //}
}
