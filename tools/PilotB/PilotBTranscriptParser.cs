using System.Text;
using System.Text.Json;

namespace CryptoIndicatorApp.PilotB;

public static class PilotBTranscriptParser
{
    private enum ParserState
    {
        AwaitingThread,
        AwaitingTurn,
        InTurn
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> ExcludedItemTypes = new(StringComparer.Ordinal)
    {
        "reasoning",
        "tool_call",
        "tool_result",
        "command_execution",
        "mcp_tool_call",
        "file_change",
        "todo_list"
    };

    public static PilotBTranscriptParseResult Parse(ReadOnlySpan<byte> utf8Jsonl)
    {
        try
        {
            return Parse(StrictUtf8.GetString(utf8Jsonl));
        }
        catch (DecoderFallbackException)
        {
            return new PilotBTranscriptParseResult(
                Array.Empty<PilotBTranscriptMessage>(),
                false,
                false,
                false,
                0,
                ["invalid-utf8"],
                Array.Empty<string>());
        }
    }

    public static PilotBTranscriptParseResult Parse(string jsonl)
    {
        var commentary = new List<PilotBTranscriptMessage>();
        var semanticMessages = new List<PilotBTranscriptMessage>();
        var finalMessages = new List<PilotBTranscriptMessage>();
        var invalidReasons = new List<string>();
        var excludedTypes = new List<string>();
        var lineCount = 0;
        var state = ParserState.AwaitingThread;
        var hasThreadStarted = false;
        var hasTurnStarted = false;
        var hasTurnCompleted = false;
        var hasTurnFailed = false;
        var hasTerminal = false;
        var terminalOutcome = PilotBTranscriptTerminalOutcome.Partial;

        using var reader = new StringReader(jsonl ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineCount++;
            if (string.IsNullOrWhiteSpace(line))
            {
                AddReason(invalidReasons, "blank-line");
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out var typeElement)
                    || typeElement.ValueKind != JsonValueKind.String)
                {
                    AddReason(invalidReasons, "invalid-event-shape");
                    continue;
                }

                var type = typeElement.GetString();
                if (type is null)
                {
                    AddReason(invalidReasons, "invalid-event-shape");
                    continue;
                }

                if (hasTerminal)
                {
                    AddReason(
                        invalidReasons,
                        terminalOutcome == PilotBTranscriptTerminalOutcome.Success && type == "turn.completed"
                            ? "duplicate-turn-completed"
                            : "trailing-event");
                    continue;
                }

                switch (state)
                {
                    case ParserState.AwaitingThread:
                        ParseBeforeThread(
                            type,
                            lineCount,
                            ref state,
                            ref hasThreadStarted,
                            ref hasTurnCompleted,
                            ref hasTurnFailed,
                            ref hasTerminal,
                            ref terminalOutcome,
                            invalidReasons);
                        break;

                    case ParserState.AwaitingTurn:
                        ParseBeforeTurn(
                            type,
                            ref state,
                            ref hasTurnStarted,
                            ref hasTurnCompleted,
                            ref hasTurnFailed,
                            ref hasTerminal,
                            ref terminalOutcome,
                            invalidReasons);
                        break;

                    case ParserState.InTurn:
                        ParseTurnContent(
                            root,
                            type,
                            ref hasTurnCompleted,
                            ref hasTurnFailed,
                            ref hasTerminal,
                            ref terminalOutcome,
                            commentary,
                            semanticMessages,
                            finalMessages,
                            excludedTypes,
                            invalidReasons);
                        break;
                }
            }
            catch (JsonException)
            {
                AddReason(invalidReasons, "malformed-json");
            }
        }

        if (lineCount == 0)
        {
            AddReason(invalidReasons, "empty-transcript");
        }

        if (!hasThreadStarted)
        {
            AddReason(invalidReasons, "missing-thread-started");
        }

        if (!hasTurnStarted)
        {
            AddReason(invalidReasons, "missing-turn-started");
        }

        if (!hasTerminal)
        {
            AddReason(invalidReasons, "missing-turn-completed");
            terminalOutcome = PilotBTranscriptTerminalOutcome.Partial;
        }

