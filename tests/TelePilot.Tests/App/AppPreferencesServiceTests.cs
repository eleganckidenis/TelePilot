using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.Core.Channels;
using TelePilot.Core.CatchUp;
using TelePilot.Infrastructure;
using TelePilot.Recording;

namespace TelePilot.Tests.App;

public sealed class AppPreferencesServiceTests
{
    [Fact]
    public async Task Load_NormalizesInvalidValuesAndRemovesDuplicateFavorites()
    {
        var sourceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var store = new MemoryStore(new AppPreferences
        {
            ChannelSortMode = (ChannelSortMode)999,
            Recording = new RecordingPreferences
            {
                OutputDirectory = " ",
                AudioFormat = (AudioOutputFormat)999,
                AudioBitrateKbps = 999,
                FileNameTemplate = " ",
            },
            Favorites =
            [
                null!,
                new FavoriteChannelPreference(sourceId, channelId, " Kanał "),
                new FavoriteChannelPreference(sourceId, channelId, "Kanał nowy"),
                new FavoriteChannelPreference(Guid.Empty, Guid.NewGuid(), "Błędny"),
            ],
        });
        var service = new AppPreferencesService(store);

        await service.LoadAsync();

        Assert.Equal(ChannelSortMode.SourceOrder, service.Current.ChannelSortMode);
        Assert.Equal(AudioOutputFormat.Mp3, service.Current.Recording.AudioFormat);
        Assert.Equal(192, service.Current.Recording.AudioBitrateKbps);
        Assert.Equal(SafeFileNameGenerator.DefaultTemplate, service.Current.Recording.FileNameTemplate);
        Assert.False(string.IsNullOrWhiteSpace(service.Current.Recording.OutputDirectory));
        var favorite = Assert.Single(service.Current.Favorites);
        Assert.Equal("Kanał nowy", favorite.ChannelName);
    }

    [Fact]
    public async Task Save_UpdatesCurrentOnlyAfterStoreSucceeds()
    {
        var store = new MemoryStore(AppPreferences.CreateDefault()) { FailSave = true };
        var service = new AppPreferencesService(store);
        await service.LoadAsync();
        var before = service.Current;

        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(
            new AppPreferences { ChannelSortMode = ChannelSortMode.Name }));

