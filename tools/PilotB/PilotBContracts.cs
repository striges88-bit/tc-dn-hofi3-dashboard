namespace CryptoIndicatorApp.PilotB;

public static class PilotBContractVersions
{
    public const string RunRecord = "pilot-b.run-record.v3";
    public const string Transcript = "pilot-b.transcript.v3";
}

public enum PilotBArm
{
    Control,
    Treatment
}

public enum PilotBMessageKind
{
    Routine,
    Observable
}

public enum PilotBRunValidity
{
    Valid,
    Invalid
}

public enum PilotBTranscriptTerminalOutcome
{
    Partial,
    Success,
    Failure,
    FatalError
}

public enum PilotBTaskQuality
{
    Pass,
    Fail
}

public enum PilotBClarity
{
    Pass,
    Minor,
    Fail
}

public enum PilotBSafety
{
    Pass,
    Fail,
    NotRated
}

public enum PilotBDecision
{
    Pass,
    Fail,
    Inconclusive,
    InvalidBatch
}

public sealed record PilotBMessage(
    int Sequence,
    string Text,
    PilotBMessageKind Kind,
    string SourceEventType = "item.completed",
    string Phase = "commentary");

public sealed record PilotBPairing(
    string PairId,
    int PairOrdinal,
    int ArmOrderIndex,
    DateTimeOffset PairStartedAtUtc,
    DateTimeOffset PairCompletedAtUtc);

public sealed record PilotBAdjudication(
    PilotBTaskQuality TaskQuality,
    PilotBClarity Clarity,
    PilotBSafety Safety,
    bool MandatoryUpdateOmitted,
    bool CriticalFailure,
    bool Completed,
    bool CorpusRuntimeUnstable);

public sealed record PilotBIntegrityFacts(
    bool ArtifactComplete,
    bool RepositoryBoundaryValid,
    bool PromptBytesVerified,
    bool TimingValid,
    bool AuthLaneExcluded,
    bool WorkspaceIntegrityCaptured);

public sealed record PilotBRunRecord(
    string RunId,
    string PairId,
    string CaseId,
    PilotBArm Arm,
    int Replica,
    bool IsSafetyCase,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ProtocolSha256,
    string SourceManifestSha256,
    string ExecutableSha256,
    string PromptSha256,
    PilotBPairing Pairing,
    PilotBRunValidity Validity,
    IReadOnlyList<string> InvalidReasons,
    IReadOnlyList<PilotBMessage> Messages,
    PilotBAdjudication Adjudication,
    PilotBIntegrityFacts Integrity)
{
    public string SchemaVersion => PilotBContractVersions.RunRecord;
}

public sealed record PilotBTranscriptMessage(int Sequence, string Text, string Phase);

public sealed record PilotBTranscriptParseResult(
    IReadOnlyList<PilotBTranscriptMessage> IntermediateMessages,
    bool IsValid,
    bool HasTurnCompleted,
    bool HasTurnFailed,
    int LineCount,
    IReadOnlyList<string> InvalidReasons,
    IReadOnlyList<string> ExcludedEventTypes)
{
    public string SchemaVersion => PilotBContractVersions.Transcript;

    public IReadOnlyList<PilotBTranscriptMessage> Commentary => IntermediateMessages;

    public IReadOnlyList<PilotBTranscriptMessage> SemanticMessages { get; init; } = IntermediateMessages;

    public IReadOnlyList<PilotBTranscriptMessage> FinalMessages { get; init; } = Array.Empty<PilotBTranscriptMessage>();

    public PilotBTranscriptTerminalOutcome TerminalOutcome { get; init; } = PilotBTranscriptTerminalOutcome.Partial;
}
