using Xunit.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CryptoIndicatorApp.Memory.Tests;

public sealed class MemoryPartialDeclarationTests(ITestOutputHelper output)
{
    [Fact]
    public void WorkingTreeRefreshCoalescesPartialClassAndPreservesDeclarationProvenance()
    {
        using var fixture = MemoryProjectFixture.Create(output);
        var alphaText = """
            namespace Repro;

            public partial class Sample
            {
                [Fact]
                public void Alpha()
                {
                }
            }
            """;
        var betaText = """
            namespace Repro;

            public partial class Sample
            {
                [Fact]
                public void Beta()
                {
                }
            }
            """;
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("Repro/Sample.Alpha.Tests.cs", alphaText);
        fixture.Write("Repro/Sample.Beta.Tests.cs", betaText);

        var refresh = fixture.RunMemoryCli("refresh", "--json");
        Assert.True(
            refresh.ExitCode == 0,
            $"working-tree refresh failed with exit code {refresh.ExitCode}\nSTDOUT:\n{refresh.StandardOutput}\nSTDERR:\n{refresh.StandardError}");

        var logicalSymbolSearch = fixture.RunMemoryCli("search", "--query", "Repro.Sample", "--json");
        AssertSearchHitCount(logicalSymbolSearch, "symbol.repro-sample", "symbol", 1);

        var alphaSearch = fixture.RunMemoryCli("search", "--query", "Alpha", "--json");
        AssertSearchHit(alphaSearch, "symbol.repro-sample-alpha-0", "symbol", "Repro/Sample.Alpha.Tests.cs");
        AssertSearchHit(alphaSearch, "event.test-method.repro-sample-alpha-0", "event", "Repro/Sample.Alpha.Tests.cs");

        var betaSearch = fixture.RunMemoryCli("search", "--query", "Beta", "--json");
        AssertSearchHit(betaSearch, "symbol.repro-sample-beta-0", "symbol", "Repro/Sample.Beta.Tests.cs");
        AssertSearchHit(betaSearch, "event.test-method.repro-sample-beta-0", "event", "Repro/Sample.Beta.Tests.cs");

        var declarations = ReadDeclarations(fixture.DatabasePath);
        Assert.Equal(2, declarations.Count);
        var canonicalCounts = ReadCanonicalCounts(fixture.DatabasePath);
        Assert.Equal(1, canonicalCounts.SymbolCount);
        Assert.Equal(1, canonicalCounts.OwnershipRelationCount);
        Assert.Collection(
            declarations,
            declaration => AssertDeclaration(
                declaration,
                "Repro.Sample",
                "Repro/Sample.Alpha.Tests.cs",
                alphaText,
                alphaText.IndexOf("\npublic partial class", StringComparison.Ordinal)),
            declaration => AssertDeclaration(
                declaration,
                "Repro.Sample",
                "Repro/Sample.Beta.Tests.cs",
                betaText,
                betaText.IndexOf("\npublic partial class", StringComparison.Ordinal)));
    }

