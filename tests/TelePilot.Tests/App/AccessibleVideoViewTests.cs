using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using TelePilot.App.Controls;

namespace TelePilot.Tests.App;

public sealed class AccessibleVideoViewTests
{
    [AvaloniaFact]
    public void VideoSurface_IsExcludedFromKeyboardNavigation()
    {
        AccessibleVideoView view = new();

        Assert.False(view.Focusable);
        Assert.False(view.IsTabStop);
    }

    [Fact]
    public void NativeInput_IsDisabledOnlyForAValidWindowsHwnd()
    {
        MethodInfo method = typeof(AccessibleVideoView).GetMethod(
            "ShouldDisableNativeInput",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Nie znaleziono reguły natywnego hosta wideo.");

        static bool Invoke(MethodInfo target, bool windows, nint handle, string descriptor) =>
            Assert.IsType<bool>(target.Invoke(null, [windows, handle, descriptor]));

        Assert.True(Invoke(method, windows: true, handle: 42, descriptor: "HWND"));
        Assert.True(Invoke(method, windows: true, handle: 42, descriptor: "hwnd"));
        Assert.False(Invoke(method, windows: false, handle: 42, descriptor: "HWND"));
        Assert.False(Invoke(method, windows: true, handle: 0, descriptor: "HWND"));
        Assert.False(Invoke(method, windows: true, handle: 42, descriptor: "NSView"));
    }

    [AvaloniaFact]
    public void VideoSurface_DoesNotExposeTheHostedHwndAsAnAutomationFragment()
    {
        AccessibleVideoView view = new();
        MethodInfo method = typeof(AccessibleVideoView).GetMethod(
            "OnCreateAutomationPeer",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Nie znaleziono izolowanego peera obrazu wideo.");

        Assert.Equal(typeof(AccessibleVideoView), method.DeclaringType);
        AutomationPeer peer = Assert.IsAssignableFrom<AutomationPeer>(method.Invoke(view, null));
        Assert.DoesNotContain("NativeControlHostPeer", peer.GetType().Name, StringComparison.Ordinal);
        Assert.Empty(peer.GetChildren());
    }
}
