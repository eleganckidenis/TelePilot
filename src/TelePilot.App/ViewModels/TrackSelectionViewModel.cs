using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TelePilot.App.ViewModels;

public sealed class TrackSelectionViewModel : ObservableObject
{
    private readonly RelayCommand applyCommand;
    private TrackOptionViewModel? selectedTrack;

    public TrackSelectionViewModel(
        string title,
        string description,
        IEnumerable<TrackOptionViewModel> tracks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(tracks);

        Title = title;
        Description = description;
        Tracks = new ObservableCollection<TrackOptionViewModel>(tracks);
        SelectedTrack = Tracks.FirstOrDefault(track => track.IsCurrent) ?? Tracks.FirstOrDefault();
        applyCommand = new RelayCommand(Apply, () => SelectedTrack is not null);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke());
    }

    public event Action<int>? Accepted;

    public event Action? Cancelled;

    public string Title { get; }

    public string Description { get; }

    public ObservableCollection<TrackOptionViewModel> Tracks { get; }

    public TrackOptionViewModel? SelectedTrack
    {
        get => selectedTrack;
        set
        {
            if (SetProperty(ref selectedTrack, value))
            {
                applyCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public IRelayCommand ApplyCommand => applyCommand;

    public IRelayCommand CancelCommand { get; }

    private void Apply()
    {
        if (SelectedTrack is not null)
        {
            Accepted?.Invoke(SelectedTrack.Id);
        }
    }
}
