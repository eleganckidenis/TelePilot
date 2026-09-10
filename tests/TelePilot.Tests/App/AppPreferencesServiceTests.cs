using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.Core.Channels;
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

    private sealed class MemoryStore(AppPreferences settings) : ISettingsStore<AppPreferences>
    {
        public bool FailSave { get; init; }

        public Task<AppPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(AppPreferences value, CancellationToken cancellationToken = default)
        {
            if (FailSave)
            {
                throw new IOException("test");
            }

            settings = value;
            return Task.CompletedTask;
        }
    }
}
