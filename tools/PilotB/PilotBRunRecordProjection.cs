namespace CryptoIndicatorApp.PilotB;

public static class PilotBRunRecordProjection
{
    public static IReadOnlyList<PilotBMessage> ProjectCommentary(
        PilotBTranscriptParseResult transcript,
        IReadOnlyList<PilotBMessageKind> adjudicationKinds)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(adjudicationKinds);

        if (adjudicationKinds.Count != transcript.Commentary.Count)
        {
            throw new ArgumentException(
                "One adjudication kind is required for each commentary message.",
                nameof(adjudicationKinds));
        }

        var messages = new PilotBMessage[transcript.Commentary.Count];
        for (var index = 0; index < messages.Length; index++)
        {
            var commentary = transcript.Commentary[index];
            messages[index] = new PilotBMessage(
                commentary.Sequence,
                commentary.Text,
                adjudicationKinds[index],
                "item.completed",
                commentary.Phase);
        }

        return messages;
    }
}