        Assert.Same(before, service.Current);
    }

    [Theory]
    [InlineData(" https://epg.example.test/program.xml.gz ", "https://epg.example.test/program.xml.gz")]
    [InlineData("file:///guide.xml", null)]
    [InlineData("relative.xml", null)]
    [InlineData(" ", null)]
    public async Task Load_NormalizesOptionalExternalEpgUrl(string value, string? expected)
    {
        var service = new AppPreferencesService(new MemoryStore(new AppPreferences { ExternalEpgUrl = value }));

        await service.LoadAsync();

        Assert.Equal(expected, service.Current.ExternalEpgUrl);
    }

    [Theory]
    [InlineData("https://epg.example.test/program.xml", true)]
    [InlineData("https://epg.example.test/program.xml?key=synthetic", false)]
    [InlineData("https://synthetic:password@epg.example.test/program.xml", false)]
    public async Task Save_ExternalEpgUrlPersistsOnlyWithoutPotentialSecrets(string url, bool persists)
    {
        var store = new MemoryStore(AppPreferences.CreateDefault());
        var service = new AppPreferencesService(store);

        await service.SaveAsync(new AppPreferences { ExternalEpgUrl = url });

        Assert.Equal(url, service.Current.ExternalEpgUrl);
        Assert.Equal(persists ? url : null, store.Saved?.ExternalEpgUrl);
        var reloaded = new AppPreferencesService(store);
        await reloaded.LoadAsync();
        Assert.Equal(persists ? url : null, reloaded.Current.ExternalEpgUrl);
    }

    [Fact]
    public async Task Load_OlderPreferencesWithoutArchiveProfilesRemainAutomatic()
    {
        var older = System.Text.Json.JsonSerializer.Deserialize<AppPreferences>("{\"Favorites\":[]}")!;
        var service = new AppPreferencesService(new MemoryStore(older));

        await service.LoadAsync();

        Assert.Empty(service.Current.ArchiveSources);
    }

    [Fact]
    public async Task Load_ArchiveProfilesRemoveInvalidIdsAndUseLastDuplicate()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var store = new MemoryStore(new AppPreferences
        {
            ArchiveSources =
            [
                null!,
                new ArchiveSourcePreference(Guid.Empty, new CatchUpSourceSettings()),
                new ArchiveSourcePreference(firstId, new CatchUpSourceSettings { Mode = CatchUpSourceMode.Disabled }),
                new ArchiveSourcePreference(secondId, new CatchUpSourceSettings { Mode = (CatchUpSourceMode)999 }),
                new ArchiveSourcePreference(firstId, new CatchUpSourceSettings { Mode = CatchUpSourceMode.FlussonicHls, RetentionDays = 7 }),
            ],
        });
        var service = new AppPreferencesService(store);

        await service.LoadAsync();

        Assert.Equal(2, service.Current.ArchiveSources.Count);
        Assert.Equal(7, service.Current.ArchiveSources.Single(profile => profile.PlaylistSourceId == firstId).Settings.RetentionDays);
        Assert.Equal(CatchUpSourceMode.Automatic, service.Current.ArchiveSources.Single(profile => profile.PlaylistSourceId == secondId).Settings.Mode);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-2, 0)]
    [InlineData(36501, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(7, double.NaN)]
    [InlineData(7, -25)]
    [InlineData(7, 25)]
    public async Task Load_InvalidManualArchiveProfileIsInactiveWithoutAssumedDays(double days, double correction)
    {
        var store = new MemoryStore(new AppPreferences
        {
            ArchiveSources = [new ArchiveSourcePreference(Guid.NewGuid(), new CatchUpSourceSettings
            {
                Mode = CatchUpSourceMode.FlussonicHls,
                RetentionDays = days,
                CorrectionHours = correction,
                OverridePlaylist = true,
            })],
        });
        var service = new AppPreferencesService(store);

        await service.LoadAsync();

        var profile = Assert.Single(service.Current.ArchiveSources).Settings;
        Assert.Equal(CatchUpSourceMode.Disabled, profile.Mode);
        Assert.Equal(0, profile.RetentionDays);
        Assert.False(profile.OverridePlaylist);
    }

    [Fact]
    public async Task Save_PreservesPerSourceProfilesWhileRemovingSecretEpgUrl()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var profiles = new List<ArchiveSourcePreference>
        {
            new(firstId, new CatchUpSourceSettings { Mode = CatchUpSourceMode.FlussonicHls, RetentionDays = 3, CorrectionHours = -1 }),
            new(secondId, new CatchUpSourceSettings { Mode = CatchUpSourceMode.XtreamCodes, RetentionDays = 7, OverridePlaylist = true }),
        };
        var store = new MemoryStore(AppPreferences.CreateDefault());
        var service = new AppPreferencesService(store);

        await service.SaveAsync(new AppPreferences
        {
            ExternalEpgUrl = "https://epg.example.test/guide.xml?token=synthetic",
            ArchiveSources = profiles,
        });

        Assert.NotNull(store.Saved);
        Assert.Equal(profiles, store.Saved.ArchiveSources);
        var json = System.Text.Json.JsonSerializer.Serialize(store.Saved);
        Assert.Contains(firstId.ToString(), json);
        Assert.DoesNotContain("synthetic", json, StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Location", json, StringComparison.Ordinal);
        var reloaded = new AppPreferencesService(store);
        await reloaded.LoadAsync();
        Assert.Equal(profiles, reloaded.Current.ArchiveSources);
    }

    private sealed class MemoryStore(AppPreferences settings) : ISettingsStore<AppPreferences>
    {
        public bool FailSave { get; init; }

        public AppPreferences? Saved { get; private set; }

        public Task<AppPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(AppPreferences value, CancellationToken cancellationToken = default)
        {
            if (FailSave)
            {
                throw new IOException("test");
            }

            settings = value;
            Saved = value;
            return Task.CompletedTask;
        }
    }
}
