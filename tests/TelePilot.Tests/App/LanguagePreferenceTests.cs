using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Infrastructure;

[assembly: AssemblyFixture(typeof(TelePilot.Tests.App.PolishBaselineFixture))]

namespace TelePilot.Tests.App;

// Legacy accessibility tests assert the Polish wording. Keep that baseline
// independent of the Windows runner's display language.
public sealed class PolishBaselineFixture : IDisposable
{
    private readonly CultureInfo? previous = CultureInfo.DefaultThreadCurrentUICulture;

    public PolishBaselineFixture() =>
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");

    public void Dispose() => CultureInfo.DefaultThreadCurrentUICulture = previous;
}

public sealed class LanguagePreferenceTests
{
    [Theory]
    [InlineData(null, "pl-PL", "pl-PL")]
    [InlineData("system", "de-DE", "en-US")]
    [InlineData("en", "pl-PL", "en-US")]
    [InlineData("pl", "en-US", "pl-PL")]
    [InlineData("invalid", "en-GB", "en-US")]
    public void Resolve_UsesExplicitChoiceOrSystemWithEnglishFallback(string? preference, string system, string expected) =>
        Assert.Equal(expected, LanguagePreference.Resolve(preference, CultureInfo.GetCultureInfo(system)).Name);

    [Fact]
    public async Task PreferenceRoundTrip_PreservesChoiceAndExistingFavorites()
    {
        var paths = new AppDataPaths(Path.Combine(Path.GetTempPath(), $"telepilot-language-{Guid.NewGuid():N}"));
        try
        {
            Assert.True(LanguagePreference.IsNewProfile(paths));
            var favorite = new FavoriteChannelPreference(Guid.NewGuid(), Guid.NewGuid(), "Test Channel");
            using var store = LanguagePreference.OpenStore(paths);
            var preferences = new AppPreferencesService(store);
            await preferences.SaveAsync(new AppPreferences { Language = "en", Favorites = [favorite] });
            Assert.False(LanguagePreference.IsNewProfile(paths));

            var reloaded = new AppPreferencesService(store);
            await reloaded.LoadAsync();
            Assert.Equal("en", reloaded.Current.Language);
            Assert.Equal(favorite, Assert.Single(reloaded.Current.Favorites));
            var settings = new SettingsWindowViewModel(reloaded.Current);
            Assert.Equal("en", settings.SelectedLanguage.Code);
        }
        finally
        {
            if (Directory.Exists(paths.RootDirectory))
            {
                Directory.Delete(paths.RootDirectory, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void EnglishWindows_LocalizeVisibleAndAccessibleText()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var main = new MainWindow();
            var manager = new PlaylistManagerWindow();
            var settings = new SettingsWindow();
            var urlDialog = new UrlSourceDialog();
            Assert.Contains("Accessible IPTV Player", main.Title, StringComparison.Ordinal);
            Assert.Equal("Playlist Manager", AutomationProperties.GetName(manager));
            Assert.Equal("Remove selected source",
                AutomationProperties.GetName(manager.FindControl<Button>("RemoveSourceButton")!));
            Assert.Equal("Settings — TelePilot", settings.Title);
            Assert.Equal("Add playlist from URL", urlDialog.Title);
            Assert.Contains(new KeyboardShortcutsViewModel().Shortcuts, item => item.Action == "Play selected channel");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
