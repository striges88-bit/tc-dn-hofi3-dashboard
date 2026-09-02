namespace CryptoIndicatorApp.PilotB;

public static class PilotBPreflightReasonCodes
{
    public const string ExecutableNotAbsolute = nameof(ExecutableNotAbsolute);
    public const string ManifestNotAbsolute = nameof(ManifestNotAbsolute);
    public const string FixtureNotAbsolute = nameof(FixtureNotAbsolute);
    public const string ArtifactNotAbsolute = nameof(ArtifactNotAbsolute);
    public const string InvalidExpectedExecutableSha256 = nameof(InvalidExpectedExecutableSha256);
    public const string InvalidExpectedManifestSha256 = nameof(InvalidExpectedManifestSha256);
    public const string EmptyPrompt = nameof(EmptyPrompt);
    public const string InvalidTimeout = nameof(InvalidTimeout);
    public const string ExecutableMissing = nameof(ExecutableMissing);
    public const string ManifestMissing = nameof(ManifestMissing);
    public const string FixtureMissing = nameof(FixtureMissing);
    public const string RepositoryBoundaryInvalid = nameof(RepositoryBoundaryInvalid);
    public const string ArtifactPathAlreadyExists = nameof(ArtifactPathAlreadyExists);
    public const string BoundaryContamination = nameof(BoundaryContamination);
    public const string ExecutableHashMismatch = nameof(ExecutableHashMismatch);
    public const string ManifestHashMismatch = nameof(ManifestHashMismatch);
    public const string MalformedManifest = nameof(MalformedManifest);
    public const string InvalidArmId = nameof(InvalidArmId);
    public const string PreflightReadFailed = nameof(PreflightReadFailed);
    public const string ArtifactOwnershipConflict = nameof(ArtifactOwnershipConflict);
    public const string ArtifactOwnershipUnavailable = nameof(ArtifactOwnershipUnavailable);
    public const string ArtifactOwnershipCleanupFailed = nameof(ArtifactOwnershipCleanupFailed);
}

public sealed class PilotBPreflightException : Exception
{
    public PilotBPreflightException(IEnumerable<string> reasonCodes)
        : this(Normalize(reasonCodes), null)
    {
    }

    public PilotBPreflightException(IEnumerable<string> reasonCodes, Exception innerException)
        : this(Normalize(reasonCodes), innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }

    private PilotBPreflightException(string[] reasonCodes, Exception? innerException)
        : base($"Pilot B preflight failed: {string.Join(", ", reasonCodes)}.", innerException)
    {
        ReasonCodes = reasonCodes;
    }

    public IReadOnlyList<string> ReasonCodes { get; }

    private static string[] Normalize(IEnumerable<string> reasonCodes)
    {
        ArgumentNullException.ThrowIfNull(reasonCodes);
        var normalized = reasonCodes
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return normalized.Length > 0
            ? normalized
            : throw new ArgumentException("At least one preflight reason code is required.", nameof(reasonCodes));
    }
}

public sealed record PilotBRunnerOptions
{
    public required string ExecutablePath { get; init; }
    public required string ExpectedExecutableSha256 { get; init; }
    public required string ArmManifestPath { get; init; }
    public required string ExpectedArmManifestSha256 { get; init; }
    public required string FixtureRoot { get; init; }
    public required string ArtifactDirectory { get; init; }
    public required byte[] PromptBytes { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required bool IsQualification { get; init; }
    public Func<DateTimeOffset> UtcNowProvider { get; init; } = static () => DateTimeOffset.UtcNow;
}

public enum PilotBRunnerStatus
{
    Valid,
    Invalid
}

public enum PilotBEvidenceState
{
    Unsealed,
    Sealed
}

public sealed record PilotBRunQualificationResult(
    PilotBRunValidity Validity,
    IReadOnlyList<string> InvalidReasons);

public sealed record PilotBVerifiedEvidenceProjection
{
    internal PilotBVerifiedEvidenceProjection(
        PilotBArm arm,
        string protocolSha256,
        PilotBTranscriptParseResult transcript,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        IReadOnlyList<string> invocationArguments,
        bool isQualification,
        int? exitCode,
        bool timedOut,
        PilotBRunQualificationResult qualification,
        PilotBRunnerIntegrityFacts integrityFacts,
        bool promptBytesVerified,
        string semanticFingerprint)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(invocationArguments);
        ArgumentNullException.ThrowIfNull(qualification);
        ArgumentNullException.ThrowIfNull(integrityFacts);

