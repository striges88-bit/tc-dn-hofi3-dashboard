using Xunit.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CryptoIndicatorApp.Memory.Tests;

public sealed class MemoryIdentityCommittedTests(ITestOutputHelper output)
{
    [Fact]
    public void CommittedRefreshPreservesRecordAndMethodIdentityWithEveryBlob()
    {
        using var fixture = MemoryProjectFixture.Create(output);
        var sources = new Dictionary<string, string>
        {
            ["A.cs"] = """
                namespace Repro;
                public class Box { }
                public partial record Box<T>
                {
                    public class Nested { }
                    partial void Check(int value);
                }
                // requires_symbol=Repro.Box`1.Nested
                """,
            ["B.cs"] = """
                namespace Repro;
                public partial record class Box<T>
                {
                    partial void Check(int value) { }
                }
                """
        };
        fixture.Write("CryptoIndicatorApp.sln", string.Empty);
        foreach (var (path, source) in sources) fixture.Write(path, source);
        fixture.InitializeGitRepository();
        var commit = fixture.RunGit("rev-parse", "HEAD").Trim();
        var tree = fixture.RunGit("rev-parse", "HEAD^{tree}").Trim();
        var blobs = sources.Keys.ToDictionary(path => path, path => fixture.RunGit("rev-parse", $"HEAD:{path}").Trim());
        // A dirty source cannot substitute for committed declaration provenance.
        fixture.Write("B.cs", "namespace Dirty;\npublic class Uncommitted { }");

        for (var iteration = 0; iteration < 2; iteration++)
        {
            var refresh = fixture.RunMemoryCli("refresh-from-commit", "--commit", commit, "--json");
            Assert.True(refresh.ExitCode == 0, refresh.StandardError);
            using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT symbol, source_path, source_hash, source_blob_sha, commit_sha, tree_sha
                FROM symbol_declarations ORDER BY symbol, source_path;
                """;
            using (var reader = command.ExecuteReader())
            {
                var names = new List<string>();
                while (reader.Read())
                {
                    names.Add(reader.GetString(0));
                    var path = reader.GetString(1);
                    Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sources[path]))).ToLowerInvariant(), reader.GetString(2));
                    Assert.Equal(blobs[path], reader.GetString(3));
                    Assert.Equal(commit, reader.GetString(4));
                    Assert.Equal(tree, reader.GetString(5));
                }
                Assert.Equal(new[] { "Repro.Box", "Repro.Box`1", "Repro.Box`1", "Repro.Box`1.Check/int", "Repro.Box`1.Check/int", "Repro.Box`1.Nested" }, names);
            }
            var search = fixture.RunMemoryCli("search", "--query", "Check", "--json");
            Assert.True(search.ExitCode == 0, search.StandardError);
            using var result = JsonDocument.Parse(search.StandardOutput);
            Assert.Single(result.RootElement.GetProperty("results").EnumerateArray(),
                h => h.GetProperty("id").GetString() == "symbol.repro-box-1-check-int");
            var stale = fixture.RunMemoryCli("stale-check", "--json");
            Assert.True(stale.ExitCode == 0, stale.StandardError + stale.StandardOutput);
            using var staleJson = JsonDocument.Parse(stale.StandardOutput);
            Assert.Empty(staleJson.RootElement.GetProperty("issues").EnumerateArray());
        }
    }
}
