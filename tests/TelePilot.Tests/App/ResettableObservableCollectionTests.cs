using System.Collections.Specialized;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TelePilot.App.Accessibility;
using TelePilot.App.Collections;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;

namespace TelePilot.Tests.App;

public sealed class ResettableObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_LargeSnapshotRaisesOneResetAndSkipsIdenticalReplacement()
    {
        ResettableObservableCollection<int> collection = [];
        int[] replacement = Enumerable.Range(0, 10_000).ToArray();
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        var propertyChanges = new List<string?>();
        collection.CollectionChanged += (_, eventArgs) => collectionChanges.Add(eventArgs.Action);
        ((System.ComponentModel.INotifyPropertyChanged)collection).PropertyChanged +=
            (_, eventArgs) => propertyChanges.Add(eventArgs.PropertyName);

        collection.ReplaceAll(replacement);

        Assert.Equal(replacement, collection);
        Assert.Equal([NotifyCollectionChangedAction.Reset], collectionChanges);
        Assert.Equal([nameof(collection.Count), "Item[]"], propertyChanges);

        collectionChanges.Clear();
        propertyChanges.Clear();
        collection.ReplaceAll(replacement);

        Assert.Empty(collectionChanges);
        Assert.Empty(propertyChanges);
    }

    [AvaloniaFact]
    public void ReplaceAll_MainWindowRestoresFocusedSelectedChannelById()
    {
        ResettableObservableCollection<ChannelListItemViewModel> collection = [];
        ChannelListItemViewModel[] initial = Enumerable.Range(0, 100)
            .Select(index => new ChannelListItemViewModel(
                $"channel-{index:D3}",
                $"Channel {index:D3}",
                "Test group"))
            .ToArray();
        collection.ReplaceAll(initial);
        ChannelListItemViewModel selected = initial[50];
        MainWindow window = new();
        ListBox listBox = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
        listBox.ItemsSource = collection;
        listBox.SelectedItem = selected;
        FocusCoordinator coordinator = new();
        FocusRequest? requestedFocus = null;
        SetPrivateField(window, "focusCoordinator", coordinator);
        EventHandler startingHandler = CreatePrivateHandler<EventHandler>(
            window,
            "OnChannelItemsReplacementStarting");
        EventHandler completedHandler = CreatePrivateHandler<EventHandler>(
            window,
            "OnChannelItemsReplacementCompleted");
        EventHandler<FocusRequest> focusHandler = CreatePrivateHandler<EventHandler<FocusRequest>>(
            window,
            "OnFocusRequested");
        collection.ReplacementStarting += startingHandler;
        collection.ReplacementCompleted += completedHandler;
        coordinator.FocusRequested += focusHandler;
        coordinator.FocusRequested += (_, request) => requestedFocus = request;

        try
        {
            window.Show();
            window.Activate();
            listBox.ScrollIntoView(selected);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.IsType<ListBoxItem>(listBox.ContainerFromItem(selected)).Focus(
                NavigationMethod.Unspecified,
                KeyModifiers.None));

            collection.ReplaceAll(initial.Reverse());
            listBox.SelectedItem = selected;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(selected, listBox.SelectedItem);
            Assert.Equal(new FocusRequest(FocusTarget.ChannelList, selected.Id), requestedFocus);
            Assert.Same(
                Assert.IsType<ListBoxItem>(listBox.ContainerFromItem(selected)),
                window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            collection.ReplacementStarting -= startingHandler;
            collection.ReplacementCompleted -= completedHandler;
            coordinator.FocusRequested -= focusHandler;
            window.Close();
        }
    }

    private static TDelegate CreatePrivateHandler<TDelegate>(object target, string methodName)
        where TDelegate : Delegate
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");
        return method.CreateDelegate<TDelegate>(target);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        field.SetValue(target, value);
    }
}
