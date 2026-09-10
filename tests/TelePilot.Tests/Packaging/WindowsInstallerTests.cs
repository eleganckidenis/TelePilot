namespace TelePilot.Tests.Packaging;

public sealed class WindowsInstallerTests
{
    [Fact]
    public void FfmpegDownload_UsesZipCapableExtractionAndBuiltInProgress()
    {
        string script = File.ReadAllText(FindInstallerScript());

        Assert.Contains("ArchiveExtraction=full", script, StringComparison.Ordinal);
        Assert.Contains(
            "Flags: external download extractarchive recursesubdirs createallsubdirs ignoreversion",
            script,
            StringComparison.Ordinal);
        Assert.Contains("ExternalSize: 321521840", script, StringComparison.Ordinal);
        Assert.Contains(
            "Hash: \"fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9\"",
            script,
            StringComparison.Ordinal);
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
