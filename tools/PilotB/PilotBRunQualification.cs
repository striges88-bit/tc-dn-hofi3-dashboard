namespace CryptoIndicatorApp.PilotB;

public sealed record PilotBRunQualificationFacts(
    bool ProcessStarted,
    int? ExitCode,
    bool TimedOut,
    PilotBTranscriptParseResult Transcript,
    bool TimingValid,
    bool ExecutableHashValid,
    bool RepositoryBoundaryValid,
    bool PromptBytesVerified,
    bool WorkspaceIntegrityCaptured,
    bool PayloadCaptured,
    IReadOnlyList<string> AdditionalInvalidReasons);

public static class PilotBRunQualification
{
    public static PilotBRunQualificationResult Evaluate(PilotBRunQualificationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.Transcript);

        var reasons = new List<string>();
        if (!facts.ProcessStarted)
        {
            Add(reasons, "process-start-failed");
        }

        if (facts.TimedOut)
        {
            Add(reasons, "timeout");
        }
        else if (facts.ExitCode is not 0)
        {
            Add(reasons, "nonzero-exit");
        }

        foreach (var reason in facts.Transcript.InvalidReasons)
        {
            Add(reasons, reason == "missing-turn-completed" ? "partial-run" : reason);
        }

        if (facts.Transcript.HasTurnFailed)
        {
            Add(reasons, "failed-event");
        }

        if (!facts.TimingValid)
        {
            Add(reasons, "timing-violation");
        }

        if (!facts.ExecutableHashValid)
        {
            Add(reasons, "executable-drift");
        }

        if (!facts.RepositoryBoundaryValid)
        {
            Add(reasons, "repository-boundary-invalid");
        }

        if (!facts.PromptBytesVerified)
        {
            Add(reasons, "prompt-bytes-unverified");
        }

        if (!facts.WorkspaceIntegrityCaptured)
        {
            Add(reasons, "workspace-integrity-missing");
        }

        if (!facts.PayloadCaptured)
        {
            Add(reasons, "missing-artifact");
        }

        foreach (var reason in facts.AdditionalInvalidReasons)
        {
            Add(reasons, reason);
        }

        return new PilotBRunQualificationResult(
            reasons.Count == 0 ? PilotBRunValidity.Valid : PilotBRunValidity.Invalid,
            reasons);
    }

    private static void Add(ICollection<string> reasons, string reason)
    {
        if (!string.IsNullOrWhiteSpace(reason) && !reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }
}
