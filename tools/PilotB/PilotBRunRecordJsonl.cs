using System.Globalization;
using System.Text.Json;

namespace CryptoIndicatorApp.PilotB;

public static class PilotBRunRecordJsonl
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string Serialize(PilotBRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var json = JsonSerializer.Serialize(new
        {
            schema_version = record.SchemaVersion,
            record_type = "run_record",
            run_id = record.RunId,
            pair_id = record.PairId,
            case_id = record.CaseId,
            arm = ArmToString(record.Arm),
            replica = record.Replica,
            is_safety_case = record.IsSafetyCase,
            started_at_utc = record.StartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            completed_at_utc = record.CompletedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            protocol_sha256 = record.ProtocolSha256,
            source_manifest_sha256 = record.SourceManifestSha256,
            executable_sha256 = record.ExecutableSha256,
            prompt_sha256 = record.PromptSha256,
            pairing = new
            {
                pair_id = record.Pairing.PairId,
                pair_ordinal = record.Pairing.PairOrdinal,
                arm_order_index = record.Pairing.ArmOrderIndex,
                pair_started_at_utc = record.Pairing.PairStartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                pair_completed_at_utc = record.Pairing.PairCompletedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            },
            validity = ValidityToString(record.Validity),
            invalid_reasons = record.InvalidReasons,
            messages = record.Messages.Select(message => new
            {
                sequence = message.Sequence,
                text = message.Text,
                kind = MessageKindToString(message.Kind),
                source_event_type = message.SourceEventType,
                phase = message.Phase
            }),
            adjudication = new
            {
                task_quality = record.Adjudication.TaskQuality.ToString().ToLowerInvariant(),
                clarity = record.Adjudication.Clarity.ToString().ToLowerInvariant(),
                safety = record.Adjudication.Safety.ToString().ToLowerInvariant(),
                mandatory_update_omitted = record.Adjudication.MandatoryUpdateOmitted,
                critical_failure = record.Adjudication.CriticalFailure,
                completed = record.Adjudication.Completed,
                corpus_runtime_unstable = record.Adjudication.CorpusRuntimeUnstable
            },
            integrity = new
            {
                artifact_complete = record.Integrity.ArtifactComplete,
                repository_boundary_valid = record.Integrity.RepositoryBoundaryValid,
                prompt_bytes_verified = record.Integrity.PromptBytesVerified,
                timing_valid = record.Integrity.TimingValid,
                auth_lane_excluded = record.Integrity.AuthLaneExcluded,
                workspace_integrity_captured = record.Integrity.WorkspaceIntegrityCaptured
            }
        }, JsonOptions);

        return json + "\n";
    }

    public static PilotBRunRecord ParseSingle(string jsonl)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        var lines = jsonl.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
        {
            throw new FormatException("A single run-record JSONL value is required.");
        }

        try
        {
            using var document = JsonDocument.Parse(lines[0]);
            return ParseElement(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The run-record JSONL value is malformed.", exception);
        }
    }

    public static IReadOnlyList<PilotBRunRecord> ParseMany(string jsonl)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        var records = new List<PilotBRunRecord>();
        using var reader = new StringReader(jsonl);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new FormatException("Blank lines are not valid in run-record JSONL.");
            }

            records.Add(ParseSingle(line));
        }

        return records;
    }

    private static PilotBRunRecord ParseElement(JsonElement root)
    {
        Require(root, "schema_version", PilotBContractVersions.RunRecord);
        Require(root, "record_type", "run_record");

        var pairingElement = RequiredObject(root, "pairing");
        var adjudicationElement = RequiredObject(root, "adjudication");
        var integrityElement = RequiredObject(root, "integrity");
        var messagesElement = RequiredArray(root, "messages");

        var messages = messagesElement.EnumerateArray().Select(message => new PilotBMessage(
            RequiredInt(message, "sequence"),
            RequiredString(message, "text"),
            ParseMessageKind(RequiredString(message, "kind")),
            RequiredString(message, "source_event_type"),
            RequiredString(message, "phase"))).ToArray();

        return new PilotBRunRecord(
            RequiredString(root, "run_id"),
            RequiredString(root, "pair_id"),
            RequiredString(root, "case_id"),
            ParseArm(RequiredString(root, "arm")),
            RequiredInt(root, "replica"),
            RequiredBool(root, "is_safety_case"),
            RequiredDateTime(root, "started_at_utc"),
            RequiredDateTime(root, "completed_at_utc"),
            RequiredString(root, "protocol_sha256"),
            RequiredString(root, "source_manifest_sha256"),
            RequiredString(root, "executable_sha256"),
            RequiredString(root, "prompt_sha256"),
            new PilotBPairing(
                RequiredString(pairingElement, "pair_id"),
                RequiredInt(pairingElement, "pair_ordinal"),
                RequiredInt(pairingElement, "arm_order_index"),
                RequiredDateTime(pairingElement, "pair_started_at_utc"),
                RequiredDateTime(pairingElement, "pair_completed_at_utc")),
            ParseValidity(RequiredString(root, "validity")),
            RequiredArray(root, "invalid_reasons").EnumerateArray().Select(value => value.GetString() ?? throw new FormatException("Invalid reason must be a string.")).ToArray(),
            messages,
            new PilotBAdjudication(
                ParseEnum<PilotBTaskQuality>(RequiredString(adjudicationElement, "task_quality")),
                ParseEnum<PilotBClarity>(RequiredString(adjudicationElement, "clarity")),
                ParseEnum<PilotBSafety>(RequiredString(adjudicationElement, "safety")),
                RequiredBool(adjudicationElement, "mandatory_update_omitted"),
                RequiredBool(adjudicationElement, "critical_failure"),
                RequiredBool(adjudicationElement, "completed"),
                RequiredBool(adjudicationElement, "corpus_runtime_unstable")),
            new PilotBIntegrityFacts(
                RequiredBool(integrityElement, "artifact_complete"),
                RequiredBool(integrityElement, "repository_boundary_valid"),
                RequiredBool(integrityElement, "prompt_bytes_verified"),
                RequiredBool(integrityElement, "timing_valid"),
                RequiredBool(integrityElement, "auth_lane_excluded"),
                RequiredBool(integrityElement, "workspace_integrity_captured")));
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

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Required string '{name}' is missing.");
        }

        return value.GetString()!;
    }

    private static DateTimeOffset RequiredDateTime(JsonElement root, string name)
    {
        var value = RequiredString(root, name);
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new FormatException($"Date-time '{name}' is invalid.");
        }

        return parsed;
    }

    private static int RequiredInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var parsed))
        {
            throw new FormatException($"Required integer '{name}' is missing.");
        }

        return parsed;
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

    private static void Require(JsonElement root, string name, string expected)
    {
        if (!string.Equals(RequiredString(root, name), expected, StringComparison.Ordinal))
        {
            throw new FormatException($"'{name}' does not match the v3 contract.");
        }
    }

    private static PilotBArm ParseArm(string value) => ParseEnum<PilotBArm>(value);

    private static PilotBMessageKind ParseMessageKind(string value) => ParseEnum<PilotBMessageKind>(value);

    private static PilotBRunValidity ParseValidity(string value) => ParseEnum<PilotBRunValidity>(value);

    private static T ParseEnum<T>(string value) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
        {
            throw new FormatException($"Enum value '{value}' is invalid for {typeof(T).Name}.");
        }

        return parsed;
    }

    private static string ArmToString(PilotBArm arm) => arm.ToString().ToLowerInvariant();

    private static string MessageKindToString(PilotBMessageKind kind) => kind.ToString().ToLowerInvariant();

    private static string ValidityToString(PilotBRunValidity validity) => validity.ToString().ToLowerInvariant();
}
