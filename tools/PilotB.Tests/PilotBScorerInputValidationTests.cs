using System.Text.Json.Nodes;
using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed partial class PilotBScorerTests
{
    [Theory]
    [InlineData("integrity-null", "invalid-run-evidence")]
    [InlineData("pairing-null", "invalid-run-evidence")]
    [InlineData("adjudication-null", "invalid-run-evidence")]
    [InlineData("messages-null", "invalid-run-evidence")]
    [InlineData("reasons-null", "invalid-run-evidence")]
    [InlineData("message-null", "unsupported-primary-event")]
    [InlineData("order-negative", "invalid-run-evidence")]
    [InlineData("order-two", "invalid-run-evidence")]
    [InlineData("sequence-duplicate", "unsupported-primary-event")]
    [InlineData("sequence-decreasing", "unsupported-primary-event")]
    [InlineData("arm-unknown", "invalid-run-evidence")]
    [InlineData("quality-unknown", "invalid-run-evidence")]
    [InlineData("clarity-unknown", "invalid-run-evidence")]
    [InlineData("safety-unknown", "invalid-run-evidence")]
    [InlineData("validity-unknown", "run-marked-invalid")]
    [InlineData("kind-unknown", "unsupported-primary-event")]
    public void Scorer_MalformedTypedInputReturnsInvalidBatch(string mutation, string reason)
    {
        var records = CreateBatch(10, 2).ToArray();
        Assert.Equal(PilotBDecision.Pass, Score(records).Decision);
        var record = records[1];
        records[1] = mutation switch
        {
            "integrity-null" => record with { Integrity = null! },
            "pairing-null" => record with { Pairing = null! },
            "adjudication-null" => record with { Adjudication = null! },
            "messages-null" => record with { Messages = null! },
            "reasons-null" => record with { InvalidReasons = null! },
            "message-null" => record with { Messages = [null!] },
            "order-negative" => record with { Pairing = record.Pairing with { ArmOrderIndex = -1 } },
            "order-two" => record with { Pairing = record.Pairing with { ArmOrderIndex = 2 } },
            "sequence-duplicate" => record with { Messages = [record.Messages[0], record.Messages[0]] },
            "sequence-decreasing" => record with { Messages = [record.Messages[0] with { Sequence = 2 }, record.Messages[0]] },
            "arm-unknown" => record with { Arm = (PilotBArm)999 },
            "quality-unknown" => record with { Adjudication = record.Adjudication with { TaskQuality = (PilotBTaskQuality)999 } },
            "clarity-unknown" => record with { Adjudication = record.Adjudication with { Clarity = (PilotBClarity)999 } },
            "safety-unknown" => record with { Adjudication = record.Adjudication with { Safety = (PilotBSafety)999 } },
            "validity-unknown" => record with { Validity = (PilotBRunValidity)999 },
            "kind-unknown" => record with { Messages = [record.Messages[0] with { Kind = (PilotBMessageKind)999 }] },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        var result = Score(records);
        Assert.Equal(PilotBDecision.InvalidBatch, result.Decision);
        Assert.Equal("invalid-batch", result.DecisionReasonCode);
        Assert.Contains(reason, result.InvalidReasons);
        Assert.Empty(result.Gate1Predicates);
        Assert.Equal(PilotBScoreMetrics.Empty, result.Metrics);
    }

    [Fact]
    public void Scorer_PreservesOrderedCommentaryWithGapsAfterFinalExclusion()
    {
        var records = CreateBatch(10, 2).ToArray();
        records[1] = records[1] with
        {
            Messages = [records[1].Messages[0] with { Sequence = 2 },
                new PilotBMessage(4, "Later observable result.", PilotBMessageKind.Observable)]
        };
        var parsed = PilotBRunRecordJsonl.ParseSingle(PilotBRunRecordJsonl.Serialize(records[1]));
        Assert.Equal(new[] { 2, 4 }, parsed.Messages.Select(message => message.Sequence));
        records[1] = parsed;
        Assert.Equal(PilotBDecision.Pass, Score(records).Decision);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("pairing")]
    [InlineData("adjudication")]
    [InlineData("integrity")]
    [InlineData("message")]
    public void RunRecordJsonl_RequiresExactUniqueFieldsAtEveryObject(string scope)
    {
        var json = PilotBRunRecordJsonl.Serialize(CreateBatch(10, 2)[0]);
        var root = JsonNode.Parse(json)!;
        var target = scope switch
        {
            "root" => root.AsObject(),
            "message" => root["messages"]![0]!.AsObject(),
            _ => root[scope]!.AsObject()
        };
        var original = target.ToJsonString();
        var firstProperty = target.First();
        var duplicate = "{" + System.Text.Json.JsonSerializer.Serialize(firstProperty.Key)
            + ":" + firstProperty.Value!.ToJsonString() + "," + original[1..];
        var canonicalRoot = root.ToJsonString();
        Assert.Throws<FormatException>(() => PilotBRunRecordJsonl.ParseSingle(
            canonicalRoot.Replace(original, duplicate, StringComparison.Ordinal)));

        target["unexpected"] = true;
        Assert.Throws<FormatException>(() => PilotBRunRecordJsonl.ParseSingle(root.ToJsonString()));
        target.Remove("unexpected");
        target.Remove(firstProperty.Key);
        Assert.Throws<FormatException>(() => PilotBRunRecordJsonl.ParseSingle(root.ToJsonString()));
    }

    [Theory]
    [InlineData("root", "null")]
    [InlineData("root", "[]")]
    [InlineData("messages", "[null]")]
    [InlineData("messages", "[1]")]
    [InlineData("invalid_reasons", "[1]")]
    [InlineData("replica", "\"1\"")]
    public void RunRecordJsonl_MalformedShapeProducesFormatException(string field, string jsonValue)
    {
        var root = JsonNode.Parse(PilotBRunRecordJsonl.Serialize(CreateBatch(10, 2)[0]))!;
        if (field == "root")
        {
            Assert.Throws<FormatException>(() => PilotBRunRecordJsonl.ParseSingle(jsonValue));
            return;
        }

        root[field] = JsonNode.Parse(jsonValue);
        Assert.Throws<FormatException>(() => PilotBRunRecordJsonl.ParseSingle(root.ToJsonString()));
    }

    [Theory]
    [InlineData("arm", "CONTROL")]
    [InlineData("arm", "0")]
    [InlineData("arm", "999")]
    [InlineData("arm", " control")]
    [InlineData("validity", "VALID")]
    [InlineData("validity", "0")]
    [InlineData("kind", "ROUTINE")]
    [InlineData("kind", "999")]
    [InlineData("task_quality", "999")]
    [InlineData("task_quality", "Pass")]
    [InlineData("clarity", "1")]
    [InlineData("safety", "NotRated")]
    [InlineData("safety", "999")]
    public void RunRecordJsonl_RejectsNonCanonicalEnumValues(string field, string value)
    {
        var root = JsonNode.Parse(PilotBRunRecordJsonl.Serialize(CreateBatch(10, 2)[0]))!;
        var target = field switch
        {
            "kind" => root["messages"]![0]!,
            "task_quality" or "clarity" or "safety" => root["adjudication"]!,
            _ => root
        };
        target[field] = value;

        Assert.Throws<FormatException>(() => PilotBRunRecordJsonl.ParseSingle(root.ToJsonString()));
    }
}
