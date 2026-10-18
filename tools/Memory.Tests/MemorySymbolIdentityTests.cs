using CryptoIndicatorApp.Memory;

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
        Assert.Contains(store.Search("Invoice").Results, h => h.Id == SymbolSearchId("Repro.Invoice`1"));
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
        Assert.Contains(store.Search("Box").Results, hit => hit.Id == SymbolSearchId("Repro.Box`1"));
        Assert.Empty(store.StaleCheck(fixture.Root).Issues);
    }

    [Fact]
    public async Task DistinctParameterTypesWithDotAndUnderscoreStayDistinctSymbols()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", """
            namespace Repro;
            public class Box<T>
            {
                public void M(Foo.Bar value) { }
                public void M(Foo_Bar value) { }
            }
            """);
        var snapshot = await fixture.Snapshot();
        const string dotted = "Repro.Box`1.M/Foo.Bar";
        const string underscored = "Repro.Box`1.M/Foo_Bar";
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == dotted);
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == underscored);
        Assert.Contains(snapshot.Relations, relation => relation.Id == Owns("Repro.Box`1", dotted));
        Assert.Contains(snapshot.Relations, relation => relation.Id == Owns("Repro.Box`1", underscored));
        using var store = new MemoryStore(":memory:");
        store.Refresh(snapshot);
        var hits = store.Search("Foo").Results;
        Assert.Contains(hits, hit => hit.Id == SymbolSearchId(dotted) && hit.Type == "symbol");
        Assert.Contains(hits, hit => hit.Id == SymbolSearchId(underscored) && hit.Type == "symbol");
    }

    [Fact]
    public async Task SymbolsThatDifferOnlyByCaseStayDistinctAfterRefresh()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", """
            namespace Repro;
            public class Foo { }
            public class foo { }
            """);
        var snapshot = await fixture.Snapshot();
        const string upper = "Repro.Foo";
        const string lower = "Repro.foo";
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == upper);
        Assert.Contains(snapshot.Symbols, symbol => symbol.Symbol == lower);
        Assert.Contains(snapshot.Relations, relation => relation.Id == ModuleOwns(upper));
        Assert.Contains(snapshot.Relations, relation => relation.Id == ModuleOwns(lower));
        using var store = new MemoryStore(":memory:");
        store.Refresh(snapshot);
        var hits = store.Search("Foo").Results;
        Assert.Contains(hits, hit => hit.Id == SymbolSearchId(upper) && hit.Type == "symbol");
        Assert.Contains(hits, hit => hit.Id == SymbolSearchId(lower) && hit.Type == "symbol");
    }

    private static string SymbolSearchId(string fullName)
        => $"symbol.{ProjectMemoryIndexer.SymbolKey(fullName)}";

    private static string Owns(string ownerFullName, string ownedFullName)
        => $"relation.{ProjectMemoryIndexer.SymbolKey(ownerFullName)}.owns.{ProjectMemoryIndexer.SymbolKey(ownedFullName)}";

    private static string ModuleOwns(string ownedFullName)
        => $"relation.{ProjectMemoryIndexer.Slug("module.Repro")}.owns.{ProjectMemoryIndexer.SymbolKey(ownedFullName)}";
}
