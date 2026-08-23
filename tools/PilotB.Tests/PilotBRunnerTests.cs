using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task Runner_ValidFakeCli_PublishesSealedValidEvidence()
    {
        using var fixture = TestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.valid");

        var result = await new PilotBRunner().RunAsync(request);

        using var integrity = JsonDocument.Parse(await File.ReadAllTextAsync(result.Artifacts.IntegrityPath));
        using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(result.Artifacts.MetadataPath));
        var root = integrity.RootElement;
        Assert.Equal("sealed", root.GetProperty("evidence_state").GetString());
        Assert.Equal("valid", root.GetProperty("run_validity").GetString());
        Assert.True(root.GetProperty("artifact_complete").GetBoolean());
        var inventory = root.GetProperty("payload_inventory");
        Assert.Equal(7, inventory.GetArrayLength());
        Assert.Equal(
            ["output.jsonl", "stderr.txt", "prompt.bin", "manifest.json", "pre-manifest.json", "post-manifest.json", "metadata.json"],
            inventory.EnumerateArray().Select(entry => entry.GetProperty("path").GetString()).ToArray());
        Assert.Equal(result.DeterministicFingerprint, root.GetProperty("semantic_fingerprint").GetString());
        Assert.Matches("^[0-9a-f]{64}$", result.DeterministicFingerprint);
        Assert.Equal(await File.ReadAllBytesAsync(fixture.ManifestPath), await File.ReadAllBytesAsync(result.Artifacts.ManifestPath));
        Assert.Equal(
            result.Transcript.SemanticMessages.ToArray(),
            PilotBTranscriptParser.Parse(await File.ReadAllBytesAsync(result.Artifacts.RawOutputPath)).SemanticMessages.ToArray());
        Assert.Equal(PilotBRunValidity.Valid, result.Qualification!.Validity);
        Assert.Empty(result.Qualification.InvalidReasons);
        Assert.True(Directory.Exists(Path.Combine(fixture.FixtureRoot, ".git")));
        Assert.False(PilotBFileManifest.IsWithin(fixture.FixtureRoot, result.Artifacts.Root));
        Assert.True(result.IntegrityFacts.AuthLaneExcluded);
        Assert.False(File.Exists(Path.Combine(result.Artifacts.Root, ".pilot-b-write-lock")));
        Assert.Equal(request.Timeout.Ticks, metadata.RootElement.GetProperty("timeout_ticks").GetInt64());
        var elapsedTicks = metadata.RootElement.GetProperty("elapsed_ticks").GetInt64();
        Assert.True(elapsedTicks >= 0);
        Assert.Equal(
            !result.TimedOut && elapsedTicks <= request.Timeout.Ticks,
            metadata.RootElement.GetProperty("timing_valid").GetBoolean());
    }

    [Fact]
    public async Task Runner_PassesPromptBytesUnchangedToFakeCli()
    {
        using var fixture = TestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.valid\n");

        var result = await new PilotBRunner().RunAsync(request);

        Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
        Assert.Equal(PilotBRunValidity.Invalid, result.RunValidity);
        Assert.Contains("nonzero-exit", result.InvalidReasons);
        Assert.Equal(request.PromptBytes, await File.ReadAllBytesAsync(result.Artifacts.PromptPath));
    }

    [Fact]
    public async Task Runner_PreservesV3FixtureManifestEntryWireNames()
    {
        using var fixture = TestFixture.Create();

        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest("pilot-b.fake.valid"));

        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(result.Artifacts.PreManifestPath));
        var entry = manifest.RootElement.GetProperty("files")[0];
        Assert.True(entry.TryGetProperty(nameof(PilotBFileManifestEntry.RelativePath), out _));
        Assert.True(entry.TryGetProperty(nameof(PilotBFileManifestEntry.Length), out _));
        Assert.True(entry.TryGetProperty(nameof(PilotBFileManifestEntry.Sha256), out _));
        Assert.False(entry.TryGetProperty("relative_path", out _));
    }

    [Fact]
    public async Task Runner_HoldsAtomicLockUntilItPublishesFinalSeal()
    {
        using var fixture = TestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.delayed-valid") with
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        var execution = new PilotBRunner().RunAsync(request);
        var lockPath = Path.Combine(request.ArtifactDirectory, ".pilot-b-write-lock");
        await WaitForFileAsync(lockPath);
        Assert.True(File.Exists(lockPath));
        Assert.False(File.Exists(Path.Combine(request.ArtifactDirectory, "integrity.json")));

        var result = await execution;

        Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
        Assert.Equal(PilotBRunValidity.Valid, result.RunValidity);
        Assert.False(File.Exists(lockPath));
        Assert.True(File.Exists(result.Artifacts.SealPath));
    }

    [Theory]
    [InlineData("pilot-b.fake.malformed", "malformed-json")]
    [InlineData("pilot-b.fake.partial", "partial-run")]
    [InlineData("pilot-b.fake.failed", "nonzero-exit")]
    [InlineData("pilot-b.fake.timeout", "timeout")]
    public async Task Runner_ControlledFailure_SealsInvalidEvidence(string prompt, string reason)
    {
        using var fixture = TestFixture.Create();
        var request = fixture.CreateRequest(prompt) with
        {
            Timeout = prompt == "pilot-b.fake.timeout" ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(2)
        };

        var result = await new PilotBRunner().RunAsync(request);

        Assert.Equal(PilotBRunnerStatus.Invalid, result.Status);
        Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
        Assert.Equal(PilotBRunValidity.Invalid, result.RunValidity);
        Assert.True(result.IntegrityFacts.ArtifactComplete);
        Assert.Contains(reason, result.InvalidReasons);
        if (prompt == "pilot-b.fake.timeout")
        {
            Assert.DoesNotContain("nonzero-exit", result.InvalidReasons);
        }
        Assert.Matches("^[0-9a-f]{64}$", result.DeterministicFingerprint);
    }

    [Fact]
    public async Task Runner_SealedEvidence_RequiresIndependentReverification()
    {
        using var fixture = TestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest("pilot-b.fake.valid"));

        var verified = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);
        Assert.Equal(PilotBEvidenceState.Sealed, verified.EvidenceState);
        Assert.Equal(result.DeterministicFingerprint, verified.SemanticFingerprint);

        var integrityJson = await File.ReadAllTextAsync(result.Artifacts.IntegrityPath);
        await File.WriteAllTextAsync(
            result.Artifacts.IntegrityPath,
            integrityJson.Replace("\"auth_lane_excluded\":true", "\"auth_lane_excluded\":false", StringComparison.Ordinal));

        var factTampered = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);
        Assert.Equal(PilotBEvidenceState.Unsealed, factTampered.EvidenceState);
        Assert.Null(factTampered.SemanticFingerprint);

        var payloadResult = await new PilotBRunner().RunAsync(fixture.CreateRequest("pilot-b.fake.valid"));
        await File.AppendAllTextAsync(payloadResult.Artifacts.StderrPath, "tampered");

        var tampered = new PilotBEvidenceBundleVerifier().Verify(payloadResult.Artifacts);
        Assert.Equal(PilotBEvidenceState.Unsealed, tampered.EvidenceState);
        Assert.Null(tampered.SemanticFingerprint);
    }

    [Theory]
    [InlineData("\"Valid\"")]
    [InlineData("\"0\"")]
    [InlineData("\"undefined\"")]
    [InlineData("null")]
    public async Task EvidenceVerifier_RejectsNoncanonicalSealValidity(string jsonValue)
    {
        using var fixture = TestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest("pilot-b.fake.valid"));
        var sealJson = await File.ReadAllTextAsync(result.Artifacts.IntegrityPath);
        var tamperedSeal = sealJson.Replace(
            "\"run_validity\":\"valid\"",
            $"\"run_validity\":{jsonValue}",
            StringComparison.Ordinal);
        Assert.NotEqual(sealJson, tamperedSeal);
        await File.WriteAllTextAsync(result.Artifacts.IntegrityPath, tamperedSeal);

        var verified = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);

        Assert.Equal(PilotBEvidenceState.Unsealed, verified.EvidenceState);
        Assert.Null(verified.Qualification);
        Assert.Null(verified.SemanticFingerprint);
    }

    [Theory]
    [InlineData("\"Valid\"")]
    [InlineData("\"0\"")]
    public async Task EvidenceVerifier_RejectsNoncanonicalQualificationValidity(string jsonValue)
    {
        using var fixture = TestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest("pilot-b.fake.valid"));
        await ReplaceMetadataQualificationValidityAsync(result.Artifacts, jsonValue);

        var verified = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);

        Assert.Equal(PilotBEvidenceState.Unsealed, verified.EvidenceState);
        Assert.Null(verified.Qualification);
        Assert.Null(verified.SemanticFingerprint);
    }

    [Fact]
    public void RunQualification_FailedProcessStart_DoesNotReportNonzeroExit()
    {
        var transcript = PilotBTranscriptParser.Parse(Encoding.UTF8.GetBytes("""
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"turn.started"}
            {"type":"item.completed","item":{"type":"agent_message","phase":"final","text":"Done."}}
            {"type":"turn.completed"}
            """));

        var result = PilotBRunQualification.Evaluate(new PilotBRunQualificationFacts(
            ProcessStarted: false,
            ExitCode: null,
            TimedOut: false,
            transcript,
            TimingValid: true,
            ExecutableHashValid: true,
            RepositoryBoundaryValid: true,
            PromptBytesVerified: true,
            WorkspaceIntegrityCaptured: true,
            PayloadCaptured: true,
            AdditionalInvalidReasons: []));

        Assert.Equal(PilotBRunValidity.Invalid, result.Validity);
        Assert.Equal(["process-start-failed"], result.InvalidReasons);
    }

    [Fact]
    public async Task Runner_SemanticFingerprint_ExcludesStorageOnlyData()
    {
        using var firstFixture = TestFixture.Create();
        using var secondFixture = TestFixture.Create();
        var runner = new PilotBRunner();

        var canonical = await runner.RunAsync(firstFixture.CreateRequest("pilot-b.fake.valid"));
        var differentPathAndTime = await runner.RunAsync(secondFixture.CreateRequest("pilot-b.fake.valid") with
        {
            UtcNowProvider = () => FixedNow.AddDays(1)
        });

        var originalManifest = await File.ReadAllTextAsync(firstFixture.ManifestPath);
        await File.WriteAllTextAsync(firstFixture.ManifestPath, "\r\n" + originalManifest + "\r\n");
        var differentlyFormattedManifest = await runner.RunAsync(firstFixture.CreateRequest("pilot-b.fake.valid") with
        {
            UtcNowProvider = () => FixedNow.AddDays(2)
        });
        var rawOutputVariant = Encoding.UTF8.GetBytes("""
            { "thread_id" : "another-storage-only-thread", "type" : "thread.started" }
            { "type" : "turn.started" }
            { "item" : { "type" : "reasoning", "text" : "hidden" }, "type" : "item.completed" }
            { "item" : { "type" : "tool_call", "name" : "noop" }, "type" : "item.completed" }
            { "item" : { "type" : "agent_message", "phase" : "commentary", "text" : "Verified the fixture boundary." }, "type" : "item.completed" }
            { "item" : { "type" : "agent_message", "phase" : "final", "text" : "Done." }, "type" : "item.completed" }
            { "type" : "turn.completed" }
            """);
        var reformattedTranscript = PilotBTranscriptParser.Parse(rawOutputVariant);
        var manifest = PilotBArmManifest.Parse(await File.ReadAllBytesAsync(canonical.Artifacts.ManifestPath));
        var preManifest = PilotBFileManifest.Parse(await File.ReadAllBytesAsync(canonical.Artifacts.PreManifestPath));
        var postManifest = PilotBFileManifest.Parse(await File.ReadAllBytesAsync(canonical.Artifacts.PostManifestPath));
        var rawOutputFingerprint = PilotBRunFingerprintWriter.Compute(new PilotBRunFingerprintInput(
            canonical.IntegrityFacts.ExecutableSha256,
            canonical.IntegrityFacts.PromptSha256,
            manifest,
            reformattedTranscript,
            PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(preManifest),
            PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(postManifest),
            canonical.IsQualification,
            canonical.Qualification!,
            canonical.ExitCode,
            canonical.TimedOut));

        Assert.All(
            new[] { canonical, differentPathAndTime, differentlyFormattedManifest },
            result =>
            {
                Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
                Assert.Equal(PilotBRunValidity.Valid, result.RunValidity);
            });
        Assert.False((await File.ReadAllBytesAsync(canonical.Artifacts.RawOutputPath))
            .SequenceEqual(rawOutputVariant));
        Assert.Equal(canonical.Transcript.SemanticMessages.ToArray(), reformattedTranscript.SemanticMessages.ToArray());
        Assert.Equal(canonical.DeterministicFingerprint, rawOutputFingerprint);
        Assert.Equal(canonical.DeterministicFingerprint, differentPathAndTime.DeterministicFingerprint);
        Assert.Equal(canonical.DeterministicFingerprint, differentlyFormattedManifest.DeterministicFingerprint);
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

    [Fact]
    public async Task Runner_RejectsEveryImmutablePreflightInputBeforeEvidenceOwnership()
    {
        using var fixture = TestFixture.Create();
        var cases = new (string Name, Func<PilotBRunnerOptions, PilotBRunnerOptions> Change)[]
        {
            ("executable", options => options with { ExecutablePath = "fake.exe" }),
            ("manifest-path", options => options with { ArmManifestPath = "arm-manifest.json" }),
            ("manifest-hash", options => options with { ExpectedArmManifestSha256 = new string('b', 64) }),
            ("fixture", options => options with { FixtureRoot = "fixture" }),
            ("artifact", options => options with { ArtifactDirectory = "artifacts" }),
            ("prompt", options => options with { PromptBytes = [] }),
            ("timeout", options => options with { Timeout = TimeSpan.Zero })
        };

        foreach (var testCase in cases)
        {
            var request = fixture.CreateRequest("pilot-b.fake.valid");
            var untouchedArtifactPath = request.ArtifactDirectory;

            var result = await new PilotBRunner().RunAsync(testCase.Change(request));

            Assert.Equal(PilotBRunnerStatus.Invalid, result.Status);
            Assert.Equal(PilotBEvidenceState.Unsealed, result.EvidenceState);
            Assert.Equal(PilotBArtifactPaths.Empty, result.Artifacts);
            Assert.False(Directory.Exists(untouchedArtifactPath), testCase.Name);
        }
    }

    [Fact]
    public async Task Runner_BindsExplicitQualificationMarkerIntoSealedEvidence()
    {
        using var fixture = TestFixture.Create();

        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest("pilot-b.fake.valid") with
        {
            IsQualification = false
        });

        using var integrity = JsonDocument.Parse(await File.ReadAllTextAsync(result.Artifacts.IntegrityPath));
        Assert.False(result.IsQualification);
        Assert.True(result.IsScored);
        Assert.False(integrity.RootElement.GetProperty("qualification_marker").GetBoolean());
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
            var configuration = Directory.GetParent(
                    Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))?.Name
                ?? throw new InvalidOperationException("Cannot determine the test output configuration.");
            var fakePath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "PilotB.FakeCli", "bin", configuration, "net8.0",
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

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!File.Exists(path))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for '{path}'.");
            }

            await Task.Delay(10);
        }
    }

    private static async Task ReplaceMetadataQualificationValidityAsync(
        PilotBArtifactPaths artifacts,
        string jsonValue)
    {
        var metadataJson = await File.ReadAllTextAsync(artifacts.MetadataPath);
        var tamperedMetadata = metadataJson.Replace(
            "\"validity\":\"valid\"",
            $"\"validity\":{jsonValue}",
            StringComparison.Ordinal);
        Assert.NotEqual(metadataJson, tamperedMetadata);
        await File.WriteAllTextAsync(artifacts.MetadataPath, tamperedMetadata);
        var metadataBytes = await File.ReadAllBytesAsync(artifacts.MetadataPath);

        var seal = JsonNode.Parse(await File.ReadAllBytesAsync(artifacts.IntegrityPath))!.AsObject();
        var metadataEntry = seal["payload_inventory"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(entry => entry["path"]!.GetValue<string>() == "metadata.json");
        metadataEntry["length"] = metadataBytes.LongLength;
        metadataEntry["sha256"] = PilotBSha256.Compute(metadataBytes);
        await File.WriteAllTextAsync(artifacts.IntegrityPath, seal.ToJsonString());
    }
}
