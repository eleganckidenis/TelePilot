using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelePilot.App.Models;
using TelePilot.Core.Channels;
using TelePilot.Recording;

namespace TelePilot.App.ViewModels;

public sealed class SettingsWindowViewModel : ObservableObject
{
    private ChannelSortOption selectedSort;
    private AudioFormatOption selectedAudioFormat;
    private int selectedAudioBitrate;
    private string recordingDirectory;
    private string ffmpegPath;
    private string fileNameTemplate;
    private bool wakeComputer;
    private string validationMessage = string.Empty;
    private readonly List<FavoriteChannelPreference> favorites;

    public SettingsWindowViewModel(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        SortOptions =
        [
            new ChannelSortOption(ChannelSortMode.SourceOrder, "Kolejność z playlisty"),
            new ChannelSortOption(ChannelSortMode.Name, "Nazwa kanału"),
            new ChannelSortOption(ChannelSortMode.GroupThenName, "Grupa, potem nazwa"),
        ];
        AudioFormats =
        [
            new AudioFormatOption(AudioOutputFormat.Mp3, "MP3"),
            new AudioFormatOption(AudioOutputFormat.M4aAac, "M4A / AAC"),
            new AudioFormatOption(AudioOutputFormat.Opus, "Opus"),
            new AudioFormatOption(AudioOutputFormat.Flac, "FLAC"),
            new AudioFormatOption(AudioOutputFormat.Wav, "WAV"),
        ];
        AudioBitrates = [64, 96, 128, 160, 192, 256, 320];
        selectedSort = SortOptions.First(option => option.Mode == preferences.ChannelSortMode);
        selectedAudioFormat = AudioFormats.First(option => option.Format == preferences.Recording.AudioFormat);
        selectedAudioBitrate = preferences.Recording.AudioBitrateKbps;
        recordingDirectory = preferences.Recording.OutputDirectory;
        ffmpegPath = preferences.Recording.FfmpegPath ?? string.Empty;
        fileNameTemplate = preferences.Recording.FileNameTemplate;
        wakeComputer = preferences.Recording.WakeComputerForScheduledRecording;
        favorites = [.. preferences.Favorites];
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, null));
    }

    public event EventHandler<AppPreferences?>? CloseRequested;

    public IReadOnlyList<ChannelSortOption> SortOptions { get; }

    public IReadOnlyList<AudioFormatOption> AudioFormats { get; }

    public IReadOnlyList<int> AudioBitrates { get; }

    public ChannelSortOption SelectedSort
    {
        get => selectedSort;
        set => SetProperty(ref selectedSort, value);
    }

    public AudioFormatOption SelectedAudioFormat
    {
        get => selectedAudioFormat;
        set => SetProperty(ref selectedAudioFormat, value);
    }

    public int SelectedAudioBitrate
    {
        get => selectedAudioBitrate;
        set => SetProperty(ref selectedAudioBitrate, value);
    }

    public string RecordingDirectory
    {
        get => recordingDirectory;
        set => SetProperty(ref recordingDirectory, value ?? string.Empty);
    }

    public string FfmpegPath
    {
        get => ffmpegPath;
        set => SetProperty(ref ffmpegPath, value ?? string.Empty);
    }

    public string FileNameTemplate
    {
        get => fileNameTemplate;
        set => SetProperty(ref fileNameTemplate, value ?? string.Empty);
    }

    public bool WakeComputer
    {
        get => wakeComputer;
        set => SetProperty(ref wakeComputer, value);
    }

    public string ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private void Save()
    {
        if (!TryNormalizePath(RecordingDirectory, requireFile: false, out var outputDirectory))
        {
            ValidationMessage = "Wpisz prawidłowy folder nagrań.";
            return;
        }

        string? normalizedFfmpeg = null;
        if (!string.IsNullOrWhiteSpace(FfmpegPath))
        {
            if (!TryNormalizePath(FfmpegPath, requireFile: true, out normalizedFfmpeg))
            {
                ValidationMessage = "Wskazany plik FFmpeg nie istnieje.";
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(FileNameTemplate))
        {
            ValidationMessage = "Format nazwy pliku nie może być pusty.";
            return;
        }

        ValidationMessage = string.Empty;
        CloseRequested?.Invoke(
            this,
            new AppPreferences
            {
                ChannelSortMode = SelectedSort.Mode,
                Recording = new RecordingPreferences
                {
                    OutputDirectory = outputDirectory!,
                    FfmpegPath = normalizedFfmpeg,
                    AudioFormat = SelectedAudioFormat.Format,
                    AudioBitrateKbps = SelectedAudioBitrate,
                    FileNameTemplate = FileNameTemplate.Trim(),
                    WakeComputerForScheduledRecording = WakeComputer,
                },
                Favorites = [.. favorites],
            });
    }

    private static bool TryNormalizePath(string value, bool requireFile, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(value.Trim());
            return !requireFile || File.Exists(normalized);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

public sealed record AudioFormatOption(AudioOutputFormat Format, string Name)
{
    public override string ToString() => Name;
}
