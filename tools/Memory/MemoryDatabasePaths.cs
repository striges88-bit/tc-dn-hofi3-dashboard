namespace CryptoIndicatorApp.Memory;

internal static class MemoryDatabasePaths
{
    private const string CanonicalFileName = "project-memory.sqlite";
    private const string RetainedFileName = "project-retained.sqlite";

    public static string Resolve(string projectRoot, string requestedPath, MemoryCommand command)
    {
        var path = requestedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = UsesRetainedStore(command)
                ? Retained(projectRoot)
                : Canonical(projectRoot);
        }
        else if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(projectRoot, path);
        }

        return Path.GetFullPath(path);
    }

    public static string Canonical(string projectRoot)
    {
        return Path.Combine(projectRoot, "docs", "memory", "generated", CanonicalFileName);
    }

    public static string Retained(string projectRoot)
    {
        return Path.Combine(projectRoot, "docs", "memory", "generated", RetainedFileName);
    }

    public static bool UsesRetainedStore(MemoryCommand command)
    {
        return command is MemoryCommand.RetainImport
            or MemoryCommand.RetainSearch
            or MemoryCommand.RetainExport
            or MemoryCommand.RetainDelete;
    }
}
