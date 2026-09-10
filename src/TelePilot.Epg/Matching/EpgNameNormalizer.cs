using System.Globalization;
using System.Text;

namespace TelePilot.Epg.Matching;

public static class EpgNameNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder result = new(value.Length);
        foreach (Rune rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            Rune lower = Rune.ToLowerInvariant(rune);
            if (lower.Value == 'ł')
            {
                result.Append('l');
            }
            else if (Rune.IsLetterOrDigit(lower))
            {
                result.Append(lower);
            }
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