    [Fact]
    public void CommittedHeadRefreshPreservesBlobProvenanceAcrossRepeat()
    {
        using var fixture = MemoryProjectFixture.Create(output);
        var alphaText = """
            namespace Repro;

            public partial class Sample
            {
                [Fact]
                public void Alpha()
                {
                }
            }
            """;
        var betaText = """
            namespace Repro;

            public partial class Sample
            {
                [Fact]
                public void Beta()
                {
                }
            }
            """;
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("Repro/Sample.Alpha.Tests.cs", alphaText);
        fixture.Write("Repro/Sample.Beta.Tests.cs", betaText);
        fixture.InitializeGitRepository();

        var commitSha = fixture.RunGit("rev-parse", "HEAD").Trim();
        var treeSha = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();
        var alphaBlobSha = fixture.RunGit("rev-parse", "HEAD:Repro/Sample.Alpha.Tests.cs").Trim();
        var betaBlobSha = fixture.RunGit("rev-parse", "HEAD:Repro/Sample.Beta.Tests.cs").Trim();

        var firstRefresh = fixture.RunMemoryCli("refresh-from-commit", "--commit", "HEAD", "--json");
        Assert.True(
            firstRefresh.ExitCode == 0,
            $"committed refresh failed with exit code {firstRefresh.ExitCode}\nSTDOUT:\n{firstRefresh.StandardOutput}\nSTDERR:\n{firstRefresh.StandardError}");
        using (var refreshJson = JsonDocument.Parse(firstRefresh.StandardOutput))
        {
            Assert.Equal("git-commit", refreshJson.RootElement.GetProperty("refresh_source").GetString());
            Assert.Equal(commitSha, refreshJson.RootElement.GetProperty("commit_sha").GetString());
            Assert.Equal(treeSha, refreshJson.RootElement.GetProperty("tree_sha").GetString());
        }

        var firstDeclarations = ReadDeclarations(fixture.DatabasePath);
        Assert.Equal(2, firstDeclarations.Count);
        Assert.Collection(
            firstDeclarations,
            declaration => AssertDeclaration(
                declaration,
                "Repro.Sample",
                "Repro/Sample.Alpha.Tests.cs",
                alphaText,
                alphaText.IndexOf("\npublic partial class", StringComparison.Ordinal),
                commitSha,
                treeSha,
                alphaBlobSha),
            declaration => AssertDeclaration(
                declaration,
                "Repro.Sample",
                "Repro/Sample.Beta.Tests.cs",
                betaText,
                betaText.IndexOf("\npublic partial class", StringComparison.Ordinal),
                commitSha,
                treeSha,
                betaBlobSha));

        var secondRefresh = fixture.RunMemoryCli("refresh-from-commit", "--commit", "HEAD", "--json");
        Assert.True(
            secondRefresh.ExitCode == 0,
            $"repeat committed refresh failed with exit code {secondRefresh.ExitCode}\nSTDOUT:\n{secondRefresh.StandardOutput}\nSTDERR:\n{secondRefresh.StandardError}");
        Assert.Equal(firstDeclarations, ReadDeclarations(fixture.DatabasePath));
    }

    [Fact]
    public void SameGenericArityPartialClassesStillCoalesce()
    {
        using var fixture = MemoryProjectFixture.Create(output);
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("Repro/First.cs", "namespace Repro;\n\npublic partial class Sample<T> { }\n");
        fixture.Write("Repro/Second.cs", "namespace Repro;\n\npublic partial class Sample<T> { }\n");

        var refresh = fixture.RunMemoryCli("refresh", "--json");
        Assert.True(
            refresh.ExitCode == 0,
            $"same-arity refresh failed with exit code {refresh.ExitCode}\nSTDOUT:\n{refresh.StandardOutput}\nSTDERR:\n{refresh.StandardError}");
        Assert.Equal(2, ReadDeclarations(fixture.DatabasePath, "Repro.Sample`1").Count);

        var search = fixture.RunMemoryCli("search", "--query", "Repro.Sample", "--json");
        AssertSearchHitCount(search, "symbol.repro-sample-1", "symbol", 1);
    }

    [Fact]
    public void DistinctGenericAritiesRemainDistinctSymbols()
    {
        using var fixture = MemoryProjectFixture.Create(output);
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("Repro/NonGeneric.cs", "namespace Repro;\n\npublic class Sample { }\n");
        fixture.Write("Repro/Generic.cs", "namespace Repro;\n\npublic class Sample<T> { }\n");

        var refresh = fixture.RunMemoryCli("refresh", "--json");
        Assert.True(
            refresh.ExitCode == 0,
            $"distinct-arity refresh failed with exit code {refresh.ExitCode}\nSTDOUT:\n{refresh.StandardOutput}\nSTDERR:\n{refresh.StandardError}");

        var search = fixture.RunMemoryCli("search", "--query", "Repro.Sample", "--json");
        AssertSearchHitCount(search, "symbol.repro-sample", "symbol", 1);
        AssertSearchHitCount(search, "symbol.repro-sample-1", "symbol", 1);
    }

