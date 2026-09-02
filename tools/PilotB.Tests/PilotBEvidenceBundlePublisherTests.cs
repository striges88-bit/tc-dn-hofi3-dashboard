using CryptoIndicatorApp.PilotB;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace CryptoIndicatorApp.PilotB.Tests;

[Collection(PilotBProcessBackedRunnerCollection.Name)]
public sealed class PilotBEvidenceBundlePublisherTests
{
    [Theory]
    [InlineData("output.jsonl")]
    [InlineData("stderr.txt")]
    [InlineData("prompt.bin")]
    [InlineData("manifest.json")]
    [InlineData("pre-manifest.json")]
    [InlineData("post-manifest.json")]
    [InlineData("metadata.json")]
    public async Task WriteNewBytesAsync_WhenPayloadExists_RejectsWithoutOverwrite(string payloadName)
    {
        var root = Directory.CreateTempSubdirectory("pilot-b-publisher-test-").FullName;
        try
        {
            var path = Path.Combine(root, payloadName);
            var original = new byte[] { 1, 2, 3 };
            await File.WriteAllBytesAsync(path, original);

            var publisher = new PilotBEvidenceBundlePublisher();

            await Assert.ThrowsAnyAsync<IOException>(
                () => publisher.WriteNewBytesAsync(path, new byte[] { 4, 5, 6 }, CancellationToken.None));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("prompt.bin")]
    [InlineData("manifest.json")]
    [InlineData("pre-manifest.json")]
    [InlineData("post-manifest.json")]
    [InlineData("output.jsonl")]
    [InlineData("stderr.txt")]
    [InlineData("metadata.json")]
    [InlineData("integrity.json")]
    public async Task Runner_WhenPublicationCallFails_ReturnsUnsealedWithoutFingerprint(string failedArtifact)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var publisher = new FailingPublisher(failedArtifact);
        var runner = new PilotBRunner(publisher);

        var result = await runner.RunAsync(fixture.CreateRequest());

        Assert.True(publisher.FailureObserved);
        Assert.Equal(PilotBEvidenceState.Unsealed, result.EvidenceState);
        Assert.Null(result.RunValidity);
        Assert.Null(result.Qualification);
        Assert.Null(result.DeterministicFingerprint);
        Assert.False(result.IntegrityFacts.ArtifactComplete);
        Assert.Contains("evidence-unsealed", result.InvalidReasons);
    }

