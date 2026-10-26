using TelePilot.App.Models;
using TelePilot.App.ViewModels;
using TelePilot.Core.CatchUp;
using TelePilot.Core.Models;

namespace TelePilot.Tests.App;

public sealed class SettingsWindowViewModelTests
{
    [Theory]
    [InlineData(" https://epg.example.test/guide.xml.gz ", "https://epg.example.test/guide.xml.gz")]
    [InlineData(" ", null)]
    public void Save_ValidatesAndTrimsOptionalExternalEpgUrl(string input, string? expected)
    {
        var viewModel = new SettingsWindowViewModel(AppPreferences.CreateDefault()) { ExternalEpgUrl = input };
        AppPreferences? saved = null;
        viewModel.CloseRequested += (_, result) => saved = result;

        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(expected, saved.ExternalEpgUrl);
        Assert.Empty(viewModel.ValidationMessage);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("file:///guide.xml")]
    [InlineData("ftp://epg.example.test/guide.xml")]
    public void Save_InvalidEpgUrlLeavesDialogOpenWithoutEchoingInput(string input)
    {
        var viewModel = new SettingsWindowViewModel(AppPreferences.CreateDefault()) { ExternalEpgUrl = input };
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.Contains("HTTP", viewModel.ValidationMessage);
        Assert.DoesNotContain(input, viewModel.ValidationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretUrl_PrivacyNoticeExplainsSessionOnlyUseWithoutEchoingUrl()
    {
        var viewModel = new SettingsWindowViewModel(AppPreferences.CreateDefault())
        {
            ExternalEpgUrl = "https://epg.example.test/guide.xml?token=synthetic",
        };

        Assert.Contains("po restarcie", viewModel.ExternalEpgPrivacyMessage);
        Assert.DoesNotContain("synthetic", viewModel.ExternalEpgPrivacyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_StoresIndependentSourceDraftsAndPreservesUnlistedProfiles()
    {
        var first = CreateSource("Pierwsza playlista");
        var second = CreateSource("Druga playlista");
        var unlistedId = Guid.NewGuid();
        var unlistedSettings = new CatchUpSourceSettings { Mode = CatchUpSourceMode.Disabled };
        var preferences = new AppPreferences
        {
            Recording = RecordingPreferences.CreateDefault(),
            ArchiveSources = [new ArchiveSourcePreference(unlistedId, unlistedSettings)],
        };
        var viewModel = new SettingsWindowViewModel(preferences, [first, second], second.Source.Id.Value);
        Assert.Same(second, viewModel.SelectedArchiveSource);
        Assert.Empty(viewModel.ArchiveRetentionDays);

        viewModel.SelectedArchiveMode = viewModel.ArchiveModeOptions.Single(option => option.Mode == CatchUpSourceMode.FlussonicHls);
        viewModel.ArchiveRetentionDays = "3,5";
        viewModel.ArchiveCorrectionHours = "-1,5";
        viewModel.OverrideArchivePlaylist = true;
        viewModel.SelectedArchiveSource = first;
        Assert.Empty(viewModel.ArchiveRetentionDays);
        Assert.False(viewModel.OverrideArchivePlaylist);
        viewModel.SelectedArchiveMode = viewModel.ArchiveModeOptions.Single(option => option.Mode == CatchUpSourceMode.Shift);
        viewModel.ArchiveRetentionDays = "7";
        viewModel.SelectedArchiveSource = second;
        Assert.Equal("3,5", viewModel.ArchiveRetentionDays);
        Assert.True(viewModel.OverrideArchivePlaylist);
        AppPreferences? saved = null;
        viewModel.CloseRequested += (_, result) => saved = result;

        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(3, saved.ArchiveSources.Count);
        var secondProfile = saved.ArchiveSources.Single(profile => profile.PlaylistSourceId == second.Source.Id.Value).Settings;
        Assert.Equal(CatchUpSourceMode.FlussonicHls, secondProfile.Mode);
        Assert.Equal(3.5, secondProfile.RetentionDays);
        Assert.Equal(-1.5, secondProfile.CorrectionHours);
        Assert.True(secondProfile.OverridePlaylist);
        Assert.Equal(7, saved.ArchiveSources.Single(profile => profile.PlaylistSourceId == first.Source.Id.Value).Settings.RetentionDays);
        Assert.Equal(unlistedSettings, saved.ArchiveSources.Single(profile => profile.PlaylistSourceId == unlistedId).Settings);
        Assert.Single(preferences.ArchiveSources);
    }

    [Fact]
    public void Cancel_DiscardsChangesWithoutMutatingPreferences()
    {
        var source = CreateSource("Playlista");
        var original = new CatchUpSourceSettings { Mode = CatchUpSourceMode.FlussonicTs, RetentionDays = 5 };
        var preferences = new AppPreferences
        {
            Recording = RecordingPreferences.CreateDefault(),
            ArchiveSources = [new ArchiveSourcePreference(source.Source.Id.Value, original)],
        };
        var viewModel = new SettingsWindowViewModel(preferences, [source]);
        AppPreferences? result = preferences;
        viewModel.CloseRequested += (_, value) => result = value;
        viewModel.ArchiveRetentionDays = "30";
        viewModel.OverrideArchivePlaylist = true;

        viewModel.CancelCommand.Execute(null);

        Assert.Null(result);
        Assert.Same(original, Assert.Single(preferences.ArchiveSources).Settings);
        Assert.Equal(5, original.RetentionDays);
        Assert.False(original.OverridePlaylist);
    }

    [Theory]
    [InlineData("", "0")]
    [InlineData("0", "0")]
    [InlineData("-1", "0")]
    [InlineData("36501", "0")]
    [InlineData("NaN", "0")]
    [InlineData("Infinity", "0")]
    [InlineData("1,000.5", "0")]
    [InlineData("7", "")]
    [InlineData("7", "-24.01")]
    [InlineData("7", "24.01")]
    [InlineData("7", "NaN")]
    public void Save_InvalidManualArchiveNumbersKeepDialogOpen(string days, string correction)
    {
        var source = CreateSource("Playlista");
        var viewModel = new SettingsWindowViewModel(AppPreferences.CreateDefault(), [source]);
        viewModel.SelectedArchiveMode = viewModel.ArchiveModeOptions.Single(option => option.Mode == CatchUpSourceMode.FlussonicHls);
        viewModel.ArchiveRetentionDays = days;
        viewModel.ArchiveCorrectionHours = correction;
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.NotEmpty(viewModel.ValidationMessage);
    }

    [Fact]
    public void Save_InvalidDraftInAnotherSourceReturnsToThatSource()
    {
        var first = CreateSource("Pierwsza playlista");
        var second = CreateSource("Druga playlista");
        var viewModel = new SettingsWindowViewModel(AppPreferences.CreateDefault(), [first, second]);
        viewModel.SelectedArchiveMode = viewModel.ArchiveModeOptions.Single(option => option.Mode == CatchUpSourceMode.Shift);
        viewModel.ArchiveRetentionDays = "błąd";
        viewModel.SelectedArchiveSource = second;

        viewModel.SaveCommand.Execute(null);

        Assert.Same(first, viewModel.SelectedArchiveSource);
        Assert.Equal("błąd", viewModel.ArchiveRetentionDays);
        Assert.Contains("dni", viewModel.ValidationMessage);
    }

    [Theory]
    [InlineData(CatchUpSourceMode.Automatic)]
    [InlineData(CatchUpSourceMode.Disabled)]
    public void Save_AutomaticAndDisabledDoNotRequireInventedRetention(CatchUpSourceMode mode)
    {
        var source = CreateSource("Playlista");
        var viewModel = new SettingsWindowViewModel(AppPreferences.CreateDefault(), [source]);
        viewModel.SelectedArchiveMode = viewModel.ArchiveModeOptions.Single(option => option.Mode == mode);
        AppPreferences? saved = null;
        viewModel.CloseRequested += (_, result) => saved = result;

        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        var profile = Assert.Single(saved.ArchiveSources).Settings;
        Assert.Equal(mode, profile.Mode);
        Assert.Equal(0, profile.RetentionDays);
        Assert.False(viewModel.IsArchiveManual);
    }

    [Fact]
    public void Constructor_WithoutSourcesHasClearDisabledState()
    {
        var viewModel = new SettingsWindowViewModel(AppPreferences.CreateDefault());

        Assert.False(viewModel.HasArchiveSources);
        Assert.False(viewModel.IsArchiveManual);
        Assert.Null(viewModel.SelectedArchiveSource);
        Assert.Contains("Dodaj źródło", viewModel.ArchiveSourceMessage);
    }

    private static PlaylistSourceItemViewModel CreateSource(string name) => new(
        new PlaylistSource(PlaylistSourceId.New(), name, PlaylistSourceKind.Url,
            "https://playlist.example.test/list.m3u?token=synthetic"));
}
