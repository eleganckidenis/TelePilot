using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TelePilot.App.Controls;

internal sealed class Win32ComboBoxHost : NativeControlHost
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsVScroll = 0x00200000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint CbsDropDownList = 0x0003;
    private const uint CbsHasStrings = 0x0200;
    private const int GwlpWndProc = -4;
    private const uint GaRoot = 2;
    private const int DefaultGuiFont = 17;

    private const uint WmCommand = 0x0111;
    private const uint WmSetFont = 0x0030;
    private const uint WmSetFocus = 0x0007;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint CbAddString = 0x0143;
    private const uint CbGetCurSel = 0x0147;
    private const uint CbResetContent = 0x014B;
    private const uint CbSetCurSel = 0x014E;
    private const uint CbSetMinVisible = 0x1701;
    private const int CbError = -1;
    private const int CbErrorSpace = -2;
    private const int CbnSelChange = 1;
    private const int CbnSelEndOk = 9;
    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkUp = 0x26;
    private const int VkDown = 0x28;
    private const int VkF10 = 0x79;

    private static long nextParentSubclassId;

    private nint windowHandle;
    private nint parentWindowHandle;
    private nint originalWindowProcedure;
    private WindowProcedureDelegate? windowProcedure;
    private SubclassProcedureDelegate? parentWindowProcedure;
    private nuint parentSubclassId;
    private string[] items = [];
    private int selectedIndex = -1;
    private string accessibleName = string.Empty;
    private bool isApplyingItems;
    private bool isSelectionReadQueued;

    internal event EventHandler<int>? NativeSelectionChanged;

    internal event EventHandler<bool>? NativeTabNavigationRequested;

    internal event EventHandler? NativeFocusReceived;

    internal IReadOnlyList<string> Items
    {
        get => items;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            items = value.ToArray();
            if (selectedIndex >= items.Length)
            {
                selectedIndex = -1;
            }

            ApplyItems();
        }
    }

    internal int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < -1 || value >= items.Length)
            {
                value = -1;
            }

            if (selectedIndex == value)
            {
                return;
            }

            selectedIndex = value;
            ApplySelectedIndex();
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

    internal bool FocusNativeSelector()
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

        parentSubclassId = unchecked((nuint)Interlocked.Increment(ref nextParentSubclassId));
        nint hwnd = CreateWindowExW(
            0,
            "COMBOBOX",
            string.Empty,
            WsChild | WsVisible | WsTabStop | WsVScroll | WsClipSiblings | CbsDropDownList | CbsHasStrings,
            0,
            0,
            160,
            320,
            parent.Handle,
            unchecked((nint)parentSubclassId),
            0,
            0);
        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            windowHandle = hwnd;
            parentWindowHandle = parent.Handle;

            windowProcedure = WindowProcedure;
            nint callback = Marshal.GetFunctionPointerForDelegate(windowProcedure);
            Marshal.SetLastPInvokeError(0);
            originalWindowProcedure = SetWindowLongPointer(hwnd, GwlpWndProc, callback);
            if (originalWindowProcedure == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            parentWindowProcedure = ParentWindowProcedure;
            if (!SetWindowSubclass(parentWindowHandle, parentWindowProcedure, parentSubclassId, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            SendMessageW(hwnd, WmSetFont, GetStockObject(DefaultGuiFont), 1);
            SendMessageW(hwnd, CbSetMinVisible, 12, 0);
            ApplyItems();
            ApplyAccessibleName();
            return new PlatformHandle(hwnd, "HWND");
        }
        catch
        {
            RemoveParentSubclass();
            if (originalWindowProcedure != 0)
            {
                SetWindowLongPointer(hwnd, GwlpWndProc, originalWindowProcedure);
            }

            windowHandle = 0;
            parentWindowHandle = 0;
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
        isSelectionReadQueued = false;
        RemoveParentSubclass();
        if (hwnd != 0 && originalWindowProcedure != 0)
        {
            SetWindowLongPointer(hwnd, GwlpWndProc, originalWindowProcedure);
        }

        windowHandle = 0;
        parentWindowHandle = 0;
        originalWindowProcedure = 0;
        windowProcedure = null;
        parentSubclassId = 0;
        if (hwnd != 0)
        {
            DestroyWindow(hwnd);
        }
    }

    private nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if ((message == WmKeyDown || message == WmKeyUp) && IsSectionShortcut(wParam))
        {
            ForwardKeyToTopLevel(hwnd, message, wParam, lParam);
            return 0;
        }

        if ((message == WmKeyDown || message == WmKeyUp)
            && wParam == VkF10
            && !HasModifierKeys())
        {
            ForwardKeyToTopLevel(hwnd, message, wParam, lParam);
            return 0;
        }

        if (message == WmKeyDown && wParam == VkTab
            && (GetKeyState(VkControl) & 0x8000) == 0 && (GetKeyState(VkMenu) & 0x8000) == 0)
        {
            bool moveBackward = (GetKeyState(VkShift) & 0x8000) != 0;
            nint topLevelHandle = GetAncestor(hwnd, GaRoot);
            if (topLevelHandle != 0)
            {
                SetFocus(topLevelHandle);
            }

            NativeTabNavigationRequested?.Invoke(this, moveBackward);
            return 0;
        }

        if (message == WmKeyDown && wParam == VkEscape && !HasModifierKeys())
        {
            ForwardKeyToTopLevel(hwnd, message, wParam, lParam);
            return 0;
        }

        if (message == WmChar && wParam == VkTab)
        {
            return 0;
        }

        bool shouldReadSelection = message == WmKeyDown && wParam is VkUp or VkDown;
        nint result = originalWindowProcedure != 0
            ? CallWindowProcW(originalWindowProcedure, hwnd, message, wParam, lParam)
            : DefWindowProcW(hwnd, message, wParam, lParam);

        if (message == WmSetFocus)
        {
            NativeFocusReceived?.Invoke(this, EventArgs.Empty);
        }

        if (shouldReadSelection)
        {
            QueueSelectionRead();
        }

        return result;
    }

    private static void ForwardKeyToTopLevel(nint hwnd, uint message, nint wParam, nint lParam)
    {
        nint topLevelHandle = GetAncestor(hwnd, GaRoot);
        if (topLevelHandle != 0)
        {
            PostMessageW(topLevelHandle, message, wParam, lParam);
        }
    }

    private static bool HasModifierKeys() =>
        (GetKeyState(VkShift) & 0x8000) != 0
        || (GetKeyState(VkControl) & 0x8000) != 0
        || (GetKeyState(VkMenu) & 0x8000) != 0;

    private static bool IsSectionShortcut(nint key) =>
        (GetKeyState(VkControl) & 0x8000) != 0 && (GetKeyState(VkMenu) & 0x8000) == 0
        && (key == VkTab || (key is >= 0x31 and <= 0x33 && (GetKeyState(VkShift) & 0x8000) == 0));

    private nint ParentWindowProcedure(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WmCommand && lParam == windowHandle)
        {
            int notification = (int)((unchecked((nuint)wParam) >> 16) & 0xFFFF);
            if (notification is CbnSelChange or CbnSelEndOk)
            {
                QueueSelectionRead();
            }
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void QueueSelectionRead()
    {
        if (isApplyingItems || isSelectionReadQueued)
        {
            return;
        }

        isSelectionReadQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                isSelectionReadQueued = false;
                int currentIndex = ReadSelectedIndex();
                if (selectedIndex != currentIndex)
                {
                    selectedIndex = currentIndex;
                    NativeSelectionChanged?.Invoke(this, currentIndex);
                }
            },
            DispatcherPriority.Input);
    }

    private int ReadSelectedIndex()
    {
        if (windowHandle == 0 || !OperatingSystem.IsWindows())
        {
            return selectedIndex;
        }

        int index = unchecked((int)SendMessageW(windowHandle, CbGetCurSel, 0, 0));
        return index >= 0 && index < items.Length ? index : -1;
    }

    private void ApplyItems()
    {
        if (windowHandle == 0 || !OperatingSystem.IsWindows())
        {
            return;
        }

        isApplyingItems = true;
        try
        {
            SendMessageW(windowHandle, CbResetContent, 0, 0);
            foreach (string item in items)
            {
                int result = unchecked((int)SendMessageW(windowHandle, CbAddString, 0, item));
                if (result is CbError or CbErrorSpace)
                {
                    throw new Win32Exception("Nie udało się dodać elementu do natywnej listy rozwijanej.");
                }
            }

            SendMessageW(windowHandle, CbSetCurSel, selectedIndex, 0);
        }
        finally
        {
            isApplyingItems = false;
        }
    }

    private void ApplySelectedIndex()
    {
        if (windowHandle != 0 && OperatingSystem.IsWindows())
        {
            SendMessageW(windowHandle, CbSetCurSel, selectedIndex, 0);
        }
    }

    private void ApplyAccessibleName()
    {
        if (windowHandle != 0 && OperatingSystem.IsWindows())
        {
            NativeAccessibility.TrySetName(windowHandle, accessibleName);
        }
    }

    private void RemoveParentSubclass()
    {
        if (parentWindowHandle != 0 && parentWindowProcedure is not null && parentSubclassId != 0)
        {
            RemoveWindowSubclass(parentWindowHandle, parentWindowProcedure, parentSubclassId);
        }

        parentWindowProcedure = null;
    }

    private static nint SetWindowLongPointer(nint hwnd, int index, nint value) =>
        nint.Size == 8
            ? SetWindowLongPtrW(hwnd, index, value)
            : SetWindowLongW(hwnd, index, value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedureDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedureDelegate(
        nint hwnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessageW(
        nint hwnd,
        uint message,
        nint wParam,
        [MarshalAs(UnmanagedType.LPWStr)] string lParam);

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

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int objectIndex);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint hwnd,
        SubclassProcedureDelegate callback,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint hwnd,
        SubclassProcedureDelegate callback,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

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
                // The standard combo remains usable if dynamic annotation is unavailable.
            }
            catch (TargetInvocationException)
            {
                // COM activation can fail on stripped-down Windows environments.
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
