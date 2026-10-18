using CryptoIndicatorApp.Memory;
using Microsoft.Data.Sqlite;

namespace CryptoIndicatorApp.Memory.Tests;

public sealed class MemoryCollisionTests
{
    [Fact]
    public async Task CaseDistinctMarkdownReferencesDoNotCollapseToASlug()
    {
        var rows = await RefreshReferences(("docs/memory/tests.md", "requires_symbol=Repro.Foo\nrequires_symbol=Repro.foo\n"));
        var foo = Assert.Single(rows, row => row.Symbol == "Repro.Foo");
        var lower = Assert.Single(rows, row => row.Symbol == "Repro.foo");
        Assert.NotEqual(WithoutSource(foo.Id), WithoutSource(lower.Id));
    }

    [Fact]
    public async Task ExternalSymbolDocumentsDoNotUseBareSlugIds()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("docs/memory/symbols.md", "- FOOBAR\n- BAZQUX\n");
        var snapshot = await fixture.Snapshot();
        var databasePath = Path.Combine(fixture.Root, "memory.sqlite");
        using var store = new MemoryStore(databasePath);
        store.Refresh(snapshot);
        var foobar = Assert.Single(store.Search("FOOBAR").Results, hit => hit.Type == "symbol" && hit.Title == "FOOBAR");
        var bazqux = Assert.Single(store.Search("BAZQUX").Results, hit => hit.Type == "symbol" && hit.Title == "BAZQUX");
        Assert.NotEqual("symbol.foobar", foobar.Id);
        Assert.NotEqual("symbol.bazqux", bazqux.Id);
        Assert.NotEqual(foobar.Id, bazqux.Id);
    }

    [Fact]
    public async Task RepeatedSymbolReferenceRefreshesInEverySourceShape()
    {
        const string reference = "requires_symbol=Repro.Marker";
        await AssertReferenceCount(2, ("A.cs", $"// {reference}\n"), ("B.cs", $"// {reference}\n"));
        await AssertReferenceCount(2, ("A.cs", $"// {reference}\n// {reference}\n"));
        await AssertReferenceCount(2, ("docs/memory/tests.md", reference + "\n"), ("A.cs", $"// {reference}\n"));
    }

    [Fact]
    public async Task ArrayRankKeepsOverloadsDistinctAfterRefresh()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", """
            namespace Repro;
            public class Box
            {
                public void M(int value) { }
                public void M([Attr] int[,] value) { }
            }
            """);
        var snapshot = await fixture.Snapshot();
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == "Repro.Box.M/int");
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == "Repro.Box.M/int[,]");
        var databasePath = Path.Combine(fixture.Root, "memory.sqlite");
        using var store = new MemoryStore(databasePath);
        store.Refresh(snapshot);
        var hits = store.Search("Box").Results.Where(hit => hit.Type == "symbol").ToArray();
        var scalar = Assert.Single(hits, hit => hit.Title.EndsWith("Repro.Box.M/int", StringComparison.Ordinal));
        var grid = Assert.Single(hits, hit => hit.Title.EndsWith("Repro.Box.M/int[,]", StringComparison.Ordinal));
        Assert.NotEqual(scalar.Id, grid.Id);
    }

    private static async Task AssertReferenceCount(int count, params (string Path, string Text)[] files)
    {
        var rows = await RefreshReferences(files);
        var ids = rows.Where(row => row.Symbol == "Repro.Marker").Select(row => row.Id).ToArray();
        Assert.Equal(count, ids.Length);
        Assert.Equal(count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static async Task<IReadOnlyList<(string Id, string Symbol)>> RefreshReferences(params (string Path, string Text)[] files)
    {
        using var fixture = new SymbolProjectFixture();
        foreach (var (path, text) in files)
        {
            fixture.Write(path, text);
        }

        var snapshot = await fixture.Snapshot();
        var databasePath = Path.Combine(fixture.Root, "memory.sqlite");
        using (var store = new MemoryStore(databasePath))
        {
            store.Refresh(snapshot);
        }

        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, symbol
            FROM events
            WHERE event_type = 'test_symbol_reference'
            ORDER BY id
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<(string Id, string Symbol)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        }

        return rows;
    }

    private static string WithoutSource(string id)
    {
        const string path = "docs/memory/tests.md";
        var pathAt = id.LastIndexOf(path, StringComparison.Ordinal);
        Assert.True(pathAt > 2 && id[pathAt - 1] == '.');
        var ordinalAt = id.LastIndexOf('.', pathAt - 2);
        Assert.True(ordinalAt > 0);
        Assert.True(int.TryParse(id[(ordinalAt + 1)..(pathAt - 1)], out _));
        return id[..ordinalAt];
    }
}
