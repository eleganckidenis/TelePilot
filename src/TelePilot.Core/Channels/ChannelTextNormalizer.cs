using System.Globalization;
using System.Text;

namespace TelePilot.Core.Channels;

public static class ChannelTextNormalizer
{
    public static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasWhiteSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var normalizedCharacter = character switch
            {
                'Ł' or 'ł' => 'l',
                _ => char.ToLowerInvariant(character),
            };

            if (char.IsWhiteSpace(normalizedCharacter))
            {
                if (!previousWasWhiteSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWasWhiteSpace = true;
                continue;
            }

            builder.Append(normalizedCharacter);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }
}
