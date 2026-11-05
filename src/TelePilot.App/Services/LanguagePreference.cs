using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.App.Models;
using TelePilot.Core.Localization;
using TelePilot.Infrastructure;

namespace TelePilot.App.Services;

public sealed record LanguageOption(string Code, string Name);

internal static class LanguagePreference
{
    internal const string SystemLanguage = "system";

    internal static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "system" => SystemLanguage,
        "pl" => "pl",
        "en" => "en",
        _ => null,
    };

    internal static CultureInfo Resolve(string? choice, CultureInfo systemCulture) =>
        CultureInfo.GetCultureInfo(Normalize(choice) switch
        {
            "pl" => "pl-PL",
            "en" => "en-US",
            _ => systemCulture.TwoLetterISOLanguageName == "pl" ? "pl-PL" : "en-US",
        });

    internal static IReadOnlyList<LanguageOption> Options() =>
    [
        new(SystemLanguage, Localizer.T("Język systemu", "System language")),
        new("pl", "Polski"),
        new("en", "English"),
    ];

    internal static bool IsNewProfile(AppDataPaths paths) =>
        !File.Exists(paths.SettingsFile)
        && !File.Exists(paths.PreferencesFile)
        && !File.Exists(paths.ScheduledRecordingsFile);

    internal static string? ReadInstallerChoice()
    {
        try
        {
            return Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "installer-language.txt")));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static JsonSettingsStore<AppPreferences> OpenStore(AppDataPaths paths) =>
        new(paths.PreferencesFile, AppPreferences.CreateDefault,
            NullLogger<JsonSettingsStore<AppPreferences>>.Instance);

    internal static void Apply(string? choice, CultureInfo systemCulture)
    {
        var culture = Resolve(choice, systemCulture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
