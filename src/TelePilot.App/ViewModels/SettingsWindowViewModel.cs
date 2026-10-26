using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using TelePilot.App.Models;
using TelePilot.Core.CatchUp;
using TelePilot.Core.Channels;
using TelePilot.Recording;
using TelePilot.App.Services;

namespace TelePilot.App.ViewModels;

public sealed class SettingsWindowViewModel : ObservableObject
{
    private ChannelSortOption selectedSort;
    private AudioFormatOption selectedAudioFormat;
    private int selectedAudioBitrate;
    private string recordingDirectory;
    private string externalEpgUrl;
    private string ffmpegPath;
    private string fileNameTemplate;
    private bool wakeComputer;
    private string validationMessage = string.Empty;
    private readonly List<FavoriteChannelPreference> favorites;
    private readonly Dictionary<Guid, ArchiveDraft> archiveDrafts;
    private PlaylistSourceItemViewModel? selectedArchiveSource;
    private ArchiveModeOption selectedArchiveMode;
    private string archiveRetentionDays = string.Empty;
    private string archiveCorrectionHours = "0";
    private bool overrideArchivePlaylist;

    public SettingsWindowViewModel(
        AppPreferences preferences,
        IReadOnlyList<PlaylistSourceItemViewModel>? archiveSources = null,
        Guid? selectedArchiveSourceId = null)
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
        externalEpgUrl = preferences.ExternalEpgUrl ?? string.Empty;
        ffmpegPath = preferences.Recording.FfmpegPath ?? string.Empty;
        fileNameTemplate = preferences.Recording.FileNameTemplate;
        wakeComputer = preferences.Recording.WakeComputerForScheduledRecording;
        favorites = [.. preferences.Favorites];
        ArchiveModeOptions =
        [
            new ArchiveModeOption(CatchUpSourceMode.Automatic, "Automatycznie z playlisty"),
            new ArchiveModeOption(CatchUpSourceMode.Disabled, "Wyłączone"),
            new ArchiveModeOption(CatchUpSourceMode.FlussonicHls, "Flussonic HLS"),
            new ArchiveModeOption(CatchUpSourceMode.FlussonicTs, "Flussonic MPEG-TS"),
            new ArchiveModeOption(CatchUpSourceMode.Shift, "Shift (UTC)"),
            new ArchiveModeOption(CatchUpSourceMode.XtreamCodes, "Xtream Codes"),
        ];
        selectedArchiveMode = ArchiveModeOptions[0];
        ArchiveSources = (archiveSources ?? [])
            .Where(static source => source.Source.Id.Value != Guid.Empty)
            .DistinctBy(static source => source.Source.Id.Value)
            .ToArray();
        archiveDrafts = (preferences.ArchiveSources ?? [])
            .Where(static profile => profile is not null && profile.PlaylistSourceId != Guid.Empty)
            .GroupBy(static profile => profile.PlaylistSourceId)
            .ToDictionary(
                static group => group.Key,
                static group => ArchiveDraft.FromSettings(
                    AppPreferencesService.NormalizeArchiveSourceSettings(group.Last().Settings)));
        SelectedArchiveSource = ArchiveSources.FirstOrDefault(source => source.Source.Id.Value == selectedArchiveSourceId)
            ?? (ArchiveSources.Count > 0 ? ArchiveSources[0] : null);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, null));
    }

    public event EventHandler<AppPreferences?>? CloseRequested;

    public IReadOnlyList<ChannelSortOption> SortOptions { get; }

    public IReadOnlyList<AudioFormatOption> AudioFormats { get; }

    public IReadOnlyList<int> AudioBitrates { get; }

    public IReadOnlyList<PlaylistSourceItemViewModel> ArchiveSources { get; }

    public IReadOnlyList<ArchiveModeOption> ArchiveModeOptions { get; }

    public bool HasArchiveSources => ArchiveSources.Count > 0;

    public bool IsArchiveManual => SelectedArchiveSource is not null
        && SelectedArchiveMode.Mode is not (CatchUpSourceMode.Automatic or CatchUpSourceMode.Disabled);

    public string ArchiveSourceMessage => HasArchiveSources
        ? "Tryb automatyczny odczytuje archiwum z danych playlisty. Ustawienia ręczne dotyczą tylko wybranego źródła: podaj typ i liczbę dni potwierdzone przez dostawcę. Samo EPG ani zmiana ustawień nie tworzą nagrań na serwerze."
        : "Dodaj źródło playlisty w głównym oknie, aby ustawić jego archiwum.";

    public PlaylistSourceItemViewModel? SelectedArchiveSource
    {
        get => selectedArchiveSource;
        set
        {
            if (ReferenceEquals(selectedArchiveSource, value))
            {
                return;
            }

            StoreArchiveDraft();
            SetProperty(ref selectedArchiveSource, value);
            var draft = value is not null && archiveDrafts.TryGetValue(value.Source.Id.Value, out var existing)
                ? existing
                : ArchiveDraft.FromSettings(new CatchUpSourceSettings());
            SelectedArchiveMode = ArchiveModeOptions.First(option => option.Mode == draft.Mode);
            ArchiveRetentionDays = draft.RetentionDays;
            ArchiveCorrectionHours = draft.CorrectionHours;
            OverrideArchivePlaylist = draft.OverridePlaylist;
            OnPropertyChanged(nameof(IsArchiveManual));
        }
    }

    public ArchiveModeOption SelectedArchiveMode
    {
        get => selectedArchiveMode;
        set
        {
            if (value is not null && SetProperty(ref selectedArchiveMode, value))
            {
                OnPropertyChanged(nameof(IsArchiveManual));
            }
        }
    }

    public string ArchiveRetentionDays
    {
        get => archiveRetentionDays;
        set => SetProperty(ref archiveRetentionDays, value ?? string.Empty);
    }

    public string ArchiveCorrectionHours
    {
        get => archiveCorrectionHours;
        set => SetProperty(ref archiveCorrectionHours, value ?? string.Empty);
    }

    public bool OverrideArchivePlaylist
    {
        get => overrideArchivePlaylist;
        set => SetProperty(ref overrideArchivePlaylist, value);
    }

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

    public string ExternalEpgUrl
    {
        get => externalEpgUrl;
        set
        {
            if (SetProperty(ref externalEpgUrl, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ExternalEpgPrivacyMessage));
            }
        }
    }

    public string ExternalEpgPrivacyMessage =>
        TelePilot.Core.Security.SensitiveDataRedactor.ContainsPotentialSecret(ExternalEpgUrl.Trim())
            ? "Adres zawiera parametry lub dane logowania. Będzie używany tylko do zamknięcia programu; po restarcie wpisz go ponownie."
            : "Adres bez parametrów i danych logowania zostanie zapisany w ustawieniach.";

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
        StoreArchiveDraft();
        List<ArchiveSourcePreference> archiveProfiles = [];
        foreach (var (sourceId, draft) in archiveDrafts)
        {
            if (!TryBuildArchiveSettings(draft, out var settings, out var error))
            {
                // Return to the invalid source so the user can correct a draft
                // left behind when selecting a different playlist.
                SelectedArchiveSource = ArchiveSources.FirstOrDefault(source => source.Source.Id.Value == sourceId)
                    ?? SelectedArchiveSource;
                ValidationMessage = error;
                return;
            }

            archiveProfiles.Add(new ArchiveSourcePreference(sourceId, settings));
        }

        var normalizedEpgUrl = AppPreferencesService.NormalizeExternalEpgUrl(ExternalEpgUrl);
        if (!string.IsNullOrWhiteSpace(ExternalEpgUrl) && normalizedEpgUrl is null)
        {
            ValidationMessage = "Wpisz prawidłowy adres HTTP lub HTTPS programu XMLTV albo pozostaw pole puste.";
            return;
        }

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
                ExternalEpgUrl = normalizedEpgUrl,
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
                ArchiveSources = archiveProfiles,
            });
    }

    private void StoreArchiveDraft()
    {
        if (selectedArchiveSource is not null)
        {
            archiveDrafts[selectedArchiveSource.Source.Id.Value] = new ArchiveDraft(
                SelectedArchiveMode.Mode,
                ArchiveRetentionDays,
                ArchiveCorrectionHours,
                OverrideArchivePlaylist);
        }
    }

    private static bool TryBuildArchiveSettings(
        ArchiveDraft draft,
        out CatchUpSourceSettings settings,
        out string error)
    {
        settings = new CatchUpSourceSettings { Mode = draft.Mode };
        error = string.Empty;
        if (draft.Mode is CatchUpSourceMode.Automatic or CatchUpSourceMode.Disabled)
        {
            settings = settings with
            {
                RetentionDays = TryReadArchiveNumber(draft.RetentionDays, out var rememberedDays)
                    && rememberedDays is >= 0.01 and <= 36500 ? rememberedDays : 0,
                CorrectionHours = TryReadArchiveNumber(draft.CorrectionHours, out var rememberedCorrection)
                    && rememberedCorrection is >= -24 and <= 24 ? rememberedCorrection : 0,
                OverridePlaylist = draft.OverridePlaylist,
            };
            return true;
        }

        if (!TryReadArchiveNumber(draft.RetentionDays, out var days) || days is < 0.01 or > 36500)
        {
            error = "Podaj liczbę dni archiwum od 0,01 do 36500 dla wybranego źródła.";
            return false;
        }

        if (!TryReadArchiveNumber(draft.CorrectionHours, out var correction) || correction is < -24 or > 24)
        {
            error = "Podaj korektę czasu archiwum od -24 do 24 godzin dla wybranego źródła.";
            return false;
        }

        settings = new CatchUpSourceSettings
        {
            Mode = draft.Mode,
            RetentionDays = days,
            CorrectionHours = correction,
            OverridePlaylist = draft.OverridePlaylist,
        };
        return true;
    }

    private static bool TryReadArchiveNumber(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private sealed record ArchiveDraft(
        CatchUpSourceMode Mode,
        string RetentionDays,
        string CorrectionHours,
        bool OverridePlaylist)
    {
        public static ArchiveDraft FromSettings(CatchUpSourceSettings settings) => new(
            settings.Mode,
            settings.RetentionDays > 0 ? settings.RetentionDays.ToString(CultureInfo.CurrentCulture) : string.Empty,
            settings.CorrectionHours.ToString(CultureInfo.CurrentCulture),
            settings.OverridePlaylist);
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

public sealed record ArchiveModeOption(CatchUpSourceMode Mode, string Name)
{
    public override string ToString() => Name;
}
