using System.Text.RegularExpressions;

namespace CryptoIndicatorApp.Memory;

internal static class CSharpMemoryExtractor
{
    private static readonly Regex NamespaceRegex = new(
        @"^\s*namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*[;{]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex TypeRegex = new(
        @"(?m)^\s*(?:\[[^\]]+\]\s*)*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|readonly|unsafe)\s+)*(?<kind>class|struct|interface|enum|record)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex MethodRegex = new(
        @"(?m)^\s*(?:\[[^\]]+\]\s*)*(?:(?:public|internal|private|protected|static|sealed|override|virtual|abstract|async|partial|extern|unsafe)\s+)+(?<return>[A-Za-z_][A-Za-z0-9_<>,\[\]\?\.]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex RequiresSymbolRegex = new(
        @"requires_symbol=(?<symbol>[A-Za-z_][A-Za-z0-9_.]*|[A-Z0-9]{3,20})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TodoRegex = new(
        @"\bTODO\b(?<text>[:\s].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExperimentOutcomeRegex = new(
        @"experiment_outcome\s*[=:]\s*(?<outcome>[A-Za-z_][A-Za-z0-9_-]*)\s*(?:\|\s*)?(?<text>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CSharpMemoryExtraction Extract(string path, string text)
    {
        var parseText = CSharpLiteralSanitizer.BlankStringAndCharLiterals(text);
        var namespaceName = NamespaceRegex.Match(parseText) is { Success: true } namespaceMatch
            ? namespaceMatch.Groups["name"].Value
            : null;
        var types = ExtractTypes(namespaceName, parseText).ToArray();
        var methods = ExtractMethods(namespaceName, parseText, types).ToArray();
        var symbols = types.Concat(methods).ToArray();
        var relations = ExtractRelations(namespaceName, path, types, methods).ToArray();
        var events = ExtractEvents(path, parseText, methods).ToArray();
        var todos = ExtractTodos(path, parseText).ToArray();
        var experiments = ExtractExperiments(path, parseText).ToArray();

        return new CSharpMemoryExtraction(symbols, relations, events, todos, experiments);
    }

    private static IEnumerable<CSharpSymbol> ExtractTypes(string? namespaceName, string text)
    {
        var candidates = TypeRegex.Matches(text)
            .Select(match => new CSharpTypeCandidate(
                match.Groups["kind"].Value,
                match.Groups["name"].Value,
                match.Index,
                FindBodyEnd(text, match.Index)))
            .OrderBy(candidate => candidate.Position)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var parent = candidates
                .Where(other => other.Position < candidate.Position && candidate.Position < other.EndPosition)
                .OrderByDescending(other => other.Position)
                .FirstOrDefault();
            candidate.ParentFullName = parent?.FullName ?? namespaceName;
            candidate.FullName = Qualify(candidate.ParentFullName, candidate.Name);

            yield return new CSharpSymbol(
                candidate.FullName,
                candidate.Kind,
                candidate.Name,
                candidate.ParentFullName,
                candidate.Position,
                candidate.EndPosition,
                HasTestAttribute: false);
        }
    }

    private static IEnumerable<CSharpSymbol> ExtractMethods(string? namespaceName, string text, IReadOnlyList<CSharpSymbol> types)
    {
        foreach (Match match in MethodRegex.Matches(text))
        {
            var name = match.Groups["name"].Value;
            if (IsControlFlowToken(name))
            {
                continue;
            }

            var parentType = types
                .Where(type => type.Position <= match.Index && match.Index < type.EndPosition)
                .OrderByDescending(type => type.Position)
                .FirstOrDefault();
            if (parentType is null)
            {
                continue;
            }

            var parameterSignature = BuildParameterSignatureKey(text, match.Index + match.Length - 1);
            var fullName = $"{parentType.FullName}.{name}/{parameterSignature}";
            yield return new CSharpSymbol(
                fullName,
                "method",
                name,
                parentType.FullName,
                match.Index,
                match.Index + match.Length,
                HasTestAttribute(match.Value));
        }
    }

