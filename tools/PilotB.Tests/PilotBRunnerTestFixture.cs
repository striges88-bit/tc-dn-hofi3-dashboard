using System.Diagnostics;
using System.Text;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

internal sealed class PilotBRunnerTestFixture : IDisposable
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);
    private readonly List<string> junctionPaths = [];

    private PilotBRunnerTestFixture(string root, string fakeExecutablePath)
    {
        Root = root;
        FakeExecutablePath = fakeExecutablePath;
        FixtureRoot = Path.Combine(root, "fixture");
        Directory.CreateDirectory(FixtureRoot);
        InitializeGitRepository(FixtureRoot);
        File.WriteAllText(Path.Combine(FixtureRoot, "README.txt"), "disposable fixture\n");
        ManifestPath = Path.Combine(root, "arm-manifest.json");
        File.WriteAllText(ManifestPath, CreateManifest(FixtureRoot));
    }

    public string Root { get; }
    public string FakeExecutablePath { get; }
    public string FixtureRoot { get; }
    public string ManifestPath { get; }

    public static PilotBRunnerTestFixture Create()
    {
        var root = Directory.CreateTempSubdirectory("pilot-b-runner-test-").FullName;
        var configuration = Directory.GetParent(
                Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))?.Name
            ?? throw new InvalidOperationException("Cannot determine the test output configuration.");
        var fakePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "PilotB.FakeCli", "bin", configuration, "net8.0",
            "CryptoIndicatorApp.PilotB.FakeCli.exe"));
        Assert.True(File.Exists(fakePath), $"Fake CLI was not built: {fakePath}");
        return new PilotBRunnerTestFixture(root, fakePath);
    }

    public PilotBRunnerOptions CreateRequest(string prompt = "pilot-b.fake.valid")
    {
        return new PilotBRunnerOptions
        {
            ExecutablePath = FakeExecutablePath,
            ExpectedExecutableSha256 = PilotBSha256.ComputeFile(FakeExecutablePath),
            ArmManifestPath = ManifestPath,
            ExpectedArmManifestSha256 = PilotBSha256.ComputeFile(ManifestPath),
            FixtureRoot = FixtureRoot,
            ArtifactDirectory = Path.Combine(Root, $"artifacts-{Guid.NewGuid():N}"),
            PromptBytes = Encoding.UTF8.GetBytes(prompt),
            Timeout = TimeSpan.FromSeconds(2),
            IsQualification = true,
            UtcNowProvider = () => FixedNow
        };
    }

    public string? CreateExistingArtifactPath(string artifactPath, string pathKind)
    {
        switch (pathKind)
        {
            case "empty-directory":
                Directory.CreateDirectory(artifactPath);
                return null;
            case "nonempty-directory":
                Directory.CreateDirectory(artifactPath);
                var directorySentinel = Path.Combine(artifactPath, "sentinel.txt");
                File.WriteAllText(directorySentinel, "sentinel");
                return directorySentinel;
            case "file":
                File.WriteAllText(artifactPath, "sentinel");
                return artifactPath;
            case "directory-reparse":
                var target = Path.Combine(Root, $"junction-target-{Guid.NewGuid():N}");
                Directory.CreateDirectory(target);
                CreateDirectoryJunction(artifactPath, target);
                junctionPaths.Add(artifactPath);
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(pathKind), pathKind, "Unknown artifact path kind.");
        }
    }

    public void ReplaceManifestValue(string oldValue, string newValue)
    {
        var original = File.ReadAllText(ManifestPath);
        var replacement = original.Replace(oldValue, newValue, StringComparison.Ordinal);
        Assert.NotEqual(original, replacement);
        File.WriteAllText(ManifestPath, replacement);
    }

    public void Dispose()
    {
        foreach (var junctionPath in junctionPaths)
        {
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath);
            }
        }

        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static string CreateManifest(string fixtureRoot)
    {
        return $$"""
            {
              "schema_version": "pilot-b.arm-manifest.v3",
              "manifest_id": "test-manifest",
              "arm_id": "treatment",
              "cli_version": "test-cli-0.0.1",
              "protocol_sha256": "{{Hash}}",
              "model_alias": "gpt-5.6-sol",
              "reasoning_effort": "max",
              "sandbox": "native-windows",
              "approval_policy": "never",
              "repository_root": "{{fixtureRoot.Replace("\\", "\\\\")}}",
              "global_instructions_sha256": "{{Hash}}",
              "project_instructions_sha256": "{{Hash}}",
              "skills_manifest_sha256": "{{Hash}}",
              "mutable_authentication_lane": "excluded-from-manifest-hash"
            }
            """;
    }

    private static void InitializeGitRepository(string root)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        process.StartInfo.ArgumentList.Add("init");
        process.StartInfo.ArgumentList.Add("--quiet");
        Assert.True(process.Start());
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
    }

    private static void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("mklink");
        process.StartInfo.ArgumentList.Add("/J");
        process.StartInfo.ArgumentList.Add(junctionPath);
        process.StartInfo.ArgumentList.Add(targetPath);
        Assert.True(process.Start());
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
        Assert.True((File.GetAttributes(junctionPath) & FileAttributes.ReparsePoint) != 0);
    }
}
