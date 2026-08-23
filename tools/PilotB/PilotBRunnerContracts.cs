namespace CryptoIndicatorApp.PilotB;

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

public sealed record PilotBEvidenceVerification(
    PilotBEvidenceState EvidenceState,
    PilotBRunQualificationResult? Qualification,
    string? SemanticFingerprint,
    IReadOnlyList<string> InvalidReasons);

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
    bool IsScored,
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
