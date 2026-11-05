using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class PlaylistManagerWindow : Window
{
    internal PlaylistManagerAction InitialAction { get; init; }

    public PlaylistManagerWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        CloseButton.Click += (_, _) => Close();
        SourcesList.AddHandler(InputElement.KeyDownEvent, OnSourceKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyDownEvent, OnEscape, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.PlaylistSourceRemoved -= OnPlaylistSourceRemoved;
            }
        };
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.PlaylistSourceRemoved += OnPlaylistSourceRemoved;
        FocusSourceOrAdd();
        var command = InitialAction switch
        {
            PlaylistManagerAction.AddFile => viewModel.AddFileSourceCommand,
            PlaylistManagerAction.AddUrl => viewModel.AddUrlSourceCommand,
            _ => null,
        };
        if (command is not null)
        {
            await command.ExecuteAsync(null);
            if (IsVisible && IsActive)
            {
                FocusSourceOrAdd();
            }
        }
    }

    private async void OnSourceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None
            && DataContext is MainWindowViewModel viewModel && viewModel.ActivateSourceCommand.CanExecute(null))
        {
            e.Handled = true;
            await viewModel.ActivateSourceCommand.ExecuteAsync(null);
        }
    }

    private void OnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnPlaylistSourceRemoved(object? sender, EventArgs e)
    {
        if (IsVisible && IsActive)
        {
            FocusSourceOrAdd();
        }
    }

    private void FocusSourceOrAdd()
    {
        if (SourcesList.Items.Count == 0)
        {
            AddFileSourceButton.Focus();
        }
        else
        {
            UpdateLayout();
            if (SourcesList.SelectedItem is { } selection)
            {
                SourcesList.ScrollIntoView(selection);
                if (SourcesList.ContainerFromItem(selection) is Control row && row.Focus())
                {
                    return;
                }
            }
            SourcesList.Focus();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason != WindowCloseReason.WindowClosing || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // Keep the correct dialog owner while an operation can still open a
        // child dialog. Escape cancels a download and then allows closing.
        if (viewModel.IsLoading || viewModel.AddFileSourceCommand.IsRunning || viewModel.AddUrlSourceCommand.IsRunning
            || viewModel.OpenArchiveSettingsCommand.IsRunning || viewModel.ActivateSourceCommand.IsRunning || viewModel.RefreshSourceCommand.IsRunning || viewModel.RemoveSourceCommand.IsRunning)
        {
            e.Cancel = true;
            if (viewModel.CancelLoadingCommand.CanExecute(null))
            {
                viewModel.CancelLoadingCommand.Execute(null);
            }
        }
    }
}
