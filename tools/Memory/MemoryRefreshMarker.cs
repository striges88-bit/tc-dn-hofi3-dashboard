namespace CryptoIndicatorApp.Memory;

internal static class MemoryRefreshMarker
{
    public const string RelativePath = "docs/memory/generated/memory-needs-refresh.marker.json";

    public static string GetMarkerPath(string projectRoot)
    {
        return Path.Combine(projectRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static void Clear(string projectRoot)
    {
        var markerPath = GetMarkerPath(projectRoot);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
    }
}
