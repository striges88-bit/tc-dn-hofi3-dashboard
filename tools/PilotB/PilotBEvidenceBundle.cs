using System.Globalization;
using System.Text.Json;

namespace CryptoIndicatorApp.PilotB;

internal sealed record PilotBEvidenceMetadata(
    string ExecutablePath,
    string ExpectedExecutableSha256,
    string ExpectedArmManifestSha256,
    string FixtureRoot,
    string ArtifactRoot,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<string> InvocationArguments,
    string ArmId,
    string CliVersion,
    string ModelAlias,
    string ReasoningEffort,
    string Sandbox,
    string ApprovalPolicy,
    bool ProcessStarted,
    int? ExitCode,
    bool TimedOut,
    long TimeoutTicks,
    long ElapsedTicks,
    bool TimingValid,
    bool RepositoryBoundaryValid,
    bool PromptBytesVerified,
    bool ExecutableHashValid,
    bool WorkspaceIntegrityCaptured,
    bool PayloadCaptured,
    bool IsQualification,
    string ExecutableSha256,
    string ArmManifestSha256,
    string PromptSha256,
    string PreManifestSha256,
    string PostManifestSha256,
    string PreFixtureSemanticSha256,
    string PostFixtureSemanticSha256,
    IReadOnlyList<string> AdditionalInvalidReasons,
    PilotBRunQualificationResult Qualification);

internal sealed record PilotBPayloadInventoryEntry(string Path, long Length, string Sha256);

internal sealed record PilotBSealData(
    PilotBRunValidity RunValidity,
    bool IsQualification,
    IReadOnlyList<string> InvalidReasons,
    string SemanticFingerprint,
    IReadOnlyList<PilotBPayloadInventoryEntry> PayloadInventory,
    PilotBRunnerIntegrityFacts IntegrityFacts);

internal static class PilotBEvidenceBundle
{
    public static readonly IReadOnlyList<string> PayloadNames =
    [
        "output.jsonl",
        "stderr.txt",
        "prompt.bin",
        "manifest.json",
        "pre-manifest.json",
        "post-manifest.json",
        "metadata.json"
    ];

    public static PilotBArtifactPaths CreatePaths(string root)
        => new(
            root,
            Path.Combine(root, "output.jsonl"),
            Path.Combine(root, "metadata.json"),
            Path.Combine(root, "manifest.json"),
            Path.Combine(root, "pre-manifest.json"),
            Path.Combine(root, "post-manifest.json"),
            Path.Combine(root, "integrity.json"),
            Path.Combine(root, "prompt.bin"),
            Path.Combine(root, "stderr.txt"));

    public static async Task WriteNewBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public static bool PayloadArtifactsExist(PilotBArtifactPaths paths, bool includeMetadata)
    {
        var names = includeMetadata ? PayloadNames : PayloadNames.Where(name => name != "metadata.json");
        return names.All(name => File.Exists(Path.Combine(paths.Root, name)));
    }

    public static IReadOnlyList<PilotBPayloadInventoryEntry> CapturePayloadInventory(PilotBArtifactPaths paths)
    {
        var entries = new List<PilotBPayloadInventoryEntry>(PayloadNames.Count);
        foreach (var name in PayloadNames)
        {
            var path = Path.Combine(paths.Root, name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("A required evidence payload is missing.", path);
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new FormatException("Evidence payloads cannot be linked files.");
            }

            var file = new FileInfo(path);
            entries.Add(new PilotBPayloadInventoryEntry(name, file.Length, PilotBSha256.ComputeFile(path)));
        }

        return entries;
    }