    private static IEnumerable<CSharpRelation> ExtractRelations(
        string? namespaceName,
        string path,
        IReadOnlyList<CSharpSymbol> types,
        IReadOnlyList<CSharpSymbol> methods)
    {
        foreach (var type in types)
        {
            var module = string.IsNullOrWhiteSpace(namespaceName) ? ModuleId(path) : $"module.{namespaceName}";
            yield return new CSharpRelation(module, "owns", $"symbol.{type.FullName}", $"module {module} owns symbol {type.FullName}");
        }

        foreach (var method in methods)
        {
            if (string.IsNullOrWhiteSpace(method.ParentSymbol))
            {
                continue;
            }

            yield return new CSharpRelation(
                $"symbol.{method.ParentSymbol}",
                "owns",
                $"symbol.{method.FullName}",
                $"symbol {method.ParentSymbol} owns method {method.FullName}");
        }
    }

    private static IEnumerable<EventRecordDraft> ExtractEvents(string path, string text, IReadOnlyList<CSharpSymbol> methods)
    {
        foreach (Match match in RequiresSymbolRegex.Matches(text))
        {
            var symbol = match.Groups["symbol"].Value;
            yield return new EventRecordDraft(
                $"event.test-symbol-reference.{ProjectMemoryIndexer.Slug(symbol)}",
                "test_symbol_reference",
                symbol,
                $"requires_symbol={symbol}");
        }

        foreach (var method in methods)
        {
            if (!path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
                && !path.Contains(".Tests/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!method.HasTestAttribute)
            {
                continue;
            }

            yield return new EventRecordDraft(
                $"event.test-method.{ProjectMemoryIndexer.Slug(method.FullName)}",
                "test_method",
                method.FullName,
                $"test_method {method.FullName} observed_in {path}");
        }
    }

    private static IEnumerable<TodoRecordDraft> ExtractTodos(string path, string text)
    {
        var lineNumber = 0;
        foreach (var rawLine in SplitLines(text))
        {
            lineNumber++;
            var match = TodoRegex.Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            var body = match.Groups["text"].Success ? match.Groups["text"].Value.Trim(' ', ':') : rawLine.Trim();
            if (string.IsNullOrWhiteSpace(body))
            {
                body = rawLine.Trim();
            }

            yield return new TodoRecordDraft(
                $"todo.{ProjectMemoryIndexer.Slug(path)}.{lineNumber}",
                "open",
                body);
        }
    }

    private static IEnumerable<ExperimentRecordDraft> ExtractExperiments(string path, string text)
    {
        var lineNumber = 0;
        foreach (var rawLine in SplitLines(text))
        {
            lineNumber++;
            var match = ExperimentOutcomeRegex.Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            var outcome = match.Groups["outcome"].Value.ToLowerInvariant();
            var description = match.Groups["text"].Value.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                description = rawLine.Trim();
            }

            yield return new ExperimentRecordDraft(
                $"experiment.{ProjectMemoryIndexer.Slug(path)}.{lineNumber}",
                "current",
                outcome,
                description);
        }
    }

    private static bool HasTestAttribute(string declaration)
    {
        return declaration.Contains("[Fact", StringComparison.Ordinal)
            || declaration.Contains("[Theory", StringComparison.Ordinal);
    }

    private static int FindBodyEnd(string text, int start)
    {
        var braceIndex = text.IndexOf('{', start);
        var semicolonIndex = text.IndexOf(';', start);
        if (braceIndex < 0 || (semicolonIndex >= 0 && semicolonIndex < braceIndex))
        {
            return semicolonIndex >= 0 ? semicolonIndex + 1 : text.Length;
        }

        var depth = 0;
        for (var index = braceIndex; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index + 1;
                }
            }
        }

