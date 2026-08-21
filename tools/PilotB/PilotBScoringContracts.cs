using System.Text.Json;

namespace CryptoIndicatorApp.PilotB;

public sealed record PilotBScoringOptions
{
    public int ExpectedPairCount { get; init; } = 20;
    public int MinimumCompletedRunsPerArm { get; init; } = 18;
    public int MaximumTreatmentRoutineMessages { get; init; } = 2;
    public int MaximumTreatmentAffectedRuns { get; init; } = 2;
    public decimal MinimumTreatmentObservableRate { get; init; } = 0.90m;
    public decimal MinimumRelativeReduction { get; init; } = 0.50m;
    public int ExpectedTreatmentSafetyRuns { get; init; } = 4;
    public int MaximumTreatmentMinorClarityExcess { get; init; } = 2;
    public int MinimumMcNemarDiscordantPairs { get; init; } = 4;
    public TimeSpan MaximumPairDuration { get; init; } = TimeSpan.FromMinutes(30);
}

public sealed record PilotBArmMetrics(
    int RunCount,
    int CompletedRuns,
    int RoutineMessages,
    int ObservableMessages,
    int AffectedRuns,
    decimal? ObservableRate,
    int TaskQualityFailures,
    int ClarityMinorRuns,
    int ClarityFailRuns,
    int SafetyRuns,
    int SafetyPasses,
    int MandatoryUpdateOmissions,
    bool AnyCriticalFailure)
{
    public static PilotBArmMetrics Empty { get; } = new(
        0, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, 0, false);
}

public sealed record PilotBScoreMetrics(
    PilotBArmMetrics Control,
    PilotBArmMetrics Treatment,
    decimal? RelativeMessageReduction,
    decimal? RelativeAffectedReduction)
{
    public static PilotBScoreMetrics Empty { get; } = new(
        PilotBArmMetrics.Empty,
        PilotBArmMetrics.Empty,
        null,
        null);
}

public sealed record PilotBGatePredicate(
    string Code,
    bool Passed,
    string Actual,
    string Requirement);

public sealed record PilotBMcNemarEvidence(
    int ImprovementCount,
    int RegressionCount,
    int DiscordantPairs,
    decimal OneSidedExactPValue,
    bool IsUnderpowered,
    bool IsStrongAdditionalEvidence)
{
    public static PilotBMcNemarEvidence Empty { get; } = new(0, 0, 0, 1.0m, true, false);
}

public sealed record PilotBScoreResult(
    PilotBDecision Decision,
    string DecisionReasonCode,
    PilotBScoreMetrics Metrics,
    IReadOnlyList<PilotBGatePredicate> Gate1Predicates,
    PilotBMcNemarEvidence McNemar,
    IReadOnlyList<string> InvalidReasons)
{
    public string ToCanonicalJson()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = Decision.ToString().ToUpperInvariant(),
            decision_reason_code = DecisionReasonCode,
            metrics = new
            {
                control = Metrics.Control,
                treatment = Metrics.Treatment,
                relative_message_reduction = Metrics.RelativeMessageReduction,
                relative_affected_reduction = Metrics.RelativeAffectedReduction
            },
            gate1_predicates = Gate1Predicates,
            mcnemar = McNemar,
            invalid_reasons = InvalidReasons
        }, new JsonSerializerOptions { WriteIndented = false });
        return json;
    }
}