    public static byte[] CreateMetadataBytes(PilotBEvidenceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", "pilot-b.runner-metadata.v3");
            writer.WriteString("executable_path", metadata.ExecutablePath);
            writer.WriteString("expected_executable_sha256", metadata.ExpectedExecutableSha256);
            writer.WriteString("expected_arm_manifest_sha256", metadata.ExpectedArmManifestSha256);
            writer.WriteString("fixture_root", metadata.FixtureRoot);
            writer.WriteString("artifact_root", metadata.ArtifactRoot);
            writer.WriteString("started_at_utc", metadata.StartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("completed_at_utc", metadata.CompletedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            WriteStrings(writer, "invocation", metadata.InvocationArguments);
            writer.WriteString("arm_id", metadata.ArmId);
            writer.WriteString("cli_version", metadata.CliVersion);
            writer.WriteString("model_alias", metadata.ModelAlias);
            writer.WriteString("reasoning_effort", metadata.ReasoningEffort);
            writer.WriteString("sandbox", metadata.Sandbox);
            writer.WriteString("approval_policy", metadata.ApprovalPolicy);
            writer.WriteBoolean("process_started", metadata.ProcessStarted);
            if (metadata.ExitCode is int exitCode)
            {
                writer.WriteNumber("exit_code", exitCode);
            }
            else
            {
                writer.WriteNull("exit_code");
            }
            writer.WriteBoolean("timed_out", metadata.TimedOut);
            writer.WriteNumber("timeout_ticks", metadata.TimeoutTicks);
            writer.WriteNumber("elapsed_ticks", metadata.ElapsedTicks);
            writer.WriteBoolean("timing_valid", metadata.TimingValid);
            writer.WriteBoolean("repository_boundary_valid", metadata.RepositoryBoundaryValid);
            writer.WriteBoolean("prompt_bytes_verified", metadata.PromptBytesVerified);
            writer.WriteBoolean("executable_hash_valid", metadata.ExecutableHashValid);
            writer.WriteBoolean("workspace_integrity_captured", metadata.WorkspaceIntegrityCaptured);
            writer.WriteBoolean("payload_captured", metadata.PayloadCaptured);
            writer.WriteBoolean("qualification_marker", metadata.IsQualification);
            writer.WriteString("executable_sha256", metadata.ExecutableSha256);
            writer.WriteString("arm_manifest_sha256", metadata.ArmManifestSha256);
            writer.WriteString("prompt_sha256", metadata.PromptSha256);
            writer.WriteString("pre_manifest_sha256", metadata.PreManifestSha256);
            writer.WriteString("post_manifest_sha256", metadata.PostManifestSha256);
            writer.WriteString("pre_fixture_semantic_sha256", metadata.PreFixtureSemanticSha256);
            writer.WriteString("post_fixture_semantic_sha256", metadata.PostFixtureSemanticSha256);
            WriteStrings(writer, "additional_invalid_reasons", metadata.AdditionalInvalidReasons);
            writer.WriteStartObject("run_qualification");
            writer.WriteString("validity", metadata.Qualification.Validity.ToString().ToLowerInvariant());
            WriteStrings(writer, "invalid_reasons", metadata.Qualification.InvalidReasons);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }

        return stream.ToArray();
    }

    public static async Task PublishSealAsync(
        PilotBArtifactPaths paths,
        PilotBEvidenceMetadata metadata,
        string semanticFingerprint,
        CancellationToken cancellationToken)
    {
        var inventory = CapturePayloadInventory(paths);
        var temporaryPath = paths.SealPath + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         FileOptions.Asynchronous))
        {
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteString("schema_version", "pilot-b.integrity.v3");
                writer.WriteString("evidence_state", "sealed");
                writer.WriteString("run_validity", metadata.Qualification.Validity.ToString().ToLowerInvariant());
                writer.WriteBoolean("qualification_marker", metadata.IsQualification);
                WriteStrings(writer, "invalid_reasons", metadata.Qualification.InvalidReasons);
                writer.WriteBoolean("artifact_complete", true);
                writer.WriteString("semantic_fingerprint", semanticFingerprint);
                writer.WriteStartArray("payload_inventory");
                foreach (var entry in inventory)
                {
                    writer.WriteStartObject();
                    writer.WriteString("path", entry.Path);
                    writer.WriteNumber("length", entry.Length);
                    writer.WriteString("sha256", entry.Sha256);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteStartObject("integrity_facts");
                writer.WriteString("executable_sha256", metadata.ExecutableSha256);
                writer.WriteString("arm_manifest_sha256", metadata.ArmManifestSha256);
                writer.WriteString("prompt_sha256", metadata.PromptSha256);
                writer.WriteString("pre_manifest_sha256", metadata.PreManifestSha256);
                writer.WriteString("post_manifest_sha256", metadata.PostManifestSha256);
                writer.WriteBoolean("repository_boundary_valid", metadata.RepositoryBoundaryValid);
                writer.WriteBoolean("artifact_complete", true);
                writer.WriteBoolean("timing_valid", metadata.TimingValid);
                writer.WriteBoolean("auth_lane_excluded", true);
                writer.WriteBoolean("workspace_integrity_captured", metadata.WorkspaceIntegrityCaptured);
                writer.WriteEndObject();
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken);
            }

            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, paths.SealPath, overwrite: false);
    }

    public static PilotBEvidenceMetadata ParseMetadata(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());
        var root = document.RootElement;
        Require(root, "schema_version", "pilot-b.runner-metadata.v3");
        var qualification = RequiredObject(root, "run_qualification");
        return new PilotBEvidenceMetadata(
            RequiredString(root, "executable_path"),
            RequiredString(root, "expected_executable_sha256"),
            RequiredString(root, "expected_arm_manifest_sha256"),
            RequiredString(root, "fixture_root"),
            RequiredString(root, "artifact_root"),
            RequiredDateTime(root, "started_at_utc"),
            RequiredDateTime(root, "completed_at_utc"),
            RequiredStrings(root, "invocation"),
            RequiredString(root, "arm_id"),
            RequiredString(root, "cli_version"),
            RequiredString(root, "model_alias"),
            RequiredString(root, "reasoning_effort"),
            RequiredString(root, "sandbox"),
            RequiredString(root, "approval_policy"),
            RequiredBool(root, "process_started"),
            RequiredNullableInt(root, "exit_code"),
            RequiredBool(root, "timed_out"),
            RequiredLong(root, "timeout_ticks"),
            RequiredLong(root, "elapsed_ticks"),
            RequiredBool(root, "timing_valid"),
            RequiredBool(root, "repository_boundary_valid"),
            RequiredBool(root, "prompt_bytes_verified"),
            RequiredBool(root, "executable_hash_valid"),
            RequiredBool(root, "workspace_integrity_captured"),
            RequiredBool(root, "payload_captured"),
            RequiredBool(root, "qualification_marker"),
            RequiredString(root, "executable_sha256"),
            RequiredString(root, "arm_manifest_sha256"),
            RequiredString(root, "prompt_sha256"),
            RequiredString(root, "pre_manifest_sha256"),
            RequiredString(root, "post_manifest_sha256"),
            RequiredString(root, "pre_fixture_semantic_sha256"),
            RequiredString(root, "post_fixture_semantic_sha256"),
            RequiredStrings(root, "additional_invalid_reasons"),
            new PilotBRunQualificationResult(
                ParseValidity(RequiredString(qualification, "validity")),
                RequiredStrings(qualification, "invalid_reasons")));
    }

    public static PilotBSealData ParseSeal(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());
        var root = document.RootElement;
        Require(root, "schema_version", "pilot-b.integrity.v3");
        var inventory = RequiredArray(root, "payload_inventory").EnumerateArray().Select(entry => new PilotBPayloadInventoryEntry(
            RequiredString(entry, "path"),
            RequiredLong(entry, "length"),
            RequiredString(entry, "sha256"))).ToArray();
        var integrityFacts = RequiredObject(root, "integrity_facts");
        return new PilotBSealData(
            ParseValidity(RequiredString(root, "run_validity")),
            RequiredBool(root, "qualification_marker"),
            RequiredStrings(root, "invalid_reasons"),
            RequiredString(root, "semantic_fingerprint"),
            inventory,
            new PilotBRunnerIntegrityFacts(
                RequiredString(integrityFacts, "executable_sha256"),
                RequiredString(integrityFacts, "arm_manifest_sha256"),
                RequiredString(integrityFacts, "prompt_sha256"),
                RequiredString(integrityFacts, "pre_manifest_sha256"),
                RequiredString(integrityFacts, "post_manifest_sha256"),
                RequiredBool(integrityFacts, "repository_boundary_valid"),
                RequiredBool(integrityFacts, "artifact_complete"),
                RequiredBool(integrityFacts, "timing_valid"),
                RequiredBool(integrityFacts, "auth_lane_excluded"),
                RequiredBool(integrityFacts, "workspace_integrity_captured")));
    }

    private static void WriteStrings(Utf8JsonWriter writer, string propertyName, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }

    private static JsonElement RequiredObject(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Required object '{name}' is missing.");
        }

        return value;
    }

    private static JsonElement RequiredArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Required array '{name}' is missing.");
        }

        return value;
    }

    private static IReadOnlyList<string> RequiredStrings(JsonElement root, string name)
        => RequiredArray(root, name).EnumerateArray().Select(value =>
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new FormatException($"Array '{name}' requires non-empty strings.");
            }

            return value.GetString()!;
        }).ToArray();

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Required string '{name}' is missing.");
        }

        return value.GetString()!;
    }

    private static bool RequiredBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            throw new FormatException($"Required boolean '{name}' is missing.");
        }

        return value.GetBoolean();
    }

    private static int? RequiredNullableInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw new FormatException($"Required integer '{name}' is missing.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!value.TryGetInt32(out var parsed))
        {
            throw new FormatException($"Required integer '{name}' is invalid.");
        }

        return parsed;
    }

    private static DateTimeOffset RequiredDateTime(JsonElement root, string name)
    {
        var value = RequiredString(root, name);
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new FormatException($"Required date-time '{name}' is invalid.");
        }

        return parsed;
    }

    private static long RequiredLong(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt64(out var parsed) || parsed < 0)
        {
            throw new FormatException($"Required non-negative integer '{name}' is missing.");
        }

        return parsed;
    }

    private static PilotBRunValidity ParseValidity(string value)
    {
        if (!Enum.TryParse<PilotBRunValidity>(value, ignoreCase: true, out var parsed))
        {
            throw new FormatException("Run validity is invalid.");
        }

        return parsed;
    }

    private static void Require(JsonElement root, string name, string expected)
    {
        if (!string.Equals(RequiredString(root, name), expected, StringComparison.Ordinal))
        {
            throw new FormatException($"'{name}' does not match the expected schema.");
        }
    }
}

