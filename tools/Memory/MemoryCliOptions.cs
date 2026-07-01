namespace CryptoIndicatorApp.Memory;

public enum MemoryCommand
{
    Refresh,
    RefreshFromCommit,
    Search,
    Explain,
    StaleCheck,
    Status,
    RetainImport,
    RetainSearch,
}

public sealed record MemoryCliOptions(
    MemoryCommand Command,
    string ProjectRoot,
    string DatabasePath,
    string Query,
    string Commit,
    string InputReportPath,
    bool Json)
{
    public static MemoryCliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new InvalidOperationException("Command is required: refresh, refresh-from-commit, search, explain, stale-check, status, retain-import, or retain-search.");
        }

        var command = args[0].ToLowerInvariant() switch
        {
            "refresh" => MemoryCommand.Refresh,
            "refresh-from-commit" => MemoryCommand.RefreshFromCommit,
            "search" => MemoryCommand.Search,
            "explain" => MemoryCommand.Explain,
            "stale-check" => MemoryCommand.StaleCheck,
            "status" => MemoryCommand.Status,
            "retain-import" => MemoryCommand.RetainImport,
            "retain-search" => MemoryCommand.RetainSearch,
            _ => throw new InvalidOperationException($"Unknown memory command: {args[0]}")
        };

        var projectRoot = string.Empty;
        var databasePath = string.Empty;
        var query = string.Empty;
        var commit = "HEAD";
        var inputReportPath = string.Empty;
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
                case "--input-report":
                    inputReportPath = ReadValue(args, ref index, argument);
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
        inputReportPath = ResolveInputReportPath(projectRoot, inputReportPath);

        if ((command is MemoryCommand.Search or MemoryCommand.Explain or MemoryCommand.RetainSearch) && string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("--query is required for search, explain, and retain-search.");
        }

        if (command is MemoryCommand.RefreshFromCommit or MemoryCommand.RetainImport && string.IsNullOrWhiteSpace(commit))
        {
            throw new InvalidOperationException("--commit is required for refresh-from-commit and retain-import.");
        }

        return new MemoryCliOptions(command, projectRoot, databasePath, query, commit, inputReportPath, json);
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

    private static string ResolveInputReportPath(string projectRoot, string inputReportPath)
    {
        if (string.IsNullOrWhiteSpace(inputReportPath))
        {
            inputReportPath = Path.Combine(projectRoot, "docs", "memory", "generated", "curated-retain-dry-run-report.json");
        }

        if (!Path.IsPathRooted(inputReportPath))
        {
            inputReportPath = Path.Combine(projectRoot, inputReportPath);
        }

        return Path.GetFullPath(inputReportPath);
    }
}
