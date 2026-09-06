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
        Assert.Contains(store.Search("Invoice").Results, h => h.Id == "symbol.repro-invoice-1");
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
        Assert.Contains(store.Search("Box").Results, hit => hit.Id == "symbol.repro-box-1");
        Assert.Empty(store.StaleCheck(fixture.Root).Issues);
    }
}