        return text.Length;
    }

    private static string BuildParameterSignatureKey(string text, int openingParen)
    {
        var parameterTypes = ExtractParameterSegments(text, openingParen)
            .Select(ExtractParameterTypeKey)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToArray();

        return parameterTypes.Length == 0 ? "0" : string.Join('-', parameterTypes);
    }

    private static IEnumerable<string> ExtractParameterSegments(string text, int openingParen)
    {
        if (openingParen < 0 || openingParen >= text.Length || text[openingParen] != '(')
        {
            yield break;
        }

        var segmentStart = openingParen + 1;
        var parenDepth = 0;
        var angleDepth = 0;
        var bracketDepth = 0;
        for (var index = segmentStart; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '(')
            {
                parenDepth++;
            }
            else if (current == ')' && parenDepth > 0)
            {
                parenDepth--;
            }
            else if (current == ')' && angleDepth == 0 && bracketDepth == 0)
            {
                if (HasParameterText(text, segmentStart, index))
                {
                    yield return text[segmentStart..index];
                }

                yield break;
            }
            else if (current == '<')
            {
                angleDepth++;
            }
            else if (current == '>' && angleDepth > 0)
            {
                angleDepth--;
            }
            else if (current == '[')
            {
                bracketDepth++;
            }
            else if (current == ']' && bracketDepth > 0)
            {
                bracketDepth--;
            }
            else if (current == ',' && parenDepth == 0 && angleDepth == 0 && bracketDepth == 0)
            {
                if (HasParameterText(text, segmentStart, index))
                {
                    yield return text[segmentStart..index];
                }

                segmentStart = index + 1;
            }
        }
    }

    private static bool HasParameterText(string text, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractParameterTypeKey(string parameter)
    {
        parameter = Regex.Replace(parameter, @"\[[^\]]+\]", " ");
        var equalsIndex = parameter.IndexOf('=');
        if (equalsIndex >= 0)
        {
            parameter = parameter[..equalsIndex];
        }

        var tokens = Regex.Matches(parameter.Trim(), @"\S+")
            .Select(match => match.Value)
            .Where(token => token is not "params" and not "ref" and not "out" and not "in" and not "this" and not "scoped")
            .ToArray();
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var typeTokens = tokens.Length == 1 ? tokens : tokens[..^1];
        var typeName = string.Concat(typeTokens);
        return ProjectMemoryIndexer.Slug(typeName);
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    private static string Qualify(string? namespaceName, string name)
    {
        return string.IsNullOrWhiteSpace(namespaceName) ? name : $"{namespaceName}.{name}";
    }

    private static string ModuleId(string path)
    {
        return $"module.{ProjectMemoryIndexer.Slug(path)}";
    }

    private static bool IsControlFlowToken(string name)
    {
        return name is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "lock";
    }
}

internal sealed record CSharpMemoryExtraction(
    IReadOnlyList<CSharpSymbol> Symbols,
    IReadOnlyList<CSharpRelation> Relations,
    IReadOnlyList<EventRecordDraft> Events,
    IReadOnlyList<TodoRecordDraft> Todos,
    IReadOnlyList<ExperimentRecordDraft> Experiments);

internal sealed record CSharpSymbol(
    string FullName,
    string Kind,
    string DisplayName,
    string? ParentSymbol,
    int Position,
    int EndPosition,
    bool HasTestAttribute);

internal sealed class CSharpTypeCandidate(string kind, string name, int position, int endPosition)
{
    public string Kind { get; } = kind;

    public string Name { get; } = name;

    public int Position { get; } = position;

    public int EndPosition { get; } = endPosition;

    public string? ParentFullName { get; set; }

    public string FullName { get; set; } = string.Empty;
}

internal sealed record CSharpRelation(string FromId, string Relation, string ToId, string Text);

internal sealed record EventRecordDraft(string Id, string EventType, string? Symbol, string Text);

internal sealed record TodoRecordDraft(string Id, string Status, string Text);

internal sealed record ExperimentRecordDraft(string Id, string Status, string Outcome, string Text);
