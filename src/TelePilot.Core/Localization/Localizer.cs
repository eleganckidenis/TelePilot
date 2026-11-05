using System.Globalization;

namespace TelePilot.Core.Localization;

public static class Localizer
{
    public static string T(string polish, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pl" ? polish : english;
}
