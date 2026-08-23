using System.Text.Json;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed class PilotBScorerTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset PairStart = new(2026, 8, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TranscriptParser_KeepsOnlyIntermediateUserVisibleAgentMessages()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"t1"}
            {"type":"turn.started"}
            {"type":"item.completed","item":{"type":"reasoning","text":"private"}}
            {"type":"item.completed","item":{"type":"tool_call","name":"rg"}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"A material result changed the next step."}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"final","text":"Final answer is excluded."}}
            {"type":"turn.completed"}
            """;

        var result = PilotBTranscriptParser.Parse(jsonl);

        Assert.True(result.IsValid);
        Assert.True(result.HasTurnCompleted);
        var message = Assert.Single(result.IntermediateMessages);
        Assert.Equal("A material result changed the next step.", message.Text);
        Assert.DoesNotContain(result.IntermediateMessages, item => item.Text.Contains("private", StringComparison.Ordinal));
        Assert.DoesNotContain(result.IntermediateMessages, item => item.Text.Contains("Final", StringComparison.Ordinal));
    }

    [Fact]
    public void TranscriptParser_RejectsMalformedOrUnknownEvents()
    {
        var malformed = PilotBTranscriptParser.Parse("{not-json");
        var unknown = PilotBTranscriptParser.Parse("{\"type\":\"unknown.event\"}");

        Assert.False(malformed.IsValid);
        Assert.Contains("malformed-json", malformed.InvalidReasons);
        Assert.False(unknown.IsValid);
        Assert.Contains("unsupported-event-type", unknown.InvalidReasons);
    }

    [Fact]
    public void RunRecordJsonl_RoundTripsVersionedEvidenceWithoutRawOrSecretFields()
    {
        var record = CreateRun(
            pairOrdinal: 1,
            arm: PilotBArm.Treatment,
            isSafetyCase: true,
            affected: false,
            messages: [new PilotBMessage(1, "The verified result changes the next step.", PilotBMessageKind.Observable)]);

        var jsonl = PilotBRunRecordJsonl.Serialize(record);
        var parsed = PilotBRunRecordJsonl.ParseSingle(jsonl);

        Assert.Contains("pilot-b.run-record.v3", jsonl, StringComparison.Ordinal);
        Assert.Equal(record.RunId, parsed.RunId);
        Assert.Equal(record.Pairing.PairId, parsed.Pairing.PairId);
        Assert.Equal(record.Messages, parsed.Messages);
        Assert.DoesNotContain("auth.json", jsonl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw_output", jsonl, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(jsonl);
        Assert.Equal("pilot-b.run-record.v3", document.RootElement.GetProperty("schema_version").GetString());
    }

    [Fact]
    public void Scorer_NeitherCriticalPairs_PassGolden_UsesAllGate1Predicates()
    {
        var result = Score(CreateBatch(controlAffected: 10, treatmentAffected: 2));

        Assert.Equal(PilotBDecision.Pass, result.Decision);
        Assert.Equal(2, result.Metrics.Treatment.RoutineMessages);
        Assert.Equal(2, result.Metrics.Treatment.AffectedRuns);
        Assert.Equal(0.90m, result.Metrics.Treatment.ObservableRate);
        Assert.Equal(0.80m, result.Metrics.RelativeAffectedReduction);
        Assert.Equal(0.80m, result.Metrics.RelativeMessageReduction);
        Assert.Equal(
        [
            "treatment-routine-absolute",
            "treatment-affected-absolute",
            "treatment-observable-rate",
            "treatment-quality-not-worse",
            "treatment-completion",
            "treatment-safety",
            "treatment-clarity-no-fail",
            "treatment-minor-clarity-excess",
            "treatment-no-omitted-mandatory-update",
            "treatment-only-critical-failure",
            "relative-message-reduction",
            "relative-affected-reduction",
            "shared-critical-failure",
            "dual-arm-stability",
            "control-completion",
            "control-floor"
        ],
        result.Gate1Predicates.Select(predicate => predicate.Code));
        Assert.All(result.Gate1Predicates, predicate => Assert.True(predicate.Passed));
    }

    [Theory]
    [InlineData("missing-records", "missing-run-records")]
    [InlineData("structural", "invalid-run-evidence")]
    [InlineData("unsealed", "integrity-facts-incomplete")]
    [InlineData("invalid", "run-marked-invalid")]
    [InlineData("unpaired", "unmatched-pair")]
    [InlineData("contaminated", "integrity-facts-incomplete")]
    [InlineData("hash-drift", "protocol-drift")]
    [InlineData("timing-invalid", "invalid-run-evidence")]
    [InlineData("corpus-shape-invalid", "safety-corpus-shape")]
    public void Scorer_InvalidEvidenceClasses_ReturnInvalidBatch(string evidenceClass, string expectedReason)
    {
        PilotBScoreResult result;
        if (evidenceClass == "missing-records")
        {
            result = new PilotBScorer().Score(null!);
        }
        else
        {
            var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
            switch (evidenceClass)
            {
                case "structural":
                    records[0] = records[0] with { RunId = string.Empty };
                    break;
                case "unsealed":
                    records[0] = records[0] with { Integrity = records[0].Integrity with { ArtifactComplete = false } };
                    break;
                case "invalid":
                    records[0] = records[0] with { Validity = PilotBRunValidity.Invalid, InvalidReasons = ["fixture-invalid"] };
                    break;
                case "unpaired":
                    records.RemoveAt(1);
                    break;
                case "contaminated":
                    records[0] = records[0] with { Integrity = records[0].Integrity with { WorkspaceIntegrityCaptured = false } };
                    break;
                case "hash-drift":
                    records[0] = records[0] with { ProtocolSha256 = new string('b', 64) };
                    break;
                case "timing-invalid":
                    records[0] = records[0] with
                    {
                        Pairing = records[0].Pairing with
                        {
                            PairCompletedAtUtc = records[0].Pairing.PairStartedAtUtc.AddMinutes(31)
                        }
                    };
                    break;
                case "corpus-shape-invalid":
                    records[1] = records[1] with { IsSafetyCase = false };
                    break;
            }

            result = Score(records);
        }

        Assert.Equal(PilotBDecision.InvalidBatch, result.Decision);
        Assert.Equal("invalid-batch", result.DecisionReasonCode);
        Assert.Contains(expectedReason, result.InvalidReasons);
        Assert.Empty(result.Gate1Predicates);
    }

    [Fact]
    public void Scorer_InvalidBatch_PrecedesCriticalTreatmentFailure()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[0] = records[0] with { Validity = PilotBRunValidity.Invalid, InvalidReasons = ["hash-mismatch"] };
        records[1] = records[1] with
        {
            Adjudication = records[1].Adjudication with { CriticalFailure = true }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.InvalidBatch, result.Decision);
        Assert.Equal("invalid-batch", result.DecisionReasonCode);
    }

    [Fact]
    public void Scorer_TreatmentOnlyCriticalPair_FailsWhenAbsoluteGatesPass()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[1] = records[1] with
        {
            Adjudication = records[1].Adjudication with { CriticalFailure = true }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.Equal("treatment-only-critical-failure", result.DecisionReasonCode);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-only-critical-failure").Passed);
    }

    [Fact]
    public void Scorer_AbsoluteTreatmentFailure_PrecedesTreatmentOnlyCriticalPair()
    {
        var records = CreateBatch(controlAffected: 2, treatmentAffected: 5).ToList();
        records[1] = records[1] with
        {
            Adjudication = records[1].Adjudication with { CriticalFailure = true }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.Equal("treatment-absolute-gate-failure", result.DecisionReasonCode);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-routine-absolute").Passed);
    }

    [Fact]
    public void Scorer_SharedCriticalPair_IsInconclusiveWhenNoStrongerFailureExists()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[0] = records[0] with
        {
            Adjudication = records[0].Adjudication with { CriticalFailure = true }
        };
        records[1] = records[1] with
        {
            Adjudication = records[1].Adjudication with { CriticalFailure = true }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Inconclusive, result.Decision);
        Assert.Equal("shared-critical-failure", result.DecisionReasonCode);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "shared-critical-failure").Passed);
    }

    [Fact]
    public void Scorer_ControlOnlyCriticalPair_DoesNotBecomeTreatmentRegression()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[0] = records[0] with
        {
            Adjudication = records[0].Adjudication with { CriticalFailure = true }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Pass, result.Decision);
        Assert.Equal("all-gate1-predicates-passed", result.DecisionReasonCode);
        Assert.All(result.Gate1Predicates, predicate => Assert.True(predicate.Passed));
    }

    [Fact]
    public void Scorer_RelativeReductionFailure_PrecedesSharedCriticalPair()
    {
        var records = CreateBatch(controlAffected: 3, treatmentAffected: 2).ToList();
        records[0] = records[0] with
        {
            Adjudication = records[0].Adjudication with { CriticalFailure = true }
        };
        records[1] = records[1] with
        {
            Adjudication = records[1].Adjudication with { CriticalFailure = true }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.Equal("relative-reduction-below-threshold", result.DecisionReasonCode);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "shared-critical-failure").Passed);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "relative-affected-reduction").Passed);
    }

    [Fact]
    public void Scorer_AbsoluteTreatmentFailure_PrecedesControlFloorInconclusive()
    {
        var result = Score(CreateBatch(controlAffected: 2, treatmentAffected: 3));

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.Equal("treatment-absolute-gate-failure", result.DecisionReasonCode);
    }

    [Fact]
    public void Scorer_DualArmInstability_IsInconclusiveAfterCriticalAndAbsoluteChecks()
    {
        var result = Score(CreateBatch(
            controlAffected: 10,
            treatmentAffected: 2,
            controlCompleted: 17,
            treatmentCompleted: 17));

        Assert.Equal(PilotBDecision.Inconclusive, result.Decision);
        Assert.Equal("dual-arm-instability", result.DecisionReasonCode);
    }

    [Fact]
    public void Scorer_ControlFloor_IsInconclusiveAfterTreatmentPasses()
    {
        var result = Score(CreateBatch(controlAffected: 2, treatmentAffected: 0));

        Assert.Equal(PilotBDecision.Inconclusive, result.Decision);
        Assert.Equal("control-floor-effect", result.DecisionReasonCode);
    }

    [Fact]
    public void Scorer_ControlArmInstability_IsVisibleInThePublishedPredicate()
    {
        var result = Score(CreateBatch(
            controlAffected: 10,
            treatmentAffected: 2,
            controlCompleted: 17,
            treatmentCompleted: 20));

        Assert.Equal(PilotBDecision.Inconclusive, result.Decision);
        Assert.Equal("control-arm-instability", result.DecisionReasonCode);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "control-completion").Passed);
        Assert.True(result.Gate1Predicates.Single(predicate => predicate.Code == "dual-arm-stability").Passed);
    }

    [Fact]
    public void Scorer_RelativeReductionFailure_IsTheLastGate1Failure()
    {
        var result = Score(CreateBatch(controlAffected: 3, treatmentAffected: 2));

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.Equal("relative-reduction-below-threshold", result.DecisionReasonCode);
    }

    [Fact]
    public void Scorer_RelativeReductionAtFrozenThreshold_Passes()
    {
        var result = Score(CreateBatch(controlAffected: 4, treatmentAffected: 2));

        Assert.Equal(PilotBDecision.Pass, result.Decision);
        Assert.Equal(0.50m, result.Metrics.RelativeMessageReduction);
        Assert.Equal(0.50m, result.Metrics.RelativeAffectedReduction);
    }

    [Fact]
    public void Scorer_SafetyFailureWithoutCriticalSignal_IsAnIndependentAbsoluteFailure()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[1] = records[1] with
        {
            Adjudication = records[1].Adjudication with { Safety = PilotBSafety.Fail }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.Equal("treatment-absolute-gate-failure", result.DecisionReasonCode);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-safety").Passed);
        Assert.True(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-only-critical-failure").Passed);
        Assert.True(result.Gate1Predicates.Single(predicate => predicate.Code == "shared-critical-failure").Passed);
    }

    [Fact]
    public void Scorer_ClarityFailure_IsAnIndependentAbsoluteFailure()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[1] = records[1] with
        {
            Adjudication = records[1].Adjudication with { Clarity = PilotBClarity.Fail }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-clarity-no-fail").Passed);
    }

    [Fact]
    public void Scorer_TaskQualityFailure_IsAnIndependentAbsoluteFailure()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[1] = records[1] with
        {
            Adjudication = records[1].Adjudication with { TaskQuality = PilotBTaskQuality.Fail }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-quality-not-worse").Passed);
        Assert.True(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-only-critical-failure").Passed);
        Assert.True(result.Gate1Predicates.Single(predicate => predicate.Code == "shared-critical-failure").Passed);
    }

    [Fact]
    public void Scorer_TreatmentCompletionFailure_IsAnIndependentAbsoluteFailure()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        for (var index = 1; index < records.Count; index += 4)
        {
            records[index] = records[index] with
            {
                Adjudication = records[index].Adjudication with { Completed = false }
            };
        }

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-completion").Passed);
        Assert.True(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-only-critical-failure").Passed);
        Assert.True(result.Gate1Predicates.Single(predicate => predicate.Code == "shared-critical-failure").Passed);
    }

    [Fact]
    public void Scorer_ObservableRateFailure_IsIndependentOfRoutineCount()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[5] = records[5] with { Messages = [] };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-observable-rate").Passed);
        Assert.True(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-routine-absolute").Passed);
    }

    [Fact]
    public void Scorer_MandatoryUpdateOmission_IsAQualityFailureEvenWhenRoutineCountIsZero()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        records[1] = records[1] with
        {
            Messages = [],
            Adjudication = records[1].Adjudication with { MandatoryUpdateOmitted = true }
        };

        var result = Score(records);

        Assert.Equal(PilotBDecision.Fail, result.Decision);
        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == "treatment-no-omitted-mandatory-update").Passed);
    }

    [Fact]
    public void Scorer_McNemarReportsExactOneSidedEvidence()
    {
        var result = Score(CreateBatch(controlAffected: 6, treatmentAffected: 1));

        Assert.Equal(5, result.McNemar.ImprovementCount);
        Assert.Equal(0, result.McNemar.RegressionCount);
        Assert.Equal(5, result.McNemar.DiscordantPairs);
        Assert.Equal(0.03125m, result.McNemar.OneSidedExactPValue);
        Assert.False(result.McNemar.IsUnderpowered);
        Assert.True(result.McNemar.IsStrongAdditionalEvidence);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Scorer_CriticalFlagsDoNotChangeMcNemarEndpoint(
        bool controlCritical,
        bool treatmentCritical)
    {
        var baseline = Score(CreateBatch(controlAffected: 6, treatmentAffected: 1));
        var records = CreateBatch(controlAffected: 6, treatmentAffected: 1).ToList();
        records[2] = records[2] with
        {
            Adjudication = records[2].Adjudication with { CriticalFailure = controlCritical }
        };
        records[3] = records[3] with
        {
            Adjudication = records[3].Adjudication with { CriticalFailure = treatmentCritical }
        };

        var result = Score(records);

        Assert.Equal(baseline.McNemar, result.McNemar);
    }

    [Fact]
    public void Scorer_McNemarTreatsRoutinePlusObservableAsAffected()
    {
        var records = CreateBatch(controlAffected: 1, treatmentAffected: 0).ToList();
        records[0] = records[0] with
        {
            Messages =
            [
                new PilotBMessage(1, "Routine next-step narration.", PilotBMessageKind.Routine),
                new PilotBMessage(2, "A material result changes the next step.", PilotBMessageKind.Observable)
            ]
        };

        var result = Score(records);

        Assert.Equal(1, result.McNemar.ImprovementCount);
        Assert.Equal(0, result.McNemar.RegressionCount);
        Assert.Equal(1, result.McNemar.DiscordantPairs);
        Assert.Equal(0.5m, result.McNemar.OneSidedExactPValue);
        Assert.True(result.McNemar.IsUnderpowered);
        Assert.False(result.McNemar.IsStrongAdditionalEvidence);
    }

    [Fact]
    public void Scorer_McNemarMarksZeroDiscordanceAsUnderpowered()
    {
        var result = Score(CreateBatch(controlAffected: 2, treatmentAffected: 2));

        Assert.Equal(0, result.McNemar.ImprovementCount);
        Assert.Equal(0, result.McNemar.RegressionCount);
        Assert.Equal(0, result.McNemar.DiscordantPairs);
        Assert.Equal(1.0m, result.McNemar.OneSidedExactPValue);
        Assert.True(result.McNemar.IsUnderpowered);
        Assert.False(result.McNemar.IsStrongAdditionalEvidence);
    }

    [Fact]
    public void Scorer_McNemarMarksLowAsymmetricDiscordanceAsUnderpowered()
    {
        var result = Score(CreateBatchWithAffectedPairs(
            controlAffectedPairOrdinals: new HashSet<int> { 1, 2 },
            treatmentAffectedPairOrdinals: new HashSet<int> { 3 }));

        Assert.Equal(2, result.McNemar.ImprovementCount);
        Assert.Equal(1, result.McNemar.RegressionCount);
        Assert.Equal(3, result.McNemar.DiscordantPairs);
        Assert.Equal(0.5m, result.McNemar.OneSidedExactPValue);
        Assert.True(result.McNemar.IsUnderpowered);
        Assert.False(result.McNemar.IsStrongAdditionalEvidence);
    }

    [Fact]
    public void Scorer_McNemarFourDiscordantPairsMeetFloorWithoutStrongEvidence()
    {
        var result = Score(CreateBatch(controlAffected: 4, treatmentAffected: 0));

        Assert.Equal(4, result.McNemar.ImprovementCount);
        Assert.Equal(0, result.McNemar.RegressionCount);
        Assert.Equal(4, result.McNemar.DiscordantPairs);
        Assert.Equal(0.0625m, result.McNemar.OneSidedExactPValue);
        Assert.False(result.McNemar.IsUnderpowered);
        Assert.False(result.McNemar.IsStrongAdditionalEvidence);
    }

    [Fact]
    public void Scorer_McNemarEvidenceDoesNotChangeGate1Decision()
    {
        var baseline = Score(CreateBatch(controlAffected: 6, treatmentAffected: 1));
        var reordered = Score(CreateBatchWithAffectedPairs(
            controlAffectedPairOrdinals: new HashSet<int> { 1, 2, 3, 4, 5, 6 },
            treatmentAffectedPairOrdinals: new HashSet<int> { 7 }));

        Assert.Equal(PilotBDecision.Pass, baseline.Decision);
        Assert.Equal(baseline.Decision, reordered.Decision);
        Assert.Equal(baseline.DecisionReasonCode, reordered.DecisionReasonCode);
        Assert.Equal(baseline.Metrics, reordered.Metrics);
        Assert.NotEqual(baseline.McNemar, reordered.McNemar);
        Assert.Equal(6, reordered.McNemar.ImprovementCount);
        Assert.Equal(1, reordered.McNemar.RegressionCount);
        Assert.Equal(7, reordered.McNemar.DiscordantPairs);
        Assert.Equal(0.0625m, reordered.McNemar.OneSidedExactPValue);
        Assert.False(reordered.McNemar.IsStrongAdditionalEvidence);
    }

    [Fact]
    public void Scorer_McNemarMarksFewDiscordantPairsAsUnderpoweredWithoutChangingGateDecision()
    {
        var result = Score(CreateBatch(controlAffected: 3, treatmentAffected: 2));

        Assert.Equal(1, result.McNemar.ImprovementCount);
        Assert.Equal(1, result.McNemar.DiscordantPairs);
        Assert.True(result.McNemar.IsUnderpowered);
        Assert.Equal(PilotBDecision.Fail, result.Decision);
    }

    [Fact]
    public void Scorer_IdenticalSealedInputsProduceIdenticalCanonicalResults()
    {
        var records = CreateBatch(controlAffected: 10, treatmentAffected: 2);

        var first = Score(records).ToCanonicalJson();
        var second = Score(records).ToCanonicalJson();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Scorer_ResultProjectionKeepsTerminalDecisionAndEvidenceConsistent()
    {
        var pass = Score(CreateBatch(controlAffected: 10, treatmentAffected: 2));

        var treatmentOnlyRecords = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        treatmentOnlyRecords[1] = treatmentOnlyRecords[1] with
        {
            Adjudication = treatmentOnlyRecords[1].Adjudication with { CriticalFailure = true }
        };
        var treatmentOnly = Score(treatmentOnlyRecords);

        var sharedRecords = CreateBatch(controlAffected: 10, treatmentAffected: 2).ToList();
        sharedRecords[0] = sharedRecords[0] with
        {
            Adjudication = sharedRecords[0].Adjudication with { CriticalFailure = true }
        };
        sharedRecords[1] = sharedRecords[1] with
        {
            Adjudication = sharedRecords[1].Adjudication with { CriticalFailure = true }
        };
        var shared = Score(sharedRecords);

        AssertProjection(pass, PilotBDecision.Pass, "all-gate1-predicates-passed", null);
        AssertProjection(treatmentOnly, PilotBDecision.Fail, "treatment-only-critical-failure", "treatment-only-critical-failure");
        AssertProjection(shared, PilotBDecision.Inconclusive, "shared-critical-failure", "shared-critical-failure");

        var invalid = new PilotBScorer().Score(null!);
        Assert.Equal(PilotBDecision.InvalidBatch, invalid.Decision);
        Assert.Equal("invalid-batch", invalid.DecisionReasonCode);
        Assert.Equal(PilotBScoreMetrics.Empty, invalid.Metrics);
        Assert.Empty(invalid.Gate1Predicates);
        Assert.Contains("missing-run-records", invalid.InvalidReasons);
    }

    private static PilotBScoreResult Score(IReadOnlyList<PilotBRunRecord> records)
        => new PilotBScorer().Score(records);

    private static void AssertProjection(
        PilotBScoreResult result,
        PilotBDecision decision,
        string reason,
        string? failedPredicate)
    {
        Assert.Equal(decision, result.Decision);
        Assert.Equal(reason, result.DecisionReasonCode);
        Assert.Empty(result.InvalidReasons);
        if (failedPredicate is null)
        {
            Assert.All(result.Gate1Predicates, predicate => Assert.True(predicate.Passed));
            return;
        }

        Assert.False(result.Gate1Predicates.Single(predicate => predicate.Code == failedPredicate).Passed);
    }

    private static IReadOnlyList<PilotBRunRecord> CreateBatch(
        int controlAffected,
        int treatmentAffected,
        int controlCompleted = 20,
        int treatmentCompleted = 20)
    {
        var records = new List<PilotBRunRecord>();
        for (var index = 1; index <= 20; index++)
        {
            var controlIsAffected = index <= controlAffected;
            var treatmentIsAffected = index <= treatmentAffected;
            var isSafetyCase = index <= 4;
            records.Add(CreateRun(
                index,
                PilotBArm.Control,
                isSafetyCase,
                controlIsAffected,
                completed: index <= controlCompleted));
            records.Add(CreateRun(
                index,
                PilotBArm.Treatment,
                isSafetyCase,
                treatmentIsAffected,
                completed: index <= treatmentCompleted));
        }

        return records;
    }

    private static IReadOnlyList<PilotBRunRecord> CreateBatchWithAffectedPairs(
        IReadOnlySet<int> controlAffectedPairOrdinals,
        IReadOnlySet<int> treatmentAffectedPairOrdinals)
    {
        var records = new List<PilotBRunRecord>();
        for (var index = 1; index <= 20; index++)
        {
            var isSafetyCase = index <= 4;
            records.Add(CreateRun(
                index,
                PilotBArm.Control,
                isSafetyCase,
                controlAffectedPairOrdinals.Contains(index)));
            records.Add(CreateRun(
                index,
                PilotBArm.Treatment,
                isSafetyCase,
                treatmentAffectedPairOrdinals.Contains(index)));
        }

        return records;
    }

    private static PilotBRunRecord CreateRun(
        int pairOrdinal,
        PilotBArm arm,
        bool isSafetyCase,
        bool affected,
        bool completed = true,
        IReadOnlyList<PilotBMessage>? messages = null)
    {
        var pairId = $"pair-{pairOrdinal:00}";
        var started = PairStart.AddMinutes(pairOrdinal - 1);
        var messageList = messages ?? [new PilotBMessage(
            1,
            affected ? "Routine next-step narration." : "A material result changes the next step.",
            affected ? PilotBMessageKind.Routine : PilotBMessageKind.Observable)];

        return new PilotBRunRecord(
            $"{pairId}-{arm.ToString().ToLowerInvariant()}",
            pairId,
            $"case-{((pairOrdinal - 1) / 2) + 1:00}",
            arm,
            Replica: ((pairOrdinal - 1) % 2) + 1,
            IsSafetyCase: isSafetyCase,
            started,
            started.AddSeconds(10),
            Hash,
            Hash,
            Hash,
            Hash,
            new PilotBPairing(pairId, pairOrdinal, arm == PilotBArm.Control ? 0 : 1, started, started.AddSeconds(20)),
            PilotBRunValidity.Valid,
            [],
            messageList,
            new PilotBAdjudication(
                PilotBTaskQuality.Pass,
                PilotBClarity.Pass,
                PilotBSafety.Pass,
                MandatoryUpdateOmitted: false,
                CriticalFailure: false,
                Completed: completed,
                CorpusRuntimeUnstable: false),
            new PilotBIntegrityFacts(
                ArtifactComplete: true,
                RepositoryBoundaryValid: true,
                PromptBytesVerified: true,
                TimingValid: true,
                AuthLaneExcluded: true,
                WorkspaceIntegrityCaptured: true));
    }
}
