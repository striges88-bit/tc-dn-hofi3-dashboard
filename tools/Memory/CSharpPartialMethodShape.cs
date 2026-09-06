using System.Text.RegularExpressions;

namespace CryptoIndicatorApp.Memory;

// Conservative syntax matching only: no type binding or loading generated build output.
internal sealed record CSharpPartialMethodShape(string Signature, bool IsImplementation)
{
    public static CSharpPartialMethodShape Read(string text, int openingParen, Match method, IEnumerable<string> parameters)
    {
        var depth = 1;
        var end = openingParen + 1;
        for (; end < text.Length && depth > 0; end++)
        {
            if (text[end] == '(') depth++;
            if (text[end] == ')') depth--;
        }

        var tail = Regex.Match(text[end..], @"^(?<constraints>[^;{=]*)(?<body>\{|=>|;)");
        if (depth != 0 || !tail.Success)
        {
            throw new InvalidOperationException("Unterminated partial method declaration.");
        }

        var modifierTokens = Regex.Matches(method.Groups["modifiers"].Value, @"\w+").Select(m => m.Value).ToArray();
        var isImplementation = tail.Groups["body"].Value != ";" || modifierTokens.Contains("extern");
        if (!isImplementation && modifierTokens.Contains("async"))
        {
            throw new InvalidOperationException("A defining partial method cannot be async.");
        }

        // C# permits async/extern only on the implementation; unsafe must match both parts.
        var modifiers = modifierTokens
            .Where(m => m is not "partial" and not "async" and not "extern")
            .Order(StringComparer.Ordinal);
        var signature = string.Join(' ', modifiers) + "|" + Compact(method.Groups["return"].Value)
            + "|" + Compact(method.Groups["generic"].Value)
            + "|" + string.Join('|', parameters.Select(ParameterShape))
            + "|" + Compact(tail.Groups["constraints"].Value);
        return new CSharpPartialMethodShape(signature, isImplementation);
    }

    private static string ParameterShape(string parameter)
    {
        // Attributes and defaults are not signature identity; keep ref-kind and exact type spelling.
        parameter = Regex.Replace(parameter.Trim(), @"^(?:\[[^\]]+\]\s*)+", "");
        var equals = parameter.IndexOf('=');
        if (equals >= 0) parameter = parameter[..equals];
        parameter = Regex.Replace(parameter.Trim(), @"\s+@?[A-Za-z_][A-Za-z0-9_]*$", "");
        return Compact(parameter);
    }

    private static string Compact(string text) => string.Join(' ',
        Regex.Matches(text, @"@?[A-Za-z_][A-Za-z0-9_]*|\S").Select(m => m.Value));
}
