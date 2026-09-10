using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class LibVlcNativeBootstrapperTests
{
    [Fact]
    public void ResolveMacPaths_ReturnsBundledFrameworkLayout()
    {
        using var context = new BundleContext();
        context.CreateCompleteBundle();

        var result = LibVlcNativeBootstrapper.ResolveMacPaths(context.MacOsDirectory);

        Assert.NotNull(result);
        Assert.Equal(context.LibraryDirectory, result.LibraryDirectory);
        Assert.Equal(context.PluginDirectory, result.PluginDirectory);
    }

    [Fact]
    public void ResolveMacPaths_WithoutBundledOrConfiguredLibrariesUsesDefaultDiscovery()
    {
        using var context = new BundleContext();

        var result = LibVlcNativeBootstrapper.ResolveMacPaths(context.MacOsDirectory);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveMacPaths_WithIncompleteBundleFailsBeforeStartingThePlayer()
    {
        using var context = new BundleContext();
        Directory.CreateDirectory(context.LibraryDirectory);
        File.WriteAllText(Path.Combine(context.LibraryDirectory, "libvlc.dylib"), string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(
            () => LibVlcNativeBootstrapper.ResolveMacPaths(context.MacOsDirectory));

        Assert.Contains("niekompletny", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveMacPaths_UsesExplicitDevelopmentDirectories()
    {
        using var context = new BundleContext();
        var developmentRoot = Path.Combine(context.RootDirectory, "development-vlc");
        var libraryDirectory = Path.Combine(developmentRoot, "lib");
        var pluginDirectory = Path.Combine(developmentRoot, "plugins");
        CreateNativeFiles(libraryDirectory, pluginDirectory);

        var result = LibVlcNativeBootstrapper.ResolveMacPaths(
            context.MacOsDirectory,
            libraryDirectory,
            pluginDirectory);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(libraryDirectory), result.LibraryDirectory);
        Assert.Equal(Path.GetFullPath(pluginDirectory), result.PluginDirectory);
    }

    [Fact]
    public void ResolveMacPaths_WithLibraryOverrideUsesSiblingPluginDirectory()
    {
        using var context = new BundleContext();
        var developmentRoot = Path.Combine(context.RootDirectory, "development-vlc");
        var libraryDirectory = Path.Combine(developmentRoot, "lib");
        var pluginDirectory = Path.Combine(developmentRoot, "plugins");
        CreateNativeFiles(libraryDirectory, pluginDirectory);

        var result = LibVlcNativeBootstrapper.ResolveMacPaths(
            context.MacOsDirectory,
            libraryDirectory);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(pluginDirectory), result.PluginDirectory);
    }

    [Fact]
    public void ResolveMacPaths_ExplicitPathsTakePrecedenceOverBundle()
    {
        using var context = new BundleContext();
        context.CreateCompleteBundle();
        var developmentRoot = Path.Combine(context.RootDirectory, "development-vlc");
        var libraryDirectory = Path.Combine(developmentRoot, "lib");
        var pluginDirectory = Path.Combine(developmentRoot, "plugins");
        CreateNativeFiles(libraryDirectory, pluginDirectory);

        var result = LibVlcNativeBootstrapper.ResolveMacPaths(
            context.MacOsDirectory,
            libraryDirectory,
            pluginDirectory);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(libraryDirectory), result.LibraryDirectory);
        Assert.Equal(Path.GetFullPath(pluginDirectory), result.PluginDirectory);
    }

    [Fact]
    public void ResolveMacPaths_WithEmptyPluginDirectoryFailsBeforeStartingThePlayer()
    {
        using var context = new BundleContext();
        Directory.CreateDirectory(context.LibraryDirectory);
        Directory.CreateDirectory(context.PluginDirectory);
        File.WriteAllText(Path.Combine(context.LibraryDirectory, "libvlc.dylib"), string.Empty);
        File.WriteAllText(Path.Combine(context.LibraryDirectory, "libvlccore.dylib"), string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(
            () => LibVlcNativeBootstrapper.ResolveMacPaths(context.MacOsDirectory));

        Assert.Contains("niekompletny", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveMacPaths_RejectsPluginOverrideWithoutLibraryOverride()
    {
        using var context = new BundleContext();

        Assert.Throws<InvalidOperationException>(() => LibVlcNativeBootstrapper.ResolveMacPaths(
            context.MacOsDirectory,
            configuredPluginDirectory: context.PluginDirectory));
    }

    [Fact]
    public void ResolveMacPaths_RejectsRelativeEnvironmentOverride()
    {
        using var context = new BundleContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => LibVlcNativeBootstrapper.ResolveMacPaths(
                context.MacOsDirectory,
                Path.Combine("relative", "lib")));

        Assert.Contains(
            LibVlcNativeBootstrapper.LibraryPathEnvironmentVariable,
            exception.Message,
            StringComparison.Ordinal);
    }

    private static void CreateNativeFiles(string libraryDirectory, string pluginDirectory)
    {
        Directory.CreateDirectory(libraryDirectory);
        Directory.CreateDirectory(pluginDirectory);
        File.WriteAllText(Path.Combine(libraryDirectory, "libvlc.dylib"), string.Empty);
        File.WriteAllText(Path.Combine(libraryDirectory, "libvlccore.dylib"), string.Empty);
        var pluginCategory = Path.Combine(pluginDirectory, "video_output");
        Directory.CreateDirectory(pluginCategory);
        File.WriteAllText(Path.Combine(pluginCategory, "libdummy_plugin.dylib"), string.Empty);
    }

    private sealed class BundleContext : IDisposable
    {
        public BundleContext()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "TelePilot.Tests", Guid.NewGuid().ToString("N"));
            MacOsDirectory = Path.Combine(RootDirectory, "TelePilot.app", "Contents", "MacOS");
            LibraryDirectory = Path.GetFullPath(Path.Combine(
                MacOsDirectory,
                "..",
                "Frameworks",
                "libvlc",
                "lib"));
            PluginDirectory = Path.GetFullPath(Path.Combine(
                MacOsDirectory,
                "..",
                "Frameworks",
                "libvlc",
                "plugins"));
            Directory.CreateDirectory(MacOsDirectory);
        }

        public string RootDirectory { get; }

        public string MacOsDirectory { get; }

        public string LibraryDirectory { get; }

        public string PluginDirectory { get; }

        public void CreateCompleteBundle() =>
            CreateNativeFiles(LibraryDirectory, PluginDirectory);

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }
}