    [Fact]
    public async Task PublishSealAsync_WhenPayloadInventoryIsNotClosed_RejectsBeforeCreatingTemporarySeal()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var bundle = await PreparePublishableBundleAsync(fixture);
        await File.WriteAllTextAsync(Path.Combine(bundle.Paths.Root, "undeclared.txt"), "unexpected");
        var publisher = new PilotBEvidenceBundlePublisher();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => publisher.PublishSealAsync(
                bundle.Paths,
                bundle.Metadata,
                bundle.SemanticFingerprint,
                CancellationToken.None));

        Assert.False(File.Exists(bundle.Paths.SealPath + ".tmp"));
        Assert.False(File.Exists(bundle.Paths.SealPath));
    }

    [Fact]
    public async Task PublishSealAsync_WhenMetadataBytesDrift_RejectsBeforeCreatingTemporarySeal()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var bundle = await PreparePublishableBundleAsync(fixture);
        await File.AppendAllTextAsync(bundle.Paths.MetadataPath, " ");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new PilotBEvidenceBundlePublisher().PublishSealAsync(
                bundle.Paths,
                bundle.Metadata,
                bundle.SemanticFingerprint,
                CancellationToken.None));

        Assert.False(File.Exists(bundle.Paths.SealPath + ".tmp"));
        Assert.False(File.Exists(bundle.Paths.SealPath));
    }

    [Fact]
    public async Task PublishSealAsync_ClosesTemporarySealBeforeAtomicRenameAndVerifies()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var bundle = await PreparePublishableBundleAsync(fixture);

        await new PilotBEvidenceBundlePublisher().PublishSealAsync(
            bundle.Paths,
            bundle.Metadata,
            bundle.SemanticFingerprint,
            CancellationToken.None);

        Assert.True(File.Exists(bundle.Paths.SealPath));
        Assert.False(File.Exists(bundle.Paths.SealPath + ".tmp"));
        var verification = new PilotBEvidenceBundleVerifier().Verify(bundle.Paths);
        Assert.Equal(PilotBEvidenceState.Sealed, verification.EvidenceState);
        Assert.Equal(bundle.SemanticFingerprint, verification.SemanticFingerprint);
    }

    [Theory]
    [InlineData("integrity.json.tmp")]
    [InlineData("integrity.json")]
    public async Task PublishSealAsync_WhenReservedSealPathExists_NeverOverwritesIt(string collisionName)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var bundle = await PreparePublishableBundleAsync(fixture);
        var collisionPath = Path.Combine(bundle.Paths.Root, collisionName);
        await File.WriteAllTextAsync(collisionPath, "sentinel");

        await Assert.ThrowsAnyAsync<IOException>(
            () => new PilotBEvidenceBundlePublisher().PublishSealAsync(
                bundle.Paths,
                bundle.Metadata,
                bundle.SemanticFingerprint,
                CancellationToken.None));

        Assert.Equal("sentinel", await File.ReadAllTextAsync(collisionPath));
        if (string.Equals(collisionName, "integrity.json", StringComparison.Ordinal))
        {
            Assert.True(File.Exists(bundle.Paths.SealPath + ".tmp"));
        }
        else
        {
            Assert.False(File.Exists(bundle.Paths.SealPath));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("nested")]
    [InlineData("temporary")]
    [InlineData("lock")]
    [InlineData("renamed")]
    [InlineData("traversal")]
    [InlineData("directory")]
    [InlineData("reparse")]
    [InlineData("hardlink")]
    public async Task EvidenceVerifier_RejectsNoncanonicalFilesystemInventory(string mutation)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest());

        switch (mutation)
        {
            case "missing":
                File.Delete(result.Artifacts.StderrPath);
                break;
            case "extra":
                await File.WriteAllTextAsync(Path.Combine(result.Artifacts.Root, "extra.txt"), "extra");
                break;
            case "nested":
                Directory.CreateDirectory(Path.Combine(result.Artifacts.Root, "nested"));
                await File.WriteAllTextAsync(Path.Combine(result.Artifacts.Root, "nested", "extra.txt"), "extra");
                break;
            case "temporary":
                await File.WriteAllTextAsync(result.Artifacts.SealPath + ".tmp", "abandoned");
                break;
            case "lock":
                await File.WriteAllTextAsync(result.Artifacts.LockPath, "abandoned");
                break;
            case "renamed":
                File.Move(result.Artifacts.StderrPath, Path.Combine(result.Artifacts.Root, "renamed.txt"));
                break;
            case "traversal":
                await ReplaceInventoryPathAsync(result.Artifacts.SealPath, "stderr.txt", "../stderr.txt");
                break;
            case "directory":
                File.Delete(result.Artifacts.StderrPath);
                Directory.CreateDirectory(result.Artifacts.StderrPath);
                break;
            case "reparse":
                File.Delete(result.Artifacts.StderrPath);
                fixture.CreateExistingArtifactPath(result.Artifacts.StderrPath, "directory-reparse");
                break;
            case "hardlink":
                var hardLinkTarget = Path.Combine(fixture.Root, "linked-prompt.bin");
                File.Copy(result.Artifacts.PromptPath, hardLinkTarget);
                File.Delete(result.Artifacts.PromptPath);
                CreateHardLink(result.Artifacts.PromptPath, hardLinkTarget);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown mutation.");
        }

        AssertUnsealed(new PilotBEvidenceBundleVerifier().Verify(result.Artifacts));
    }

    [Fact]
    public async Task EvidenceVerifier_RejectsReparseArtifactRoot()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest());
        var originalRoot = result.Artifacts.Root;
        var targetRoot = Path.Combine(fixture.Root, $"artifact-target-{Guid.NewGuid():N}");
        Directory.Move(originalRoot, targetRoot);
        CreateDirectoryJunction(originalRoot, targetRoot);
        try
        {
            AssertUnsealed(new PilotBEvidenceBundleVerifier().Verify(originalRoot));
        }
        finally
        {
            Directory.Delete(originalRoot);
            Directory.Move(targetRoot, originalRoot);
        }
    }

    [Theory]
    [InlineData("seal-extra")]
    [InlineData("seal-duplicate")]
    [InlineData("metadata-extra")]
    [InlineData("metadata-duplicate")]
    public async Task EvidenceVerifier_RejectsExtraOrDuplicateSchemaProperties(string mutation)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest());

        if (mutation.StartsWith("seal-", StringComparison.Ordinal))
        {
            var seal = await File.ReadAllTextAsync(result.Artifacts.SealPath);
            var property = mutation.EndsWith("extra", StringComparison.Ordinal)
                ? "\"unexpected\":true,"
                : "\"schema_version\":\"pilot-b.integrity.v3\",";
            await File.WriteAllTextAsync(result.Artifacts.SealPath, seal.Insert(1, property));
        }
        else
        {
            var metadata = await File.ReadAllTextAsync(result.Artifacts.MetadataPath);
            var property = mutation.EndsWith("extra", StringComparison.Ordinal)
                ? "\"unexpected\":true,"
                : "\"schema_version\":\"pilot-b.runner-metadata.v3\",";
            await ReplaceMetadataAndSealInventoryAsync(
                result.Artifacts,
                metadata.Insert(1, property));
        }

        AssertUnsealed(new PilotBEvidenceBundleVerifier().Verify(result.Artifacts));
    }

    [Theory]
    [InlineData("missing-seal")]
    [InlineData("seal-only")]
    [InlineData("partial")]
    [InlineData("malformed")]
    [InlineData("unsupported")]
    [InlineData("state")]
    [InlineData("incomplete")]
    [InlineData("length")]
    [InlineData("hash")]
    public async Task EvidenceVerifier_RejectsIncompleteOrInconsistentSeal(string mutation)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest());

        switch (mutation)
        {
            case "missing-seal":
                File.Delete(result.Artifacts.SealPath);
                break;
            case "seal-only":
                foreach (var name in PilotBEvidenceBundle.PayloadNames)
                {
                    File.Delete(Path.Combine(result.Artifacts.Root, name));
                }
                break;
            case "partial":
                await File.WriteAllTextAsync(result.Artifacts.SealPath, "{");
                break;
            case "malformed":
                await File.WriteAllTextAsync(result.Artifacts.SealPath, "[]");
                break;
            case "unsupported":
                await ReplaceSealTextAsync(result.Artifacts.SealPath, "pilot-b.integrity.v3", "pilot-b.integrity.v4");
                break;
            case "state":
                await ReplaceSealTextAsync(result.Artifacts.SealPath, "\"evidence_state\":\"sealed\"", "\"evidence_state\":\"unsealed\"");
                break;
            case "incomplete":
                await ReplaceSealTextAsync(result.Artifacts.SealPath, "\"artifact_complete\":true", "\"artifact_complete\":false");
                break;
            case "length":
                await MutateInventoryEntryAsync(result.Artifacts.SealPath, entry => entry["length"] = entry["length"]!.GetValue<long>() + 1);
                break;
            case "hash":
                await MutateInventoryEntryAsync(result.Artifacts.SealPath, entry => entry["sha256"] = new string('0', 64));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown mutation.");
        }

        AssertUnsealed(new PilotBEvidenceBundleVerifier().Verify(result.Artifacts));
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("mutate")]
    public async Task EvidenceVerifier_RejectsPostSealPayloadChanges(string mutation)
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var result = await new PilotBRunner().RunAsync(fixture.CreateRequest());

        if (string.Equals(mutation, "replace", StringComparison.Ordinal))
        {
            var bytes = await File.ReadAllBytesAsync(result.Artifacts.PromptPath);
            Assert.NotEmpty(bytes);
            bytes[0] ^= 0xff;
            await File.WriteAllBytesAsync(result.Artifacts.PromptPath, bytes);
        }
        else
        {
            await File.AppendAllTextAsync(result.Artifacts.StderrPath, "tampered");
        }

        AssertUnsealed(new PilotBEvidenceBundleVerifier().Verify(result.Artifacts));
    }

    [Fact]
    public async Task Runner_WhenPublisherReturnsTamperedSeal_RemainsUnsealedWithoutFingerprint()
    {
        using var fixture = PilotBRunnerTestFixture.Create();

        var result = await new PilotBRunner(new TamperingPublisher()).RunAsync(fixture.CreateRequest());

        Assert.Equal(PilotBEvidenceState.Unsealed, result.EvidenceState);
        Assert.Null(result.RunValidity);
        Assert.Null(result.Qualification);
        Assert.Null(result.DeterministicFingerprint);
    }

    [Fact]
    public async Task PublisherVerifierRoundTrip_IsDeterministicAcrossPhysicalDirectories()
    {
        using var fixture = PilotBRunnerTestFixture.Create();
        var first = await PreparePublishableBundleAsync(fixture);
        var second = await PreparePublishableBundleAsync(fixture);
        var publisher = new PilotBEvidenceBundlePublisher();

        await publisher.PublishSealAsync(first.Paths, first.Metadata, first.SemanticFingerprint, CancellationToken.None);
        await publisher.PublishSealAsync(second.Paths, second.Metadata, second.SemanticFingerprint, CancellationToken.None);
        var firstVerification = new PilotBEvidenceBundleVerifier().Verify(first.Paths);
        var secondVerification = new PilotBEvidenceBundleVerifier().Verify(second.Paths);

        Assert.Equal(PilotBEvidenceState.Sealed, firstVerification.EvidenceState);
        Assert.Equal(PilotBEvidenceState.Sealed, secondVerification.EvidenceState);
        Assert.Equal(firstVerification.Qualification!.Validity, secondVerification.Qualification!.Validity);
        Assert.Equal(
            firstVerification.Qualification.InvalidReasons,
            secondVerification.Qualification.InvalidReasons);
        Assert.Equal(firstVerification.SemanticFingerprint, secondVerification.SemanticFingerprint);
    }

    private static async Task<PublishableBundle> PreparePublishableBundleAsync(PilotBRunnerTestFixture fixture)
    {
        var source = await new PilotBRunner().RunAsync(fixture.CreateRequest());
        var targetRoot = Path.Combine(fixture.Root, $"publisher-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetRoot);
        var targetPaths = PilotBEvidenceBundle.CreatePaths(targetRoot);

        foreach (var name in PilotBEvidenceBundle.PayloadNames.Where(name => name != "metadata.json"))
        {
            File.Copy(Path.Combine(source.Artifacts.Root, name), Path.Combine(targetRoot, name));
        }

        var metadata = PilotBEvidenceBundle.ParseMetadata(await File.ReadAllBytesAsync(source.Artifacts.MetadataPath)) with
        {
            ArtifactRoot = targetRoot
        };
        await File.WriteAllBytesAsync(targetPaths.MetadataPath, PilotBEvidenceBundle.CreateMetadataBytes(metadata));
        return new PublishableBundle(targetPaths, metadata, source.DeterministicFingerprint!);
    }

    private static async Task ReplaceInventoryPathAsync(string sealPath, string oldPath, string newPath)
    {
        var seal = JsonNode.Parse(await File.ReadAllBytesAsync(sealPath))!.AsObject();
        var entry = seal["payload_inventory"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(item => item["path"]!.GetValue<string>() == oldPath);
        entry["path"] = newPath;
        await File.WriteAllTextAsync(sealPath, seal.ToJsonString());
    }

    private static async Task ReplaceMetadataAndSealInventoryAsync(
        PilotBArtifactPaths artifacts,
        string metadataJson)
    {
        await File.WriteAllTextAsync(artifacts.MetadataPath, metadataJson);
        var metadataBytes = await File.ReadAllBytesAsync(artifacts.MetadataPath);
        var seal = JsonNode.Parse(await File.ReadAllBytesAsync(artifacts.SealPath))!.AsObject();
        var entry = seal["payload_inventory"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(item => item["path"]!.GetValue<string>() == "metadata.json");
        entry["length"] = metadataBytes.LongLength;
        entry["sha256"] = PilotBSha256.Compute(metadataBytes);
        await File.WriteAllTextAsync(artifacts.SealPath, seal.ToJsonString());
    }

    private static async Task MutateInventoryEntryAsync(string sealPath, Action<JsonObject> mutation)
    {
        var seal = JsonNode.Parse(await File.ReadAllBytesAsync(sealPath))!.AsObject();
        mutation(seal["payload_inventory"]!.AsArray()[0]!.AsObject());
        await File.WriteAllTextAsync(sealPath, seal.ToJsonString());
    }

    private static async Task ReplaceSealTextAsync(string sealPath, string oldValue, string newValue)
    {
        var original = await File.ReadAllTextAsync(sealPath);
        var replacement = original.Replace(oldValue, newValue, StringComparison.Ordinal);
        Assert.NotEqual(original, replacement);
        await File.WriteAllTextAsync(sealPath, replacement);
    }

    private static void AssertUnsealed(PilotBEvidenceVerification verification)
    {
        Assert.Equal(PilotBEvidenceState.Unsealed, verification.EvidenceState);
        Assert.Null(verification.Qualification);
        Assert.Null(verification.SemanticFingerprint);
    }

    private static void CreateHardLink(string linkPath, string targetPath)
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
        process.StartInfo.ArgumentList.Add("/H");
        process.StartInfo.ArgumentList.Add(linkPath);
        process.StartInfo.ArgumentList.Add(targetPath);
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
    }

    private sealed class FailingPublisher(string failedArtifact) : IEvidenceBundlePublisher
    {
        private readonly PilotBEvidenceBundlePublisher inner = new();

        public bool FailureObserved { get; private set; }

        public Task WriteNewBytesAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
        {
            if (string.Equals(Path.GetFileName(path), failedArtifact, StringComparison.Ordinal))
            {
                FailureObserved = true;
                throw new IOException("Injected publication failure.");
            }

            return inner.WriteNewBytesAsync(path, bytes, cancellationToken);
        }

        public Task PublishSealAsync(
            PilotBArtifactPaths paths,
            PilotBEvidenceMetadata metadata,
            string semanticFingerprint,
            CancellationToken cancellationToken)
        {
            if (string.Equals(failedArtifact, "integrity.json", StringComparison.Ordinal))
            {
                FailureObserved = true;
                throw new IOException("Injected seal publication failure.");
            }

            return inner.PublishSealAsync(paths, metadata, semanticFingerprint, cancellationToken);
        }
    }

    private sealed class TamperingPublisher : IEvidenceBundlePublisher
    {
        private readonly PilotBEvidenceBundlePublisher inner = new();

        public Task WriteNewBytesAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
            => inner.WriteNewBytesAsync(path, bytes, cancellationToken);

        public async Task PublishSealAsync(
            PilotBArtifactPaths paths,
            PilotBEvidenceMetadata metadata,
            string semanticFingerprint,
            CancellationToken cancellationToken)
        {
            await inner.PublishSealAsync(paths, metadata, semanticFingerprint, cancellationToken);
            await File.AppendAllTextAsync(paths.StderrPath, "tampered", cancellationToken);
        }
    }

    private sealed record PublishableBundle(
        PilotBArtifactPaths Paths,
        PilotBEvidenceMetadata Metadata,
        string SemanticFingerprint);
}
