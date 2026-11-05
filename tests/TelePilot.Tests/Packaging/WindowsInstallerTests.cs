namespace TelePilot.Tests.Packaging;

public sealed class WindowsInstallerTests
{
    [Theory]
    [InlineData("win-x64", "/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-essentials_build.zip",
        "tools\\ffmpeg", 321521840, "fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9")]
    [InlineData("win-arm64", "/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-31-12-59/ffmpeg-n9.0.2-39-g7adfbe40dc-winarm64-lgpl-9.0.zip",
        "tools\\ffmpeg-arm64", 256581782, "d1981518b99a1c05d6b864200b1ac9459b09e2ee174feffe84b6e6bb0bfa79e2")]
    public void FfmpegDownload_UsesPinnedArchiveForRuntime(string runtime, string archivePath, string destination, int size, string sha256)
    {
        string script = File.ReadAllText(FindInstallerScript()).ReplaceLineEndings("\n");

        string branch = Assert.Single(script.Split("[Files]\n", StringSplitOptions.None)[1]
            .Split("#endif", StringSplitOptions.None)[0]
            .Split(["#if Runtime == ", "#elif Runtime == "], StringSplitOptions.None),
            block => block.StartsWith($"\"{runtime}\"", StringComparison.Ordinal));
        string downloadEntry = Assert.Single(branch.Split('\n'), line => line.Contains("Check: ShouldInstallFfmpeg", StringComparison.Ordinal));
        string url = downloadEntry.Split('"')[1];
        Uri download = new(url);
        Assert.Equal(Uri.UriSchemeHttps, download.Scheme);
        Assert.Equal("github.com", download.Host);
        Assert.Equal(archivePath, download.AbsolutePath);
        Assert.Empty(download.Query);
        Assert.Contains("DestName: \"" + Path.GetFileName(download.AbsolutePath) + "\"", downloadEntry, StringComparison.Ordinal);
        Assert.Contains($"DestDir: \"{{app}}\\{destination}\"", downloadEntry, StringComparison.Ordinal);

        Assert.Contains("ArchiveExtraction=full", script, StringComparison.Ordinal);
        Assert.Contains(
            "Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion",
            downloadEntry,
            StringComparison.Ordinal);
        Assert.Contains($"ExternalSize: {size}", downloadEntry, StringComparison.Ordinal);
        Assert.Contains($"Hash: \"{sha256}\"", downloadEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("DownloadTemporaryFile(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractArchive(", script, StringComparison.Ordinal);
    }

    private static string FindInstallerScript()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "installer",
                "windows",
                "TelePilot.iss");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Nie znaleziono skryptu instalatora TelePilot.iss.");
    }
}
