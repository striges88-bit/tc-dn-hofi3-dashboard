using System.Text.Json;

namespace CryptoIndicatorApp.Memory;

public static class MemoryCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = MemoryCliOptions.Parse(args);
            using var store = new MemoryStore(options.DatabasePath);

            object response = options.Command switch
            {
                MemoryCommand.Refresh => await RefreshAsync(options, store),
                MemoryCommand.RefreshFromCommit => await RefreshFromCommitAsync(options, store),
                MemoryCommand.Search => store.Search(options.Query),
                MemoryCommand.Explain => store.Explain(options.Query),
                MemoryCommand.StaleCheck => store.StaleCheck(options.ProjectRoot),
                MemoryCommand.Status => store.Status(options.ProjectRoot),
                _ => throw new InvalidOperationException($"Unsupported command: {options.Command}")
            };

            WriteResponse(response, options.Json);
            return response is StaleCheckResult staleCheck && staleCheck.Issues.Count > 0 ? 2 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<RefreshResult> RefreshAsync(MemoryCliOptions options, MemoryStore store)
    {
        var indexer = new ProjectMemoryIndexer(options.ProjectRoot);
        var snapshot = await indexer.BuildSnapshotAsync();
        return store.Refresh(snapshot);
    }

    private static async Task<RefreshResult> RefreshFromCommitAsync(MemoryCliOptions options, MemoryStore store)
    {
        var indexer = new GitCommitMemoryIndexer(options.ProjectRoot);
        var snapshot = await indexer.BuildSnapshotAsync(options.Commit);
        var result = store.Refresh(snapshot);
        MemoryRefreshMarker.Clear(options.ProjectRoot);
        return result;
    }

    private static void WriteResponse(object response, bool json)
    {
        if (json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            };
            Console.WriteLine(JsonSerializer.Serialize(response, options));
            return;
        }

        Console.WriteLine(response);
    }
}
