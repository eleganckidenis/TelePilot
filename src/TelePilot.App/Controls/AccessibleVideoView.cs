using System.Runtime.InteropServices;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Platform;
using LibVLCSharp.Avalonia;

namespace TelePilot.App.Controls;

/// <summary>
/// Hosts LibVLC video without allowing the native rendering window to become
/// a keyboard-focus target on Windows.
/// </summary>
public sealed class AccessibleVideoView : VideoView
{
    public AccessibleVideoView()
    {
        Focusable = false;
        IsTabStop = false;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        if (ShouldDisableNativeInput(
                OperatingSystem.IsWindows(),
                handle.Handle,
                handle.HandleDescriptor))
        {
            // LibVLC still renders into a disabled child HWND, but Windows can
            // no longer move keyboard focus from Avalonia controls into it.
            EnableWindow(handle.Handle, enable: false);
        }

        return handle;
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new InertVideoSurfaceAutomationPeer(this);

    internal static bool ShouldDisableNativeInput(
        bool isWindows,
        nint handle,
        string? handleDescriptor)
    {
        return isWindows
            && handle != 0
            && string.Equals(handleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(
        nint windowHandle,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    /// <summary>
    /// The video HWND is an output surface, not an interactive control. The
    /// default NativeControlHost peer exposes that HWND as a separate UIA
    /// fragment. LibVLC can replace descendants of the surface while stopping
    /// or reconnecting, which can leave a screen reader tracking the native
    /// provider instead of Avalonia's stable root. Keep the inert Avalonia peer
    /// but deliberately omit the hosted HWND from the automation tree.
    /// </summary>
    private sealed class InertVideoSurfaceAutomationPeer(AccessibleVideoView owner)
        : ControlAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => null;
    }
}
