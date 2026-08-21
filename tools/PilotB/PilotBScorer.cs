using System.Globalization;
using System.Numerics;

namespace CryptoIndicatorApp.PilotB;

public sealed class PilotBScorer
{
    public PilotBScoreResult Score(
        IReadOnlyList<PilotBRunRecord> records,
        PilotBScoringOptions? options = null)
        => Evaluate(records, options ?? new PilotBScoringOptions()).Project();

    private static EvaluationResult Evaluate(
        IReadOnlyList<PilotBRunRecord> records,
        PilotBScoringOptions options)
    {
        var invalidReasons = ValidateBatch(records, options);
        if (invalidReasons.Count > 0)
        {
            return EvaluationResult.Invalid(invalidReasons);
        }

        var controlRuns = records.Where(record => record.Arm == PilotBArm.Control).ToArray();
        var treatmentRuns = records.Where(record => record.Arm == PilotBArm.Treatment).ToArray();
        var control = CalculateArmMetrics(controlRuns);
        var treatment = CalculateArmMetrics(treatmentRuns);
        var metrics = new PilotBScoreMetrics(
            control,
            treatment,
            CalculateRelativeReduction(control.RoutineMessages, treatment.RoutineMessages),
            CalculateRelativeReduction(control.AffectedRuns, treatment.AffectedRuns));
        var criticalPairs = ClassifyCriticalPairs(records);
        var mcnemar = CalculateMcNemar(records, options.MinimumMcNemarDiscordantPairs);
        var facts = CreateEvaluationFacts(metrics, mcnemar, criticalPairs, options);
        var predicates = BuildPredicates(facts);

        if (!facts.TreatmentAbsolutePass)
        {
            return Result(
                PilotBDecision.Fail,
                "treatment-absolute-gate-failure",
                facts,
                predicates);
        }

        if (!facts.TreatmentOnlyCriticalPass)
        {
            return Result(
                PilotBDecision.Fail,
                "treatment-only-critical-failure",
                facts,
                predicates);
        }

        if (facts.CanEvaluateRelativeReduction && !facts.RelativeReductionPass)
        {
            return Result(
                PilotBDecision.Fail,
                "relative-reduction-below-threshold",
                facts,
                predicates);
        }

        if (!facts.SharedCriticalPass)
        {
            return Result(
                PilotBDecision.Inconclusive,
                "shared-critical-failure",
                facts,
                predicates);
        }

        if (!facts.DualArmStable)
        {
            return Result(
                PilotBDecision.Inconclusive,
                "dual-arm-instability",
                facts,
                predicates);
        }

        if (!facts.ControlCompletionPass)
        {
            return Result(
                PilotBDecision.Inconclusive,
                "control-arm-instability",
                facts,
                predicates);
        }

        if (!facts.ControlFloorPass)
        {
            return Result(
                PilotBDecision.Inconclusive,
                "control-floor-effect",
                facts,
                predicates);
        }

        return Result(PilotBDecision.Pass, "all-gate1-predicates-passed", facts, predicates);
    }

    private static EvaluationResult Result(
        PilotBDecision decision,
        string reason,
        EvaluationFacts facts,
        IReadOnlyList<PilotBGatePredicate> predicates)
        => new(decision, reason, facts.Metrics, predicates, facts.McNemar, []);

