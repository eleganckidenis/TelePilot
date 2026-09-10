using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TelePilot.App.Controls;

internal sealed class Win32EditHost : NativeControlHost
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsDisabled = 0x08000000;
    private const uint WsExClientEdge = 0x00000200;
    private const uint EsLeft = 0x0000;
    private const uint EsMultiline = 0x0004;
    private const uint EsAutoVScroll = 0x0040;
    private const uint EsAutoHScroll = 0x0080;
    private const uint EsReadOnly = 0x0800;
    private const uint EsWantReturn = 0x1000;
    private const int GwlpWndProc = -4;
    private const uint GaRoot = 2;
    private const int DefaultGuiFont = 17;

    private const uint WmSetFont = 0x0030;
    private const uint WmSetFocus = 0x0007;
    private const uint WmSetText = 0x000C;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmCut = 0x0300;
    private const uint WmCopy = 0x0301;
    private const uint WmPaste = 0x0302;
    private const uint WmClear = 0x0303;
    private const uint WmUndo = 0x0304;
    private const uint WmImeEndComposition = 0x010E;
    private const uint EmReplaceSel = 0x00C2;
    private const uint EmSetReadOnly = 0x00CF;
    private const uint EmSetSel = 0x00B1;
    private const uint EmSetCueBanner = 0x1501;
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkEscape = 0x1B;
    private const int VkF10 = 0x79;
    private const int VkA = 0x41;

    private nint windowHandle;
    private nint originalWindowProcedure;
    private WindowProcedureDelegate? windowProcedure;
    private string text = string.Empty;
    private string? watermark;
    private string accessibleName = string.Empty;
    private bool isReadOnly;
    private bool acceptsReturn;
    private TextWrapping textWrapping;
    private bool nativeIsEnabled = true;
    private bool isApplyingText;
    private bool isTextReadQueued;

    internal event EventHandler<string>? NativeTextChanged;

    internal event EventHandler<bool>? NativeTabNavigationRequested;

    internal event EventHandler? NativeFocusReceived;

    internal string Text
    {
        get => text;
        set
        {
            value ??= string.Empty;
            if (string.Equals(text, value, StringComparison.Ordinal))
            {
                return;
            }

            text = value;
            ApplyText();
        }
    }

    internal string? Watermark
    {
        get => watermark;
        set
        {
            if (string.Equals(watermark, value, StringComparison.Ordinal))
            {
                return;
            }

            watermark = value;
            ApplyWatermark();
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

    internal bool IsReadOnly
    {
        get => isReadOnly;
        set
        {
            if (isReadOnly == value)
            {
                return;
            }

            isReadOnly = value;
            ApplyReadOnly();
        }
    }

    internal bool AcceptsReturn
    {
        get => acceptsReturn;
        set => acceptsReturn = value;
    }

    internal TextWrapping TextWrapping
    {
        get => textWrapping;
        set => textWrapping = value;
    }

    internal bool NativeIsEnabled
    {
        get => nativeIsEnabled;
        set
        {
            if (nativeIsEnabled == value)
            {
                return;
            }

            nativeIsEnabled = value;
            ApplyEnabled();
        }
    }

    internal bool FocusNativeEditor()
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

        nint hwnd = CreateWindowExW(
            WsExClientEdge,
            "EDIT",
            text,
            BuildWindowStyle(),
            0,
            0,
            100,
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
            ApplyReadOnly();
            ApplyEnabled();
            ApplyWatermark();
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
        isTextReadQueued = false;
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
        if ((message == WmKeyDown || message == WmKeyUp)
            && wParam == VkF10
            && !HasModifierKeys())
        {
            ForwardKeyToTopLevel(hwnd, message, wParam, lParam);
            return 0;
        }

        if (message == WmKeyDown && wParam == VkTab)
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

        if ((message == WmKeyDown || message == WmKeyUp)
            && (wParam == VkEscape || (wParam == VkReturn && !acceptsReturn)))
        {
            ForwardKeyToTopLevel(hwnd, message, wParam, lParam);
            return 0;
        }

        if (message == WmKeyDown
            && wParam == VkA
            && (GetKeyState(VkControl) & 0x8000) != 0)
        {
            SendMessageW(hwnd, EmSetSel, 0, -1);
            return 0;
        }

        if (message == WmChar
            && (wParam == VkTab || (wParam == VkReturn && !acceptsReturn)))
        {
            return 0;
        }

        nint result = originalWindowProcedure != 0
            ? CallWindowProcW(originalWindowProcedure, hwnd, message, wParam, lParam)
            : DefWindowProcW(hwnd, message, wParam, lParam);

        if (message == WmSetFocus)
        {
            NativeFocusReceived?.Invoke(this, EventArgs.Empty);
        }

        if (!isApplyingText && MessageCanChangeText(message, wParam))
        {
            QueueTextRead();
        }

        return result;
    }

    private static bool MessageCanChangeText(uint message, nint wParam) =>
        message is WmSetText or WmCut or WmPaste or WmClear or WmUndo or WmImeEndComposition or EmReplaceSel
        || message == WmChar
        || (message == WmKeyDown && wParam == 0x2E);

    private uint BuildWindowStyle()
    {
        uint style = WsChild | WsVisible | WsTabStop | WsClipSiblings | EsLeft;
        bool isMultiline = acceptsReturn || textWrapping != Avalonia.Media.TextWrapping.NoWrap;
        if (isMultiline)
        {
            style |= EsMultiline | EsAutoVScroll;
            if (acceptsReturn)
            {
                style |= EsWantReturn;
            }

            if (textWrapping == Avalonia.Media.TextWrapping.NoWrap)
            {
                style |= EsAutoHScroll;
            }
        }
        else
        {
            style |= EsAutoHScroll;
        }

        if (isReadOnly)
        {
            style |= EsReadOnly;
        }

        if (!nativeIsEnabled)
        {
            style |= WsDisabled;
        }

        return style;
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

    private void QueueTextRead()
    {
        if (isTextReadQueued)
        {
            return;
        }

        isTextReadQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                isTextReadQueued = false;
                string currentText = ReadText();
                if (!string.Equals(text, currentText, StringComparison.Ordinal))
                {
                    text = currentText;
                    NativeTextChanged?.Invoke(this, currentText);
                }
            },
            DispatcherPriority.Input);
    }

    private string ReadText()
    {
        if (windowHandle == 0 || !OperatingSystem.IsWindows())
        {
            return text;
        }

        int length = GetWindowTextLengthW(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        int copiedCharacters = GetWindowTextW(windowHandle, buffer, buffer.Length);
        return copiedCharacters > 0
            ? new string(buffer, 0, copiedCharacters)
            : string.Empty;
    }

    private void ApplyText()
    {
        if (windowHandle == 0 || !OperatingSystem.IsWindows())
        {
            return;
        }

        isApplyingText = true;
        try
        {
            SetWindowTextW(windowHandle, text);
        }
        finally
        {
            isApplyingText = false;
        }
    }

    private void ApplyWatermark()
    {
        if (windowHandle != 0 && OperatingSystem.IsWindows())
        {
            SendMessageW(windowHandle, EmSetCueBanner, 1, watermark ?? string.Empty);
        }
    }

    private void ApplyReadOnly()
    {
        if (windowHandle != 0 && OperatingSystem.IsWindows())
        {
            SendMessageW(windowHandle, EmSetReadOnly, isReadOnly ? 1 : 0, 0);
        }
    }

    private void ApplyEnabled()
    {
        if (windowHandle != 0 && OperatingSystem.IsWindows())
        {
            EnableWindow(windowHandle, nativeIsEnabled);
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedureDelegate(nint hwnd, uint message, nint wParam, nint lParam);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enabled);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint hwnd, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(
        nint hwnd,
        [Out] char[] text,
        int maximumCount);

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
                // The native edit remains usable when dynamic annotation is unavailable.
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
