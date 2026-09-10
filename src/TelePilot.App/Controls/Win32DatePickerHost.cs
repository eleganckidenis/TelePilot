using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TelePilot.App.Controls;

internal sealed class Win32DatePickerHost : NativeControlHost
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsExClientEdge = 0x00000200;
    private const uint DtsShortDateFormat = 0x0000;
    private const uint DtsShowNone = 0x0002;
    private const uint IccDateClasses = 0x00000100;
    private const int GwlpWndProc = -4;
    private const uint GaRoot = 2;
    private const int DefaultGuiFont = 17;

    private const uint WmSetFont = 0x0030;
    private const uint WmSetFocus = 0x0007;
    private const uint WmKillFocus = 0x0008;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmTimer = 0x0113;
    private const uint DtmFirst = 0x1000;
    private const uint DtmGetSystemTime = DtmFirst + 1;
    private const uint DtmSetSystemTime = DtmFirst + 2;
    private const int GdtValid = 0;
    private const int GdtNone = 1;
    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;
    private const int VkShift = 0x10;
    private const nuint DatePollTimerId = 1;

    private nint windowHandle;
    private nint originalWindowProcedure;
    private WindowProcedureDelegate? windowProcedure;
    private DateTime? selectedDate;
    private string accessibleName = string.Empty;
    private bool isApplyingDate;
    private bool isDateReadQueued;

    internal event EventHandler<DateTime?>? NativeDateChanged;

    internal event EventHandler<bool>? NativeTabNavigationRequested;

    internal event EventHandler? NativeFocusReceived;

    internal DateTime? SelectedDate
    {
        get => selectedDate;
        set
        {
            DateTime? normalized = value?.Date;
            if (selectedDate == normalized)
            {
                return;
            }

            selectedDate = normalized;
            ApplyDate();
        }
    }

    internal string AccessibleName
    {
        get => accessibleName;
        set
        {
            value ??= string.Empty;
            if (string.Equals(accessibleName, value, StringComparison.Ordinal))
            {
                return;
            }

            accessibleName = value;
            ApplyAccessibleName();
        }
    }

    internal bool FocusNativePicker()
    {
        if (windowHandle == 0 || !OperatingSystem.IsWindows())
        {
            return false;
        }

        SetFocus(windowHandle);
        return GetFocus() == windowHandle;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows()
            || !string.Equals(parent.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            return base.CreateNativeControlCore(parent);
        }

        var initialization = new InitCommonControlsExData
        {
            Size = (uint)Marshal.SizeOf<InitCommonControlsExData>(),
            Classes = IccDateClasses,
        };
        if (!InitializeCommonControlsEx(ref initialization))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        nint hwnd = CreateWindowExW(
            WsExClientEdge,
            "SysDateTimePick32",
            string.Empty,
            WsChild | WsVisible | WsTabStop | WsClipSiblings | DtsShortDateFormat | DtsShowNone,
            0,
            0,
            180,
            32,
            parent.Handle,
            0,
            0,
            0);
        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            windowHandle = hwnd;
            windowProcedure = WindowProcedure;
            nint callback = Marshal.GetFunctionPointerForDelegate(windowProcedure);
            Marshal.SetLastPInvokeError(0);
            originalWindowProcedure = SetWindowLongPointer(hwnd, GwlpWndProc, callback);
            if (originalWindowProcedure == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            SendMessageW(hwnd, WmSetFont, GetStockObject(DefaultGuiFont), 1);
            ApplyDate();
            ApplyAccessibleName();
            return new PlatformHandle(hwnd, "HWND");
        }
        catch
        {
            windowHandle = 0;
            originalWindowProcedure = 0;
            windowProcedure = null;
            DestroyWindow(hwnd);
            throw;
        }
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!OperatingSystem.IsWindows()
            || !string.Equals(control.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            base.DestroyNativeControlCore(control);
            return;
        }

        nint hwnd = control.Handle;
        isDateReadQueued = false;
        if (hwnd != 0)
        {
            KillTimer(hwnd, DatePollTimerId);
        }

        if (hwnd != 0 && originalWindowProcedure != 0)
        {
            SetWindowLongPointer(hwnd, GwlpWndProc, originalWindowProcedure);
        }

        windowHandle = 0;
        originalWindowProcedure = 0;
        windowProcedure = null;
        if (hwnd != 0)
        {
            DestroyWindow(hwnd);
        }
    }

    private nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmKeyDown && wParam == VkTab)
        {
            QueueDateRead();
            bool moveBackward = (GetKeyState(VkShift) & 0x8000) != 0;
            nint topLevelHandle = GetAncestor(hwnd, GaRoot);
            if (topLevelHandle != 0)
            {
                SetFocus(topLevelHandle);
            }

            NativeTabNavigationRequested?.Invoke(this, moveBackward);
            return 0;
        }

        if (message == WmKeyDown && wParam == VkEscape)
        {
            nint topLevelHandle = GetAncestor(hwnd, GaRoot);
            if (topLevelHandle != 0)
            {
                PostMessageW(topLevelHandle, message, wParam, lParam);
            }

            return 0;
        }

        if (message == WmChar && wParam == VkTab)
        {
            return 0;
        }

        nint result = originalWindowProcedure != 0
            ? CallWindowProcW(originalWindowProcedure, hwnd, message, wParam, lParam)
            : DefWindowProcW(hwnd, message, wParam, lParam);

        switch (message)
        {
            case WmSetFocus:
                SetTimer(hwnd, DatePollTimerId, 100, 0);
                NativeFocusReceived?.Invoke(this, EventArgs.Empty);
                break;
            case WmKillFocus:
                KillTimer(hwnd, DatePollTimerId);
                QueueDateRead();
                break;
            case WmKeyUp:
            case WmLButtonUp:
            case WmTimer when (nuint)wParam == DatePollTimerId:
                QueueDateRead();
                break;
        }

        return result;
    }

    private void QueueDateRead()
    {
        if (isApplyingDate || isDateReadQueued)
        {
            return;
        }

        isDateReadQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                isDateReadQueued = false;
                DateTime? currentDate = ReadDate();
                if (selectedDate != currentDate)
                {
                    selectedDate = currentDate;
                    NativeDateChanged?.Invoke(this, currentDate);
                }
            },
            DispatcherPriority.Input);
    }

    private DateTime? ReadDate()
    {
        if (windowHandle == 0 || !OperatingSystem.IsWindows())
        {
            return selectedDate;
        }

        var systemTime = new SystemTime();
        int result = (int)SendMessageW(windowHandle, DtmGetSystemTime, 0, ref systemTime);
        if (result != GdtValid)
        {
            return null;
        }

        try
        {
            return new DateTime(systemTime.Year, systemTime.Month, systemTime.Day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return selectedDate;
        }
    }

    private void ApplyDate()
    {
        if (windowHandle == 0 || !OperatingSystem.IsWindows())
        {
            return;
        }

        isApplyingDate = true;
        try
        {
            if (selectedDate is not { } value)
            {
                SendMessageW(windowHandle, DtmSetSystemTime, GdtNone, 0);
                return;
            }

            var systemTime = new SystemTime
            {
                Year = (ushort)value.Year,
                Month = (ushort)value.Month,
                Day = (ushort)value.Day,
                DayOfWeek = (ushort)value.DayOfWeek,
            };
            SendMessageW(windowHandle, DtmSetSystemTime, GdtValid, ref systemTime);
        }
        finally
        {
            isApplyingDate = false;
        }
    }

    private void ApplyAccessibleName()
    {
        if (windowHandle != 0 && OperatingSystem.IsWindows())
        {
            NativeAccessibility.TrySetName(windowHandle, accessibleName);
        }
    }

    private static nint SetWindowLongPointer(nint hwnd, int index, nint value) =>
        nint.Size == 8
            ? SetWindowLongPtrW(hwnd, index, value)
            : SetWindowLongW(hwnd, index, value);

    [StructLayout(LayoutKind.Sequential)]
    private struct InitCommonControlsExData
    {
        internal uint Size;
        internal uint Classes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        internal ushort Year;
        internal ushort Month;
        internal ushort DayOfWeek;
        internal ushort Day;
        internal ushort Hour;
        internal ushort Minute;
        internal ushort Second;
        internal ushort Milliseconds;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedureDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitCommonControlsEx(ref InitCommonControlsExData initialization);

    private static bool InitializeCommonControlsEx(ref InitCommonControlsExData initialization) =>
        InitCommonControlsEx(ref initialization);

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

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern nint SetWindowLongW(nint hwnd, int index, nint value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CallWindowProcW(
        nint previousWindowProcedure,
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, ref SystemTime lParam);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nuint SetTimer(nint hwnd, nuint eventId, uint intervalMilliseconds, nint timerProcedure);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(nint hwnd, nuint eventId);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int objectIndex);

    private static class NativeAccessibility
    {
        private const uint ObjIdClient = unchecked((uint)-4);
        private const uint ChildIdSelf = 0;
        private static readonly Guid AccessibleNameProperty =
            new("608D3DF8-8128-4AA7-A428-F55E49267291");

        internal static void TrySetName(nint hwnd, string name)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            object? instance = null;
            try
            {
                instance = Activator.CreateInstance<AccPropServicesClass>();
                if (instance is IAccPropServices service)
                {
                    service.SetHwndPropStr(hwnd, ObjIdClient, ChildIdSelf, AccessibleNameProperty, name);
                }
            }
            catch (COMException)
            {
                // The native picker remains usable if dynamic annotation is unavailable.
            }
            catch (TargetInvocationException)
            {
                // COM activation can fail in stripped-down Windows environments.
            }
            finally
            {
                if (instance is not null && Marshal.IsComObject(instance))
                {
                    Marshal.FinalReleaseComObject(instance);
                }
            }
        }

        [ComImport]
        [Guid("B5F8350B-0548-48B1-A6EE-88BD00B4A5E7")]
        private sealed class AccPropServicesClass;

        [ComImport]
        [Guid("6E26E776-04F0-495D-80E4-3330352E3169")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAccPropServices
        {
            [PreserveSig]
            int SetPropValue(nint identity, uint identityLength, Guid propertyId, object value);

            [PreserveSig]
            int SetPropServer(nint identity, uint identityLength, nint propertyIds, int propertyCount, nint server, int scope);

            [PreserveSig]
            int ClearProps(nint identity, uint identityLength, nint propertyIds, int propertyCount);

            [PreserveSig]
            int SetHwndProp(nint hwnd, uint objectId, uint childId, Guid propertyId, object value);

            [PreserveSig]
            int SetHwndPropStr(
                nint hwnd,
                uint objectId,
                uint childId,
                Guid propertyId,
                [MarshalAs(UnmanagedType.LPWStr)] string value);
        }
    }
}