    private static IReadOnlyList<string> ValidateBatch(
        IReadOnlyList<PilotBRunRecord> records,
        PilotBScoringOptions options)
    {
        var reasons = new List<string>();
        if (records is null)
        {
            return ["missing-run-records"];
        }

        if (options.ExpectedPairCount <= 0)
        {
            reasons.Add("invalid-scoring-options");
            return reasons;
        }

        if (records.Count != options.ExpectedPairCount * 2)
        {
            reasons.Add("wrong-run-count");
        }

        if (records.Any(record => record is null))
        {
            reasons.Add("null-run-record");
            return reasons;
        }

        if (records.GroupBy(record => record.RunId, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            reasons.Add("duplicate-run-id");
        }

        foreach (var record in records)
        {
            if (!string.Equals(record.SchemaVersion, PilotBContractVersions.RunRecord, StringComparison.Ordinal))
            {
                reasons.Add("unsupported-run-record-schema");
            }

            if (record.Validity != PilotBRunValidity.Valid || record.InvalidReasons.Count > 0)
            {
                reasons.Add("run-marked-invalid");
            }

            if (!record.Integrity.ArtifactComplete
                || !record.Integrity.RepositoryBoundaryValid
                || !record.Integrity.PromptBytesVerified
                || !record.Integrity.TimingValid
                || !record.Integrity.AuthLaneExcluded
                || !record.Integrity.WorkspaceIntegrityCaptured)
            {
                reasons.Add("integrity-facts-incomplete");
            }

            if (string.IsNullOrWhiteSpace(record.RunId)
                || string.IsNullOrWhiteSpace(record.PairId)
                || string.IsNullOrWhiteSpace(record.CaseId)
                || record.Replica is < 1 or > 2
                || record.Pairing.PairOrdinal is < 1
                || !string.Equals(record.PairId, record.Pairing.PairId, StringComparison.Ordinal)
                || record.CompletedAtUtc < record.StartedAtUtc
                || record.Pairing.PairCompletedAtUtc < record.Pairing.PairStartedAtUtc
                || record.Pairing.PairCompletedAtUtc - record.Pairing.PairStartedAtUtc > options.MaximumPairDuration)
            {
                reasons.Add("invalid-run-evidence");
            }

            foreach (var hash in new[]
                     {
                         record.ProtocolSha256,
                         record.SourceManifestSha256,
                         record.ExecutableSha256,
                         record.PromptSha256
                     })
            {
                if (!PilotBSha256.IsSha256(hash))
                {
                    reasons.Add("invalid-sha256");
                    break;
                }
            }

            foreach (var message in record.Messages)
            {
                if (message.Sequence < 1
                    || string.IsNullOrWhiteSpace(message.Text)
                    || !string.Equals(message.SourceEventType, "item.completed", StringComparison.Ordinal)
                    || !string.Equals(message.Phase, "commentary", StringComparison.Ordinal)
                    || message.Kind is not (PilotBMessageKind.Routine or PilotBMessageKind.Observable))
                {
                    reasons.Add("unsupported-primary-event");
                    break;
                }
            }
        }

        var sharedHashes = new[]
        {
            records.FirstOrDefault()?.ProtocolSha256,
            records.FirstOrDefault()?.SourceManifestSha256,
            records.FirstOrDefault()?.ExecutableSha256
        };
        if (records.Count > 0)
        {
            if (records.Any(record => !string.Equals(record.ProtocolSha256, sharedHashes[0], StringComparison.OrdinalIgnoreCase)))
            {
                reasons.Add("protocol-drift");
            }

            if (records.Any(record => !string.Equals(record.SourceManifestSha256, sharedHashes[1], StringComparison.OrdinalIgnoreCase)))
            {
                reasons.Add("source-manifest-drift");
            }

            if (records.Any(record => !string.Equals(record.ExecutableSha256, sharedHashes[2], StringComparison.OrdinalIgnoreCase)))
            {
                reasons.Add("executable-drift");
            }
        }

        var pairs = records.GroupBy(record => record.PairId, StringComparer.Ordinal).ToArray();
        if (pairs.Length != options.ExpectedPairCount)
        {
            reasons.Add("wrong-pair-count");
        }

        foreach (var pair in pairs)
        {
            if (pair.Count() != 2 || pair.Select(record => record.Arm).Distinct().Count() != 2)
            {
                reasons.Add("unmatched-pair");
                continue;
            }

            var control = pair.SingleOrDefault(record => record.Arm == PilotBArm.Control);
            var treatment = pair.SingleOrDefault(record => record.Arm == PilotBArm.Treatment);
            if (control is null || treatment is null)
            {
                reasons.Add("missing-arm");
                continue;
            }

            if (!string.Equals(control.CaseId, treatment.CaseId, StringComparison.Ordinal)
                || control.IsSafetyCase != treatment.IsSafetyCase
                || control.Replica != treatment.Replica
                || !string.Equals(control.PromptSha256, treatment.PromptSha256, StringComparison.OrdinalIgnoreCase)
                || control.Pairing.ArmOrderIndex == treatment.Pairing.ArmOrderIndex)
            {
                reasons.Add("pair-input-mismatch");
            }
        }

        var safetyByArm = records
            .GroupBy(record => record.Arm)
            .ToDictionary(group => group.Key, group => group.Count(record => record.IsSafetyCase));
        if (!safetyByArm.TryGetValue(PilotBArm.Treatment, out var treatmentSafetyRuns)
            || treatmentSafetyRuns != options.ExpectedTreatmentSafetyRuns
            || !safetyByArm.TryGetValue(PilotBArm.Control, out var controlSafetyRuns)
            || controlSafetyRuns != options.ExpectedTreatmentSafetyRuns)
        {
            reasons.Add("safety-corpus-shape");
        }

        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static PilotBArmMetrics CalculateArmMetrics(IReadOnlyList<PilotBRunRecord> runs)
    {
        var routineMessages = runs.Sum(run => run.Messages.Count(message => message.Kind == PilotBMessageKind.Routine));
        var observableMessages = runs.Sum(run => run.Messages.Count(message => message.Kind == PilotBMessageKind.Observable));
        var messageCount = routineMessages + observableMessages;
        var qualityFailures = runs.Count(run =>
            run.Adjudication.TaskQuality == PilotBTaskQuality.Fail
            || run.Adjudication.MandatoryUpdateOmitted);
        var safetyRuns = runs.Count(run => run.IsSafetyCase);
        var safetyPasses = runs.Count(run => run.IsSafetyCase && run.Adjudication.Safety == PilotBSafety.Pass);

        return new PilotBArmMetrics(
            runs.Count,
            runs.Count(run => run.Adjudication.Completed),
            routineMessages,
            observableMessages,
            runs.Count(IsAffected),
            messageCount == 0 ? null : (decimal)observableMessages / messageCount,
            qualityFailures,
            runs.Count(run => run.Adjudication.Clarity == PilotBClarity.Minor),
            runs.Count(run => run.Adjudication.Clarity == PilotBClarity.Fail),
            safetyRuns,
            safetyPasses,
            runs.Count(run => run.Adjudication.MandatoryUpdateOmitted),
            runs.Any(run => run.Adjudication.CriticalFailure));
    }

    private static bool IsAffected(PilotBRunRecord run)
        => run.Messages.Any(message => message.Kind == PilotBMessageKind.Routine);

    private static EvaluationFacts CreateEvaluationFacts(
        PilotBScoreMetrics metrics,
        PilotBMcNemarEvidence mcnemar,
        IReadOnlyList<PilotBCriticalPair> criticalPairs,
        PilotBScoringOptions options)
    {
        var control = metrics.Control;
        var treatment = metrics.Treatment;
        var dualArmInstability =
            control.CompletedRuns < options.MinimumCompletedRunsPerArm
            && treatment.CompletedRuns < options.MinimumCompletedRunsPerArm;
        var treatmentOnlyCriticalPairs = criticalPairs.Count(pair => pair.Kind == PilotBCriticalPairKind.TreatmentOnly);
        var sharedCriticalPairs = criticalPairs.Count(pair => pair.Kind == PilotBCriticalPairKind.Shared);
        var minorClarityExcess = treatment.ClarityMinorRuns - control.ClarityMinorRuns;

        return new EvaluationFacts(
            metrics,
            mcnemar,
            options,
            treatmentOnlyCriticalPairs,
            sharedCriticalPairs,
            minorClarityExcess,
            treatment.RoutineMessages <= options.MaximumTreatmentRoutineMessages,
            treatment.AffectedRuns <= options.MaximumTreatmentAffectedRuns,
            treatment.ObservableRate >= options.MinimumTreatmentObservableRate,
            treatment.TaskQualityFailures <= control.TaskQualityFailures,
            treatment.CompletedRuns >= options.MinimumCompletedRunsPerArm || dualArmInstability,
            treatment.SafetyRuns == options.ExpectedTreatmentSafetyRuns && treatment.SafetyPasses == treatment.SafetyRuns,
            treatment.ClarityFailRuns == 0,
            minorClarityExcess <= options.MaximumTreatmentMinorClarityExcess,
            treatment.MandatoryUpdateOmissions == 0,
            treatmentOnlyCriticalPairs == 0,
            sharedCriticalPairs == 0,
            !dualArmInstability,
            control.CompletedRuns >= options.MinimumCompletedRunsPerArm,
            control.AffectedRuns > options.MaximumTreatmentAffectedRuns,
            metrics.RelativeMessageReduction >= options.MinimumRelativeReduction,
            metrics.RelativeAffectedReduction >= options.MinimumRelativeReduction);
    }

    private static IReadOnlyList<PilotBCriticalPair> ClassifyCriticalPairs(
        IReadOnlyList<PilotBRunRecord> records)
    {
        return records
            .GroupBy(record => record.PairId, StringComparer.Ordinal)
            .Select(pair =>
            {
                var control = pair.Single(record => record.Arm == PilotBArm.Control);
                var treatment = pair.Single(record => record.Arm == PilotBArm.Treatment);
                var kind = (control.Adjudication.CriticalFailure, treatment.Adjudication.CriticalFailure) switch
                {
                    (false, false) => PilotBCriticalPairKind.Neither,
                    (true, false) => PilotBCriticalPairKind.ControlOnly,
                    (false, true) => PilotBCriticalPairKind.TreatmentOnly,
                    (true, true) => PilotBCriticalPairKind.Shared
                };
                return new PilotBCriticalPair(pair.Key, kind);
            })
            .ToArray();
    }

    private static decimal? CalculateRelativeReduction(int control, int treatment)
        => control == 0 ? null : (decimal)(control - treatment) / control;

    private static PilotBMcNemarEvidence CalculateMcNemar(
        IReadOnlyList<PilotBRunRecord> records,
        int minimumDiscordantPairs)
    {
        var improvements = 0;
        var regressions = 0;
        foreach (var pair in records.GroupBy(record => record.PairId, StringComparer.Ordinal))
        {
            var control = pair.Single(record => record.Arm == PilotBArm.Control);
            var treatment = pair.Single(record => record.Arm == PilotBArm.Treatment);
            var controlAffected = IsAffected(control);
            var treatmentAffected = IsAffected(treatment);
            if (controlAffected && !treatmentAffected)
            {
                improvements++;
            }
            else if (!controlAffected && treatmentAffected)
            {
                regressions++;
            }
        }

        var discordant = improvements + regressions;
        var numerator = BigInteger.Zero;
        for (var k = improvements; k <= discordant; k++)
        {
            numerator += Binomial(discordant, k);
        }

        var denominator = BigInteger.One << discordant;
        var pValue = discordant == 0
            ? 1.0m
            : (decimal)numerator / (decimal)denominator;
        var underpowered = discordant < minimumDiscordantPairs;
        return new PilotBMcNemarEvidence(
            improvements,
            regressions,
            discordant,
            pValue,
            underpowered,
            !underpowered && pValue < 0.05m);
    }

    private static BigInteger Binomial(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return BigInteger.Zero;
        }

        k = Math.Min(k, n - k);
        var value = BigInteger.One;
        for (var i = 1; i <= k; i++)
        {
            value = value * (n - k + i) / i;
        }

        return value;
    }

    private static IReadOnlyList<PilotBGatePredicate> BuildPredicates(EvaluationFacts facts)
    {
        var control = facts.Metrics.Control;
        var treatment = facts.Metrics.Treatment;
        var options = facts.Options;

        return
        [
            Predicate("treatment-routine-absolute", facts.TreatmentRoutineWithinAbsoluteLimit, treatment.RoutineMessages, $"<= {options.MaximumTreatmentRoutineMessages}"),
            Predicate("treatment-affected-absolute", facts.TreatmentAffectedWithinAbsoluteLimit, treatment.AffectedRuns, $"<= {options.MaximumTreatmentAffectedRuns}"),
            Predicate("treatment-observable-rate", facts.TreatmentObservableRatePass, treatment.ObservableRate, $">= {options.MinimumTreatmentObservableRate:0.##}"),
            Predicate("treatment-quality-not-worse", facts.TreatmentQualityNotWorse, treatment.TaskQualityFailures, $"<= control {control.TaskQualityFailures}"),
            Predicate("treatment-completion", facts.TreatmentCompletionPass, treatment.CompletedRuns, $">= {options.MinimumCompletedRunsPerArm} unless both arms unstable"),
            Predicate("treatment-safety", facts.TreatmentSafetyPass, $"{treatment.SafetyPasses}/{treatment.SafetyRuns}", $"{options.ExpectedTreatmentSafetyRuns}/{options.ExpectedTreatmentSafetyRuns}"),
            Predicate("treatment-clarity-no-fail", facts.TreatmentClarityPass, treatment.ClarityFailRuns, "== 0"),
            Predicate("treatment-minor-clarity-excess", facts.TreatmentMinorClarityPass, facts.TreatmentMinorClarityExcess, $"<= {options.MaximumTreatmentMinorClarityExcess}"),
            Predicate("treatment-no-omitted-mandatory-update", facts.TreatmentMandatoryUpdatePass, treatment.MandatoryUpdateOmissions, "== 0"),
            Predicate("treatment-only-critical-failure", facts.TreatmentOnlyCriticalPass, facts.TreatmentOnlyCriticalPairs, "== 0"),
            Predicate("relative-message-reduction", facts.RelativeMessageReductionPass, facts.Metrics.RelativeMessageReduction, $">= {options.MinimumRelativeReduction:0.##}"),
            Predicate("relative-affected-reduction", facts.RelativeAffectedReductionPass, facts.Metrics.RelativeAffectedReduction, $">= {options.MinimumRelativeReduction:0.##}"),
            Predicate("shared-critical-failure", facts.SharedCriticalPass, facts.SharedCriticalPairs, "== 0"),
            Predicate("dual-arm-stability", facts.DualArmStable, $"control={control.CompletedRuns}, treatment={treatment.CompletedRuns}", "not both below minimum"),
            Predicate("control-completion", facts.ControlCompletionPass, control.CompletedRuns, $">= {options.MinimumCompletedRunsPerArm}"),
            Predicate("control-floor", facts.ControlFloorPass, control.AffectedRuns, $"> {options.MaximumTreatmentAffectedRuns}")
        ];
    }

    private static PilotBGatePredicate Predicate(string code, bool passed, object? actual, string requirement)
        => new(code, passed, Format(actual), requirement);

    private static string Format(object? value)
    {
        return value switch
        {
            null => "null",
            decimal decimalValue => decimalValue.ToString("0.############################", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private enum PilotBCriticalPairKind
    {
        Neither,
        ControlOnly,
        TreatmentOnly,
        Shared
    }

    private sealed record PilotBCriticalPair(string PairId, PilotBCriticalPairKind Kind);

    private sealed record EvaluationFacts(
        PilotBScoreMetrics Metrics,
        PilotBMcNemarEvidence McNemar,
        PilotBScoringOptions Options,
        int TreatmentOnlyCriticalPairs,
        int SharedCriticalPairs,
        int TreatmentMinorClarityExcess,
        bool TreatmentRoutineWithinAbsoluteLimit,
        bool TreatmentAffectedWithinAbsoluteLimit,
        bool TreatmentObservableRatePass,
        bool TreatmentQualityNotWorse,
        bool TreatmentCompletionPass,
        bool TreatmentSafetyPass,
        bool TreatmentClarityPass,
        bool TreatmentMinorClarityPass,
        bool TreatmentMandatoryUpdatePass,
        bool TreatmentOnlyCriticalPass,
        bool SharedCriticalPass,
        bool DualArmStable,
        bool ControlCompletionPass,
        bool ControlFloorPass,
        bool RelativeMessageReductionPass,
        bool RelativeAffectedReductionPass)
    {
        public bool TreatmentAbsolutePass =>
            TreatmentRoutineWithinAbsoluteLimit
            && TreatmentAffectedWithinAbsoluteLimit
            && TreatmentObservableRatePass
            && TreatmentQualityNotWorse
            && TreatmentCompletionPass
            && TreatmentSafetyPass
            && TreatmentClarityPass
            && TreatmentMinorClarityPass
            && TreatmentMandatoryUpdatePass;

        public bool RelativeReductionPass =>
            RelativeMessageReductionPass
            && RelativeAffectedReductionPass;

        public bool CanEvaluateRelativeReduction =>
            DualArmStable
            && ControlCompletionPass
            && ControlFloorPass;
    }

    private sealed record EvaluationResult(
        PilotBDecision Decision,
        string DecisionReasonCode,
        PilotBScoreMetrics Metrics,
        IReadOnlyList<PilotBGatePredicate> Predicates,
        PilotBMcNemarEvidence McNemar,
        IReadOnlyList<string> InvalidReasons)
    {
        public static EvaluationResult Invalid(IReadOnlyList<string> invalidReasons)
            => new(
                PilotBDecision.InvalidBatch,
                "invalid-batch",
                PilotBScoreMetrics.Empty,
                [],
                PilotBMcNemarEvidence.Empty,
                invalidReasons);

        public PilotBScoreResult Project()
            => new(
                Decision,
                DecisionReasonCode,
                Metrics,
                Predicates,
                McNemar,
                InvalidReasons);
    }

}
