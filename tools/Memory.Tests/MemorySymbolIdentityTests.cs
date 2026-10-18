using CryptoIndicatorApp.Memory;
using Microsoft.Data.Sqlite;

namespace CryptoIndicatorApp.Memory.Tests;

public sealed class MemorySymbolIdentityTests
{
    [Theory]
    [InlineData("record", "record class", "record")]
    [InlineData("record class", "record", "record")]
    [InlineData("record struct", "record struct", "record struct")]
    public async Task PartialRecordFormsKeepNamesKindsAndProvenance(string firstKind, string secondKind, string expectedKind)
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", $"namespace Repro;\npublic partial {firstKind} Invoice<T>;\npublic partial {firstKind} Receipt<T>;");
        fixture.Write("B.cs", $"namespace Repro;\npublic partial {secondKind} Invoice<T> {{ }}\npublic partial {secondKind} Receipt<T> {{ }}");
        var snapshot = await fixture.Snapshot();
        Assert.Equal(new[] { "Repro.Invoice`1", "Repro.Receipt`1" }, snapshot.Symbols.Select(s => s.Symbol));
        Assert.All(snapshot.Symbols, s => Assert.Equal(expectedKind, s.Kind));
        Assert.Equal(4, snapshot.SymbolDeclarations.Count);
        Assert.Equal(2, snapshot.Relations.Count);
        using var store = new MemoryStore(":memory:");
        store.Refresh(snapshot);
        Assert.Contains(store.Search("Invoice").Results, hit => hit.Type == "symbol" && hit.Title.EndsWith("Repro.Invoice`1", StringComparison.Ordinal));
        Assert.Empty(store.StaleCheck(fixture.Root).Issues);
    }

    [Fact]
    public async Task ReferenceAndValueRecordsCannotCoalesce()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", "namespace Repro;\npublic partial record class Invoice<T>;");
        fixture.Write("B.cs", "namespace Repro;\npublic partial record struct Invoice<T>;");
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.Snapshot);
    }

    [Fact]
    public async Task DifferentTypeAritiesKeepNestedOwnersAndReferencesDistinct()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("Types.Tests.cs", """
            namespace Repro;
            public class Box
            {
                public class Nested
                {
                    [Fact] public void Check() { }
                }
            }
            public partial class Box<T>
            {
                public class Nested
                {
                    [Fact] public void Check() { }
                }
            }
            public class Box<T, U> { }
            // requires_symbol=Repro.Box`1.Nested
            """);
        fixture.Write("Part.cs", "namespace Repro;\npublic partial class Box<T> { }");
        fixture.Write("docs/memory/tests.md", "requires_symbol=Repro.Box`2");
        var snapshot = await fixture.Snapshot();
        Assert.Equal(new[] { "Repro.Box", "Repro.Box.Nested", "Repro.Box.Nested.Check/0", "Repro.Box`1", "Repro.Box`1.Nested", "Repro.Box`1.Nested.Check/0", "Repro.Box`2" },
            snapshot.Symbols.Select(s => s.Symbol).Order(StringComparer.Ordinal));
        Assert.Equal(2, snapshot.SymbolDeclarations.Count(d => d.Symbol == "Repro.Box`1"));
        Assert.Contains(snapshot.Relations, r => r.FromId == "symbol.Repro.Box`1.Nested" && r.ToId == "symbol.Repro.Box`1.Nested.Check/0");
        Assert.Contains(snapshot.Events, e => e.EventType == "test_method" && e.Symbol == "Repro.Box`1.Nested.Check/0");
        Assert.Contains(snapshot.Events, e => e.EventType == "test_symbol_reference" && e.Symbol == "Repro.Box`1.Nested");
        Assert.Contains(snapshot.Events, e => e.EventType == "test_symbol_reference" && e.Symbol == "Repro.Box`2");
        using var store = new MemoryStore(":memory:");
        store.Refresh(snapshot);
        Assert.Contains(store.Search("Box").Results, hit => hit.Type == "symbol" && hit.Title.EndsWith("Repro.Box`1", StringComparison.Ordinal));
        Assert.Empty(store.StaleCheck(fixture.Root).Issues);
    }

    [Fact]
    public async Task DistinctParameterTypesWithDotAndUnderscoreStayDistinctSymbols()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("Methods.Tests.cs", """
            namespace Repro;
            public class Box<T>
            {
                [Fact] public void M(Foo.Bar value) { }
                [Fact] public void M(Foo_Bar value) { }
            }
            """);
        var snapshot = await fixture.Snapshot();
        const string dotted = "Repro.Box`1.M/Foo.Bar";
        const string underscored = "Repro.Box`1.M/Foo_Bar";
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == dotted);
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == underscored);
        var databasePath = Path.Combine(fixture.Root, "memory.sqlite");
        using (var store = new MemoryStore(databasePath))
        {
            store.Refresh(snapshot);
            AssertSearchFindsBoth(store, "Foo", dotted, underscored);
        }

        AssertDistinctPrimaryKeys(databasePath, dotted, underscored);
    }

    [Fact]
    public async Task SymbolsThatDifferOnlyByCaseStayDistinctAfterRefresh()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", """
            namespace Repro;
            public class Foo { }
            public class foo { }
            // requires_symbol=Repro.Foo
            // requires_symbol=Repro.foo
            """);
        var snapshot = await fixture.Snapshot();
        const string upper = "Repro.Foo";
        const string lower = "Repro.foo";
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == upper);
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == lower);
        var databasePath = Path.Combine(fixture.Root, "memory.sqlite");
        using (var store = new MemoryStore(databasePath))
        {
            store.Refresh(snapshot);
            AssertSearchFindsBoth(store, "Foo", upper, lower);
        }

        AssertDistinctPrimaryKeys(databasePath, upper, lower);
    }

    private static void AssertSearchFindsBoth(MemoryStore store, string query, string firstSymbol, string secondSymbol)
    {
        var hits = store.Search(query).Results.Where(hit => hit.Type == "symbol").ToArray();
        var first = Assert.Single(hits, hit => hit.Title.EndsWith(firstSymbol, StringComparison.Ordinal));
        var second = Assert.Single(hits, hit => hit.Title.EndsWith(secondSymbol, StringComparison.Ordinal));
        Assert.NotEqual(first.Id, second.Id);
    }

    private static void AssertDistinctPrimaryKeys(string databasePath, string firstSymbol, string secondSymbol)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        Assert.NotEqual(
            SingleId(connection, "SELECT id, title FROM search_documents WHERE type = 'symbol'", title => title.EndsWith(firstSymbol, StringComparison.Ordinal)),
            SingleId(connection, "SELECT id, title FROM search_documents WHERE type = 'symbol'", title => title.EndsWith(secondSymbol, StringComparison.Ordinal)));
        Assert.NotEqual(
            SingleId(connection, "SELECT id, to_id FROM relations WHERE relation = 'owns'", toId => toId == "symbol." + firstSymbol),
            SingleId(connection, "SELECT id, to_id FROM relations WHERE relation = 'owns'", toId => toId == "symbol." + secondSymbol));
        Assert.NotEqual(
            SingleId(connection, "SELECT id, symbol FROM events WHERE symbol IS NOT NULL", symbol => symbol == firstSymbol),
            SingleId(connection, "SELECT id, symbol FROM events WHERE symbol IS NOT NULL", symbol => symbol == secondSymbol));
    }

    private static string SingleId(SqliteConnection connection, string sql, Func<string, bool> match)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            if (match(reader.GetString(1)))
            {
                ids.Add(reader.GetString(0));
            }
        }

        return Assert.Single(ids);
    }
}
