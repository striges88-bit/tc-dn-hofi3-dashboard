using CryptoIndicatorApp.Memory;

namespace CryptoIndicatorApp.Memory.Tests;

public sealed class MemoryPartialMethodTests
{
    [Theory]
    [InlineData("public partial System.Threading.Tasks.Task Check();", "public async partial System.Threading.Tasks.Task Check() => await System.Threading.Tasks.Task.Delay(1);")]
    [InlineData("public static partial void Check();", "[System.Runtime.InteropServices.DllImport(\"unused-proof-library\")] public static extern partial void Check();")]
    [InlineData("public unsafe partial int Check();", "public unsafe partial int Check() => 0;")]
    public async Task ImplementationSpecificModifiersKeepOnePartialMethod(string first, string second)
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap(first).Replace("Box<T>", "Box", StringComparison.Ordinal));
        fixture.Write("B.cs", Wrap(second).Replace("Box<T>", "Box", StringComparison.Ordinal));
        var snapshot = await fixture.Snapshot();
        Assert.Single(snapshot.Symbols, s => s.Kind == "method");
        Assert.Equal(2, snapshot.SymbolDeclarations.Count(d => d.Symbol == "Repro.Box.Check/0"));
    }

    [Theory]
    [InlineData("public partial int Check();", "public unsafe partial int Check() => 0;")]
    [InlineData("public async partial System.Threading.Tasks.Task Check();", "public partial System.Threading.Tasks.Task Check() => System.Threading.Tasks.Task.CompletedTask;")]
    public async Task IncompatibleImplementationModifiersAreRejected(string first, string second)
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap(first));
        fixture.Write("B.cs", Wrap(second));
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.Snapshot);
    }

    [Fact]
    public async Task CallsWithoutDeclarationModifiersDoNotBecomeMethodSymbols()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap("""
            public int Make()
            {
                var items = new[] {
                    new object(),
                    new object()
                };
                return Make();
            }
            public int Pass(int first, int second)
            {
                return Pass(
                    first,
                    Make());
            }
            """));
        var snapshot = await fixture.Snapshot();
        Assert.Equal(2, snapshot.Symbols.Count(s => s.Kind == "method"));
    }

    [Fact]
    public async Task LocalFunctionsDoNotCollideAcrossContainingMethods()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap("""
            public void First()
            {
                void Local() { }
                Local();
            }
            public void Second()
            {
                void Local() { }
                Local();
            }
            """));
        var snapshot = await fixture.Snapshot();
        Assert.Equal(2, snapshot.Symbols.Count(s => s.Kind == "method"));
    }

    [Fact]
    public async Task MatchingGenericMethodIgnoresCommentsAndParameterNames()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap("public static partial TItem Check<TItem>(ref int[] values, TItem item) where TItem : class;"));
        fixture.Write("B.cs", Wrap("public static partial TItem Check<TItem>(ref int[] data, TItem other) /* ; { */ where TItem : class => other;"));
        var snapshot = await fixture.Snapshot();
        Assert.Single(snapshot.Symbols, s => s.Symbol == "Repro.Box`1.Check`1/int-titem");
        Assert.Equal(2, snapshot.SymbolDeclarations.Count(d => d.Symbol == "Repro.Box`1.Check`1/int-titem"));
    }

    [Theory]
    [InlineData("partial void Check(int value);", "partial void Check(int value);")]
    [InlineData("partial void Check(int value) { }", "partial void Check(int value) { }")]
    [InlineData("partial void Check(int value);", "void Check(int value) { }")]
    [InlineData("public partial int Check(int value);", "public partial string Check(int value) => null;")]
    [InlineData("public partial ref int Check(int value);", "public partial int Check(int value) => value;")]
    [InlineData("public partial void Check(ref int value);", "public partial void Check(out int value) { value = 0; }")]
    [InlineData("public partial void Check(int value);", "private partial void Check(int value) { }")]
    public async Task IncompatiblePartialMethodDeclarationsAreRejected(string first, string second)
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap(first));
        fixture.Write("B.cs", Wrap(second));
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.Snapshot);
    }

    [Fact]
    public async Task AThirdPartialMethodDeclarationCannotJoinThePair()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap("partial void Check();"));
        fixture.Write("B.cs", Wrap("partial void Check() { }"));
        fixture.Write("C.cs", Wrap("partial void Check() { }"));
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.Snapshot);
    }

    [Fact]
    public async Task OptionalDeclarationSurvivesWithoutLoadingGeneratedImplementation()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap("partial void Check();"));
        fixture.Write("obj/Generated.cs", Wrap("partial void Check() { }"));
        var snapshot = await fixture.Snapshot();
        Assert.Single(snapshot.SymbolDeclarations, d => d.Symbol == "Repro.Box`1.Check/0");
        Assert.DoesNotContain(snapshot.Files, f => f.Path.StartsWith("obj/", StringComparison.Ordinal));
        using var store = new MemoryStore(":memory:");
        store.Refresh(snapshot);
        Assert.Empty(store.StaleCheck(fixture.Root).Issues);
    }

    [Fact]
    public async Task OrdinaryDuplicateMethodsRemainRejected()
    {
        using var fixture = new SymbolProjectFixture();
        fixture.Write("A.cs", Wrap("public void Check() { }\npublic void Check() { }"));
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.Snapshot);
    }

    private static string Wrap(string method) => $"namespace Repro;\npublic partial class Box<T>\n{{\n{method}\n}}";

    [Theory]
    [InlineData("{ }", false)]
    [InlineData("=> System.Console.WriteLine(value);", true)]
    public async Task PartialDefinitionAndImplementationKeepOneSymbolAndBothSources(string body, bool implementationFirst)
    {
        using var fixture = new SymbolProjectFixture();
        var definition = "namespace Repro;\npublic partial class Box<T>\n{\n[Fact] public partial void Check(int value);\n}";
        var implementation = $"namespace Repro;\npublic partial class Box<T>\n{{\npublic partial void Check(int value) {body}\n}}";
        fixture.Write("A.Tests.cs", implementationFirst ? implementation : definition);
        fixture.Write("B.Tests.cs", implementationFirst ? definition : implementation);
        var snapshot = await fixture.Snapshot();
        Assert.Single(snapshot.Symbols, s => s.Symbol == "Repro.Box`1.Check/int");
        Assert.Equal(2, snapshot.SymbolDeclarations.Count(d => d.Symbol == "Repro.Box`1.Check/int"));
        Assert.Single(snapshot.Relations, r => r.ToId == "symbol.Repro.Box`1.Check/int");
        Assert.Single(snapshot.Events, e => e.Symbol == "Repro.Box`1.Check/int");
        using var store = new MemoryStore(":memory:");
        store.Refresh(snapshot);
        Assert.Contains(store.Search("Check").Results, h => h.Id == "symbol.repro-box-1-check-int");
        Assert.Empty(store.StaleCheck(fixture.Root).Issues);
    }
}
