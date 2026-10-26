using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TelePilot.App.Controls;

/// <summary>
/// A date field backed by the standard Windows date-time picker when an HWND is available.
/// </summary>
/// <remarks>
/// The native control exposes the date sections and calendar through the platform
/// accessibility API. Other platforms, previews, and headless tests use Avalonia's
/// <see cref="CalendarDatePicker"/>.
/// </remarks>
public sealed class AccessibleDatePicker : ContentControl
{
    public static readonly StyledProperty<DateTime?> SelectedDateProperty =
        AvaloniaProperty.Register<AccessibleDatePicker, DateTime?>(
            nameof(SelectedDate),
            defaultBindingMode: BindingMode.TwoWay,
            enableDataValidation: true);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<AccessibleDatePicker, string?>(nameof(Watermark));

    private readonly CalendarDatePicker fallbackPicker;
    private Control editor;
    private Win32DatePickerHost? nativePicker;
    private bool isSynchronizingEditor;
    private bool isFocusingNativeEditor;

    static AccessibleDatePicker()
    {
        FocusableProperty.OverrideDefaultValue<AccessibleDatePicker>(true);
        KeyboardNavigation.IsTabStopProperty.OverrideDefaultValue<AccessibleDatePicker>(true);
        MinHeightProperty.OverrideDefaultValue<AccessibleDatePicker>(32d);
        HorizontalContentAlignmentProperty.OverrideDefaultValue<AccessibleDatePicker>(HorizontalAlignment.Stretch);
        VerticalContentAlignmentProperty.OverrideDefaultValue<AccessibleDatePicker>(VerticalAlignment.Stretch);
    }

    public AccessibleDatePicker()
    {
        fallbackPicker = new CalendarDatePicker
        {
            IsTabStop = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            SelectedDateFormat = CalendarDatePickerFormat.Custom,
            CustomDateFormatString = "yyyy-MM-dd",
        };
        fallbackPicker.SelectedDateChanged += OnFallbackSelectedDateChanged;
        editor = fallbackPicker;
        Content = editor;

        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ApplyEditorProperties();
    }

    public DateTime? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public bool IsUsingNativePicker => ReferenceEquals(editor, nativePicker);

    public Control? PreviousTabTarget { get; set; }

    public Control? NextTabTarget { get; set; }

    public bool FocusEditor()
    {
        if (nativePicker is not null && ReferenceEquals(editor, nativePicker))
        {
            // Establish logical focus first. Starting with the HWND causes its
            // focus callback to focus the wrapper and then re-enter the HWND,
            // which resets the section selected by the native date editor.
            isFocusingNativeEditor = true;
            try
            {
                if (!IsKeyboardFocusWithin && !Focus(NavigationMethod.Unspecified, KeyModifiers.None))
                {
                    return false;
                }

                return nativePicker.FocusNativePicker();
            }
            finally
            {
                isFocusingNativeEditor = false;
            }
        }

        return fallbackPicker.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SelectPlatformEditor();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.NewFocusedElement, this) && !isFocusingNativeEditor)
        {
            FocusEditor();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedDateProperty)
        {
            SynchronizeEditorDate(change.GetNewValue<DateTime?>()?.Date);
        }
        else if (change.Property == WatermarkProperty
                 || change.Property == AutomationProperties.NameProperty
                 || change.Property == AutomationProperties.HelpTextProperty
                 || change.Property == AutomationProperties.AutomationIdProperty)
        {
            ApplyEditorProperties();
        }
    }

    private void SelectPlatformEditor()
    {
        IPlatformHandle? topLevelHandle = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle();
        bool canHostWin32Picker = OperatingSystem.IsWindows()
            && string.Equals(topLevelHandle?.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase);

        if (canHostWin32Picker)
        {
            UseNativePicker();
        }
        else
        {
            SwitchEditor(fallbackPicker);
        }
    }

    private void UseNativePicker()
    {
        if (nativePicker is null)
        {
            nativePicker = new Win32DatePickerHost
            {
                IsTabStop = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            nativePicker.NativeDateChanged += OnNativeDateChanged;
            nativePicker.NativeTabNavigationRequested += OnNativeTabNavigationRequested;
            nativePicker.NativeFocusReceived += OnNativeFocusReceived;
        }

        SwitchEditor(nativePicker);
    }

    private void SwitchEditor(Control replacement)
    {
        if (!ReferenceEquals(editor, replacement))
        {
            editor = replacement;
            Content = replacement;
        }

        ApplyEditorProperties();
        SynchronizeEditorDate(SelectedDate?.Date);
    }

    private void ApplyEditorProperties()
    {
        if (fallbackPicker is null)
        {
            return;
        }

        string accessibleName = AutomationProperties.GetName(this) ?? string.Empty;
        string helpText = AutomationProperties.GetHelpText(this) ?? string.Empty;
        string automationId = AutomationProperties.GetAutomationId(this) ?? string.Empty;

        isSynchronizingEditor = true;
        try
        {
            if (ReferenceEquals(editor, fallbackPicker))
            {
                fallbackPicker.PlaceholderText = Watermark;
                AutomationProperties.SetName(fallbackPicker, accessibleName);
                AutomationProperties.SetHelpText(fallbackPicker, helpText);
                AutomationProperties.SetAutomationId(fallbackPicker, automationId);
                AutomationProperties.SetAccessibilityView(fallbackPicker, AccessibilityView.Control);
            }
            else if (nativePicker is not null)
            {
                nativePicker.AccessibleName = accessibleName;
                AutomationProperties.SetName(nativePicker, accessibleName);
                AutomationProperties.SetHelpText(nativePicker, helpText);
                AutomationProperties.SetAutomationId(nativePicker, automationId);
                AutomationProperties.SetAccessibilityView(nativePicker, AccessibilityView.Control);
            }
        }
        finally
        {
            isSynchronizingEditor = false;
        }
    }

    private void SynchronizeEditorDate(DateTime? selectedDate)
    {
        if (isSynchronizingEditor)
        {
            return;
        }

        isSynchronizingEditor = true;
        try
        {
            if (ReferenceEquals(editor, fallbackPicker))
            {
                fallbackPicker.SelectedDate = selectedDate;
            }
            else if (nativePicker is not null)
            {
                nativePicker.SelectedDate = selectedDate;
            }
        }
        finally
        {
            isSynchronizingEditor = false;
        }
    }

    private void OnFallbackSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!isSynchronizingEditor)
        {
            SetCurrentValue(SelectedDateProperty, fallbackPicker.SelectedDate?.Date);
        }
    }

    private void OnNativeDateChanged(object? sender, DateTime? selectedDate)
    {
        if (!isSynchronizingEditor)
        {
            SetCurrentValue(SelectedDateProperty, selectedDate?.Date);
        }
    }

    private void OnNativeTabNavigationRequested(object? sender, bool moveBackward)
    {
        void MoveFocus()
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

        Dispatcher.UIThread.Post(MoveFocus, DispatcherPriority.Input);
    }

    private void OnNativeFocusReceived(object? sender, EventArgs e)
    {
        if (!IsKeyboardFocusWithin)
        {
            Focus(NavigationMethod.Pointer, KeyModifiers.None);
        }
    }
}
