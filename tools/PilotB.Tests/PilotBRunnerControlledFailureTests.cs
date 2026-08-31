using System.Text.Json;
using System.Text.Json.Nodes;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed class PilotBRunnerControlledFailureTests
{
    private const int ControlledNonzeroExitCode = 23;

    [Theory]
    [MemberData(nameof(MalformedAndPartialCases))]
    public async Task Runner_MalformedOrPartialTranscript_SealsExactInvalidQualification(
        string prompt,
        string[] expectedReasons)
    {
        using var fixture = PilotBRunnerTestFixture.Create();

        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest(prompt));

        await AssertSealedInvalidAgreementAsync(result, expectedReasons);
    }

    [Fact]
    public async Task Runner_UnsupportedTranscript_SealsExactInvalidQualification()
    {
        using var fixture = PilotBRunnerTestFixture.Create();

        var result = await new PilotBRunner().RunAsync(
            fixture.CreateRequest("pilot-b.fake.unsupported"));

        await AssertSealedInvalidAgreementAsync(result, ["unsupported-event-type"]);
    }

    [Fact]
    public async Task Runner_OutOfOrderTranscript_SealsExactInvalidQualification()
    {
        using var fixture = PilotBRunnerTestFixture.Create();

        var result = await new PilotBRunner().RunAsync(
            fixture.CreateRequest("pilot-b.fake.out-of-order"));

        await AssertSealedInvalidAgreementAsync(
            result,
            ["turn-started-before-thread-started", "thread-started-not-first"]);
    }

    [Fact]
    public async Task Runner_TerminalFailure_SealsExactInvalidQualification()
    {
        using var fixture = PilotBRunnerTestFixture.Create();

        var result = await new PilotBRunner().RunAsync(
            fixture.CreateRequest("pilot-b.fake.terminal-failure"));

        await AssertSealedInvalidAgreementAsync(result, ["turn-failed", "failed-event"]);
    }

    [Fact]
    public async Task Runner_CleanTranscriptWithNonzeroExit_SealsOnlyProcessFailure()
    {
        using var fixture = PilotBRunnerTestFixture.Create();

        var result = await new PilotBRunner().RunAsync(
            fixture.CreateRequest("pilot-b.fake.nonzero"));

        Assert.True(result.Transcript.IsValid, string.Join("|", result.Transcript.InvalidReasons));
        Assert.Equal(ControlledNonzeroExitCode, result.ExitCode);
        Assert.Contains(
            "fake nonzero exit diagnostic",
            await File.ReadAllTextAsync(result.Artifacts.StderrPath),
            StringComparison.Ordinal);
        await AssertSealedInvalidAgreementAsync(result, ["nonzero-exit"]);
    }

    [Fact]
    public void RunQualification_MixedFailures_UsesCanonicalOrderAndDeduplicatesReasons()
    {
        var transcript = PilotBTranscriptParser.Parse("""
            {"type":"turn.started"}
            {"type":"thread.started","thread_id":"fake-thread"}
            {"type":"turn.started"}
            {"type":"turn.completed"}
            """);

        var qualification = PilotBRunQualification.Evaluate(new PilotBRunQualificationFacts(
            ProcessStarted: true,
            ExitCode: ControlledNonzeroExitCode,
            TimedOut: true,
            transcript,
            TimingValid: false,
            ExecutableHashValid: false,
            RepositoryBoundaryValid: false,
            PromptBytesVerified: false,
            WorkspaceIntegrityCaptured: false,
            PayloadCaptured: false,
            AdditionalInvalidReasons:
            [
                "timeout",
                "executable-drift",
                "custom-capture-failure",
                "custom-capture-failure"
            ]));

        Assert.Equal(PilotBRunValidity.Invalid, qualification.Validity);
        Assert.Equal(
            [
                "timeout",
                "turn-started-before-thread-started",
                "thread-started-not-first",
                "timing-violation",
                "executable-drift",
                "repository-boundary-invalid",
                "prompt-bytes-unverified",
                "workspace-integrity-missing",
                "missing-artifact",
                "custom-capture-failure"
            ],
            qualification.InvalidReasons);
        Assert.DoesNotContain("nonzero-exit", qualification.InvalidReasons);
    }

    [Fact]
    public async Task EvidenceVerifier_ControlledInvalidSealReasonDrift_FailsClosed()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(
            fixture.CreateRequest("pilot-b.fake.unsupported"));
        var seal = JsonNode.Parse(await File.ReadAllBytesAsync(result.Artifacts.SealPath))!.AsObject();
        seal["invalid_reasons"] = new JsonArray("different-reason");
        await File.WriteAllTextAsync(result.Artifacts.SealPath, seal.ToJsonString());

        var verification = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);

        Assert.Equal(PilotBEvidenceState.Unsealed, verification.EvidenceState);
        Assert.Null(verification.Qualification);
        Assert.Null(verification.SemanticFingerprint);
    }

    [Fact]
    public async Task Runner_PostStartExecutableDrift_SealsExactInvalidQualification()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var request = CreateRequestWithDisposableExecutable(fixture, "pilot-b.fake.valid");
        var publisher = new PostStartMutationPublisher(() => AppendDriftByte(request.ExecutablePath));

        var result = await new PilotBRunner(publisher).RunAsync(request);

        Assert.True(publisher.MutationObserved);
        await AssertSealedInvalidAgreementAsync(result, ["executable-drift"]);
    }

    [Fact]
    public async Task Runner_PostStartRepositoryBoundaryDrift_SealsExactInvalidQualification()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var publisher = new PostStartMutationPublisher(
            () => Directory.Delete(Path.Combine(fixture.FixtureRoot, ".git"), recursive: true));

        var result = await new PilotBRunner(publisher).RunAsync(
            fixture.CreateRequest("pilot-b.fake.valid"));

        Assert.True(publisher.MutationObserved);
        await AssertSealedInvalidAgreementAsync(result, ["repository-boundary-invalid"]);
    }

    [Fact]
    public async Task Runner_PostStartPromptDrift_SealsExactInvalidQualification()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var request = fixture.CreateRequest("pilot-b.fake.valid");
        var publisher = new PostStartMutationPublisher(
            () => AppendDriftByte(Path.Combine(request.ArtifactDirectory, "prompt.bin")));

        var result = await new PilotBRunner(publisher).RunAsync(request);

        Assert.True(publisher.MutationObserved);
        var expectedPromptSha = PilotBSha256.Compute(request.PromptBytes);
        Assert.NotEqual(expectedPromptSha, PilotBSha256.ComputeFile(result.Artifacts.PromptPath));
        Assert.Equal(expectedPromptSha, result.IntegrityFacts.PromptSha256);
        using (var metadata = JsonDocument.Parse(
                   await File.ReadAllBytesAsync(result.Artifacts.MetadataPath)))
        {
            Assert.Equal(expectedPromptSha, metadata.RootElement.GetProperty("prompt_sha256").GetString());
        }
        await AssertSealedInvalidAgreementAsync(result, ["prompt-bytes-unverified"]);
    }

    public static TheoryData<string, string[]> MalformedAndPartialCases => new()
    {
        {
            "pilot-b.fake.malformed",
            ["malformed-json", "missing-turn-started", "partial-run"]
        },
        {
            "pilot-b.fake.partial",
            ["missing-turn-started", "partial-run"]
        }
    };

    private static PilotBRunnerOptions CreateRequestWithDisposableExecutable(
        PilotBRunnerTestFixture fixture,
        string prompt)
    {
        var sourceDirectory = Path.GetDirectoryName(fixture.FakeExecutablePath)
            ?? throw new InvalidOperationException("Cannot resolve the fake CLI output directory.");
        var targetDirectory = Path.Combine(fixture.Root, "disposable-fake-cli");
        Directory.CreateDirectory(targetDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(sourcePath, Path.Combine(targetDirectory, Path.GetFileName(sourcePath)));
        }

        var executablePath = Path.Combine(targetDirectory, Path.GetFileName(fixture.FakeExecutablePath));
        return fixture.CreateRequest(prompt) with
        {
            ExecutablePath = executablePath,
            ExpectedExecutableSha256 = PilotBSha256.ComputeFile(executablePath)
        };
    }

    private static void AppendDriftByte(string path)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.WriteByte(0);
    }

    private static async Task AssertSealedInvalidAgreementAsync(
        PilotBRunnerResult result,
        IReadOnlyList<string> expectedReasons)
    {
        Assert.Equal(expectedReasons.Count, expectedReasons.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(PilotBRunnerStatus.Invalid, result.Status);
        Assert.Equal(PilotBEvidenceState.Sealed, result.EvidenceState);
        Assert.Equal(PilotBRunValidity.Invalid, result.RunValidity);
        Assert.NotNull(result.Qualification);
        Assert.Equal(expectedReasons, result.InvalidReasons);
        Assert.Equal(expectedReasons, result.Qualification.InvalidReasons);
        Assert.True(result.IntegrityFacts.ArtifactComplete);
        Assert.Matches("^[0-9a-f]{64}$", result.DeterministicFingerprint);

        using var metadata = JsonDocument.Parse(
            await File.ReadAllBytesAsync(result.Artifacts.MetadataPath));
        var metadataQualification = metadata.RootElement.GetProperty("run_qualification");
        Assert.Equal("invalid", metadataQualification.GetProperty("validity").GetString());
        Assert.Equal(
            expectedReasons,
            metadataQualification.GetProperty("invalid_reasons")
                .EnumerateArray()
                .Select(reason => reason.GetString()!)
                .ToArray());

        using var seal = JsonDocument.Parse(
            await File.ReadAllBytesAsync(result.Artifacts.SealPath));
        Assert.Equal("sealed", seal.RootElement.GetProperty("evidence_state").GetString());
        Assert.Equal("invalid", seal.RootElement.GetProperty("run_validity").GetString());
        Assert.Equal(
            expectedReasons,
            seal.RootElement.GetProperty("invalid_reasons")
                .EnumerateArray()
                .Select(reason => reason.GetString()!)
                .ToArray());

        Assert.All(
            PilotBEvidenceBundle.PayloadNames,
            name => Assert.True(File.Exists(Path.Combine(result.Artifacts.Root, name)), name));

        var verification = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);
        Assert.Equal(PilotBEvidenceState.Sealed, verification.EvidenceState);
        Assert.NotNull(verification.Qualification);
        Assert.Equal(PilotBRunValidity.Invalid, verification.Qualification.Validity);
        Assert.Equal(expectedReasons, verification.Qualification.InvalidReasons);
        Assert.Equal(result.DeterministicFingerprint, verification.SemanticFingerprint);
    }

    private sealed class PostStartMutationPublisher(Action mutation) : IEvidenceBundlePublisher
    {
        private readonly PilotBEvidenceBundlePublisher inner = new();
        private int mutationState;

        public bool MutationObserved => mutationState != 0;

        public Task WriteNewBytesAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
        {
            if (string.Equals(Path.GetFileName(path), "post-manifest.json", StringComparison.Ordinal)
                && Interlocked.Exchange(ref mutationState, 1) == 0)
            {
                mutation();
            }

            return inner.WriteNewBytesAsync(path, bytes, cancellationToken);
        }

        public Task PublishSealAsync(
            PilotBArtifactPaths paths,
            PilotBEvidenceMetadata metadata,
            string semanticFingerprint,
            CancellationToken cancellationToken)
            => inner.PublishSealAsync(paths, metadata, semanticFingerprint, cancellationToken);
    }
}
