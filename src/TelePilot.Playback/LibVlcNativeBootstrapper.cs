namespace TelePilot.Playback;

internal sealed record LibVlcNativePaths(
    string LibraryDirectory,
    string PluginDirectory);

internal static class LibVlcNativeBootstrapper
{
    internal const string LibraryPathEnvironmentVariable = "TELEPILOT_LIBVLC_PATH";
    internal const string PluginPathEnvironmentVariable = "TELEPILOT_VLC_PLUGIN_PATH";
    private const string VlcPluginPathEnvironmentVariable = "VLC_PLUGIN_PATH";

    public static void Initialize()
    {
        if (!OperatingSystem.IsMacOS())
        {
            LibVLCSharp.Shared.Core.Initialize();
            return;
        }

        var paths = ResolveMacPaths(
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable(LibraryPathEnvironmentVariable),
            Environment.GetEnvironmentVariable(PluginPathEnvironmentVariable));
        if (paths is null)
        {
            LibVLCSharp.Shared.Core.Initialize();
            return;
        }

        Environment.SetEnvironmentVariable(
            VlcPluginPathEnvironmentVariable,
            paths.PluginDirectory);
        LibVLCSharp.Shared.Core.Initialize(paths.LibraryDirectory);
    }

    internal static LibVlcNativePaths? ResolveMacPaths(
        string applicationBaseDirectory,
        string? configuredLibraryDirectory = null,
        string? configuredPluginDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);

        if (!string.IsNullOrWhiteSpace(configuredLibraryDirectory))
        {
            var libraryDirectory = GetConfiguredFullPath(
                configuredLibraryDirectory,
                LibraryPathEnvironmentVariable);
            var pluginDirectory = !string.IsNullOrWhiteSpace(configuredPluginDirectory)
                ? GetConfiguredFullPath(
                    configuredPluginDirectory,
                    PluginPathEnvironmentVariable)
                : Path.GetFullPath(Path.Combine(libraryDirectory, "..", "plugins"));
            return ValidateMacPaths(
                libraryDirectory,
                pluginDirectory,
                "Ścieżki LibVLC skonfigurowane w środowisku są niekompletne.");
        }

        if (!string.IsNullOrWhiteSpace(configuredPluginDirectory))
        {
            throw new InvalidOperationException(
                $"{PluginPathEnvironmentVariable} wymaga również {LibraryPathEnvironmentVariable}.");
        }

        var bundleRoot = Path.GetFullPath(Path.Combine(
            applicationBaseDirectory,
            "..",
            "Frameworks",
            "libvlc"));
        if (!Directory.Exists(bundleRoot))
        {
            return null;
        }

        return ValidateMacPaths(
            Path.Combine(bundleRoot, "lib"),
            Path.Combine(bundleRoot, "plugins"),
            "Pakiet TelePilot.app zawiera niekompletny zestaw LibVLC.");
    }

    private static LibVlcNativePaths ValidateMacPaths(
        string libraryDirectory,
        string pluginDirectory,
        string errorMessage)
    {
        var fullLibraryDirectory = Path.GetFullPath(libraryDirectory);
        var fullPluginDirectory = Path.GetFullPath(pluginDirectory);
        var libVlc = Path.Combine(fullLibraryDirectory, "libvlc.dylib");
        var libVlcCore = Path.Combine(fullLibraryDirectory, "libvlccore.dylib");
        var containsPlugin = Directory.Exists(fullPluginDirectory)
            && Directory.EnumerateFiles(
                fullPluginDirectory,
                "*.dylib",
                SearchOption.AllDirectories).Any();

        if (!File.Exists(libVlc)
            || !File.Exists(libVlcCore)
            || !containsPlugin)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return new LibVlcNativePaths(fullLibraryDirectory, fullPluginDirectory);
    }

    private static string GetConfiguredFullPath(string configuredPath, string variableName)
    {
        if (!Path.IsPathFullyQualified(configuredPath))
        {
            throw new InvalidOperationException(
                $"{variableName} musi wskazywać bezwzględną ścieżkę.");
        }

        return Path.GetFullPath(configuredPath);
    }
}
