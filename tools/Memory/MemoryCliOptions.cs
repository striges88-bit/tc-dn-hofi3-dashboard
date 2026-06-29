namespace CryptoIndicatorApp.Memory;

public enum MemoryCommand
{
    Refresh,
    RefreshFromCommit,
    Search,
    Explain,
    StaleCheck,
    Status,
}

public sealed record MemoryCliOptions(
    MemoryCommand Command,
    string ProjectRoot,
    string DatabasePath,
    string Query,
    string Commit,
    bool Json)
{
    public static MemoryCliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new InvalidOperationException("Command is required: refresh, refresh-from-commit, search, explain, stale-check, or status.");
        }

        var command = args[0].ToLowerInvariant() switch
        {
            "refresh" => MemoryCommand.Refresh,
            "refresh-from-commit" => MemoryCommand.RefreshFromCommit,
            "search" => MemoryCommand.Search,
            "explain" => MemoryCommand.Explain,
            "stale-check" => MemoryCommand.StaleCheck,
            "status" => MemoryCommand.Status,
            _ => throw new InvalidOperationException($"Unknown memory command: {args[0]}")
        };

        var projectRoot = string.Empty;
        var databasePath = string.Empty;
        var query = string.Empty;
        var commit = "HEAD";
        var json = false;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--project-root":
                    projectRoot = ReadValue(args, ref index, argument);
                    break;
                case "--db":
                    databasePath = ReadValue(args, ref index, argument);
                    break;
                case "--query":
                    query = ReadValue(args, ref index, argument);
                    break;
                case "--commit":
                    commit = ReadValue(args, ref index, argument);
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown option: {argument}");
            }
        }

        projectRoot = ResolveProjectRoot(projectRoot);
        databasePath = ResolveDatabasePath(projectRoot, databasePath);

        if ((command is MemoryCommand.Search or MemoryCommand.Explain) && string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("--query is required for search and explain.");
        }

        if (command is MemoryCommand.RefreshFromCommit && string.IsNullOrWhiteSpace(commit))
        {
            throw new InvalidOperationException("--commit is required for refresh-from-commit.");
        }

        return new MemoryCliOptions(command, projectRoot, databasePath, query, commit, json);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static string ResolveProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            projectRoot = Directory.GetCurrentDirectory();
        }

        var resolved = Path.GetFullPath(projectRoot);
        if (!File.Exists(Path.Combine(resolved, "CryptoIndicatorApp.sln")))
        {
            throw new InvalidOperationException($"Project root does not contain CryptoIndicatorApp.sln: {resolved}");
        }

        return resolved;
    }

    private static string ResolveDatabasePath(string projectRoot, string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            databasePath = Path.Combine(projectRoot, "docs", "memory", "generated", "project-memory.sqlite");
        }

        if (!Path.IsPathRooted(databasePath))
        {
            databasePath = Path.Combine(projectRoot, databasePath);
        }

        return Path.GetFullPath(databasePath);
    }
}
