namespace TelePilot.Playlists;

internal sealed record M3uAttributeParseResult(
    Dictionary<string, string> Attributes,
    bool HadMalformedAttribute);

internal static class M3uAttributeParser
{
    public static M3uAttributeParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

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

            var key = text[keyStart..index].Trim();
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
                attributes[key] = string.Empty;
                break;
            }

            string value;
            if (text[index] is '"' or '\'')
            {
                var quote = text[index++];
                var builder = new System.Text.StringBuilder();
                var terminated = false;
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
                        break;
                    }
                    else
                    {
                        builder.Append(character);
                    }
                }

                value = builder.ToString();
                malformed |= !terminated;
            }
            else
            {
                var valueStart = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                value = text[valueStart..index];
            }

            attributes[key] = value;
        }

        return new M3uAttributeParseResult(attributes, malformed);
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }

}
