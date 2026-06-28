namespace CryptoIndicatorApp.Infrastructure.Jsonl;

public sealed class JsonlReplayException : Exception
{
    public JsonlReplayException(string message)
        : base(message)
    {
    }

    public JsonlReplayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
