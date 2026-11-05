using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using TelePilot.App.Models;
using TelePilot.Core.CatchUp;
using TelePilot.Core.Channels;
using TelePilot.Recording;
using TelePilot.App.Services;
using static TelePilot.Core.Localization.Localizer;

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
    private string bufferSeconds, bufferMaximum, variantCap;
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
        LanguageOptions = LanguagePreference.Options();
        SelectedLanguage = LanguageOptions.First(option => option.Code == (LanguagePreference.Normalize(preferences.Language) ?? LanguagePreference.SystemLanguage));
        var buffering = preferences.Buffering.Normalize();
        bufferSeconds = buffering.Seconds.ToString(CultureInfo.CurrentCulture);
        bufferMaximum = buffering.MaximumSeconds.ToString(CultureInfo.CurrentCulture);
        variantCap = buffering.VariantMaxMbps.ToString(CultureInfo.CurrentCulture);
        SortOptions =
        [
            new ChannelSortOption(ChannelSortMode.SourceOrder, T("Kolejność z playlisty", "Playlist order")),
            new ChannelSortOption(ChannelSortMode.Name, T("Nazwa kanału", "Channel name")),
            new ChannelSortOption(ChannelSortMode.GroupThenName, T("Grupa, potem nazwa", "Group, then name")),
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
            new ArchiveModeOption(CatchUpSourceMode.Automatic, T("Automatycznie z playlisty", "Automatic from playlist")),
            new ArchiveModeOption(CatchUpSourceMode.Disabled, T("Wyłączone", "Off")),
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

    internal bool FocusArchiveOnOpen { get; init; }

    public event EventHandler<AppPreferences?>? CloseRequested;

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public LanguageOption SelectedLanguage { get; set; }

    public IReadOnlyList<ChannelSortOption> SortOptions { get; }

    public IReadOnlyList<AudioFormatOption> AudioFormats { get; }

    public IReadOnlyList<int> AudioBitrates { get; }

    public IReadOnlyList<PlaylistSourceItemViewModel> ArchiveSources { get; }

    public IReadOnlyList<ArchiveModeOption> ArchiveModeOptions { get; }

    public bool HasArchiveSources => ArchiveSources.Count > 0;

    public bool IsArchiveManual => SelectedArchiveSource is not null
        && SelectedArchiveMode.Mode is not (CatchUpSourceMode.Automatic or CatchUpSourceMode.Disabled);

    public string ArchiveSourceMessage => HasArchiveSources
        ? T("Tryb automatyczny odczytuje archiwum z danych playlisty. Ustawienia ręczne dotyczą tylko wybranego źródła: podaj typ i liczbę dni potwierdzone przez dostawcę. Samo EPG ani zmiana ustawień nie tworzą nagrań na serwerze.", "Automatic mode reads archive settings from the playlist. Manual settings apply to the selected source: enter the type and number of days your provider confirms. EPG and settings cannot create recordings on the server.")
        : T("Dodaj źródło w menedżerze playlist, aby ustawić jego archiwum.", "Add a source in Playlist Manager to configure its archive.");

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
            ? T("Adres zawiera parametry lub dane logowania. Będzie używany tylko do zamknięcia programu; po restarcie wpisz go ponownie.", "The address contains parameters or sign-in details. It will be used until you close the app; enter it again after restarting.")
            : T("Adres bez parametrów i danych logowania zostanie zapisany w ustawieniach.", "Addresses without parameters or sign-in details are saved in Settings.");

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

    public string BufferSeconds { get => bufferSeconds; set => SetProperty(ref bufferSeconds, value ?? string.Empty); }
    public string BufferMaximum { get => bufferMaximum; set => SetProperty(ref bufferMaximum, value ?? string.Empty); }
    public string VariantCap { get => variantCap; set => SetProperty(ref variantCap, value ?? string.Empty); }

    public string ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private void Save()
    {
        if (!TryReadArchiveNumber(BufferSeconds, out var seconds) || seconds <= 0 ||
            !TryReadArchiveNumber(BufferMaximum, out var maximum) || maximum < seconds || maximum > 300 ||
            !TryReadArchiveNumber(VariantCap, out var cap) || cap < 0 || cap > 500)
        {
            ValidationMessage = T("Podaj dodatni bufor i maksimum do 300 sekund, nie mniejsze niż bufor. Limit HLS: od 0 do 500 Mb/s; 0 wyłącza limit.", "Enter a positive buffer and a maximum of up to 300 seconds, at least as large as the buffer. The HLS limit must be 0 to 500 Mbps; 0 disables it.");
            return;
        }
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
            ValidationMessage = T("Wpisz prawidłowy adres HTTP lub HTTPS programu XMLTV albo pozostaw pole puste.", "Enter a valid HTTP or HTTPS address for the XMLTV guide, or leave the field blank.");
            return;
        }

        if (!TryNormalizePath(RecordingDirectory, requireFile: false, out var outputDirectory))
        {
            ValidationMessage = T("Wpisz prawidłowy folder nagrań.", "Enter a valid recording folder.");
            return;
        }

        string? normalizedFfmpeg = null;
        if (!string.IsNullOrWhiteSpace(FfmpegPath))
        {
            if (!TryNormalizePath(FfmpegPath, requireFile: true, out normalizedFfmpeg))
            {
                ValidationMessage = T("Wskazany plik FFmpeg nie istnieje.", "The selected FFmpeg file does not exist.");
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(FileNameTemplate))
        {
            ValidationMessage = T("Format nazwy pliku nie może być pusty.", "The file name format cannot be empty.");
            return;
        }

        ValidationMessage = string.Empty;
        CloseRequested?.Invoke(
            this,
            new AppPreferences
            {
                Language = SelectedLanguage?.Code ?? LanguagePreference.SystemLanguage,
                ChannelSortMode = SelectedSort.Mode,
                Buffering = new TelePilot.Core.Models.PlaybackBufferSettings(seconds, maximum, cap).Normalize(),
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
            error = T("Podaj liczbę dni archiwum od 0,01 do 36500 dla wybranego źródła.", "Enter the number of archive days, from 0.01 to 36500, for the selected source.");
            return false;
        }

        if (!TryReadArchiveNumber(draft.CorrectionHours, out var correction) || correction is < -24 or > 24)
        {
            error = T("Podaj korektę czasu archiwum od -24 do 24 godzin dla wybranego źródła.", "Enter an archive time correction between -24 and 24 hours for the selected source.");
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
