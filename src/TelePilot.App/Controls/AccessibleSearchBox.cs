using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TelePilot.App.Controls;

/// <summary>
/// A text field that uses a native Win32 edit control on Windows.
/// </summary>
/// <remarks>
/// The native edit control supplies the UI Automation Text pattern that screen
/// readers need for caret movement and deleted-character announcements. Other
/// platforms, previews, and headless tests use a regular Avalonia TextBox.
/// </remarks>
public class AccessibleTextBox : ContentControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<AccessibleTextBox, string>(
            nameof(Text),
            string.Empty,
            defaultBindingMode: BindingMode.TwoWay,
            coerce: static (_, value) => value ?? string.Empty,
            enableDataValidation: true);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<AccessibleTextBox, string?>(nameof(Watermark));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<AccessibleTextBox, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<bool> AcceptsReturnProperty =
        AvaloniaProperty.Register<AccessibleTextBox, bool>(nameof(AcceptsReturn));

    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        AvaloniaProperty.Register<AccessibleTextBox, TextWrapping>(
            nameof(TextWrapping),
            Avalonia.Media.TextWrapping.NoWrap);

    private readonly TextBox fallbackEditor;
    private Control editor;
    private Win32EditHost? nativeEditor;
    private bool isSynchronizingEditor;

    static AccessibleTextBox()
    {
        FocusableProperty.OverrideDefaultValue<AccessibleTextBox>(true);
        KeyboardNavigation.IsTabStopProperty.OverrideDefaultValue<AccessibleTextBox>(true);
        MinHeightProperty.OverrideDefaultValue<AccessibleTextBox>(32d);
        HorizontalContentAlignmentProperty.OverrideDefaultValue<AccessibleTextBox>(HorizontalAlignment.Stretch);
        VerticalContentAlignmentProperty.OverrideDefaultValue<AccessibleTextBox>(VerticalAlignment.Stretch);
    }

    public AccessibleTextBox()
    {
        fallbackEditor = CreateFallbackEditor();
        editor = fallbackEditor;
        Content = editor;

        // The wrapper owns logical focus while its child supplies the actual
        // platform accessibility implementation. Hiding the wrapper avoids a
        // duplicate edit field in the automation tree.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        ApplyEditorProperties();
    }

    public event EventHandler<AccessibleSearchTextChangedEventArgs>? TextChanged;

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool AcceptsReturn
    {
        get => GetValue(AcceptsReturnProperty);
        set => SetValue(AcceptsReturnProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets whether the control is currently backed by a native Win32 EDIT.
    /// </summary>
    public bool IsUsingNativeEditor => ReferenceEquals(editor, nativeEditor);

    internal event EventHandler? TabNavigationStarting;

    /// <summary>
    /// Optional explicit targets used when the native Win32 editor receives Tab.
    /// </summary>
    public Control? PreviousTabTarget { get; set; }

    public Control? NextTabTarget { get; set; }

    /// <summary>
    /// Moves keyboard focus to the platform editor used by this control.
    /// </summary>
    public bool FocusEditor()
    {
        if (nativeEditor is not null && ReferenceEquals(editor, nativeEditor))
        {
            return nativeEditor.FocusNativeEditor();
        }

        return fallbackEditor.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SelectPlatformEditor();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.NewFocusedElement, this))
        {
            FocusEditor();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            string oldText = change.GetOldValue<string>() ?? string.Empty;
            string newText = change.GetNewValue<string>() ?? string.Empty;
            SynchronizeEditorText(newText);
            if (!string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                TextChanged?.Invoke(this, new AccessibleSearchTextChangedEventArgs(oldText, newText));
            }
        }
        else if (change.Property == AcceptsReturnProperty
                 || change.Property == TextWrappingProperty)
        {
            if (IsUsingNativeEditor)
            {
                RecreateNativeEditor();
            }
            else
            {
                ApplyEditorProperties();
            }
        }
        else if (change.Property == WatermarkProperty
                 || change.Property == IsReadOnlyProperty
                 || change.Property == IsEnabledProperty
                 || change.Property == AutomationProperties.NameProperty
                 || change.Property == AutomationProperties.HelpTextProperty
                 || change.Property == AutomationProperties.AutomationIdProperty)
        {
            ApplyEditorProperties();
        }
    }

    private TextBox CreateFallbackEditor()
    {
        var textBox = new TextBox
        {
            IsTabStop = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        textBox.TextChanged += OnFallbackTextChanged;
        textBox.AddHandler(
            InputElement.KeyDownEvent,
            OnFallbackEditorKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        return textBox;
    }

    private void SelectPlatformEditor()
    {
        IPlatformHandle? topLevelHandle = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle();
        bool canHostWin32Editor = OperatingSystem.IsWindows()
            && string.Equals(topLevelHandle?.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase);

        if (canHostWin32Editor)
        {
            UseNativeEditor();
        }
        else
        {
            UseFallbackEditor();
        }
    }

    private void UseNativeEditor()
    {
        if (nativeEditor is null)
        {
            nativeEditor = CreateNativeEditor();
        }

        SwitchEditor(nativeEditor);
    }

    private void UseFallbackEditor() => SwitchEditor(fallbackEditor);

    private Win32EditHost CreateNativeEditor()
    {
        var native = new Win32EditHost
        {
            IsTabStop = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        native.Text = Text;
        native.Watermark = Watermark;
        native.AccessibleName = AutomationProperties.GetName(this) ?? string.Empty;
        native.IsReadOnly = IsReadOnly;
        native.AcceptsReturn = AcceptsReturn;
        native.TextWrapping = TextWrapping;
        native.NativeIsEnabled = IsEnabled;
        native.NativeTextChanged += OnNativeTextChanged;
        native.NativeTabNavigationRequested += OnNativeTabNavigationRequested;
        native.NativeFocusReceived += OnNativeFocusReceived;
        return native;
    }

    private void RecreateNativeEditor()
    {
        if (nativeEditor is null || !ReferenceEquals(editor, nativeEditor))
        {
            ApplyEditorProperties();
            return;
        }

        bool restoreFocus = IsKeyboardFocusWithin;
        Win32EditHost previous = nativeEditor;
        previous.NativeTextChanged -= OnNativeTextChanged;
        previous.NativeTabNavigationRequested -= OnNativeTabNavigationRequested;
        previous.NativeFocusReceived -= OnNativeFocusReceived;

        nativeEditor = CreateNativeEditor();
        SwitchEditor(nativeEditor);
        if (restoreFocus)
        {
            Dispatcher.UIThread.Post(() => FocusEditor(), DispatcherPriority.Input);
        }
    }

    private void SwitchEditor(Control replacement)
    {
        if (ReferenceEquals(editor, replacement))
        {
            ApplyEditorProperties();
            return;
        }

        editor = replacement;
        Content = replacement;
        ApplyEditorProperties();
    }

    private void ApplyEditorProperties()
    {
        if (fallbackEditor is null)
        {
            return;
        }

        string accessibleName = AutomationProperties.GetName(this) ?? string.Empty;
        string helpText = AutomationProperties.GetHelpText(this) ?? string.Empty;
        string automationId = AutomationProperties.GetAutomationId(this) ?? string.Empty;

        isSynchronizingEditor = true;
        try
        {
            if (ReferenceEquals(editor, fallbackEditor))
            {
                fallbackEditor.Text = Text;
                fallbackEditor.PlaceholderText = Watermark;
                fallbackEditor.IsReadOnly = IsReadOnly;
                fallbackEditor.AcceptsReturn = AcceptsReturn;
                fallbackEditor.TextWrapping = TextWrapping;
                fallbackEditor.IsEnabled = IsEnabled;
                AutomationProperties.SetName(fallbackEditor, accessibleName);
                AutomationProperties.SetHelpText(fallbackEditor, helpText);
                AutomationProperties.SetAutomationId(fallbackEditor, automationId);
                AutomationProperties.SetAccessibilityView(fallbackEditor, AccessibilityView.Control);
            }
            else if (nativeEditor is not null)
            {
                nativeEditor.Text = Text;
                nativeEditor.Watermark = Watermark;
                nativeEditor.AccessibleName = accessibleName;
                nativeEditor.IsReadOnly = IsReadOnly;
                nativeEditor.AcceptsReturn = AcceptsReturn;
                nativeEditor.TextWrapping = TextWrapping;
                nativeEditor.NativeIsEnabled = IsEnabled;
                AutomationProperties.SetName(nativeEditor, accessibleName);
                AutomationProperties.SetHelpText(nativeEditor, helpText);
                AutomationProperties.SetAutomationId(nativeEditor, automationId);
                AutomationProperties.SetAccessibilityView(nativeEditor, AccessibilityView.Control);
            }
        }
        finally
        {
            isSynchronizingEditor = false;
        }
    }

    private void SynchronizeEditorText(string text)
    {
        if (isSynchronizingEditor)
        {
            return;
        }

        isSynchronizingEditor = true;
        try
        {
            if (ReferenceEquals(editor, fallbackEditor))
            {
                if (!string.Equals(fallbackEditor.Text, text, StringComparison.Ordinal))
                {
                    fallbackEditor.Text = text;
                }
            }
            else if (nativeEditor is not null)
            {
                nativeEditor.Text = text;
            }
        }
        finally
        {
            isSynchronizingEditor = false;
        }
    }

    private void OnFallbackTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!isSynchronizingEditor)
        {
            SetCurrentValue(TextProperty, fallbackEditor.Text ?? string.Empty);
        }
    }

    private void OnNativeTextChanged(object? sender, string text)
    {
        if (!isSynchronizingEditor)
        {
            SetCurrentValue(TextProperty, text);
        }
    }

    private void OnFallbackEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab)
        {
            return;
        }

        TabNavigationStarting?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
        bool moveBackward = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        Dispatcher.UIThread.Post(
            () =>
            {
                var explicitTarget = moveBackward ? PreviousTabTarget : NextTabTarget;
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
            },
            DispatcherPriority.Input);
    }

    private void OnNativeTabNavigationRequested(object? sender, bool moveBackward)
    {
        TabNavigationStarting?.Invoke(this, EventArgs.Empty);

        void MoveFocus()
        {
            var explicitTarget = moveBackward ? PreviousTabTarget : NextTabTarget;
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

        // Win32EditHost najpierw oddaje fizyczny fokus do okna głównego.
        // Cel Avalonia fokusujemy po zakończeniu bieżącego komunikatu Win32,
        // żeby WM_KILLFOCUS edytora nie nadpisał nowego fokusu logicznego.
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

/// <summary>
/// Backward-compatible name for search fields created before
/// <see cref="AccessibleTextBox"/> became a general-purpose control.
/// </summary>
public sealed class AccessibleSearchBox : AccessibleTextBox
{
}
