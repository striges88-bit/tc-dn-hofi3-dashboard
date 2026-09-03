using System.Text;
using System.Text.Json;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

[Collection(PilotBProcessBackedRunnerCollection.Name)]
public sealed class PilotBRunRecordProducerTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset PairStart = new(2026, 8, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunnerAndVerifier_ExposeTheSameTypedVerifiedProjection()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest() with
        {
            IsQualification = false
        });

        var verification = new PilotBEvidenceBundleVerifier().Verify(result.Artifacts);
        var projection = Assert.IsType<PilotBVerifiedEvidenceProjection>(verification.VerifiedEvidence);

        Assert.Equal(projection.IsQualification, result.IsQualification);
        Assert.Equal(projection.ExitCode, result.ExitCode);
        Assert.Equal(projection.TimedOut, result.TimedOut);
        Assert.Equal(projection.Qualification.Validity, result.RunValidity);
        Assert.Equal(projection.Qualification.InvalidReasons, result.InvalidReasons);
        Assert.Equal(projection.Transcript.SemanticMessages.ToArray(), result.Transcript.SemanticMessages.ToArray());
        Assert.Equal(projection.IntegrityFacts, result.IntegrityFacts);
        Assert.Equal(projection.SemanticFingerprint, result.DeterministicFingerprint);
        Assert.Throws<NotSupportedException>(
            () => ((IList<string>)projection.InvocationArguments).Add("mutated"));
        Assert.Throws<NotSupportedException>(
            () => ((IList<PilotBTranscriptMessage>)projection.Transcript.SemanticMessages).Clear());
        Assert.DoesNotContain(
            typeof(PilotBRunnerResult).GetProperties(),
            property => string.Equals(property.Name, "IsScored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Producer_ValidEvidence_ProjectsExactCommentaryAndRoundTripsRunRecordV3()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var runnerResult = await new PilotBRunner().RunAsync(fixture.CreateRequest() with
        {
            IsQualification = false
        });
        var request = CreateProductionRequest(
            runnerResult.Artifacts.Root,
            [PilotBMessageKind.Observable]);

        var result = new PilotBRunRecordProducer().Produce(request);

        Assert.Null(result.Rejection);
        var record = Assert.IsType<PilotBRunRecord>(result.RunRecord);
        var manifest = PilotBArmManifest.Parse(await File.ReadAllBytesAsync(runnerResult.Artifacts.ManifestPath));
        Assert.Equal(PilotBArm.Treatment, record.Arm);
        Assert.Equal(manifest.ProtocolSha256, record.ProtocolSha256);
        Assert.Equal(Hash, record.SourceManifestSha256);
        Assert.Equal(runnerResult.IntegrityFacts.ExecutableSha256, record.ExecutableSha256);
        Assert.Equal(runnerResult.IntegrityFacts.PromptSha256, record.PromptSha256);
        Assert.Equal(PilotBRunValidity.Valid, record.Validity);
        Assert.Empty(record.InvalidReasons);
        Assert.Equal(
            PilotBRunRecordProjection.ProjectCommentary(
                runnerResult.Transcript,
                request.MessageKinds).ToArray(),
            record.Messages.ToArray());
        Assert.True(record.Integrity.ArtifactComplete);
        Assert.True(record.Integrity.PromptBytesVerified);

        var jsonl = PilotBRunRecordJsonl.Serialize(record);
        var roundTrip = PilotBRunRecordJsonl.ParseSingle(jsonl);
        Assert.Equal(jsonl, PilotBRunRecordJsonl.Serialize(roundTrip));
        Assert.Equal(record.RunId, roundTrip.RunId);
        Assert.Equal(record.Messages.ToArray(), roundTrip.Messages.ToArray());
        Assert.Equal(record.Integrity, roundTrip.Integrity);
        using var json = JsonDocument.Parse(jsonl);
        Assert.Equal(PilotBContractVersions.RunRecord, json.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal(
            [
                "schema_version", "record_type", "run_id", "pair_id", "case_id", "arm", "replica",
                "is_safety_case", "started_at_utc", "completed_at_utc", "protocol_sha256",
                "source_manifest_sha256", "executable_sha256", "prompt_sha256", "pairing", "validity",
                "invalid_reasons", "messages", "adjudication", "integrity"
            ],
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.False(json.RootElement.TryGetProperty("semantic_fingerprint", out _));
        Assert.False(json.RootElement.TryGetProperty("qualification_marker", out _));
    }

    [Fact]
    public async Task Producer_ProjectsCommentaryAfterFinalWithExactGlobalSequenceAndOrder()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var source = await new PilotBRunner().RunAsync(fixture.CreateRequest() with
        {
            IsQualification = false
        });
        var output = Encoding.UTF8.GetBytes("""
            {"type":"thread.started","thread_id":"projection-thread"}
            {"type":"turn.started"}
            {"type":"item.completed","item":{"type":"agent_message","phase":"final","text":"Retained final."}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"first  exact\nline"}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"second exact"}}
            {"type":"turn.completed"}
            """);
        var variant = await CreateStorageVariantAsync(
            fixture,
            source,
            TimeSpan.FromHours(1),
            output);

        var result = new PilotBRunRecordProducer().Produce(CreateProductionRequest(
            variant.Paths.Root,
            [PilotBMessageKind.Routine, PilotBMessageKind.Observable]));

        var record = Assert.IsType<PilotBRunRecord>(result.RunRecord);
        Assert.Null(result.Rejection);
        Assert.Equal([2, 3], record.Messages.Select(message => message.Sequence));
        Assert.Equal(["first  exact\nline", "second exact"], record.Messages.Select(message => message.Text));
        Assert.Equal(
            [PilotBMessageKind.Routine, PilotBMessageKind.Observable],
            record.Messages.Select(message => message.Kind));
        Assert.All(record.Messages, message =>
        {
            Assert.Equal("item.completed", message.SourceEventType);
            Assert.Equal("commentary", message.Phase);
        });
        Assert.DoesNotContain(record.Messages, message => message.Text.Contains("final", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("unsealed", PilotBRunRecordRejectionCode.UnsealedEvidence)]
    [InlineData("sealed-invalid", PilotBRunRecordRejectionCode.InvalidRun)]
    [InlineData("qualification", PilotBRunRecordRejectionCode.QualificationEvidence)]
    public async Task Producer_RejectsIneligibleEvidenceWithoutMutatingIt(
        string evidenceKind,
        PilotBRunRecordRejectionCode expectedCode)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var runnerRequest = fixture.CreateRequest(
            evidenceKind == "sealed-invalid" ? "pilot-b.fake.malformed" : "pilot-b.fake.valid") with
        {
            IsQualification = evidenceKind == "qualification"
        };
        var runnerResult = await new PilotBRunner().RunAsync(runnerRequest);
        if (evidenceKind == "unsealed")
        {
            await File.AppendAllTextAsync(runnerResult.Artifacts.StderrPath, "tampered");
        }

        var before = CaptureBundleState(runnerResult.Artifacts.Root);
        var result = new PilotBRunRecordProducer().Produce(CreateProductionRequest(
            runnerResult.Artifacts.Root,
            [PilotBMessageKind.Observable]));
        var after = CaptureBundleState(runnerResult.Artifacts.Root);

        Assert.Null(result.RunRecord);
        Assert.Equal(expectedCode, Assert.IsType<PilotBRunRecordRejection>(result.Rejection).Code);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Producer_InvalidProjectionInput_ReturnsTypedRejectionWithoutMutatingEvidence()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var runnerResult = await new PilotBRunner().RunAsync(fixture.CreateRequest() with
        {
            IsQualification = false
        });
        var before = CaptureBundleState(runnerResult.Artifacts.Root);
        var request = CreateProductionRequest(runnerResult.Artifacts.Root, []);

        var result = new PilotBRunRecordProducer().Produce(request);

        Assert.Null(result.RunRecord);
        Assert.Equal(
            PilotBRunRecordRejectionCode.InvalidProjectionInput,
            Assert.IsType<PilotBRunRecordRejection>(result.Rejection).Code);
        Assert.Equal(before, CaptureBundleState(runnerResult.Artifacts.Root));
    }

    [Fact]
    public async Task PublisherVerifierProducerScorer_IsRepeatableAcrossStorageOnlyVariation()
    {
        using var controlFixture = PilotBRunnerTestFixture.Create();
        using var treatmentFixture = PilotBRunnerTestFixture.Create();
        controlFixture.ReplaceManifestValue("\"arm_id\": \"treatment\"", "\"arm_id\": \"control\"");
        var runner = new PilotBRunner();
        var controlSource = await runner.RunAsync(controlFixture.CreateRequest() with { IsQualification = false });
        var treatmentSource = await runner.RunAsync(treatmentFixture.CreateRequest() with { IsQualification = false });
        var producer = new PilotBRunRecordProducer();
        var scorer = new PilotBScorer();
        var options = new PilotBScoringOptions
        {
            ExpectedPairCount = 1,
            MinimumCompletedRunsPerArm = 1,
            MaximumTreatmentRoutineMessages = 0,
            MaximumTreatmentAffectedRuns = 0,
            MinimumTreatmentObservableRate = 1m,
            MinimumRelativeReduction = 1m,
            ExpectedTreatmentSafetyRuns = 0,
            MaximumTreatmentMinorClarityExcess = 0,
            MinimumMcNemarDiscordantPairs = 1
        };
        var sourceControlState = CaptureBundleState(controlSource.Artifacts.Root);
        var sourceTreatmentState = CaptureBundleState(treatmentSource.Artifacts.Root);
        var controlARequest = CreateProductionRequest(
            controlSource.Artifacts.Root,
            [PilotBMessageKind.Routine],
            "run-a-control",
            "pair-a",
            armOrderIndex: 0,
            pairStart: PairStart);
        var treatmentARequest = CreateProductionRequest(
            treatmentSource.Artifacts.Root,
            [PilotBMessageKind.Observable],
            "run-a-treatment",
            "pair-a",
            armOrderIndex: 1,
            pairStart: PairStart);
        var controlA = ProduceRecord(producer, controlARequest);
        var treatmentA = ProduceRecord(producer, treatmentARequest);
        var firstScore = scorer.Score([controlA, treatmentA], options);
        Assert.Equal(PilotBDecision.Pass, firstScore.Decision);
        Assert.Equal(sourceControlState, CaptureBundleState(controlSource.Artifacts.Root));
        Assert.Equal(sourceTreatmentState, CaptureBundleState(treatmentSource.Artifacts.Root));

        var controlVariant = await CreateStorageVariantAsync(controlFixture, controlSource, TimeSpan.FromDays(1));
        var treatmentVariant = await CreateStorageVariantAsync(treatmentFixture, treatmentSource, TimeSpan.FromDays(2));
        Assert.Equal(controlSource.DeterministicFingerprint, controlVariant.SemanticFingerprint);
        Assert.Equal(treatmentSource.DeterministicFingerprint, treatmentVariant.SemanticFingerprint);
        Assert.NotEqual(
            PilotBSha256.ComputeFile(controlSource.Artifacts.RawOutputPath),
            PilotBSha256.ComputeFile(controlVariant.Paths.RawOutputPath));
        Assert.NotEqual(
            PilotBSha256.ComputeFile(controlSource.Artifacts.StderrPath),
            PilotBSha256.ComputeFile(controlVariant.Paths.StderrPath));
        Assert.NotEqual(
            PilotBSha256.ComputeFile(controlSource.Artifacts.ManifestPath),
            PilotBSha256.ComputeFile(controlVariant.Paths.ManifestPath));
        var variantControlState = CaptureBundleState(controlVariant.Paths.Root);
        var variantTreatmentState = CaptureBundleState(treatmentVariant.Paths.Root);
        var controlBRequest = CreateProductionRequest(
            controlVariant.Paths.Root,
            [PilotBMessageKind.Observable],
            "run-b-control",
            "pair-b",
            armOrderIndex: 0,
            pairStart: PairStart.AddHours(1));
        var treatmentBRequest = CreateProductionRequest(
            treatmentVariant.Paths.Root,
            [PilotBMessageKind.Routine],
            "run-b-treatment",
            "pair-b",
            armOrderIndex: 1,
            pairStart: PairStart.AddHours(1));
        var controlB = ProduceRecord(producer, controlBRequest);
        var treatmentB = ProduceRecord(producer, treatmentBRequest);
        var interferingScore = scorer.Score([controlB, treatmentB], options);
        Assert.Equal(PilotBDecision.Fail, interferingScore.Decision);
        Assert.Equal(variantControlState, CaptureBundleState(controlVariant.Paths.Root));
        Assert.Equal(variantTreatmentState, CaptureBundleState(treatmentVariant.Paths.Root));

        var repeatedControlVariant = await CreateStorageVariantAsync(
            controlFixture,
            controlSource,
            TimeSpan.FromDays(3));
        var repeatedTreatmentVariant = await CreateStorageVariantAsync(
            treatmentFixture,
            treatmentSource,
            TimeSpan.FromDays(4));
        Assert.Equal(controlSource.DeterministicFingerprint, repeatedControlVariant.SemanticFingerprint);
        Assert.Equal(treatmentSource.DeterministicFingerprint, repeatedTreatmentVariant.SemanticFingerprint);
        var repeatedControlState = CaptureBundleState(repeatedControlVariant.Paths.Root);
        var repeatedTreatmentState = CaptureBundleState(repeatedTreatmentVariant.Paths.Root);
        var repeatedControl = ProduceRecord(producer, CreateProductionRequest(
            repeatedControlVariant.Paths.Root,
            [PilotBMessageKind.Routine],
            "run-c-control",
            "pair-c",
            armOrderIndex: 0,
            pairStart: PairStart.AddHours(2)));
        var repeatedTreatment = ProduceRecord(producer, CreateProductionRequest(
            repeatedTreatmentVariant.Paths.Root,
            [PilotBMessageKind.Observable],
            "run-c-treatment",
            "pair-c",
            armOrderIndex: 1,
            pairStart: PairStart.AddHours(2)));
        var repeatedScore = scorer.Score([repeatedControl, repeatedTreatment], options);

        Assert.Equal(PilotBDecision.Pass, repeatedScore.Decision);
        Assert.Equal(firstScore.ToCanonicalJson(), repeatedScore.ToCanonicalJson());
        Assert.Equal(sourceControlState, CaptureBundleState(controlSource.Artifacts.Root));
        Assert.Equal(sourceTreatmentState, CaptureBundleState(treatmentSource.Artifacts.Root));
        Assert.Equal(variantControlState, CaptureBundleState(controlVariant.Paths.Root));
        Assert.Equal(variantTreatmentState, CaptureBundleState(treatmentVariant.Paths.Root));
        Assert.Equal(repeatedControlState, CaptureBundleState(repeatedControlVariant.Paths.Root));
        Assert.Equal(repeatedTreatmentState, CaptureBundleState(repeatedTreatmentVariant.Paths.Root));
    }

    private static PilotBRunRecordProductionRequest CreateProductionRequest(
        string artifactDirectory,
        IReadOnlyList<PilotBMessageKind> messageKinds,
        string runId = "run-01-treatment",
        string pairId = "pair-01",
        int armOrderIndex = 1,
        DateTimeOffset? pairStart = null)
        => new()
        {
            ArtifactDirectory = artifactDirectory,
            RunId = runId,
            PairId = pairId,
            CaseId = "case-01",
            Replica = 1,
            IsSafetyCase = false,
            SourceManifestSha256 = Hash,
            Pairing = new PilotBPairing(
                pairId,
                1,
                armOrderIndex,
                pairStart ?? PairStart,
                (pairStart ?? PairStart).AddSeconds(20)),
            MessageKinds = messageKinds,
            Adjudication = new PilotBAdjudication(
                PilotBTaskQuality.Pass,
                PilotBClarity.Pass,
                PilotBSafety.NotRated,
                MandatoryUpdateOmitted: false,
                CriticalFailure: false,
                Completed: true,
                CorpusRuntimeUnstable: false)
        };

    private static async Task<StorageVariant> CreateStorageVariantAsync(
        PilotBRunnerTestFixture fixture,
        PilotBRunnerResult source,
        TimeSpan timestampShift,
        byte[]? outputOverride = null)
    {
        var targetRoot = Path.Combine(fixture.Root, $"storage-variant-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetRoot);
        var target = PilotBEvidenceBundle.CreatePaths(targetRoot);
        var publisher = new PilotBEvidenceBundlePublisher();
        var output = outputOverride ?? Encoding.UTF8.GetBytes("""
            { "thread_id" : "storage-only-thread", "type" : "thread.started" }
            { "type" : "turn.started" }
            { "item" : { "type" : "reasoning", "text" : "different hidden bytes" }, "type" : "item.completed" }
            { "item" : { "type" : "tool_call", "name" : "different-noop" }, "type" : "item.completed" }
            { "item" : { "type" : "agent_message", "phase" : "commentary", "text" : "Verified the fixture boundary." }, "type" : "item.completed" }
            { "item" : { "type" : "agent_message", "phase" : "final", "text" : "Done." }, "type" : "item.completed" }
            { "type" : "turn.completed" }
            """);
        var manifest = Encoding.UTF8.GetBytes(
            "\r\n" + await File.ReadAllTextAsync(source.Artifacts.ManifestPath) + "\r\n");
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["output.jsonl"] = output,
            ["stderr.txt"] = Encoding.UTF8.GetBytes($"storage-only stderr {Guid.NewGuid():N}"),
            ["prompt.bin"] = await File.ReadAllBytesAsync(source.Artifacts.PromptPath),
            ["manifest.json"] = manifest,
            ["pre-manifest.json"] = await File.ReadAllBytesAsync(source.Artifacts.PreManifestPath),
            ["post-manifest.json"] = await File.ReadAllBytesAsync(source.Artifacts.PostManifestPath)
        };
        foreach (var payload in payloads)
        {
            await publisher.WriteNewBytesAsync(
                Path.Combine(targetRoot, payload.Key),
                payload.Value,
                CancellationToken.None);
        }

        if (source.IsQualification)
        {
            throw new InvalidOperationException("A scoring variant cannot be qualification evidence.");
        }

        var manifestSha = PilotBSha256.Compute(manifest);
        var sourceMetadata = PilotBEvidenceBundle.ParseMetadata(
            await File.ReadAllBytesAsync(source.Artifacts.MetadataPath));
        var metadata = sourceMetadata with
        {
            ArtifactRoot = targetRoot,
            StartedAtUtc = sourceMetadata.StartedAtUtc.Add(timestampShift),
            CompletedAtUtc = sourceMetadata.CompletedAtUtc.Add(timestampShift),
            ExpectedArmManifestSha256 = manifestSha,
            ArmManifestSha256 = manifestSha
        };
        var parsedManifest = PilotBArmManifest.Parse(manifest);
        var parsedTranscript = PilotBTranscriptParser.Parse(output);
        var preManifest = PilotBFileManifest.Parse(payloads["pre-manifest.json"]);
        var postManifest = PilotBFileManifest.Parse(payloads["post-manifest.json"]);
        var semanticFingerprint = PilotBRunFingerprintWriter.Compute(new PilotBRunFingerprintInput(
            sourceMetadata.ExecutableSha256,
            sourceMetadata.PromptSha256,
            parsedManifest,
            parsedTranscript,
            PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(preManifest),
            PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(postManifest),
            sourceMetadata.IsQualification,
            sourceMetadata.Qualification,
            sourceMetadata.ExitCode,
            sourceMetadata.TimedOut));
        await publisher.WriteNewBytesAsync(
            target.MetadataPath,
            PilotBEvidenceBundle.CreateMetadataBytes(metadata),
            CancellationToken.None);
        await publisher.PublishSealAsync(
            target,
            metadata,
            semanticFingerprint,
            CancellationToken.None);

        var verification = new PilotBEvidenceBundleVerifier().Verify(target);
        Assert.Equal(PilotBEvidenceState.Sealed, verification.EvidenceState);
        return new StorageVariant(target, verification.SemanticFingerprint!);
    }

    private static PilotBRunRecord ProduceRecord(
        PilotBRunRecordProducer producer,
        PilotBRunRecordProductionRequest request)
    {
        var result = producer.Produce(request);
        Assert.Null(result.Rejection);
        return Assert.IsType<PilotBRunRecord>(result.RunRecord);
    }

    private static IReadOnlyDictionary<string, string> CaptureBundleState(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetFileName(path),
                PilotBSha256.ComputeFile,
                StringComparer.Ordinal);

    private sealed record StorageVariant(
        PilotBArtifactPaths Paths,
        string SemanticFingerprint);
}
