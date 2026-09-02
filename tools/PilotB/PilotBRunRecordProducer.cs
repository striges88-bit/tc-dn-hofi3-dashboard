namespace CryptoIndicatorApp.PilotB;

public enum PilotBRunRecordRejectionCode
{
    UnsealedEvidence,
    InvalidRun,
    QualificationEvidence,
    InvalidProjectionInput
}

public sealed record PilotBRunRecordRejection
{
    internal PilotBRunRecordRejection(
        PilotBRunRecordRejectionCode code,
        IReadOnlyList<string> details)
    {
        Code = code;
        Details = details;
    }

    public PilotBRunRecordRejectionCode Code { get; }
    public IReadOnlyList<string> Details { get; }
}

public sealed record PilotBRunRecordProductionResult
{
    internal PilotBRunRecordProductionResult(
        PilotBRunRecord? runRecord,
        PilotBRunRecordRejection? rejection)
    {
        RunRecord = runRecord;
        Rejection = rejection;
    }

    public PilotBRunRecord? RunRecord { get; }
    public PilotBRunRecordRejection? Rejection { get; }
}

public sealed record PilotBRunRecordProductionRequest
{
    public required string ArtifactDirectory { get; init; }
    public required string RunId { get; init; }
    public required string PairId { get; init; }
    public required string CaseId { get; init; }
    public required int Replica { get; init; }
    public required bool IsSafetyCase { get; init; }
    public required string SourceManifestSha256 { get; init; }
    public required PilotBPairing Pairing { get; init; }
    public required IReadOnlyList<PilotBMessageKind> MessageKinds { get; init; }
    public required PilotBAdjudication Adjudication { get; init; }
}

public sealed class PilotBRunRecordProducer
{
    public PilotBRunRecordProductionResult Produce(PilotBRunRecordProductionRequest? request)
    {
        var messageKinds = request?.MessageKinds?.ToArray() ?? [];

        if (!HasValidProjectionInput(request, messageKinds))
        {
            return InvalidProjectionInput();
        }

        var verification = new PilotBEvidenceBundleVerifier().Verify(request!.ArtifactDirectory);
        if (verification.EvidenceState != PilotBEvidenceState.Sealed
            || verification.VerifiedEvidence is not { } evidence)
        {
            return Reject(
                PilotBRunRecordRejectionCode.UnsealedEvidence,
                verification.InvalidReasons);
        }

        if (evidence.Qualification.Validity != PilotBRunValidity.Valid)
        {
            return Reject(
                PilotBRunRecordRejectionCode.InvalidRun,
                evidence.Qualification.InvalidReasons);
        }

        if (evidence.IsQualification)
        {
            return Reject(
                PilotBRunRecordRejectionCode.QualificationEvidence,
                ["qualification-evidence"]);
        }

        try
        {
            var messages = PilotBRunRecordProjection.ProjectCommentary(
                evidence.Transcript,
                messageKinds);
            var record = new PilotBRunRecord(
                request.RunId,
                request.PairId,
                request.CaseId,
                evidence.Arm,
                request.Replica,
                request.IsSafetyCase,
                evidence.StartedAtUtc,
                evidence.CompletedAtUtc,
                evidence.ProtocolSha256.ToLowerInvariant(),
                request.SourceManifestSha256.ToLowerInvariant(),
                evidence.IntegrityFacts.ExecutableSha256.ToLowerInvariant(),
                evidence.IntegrityFacts.PromptSha256.ToLowerInvariant(),
                request.Pairing,
                evidence.Qualification.Validity,
                Array.AsReadOnly(evidence.Qualification.InvalidReasons.ToArray()),
                Array.AsReadOnly(messages.ToArray()),
                request.Adjudication,
                new PilotBIntegrityFacts(
                    evidence.IntegrityFacts.ArtifactComplete,
                    evidence.IntegrityFacts.RepositoryBoundaryValid,
                    evidence.PromptBytesVerified,
                    evidence.IntegrityFacts.TimingValid,
                    evidence.IntegrityFacts.AuthLaneExcluded,
                    evidence.IntegrityFacts.WorkspaceIntegrityCaptured));
            return new PilotBRunRecordProductionResult(record, null);
        }
        catch (ArgumentException)
        {
            return InvalidProjectionInput();
        }
    }

    private static bool HasValidProjectionInput(
        PilotBRunRecordProductionRequest? request,
        IReadOnlyList<PilotBMessageKind> messageKinds)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.ArtifactDirectory)
            || !Path.IsPathFullyQualified(request.ArtifactDirectory)
            || string.IsNullOrWhiteSpace(request.RunId)
            || string.IsNullOrWhiteSpace(request.PairId)
            || string.IsNullOrWhiteSpace(request.CaseId)
            || request.Replica is < 1 or > 2
            || !PilotBSha256.IsSha256(request.SourceManifestSha256)
            || request.Pairing is null
            || request.MessageKinds is null
            || request.Adjudication is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Pairing.PairId)
            || !string.Equals(request.PairId, request.Pairing.PairId, StringComparison.Ordinal)
            || request.Pairing.PairOrdinal < 1
            || request.Pairing.ArmOrderIndex is < 0 or > 1
            || request.Pairing.PairCompletedAtUtc < request.Pairing.PairStartedAtUtc
            || messageKinds.Any(kind => kind is not (PilotBMessageKind.Routine or PilotBMessageKind.Observable)))
        {
            return false;
        }

        return request.Adjudication.TaskQuality is PilotBTaskQuality.Pass or PilotBTaskQuality.Fail
            && request.Adjudication.Clarity is PilotBClarity.Pass or PilotBClarity.Minor or PilotBClarity.Fail
            && request.Adjudication.Safety is PilotBSafety.Pass or PilotBSafety.Fail or PilotBSafety.NotRated;
    }

    private static PilotBRunRecordProductionResult InvalidProjectionInput()
        => Reject(
            PilotBRunRecordRejectionCode.InvalidProjectionInput,
            ["invalid-projection-input"]);

    private static PilotBRunRecordProductionResult Reject(
        PilotBRunRecordRejectionCode code,
        IEnumerable<string> details)
    {
        var frozenDetails = details
            .Where(detail => !string.IsNullOrWhiteSpace(detail))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (frozenDetails.Length == 0)
        {
            frozenDetails = [code.ToString()];
        }

        return new PilotBRunRecordProductionResult(
            null,
            new PilotBRunRecordRejection(code, Array.AsReadOnly(frozenDetails)));
    }
}
