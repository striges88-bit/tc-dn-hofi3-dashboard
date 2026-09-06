using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CryptoIndicatorApp.Memory.Tests;

public sealed class MemoryPartialDeclarationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DotnetPath = File.Exists(Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe"))
        ? Path.Combine(RepositoryRoot, ".dotnet", "dotnet.exe")
        : "dotnet";
#if DEBUG
    private const string DotnetConfiguration = "Debug";
#else
    private const string DotnetConfiguration = "Release";
#endif

    [Fact]
    public void WorkingTreeRefreshCoalescesPartialClassAndPreservesDeclarationProvenance()
    {
        using var fixture = MemoryProjectFixture.Create();
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
        using var fixture = MemoryProjectFixture.Create();
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
        using var fixture = MemoryProjectFixture.Create();
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        fixture.Write("Repro/First.cs", "namespace Repro;\n\npublic partial class Sample<T> { }\n");
        fixture.Write("Repro/Second.cs", "namespace Repro;\n\npublic partial class Sample<T> { }\n");

        var refresh = fixture.RunMemoryCli("refresh", "--json");
        Assert.True(
            refresh.ExitCode == 0,
            $"same-arity refresh failed with exit code {refresh.ExitCode}\nSTDOUT:\n{refresh.StandardOutput}\nSTDERR:\n{refresh.StandardError}");
        Assert.Equal(2, ReadDeclarations(fixture.DatabasePath).Count);

        var search = fixture.RunMemoryCli("search", "--query", "Repro.Sample", "--json");
        AssertSearchHitCount(search, "symbol.repro-sample", "symbol", 1);
    }

    [Theory]
    [MemberData(nameof(IncompatibleDuplicateDeclarations))]
    public void WorkingTreeRefreshRejectsIncompatibleDuplicateTypes(string firstDeclaration, string secondDeclaration)
    {
        using var fixture = MemoryProjectFixture.Create();
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
        using var fixture = MemoryProjectFixture.Create();
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
        { "public partial class Sample { }", "public partial class Sample<T> { }" },
    };

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
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT symbol, source_path, source_hash, declaration_position,
                   commit_sha, tree_sha, source_blob_sha
            FROM symbol_declarations
            WHERE symbol = 'Repro.Sample'
            ORDER BY source_path;
            """;

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CryptoIndicatorApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record DeclarationProvenance(
        string Symbol,
        string SourcePath,
        string SourceHash,
        int DeclarationPosition,
        string? CommitSha,
        string? TreeSha,
        string? SourceBlobSha);

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class MemoryProjectFixture : IDisposable
    {
        private MemoryProjectFixture(string root)
        {
            Root = root;
            DatabasePath = Path.Combine(root, "docs", "memory", "generated", "project-memory.sqlite");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public static MemoryProjectFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "tc-dn-hofi3-memory-partial-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new MemoryProjectFixture(root);
        }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public CliResult RunMemoryCli(params string[] arguments)
        {
            var projectPath = Path.Combine(RepositoryRoot, "tools", "Memory", "CryptoIndicatorApp.Memory.csproj");
            var startInfo = new ProcessStartInfo
            {
                FileName = DotnetPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = RepositoryRoot,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--no-restore");
            startInfo.ArgumentList.Add("--no-build");
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add(DotnetConfiguration);
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--");
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.ArgumentList.Add("--project-root");
            startInfo.ArgumentList.Add(Root);
            startInfo.ArgumentList.Add("--db");
            startInfo.ArgumentList.Add(DatabasePath);

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var stdoutTask = process!.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                process.WaitForExit(TimeSpan.FromSeconds(5));
                Assert.Fail("memory CLI timed out.");
            }

            return new CliResult(
                process.ExitCode,
                stdoutTask.GetAwaiter().GetResult(),
                stderrTask.GetAwaiter().GetResult());
        }

        public void InitializeGitRepository()
        {
            RunGit("init");
            RunGit("config", "user.name", "Memory Partial Test");
            RunGit("config", "user.email", "memory-partial-test@example.invalid");
            RunGit("add", ".");
            RunGit("commit", "-m", "partial declaration fixture");
        }

        public string RunGit(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GitPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Root,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var stdoutTask = process!.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                process.WaitForExit(TimeSpan.FromSeconds(5));
                Assert.Fail($"git {string.Join(' ', arguments)} timed out.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            Assert.True(
                process.ExitCode == 0,
                $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            return stdout;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                ClearReadOnlyAttributes();
                Directory.Delete(Root, recursive: true);
            }
        }

        private void ClearReadOnlyAttributes()
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            foreach (var directory in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(directory, FileAttributes.Directory);
            }
        }
    }

    private static string GitPath => File.Exists(@"C:\Program Files\Git\cmd\git.exe")
        ? @"C:\Program Files\Git\cmd\git.exe"
        : "git";
}
