using System.Text;
using System.Diagnostics;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed class PilotBRunnerTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Runner_ValidQualification_CapturesExactInvocationAndEvidenceArtifacts()
    {
        using var fixture = TestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.valid");

        var result = await new PilotBRunner().RunAsync(request);

        Assert.True(result.Status == PilotBRunnerStatus.Valid, string.Join("|", result.InvalidReasons));
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.IsQualification);
        Assert.False(result.IsScored);
        Assert.Equal(["codex", "exec", "--ephemeral", "--json"], result.InvocationArguments);
        Assert.Single(result.Transcript.IntermediateMessages);
        Assert.Equal(Encoding.UTF8.GetBytes("pilot-b.fake.valid"), await File.ReadAllBytesAsync(result.Artifacts.PromptPath));
        Assert.True(File.Exists(result.Artifacts.RawOutputPath));
        Assert.True(File.Exists(result.Artifacts.MetadataPath));
        Assert.True(File.Exists(result.Artifacts.ManifestPath));
        Assert.True(File.Exists(result.Artifacts.PreManifestPath));
        Assert.True(File.Exists(result.Artifacts.PostManifestPath));
        Assert.True(File.Exists(result.Artifacts.IntegrityPath));
        Assert.Contains("test-cli-0.0.1", await File.ReadAllTextAsync(result.Artifacts.MetadataPath), StringComparison.Ordinal);
        Assert.Contains("auth_lane_excluded", await File.ReadAllTextAsync(result.Artifacts.IntegrityPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_RequiresAbsoluteExecutableAndExpectedSha256()
    {
        using var fixture = TestFixture.Create();
        var relative = fixture.CreateRequest("pilot-b.fake.valid") with { ExecutablePath = "fake.exe" };
        var drifted = fixture.CreateRequest("pilot-b.fake.valid") with
        {
            ExpectedExecutableSha256 = new string('b', 64)
        };

        var relativeResult = await new PilotBRunner().RunAsync(relative);
        var driftedResult = await new PilotBRunner().RunAsync(drifted);

        Assert.Equal(PilotBRunnerStatus.Invalid, relativeResult.Status);
        Assert.Contains("executable-not-absolute", relativeResult.InvalidReasons);
        Assert.Equal(PilotBRunnerStatus.Invalid, driftedResult.Status);
        Assert.Contains("executable-hash-mismatch", driftedResult.InvalidReasons);
    }

    [Theory]
    [InlineData("pilot-b.fake.malformed", "malformed-json")]
    [InlineData("pilot-b.fake.partial", "partial-run")]
    [InlineData("pilot-b.fake.failed", "nonzero-exit")]
    public async Task Runner_RejectsMalformedPartialAndFailedRuns(string prompt, string reason)
    {
        using var fixture = TestFixture.Create();

        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest(prompt));

        Assert.Equal(PilotBRunnerStatus.Invalid, result.Status);
        Assert.Contains(reason, result.InvalidReasons);
    }

    [Fact]
    public async Task Runner_RejectsTimeoutAsInvalidEvidence()
    {
        using var fixture = TestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.timeout") with
        {
            Timeout = TimeSpan.FromMilliseconds(100)
        };

        var result = await new PilotBRunner().RunAsync(request);

        Assert.Equal(PilotBRunnerStatus.Invalid, result.Status);
        Assert.True(result.TimedOut);
        Assert.Contains("timeout", result.InvalidReasons);
    }

    [Fact]
    public async Task Runner_RejectsArtifactInsideFixtureAsBoundaryContamination()
    {
        using var fixture = TestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.valid") with
        {
            ArtifactDirectory = Path.Combine(fixture.FixtureRoot, "artifacts")
        };

        var result = await new PilotBRunner().RunAsync(request);

        Assert.Equal(PilotBRunnerStatus.Invalid, result.Status);
        Assert.Contains("boundary-contamination", result.InvalidReasons);
    }

    [Fact]
    public async Task Runner_RepeatedFixtureHasIdenticalDeterministicFingerprint()
    {
        using var fixture = TestFixture.Create();
        var first = await new PilotBRunner().RunAsync(fixture.CreateRequest("pilot-b.fake.valid"));
        var second = await new PilotBRunner().RunAsync(fixture.CreateRequest("pilot-b.fake.valid"));

        Assert.True(first.Status == PilotBRunnerStatus.Valid, string.Join("|", first.InvalidReasons));
        Assert.True(second.Status == PilotBRunnerStatus.Valid, string.Join("|", second.InvalidReasons));
        Assert.Equal(first.DeterministicFingerprint, second.DeterministicFingerprint);
        Assert.Equal(first.Transcript.IntermediateMessages, second.Transcript.IntermediateMessages);
        Assert.Equal(first.IntegrityFacts, second.IntegrityFacts);
    }

    private sealed class TestFixture : IDisposable
    {
        private TestFixture(string root, string fakeExecutablePath)
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

        public static TestFixture Create()
        {
            var root = Directory.CreateTempSubdirectory("pilot-b-runner-test-").FullName;
            var fakePath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "PilotB.FakeCli", "bin", "Debug", "net8.0",
                "CryptoIndicatorApp.PilotB.FakeCli.exe"));
            Assert.True(File.Exists(fakePath), $"Fake CLI was not built: {fakePath}");
            return new TestFixture(root, fakePath);
        }

        public PilotBRunnerOptions CreateRequest(string prompt)
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

        public void Dispose()
        {
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
    }
}
