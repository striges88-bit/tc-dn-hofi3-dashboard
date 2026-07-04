namespace CryptoIndicatorApp.Memory;

internal static class CSharpLiteralSanitizer
{
    public static string BlankStringAndCharLiterals(string text)
    {
        var chars = text.ToCharArray();
        var index = 0;
        while (index < text.Length)
        {
            if (StartsWith(text, index, "//"))
            {
                index = MoveToLineEnd(text, index + 2);
                continue;
            }

            if (StartsWith(text, index, "/*"))
            {
                index = MovePastBlockComment(text, index + 2);
                continue;
            }

            if (TryFindRawStringEnd(text, index, out var rawEnd))
            {
                BlankRangePreserveNewlines(chars, index, rawEnd);
                index = rawEnd;
                continue;
            }

            if (TryFindVerbatimStringEnd(text, index, out var verbatimEnd))
            {
                BlankRangePreserveNewlines(chars, index, verbatimEnd);
                index = verbatimEnd;
                continue;
            }

            if (TryFindRegularStringEnd(text, index, out var stringEnd))
            {
                BlankRangePreserveNewlines(chars, index, stringEnd);
                index = stringEnd;
                continue;
            }

            if (TryFindCharLiteralEnd(text, index, out var charEnd))
            {
                BlankRangePreserveNewlines(chars, index, charEnd);
                index = charEnd;
                continue;
            }

            index++;
        }

        return new string(chars);
    }

    private static bool TryFindRawStringEnd(string text, int start, out int end)
    {
        end = start;
        var delimiterStart = start;
        while (delimiterStart < text.Length && text[delimiterStart] == '$')
        {
            delimiterStart++;
        }

        if (delimiterStart >= text.Length || text[delimiterStart] != '"')
        {
            return false;
        }

        var quoteCount = CountRepeated(text, delimiterStart, '"');
        if (quoteCount < 3)
        {
            return false;
        }

        var searchIndex = delimiterStart + quoteCount;
        while (searchIndex < text.Length)
        {
            if (text[searchIndex] == '"' && CountRepeated(text, searchIndex, '"') >= quoteCount)
            {
                end = searchIndex + quoteCount;
                return true;
            }

            searchIndex++;
        }

        end = text.Length;
        return true;
    }

    private static bool TryFindVerbatimStringEnd(string text, int start, out int end)
    {
        end = start;
        var quoteIndex = start;
        if (StartsWith(text, start, "@\""))
        {
            quoteIndex = start + 1;
        }
        else if (StartsWith(text, start, "$@\"") || StartsWith(text, start, "@$\""))
        {
            quoteIndex = start + 2;
        }
        else
        {
            return false;
        }

        var index = quoteIndex + 1;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                end = index + 1;
                return true;
            }

            index++;
        }

        end = text.Length;
        return true;
    }

    private static bool TryFindRegularStringEnd(string text, int start, out int end)
    {
        end = start;
        var quoteIndex = start;
        while (quoteIndex < text.Length && text[quoteIndex] == '$')
        {
            quoteIndex++;
        }

        if (quoteIndex >= text.Length || text[quoteIndex] != '"')
        {
            return false;
        }

        if (CountRepeated(text, quoteIndex, '"') >= 3)
        {
            return false;
        }

        var escaped = false;
        for (var index = quoteIndex + 1; index < text.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (text[index] == '"')
            {
                end = index + 1;
                return true;
            }
        }

        end = text.Length;
        return true;
    }

    private static bool TryFindCharLiteralEnd(string text, int start, out int end)
    {
        end = start;
        if (text[start] != '\'')
        {
            return false;
        }

        var escaped = false;
        for (var index = start + 1; index < text.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (text[index] == '\'')
            {
                end = index + 1;
                return true;
            }
        }

        end = text.Length;
        return true;
    }

    private static int CountRepeated(string text, int start, char value)
    {
        var count = 0;
        while (start + count < text.Length && text[start + count] == value)
        {
            count++;
        }

        return count;
    }

    private static void BlankRangePreserveNewlines(char[] chars, int start, int end)
    {
        for (var index = start; index < end && index < chars.Length; index++)
        {
            if (chars[index] is not '\r' and not '\n')
            {
                chars[index] = ' ';
            }
        }
    }

    private static bool StartsWith(string text, int start, string value)
    {
        return start + value.Length <= text.Length
            && string.CompareOrdinal(text, start, value, 0, value.Length) == 0;
    }

    private static int MoveToLineEnd(string text, int start)
    {
        var index = start;
        while (index < text.Length && text[index] is not '\r' and not '\n')
        {
            index++;
        }

        return index;
    }

    private static int MovePastBlockComment(string text, int start)
    {
        var index = start;
        while (index + 1 < text.Length)
        {
            if (text[index] == '*' && text[index + 1] == '/')
            {
                return index + 2;
            }

            index++;
        }

        return text.Length;
    }
}
