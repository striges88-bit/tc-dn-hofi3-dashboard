using CryptoIndicatorApp.PilotB;

namespace CryptoIndicatorApp.PilotB.Tests;

public sealed class PilotBTranscriptParserTests
{
    [Fact]
    public void Parser_ValidatesLifecycleAndRetainsOnlyCompletedSemanticMessages()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"turn.started","turn_id":"turn-1"}
            {"type":"item.started","item":{"type":"agent_message"}}
            {"type":"item.updated","item":{"type":"agent_message","phase":"commentary","text":"draft"}}
            {"type":"item.completed","item":{"type":"reasoning","text":"private"}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"Exact  commentary\nwith unicode Ω."}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"final","text":"Exact final."}}
            {"type":"turn.completed"}
            """;

        var result = PilotBTranscriptParser.Parse(jsonl);

        Assert.True(result.IsValid, string.Join("|", result.InvalidReasons));
        Assert.Equal(PilotBTranscriptTerminalOutcome.Success, result.TerminalOutcome);
        Assert.Equal(result.IntermediateMessages, result.Commentary);
        Assert.Equal(["Exact  commentary\nwith unicode Ω."], result.Commentary.Select(message => message.Text));
        Assert.Equal(["Exact final."], result.FinalMessages.Select(message => message.Text));
        Assert.Equal(["commentary", "final"], result.SemanticMessages.Select(message => message.Phase));
        Assert.DoesNotContain(result.SemanticMessages, message => message.Text.Contains("private", StringComparison.Ordinal));
    }

    [Fact]
    public void Parser_ReportsPartialTranscriptWithDeterministicTerminalOutcome()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"turn.started","turn_id":"turn-1"}
            """;

        var result = PilotBTranscriptParser.Parse(jsonl);

        Assert.False(result.IsValid);
        Assert.Equal(PilotBTranscriptTerminalOutcome.Partial, result.TerminalOutcome);
        Assert.Equal(["missing-turn-completed"], result.InvalidReasons);
    }

    [Fact]
    public void Parser_RejectsDuplicateTerminalAndTrailingEventsInEncounterOrder()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"turn.started","turn_id":"turn-1"}
            {"type":"turn.completed"}
            {"type":"turn.completed"}
            {"type":"item.updated","item":{"type":"agent_message"}}
            """;

        var result = PilotBTranscriptParser.Parse(jsonl);

        Assert.False(result.IsValid);
        Assert.Equal(PilotBTranscriptTerminalOutcome.Success, result.TerminalOutcome);
        Assert.Equal(["duplicate-turn-completed", "trailing-event"], result.InvalidReasons);
    }

    [Fact]
    public void Parser_RejectsDuplicateThreadAndTurnStarts()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"thread.started","thread_id":"thread-2"}
            {"type":"turn.started","turn_id":"turn-1"}
            {"type":"turn.started","turn_id":"turn-2"}
            {"type":"turn.completed"}
            """;

        var result = PilotBTranscriptParser.Parse(jsonl);

        Assert.False(result.IsValid);
        Assert.Equal(["duplicate-thread-started", "duplicate-turn-started"], result.InvalidReasons);
    }

    [Fact]
    public void Parser_RejectsUnsupportedCompletedItemWithoutCreatingSemanticOutput()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"turn.started","turn_id":"turn-1"}
            {"type":"item.completed","item":{"type":"future_item"}}
            {"type":"turn.completed"}
            """;

        var result = PilotBTranscriptParser.Parse(jsonl);

        Assert.False(result.IsValid);
        Assert.Equal(["unsupported-item-type:future_item"], result.InvalidReasons);
        Assert.Empty(result.SemanticMessages);
    }

    [Fact]
    public void Parser_RejectsOutOfOrderContentAndMissingProtocolPrefix()
    {
        const string jsonl = """
            {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"must not count"}}
            {"type":"unknown.event"}
            """;

        var result = PilotBTranscriptParser.Parse(jsonl);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "turn-content-before-thread-started",
                "unsupported-event-type",
                "missing-thread-started",
                "missing-turn-started",
                "missing-turn-completed"
            ],
            result.InvalidReasons);
        Assert.Empty(result.SemanticMessages);
    }

    [Fact]
    public void Projection_PreservesCommentaryTextSequenceOrderAndAddsOnlyAdjudicationKinds()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"turn.started","turn_id":"turn-1"}
            {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"first  exact\nline"}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"commentary","text":"second exact Ω"}}
            {"type":"item.completed","item":{"type":"agent_message","phase":"final","text":"final is retained elsewhere"}}
            {"type":"turn.completed"}
            """;

        var parsed = PilotBTranscriptParser.Parse(jsonl);
        var projected = PilotBRunRecordProjection.ProjectCommentary(
            parsed,
            [PilotBMessageKind.Routine, PilotBMessageKind.Observable]);

        Assert.Equal([1, 2], projected.Select(message => message.Sequence));
        Assert.Equal(["first  exact\nline", "second exact Ω"], projected.Select(message => message.Text));
        Assert.Equal([PilotBMessageKind.Routine, PilotBMessageKind.Observable], projected.Select(message => message.Kind));
        Assert.All(projected, message =>
        {
            Assert.Equal("item.completed", message.SourceEventType);
            Assert.Equal("commentary", message.Phase);
        });
        Assert.DoesNotContain(projected, message => message.Text.Contains("final", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("turn.failed", PilotBTranscriptTerminalOutcome.Failure, "turn-failed")]
    [InlineData("error", PilotBTranscriptTerminalOutcome.FatalError, "fatal-error")]
    public void Parser_RecordsFailureTerminalOutcomes(string terminalEvent, PilotBTranscriptTerminalOutcome outcome, string reason)
    {
        var jsonl = $$"""
            {"type":"thread.started","thread_id":"thread-1"}
            {"type":"turn.started","turn_id":"turn-1"}
            {"type":"{{terminalEvent}}"}
            """;

        var result = PilotBTranscriptParser.Parse(jsonl);

        Assert.False(result.IsValid);
        Assert.True(result.HasTurnFailed);
        Assert.Equal(outcome, result.TerminalOutcome);
        Assert.Equal([reason], result.InvalidReasons);
    }
}
