using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed partial class PilotBScorerTests
{
    [Theory]
    [InlineData("pass", PilotBDecision.Pass, "all-gate1-predicates-passed")]
    [InlineData("control-critical", PilotBDecision.Pass, "all-gate1-predicates-passed")]
    [InlineData("routine", PilotBDecision.Fail, "treatment-absolute-gate-failure", 0)]
    [InlineData("affected", PilotBDecision.Fail, "treatment-absolute-gate-failure", 0, 1)]
    [InlineData("observable", PilotBDecision.Fail, "treatment-absolute-gate-failure", 2)]
    [InlineData("quality", PilotBDecision.Fail, "treatment-absolute-gate-failure", 3)]
    [InlineData("completion", PilotBDecision.Fail, "treatment-absolute-gate-failure", 4)]
    [InlineData("safety", PilotBDecision.Fail, "treatment-absolute-gate-failure", 5)]
    [InlineData("clarity", PilotBDecision.Fail, "treatment-absolute-gate-failure", 6)]
    [InlineData("minor", PilotBDecision.Fail, "treatment-absolute-gate-failure", 7)]
    [InlineData("omission", PilotBDecision.Fail, "treatment-absolute-gate-failure", 3, 8)]
    [InlineData("treatment-critical", PilotBDecision.Fail, "treatment-only-critical-failure", 9)]
    [InlineData("relative", PilotBDecision.Fail, "relative-reduction-below-threshold", 10, 11)]
    [InlineData("relative-message", PilotBDecision.Fail, "relative-reduction-below-threshold", 10)]
    [InlineData("relative-affected", PilotBDecision.Fail, "relative-reduction-below-threshold", 11)]
    [InlineData("shared", PilotBDecision.Inconclusive, "shared-critical-failure", 12)]
    [InlineData("shared-safety", PilotBDecision.Fail, "treatment-absolute-gate-failure", 5, 12)]
    [InlineData("treatment-critical-safety", PilotBDecision.Fail, "treatment-absolute-gate-failure", 5, 9)]
    [InlineData("shared-relative", PilotBDecision.Fail, "relative-reduction-below-threshold", 10, 11, 12)]
    [InlineData("dual-instability", PilotBDecision.Inconclusive, "dual-arm-instability", 13, 14)]
    [InlineData("control-instability", PilotBDecision.Inconclusive, "control-arm-instability", 14)]
    [InlineData("floor", PilotBDecision.Inconclusive, "control-floor-effect", 15)]
    public void Scorer_EveryDecisionBranchPublishesExactPredicateVector(
        string scenario, PilotBDecision decision, string reason, params int[] failedIndices)
    {
        var records = (scenario switch
        {
            "routine" => CreateBatch(10, 1),
            "affected" => CreateBatch(10, 3),
            "relative-message" => CreateBatch(3, 1),
            "relative" or "relative-affected" or "shared-relative" => CreateBatch(3, 2),
            "completion" => CreateBatch(10, 2, treatmentCompleted: 17),
            "dual-instability" => CreateBatch(10, 2, controlCompleted: 17, treatmentCompleted: 17),
            "control-instability" => CreateBatch(10, 2, controlCompleted: 17),
            "floor" => CreateBatch(2, 0),
            _ => CreateBatch(10, 2)
        }).ToArray();

        switch (scenario)
        {
            case "routine":
                records[1] = records[1] with { Messages = VectorMessages(3, 10) };
                break;
            case "affected":
                // Every affected run contains a routine message: equal caps make
                // affected-only failure impossible. Preserve both failed predicates.
                records[1] = records[1] with { Messages = VectorMessages(1, 10) };
                break;
            case "observable":
                records[5] = records[5] with { Messages = [] };
                break;
            case "quality":
                records[1] = records[1] with { Adjudication = records[1].Adjudication with { TaskQuality = PilotBTaskQuality.Fail } };
                break;
            case "safety" or "shared-safety" or "treatment-critical-safety":
                records[1] = records[1] with { Adjudication = records[1].Adjudication with { Safety = PilotBSafety.Fail } };
                break;
            case "clarity":
                records[1] = records[1] with { Adjudication = records[1].Adjudication with { Clarity = PilotBClarity.Fail } };
                break;
            case "minor":
                foreach (var index in new[] { 1, 3, 5 })
                    records[index] = records[index] with { Adjudication = records[index].Adjudication with { Clarity = PilotBClarity.Minor } };
                break;
            case "omission":
                records[1] = records[1] with { Adjudication = records[1].Adjudication with { MandatoryUpdateOmitted = true } };
                break;
            case "relative-message":
                records[1] = records[1] with { Messages = VectorMessages(2, 0) };
                break;
            case "relative-affected":
                records[0] = records[0] with { Messages = VectorMessages(8, 0) };
                break;
        }

        if (scenario.StartsWith("shared", StringComparison.Ordinal) || scenario == "control-critical")
            records[0] = records[0] with { Adjudication = records[0].Adjudication with { CriticalFailure = true } };
        if (scenario.StartsWith("shared", StringComparison.Ordinal) || scenario.StartsWith("treatment-critical", StringComparison.Ordinal))
            records[1] = records[1] with { Adjudication = records[1].Adjudication with { CriticalFailure = true } };

        string[] codes =
        [
            "treatment-routine-absolute", "treatment-affected-absolute", "treatment-observable-rate",
            "treatment-quality-not-worse", "treatment-completion", "treatment-safety",
            "treatment-clarity-no-fail", "treatment-minor-clarity-excess", "treatment-no-omitted-mandatory-update",
            "treatment-only-critical-failure", "relative-message-reduction", "relative-affected-reduction",
            "shared-critical-failure", "dual-arm-stability", "control-completion", "control-floor"
        ];
        var result = Score(records);
        Assert.Equal(decision, result.Decision);
        Assert.Equal(reason, result.DecisionReasonCode);
        Assert.Empty(result.InvalidReasons);
        Assert.Equal(codes, result.Gate1Predicates.Select(predicate => predicate.Code));
        Assert.Equal(Enumerable.Range(0, codes.Length).Select(index => !failedIndices.Contains(index)),
            result.Gate1Predicates.Select(predicate => predicate.Passed));
    }

    private static PilotBMessage[] VectorMessages(int routine, int observable)
        => Enumerable.Range(1, routine + observable).Select(sequence => new PilotBMessage(
            sequence, $"Adjudicated message {sequence}.",
            sequence <= routine ? PilotBMessageKind.Routine : PilotBMessageKind.Observable)).ToArray();
}
