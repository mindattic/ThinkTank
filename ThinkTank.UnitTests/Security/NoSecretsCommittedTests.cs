using System.Diagnostics;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ThinkTank.UnitTests.Security;

/// <summary>
/// TT-LAW-6 guard: no real-looking provider credential is committed to the repo.
/// Scans every file git tracks (falls back to a filesystem walk that skips
/// build output and node_modules when git is unavailable). Provider auth lives
/// in Settings.json under %LOCALAPPDATA%, outside the repo, so it is never scanned.
/// </summary>
public class NoSecretsCommittedTests
{
    // Shapes of real credentials, not bare prefixes: a plain "sk-" substring
    // also occurs in words like "task-" and "disk-", so each pattern requires
    // the full-length key body.
    private static readonly (string Name, Regex Pattern)[] KeyPatterns =
    [
        ("Anthropic API key", new Regex(@"sk-ant-[A-Za-z0-9_\-]{32,}")),
        ("OpenAI API key", new Regex(@"\bsk-(?:proj-|svcacct-|admin-)?[A-Za-z0-9_\-]{32,}")),
        ("Google API key", new Regex(@"AIza[0-9A-Za-z_\-]{35}")),
        ("GitHub token", new Regex(@"\b(?:gh[pousr]_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{60,})")),
        ("AWS access key id", new Regex(@"\bAKIA[0-9A-Z]{16}\b")),
        ("Slack token", new Regex(@"\bxox[baprs]-[A-Za-z0-9\-]{10,}")),
        ("Private key block", new Regex(@"-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY")),
    ];

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".webp", ".bmp", ".woff", ".woff2", ".ttf", ".otf",
        ".eot", ".zip", ".gz", ".7z", ".dll", ".exe", ".pdb", ".nupkg", ".pdf", ".mp3", ".mp4", ".wav",
    };

    [Test]
    public void ProviderAuthConfigs_ShouldNotContainRealLookingKeys_InRepoFiles()
    {
        var root = FindRepoRoot();
        var files = TrackedFiles(root);
        Assert.That(files, Is.Not.Empty, $"no repository files found under {root}");

        var leaks = new List<string>();
        foreach (var rel in files)
        {
            if (BinaryExtensions.Contains(Path.GetExtension(rel))) continue;
            var path = Path.Combine(root, rel);
            if (!File.Exists(path)) continue; // tracked but deleted in the working tree
            var text = File.ReadAllText(path);
            foreach (var (name, pattern) in KeyPatterns)
            {
                if (pattern.IsMatch(text))
                    leaks.Add($"{name} in {rel}");
            }
        }

        Assert.That(leaks, Is.Empty,
            "Real-looking credential(s) committed to the repo:\n  " + string.Join("\n  ", leaks));
    }

    [Test]
    public void KeyPatterns_DetectRealShapes_AndIgnoreOrdinaryText()
    {
        // Synthetic keys assembled at runtime so no key-shaped literal lives in this file.
        string[] keyShaped =
        [
            "sk-ant-" + new string('a', 40),
            "sk-proj-" + new string('B', 48),
            "AIza" + new string('x', 35),
            "ghp_" + new string('c', 36),
            "AKIA" + new string('D', 16),
        ];
        foreach (var key in keyShaped)
        {
            Assert.That(KeyPatterns.Any(p => p.Pattern.IsMatch($"apiKey = \"{key}\"")), Is.True,
                $"pattern set should flag {key[..6]}…");
        }

        const string ordinary = "run the task-runner on disk-cleanup; ask-me later; risk-free";
        Assert.That(KeyPatterns.Any(p => p.Pattern.IsMatch(ordinary)), Is.False);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThinkTank.slnx")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "could not locate ThinkTank.slnx above the test directory");
        return dir!.FullName;
    }

    private static List<string> TrackedFiles(string root)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "ls-files -z")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode == 0)
                return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git not installed: fall through to the filesystem walk.
        }

        string[] skipDirs = ["bin", "obj", "node_modules", ".git", ".vs", "TestResults"];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f))
            .Where(rel => !rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => skipDirs.Contains(part, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }
}
