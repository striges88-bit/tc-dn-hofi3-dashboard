namespace CryptoIndicatorApp.Desktop.Configuration;

public sealed class ProxyOptions
{
    public bool Enabled { get; set; }

    public string Type { get; set; } = "Http";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public void Normalize()
    {
        Type = string.IsNullOrWhiteSpace(Type)
            ? "Http"
            : NormalizeType(Type);

        Host = string.IsNullOrWhiteSpace(Host)
            ? string.Empty
            : Host.Trim();
    }

    private static string NormalizeType(string value)
    {
        var trimmed = value.Trim();
        return string.Equals(trimmed, "http", StringComparison.OrdinalIgnoreCase)
            ? "Http"
            : trimmed;
    }
}
