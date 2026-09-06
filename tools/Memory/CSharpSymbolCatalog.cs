namespace CryptoIndicatorApp.Memory;

internal sealed class CSharpSymbolCatalog
{
    private readonly Dictionary<string, CSharpSymbol> _symbols = new(StringComparer.Ordinal);
    private readonly List<SymbolDeclarationRecord> _declarations = [];

    public IReadOnlyList<SymbolDeclarationRecord> Declarations => _declarations;

    public bool Add(CSharpSymbol symbol, string path, string hash)
    {
        var isNew = _symbols.TryAdd(symbol.FullName, symbol);
        if (!isNew)
        {
            var first = _symbols[symbol.FullName];
            if (!first.IsPartial || !symbol.IsPartial
                || symbol.Kind is not ("class" or "struct" or "interface" or "record")
                || first.Kind != symbol.Kind
                || first.GenericArity != symbol.GenericArity
                || first.ParentSymbol != symbol.ParentSymbol
                || first.DisplayName != symbol.DisplayName)
            {
                throw new InvalidOperationException($"Duplicate C# symbol '{symbol.FullName}' from '{path}' is not a matching partial type declaration.");
            }
        }

        // Files arrive in ordinal path order; retain that canonical source and every declaration.
        _declarations.Add(new SymbolDeclarationRecord(symbol.FullName, path, hash, symbol.Position));
        return isNew;
    }
}
