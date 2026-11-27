namespace TelePilot.Playlists;

internal sealed record M3uAttributeParseResult(
    Dictionary<string, string> Attributes,
    bool HadMalformedAttribute);

internal static class M3uAttributeParser
{
    public static M3uAttributeParseResult Parse(ReadOnlySpan<char> text)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var malformed = false;
        var index = 0;
        while (index < text.Length)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                break;
            }

            var keyStart = index;
            while (index < text.Length &&
                   !char.IsWhiteSpace(text[index]) &&
                   text[index] != '=')
            {
                index++;
            }

            var key = text[keyStart..index];
            SkipWhitespace(text, ref index);
            if (key.Length == 0)
            {
                malformed = true;
                index++;
                continue;
            }

            if (index >= text.Length || text[index] != '=')
            {
                malformed = true;
                continue;
            }

            index++;
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                attributes[key.ToString()] = string.Empty;
                break;
            }

            string value;
            if (text[index] is '"' or '\'')
            {
                var quote = text[index++];
                var quotedValue = text[index..];
                var closingQuote = quotedValue.IndexOf(quote);
                if (closingQuote < 0)
                {
                    value = quotedValue.ToString();
                    index = text.Length;
                    malformed = true;
                }
                else if (closingQuote == 0 || quotedValue[closingQuote - 1] != '\\')
                {
                    value = quotedValue[..closingQuote].ToString();
                    index += closingQuote + 1;
                }
                else
                {
                    value = ReadEscapedValue(text, quote, ref index, out var terminated);
                    malformed |= !terminated;
                }
            }
            else
            {
                var valueStart = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                value = text[valueStart..index].ToString();
            }

            attributes[key.ToString()] = value;
        }

        return new M3uAttributeParseResult(attributes, malformed);
    }

    private static string ReadEscapedValue(ReadOnlySpan<char> text, char quote, ref int index, out bool terminated)
    {
        var builder = new System.Text.StringBuilder();
        while (index < text.Length)
        {
            var character = text[index++];
            if (character == '\\' && index < text.Length && text[index] == quote)
            {
                builder.Append(quote);
                index++;
            }
            else if (character == quote)
            {
                terminated = true;
                return builder.ToString();
            }
            else
            {
                builder.Append(character);
            }
        }

        terminated = false;
        return builder.ToString();
    }

    private static void SkipWhitespace(ReadOnlySpan<char> text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }

}
