using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TelePilot.App.Controls;

/// <summary>
/// A non-editable selector backed by a standard Win32 COMBOBOX on Windows.
/// </summary>
/// <remarks>
/// A native <c>CBS_DROPDOWNLIST</c> is used when the control is attached to a
/// Win32 HWND. Avalonia previews, headless tests, and other platforms use a
/// regular Avalonia <see cref="ComboBox"/> with the same public properties.
/// </remarks>
public sealed class AccessibleComboBox : ContentControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<AccessibleComboBox, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<AccessibleComboBox, object?>(
            nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay,
            enableDataValidation: true);

    public static readonly StyledProperty<string?> DisplayMemberPathProperty =
        AvaloniaProperty.Register<AccessibleComboBox, string?>(nameof(DisplayMemberPath));

    private readonly ComboBox fallbackSelector;
    private readonly List<object?> itemSnapshot = [];
    private Control selector;
    private Win32ComboBoxHost? nativeSelector;
    private INotifyCollectionChanged? subscribedCollection;
    private bool isSynchronizingSelector;

    static AccessibleComboBox()
    {
        FocusableProperty.OverrideDefaultValue<AccessibleComboBox>(true);
        KeyboardNavigation.IsTabStopProperty.OverrideDefaultValue<AccessibleComboBox>(true);
        MinHeightProperty.OverrideDefaultValue<AccessibleComboBox>(32d);
        HorizontalContentAlignmentProperty.OverrideDefaultValue<AccessibleComboBox>(HorizontalAlignment.Stretch);
        VerticalContentAlignmentProperty.OverrideDefaultValue<AccessibleComboBox>(VerticalAlignment.Stretch);
    }

    public AccessibleComboBox()
    {
        fallbackSelector = CreateFallbackSelector();
        selector = fallbackSelector;
        Content = selector;

        // The child is the actual platform accessibility element. Keeping the
        // wrapper out of the control view prevents a duplicate combo box.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        RebuildItems(coerceMissingSelection: false);
        ApplyAutomationProperties();
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the property path used to obtain the text shown for an item.
    /// A null or empty path uses the item's <see cref="object.ToString"/> value.
    /// </summary>
    public string? DisplayMemberPath
    {
        get => GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>
    /// Gets whether the selector is currently backed by a native Win32 COMBOBOX.
    /// </summary>
    public bool IsUsingNativeSelector => ReferenceEquals(selector, nativeSelector);

    internal event EventHandler? TabNavigationStarting;

    /// <summary>
    /// Optional explicit targets used for Shift+Tab and Tab respectively.
    /// </summary>
    public Control? PreviousTabTarget { get; set; }

    public Control? NextTabTarget { get; set; }

    /// <summary>
    /// Moves keyboard focus to the active platform selector.
    /// </summary>
    public bool FocusSelector()
    {
        if (nativeSelector is not null && ReferenceEquals(selector, nativeSelector))
        {
            return nativeSelector.FocusNativeSelector();
        }

        return fallbackSelector.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToCollection();
        RebuildItems(coerceMissingSelection: true);
        SelectPlatformSelector();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromCollection();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.NewFocusedElement, this))
        {
            FocusSelector();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            UnsubscribeFromCollection();
            SubscribeToCollection();
            RebuildItems(coerceMissingSelection: true);
        }
        else if (change.Property == DisplayMemberPathProperty)
        {
            RebuildItems(coerceMissingSelection: false);
        }
        else if (change.Property == SelectedItemProperty)
        {
            SynchronizeSelection(change.GetNewValue<object?>());
        }
        else if (change.Property == AutomationProperties.NameProperty
                 || change.Property == AutomationProperties.HelpTextProperty
                 || change.Property == AutomationProperties.AutomationIdProperty)
        {
            ApplyAutomationProperties();
        }
    }

    private ComboBox CreateFallbackSelector()
    {
        var comboBox = new ComboBox
        {
            IsTabStop = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        comboBox.SelectionChanged += OnFallbackSelectionChanged;
        comboBox.AddHandler(
            InputElement.KeyDownEvent,
            OnFallbackKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        return comboBox;
    }

    private void SelectPlatformSelector()
    {
        IPlatformHandle? topLevelHandle = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle();
        bool canHostWin32Selector = OperatingSystem.IsWindows()
            && string.Equals(topLevelHandle?.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase);

        if (canHostWin32Selector)
        {
            UseNativeSelector();
        }
        else
        {
            UseFallbackSelector();
        }
    }

    private void UseNativeSelector()
    {
        if (nativeSelector is null)
        {
            nativeSelector = new Win32ComboBoxHost
            {
                IsTabStop = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            nativeSelector.NativeSelectionChanged += OnNativeSelectionChanged;
            nativeSelector.NativeTabNavigationRequested += OnNativeTabNavigationRequested;
            nativeSelector.NativeFocusReceived += OnNativeFocusReceived;
        }

        SwitchSelector(nativeSelector);
    }

    private void UseFallbackSelector() => SwitchSelector(fallbackSelector);

    private void SwitchSelector(Control replacement)
    {
        if (!ReferenceEquals(selector, replacement))
        {
            selector = replacement;
            Content = replacement;
        }

        ApplyItemsToSelector();
        SynchronizeSelection(SelectedItem);
        ApplyAutomationProperties();
    }

    private void SubscribeToCollection()
    {
        if (ItemsSource is not INotifyCollectionChanged collection
            || ReferenceEquals(collection, subscribedCollection))
        {
            return;
        }

        subscribedCollection = collection;
        collection.CollectionChanged += OnItemsCollectionChanged;
    }

    private void UnsubscribeFromCollection()
    {
        if (subscribedCollection is null)
        {
            return;
        }

        subscribedCollection.CollectionChanged -= OnItemsCollectionChanged;
        subscribedCollection = null;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildItems(coerceMissingSelection: true);
        }
        else
        {
            Dispatcher.UIThread.Post(
                () => RebuildItems(coerceMissingSelection: true),
                DispatcherPriority.Background);
        }
    }

    private void RebuildItems(bool coerceMissingSelection)
    {
        object? selectedItem = SelectedItem;
        itemSnapshot.Clear();
        if (ItemsSource is not null)
        {
            foreach (object? item in ItemsSource)
            {
                itemSnapshot.Add(item);
            }
        }

        if (coerceMissingSelection
            && ItemsSource is not null
            && selectedItem is not null
            && FindItemIndex(selectedItem) < 0)
        {
            selectedItem = null;
            SetCurrentValue(SelectedItemProperty, null);
        }

        ApplyItemsToSelector();
        SynchronizeSelection(selectedItem);
    }

    private void ApplyItemsToSelector()
    {
        isSynchronizingSelector = true;
        try
        {
            if (ReferenceEquals(selector, fallbackSelector))
            {
                fallbackSelector.DisplayMemberBinding = string.IsNullOrWhiteSpace(DisplayMemberPath)
                    ? null
                    : new Binding(DisplayMemberPath);
                fallbackSelector.ItemsSource = itemSnapshot.ToArray();
            }
            else if (nativeSelector is not null)
            {
                nativeSelector.Items = itemSnapshot.Select(GetDisplayText).ToArray();
            }
        }
        finally
        {
            isSynchronizingSelector = false;
        }
    }

    private void SynchronizeSelection(object? selectedItem)
    {
        if (isSynchronizingSelector)
        {
            return;
        }

        int selectedIndex = selectedItem is null ? -1 : FindItemIndex(selectedItem);
        isSynchronizingSelector = true;
        try
        {
            if (ReferenceEquals(selector, fallbackSelector))
            {
                object? effectiveSelection = selectedIndex >= 0 ? itemSnapshot[selectedIndex] : null;
                if (!ReferenceEquals(fallbackSelector.SelectedItem, effectiveSelection))
                {
                    fallbackSelector.SelectedItem = effectiveSelection;
                }
            }
            else if (nativeSelector is not null)
            {
                nativeSelector.SelectedIndex = selectedIndex;
            }
        }
        finally
        {
            isSynchronizingSelector = false;
        }
    }

    private int FindItemIndex(object selectedItem)
    {
        for (int index = 0; index < itemSnapshot.Count; index++)
        {
            object? candidate = itemSnapshot[index];
            if (ReferenceEquals(candidate, selectedItem) || Equals(candidate, selectedItem))
            {
                return index;
            }
        }

        return -1;
    }

    private string GetDisplayText(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        string? path = DisplayMemberPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return item.ToString() ?? string.Empty;
        }

        object? value = item;
        foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value is null)
            {
                return string.Empty;
            }

            PropertyDescriptor? property = TypeDescriptor.GetProperties(value).Find(segment, ignoreCase: false);
            if (property is null)
            {
                return item.ToString() ?? string.Empty;
            }

            value = property.GetValue(value);
        }

        return value?.ToString() ?? string.Empty;
    }

    private void ApplyAutomationProperties()
    {
        string accessibleName = AutomationProperties.GetName(this) ?? string.Empty;
        string helpText = AutomationProperties.GetHelpText(this) ?? string.Empty;
        string automationId = AutomationProperties.GetAutomationId(this) ?? string.Empty;

        AutomationProperties.SetName(fallbackSelector, accessibleName);
        AutomationProperties.SetHelpText(fallbackSelector, helpText);
        AutomationProperties.SetAutomationId(fallbackSelector, automationId);
        AutomationProperties.SetAccessibilityView(fallbackSelector, AccessibilityView.Control);

        if (nativeSelector is not null)
        {
            nativeSelector.AccessibleName = accessibleName;
            AutomationProperties.SetName(nativeSelector, accessibleName);
            AutomationProperties.SetHelpText(nativeSelector, helpText);
            AutomationProperties.SetAutomationId(nativeSelector, automationId);
            AutomationProperties.SetAccessibilityView(nativeSelector, AccessibilityView.Control);
        }
    }

    private void OnFallbackSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!isSynchronizingSelector)
        {
            SetCurrentValue(SelectedItemProperty, fallbackSelector.SelectedItem);
        }
    }

    private void OnFallbackKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab)
        {
            return;
        }

        TabNavigationStarting?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
        MoveTabFocus((e.KeyModifiers & KeyModifiers.Shift) != 0);
    }

    private void OnNativeSelectionChanged(object? sender, int selectedIndex)
    {
        if (isSynchronizingSelector)
        {
            return;
        }

        object? selectedItem = selectedIndex >= 0 && selectedIndex < itemSnapshot.Count
            ? itemSnapshot[selectedIndex]
            : null;
        SetCurrentValue(SelectedItemProperty, selectedItem);
    }

    private void OnNativeTabNavigationRequested(object? sender, bool moveBackward)
    {
        TabNavigationStarting?.Invoke(this, EventArgs.Empty);

        // The native host first returns physical focus to the Avalonia window.
        // Moving logical focus after the current Win32 message prevents the
        // combo's WM_KILLFOCUS from overwriting the requested target.
        Dispatcher.UIThread.Post(
            () => MoveTabFocus(moveBackward),
            DispatcherPriority.Input);
    }

    private void MoveTabFocus(bool moveBackward)
    {
        Control? explicitTarget = moveBackward ? PreviousTabTarget : NextTabTarget;
        if (explicitTarget?.Focus(
                NavigationMethod.Tab,
                moveBackward ? KeyModifiers.Shift : KeyModifiers.None) == true)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        topLevel?.FocusManager?.TryMoveFocus(
            moveBackward ? NavigationDirection.Previous : NavigationDirection.Next,
            new FindNextElementOptions
            {
                FocusedElement = this,
                SearchRoot = topLevel,
            });
    }

    private void OnNativeFocusReceived(object? sender, EventArgs e)
    {
        if (!IsKeyboardFocusWithin)
        {
            Focus(NavigationMethod.Pointer, KeyModifiers.None);
        }
    }
}
