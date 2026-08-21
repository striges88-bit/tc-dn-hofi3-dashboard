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
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);
    public bool IsQualification { get; init; } = true;
    public Func<DateTimeOffset> UtcNowProvider { get; init; } = static () => DateTimeOffset.UtcNow;
}

public enum PilotBRunnerStatus
{
    Valid,
    Invalid
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
    string DeterministicFingerprint);
