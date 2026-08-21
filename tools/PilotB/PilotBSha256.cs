using System.Security.Cryptography;
using System.Text;

namespace CryptoIndicatorApp.PilotB;

public static class PilotBSha256
{
    public static string Compute(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Compute(string value)
        => Compute(Encoding.UTF8.GetBytes(value));

    public static string ComputeFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool IsSha256(string? value)
        => value is not null
           && value.Length == 64
           && value.All(character => char.IsAsciiHexDigit(character));
}
