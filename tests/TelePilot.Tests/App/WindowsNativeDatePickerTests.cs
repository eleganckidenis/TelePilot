using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using TelePilot.App.Controls;

namespace TelePilot.Tests.App;

public sealed class WindowsNativeDatePickerTests
{
    private const uint WmGetDlgCode = 0x0087;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint DtmSetFormatW = 0x1032;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkTab = 0x09;
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsExToolWindow = 0x00000080;

    [AvaloniaFact]
    public void NativeSectionsKeepArrowEditingAndFocusAfterDateAndNameSynchronization()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WithNativePicker((host, hwnd, _) =>
        {
            // Isolate section order from the machine's regional settings.
            SendMessageW(hwnd, DtmSetFormatW, 0, "dd'/'MM'/'yyyy");
            DateTime initialDate = new(2026, 10, 15);
            host.SelectedDate = initialDate;
            host.AccessibleName = "Data archiwum";
            host.NativeDateChanged += (_, date) =>
            {
                // Mirror the wrapper and its date-dependent accessible name.
                host.SelectedDate = date;
                host.AccessibleName = $"Data archiwum, {date:yyyy-MM-dd}";
            };

            Assert.True(host.FocusNativePicker());
            Assert.NotEqual(0, (int)SendMessageW(hwnd, WmGetDlgCode, VkRight, 0) & 1);
            PressKey(hwnd, VkRight);
            PressKey(hwnd, VkUp);
            DateTime? firstSectionEdit = host.SelectedDate;
            Assert.NotEqual(initialDate, firstSectionEdit);

            Assert.True(host.FocusNativePicker());
            PressKey(hwnd, VkRight);
            PressKey(hwnd, VkUp);
            Assert.NotEqual(firstSectionEdit, host.SelectedDate);
            Assert.Equal(hwnd, GetFocus());

            // Left must return to the previous section after undoing the second.
            PressKey(hwnd, VkDown);
            Assert.Equal(firstSectionEdit, host.SelectedDate);
            PressKey(hwnd, VkLeft);
            PressKey(hwnd, VkDown);
            Assert.Equal(initialDate, host.SelectedDate);
            Assert.Equal(hwnd, GetFocus());
        });
    }

    [AvaloniaFact]
    public void NativeTabRequestsNavigationAndReturnsFocusToTheParent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WithNativePicker((host, hwnd, parent) =>
        {
            bool? moveBackward = null;
            host.NativeTabNavigationRequested += (_, backward) => moveBackward = backward;
            Assert.True(host.FocusNativePicker());

            // SendMessage does not synthesize keyboard state. A modifier held
            // on the desktop is still visible to the native control.
            bool shiftHeld = (GetKeyState(0x10) & 0x8000) != 0;
            PressKey(hwnd, VkTab);

            Assert.Equal(shiftHeld, moveBackward);
            Assert.Equal(parent, GetFocus());
        });
    }

    private static void PressKey(nint hwnd, int key)
    {
        SendMessageW(hwnd, WmKeyDown, key, 0);
        SendMessageW(hwnd, WmKeyUp, key, 0);
        Dispatcher.UIThread.RunJobs();
    }

    private static void WithNativePicker(Action<Win32DatePickerHost, nint, nint> verify)
    {
        nint previousActive = GetActiveWindow();
        // A hidden popup is not a reliable keyboard-focus fixture. Keep this
        // enabled native tool window visible but outside the desktop, and make
        // it active on this test thread before asking the child to take focus.
        nint parent = CreateWindowExW(WsExToolWindow, "STATIC", "TelePilot date test host", WsPopup | WsVisible,
            -32000, -32000, 400, 100, 0, 0, 0, 0);
        Assert.NotEqual(0, parent);
        Win32DatePickerHost host = new();
        IPlatformHandle? nativeHandle = null;
        MethodInfo create = typeof(Win32DatePickerHost).GetMethod(
            "CreateNativeControlCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo destroy = typeof(Win32DatePickerHost).GetMethod(
            "DestroyNativeControlCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            Assert.True(IsWindowVisible(parent));
            Assert.True(IsWindowEnabled(parent));
            SetActiveWindow(parent);
            Assert.Equal(parent, GetActiveWindow());
            nativeHandle = Assert.IsAssignableFrom<IPlatformHandle>(
                create.Invoke(host, [new PlatformHandle(parent, "HWND")]));
            verify(host, nativeHandle.Handle, parent);
        }
        finally
        {
            if (nativeHandle is not null)
            {
                destroy.Invoke(host, [nativeHandle]);
            }

            DestroyWindow(parent);
            if (previousActive != 0)
            {
                SetActiveWindow(previousActive);
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, string lParam);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint hwnd);
}
