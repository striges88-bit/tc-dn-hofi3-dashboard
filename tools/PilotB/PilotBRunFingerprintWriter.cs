using System.Text.Json;

namespace CryptoIndicatorApp.PilotB;

public sealed record PilotBRunFingerprintInput(
    string ExecutableSha256,
    string PromptSha256,
    PilotBArmManifest Manifest,
    PilotBTranscriptParseResult Transcript,
    string PreFixtureSemanticSha256,
    string PostFixtureSemanticSha256,
    bool IsQualification,
    PilotBRunQualificationResult Qualification,
    int? ExitCode,
    bool TimedOut);

public static class PilotBRunFingerprintWriter
{
    public const string SchemaVersion = "pilot-b.run-fingerprint.v3";
    private const string FixtureSchemaVersion = "pilot-b.fixture-semantic.v3";

    public static string Compute(PilotBRunFingerprintInput input)
        => PilotBSha256.Compute(Write(input));

    public static byte[] Write(PilotBRunFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Manifest);
        ArgumentNullException.ThrowIfNull(input.Transcript);
        ArgumentNullException.ThrowIfNull(input.Qualification);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", SchemaVersion);
            writer.WriteString("executable_sha256", CanonicalSha(input.ExecutableSha256));
            writer.WriteString("prompt_sha256", CanonicalSha(input.PromptSha256));
            WriteSemanticManifest(writer, input.Manifest);
            WriteSemanticTranscript(writer, input.Transcript);
            writer.WriteString("pre_fixture_semantic_sha256", CanonicalSha(input.PreFixtureSemanticSha256));
            writer.WriteString("post_fixture_semantic_sha256", CanonicalSha(input.PostFixtureSemanticSha256));
            writer.WriteBoolean("qualification_marker", input.IsQualification);
            writer.WriteString("run_validity", input.Qualification.Validity.ToString().ToLowerInvariant());
            writer.WriteStartArray("invalid_reasons");
            foreach (var reason in input.Qualification.InvalidReasons)
            {
                writer.WriteStringValue(RequireText(reason, "Invalid reason"));
            }
            writer.WriteEndArray();
            if (input.ExitCode is int exitCode)
            {
                writer.WriteNumber("exit_code", exitCode);
            }
            else
            {
                writer.WriteNull("exit_code");
            }
            writer.WriteBoolean("timed_out", input.TimedOut);
            writer.WriteEndObject();
            writer.Flush();
        }

        return stream.ToArray();
    }

    public static string ComputeFixtureSemanticSha256(PilotBFileManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return ComputeFixtureSemanticSha256(manifest.Files);
    }

    public static string ComputeFixtureSemanticSha256(IReadOnlyList<PilotBFileManifestEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", FixtureSchemaVersion);
            writer.WriteStartArray("files");
            foreach (var entry in files.OrderBy(entry => NormalizeRelativePath(entry.RelativePath), StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", NormalizeRelativePath(entry.RelativePath));
                writer.WriteNumber("length", entry.Length);
                writer.WriteString("sha256", CanonicalSha(entry.Sha256));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return PilotBSha256.Compute(stream.ToArray());
    }

    private static void WriteSemanticManifest(Utf8JsonWriter writer, PilotBArmManifest manifest)
    {
        writer.WriteStartObject("semantic_arm_manifest");
        writer.WriteString("projection_version", "pilot-b.semantic-arm-manifest.v3");
        writer.WriteString("arm_id", RequireText(manifest.ArmId, "Arm id").ToLowerInvariant());
        writer.WriteString("cli_version", RequireText(manifest.CliVersion, "CLI version"));
        writer.WriteString("protocol_sha256", CanonicalSha(manifest.ProtocolSha256));
        writer.WriteString("model_alias", RequireText(manifest.ModelAlias, "Model alias"));
        writer.WriteString("reasoning_effort", RequireText(manifest.ReasoningEffort, "Reasoning effort"));
        writer.WriteString("sandbox", RequireText(manifest.Sandbox, "Sandbox"));
        writer.WriteString("approval_policy", RequireText(manifest.ApprovalPolicy, "Approval policy"));
        writer.WriteString("global_instructions_sha256", CanonicalSha(manifest.GlobalInstructionsSha256));
        writer.WriteString("project_instructions_sha256", CanonicalSha(manifest.ProjectInstructionsSha256));
        writer.WriteString("skills_manifest_sha256", CanonicalSha(manifest.SkillsManifestSha256));
        writer.WriteEndObject();
    }

    private static void WriteSemanticTranscript(Utf8JsonWriter writer, PilotBTranscriptParseResult transcript)
    {
        writer.WriteStartObject("semantic_transcript");
        writer.WriteString("projection_version", "pilot-b.semantic-transcript.v3");
        writer.WriteStartArray("messages");
        foreach (var message in transcript.SemanticMessages)
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", message.Sequence);
            writer.WriteString("text", RequireText(message.Text, "Semantic message text"));
            writer.WriteString("phase", RequireText(message.Phase, "Semantic message phase"));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteString("terminal_outcome", transcript.TerminalOutcome switch
        {
            PilotBTranscriptTerminalOutcome.FatalError => "fatal_error",
            _ => transcript.TerminalOutcome.ToString().ToLowerInvariant()
        });
        writer.WriteBoolean("valid", transcript.IsValid);
        writer.WriteStartArray("invalid_reasons");
        foreach (var reason in transcript.InvalidReasons)
        {
            writer.WriteStringValue(RequireText(reason, "Transcript invalid reason"));
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string CanonicalSha(string value)
    {
        if (!PilotBSha256.IsSha256(value))
        {
            throw new InvalidOperationException("A canonical SHA-256 value is required.");
        }

        return value.ToLowerInvariant();
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = RequireText(value, "Relative path").Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidOperationException("A canonical relative path is required.");
        }

        return normalized;
    }

    private static string RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value;
    }
}
