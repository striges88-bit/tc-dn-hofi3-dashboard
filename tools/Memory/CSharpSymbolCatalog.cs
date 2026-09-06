namespace CryptoIndicatorApp.Memory;

internal sealed class CSharpSymbolCatalog
{
    private readonly Dictionary<string, CSharpSymbol> _symbols = new(StringComparer.Ordinal);
    private readonly List<SymbolDeclarationRecord> _declarations = [];
    private readonly HashSet<string> _pairedMethods = new(StringComparer.Ordinal);

    public IReadOnlyList<SymbolDeclarationRecord> Declarations => _declarations;

    public bool Add(CSharpSymbol symbol, string path, string hash)
    {
        var isNew = _symbols.TryAdd(symbol.FullName, symbol);
        if (!isNew)
        {
            var first = _symbols[symbol.FullName];
            var matchingIdentity = first.IsPartial && symbol.IsPartial
                && first.Kind == symbol.Kind
                && first.GenericArity == symbol.GenericArity
                && first.ParentSymbol == symbol.ParentSymbol
                && first.DisplayName == symbol.DisplayName;
            var matchingType = symbol.Kind is "class" or "struct" or "interface" or "record" or "record struct";
            var matchingMethod = first.PartialMethod is { } firstMethod && symbol.PartialMethod is { } nextMethod
                && firstMethod.Signature == nextMethod.Signature && firstMethod.IsImplementation != nextMethod.IsImplementation
                && !_pairedMethods.Contains(symbol.FullName);
            if (!matchingIdentity || (!matchingType && !matchingMethod))
            {
                throw new InvalidOperationException($"Duplicate C# symbol '{symbol.FullName}' from '{path}' is not a matching partial declaration.");
            }
            if (matchingMethod) _pairedMethods.Add(symbol.FullName);
        }

        // Files arrive in ordinal path order; retain that canonical source and every declaration.
        _declarations.Add(new SymbolDeclarationRecord(symbol.FullName, path, hash, symbol.Position));
        return isNew;
    }
}