        Arm = arm;
        ProtocolSha256 = protocolSha256;
        Transcript = Freeze(transcript);
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        InvocationArguments = Array.AsReadOnly(invocationArguments.ToArray());
        IsQualification = isQualification;
        ExitCode = exitCode;
        TimedOut = timedOut;
        Qualification = new PilotBRunQualificationResult(
            qualification.Validity,
            Array.AsReadOnly(qualification.InvalidReasons.ToArray()));
        IntegrityFacts = integrityFacts;
        PromptBytesVerified = promptBytesVerified;
        SemanticFingerprint = semanticFingerprint;
    }

    public PilotBArm Arm { get; }
    public string ProtocolSha256 { get; }
    public PilotBTranscriptParseResult Transcript { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset CompletedAtUtc { get; }
    public IReadOnlyList<string> InvocationArguments { get; }
    public bool IsQualification { get; }
    public int? ExitCode { get; }
    public bool TimedOut { get; }
    public PilotBRunQualificationResult Qualification { get; }
    public PilotBRunnerIntegrityFacts IntegrityFacts { get; }
    public bool PromptBytesVerified { get; }
    public string SemanticFingerprint { get; }

    internal static bool TryParseArm(string armId, out PilotBArm arm)
        => Enum.TryParse(armId, ignoreCase: true, out arm)
           && Enum.IsDefined(arm)
           && string.Equals(armId, arm.ToString().ToLowerInvariant(), StringComparison.Ordinal);

    private static PilotBTranscriptParseResult Freeze(PilotBTranscriptParseResult transcript)
        => new(
            Array.AsReadOnly(transcript.IntermediateMessages.ToArray()),
            transcript.IsValid,
            transcript.HasTurnCompleted,
            transcript.HasTurnFailed,
            transcript.LineCount,
            Array.AsReadOnly(transcript.InvalidReasons.ToArray()),
            Array.AsReadOnly(transcript.ExcludedEventTypes.ToArray()))
        {
            SemanticMessages = Array.AsReadOnly(transcript.SemanticMessages.ToArray()),
            FinalMessages = Array.AsReadOnly(transcript.FinalMessages.ToArray()),
            TerminalOutcome = transcript.TerminalOutcome
        };
}

public sealed record PilotBEvidenceVerification
{
    internal PilotBEvidenceVerification(
        PilotBEvidenceState evidenceState,
        PilotBVerifiedEvidenceProjection? verifiedEvidence,
        IReadOnlyList<string> invalidReasons)
    {
        ArgumentNullException.ThrowIfNull(invalidReasons);
        EvidenceState = evidenceState;
        VerifiedEvidence = verifiedEvidence;
        InvalidReasons = Array.AsReadOnly(invalidReasons.ToArray());
    }

    public PilotBEvidenceState EvidenceState { get; }

    public PilotBVerifiedEvidenceProjection? VerifiedEvidence { get; }

    public IReadOnlyList<string> InvalidReasons { get; }

    public PilotBRunQualificationResult? Qualification => VerifiedEvidence?.Qualification;

    public string? SemanticFingerprint => VerifiedEvidence?.SemanticFingerprint;
}

public sealed record PilotBArtifactPaths(
    string Root,
    string RawOutputPath,
    string MetadataPath,
    string ManifestPath,
    string PreManifestPath,
    string PostManifestPath,
    string IntegrityPath,
    string PromptPath,
    string StderrPath)
{
    public static PilotBArtifactPaths Empty { get; } = new("", "", "", "", "", "", "", "", "");

    public string LockPath => Path.Combine(Root, ".pilot-b-write-lock");

    public string SealPath => IntegrityPath;
}

public sealed record PilotBRunnerIntegrityFacts(
    string ExecutableSha256,
    string ArmManifestSha256,
    string PromptSha256,
    string PreManifestSha256,
    string PostManifestSha256,
    bool RepositoryBoundaryValid,
    bool ArtifactComplete,
    bool TimingValid,
    bool AuthLaneExcluded,
    bool WorkspaceIntegrityCaptured);

public sealed record PilotBRunnerResult(
    PilotBRunnerStatus Status,
    bool IsQualification,
    int? ExitCode,
    bool TimedOut,
    IReadOnlyList<string> InvalidReasons,
    IReadOnlyList<string> InvocationArguments,
    PilotBTranscriptParseResult Transcript,
    PilotBArtifactPaths Artifacts,
    PilotBRunnerIntegrityFacts IntegrityFacts,
    string? DeterministicFingerprint)
{
    public PilotBEvidenceState EvidenceState { get; init; } = PilotBEvidenceState.Unsealed;

    public PilotBRunValidity? RunValidity { get; init; }

    public PilotBRunQualificationResult? Qualification { get; init; }
}
