namespace CryptoIndicatorApp.Infrastructure.Tests;

public sealed class MemoryCiContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void ContinuousIntegrationUsesSharedCommitBoundContracts()
    {
        var workflow = ReadText(".github/workflows/ci.yml");
        var semanticEval = workflow.IndexOf("-Command eval", StringComparison.Ordinal);
        var semanticContract = workflow.IndexOf("Assert semantic commit and manifest contract", StringComparison.Ordinal);

        Assert.True(semanticEval >= 0, "Missing semantic eval step.");
        Assert.True(semanticContract > semanticEval, "Semantic contract assertion must run after eval.");
        Assert.Contains(". scripts\\memory-pre-push-contract.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-JsonPropertyFalse -Object $status -Name 'needs_refresh'", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-JsonStringProperty -Object $status -Name 'head'", workflow, StringComparison.Ordinal);
        Assert.Contains("git rev-parse 'HEAD^{tree}'", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-LanceDbEvalReport", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-CommitAddressedFreshness", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-SemanticIndexManifest", workflow, StringComparison.Ordinal);
        Assert.Contains("-MinimumEvalCases 11", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryDocsDescribeCommitBoundManifestAndStructuredGitFailures()
    {
        var contract = ReadText("docs/memory/contract.md");
        var scriptsReadme = ReadText("scripts/README.md");

        foreach (var requiredTerm in new[]
                 {
                     "lancedb-manifest.json",
                     "source_store=sqlite-fts5",
                     "indexed_count",
                     "commit_sha",
                     "tree_sha",
                     "indexed_at",
                     "failure_code",
                     "timed_out",
                     "git-unavailable",
                     "git-timeout",
                 })
        {
            Assert.Contains(requiredTerm, contract, StringComparison.Ordinal);
        }

        Assert.Contains("lancedb-manifest.json", scriptsReadme, StringComparison.Ordinal);
        Assert.Contains("commit-addressed", scriptsReadme, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadText(string relativePath)
    {
        return File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CryptoIndicatorApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
