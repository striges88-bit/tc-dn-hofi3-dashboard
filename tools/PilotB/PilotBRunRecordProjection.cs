namespace CryptoIndicatorApp.PilotB;

public static class PilotBRunRecordProjection
{
    public static IReadOnlyList<PilotBMessage> ProjectSemanticMessages(
        PilotBTranscriptParseResult transcript,
        IReadOnlyList<PilotBMessageKind> adjudicationKinds)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(adjudicationKinds);

        if (adjudicationKinds.Count != transcript.SemanticMessages.Count)
        {
            throw new ArgumentException(
                "One adjudication kind is required for each semantic message.",
                nameof(adjudicationKinds));
        }

        var messages = new PilotBMessage[transcript.SemanticMessages.Count];
        for (var index = 0; index < messages.Length; index++)
        {
            var message = transcript.SemanticMessages[index];
            messages[index] = new PilotBMessage(
                message.Sequence,
                message.Text,
                adjudicationKinds[index],
                message.Phase,
                "item.completed");
        }

        return messages;
    }
}