    [Theory]
    [MemberData(nameof(IncompatibleDuplicateDeclarations))]
    public void WorkingTreeRefreshRejectsIncompatibleDuplicateTypes(string firstDeclaration, string secondDeclaration)
    {
        using var fixture = MemoryProjectFixture.Create(output);
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("Repro/First.cs", $"namespace Repro;\n\n{firstDeclaration}\n");
        fixture.Write("Repro/Second.cs", $"namespace Repro;\n\n{secondDeclaration}\n");

        var refresh = fixture.RunMemoryCli("refresh", "--json");
        Assert.Equal(1, refresh.ExitCode);
        Assert.Contains("Duplicate C# symbol 'Repro.Sample'", refresh.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctSymbolsWithCollidingSearchIdsAreStillRejected()
    {
        using var fixture = MemoryProjectFixture.Create(output);
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("First.cs", "namespace Repro;\npublic class Sample_A { }\n");
        fixture.Write("Second.cs", "namespace Repro.Sample;\npublic class A { }\n");

        var refresh = fixture.RunMemoryCli("refresh", "--json");
        Assert.Equal(1, refresh.ExitCode);
        Assert.Contains("Duplicate search document id 'symbol.repro-sample-a'", refresh.StandardError, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> IncompatibleDuplicateDeclarations => new()
    {
        { "public class Sample { }", "public class Sample { }" },
        { "public partial class Sample { }", "public class Sample { }" },
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposeAfterPrimaryFailurePreservesPrimaryAndDiagnosesCleanupFailure(bool outputUnavailable)
    {
        var captured = new CapturedOutput(outputUnavailable);
        var deletion = new ControlledDirectoryDelete();
        var fixture = MemoryProjectFixture.Create(captured, deletion.Delete);
        var primaryFailure = new TimeoutException("memory CLI timed out.");

        try
        {
            var failure = Record.Exception((Action)(() =>
            {
                try
                {
                    fixture.RecordPrimaryFailure(primaryFailure);
                    throw primaryFailure;
                }
                finally
                {
                    fixture.Dispose();
                }
            }));

            Assert.Same(primaryFailure, failure);
            Assert.Equal(5, deletion.Attempts);
            var cleanupFailures = Assert.IsAssignableFrom<IReadOnlyList<Exception>>(
                primaryFailure.Data[MemoryProjectFixture.CleanupFailuresDataKey]);
            Assert.Equal(outputUnavailable ? 2 : 1, cleanupFailures.Count);
            Assert.Contains("after 5 attempts", cleanupFailures[0].Message, StringComparison.Ordinal);
            var deletionFailure = Assert.IsType<IOException>(cleanupFailures[0].InnerException);
            Assert.Equal("controlled directory deletion failure", deletionFailure.Message);
            if (outputUnavailable)
            {
                Assert.Equal("test output unavailable", cleanupFailures[1].Message);
            }
            else
            {
                Assert.Contains("stage=fixture-dispose", captured.Text);
                Assert.Contains("System.TimeoutException", captured.Text);
                Assert.Contains("System.IO.IOException", captured.Text);
                Assert.Contains("after 5 attempts", captured.Text);
            }
            Assert.True(Directory.Exists(fixture.Root));
        }
        finally
        {
            deletion.Fail = false;
            fixture.Dispose();
        }
    }

    [Fact]
    public void DisposeWithoutPrimaryPropagatesCleanupFailureAfterRetries()
    {
        var deletion = new ControlledDirectoryDelete();
        var fixture = MemoryProjectFixture.Create(output, deletion.Delete);

        try
        {
            var cleanupFailure = Record.Exception((Action)fixture.Dispose);
            Assert.NotNull(cleanupFailure);
            Assert.Equal(5, deletion.Attempts);
            Assert.Contains("after 5 attempts", cleanupFailure!.Message, StringComparison.Ordinal);
            var deletionFailure = Assert.IsType<IOException>(cleanupFailure.InnerException);
            Assert.Equal("controlled directory deletion failure", deletionFailure.Message);
            Assert.True(Directory.Exists(fixture.Root));
        }
        finally
        {
            deletion.Fail = false;
            fixture.Dispose();
        }
    }

    private sealed class ControlledDirectoryDelete
    {
        public int Attempts { get; private set; }

        public bool Fail { get; set; } = true;

        public void Delete(string path)
        {
            Attempts++;
            if (Fail)
            {
                throw new IOException("controlled directory deletion failure");
            }

            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class CapturedOutput(bool unavailable) : ITestOutputHelper
    {
        private readonly StringBuilder _text = new();
        public string Text => _text.ToString();
        public void WriteLine(string message)
        {
            if (unavailable) throw new IOException("test output unavailable");
            _text.AppendLine(message);
        }
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(format, args));
    }

    private static void AssertDeclaration(
        DeclarationProvenance actual,
        string expectedSymbol,
        string expectedPath,
        string expectedText,
        int expectedPosition,
        string? expectedCommitSha = null,
        string? expectedTreeSha = null,
        string? expectedSourceBlobSha = null)
    {
        Assert.Equal(expectedSymbol, actual.Symbol);
        Assert.Equal(expectedPath, actual.SourcePath);
        Assert.Equal(HashText(expectedText), actual.SourceHash);
        Assert.Equal(expectedPosition, actual.DeclarationPosition);
        Assert.Equal(expectedCommitSha, actual.CommitSha);
        Assert.Equal(expectedTreeSha, actual.TreeSha);
        Assert.Equal(expectedSourceBlobSha, actual.SourceBlobSha);
    }

    private static void AssertSearchHitCount(CliResult result, string id, string type, int expectedCount)
    {
        Assert.True(
            result.ExitCode == 0,
            $"search failed with exit code {result.ExitCode}\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
        using var document = JsonDocument.Parse(result.StandardOutput);
        var hits = document.RootElement
            .GetProperty("results")
            .EnumerateArray()
            .Where(hit => hit.GetProperty("id").GetString() == id && hit.GetProperty("type").GetString() == type)
            .ToArray();
        Assert.Equal(expectedCount, hits.Length);
    }

    private static void AssertSearchHit(CliResult result, string id, string type, string sourcePath)
    {
        Assert.True(
            result.ExitCode == 0,
            $"search failed with exit code {result.ExitCode}\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Contains(
            document.RootElement.GetProperty("results").EnumerateArray(),
            hit => hit.GetProperty("id").GetString() == id
                && hit.GetProperty("type").GetString() == type
                && hit.GetProperty("source_path").GetString() == sourcePath);
    }

    private static IReadOnlyList<DeclarationProvenance> ReadDeclarations(string databasePath)
        => ReadDeclarations(databasePath, "Repro.Sample");

    private static IReadOnlyList<DeclarationProvenance> ReadDeclarations(string databasePath, string symbol)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT symbol, source_path, source_hash, declaration_position,
                   commit_sha, tree_sha, source_blob_sha
            FROM symbol_declarations
            WHERE symbol = $symbol
            ORDER BY source_path;
            """;
        command.Parameters.AddWithValue("$symbol", symbol);

        using var reader = command.ExecuteReader();
        var declarations = new List<DeclarationProvenance>();
        while (reader.Read())
        {
            declarations.Add(new DeclarationProvenance(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                ReadNullable(reader, 4),
                ReadNullable(reader, 5),
                ReadNullable(reader, 6)));
        }

        return declarations;
    }

    private static (int SymbolCount, int OwnershipRelationCount) ReadCanonicalCounts(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM symbols WHERE symbol = 'Repro.Sample'),
                (SELECT COUNT(*) FROM relations WHERE relation = 'owns' AND to_id = 'symbol.Repro.Sample');
            """;
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static string? ReadNullable(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string HashText(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record DeclarationProvenance(
        string Symbol,
        string SourcePath,
        string SourceHash,
        int DeclarationPosition,
        string? CommitSha,
        string? TreeSha,
        string? SourceBlobSha);

}
