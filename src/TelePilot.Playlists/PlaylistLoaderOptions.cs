namespace TelePilot.Playlists;

public sealed class PlaylistLoaderOptions
{
    public const long DefaultMaximumContentBytes = 50L * 1024L * 1024L;

    public PlaylistLoaderOptions(
        long maximumContentBytes = DefaultMaximumContentBytes,
        bool allowHttpsToHttpRedirects = false)
    {
        if (maximumContentBytes is <= 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumContentBytes),
                "Maksymalny rozmiar playlisty musi być większy od zera i nie może przekraczać 2 GB.");
        }

        MaximumContentBytes = maximumContentBytes;
        AllowHttpsToHttpRedirects = allowHttpsToHttpRedirects;
    }

    public long MaximumContentBytes { get; }

    /// <summary>
    /// Gets a value indicating whether a server may redirect a playlist request from HTTPS to HTTP.
    /// This opt-in is intended for legacy IPTV providers. Error messages never include either URL.
    /// </summary>
    public bool AllowHttpsToHttpRedirects { get; }
}
