using System.Runtime.InteropServices;

namespace TelePilot.NativeFullscreenSmoke;

internal static class WindowsDesktop
{
    public const byte Enter = 0x0d;
    public const byte Tab = 0x09;
    public const byte Shift = 0x10;
    public const byte Control = 0x11;
    public const byte Escape = 0x1b;
    public const byte Down = 0x28;
    public const byte L = 0x4c;
    public const byte S = 0x53;
    public const byte F11 = 0x7a;

    public static void EnablePerMonitorDpi() => _ = SetProcessDpiAwarenessContext(-4);

    public static bool IsActiveOnUiThread(nint window) => GetActiveWindow() == window;

    public static bool HasKeyboardFocus(nint window) => GetFocus() == window;

    public static string DescribeWindows(nint window) =>
        $"Windows HWNDs: test=0x{window:X}, foreground=0x{GetForegroundWindow():X}, " +
        $"thread active=0x{GetActiveWindow():X}, thread focus=0x{GetFocus():X}.";

    public static void Press(nint window, byte key, byte modifier = 0)
    {
        // The hosted desktop can leave the global foreground HWND unset while
        // the test UI thread still owns activation and keyboard focus.
        if (!IsActiveOnUiThread(window) || !HasKeyboardFocus(window))
        {
            throw new InvalidOperationException($"Native keyboard focus left the synthetic playback window or moved to a child HWND. {DescribeWindows(window)}");
        }

        byte[]? previousState = null;
        if (modifier != 0)
        {
            previousState = new byte[256];
            if (!GetKeyboardState(previousState))
            {
                throw new InvalidOperationException("Could not read the synthetic window's keyboard state.");
            }

            // Set the UI thread's modifier for these messages only. Global input
            // injection can be intercepted by desktop hooks and other software.
            var state = (byte[])previousState.Clone();
            state[modifier] |= 0x80;
            state[modifier == Shift ? 0xa0 : 0xa2] |= 0x80; // VK_LSHIFT or VK_LCONTROL
            if (!SetKeyboardState(state))
            {
                throw new InvalidOperationException("Could not set the synthetic window's keyboard modifier.");
            }
        }

        try
        {
            const uint keyDown = 0x100;
            const uint keyUp = 0x101;
            const uint released = 0xc0000000;
            var scan = MapVirtualKey(key, 4);
            var keyData = 1 | ((scan & 0xff) << 16) | ((scan & 0xff00) == 0xe000 ? 1u << 24 : 0);
            _ = SendMessage(window, keyDown, key, unchecked((int)keyData));
            _ = SendMessage(window, keyUp, key, unchecked((int)(keyData | released)));
        }
        finally
        {
            if (previousState is not null)
            {
                _ = SetKeyboardState(previousState);
            }
        }
    }

    public static string? CheckGradient(nint window, PixelRect video, double contentAspect, bool fullscreen)
    {
        if (video.Width < 32 || video.Height < 32)
        {
            return $"The video area is too small to sample: {video}.";
        }

        if (fullscreen)
        {
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor))
            {
                return "Could not determine the fullscreen monitor.";
            }

            if (!video.Matches(monitor.Bounds, tolerance: 2))
            {
                return $"Video area {video} does not cover monitor {monitor.Bounds}.";
            }

            video = monitor.Bounds;
        }

        var screen = GetDC(0);
        if (screen == 0)
        {
            return "GetDC(NULL) failed to open the Windows desktop for sampling.";
        }

        try
        {
            var viewportAspect = (double)video.Width / video.Height;
            var retainedX = fullscreen ? Math.Min(1, viewportAspect / contentAspect) : 1;
            var retainedY = fullscreen ? Math.Min(1, contentAspect / viewportAspect) : 1;
            var fittedX = fullscreen ? 1 : Math.Min(1, contentAspect / viewportAspect);
            var fittedY = fullscreen ? 1 : Math.Min(1, viewportAspect / contentAspect);
            double[] positionsX = fullscreen ? [2d / video.Width, 0.25, 0.5, 0.75, 1 - (3d / video.Width)] : [0.15, 0.5, 0.85];
            double[] positionsY = fullscreen ? [2d / video.Height, 0.25, 0.5, 0.75, 1 - (3d / video.Height)] : [0.15, 0.5, 0.85];
            const int colorTolerance = 12; // Allow YUV conversion, quantization and interpolation across renderers.

            // The red and green ramps distinguish a centered crop from stretching
            // or shifting the picture. Fullscreen includes samples near all edges.
            foreach (var x in positionsX)
            {
                foreach (var y in positionsY)
                {
                    var px = video.Left + (int)Math.Round((0.5 + ((x - 0.5) * fittedX)) * video.Width);
                    var py = video.Top + (int)Math.Round((0.5 + ((y - 0.5) * fittedY)) * video.Height);
                    if (ReadPixel(screen, px, py) is not { } color)
                    {
                        return $"GetPixel returned CLR_INVALID at desktop coordinate ({px}, {py}) in {video}.";
                    }

                    var (red, green, blue) = color;
                    var expectedRed = 48 + (160 * (0.5 + ((x - 0.5) * retainedX)));
                    var expectedGreen = 48 + (160 * (0.5 + ((y - 0.5) * retainedY)));
                    if (Math.Abs(red - expectedRed) > colorTolerance || Math.Abs(green - expectedGreen) > colorTolerance ||
                        Math.Abs(blue - 24) > colorTolerance)
                    {
                        return $"Pixel ({px}, {py}) is RGB ({red}, {green}, {blue}), expected near ({expectedRed:F0}, {expectedGreen:F0}, 24).";
                    }
                }
            }

            if (fittedX < 0.9 && (!IsBlack(screen, video.Left + 4, video.Top + (video.Height / 2)) ||
                !IsBlack(screen, video.Right - 5, video.Top + (video.Height / 2))))
            {
                return "Windowed playback lost its expected left/right aspect-fit bars.";
            }

            if (fittedY < 0.9 && (!IsBlack(screen, video.Left + (video.Width / 2), video.Top + 4) ||
                !IsBlack(screen, video.Left + (video.Width / 2), video.Bottom - 5)))
            {
                return "Windowed playback lost its expected top/bottom aspect-fit bars.";
            }

            return null;
        }
        finally
        {
            _ = ReleaseDC(0, screen);
        }
    }

    private static bool IsBlack(nint screen, int x, int y)
    {
        return ReadPixel(screen, x, y) is { Red: < 24, Green: < 24, Blue: < 24 };
    }

    private static (int Red, int Green, int Blue)? ReadPixel(nint screen, int x, int y)
    {
        var pixel = GetPixel(screen, x, y);
        return pixel == uint.MaxValue ? null : ((int)(pixel & 255), (int)((pixel >> 8) & 255), (int)((pixel >> 16) & 255));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public bool Matches(PixelRect other, int tolerance) => Math.Abs(Left - other.Left) <= tolerance &&
            Math.Abs(Top - other.Top) <= tolerance && Math.Abs(Right - other.Right) <= tolerance &&
            Math.Abs(Bottom - other.Bottom) <= tolerance;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public PixelRect Bounds;
        private PixelRect workingArea;
        private uint flags;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetFocus();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] state);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW", ExactSpelling = true)]
    private static extern uint MapVirtualKey(uint code, uint mapping);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern uint GetPixel(nint deviceContext, int x, int y);
}
