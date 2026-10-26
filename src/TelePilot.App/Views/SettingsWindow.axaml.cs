using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.ComponentModel;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class SettingsWindow : Window
{
    private SettingsWindowViewModel? observedViewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        SortPicker.PreviousTabTarget = SaveButton;
        SortPicker.NextTabTarget = ExternalEpgUrlBox;
        ExternalEpgUrlBox.PreviousTabTarget = SortPicker;
        ExternalEpgUrlBox.NextTabTarget = RecordingDirectoryBox;
        RecordingDirectoryBox.PreviousTabTarget = ExternalEpgUrlBox;
        ArchiveSourcePicker.PreviousTabTarget = ExternalEpgUrlBox;
        ArchiveSourcePicker.NextTabTarget = ArchiveModePicker;
        ArchiveModePicker.PreviousTabTarget = ArchiveSourcePicker;
        ArchiveDaysBox.PreviousTabTarget = ArchiveModePicker;
        ArchiveDaysBox.NextTabTarget = ArchiveCorrectionBox;
        ArchiveCorrectionBox.PreviousTabTarget = ArchiveDaysBox;
        ArchiveCorrectionBox.NextTabTarget = ArchiveOverrideCheckBox;
        RecordingDirectoryBox.NextTabTarget = BrowseRecordingDirectoryButton;
        FfmpegPathBox.PreviousTabTarget = BrowseRecordingDirectoryButton;
        AudioFormatPicker.NextTabTarget = AudioBitratePicker;
        AudioBitratePicker.PreviousTabTarget = AudioFormatPicker;
        AudioBitratePicker.NextTabTarget = FileNameTemplateBox;
        FileNameTemplateBox.PreviousTabTarget = AudioBitratePicker;
        SaveButton.AddHandler(
            InputElement.KeyDownEvent,
            OnSaveButtonKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Opened += OnOpened;
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => ObserveViewModel(null);
    }

    private void OnSaveButtonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        Dispatcher.UIThread.Post(() => SortPicker.FocusSelector(), DispatcherPriority.Input);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        SortPicker.FocusSelector();
    }

    private void OnDataContextChanged(object? sender, EventArgs e) =>
        ObserveViewModel(DataContext as SettingsWindowViewModel);

    private void ObserveViewModel(SettingsWindowViewModel? viewModel)
    {
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        observedViewModel = viewModel;
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateArchiveTabTargets();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsWindowViewModel.IsArchiveManual)
            or nameof(SettingsWindowViewModel.SelectedArchiveSource))
        {
            UpdateArchiveTabTargets();
        }
    }

    private void UpdateArchiveTabTargets()
    {
        var hasSource = observedViewModel?.HasArchiveSources == true;
        var manual = observedViewModel?.IsArchiveManual == true;
        ExternalEpgUrlBox.NextTabTarget = hasSource ? ArchiveSourcePicker : RecordingDirectoryBox;
        ArchiveModePicker.NextTabTarget = manual ? ArchiveDaysBox : RecordingDirectoryBox;
        RecordingDirectoryBox.PreviousTabTarget = manual
            ? ArchiveOverrideCheckBox
            : hasSource ? ArchiveModePicker : ExternalEpgUrlBox;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SettingsWindowViewModel viewModel)
        {
            e.Handled = true;
            viewModel.CancelCommand.Execute(null);
        }
    }

    private async void OnBrowseRecordingDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsWindowViewModel viewModel)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Wybierz folder nagrań", AllowMultiple = false });
        var path = folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            viewModel.RecordingDirectory = path;
        }
    }

    private async void OnBrowseFfmpeg(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Wybierz ffmpeg.exe",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("FFmpeg") { Patterns = ["ffmpeg.exe", "ffmpeg"] },
                    FilePickerFileTypes.All,
                ],
            });
        var path = files.Count == 0 ? null : files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            viewModel.FfmpegPath = path;
        }
    }
}
