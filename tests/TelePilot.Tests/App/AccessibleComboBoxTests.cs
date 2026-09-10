using System.Collections.ObjectModel;
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
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TelePilot.App.Controls;

namespace TelePilot.Tests.App;

public sealed class AccessibleComboBoxTests
{
    private static readonly string[] NativeItems = ["Pierwszy", "Drugi"];

    private const uint WsPopup = 0x80000000;
    private const int GwlStyle = -16;
    private const uint WmKeyDown = 0x0100;
    private const uint CbGetCount = 0x0146;
    private const uint CbGetCurSel = 0x0147;
    private const int VkDown = 0x28;
    private const uint ObjIdClient = unchecked((uint)-4);

    [AvaloniaFact]
    public void SelectedItemProperty_UsesTwoWayBindingByDefault()
    {
        Assert.Equal(
            BindingMode.TwoWay,
            AccessibleComboBox.SelectedItemProperty
                .GetMetadata(typeof(AccessibleComboBox))
                .DefaultBindingMode);
    }

    [AvaloniaFact]
    public void HeadlessHost_UsesAvaloniaFallbackAndPropagatesItemsSelectionAndAutomationProperties()
    {
        SelectorItem first = new("one", "Pierwszy kanał");
        SelectorItem second = new("two", "Drugi kanał");
        var selector = new AccessibleComboBox
        {
            ItemsSource = new[] { first, second },
            SelectedItem = second,
            DisplayMemberPath = nameof(SelectorItem.Name),
        };
        AutomationProperties.SetName(selector, "Wybierz kanał");
        AutomationProperties.SetHelpText(selector, "Strzałkami wybierz kanał.");
        AutomationProperties.SetAutomationId(selector, "channel-selector");
        var window = new Window { Content = selector };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            ComboBox fallback = Assert.IsType<ComboBox>(selector.Content);
            Assert.False(selector.IsUsingNativeSelector);
            Assert.Equal(2, fallback.ItemCount);
            Assert.Same(second, fallback.SelectedItem);
            Assert.NotNull(fallback.DisplayMemberBinding);
            Assert.Equal("Wybierz kanał", AutomationProperties.GetName(fallback));
            Assert.Equal("Strzałkami wybierz kanał.", AutomationProperties.GetHelpText(fallback));
            Assert.Equal("channel-selector", AutomationProperties.GetAutomationId(fallback));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FallbackSelectionAndPublicSelectedItemStaySynchronizedBothWays()
    {
        SelectorItem first = new("one", "Pierwszy kanał");
        SelectorItem second = new("two", "Drugi kanał");
        var selector = new AccessibleComboBox { ItemsSource = new[] { first, second } };
        var window = new Window { Content = selector };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ComboBox fallback = Assert.IsType<ComboBox>(selector.Content);

            selector.SelectedItem = first;
            Assert.Same(first, fallback.SelectedItem);

            fallback.SelectedItem = second;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(second, selector.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ObservableItemsSourceChangesRebuildTheFallbackAndClearRemovedSelection()
    {
        SelectorItem first = new("one", "Pierwszy kanał");
        SelectorItem second = new("two", "Drugi kanał");
        var items = new ObservableCollection<SelectorItem> { first };
        var selector = new AccessibleComboBox
        {
            ItemsSource = items,
            SelectedItem = first,
        };
        var window = new Window { Content = selector };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ComboBox fallback = Assert.IsType<ComboBox>(selector.Content);

            items.Add(second);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, fallback.ItemCount);
            Assert.Same(first, selector.SelectedItem);

            items.Remove(first);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(fallback.Items);
            Assert.Null(selector.SelectedItem);
            Assert.Null(fallback.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FallbackUsesDisplayMemberPathForVisibleOptionText()
    {
        SelectorItem item = new("technical-value", "Czytelna nazwa");
        var selector = new AccessibleComboBox
        {
            ItemsSource = new[] { item },
            SelectedItem = item,
            DisplayMemberPath = nameof(SelectorItem.Name),
        };
        var window = new Window { Content = selector };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ComboBox fallback = Assert.IsType<ComboBox>(selector.Content);
            fallback.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            ComboBoxItem container = Assert.IsType<ComboBoxItem>(fallback.ContainerFromItem(item));
            Assert.Contains(
                container.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Czytelna nazwa");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UpAndDownChangeSelectionWithoutOpeningTheFallbackDropDown()
    {
        SelectorItem first = new("one", "Pierwszy kanał");
        SelectorItem second = new("two", "Drugi kanał");
        var selector = new AccessibleComboBox
        {
            ItemsSource = new[] { first, second },
            SelectedItem = first,
        };
        var window = new Window { Content = selector };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ComboBox fallback = Assert.IsType<ComboBox>(selector.Content);
            Assert.True(selector.FocusSelector());

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(fallback.IsDropDownOpen);
            Assert.Same(second, selector.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TabAndShiftTabUseExplicitFallbackTargets()
    {
        var previous = new Button { Content = "Poprzedni" };
        var next = new Button { Content = "Następny" };
        var selector = new AccessibleComboBox
        {
            ItemsSource = new[] { "Jeden", "Dwa" },
            SelectedItem = "Jeden",
            PreviousTabTarget = previous,
            NextTabTarget = next,
        };
        var panel = new StackPanel { Children = { previous, selector, next } };
        var window = new Window { Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(selector.FocusSelector());

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(next.IsKeyboardFocusWithin);

            Assert.True(selector.FocusSelector());
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
    public void WindowsNativeHostCreatesStandardDropDownListAndExposesAccessibleName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        nint parent = CreateWindowExW(
            0,
            "STATIC",
            "TelePilot combo test host",
            WsPopup,
            0,
            0,
            400,
            400,
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
            Type hostType = typeof(AccessibleComboBox).Assembly.GetType(
                "TelePilot.App.Controls.Win32ComboBoxHost",
                throwOnError: true)!;
            host = Activator.CreateInstance(hostType, nonPublic: true);
            Assert.NotNull(host);

            SetInternalProperty(hostType, host, "Items", NativeItems);
            SetInternalProperty(hostType, host, "SelectedIndex", 0);
            SetInternalProperty(hostType, host, "AccessibleName", "Natywna lista kanałów");

            MethodInfo createMethod = hostType.GetMethod(
                "CreateNativeControlCore",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Nie znaleziono metody tworzącej natywny ComboBox.");
            destroyMethod = hostType.GetMethod(
                "DestroyNativeControlCore",
                BindingFlags.Instance | BindingFlags.NonPublic);
            nativeHandle = Assert.IsAssignableFrom<IPlatformHandle>(
                createMethod.Invoke(host, [new PlatformHandle(parent, "HWND")]));

            nint combo = nativeHandle.Handle;
            var className = new char[64];
            int classNameLength = GetClassNameW(combo, className, className.Length);
            Assert.Equal("ComboBox", new string(className, 0, classNameLength), ignoreCase: true);

            nint style = GetWindowLongPointer(combo, GwlStyle);
            Assert.Equal(0x0003u, unchecked((uint)style) & 0x0003u);
            Assert.Equal(2, unchecked((int)SendMessageW(combo, CbGetCount, 0, 0)));
            Assert.Equal(0, unchecked((int)SendMessageW(combo, CbGetCurSel, 0, 0)));

            SetFocus(combo);
            SendMessageW(combo, WmKeyDown, VkDown, 0);
            Assert.Equal(1, unchecked((int)SendMessageW(combo, CbGetCurSel, 0, 0)));
            Assert.Equal("Natywna lista kanałów", ReadAccessibleName(combo));
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
            ?? throw new InvalidOperationException($"Nie znaleziono właściwości {name} natywnego ComboBox.");
        property.SetValue(host, value);
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

    private sealed record SelectorItem(string Id, string Name)
    {
        public override string ToString() => Id;
    }
}
