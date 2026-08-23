using System.Text.Json;

namespace CryptoIndicatorApp.PilotB;

public sealed record PilotBFileManifestEntry(string RelativePath, long Length, string Sha256);

public sealed record PilotBFileManifest(
    string Root,
    IReadOnlyList<PilotBFileManifestEntry> Files,
    string Sha256)
{
    public static PilotBFileManifest Capture(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(fullRoot);
        }

        var entries = new List<PilotBFileManifestEntry>();
        foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            var relativePath = Path.GetRelativePath(fullRoot, fullPath);
            if (!IsWithin(fullRoot, fullPath)
                || relativePath.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith(".git" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var info = new FileInfo(fullPath);
            entries.Add(new PilotBFileManifestEntry(
                relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
                info.Length,
                PilotBSha256.ComputeFile(fullPath)));
        }

        var ordered = entries
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var canonical = JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = false });
        return new PilotBFileManifest(fullRoot, ordered, PilotBSha256.Compute(canonical));
    }

    public string ToJson()
        => JsonSerializer.Serialize(new
        {
            schema_version = "pilot-b.file-manifest.v3",
            root = Root,
            files = Files,
            sha256 = Sha256
        }, new JsonSerializerOptions { WriteIndented = false });

    public static PilotBFileManifest Parse(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());
        var root = document.RootElement;
        Require(root, "schema_version", "pilot-b.file-manifest.v3");
        var files = RequiredArray(root, "files").EnumerateArray().Select(file => new PilotBFileManifestEntry(
            RequiredString(file, nameof(PilotBFileManifestEntry.RelativePath)),
            RequiredLong(file, nameof(PilotBFileManifestEntry.Length)),
            RequiredString(file, nameof(PilotBFileManifestEntry.Sha256)))).ToArray();
        var ordered = files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
        if (!files.SequenceEqual(ordered))
        {
            throw new FormatException("File manifest entries must use canonical order.");
        }

        var expectedSha = PilotBSha256.Compute(JsonSerializer.Serialize(
            ordered,
            new JsonSerializerOptions { WriteIndented = false }));
        var recordedSha = RequiredString(root, "sha256");
        if (!string.Equals(recordedSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("File manifest semantic hash does not match its canonical payload.");
        }

        return new PilotBFileManifest(RequiredString(root, "root"), ordered, expectedSha);
    }

    public static bool IsWithin(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullCandidate, fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement RequiredArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Required array '{name}' is missing.");
        }

        return value;
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Required string '{name}' is missing.");
        }

        return value.GetString()!;
    }

    private static long RequiredLong(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt64(out var parsed) || parsed < 0)
        {
            throw new FormatException($"Required non-negative integer '{name}' is missing.");
        }

        return parsed;
    }

    private static void Require(JsonElement root, string name, string expected)
    {
        if (!string.Equals(RequiredString(root, name), expected, StringComparison.Ordinal))
        {
            throw new FormatException($"'{name}' does not match the v3 contract.");
        }
    }
}
