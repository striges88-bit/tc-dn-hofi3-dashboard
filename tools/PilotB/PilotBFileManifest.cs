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

    public static bool IsWithin(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullCandidate, fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
