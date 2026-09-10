namespace TelePilot.Infrastructure;

public sealed class AppDataPaths
{
    public AppDataPaths(string applicationName = "TelePilot")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        RootDirectory = Path.Combine(baseDirectory, applicationName);
        SettingsFile = Path.Combine(RootDirectory, "settings.json");
        PreferencesFile = Path.Combine(RootDirectory, "preferences.json");
        ScheduledRecordingsFile = Path.Combine(RootDirectory, "scheduled-recordings.json");
        CacheDirectory = Path.Combine(RootDirectory, "cache");
        LogDirectory = Path.Combine(RootDirectory, "logs");
        ManagedPlaylistsDirectory = Path.Combine(RootDirectory, "playlists");
    }

    public string RootDirectory { get; }

    public string SettingsFile { get; }

    public string PreferencesFile { get; }

    public string ScheduledRecordingsFile { get; }

    public string CacheDirectory { get; }

    public string LogDirectory { get; }

    public string ManagedPlaylistsDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ManagedPlaylistsDirectory);
    }
}