public sealed class PilotBEvidenceBundleVerifier
{
    public PilotBEvidenceVerification Verify(PilotBArtifactPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Verify(paths.Root);
    }

    public PilotBEvidenceVerification Verify(string artifactRoot)
    {
        try
        {
            var root = Path.GetFullPath(artifactRoot);
            var paths = PilotBEvidenceBundle.CreatePaths(root);
            VerifyFilesystemInventory(paths);

            var seal = PilotBEvidenceBundle.ParseSeal(File.ReadAllBytes(paths.SealPath));
            Require(string.Equals(ReadString(File.ReadAllBytes(paths.SealPath), "evidence_state"), "sealed", StringComparison.Ordinal), "seal-state-invalid");
            Require(ReadBool(File.ReadAllBytes(paths.SealPath), "artifact_complete"), "seal-artifact-incomplete");
            Require(PilotBSha256.IsSha256(seal.SemanticFingerprint), "seal-fingerprint-invalid");

            var inventory = PilotBEvidenceBundle.CapturePayloadInventory(paths);
            Require(InventoryMatches(inventory, seal.PayloadInventory), "payload-inventory-mismatch");

            var metadata = PilotBEvidenceBundle.ParseMetadata(File.ReadAllBytes(paths.MetadataPath));
            var manifestBytes = File.ReadAllBytes(paths.ManifestPath);
            var manifestSha = PilotBSha256.Compute(manifestBytes);
            var manifest = PilotBArmManifest.Parse(manifestBytes);
            var transcript = PilotBTranscriptParser.Parse(File.ReadAllBytes(paths.RawOutputPath));
            var preManifest = PilotBFileManifest.Parse(File.ReadAllBytes(paths.PreManifestPath));
            var postManifest = PilotBFileManifest.Parse(File.ReadAllBytes(paths.PostManifestPath));
            var executableSha = PilotBSha256.ComputeFile(metadata.ExecutablePath);
            var promptSha = PilotBSha256.ComputeFile(paths.PromptPath);

            var executableHashValid = string.Equals(executableSha, metadata.ExecutableSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(executableSha, metadata.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase);
            var promptBytesVerified = string.Equals(promptSha, metadata.PromptSha256, StringComparison.OrdinalIgnoreCase);
            var repositoryBoundaryValid = string.Equals(Path.GetFullPath(metadata.ArtifactRoot), root, StringComparison.OrdinalIgnoreCase)
                && PilotBGitBoundary.IsExactRepositoryRoot(metadata.FixtureRoot)
                && string.Equals(Path.GetFullPath(manifest.RepositoryRoot), Path.GetFullPath(metadata.FixtureRoot), StringComparison.OrdinalIgnoreCase)
                && !PilotBFileManifest.IsWithin(metadata.FixtureRoot, metadata.ExecutablePath)
                && !PilotBFileManifest.IsWithin(metadata.FixtureRoot, root);
            var workspaceIntegrityCaptured = string.Equals(preManifest.Sha256, metadata.PreManifestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(postManifest.Sha256, metadata.PostManifestSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(preManifest), metadata.PreFixtureSemanticSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(postManifest), metadata.PostFixtureSemanticSha256, StringComparison.OrdinalIgnoreCase);
            var timingValid = !metadata.TimedOut
                && metadata.TimeoutTicks > 0
                && metadata.ElapsedTicks >= 0
                && metadata.ElapsedTicks <= metadata.TimeoutTicks;

            Require(string.Equals(manifestSha, metadata.ArmManifestSha256, StringComparison.OrdinalIgnoreCase), "manifest-hash-drift");
            Require(string.Equals(manifestSha, metadata.ExpectedArmManifestSha256, StringComparison.OrdinalIgnoreCase), "manifest-expected-hash-drift");
            Require(string.Equals(manifest.ArmId, metadata.ArmId, StringComparison.Ordinal), "metadata-arm-mismatch");
            Require(string.Equals(manifest.CliVersion, metadata.CliVersion, StringComparison.Ordinal), "metadata-cli-mismatch");
            Require(string.Equals(manifest.ModelAlias, metadata.ModelAlias, StringComparison.Ordinal), "metadata-model-mismatch");
            Require(metadata.InvocationArguments.SequenceEqual(["codex", "exec", "--ephemeral", "--json"], StringComparer.Ordinal), "metadata-invocation-mismatch");
            Require(metadata.ExecutableHashValid == executableHashValid, "metadata-executable-fact-mismatch");
            Require(metadata.PromptBytesVerified == promptBytesVerified, "metadata-prompt-fact-mismatch");
            Require(metadata.RepositoryBoundaryValid == repositoryBoundaryValid, "metadata-boundary-fact-mismatch");
            Require(metadata.WorkspaceIntegrityCaptured == workspaceIntegrityCaptured, "metadata-workspace-fact-mismatch");
            Require(metadata.TimingValid == timingValid, "metadata-timing-fact-mismatch");
            Require(metadata.PayloadCaptured && PilotBEvidenceBundle.PayloadArtifactsExist(paths, includeMetadata: true), "metadata-payload-fact-mismatch");
            var expectedIntegrityFacts = new PilotBRunnerIntegrityFacts(
                executableSha,
                manifestSha,
                promptSha,
                preManifest.Sha256,
                postManifest.Sha256,
                repositoryBoundaryValid,
                ArtifactComplete: true,
                timingValid,
                AuthLaneExcluded: true,
                WorkspaceIntegrityCaptured: workspaceIntegrityCaptured);
            Require(seal.IntegrityFacts == expectedIntegrityFacts, "seal-integrity-facts-mismatch");

            var qualification = PilotBRunQualification.Evaluate(new PilotBRunQualificationFacts(
                metadata.ProcessStarted,
                metadata.ExitCode,
                metadata.TimedOut,
                transcript,
                timingValid,
                executableHashValid,
                repositoryBoundaryValid,
                promptBytesVerified,
                workspaceIntegrityCaptured,
                metadata.PayloadCaptured,
                metadata.AdditionalInvalidReasons));
            Require(QualificationMatches(qualification, metadata.Qualification), "metadata-qualification-mismatch");
            Require(qualification.Validity == seal.RunValidity, "seal-validity-mismatch");
            Require(metadata.IsQualification == seal.IsQualification, "seal-qualification-marker-mismatch");
            Require(qualification.InvalidReasons.SequenceEqual(seal.InvalidReasons, StringComparer.Ordinal), "seal-reasons-mismatch");

            var fingerprint = PilotBRunFingerprintWriter.Compute(new PilotBRunFingerprintInput(
                executableSha,
                promptSha,
                manifest,
                transcript,
                PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(preManifest),
                PilotBRunFingerprintWriter.ComputeFixtureSemanticSha256(postManifest),
                metadata.IsQualification,
                qualification,
                metadata.ExitCode,
                metadata.TimedOut));
            Require(string.Equals(fingerprint, seal.SemanticFingerprint, StringComparison.Ordinal), "semantic-fingerprint-mismatch");

            return new PilotBEvidenceVerification(PilotBEvidenceState.Sealed, qualification, fingerprint, []);
        }
        catch (PilotBEvidenceVerificationException exception)
        {
            return new PilotBEvidenceVerification(PilotBEvidenceState.Unsealed, null, null, [exception.Reason]);
        }
        catch
        {
            return new PilotBEvidenceVerification(PilotBEvidenceState.Unsealed, null, null, ["evidence-verification-failed"]);
        }
    }

    private static void VerifyFilesystemInventory(PilotBArtifactPaths paths)
    {
        if (!Directory.Exists(paths.Root))
        {
            throw new PilotBEvidenceVerificationException("artifact-directory-missing");
        }

        var expected = PilotBEvidenceBundle.PayloadNames.Append(Path.GetFileName(paths.SealPath)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var actual = Directory.EnumerateFileSystemEntries(paths.Root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Require(actual.SequenceEqual(expected, StringComparer.Ordinal), "artifact-inventory-not-closed");

        foreach (var name in expected)
        {
            var path = Path.Combine(paths.Root, name);
            Require(File.Exists(path), "artifact-file-missing");
            Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "artifact-reparse-point");
        }
    }

    private static bool InventoryMatches(
        IReadOnlyList<PilotBPayloadInventoryEntry> actual,
        IReadOnlyList<PilotBPayloadInventoryEntry> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var index = 0; index < actual.Count; index++)
        {
            if (!string.Equals(actual[index].Path, expected[index].Path, StringComparison.Ordinal)
                || actual[index].Length != expected[index].Length
                || !string.Equals(actual[index].Sha256, expected[index].Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool QualificationMatches(PilotBRunQualificationResult left, PilotBRunQualificationResult right)
        => left.Validity == right.Validity
           && left.InvalidReasons.SequenceEqual(right.InvalidReasons, StringComparer.Ordinal);

    private static string ReadString(byte[] json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).GetString()
            ?? throw new PilotBEvidenceVerificationException("seal-property-invalid");
    }

    private static bool ReadBool(byte[] json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(name).GetBoolean();
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition)
        {
            throw new PilotBEvidenceVerificationException(reason);
        }
    }

    private sealed class PilotBEvidenceVerificationException(string reason) : Exception
    {
        public string Reason { get; } = reason;
    }
}
