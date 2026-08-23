using System.Text.Json;

namespace CryptoIndicatorApp.PilotB;

public sealed record PilotBArmManifest(
    string ManifestId,
    string ArmId,
    string CliVersion,
    string ProtocolSha256,
    string ModelAlias,
    string ReasoningEffort,
    string Sandbox,
    string ApprovalPolicy,
    string RepositoryRoot,
    string GlobalInstructionsSha256,
    string ProjectInstructionsSha256,
    string SkillsManifestSha256,
    bool MutableAuthenticationLanePresent)
{
    public const string SchemaVersion = "pilot-b.arm-manifest.v3";

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "schema_version",
        "manifest_id",
        "arm_id",
        "cli_version",
        "protocol_sha256",
        "model_alias",
        "reasoning_effort",
        "sandbox",
        "approval_policy",
        "repository_root",
        "global_instructions_sha256",
        "project_instructions_sha256",
        "skills_manifest_sha256",
        "mutable_authentication_lane"
    };

    public static PilotBArmManifest Parse(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Arm manifest must be a JSON object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!AllowedProperties.Contains(property.Name))
            {
                throw new FormatException($"Arm manifest contains unsupported field '{property.Name}'.");
            }
        }

        var schemaVersion = RequiredString(root, "schema_version");
        if (!string.Equals(schemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new FormatException("Arm manifest schema version is not v3.");
        }

        var manifest = new PilotBArmManifest(
            RequiredString(root, "manifest_id"),
            RequiredString(root, "arm_id"),
            RequiredString(root, "cli_version"),
            RequiredString(root, "protocol_sha256"),
            RequiredString(root, "model_alias"),
            RequiredString(root, "reasoning_effort"),
            RequiredString(root, "sandbox"),
            RequiredString(root, "approval_policy"),
            RequiredString(root, "repository_root"),
            RequiredString(root, "global_instructions_sha256"),
            RequiredString(root, "project_instructions_sha256"),
            RequiredString(root, "skills_manifest_sha256"),
            root.TryGetProperty("mutable_authentication_lane", out var mutableLane)
                && mutableLane.ValueKind == JsonValueKind.String);

        if (string.IsNullOrWhiteSpace(manifest.ManifestId)
            || string.IsNullOrWhiteSpace(manifest.ArmId)
            || string.IsNullOrWhiteSpace(manifest.CliVersion)
            || string.IsNullOrWhiteSpace(manifest.ModelAlias)
            || string.IsNullOrWhiteSpace(manifest.ReasoningEffort)
            || string.IsNullOrWhiteSpace(manifest.Sandbox)
            || string.IsNullOrWhiteSpace(manifest.ApprovalPolicy)
            || !PilotBSha256.IsSha256(manifest.ProtocolSha256)
            || !PilotBSha256.IsSha256(manifest.GlobalInstructionsSha256)
            || !PilotBSha256.IsSha256(manifest.ProjectInstructionsSha256)
            || !PilotBSha256.IsSha256(manifest.SkillsManifestSha256))
        {
            throw new FormatException("Arm manifest contains missing or invalid required evidence.");
        }

        if (root.TryGetProperty("mutable_authentication_lane", out var mutableLaneValue)
            && mutableLaneValue.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("Mutable authentication lane must be a non-secret string marker.");
        }

        return manifest;
    }

    public string ToSanitizedJson()
    {
        return JsonSerializer.Serialize(new
        {
            schema_version = SchemaVersion,
            manifest_id = ManifestId,
            arm_id = ArmId,
            cli_version = CliVersion,
            protocol_sha256 = ProtocolSha256,
            model_alias = ModelAlias,
            reasoning_effort = ReasoningEffort,
            sandbox = Sandbox,
            approval_policy = ApprovalPolicy,
            repository_root = RepositoryRoot,
            global_instructions_sha256 = GlobalInstructionsSha256,
            project_instructions_sha256 = ProjectInstructionsSha256,
            skills_manifest_sha256 = SkillsManifestSha256,
            mutable_authentication_lane_excluded = true
        });
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Arm manifest requires string field '{name}'.");
        }

        return value.GetString()!;
    }
}
