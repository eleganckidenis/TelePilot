using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using TelePilot.App.Controls;

namespace TelePilot.Tests.App;

public sealed class AccessibleTextBoxTests
{
    private const uint WsPopup = 0x80000000;
    private const int GwlStyle = -16;
    private const uint EsMultiline = 0x0004;
    private const uint EsAutoHScroll = 0x0080;
    private const uint EsReadOnly = 0x0800;
    private const uint EsWantReturn = 0x1000;
    private const uint WmChar = 0x0102;
    private const uint ObjIdClient = unchecked((uint)-4);

    [AvaloniaFact]
    public void HeadlessFallback_MapsReadOnlyAndMultilineProperties()
    {
        var textBox = new AccessibleTextBox
        {
            Text = "Pierwszy wiersz\nDrugi wiersz",
            Watermark = "Opis",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(textBox, "Szczegóły audycji");
        var window = new Window { Content = textBox };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TextBox editor = Assert.IsType<TextBox>(textBox.Content);
            Assert.False(textBox.IsUsingNativeEditor);
            Assert.Equal(textBox.Text, editor.Text);
            Assert.Equal("Opis", editor.PlaceholderText);
            Assert.True(editor.IsReadOnly);
            Assert.True(editor.AcceptsReturn);
            Assert.Equal(TextWrapping.Wrap, editor.TextWrapping);
            Assert.Equal("Szczegóły audycji", AutomationProperties.GetName(editor));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RuntimePropertyChanges_ArePropagatedToFallback()
    {
        var textBox = new AccessibleTextBox();
        TextBox editor = Assert.IsType<TextBox>(textBox.Content);

        textBox.IsReadOnly = true;
        textBox.AcceptsReturn = true;
        textBox.TextWrapping = TextWrapping.WrapWithOverflow;
        textBox.IsEnabled = false;

        Assert.True(editor.IsReadOnly);
        Assert.True(editor.AcceptsReturn);
        Assert.Equal(TextWrapping.WrapWithOverflow, editor.TextWrapping);
        Assert.False(editor.IsEnabled);
    }

    [AvaloniaFact]
    public void SearchAlias_PreservesTheOriginalPublicContract()
    {
        var searchBox = new AccessibleSearchBox
        {
            Text = "TVP",
            Watermark = "Szukaj",
        };

        Assert.IsAssignableFrom<AccessibleTextBox>(searchBox);
        Assert.Equal("TVP", searchBox.Text);
        Assert.Equal("Szukaj", searchBox.Watermark);
        Assert.False(searchBox.IsReadOnly);
        Assert.False(searchBox.AcceptsReturn);
        Assert.Equal(TextWrapping.NoWrap, searchBox.TextWrapping);
        Assert.Equal(
            BindingMode.TwoWay,
            AccessibleSearchBox.TextProperty.GetMetadata(typeof(AccessibleSearchBox)).DefaultBindingMode);
    }

    [AvaloniaFact]
    public void OneWayBinding_UpdatesThePlatformEditor()
    {
        var source = new TextSource { Value = "Wartość początkowa" };
        var textBox = new AccessibleTextBox();
        using IDisposable binding = textBox.Bind(
            AccessibleTextBox.TextProperty,
            new Binding(nameof(TextSource.Value))
            {
                Source = source,
                Mode = BindingMode.OneWay,
            });

        Dispatcher.UIThread.RunJobs();
        source.Value = "Wartość zmieniona";
        Dispatcher.UIThread.RunJobs();

        TextBox editor = Assert.IsType<TextBox>(textBox.Content);
        Assert.Equal("Wartość zmieniona", textBox.Text);
        Assert.Equal("Wartość zmieniona", editor.Text);
    }

    [AvaloniaFact]
    public void OneWayToSourceBinding_ReceivesUserTextFromPlatformEditor()
    {
        var source = new TextSource();
        var textBox = new AccessibleTextBox();
        using IDisposable binding = textBox.Bind(
            AccessibleTextBox.TextProperty,
            new Binding(nameof(TextSource.Value))
            {
                Source = source,
                Mode = BindingMode.OneWayToSource,
            });
        var window = new Window { Content = textBox };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TextBox editor = Assert.IsType<TextBox>(textBox.Content);

            editor.Text = "Tekst użytkownika";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Tekst użytkownika", source.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TwoWayBinding_SynchronizesSourceAndPlatformEditor()
    {
        var source = new TextSource { Value = "Początek" };
        var textBox = new AccessibleTextBox();
        using IDisposable binding = textBox.Bind(
            AccessibleTextBox.TextProperty,
            new Binding(nameof(TextSource.Value))
            {
                Source = source,
                Mode = BindingMode.TwoWay,
            });
        var window = new Window { Content = textBox };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TextBox editor = Assert.IsType<TextBox>(textBox.Content);
            Assert.Equal("Początek", editor.Text);

            editor.Text = "Koniec";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Koniec", source.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TabAndShiftTab_LeaveTheFallbackEditorInTheExpectedDirection()
    {
        var previous = new Button { Content = "Poprzedni" };
        var textBox = new AccessibleTextBox();
        var next = new Button { Content = "Następny" };
        textBox.PreviousTabTarget = previous;
        textBox.NextTabTarget = next;
        var panel = new StackPanel { Children = { previous, textBox, next } };
        var window = new Window { Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(textBox.FocusEditor());

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(next.IsKeyboardFocusWithin);

            Assert.True(textBox.FocusEditor());
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(previous.IsKeyboardFocusWithin);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void WindowsNativeHost_CreatesStandardAccessibleMultilineReadOnlyEdit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        nint parent = CreateWindowExW(
            0,
            "STATIC",
            "TelePilot text test host",
            WsPopup,
            0,
            0,
            500,
            300,
            0,
            0,
            0,
            0);
        Assert.NotEqual(0, parent);

        object? host = null;
        IPlatformHandle? nativeHandle = null;
        MethodInfo? destroyMethod = null;
        try
        {
            Type hostType = typeof(AccessibleTextBox).Assembly.GetType(
                "TelePilot.App.Controls.Win32EditHost",
                throwOnError: true)!;
            host = Activator.CreateInstance(hostType, nonPublic: true);
            Assert.NotNull(host);

            SetInternalProperty(hostType, host, "Text", "Pierwszy\r\nDrugi");
            SetInternalProperty(hostType, host, "AccessibleName", "Szczegóły audycji");
            SetInternalProperty(hostType, host, "IsReadOnly", true);
            SetInternalProperty(hostType, host, "AcceptsReturn", true);
            SetInternalProperty(hostType, host, "TextWrapping", TextWrapping.Wrap);

            MethodInfo createMethod = hostType.GetMethod(
                "CreateNativeControlCore",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Nie znaleziono metody tworzącej natywne pole tekstowe.");
            destroyMethod = hostType.GetMethod(
                "DestroyNativeControlCore",
                BindingFlags.Instance | BindingFlags.NonPublic);
            nativeHandle = Assert.IsAssignableFrom<IPlatformHandle>(
                createMethod.Invoke(host, [new PlatformHandle(parent, "HWND")]));

            nint edit = nativeHandle.Handle;
            var className = new char[32];
            int classNameLength = GetClassNameW(edit, className, className.Length);
            Assert.Equal("Edit", new string(className, 0, classNameLength), ignoreCase: true);

            uint style = unchecked((uint)GetWindowLongPointer(edit, GwlStyle));
            Assert.NotEqual(0u, style & EsMultiline);
            Assert.NotEqual(0u, style & EsReadOnly);
            Assert.NotEqual(0u, style & EsWantReturn);
            Assert.Equal(0u, style & EsAutoHScroll);
            Assert.Equal("Pierwszy\r\nDrugi", ReadWindowText(edit));

            SetFocus(edit);
            _ = SendMessageW(edit, WmChar, 'X', 0);
            Assert.Equal("Pierwszy\r\nDrugi", ReadWindowText(edit));
            Assert.Equal("Szczegóły audycji", ReadAccessibleName(edit));
        }
        finally
        {
            if (host is not null && nativeHandle is not null && destroyMethod is not null)
            {
                destroyMethod.Invoke(host, [nativeHandle]);
            }

            DestroyWindow(parent);
        }
    }

    private static void SetInternalProperty(Type hostType, object host, string name, object value)
    {
        PropertyInfo property = hostType.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Nie znaleziono właściwości {name} natywnego pola tekstowego.");
        property.SetValue(host, value);
    }

    private static string ReadWindowText(nint hwnd)
    {
        int length = GetWindowTextLengthW(hwnd);
        var buffer = new char[length + 1];
        int copied = GetWindowTextW(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadAccessibleName(nint hwnd)
    {
        Guid interfaceId = typeof(IAccessible).GUID;
        int result = AccessibleObjectFromWindow(hwnd, ObjIdClient, ref interfaceId, out IAccessible accessible);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            return accessible[0];
        }
        finally
        {
            if (Marshal.IsComObject(accessible))
            {
                Marshal.FinalReleaseComObject(accessible);
            }
        }
    }

    private static nint GetWindowLongPointer(nint hwnd, int index) =>
        nint.Size == 8 ? GetWindowLongPtrW(hwnd, index) : GetWindowLongW(hwnd, index);

    private sealed class TextSource : INotifyPropertyChanged
    {
        private string value = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Value
        {
            get => value;
            set
            {
                if (string.Equals(this.value, value, StringComparison.Ordinal))
                {
                    return;
                }

                this.value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, [Out] char[] className, int maximumCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern nint GetWindowLongW(nint hwnd, int index);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hwnd, [Out] char[] text, int maximumCount);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint hwnd,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IAccessible
    {
        [IndexerName("accName")]
        [DispId(-5003)]
        string? this[[MarshalAs(UnmanagedType.Struct)] object childId] { get; }
    }
}