        var result = new PilotBTranscriptParseResult(
            commentary,
            invalidReasons.Count == 0,
            hasTurnCompleted,
            hasTurnFailed,
            lineCount,
            invalidReasons,
            excludedTypes);

        return result with
        {
            SemanticMessages = semanticMessages,
            FinalMessages = finalMessages,
            TerminalOutcome = terminalOutcome
        };
    }

    private static void ParseBeforeThread(
        string type,
        int lineCount,
        ref ParserState state,
        ref bool hasThreadStarted,
        ref bool hasTurnCompleted,
        ref bool hasTurnFailed,
        ref bool hasTerminal,
        ref PilotBTranscriptTerminalOutcome terminalOutcome,
        ICollection<string> invalidReasons)
    {
        if (type == "thread.started")
        {
            if (lineCount != 1)
            {
                AddReason(invalidReasons, "thread-started-not-first");
            }

            if (hasThreadStarted)
            {
                AddReason(invalidReasons, "duplicate-thread-started");
                return;
            }

            hasThreadStarted = true;
            state = ParserState.AwaitingTurn;
            return;
        }

        if (type == "turn.started")
        {
            AddReason(invalidReasons, "turn-started-before-thread-started");
            return;
        }

        if (IsItemEvent(type))
        {
            AddReason(invalidReasons, "turn-content-before-thread-started");
            return;
        }

        if (IsTerminalEvent(type))
        {
            AddReason(invalidReasons, "terminal-before-thread-started");
            SetTerminal(
                type,
                ref hasTurnCompleted,
                ref hasTurnFailed,
                ref hasTerminal,
                ref terminalOutcome,
                invalidReasons);
            return;
        }

        AddReason(invalidReasons, "unsupported-event-type");
    }

    private static void ParseBeforeTurn(
        string type,
        ref ParserState state,
        ref bool hasTurnStarted,
        ref bool hasTurnCompleted,
        ref bool hasTurnFailed,
        ref bool hasTerminal,
        ref PilotBTranscriptTerminalOutcome terminalOutcome,
        ICollection<string> invalidReasons)
    {
        if (type == "thread.started")
        {
            AddReason(invalidReasons, "duplicate-thread-started");
            return;
        }

        if (type == "turn.started")
        {
            if (hasTurnStarted)
            {
                AddReason(invalidReasons, "duplicate-turn-started");
                return;
            }

            hasTurnStarted = true;
            state = ParserState.InTurn;
            return;
        }

        if (IsItemEvent(type))
        {
            AddReason(invalidReasons, "turn-content-before-turn-started");
            return;
        }

        if (IsTerminalEvent(type))
        {
            AddReason(invalidReasons, "terminal-before-turn-started");
            SetTerminal(
                type,
                ref hasTurnCompleted,
                ref hasTurnFailed,
                ref hasTerminal,
                ref terminalOutcome,
                invalidReasons);
            return;
        }

        AddReason(invalidReasons, "unsupported-event-type");
    }

    private static void ParseTurnContent(
        JsonElement root,
        string type,
        ref bool hasTurnCompleted,
        ref bool hasTurnFailed,
        ref bool hasTerminal,
        ref PilotBTranscriptTerminalOutcome terminalOutcome,
        ICollection<PilotBTranscriptMessage> commentary,
        ICollection<PilotBTranscriptMessage> semanticMessages,
        ICollection<PilotBTranscriptMessage> finalMessages,
        ICollection<string> excludedTypes,
        ICollection<string> invalidReasons)
    {
        if (type == "thread.started")
        {
            AddReason(invalidReasons, "duplicate-thread-started");
            return;
        }

        if (type == "turn.started")
        {
            AddReason(invalidReasons, "duplicate-turn-started");
            return;
        }

        if (IsItemEvent(type))
        {
            ParseItem(
                root,
                type,
                commentary,
                semanticMessages,
                finalMessages,
                excludedTypes,
                invalidReasons);
            return;
        }

        if (IsTerminalEvent(type))
        {
            SetTerminal(
                type,
                ref hasTurnCompleted,
                ref hasTurnFailed,
                ref hasTerminal,
                ref terminalOutcome,
                invalidReasons);
            return;
        }

        AddReason(invalidReasons, "unsupported-event-type");
    }

    private static void SetTerminal(
        string type,
        ref bool hasTurnCompleted,
        ref bool hasTurnFailed,
        ref bool hasTerminal,
        ref PilotBTranscriptTerminalOutcome terminalOutcome,
        ICollection<string> invalidReasons)
    {
        hasTerminal = true;
        switch (type)
        {
            case "turn.completed":
                hasTurnCompleted = true;
                terminalOutcome = PilotBTranscriptTerminalOutcome.Success;
                break;

            case "turn.failed":
                hasTurnFailed = true;
                terminalOutcome = PilotBTranscriptTerminalOutcome.Failure;
                AddReason(invalidReasons, "turn-failed");
                break;

            case "error":
                hasTurnFailed = true;
                terminalOutcome = PilotBTranscriptTerminalOutcome.FatalError;
                AddReason(invalidReasons, "fatal-error");
                break;
        }
    }

    private static void ParseItem(
        JsonElement root,
        string eventType,
        ICollection<PilotBTranscriptMessage> commentary,
        ICollection<PilotBTranscriptMessage> semanticMessages,
        ICollection<PilotBTranscriptMessage> finalMessages,
        ICollection<string> excludedTypes,
        ICollection<string> invalidReasons)
    {
        if (!root.TryGetProperty("item", out var item)
            || item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("type", out var itemTypeElement)
            || itemTypeElement.ValueKind != JsonValueKind.String)
        {
            AddReason(invalidReasons, "invalid-item-shape");
            return;
        }

        var itemType = itemTypeElement.GetString();
        if (!string.Equals(eventType, "item.completed", StringComparison.Ordinal))
        {
            if (!IsKnownItemType(itemType))
            {
                AddReason(invalidReasons, $"unsupported-item-type:{itemType}");
            }

            return;
        }

        if (string.Equals(itemType, "agent_message", StringComparison.Ordinal))
        {
            if (!item.TryGetProperty("phase", out var phaseElement)
                || phaseElement.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("text", out var textElement)
                || textElement.ValueKind != JsonValueKind.String)
            {
                AddReason(invalidReasons, "invalid-agent-message-shape");
                return;
            }

            var phase = phaseElement.GetString();
            var text = textElement.GetString();
            if (string.IsNullOrWhiteSpace(phase) || string.IsNullOrWhiteSpace(text))
            {
                AddReason(invalidReasons, "empty-agent-message");
                return;
            }

            if (string.Equals(phase, "commentary", StringComparison.Ordinal))
            {
                var message = new PilotBTranscriptMessage(semanticMessages.Count + 1, text!, phase!);
                commentary.Add(message);
                semanticMessages.Add(message);
                return;
            }

            if (string.Equals(phase, "final", StringComparison.Ordinal))
            {
                var message = new PilotBTranscriptMessage(semanticMessages.Count + 1, text!, phase!);
                finalMessages.Add(message);
                semanticMessages.Add(message);
                AddExcludedType(excludedTypes, "agent_message.final");
                return;
            }

            AddReason(invalidReasons, "unsupported-agent-message-phase");
            return;
        }

        if (ExcludedItemTypes.Contains(itemType ?? string.Empty))
        {
            AddExcludedType(excludedTypes, itemType!);
            return;
        }

        AddReason(invalidReasons, $"unsupported-item-type:{itemType}");
    }

    private static bool IsItemEvent(string type)
        => type is "item.started" or "item.updated" or "item.completed";

    private static bool IsTerminalEvent(string type)
        => type is "turn.completed" or "turn.failed" or "error";

    private static bool IsKnownItemType(string? itemType)
        => string.Equals(itemType, "agent_message", StringComparison.Ordinal)
            || ExcludedItemTypes.Contains(itemType ?? string.Empty);

    private static void AddExcludedType(ICollection<string> excludedTypes, string type)
    {
        if (!excludedTypes.Contains(type))
        {
            excludedTypes.Add(type);
        }
    }

    private static void AddReason(ICollection<string> reasons, string reason)
    {
        if (!reasons.Contains(reason))
        {
            reasons.Add(reason);
        }
    }
}
