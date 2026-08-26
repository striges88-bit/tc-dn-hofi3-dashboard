using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CryptoIndicatorApp.PilotB;

internal interface IEvidenceBundlePublisher
{
    Task WriteNewBytesAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken);

    Task PublishSealAsync(
        PilotBArtifactPaths paths,
        PilotBEvidenceMetadata metadata,
        string semanticFingerprint,
        CancellationToken cancellationToken);
}

internal sealed class PilotBEvidenceBundlePublisher : IEvidenceBundlePublisher
{
    public async Task WriteNewBytesAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
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

    public async Task PublishSealAsync(
        PilotBArtifactPaths paths,
        PilotBEvidenceMetadata metadata,
        string semanticFingerprint,
        CancellationToken cancellationToken)
    {
        ValidatePayloadInventory(paths);
        var payloads = PilotBEvidenceBundle.CapturePayloadSnapshots(paths);
        ValidateMetadata(payloads["metadata.json"], metadata);
        var inventory = PilotBEvidenceBundle.CapturePayloadInventory(payloads);
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

    private static void ValidateMetadata(byte[] actual, PilotBEvidenceMetadata metadata)
    {
        try
        {
            _ = PilotBEvidenceBundle.ParseMetadata(actual);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw new InvalidDataException("The evidence metadata does not match the supported schema.", exception);
        }

        var expected = PilotBEvidenceBundle.CreateMetadataBytes(metadata);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException("The evidence metadata does not match the publication input.");
        }
    }

    private static void ValidatePayloadInventory(PilotBArtifactPaths paths)
    {
        var expected = PilotBEvidenceBundle.PayloadNames.OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (File.Exists(paths.LockPath))
        {
            if ((File.GetAttributes(paths.LockPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("The evidence ownership lock must be a regular file.");
            }

            expected.Add(Path.GetFileName(paths.LockPath));
            expected.Sort(StringComparer.Ordinal);
        }

        var actual = Directory.EnumerateFileSystemEntries(paths.Root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Where(name => !string.Equals(name, Path.GetFileName(paths.SealPath), StringComparison.Ordinal)
                           && !string.Equals(name, Path.GetFileName(paths.SealPath) + ".tmp", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The evidence payload inventory is not closed.");
        }
    }

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }
}

internal static class PilotBEvidenceFileIdentity
{
    public static bool HasMultipleHardLinks(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out var information))
        {
            throw new IOException(
                "Cannot inspect evidence file identity.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }

        return information.NumberOfLinks > 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FILETIME CreationTime;
        public FILETIME LastAccessTime;
        public FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}

internal static class PilotBEvidenceSchema
{
    private static readonly string[] MetadataPropertyNames =
    [
        "schema_version",
        "executable_path",
        "expected_executable_sha256",
        "expected_arm_manifest_sha256",
        "fixture_root",
        "artifact_root",
        "started_at_utc",
        "completed_at_utc",
        "invocation",
        "arm_id",
        "cli_version",
        "model_alias",
        "reasoning_effort",
        "sandbox",
        "approval_policy",
        "process_started",
        "exit_code",
        "timed_out",
        "timeout_ticks",
        "elapsed_ticks",
        "timing_valid",
        "repository_boundary_valid",
        "prompt_bytes_verified",
        "executable_hash_valid",
        "workspace_integrity_captured",
        "payload_captured",
        "qualification_marker",
        "executable_sha256",
        "arm_manifest_sha256",
        "prompt_sha256",
        "pre_manifest_sha256",
        "post_manifest_sha256",
        "pre_fixture_semantic_sha256",
        "post_fixture_semantic_sha256",
        "additional_invalid_reasons",
        "run_qualification"
    ];

    private static readonly string[] SealPropertyNames =
    [
        "schema_version",
        "evidence_state",
        "run_validity",
        "qualification_marker",
        "invalid_reasons",
        "artifact_complete",
        "semantic_fingerprint",
        "payload_inventory",
        "integrity_facts"
    ];

    private static readonly string[] InventoryEntryPropertyNames = ["path", "length", "sha256"];
    private static readonly string[] QualificationPropertyNames = ["validity", "invalid_reasons"];
    private static readonly string[] IntegrityFactPropertyNames =
    [
        "executable_sha256",
        "arm_manifest_sha256",
        "prompt_sha256",
        "pre_manifest_sha256",
        "post_manifest_sha256",
        "repository_boundary_valid",
        "artifact_complete",
        "timing_valid",
        "auth_lane_excluded",
        "workspace_integrity_captured"
    ];

    public static void RequireMetadata(JsonElement root)
        => RequireExactProperties(root, MetadataPropertyNames);

    public static void RequireSeal(JsonElement root)
        => RequireExactProperties(root, SealPropertyNames);

    public static void RequireInventoryEntry(JsonElement root)
        => RequireExactProperties(root, InventoryEntryPropertyNames);

    public static void RequireQualification(JsonElement root)
        => RequireExactProperties(root, QualificationPropertyNames);

    public static void RequireIntegrityFacts(JsonElement root)
        => RequireExactProperties(root, IntegrityFactPropertyNames);

    private static void RequireExactProperties(JsonElement root, IReadOnlyCollection<string> expected)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A required JSON object is invalid.");
        }

        var actual = root.EnumerateObject().Select(property => property.Name).ToArray();
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        if (actual.Length != expectedSet.Count || actual.Any(name => !expectedSet.Contains(name)))
        {
            throw new FormatException("The JSON object properties do not match the supported schema.");
        }
    }
}
