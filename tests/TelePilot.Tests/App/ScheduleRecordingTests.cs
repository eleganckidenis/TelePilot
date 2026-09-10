using System.Xml.Linq;
using System.Globalization;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.Recording;
using Microsoft.Extensions.Logging.Abstractions;
using TelePilot.Infrastructure;

namespace TelePilot.Tests.App;

public sealed class ScheduleRecordingTests
{
    [Fact]
    public async Task TaskXml_FileEncodingMatchesDeclarationAndPreservesPolishText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"telepilot-task-encoding-{Guid.NewGuid():N}.xml");
        try
        {
            var entry = CreateEntry() with { ChannelName = "Łódź i Śląsk" };
            var document = WindowsRecordingTaskScheduler.BuildTaskDocument(entry, "TelePilot.exe", false);
            await WindowsRecordingTaskScheduler.WriteTaskDocumentAsync(path, document, TestContext.Current.CancellationToken);
            var parsed = XDocument.Load(path);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            Assert.Equal("Nagranie TelePilot: Łódź i Śląsk", parsed.Descendants(ns + "Description").Single().Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ScheduleStore_ConcurrentInstancesPreserveAllRecordings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"telepilot-schedule-concurrency-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "schedule.json");
        try
        {
            using var first = new ScheduledRecordingStore(new JsonSettingsStore<ScheduledRecordingSettings>(
                path, () => new(), NullLogger<JsonSettingsStore<ScheduledRecordingSettings>>.Instance));
            using var second = new ScheduledRecordingStore(new JsonSettingsStore<ScheduledRecordingSettings>(
                path, () => new(), NullLogger<JsonSettingsStore<ScheduledRecordingSettings>>.Instance));
            var entries = Enumerable.Range(0, 12).Select(_ => CreateEntry()).ToArray();
            await Task.WhenAll(entries.Select((entry, index) =>
                (index % 2 == 0 ? first : second).SaveAsync(entry, TestContext.Current.CancellationToken)));
            var stored = await first.GetAllAsync(TestContext.Current.CancellationToken);
            Assert.Equal(entries.Select(entry => entry.Id).Order(), stored.Select(entry => entry.Id).Order());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ViewModel_RejectsEndBeforeStart()
    {
        var channel = new ChannelListItemViewModel(Guid.NewGuid().ToString("D"), "TTV", "Ogólne");
        var start = DateTimeOffset.Now.AddHours(2);
        var viewModel = new ScheduleRecordingViewModel([channel], channel.Id, start, start.AddHours(1));
        viewModel.EndDate = start.LocalDateTime.Date;
        viewModel.EndTime = start.AddMinutes(-5).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        viewModel.SaveCommand.Execute(null);

        Assert.Contains("po jego rozpoczęciu", viewModel.ValidationMessage, StringComparison.CurrentCulture);
    }

    [Fact]
    public void TaskXml_UsesOneTimeTriggerLeastPrivilegeAndHeadlessRecordingId()
    {
        var entry = CreateEntry();
        var executable = Path.Combine("C:\\Program Files", "TelePilot", "TelePilot.exe");

        XDocument document = WindowsRecordingTaskScheduler.BuildTaskDocument(entry, executable, wakeComputer: true);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal("InteractiveToken", document.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal("LeastPrivilege", document.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("true", document.Descendants(ns + "WakeToRun").Single().Value);
        Assert.Equal(executable, document.Descendants(ns + "Command").Single().Value);
        Assert.Equal($"--run-scheduled-recording {entry.Id:D}", document.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal(
            entry.Start.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            document.Descendants(ns + "StartBoundary").Single().Value);
        Assert.Equal(
            entry.End.AddMinutes(30).ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            document.Descendants(ns + "EndBoundary").Single().Value);
        Assert.Equal("PT1H", document.Descendants(ns + "DeleteExpiredTaskAfter").Single().Value);
    }

    [Fact]
    public void CurrentUserProtector_RoundTripsWithoutLeavingPlainText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string secret = "https://example.invalid/live?token=ściśle-tajne";
        var protector = new WindowsCurrentUserDataProtector();

        var protectedValue = protector.Protect(secret);

        Assert.DoesNotContain("example.invalid", protectedValue, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(secret, protector.Unprotect(protectedValue));
    }

    [Theory]
    [InlineData("--run-scheduled-recording")]
    [InlineData("--run-scheduled-recording", "not-a-guid")]
    [InlineData("--run-scheduled-recording", "00000000-0000-0000-0000-000000000000")]
    public void CommandLineParser_RejectsInvalidScheduledInvocation(params string[] args)
    {
        Assert.False(TelePilot.App.Program.TryGetScheduledRecordingId(args, out _));
    }

    [Fact]
    public void CommandLineParser_AcceptsExactScheduledInvocation()
    {
        var id = Guid.NewGuid();

        Assert.True(
            TelePilot.App.Program.TryGetScheduledRecordingId(
                ["--run-scheduled-recording", id.ToString("D")],
                out var parsed));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public async Task RealTaskScheduler_RegistersAndDeletesWhenExplicitlyEnabled()
    {
        if (!OperatingSystem.IsWindows()
            || !string.Equals(
                Environment.GetEnvironmentVariable("TELEPILOT_TEST_TASK_SCHEDULER"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Test wymaga Windows i TELEPILOT_TEST_TASK_SCHEDULER=1.");
            return;
        }

        var processPath = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(processPath));
        var entry = CreateEntry() with
        {
            Start = DateTimeOffset.Now.AddHours(12),
            End = DateTimeOffset.Now.AddHours(13),
        };
        var scheduler = new WindowsRecordingTaskScheduler();
        var registered = false;
        try
        {
            await scheduler.RegisterAsync(
                entry,
                processPath,
                wakeComputer: false,
                TestContext.Current.CancellationToken);
            registered = true;
        }
        finally
        {
            if (registered)
            {
                await scheduler.DeleteAsync(entry.Id, CancellationToken.None);
            }
        }
    }

    private static ScheduledRecordingEntry CreateEntry()
    {
        var start = DateTimeOffset.Now.AddHours(1);
        return new ScheduledRecordingEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TTV",
            "Program",
            start,
            start.AddHours(2),
            RecordingMode.StreamCopy,
            "protected",
            Path.GetTempPath(),
            AudioOutputFormat.Mp3,
            192,
            SafeFileNameGenerator.DefaultTemplate);
    }
}
