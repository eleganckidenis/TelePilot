using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;

namespace TelePilot.Tests.App;

public sealed class UpdateStartupTests
{
    private static readonly byte[] Payload = "MZ synthetic verified installer"u8.ToArray();
    private const string Name = "TelePilot-2.10.0-win-x64-setup.exe";
    private static HttpResponseMessage Metadata(string tag = "v2.10.0") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            tag_name = tag, draft = false, prerelease = false,
            assets = new[] { new { name = Name, state = "uploaded", size = Payload.Length,
                browser_download_url = "https://github.com/eleganckidenis/TelePilot/releases/download/v2.10.0/" + Name,
                digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant() } },
        })),
    };
    private static HttpResponseMessage Bytes(Stream stream) => new(HttpStatusCode.OK) { Content = new StreamContent(stream) };
    private static async Task DrainUntilAsync(Func<bool> condition)
    {
        for (var tick = 0; tick < 100; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task AutomaticAvailabilityAppearsBeforeDownloadCompletesWithoutDisablingOrRefocusingOwner()
    {
        var body = new HeldBody();
        await using var fixture = new Fixture((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Metadata() : Bytes(body)));
        var editor = new TextBox(); fixture.Owner.Content = editor; editor.Focus();
        fixture.Session.Request(manual: false);
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();
        var window = Assert.IsType<UpdateProgressWindow>(fixture.Session.ProgressWindow);
        var model = Assert.IsType<UpdateProgressViewModel>(window.DataContext);
        await DrainUntilAsync(() => window.IsVisible);
        Assert.True(window.IsVisible); Assert.False(window.ShowActivated);
        Assert.True(fixture.Owner.IsEnabled);
        Assert.Same(editor, fixture.Owner.FocusManager!.GetFocusedElement());
        Assert.Contains("2.10.0", model.Message, StringComparison.Ordinal);
        Assert.True(model.IsBusy);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(window.FindControl<TextBlock>("MessageText")!));
        var completion = fixture.Session.Completion;
        fixture.Session.Request(manual: true);
        Assert.Same(completion, fixture.Session.Completion);
        Assert.Equal(2, fixture.Requests);
        model.CancelCommand.Execute(null);
        await fixture.Session.Completion;
        await fixture.Session.StopAsync();
        Assert.Empty(Directory.EnumerateFiles(fixture.Directory));
    }

    [AvaloniaFact]
    public async Task ManualCheckCoalescesPendingAutomaticMetadataAndExplicitlyReportsNoUpdate()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = new Fixture((_, token) => response.Task.WaitAsync(token));
        fixture.Session.Request(manual: false);
        var operation = fixture.Session.Completion;
        fixture.Session.Request(manual: true);
        Assert.Same(operation, fixture.Session.Completion); Assert.Equal(1, fixture.Requests);
        response.SetResult(Metadata("v2.0.2"));
        await operation;
        var model = Assert.IsType<UpdateProgressViewModel>(fixture.Session.ProgressWindow!.DataContext);
        Assert.False(model.IsBusy); Assert.False(model.CanRetry);
        Assert.Contains("najnowszą", model.Message, StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task RateLimitsProduceVisibleFailureWithOneAutomaticRequest(int status)
    {
        await using var fixture = new Fixture((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return Task.FromResult(response);
        });
        fixture.Session.Request(manual: false); await fixture.Session.Completion;
        var model = Assert.IsType<UpdateProgressViewModel>(fixture.Session.ProgressWindow!.DataContext);
        Assert.True(model.CanRetry); Assert.True(model.CanOpenPage); Assert.False(model.IsBusy);
        Assert.DoesNotContain("najnowszą", model.Message, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Requests);
        fixture.Session.Request(manual: true); await fixture.Session.Completion;
        Assert.Equal(1, fixture.Requests); // Server cooldown also applies to manual retries.
    }

    [AvaloniaFact]
    public async Task DownloadTimeoutWithoutUserCancellationIsVisibleAndRetryable()
    {
        await using var fixture = new Fixture((request, _) => request.RequestUri!.Host == "api.github.com"
            ? Task.FromResult(Metadata()) : Task.FromException<HttpResponseMessage>(new TaskCanceledException("Synthetic transport deadline.")));
        fixture.Session.Request(manual: false);
        await fixture.Session.Completion;
        var model = Assert.IsType<UpdateProgressViewModel>(fixture.Session.ProgressWindow!.DataContext);
        Assert.False(model.IsBusy); Assert.True(model.CanRetry); Assert.True(model.CanOpenPage);
        Assert.Contains("czas", model.Message, StringComparison.Ordinal);
        Assert.False(fixture.Session.Completion.IsFaulted);
    }

    [AvaloniaFact]
    public async Task CancelThenRetryIgnoresLateOldNetworkAndProgressWithoutOverwritingNewState()
    {
        var oldMetadata = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadataCalls = 0;
        await using var fixture = new Fixture((_, _) => ++metadataCalls == 1 ? oldMetadata.Task : Task.FromResult(Metadata("v2.0.2")));
        fixture.Session.Request(manual: true);
        var oldOperation = fixture.Session.Completion;
        var model = Assert.IsType<UpdateProgressViewModel>(fixture.Session.ProgressWindow!.DataContext);
        model.CancelCommand.Execute(null);
        model.RetryCommand.Execute(null);
        var newOperation = fixture.Session.Completion;
        Assert.NotSame(oldOperation, newOperation);
        await newOperation;
        var newMessage = model.Message;
        oldMetadata.SetResult(Metadata());
        await oldOperation;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(newMessage, model.Message); Assert.False(model.IsBusy);
        Assert.Equal(2, metadataCalls); Assert.Equal(2, fixture.Requests);
    }

    [AvaloniaFact]
    public async Task CanceledDownloadCannotPublishLateProgressIntoItsRetry()
    {
        var body = new HeldBody(ignoreCancellation: true);
        var metadataCalls = 0;
        await using var fixture = new Fixture((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com"
            ? (++metadataCalls == 1 ? Metadata() : Metadata("v2.0.2")) : Bytes(body)));
        fixture.Session.Request(manual: true);
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var model = Assert.IsType<UpdateProgressViewModel>(fixture.Session.ProgressWindow!.DataContext);
        model.CancelCommand.Execute(null); model.RetryCommand.Execute(null);
        await fixture.Session.Completion;
        var latestMessage = model.Message;
        body.Release.TrySetResult();
        await DrainUntilAsync(() => !Directory.EnumerateFiles(fixture.Directory, "*.part").Any());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(latestMessage, model.Message); Assert.False(model.IsBusy);
        Assert.Equal(3, fixture.Requests);
    }

    [AvaloniaTheory]
    [InlineData("en-US", "TelePilot update", "Cancel", "Retry")]
    [InlineData("pl-PL", "Aktualizacja TelePilot", "Anuluj", "Spróbuj ponownie")]
    public void ProgressWindowHasLocalizedAccessibleControlsAndCyclicKeyboardNavigation(string culture, string title, string cancel, string retry)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var window = new UpdateProgressWindow();
        try
        {
            var model = new UpdateProgressViewModel(() => { }, () => { }, () => { }, window.Close);
            window.DataContext = model; window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(title, window.Title);
            Assert.Equal(cancel, AutomationProperties.GetName(window.FindControl<Button>("CancelButton")!));
            model.Complete("Failure", retry: true, openPage: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(retry, AutomationProperties.GetName(window.FindControl<Button>("RetryButton")!));
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(Assert.IsType<Grid>(window.Content)));
            Assert.True(window.FindControl<Button>("RetryButton")!.Focus());
        }
        finally { window.Close(); CultureInfo.CurrentUICulture = previous; }
    }

    [AvaloniaFact]
    public async Task ClosingOwnerCancelsDownloadAndDeletesPartialBeforeShutdownCompletes()
    {
        var body = new HeldBody();
        await using var fixture = new Fixture((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Metadata() : Bytes(body)), new MainWindow());
        UpdateStartup.ForOwner(fixture.Owner, _ => fixture.Session);
        fixture.Session.Request(manual: false);
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(Directory.EnumerateFiles(fixture.Directory, "*.part"));
        fixture.Owner.Close();
        await DrainUntilAsync(() => !fixture.Owner.IsVisible);
        await fixture.Session.Completion;
        Assert.Empty(Directory.EnumerateFiles(fixture.Directory));
        Assert.Null(fixture.Session.ProgressWindow);
    }

    [AvaloniaFact]
    public async Task FocusedManualProgressTransitionsToExistingReadyConfirmationWithoutClickingMain()
    {
        var body = new HeldBody();
        await using var fixture = new Fixture((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Metadata() : Bytes(body)));
        fixture.Session.Request(manual: true);
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Session.ProgressWindow!.Activate();
        body.Release.TrySetResult();
        await DrainUntilAsync(() => fixture.Owner.OwnedWindows.OfType<ConfirmationDialog>().Any());
        var ready = fixture.Owner.OwnedWindows.OfType<ConfirmationDialog>().Single();
        var data = Assert.IsType<ConfirmationDialogViewModel>(ready.DataContext);
        Assert.Contains("2.10.0", data.Message, StringComparison.Ordinal);
        Assert.Equal("Otwórz instalator", data.ConfirmButtonText);
        ready.Close(false);
        await fixture.Session.Completion;
    }

    private static void SetActive(Window owner, bool active) =>
        typeof(WindowBase).GetProperty(nameof(WindowBase.IsActive))!.SetValue(owner, active);

    [AvaloniaFact]
    public async Task ManualCheckDuringReadyOwnerWaitDoesNotCreateAnotherBusyPanel()
    {
        var body = new HeldBody();
        await using var fixture = new Fixture((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Metadata() : Bytes(body)));
        SetActive(fixture.Owner, false);
        fixture.Session.Request(manual: false);
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        body.Release.TrySetResult();
        await DrainUntilAsync(() => fixture.Session.ProgressWindow is null && File.Exists(Path.Combine(fixture.Directory, Name)));
        fixture.Session.Request(manual: true);
        Assert.Null(fixture.Session.ProgressWindow);
        await DrainUntilAsync(() => fixture.Owner.OwnedWindows.OfType<ConfirmationDialog>().Any());
        fixture.Owner.OwnedWindows.OfType<ConfirmationDialog>().Single().Close(false);
        await fixture.Session.Completion;
    }

    [AvaloniaFact]
    public async Task SecondFailurePanelInSameOperationCanRevealAfterBackgroundVerificationFailure()
    {
        var body = new HeldBody();
        await using var fixture = new Fixture((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Metadata() : Bytes(body)));
        SetActive(fixture.Owner, false);
        fixture.Session.Request(manual: false);
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var firstPanel = fixture.Session.ProgressWindow;
        Assert.False(firstPanel!.IsVisible);
        body.Release.TrySetResult();
        await DrainUntilAsync(() => fixture.Session.ProgressWindow is null && File.Exists(Path.Combine(fixture.Directory, Name)));
        fixture.Owner.Activate();
        await DrainUntilAsync(() => fixture.Owner.OwnedWindows.OfType<ConfirmationDialog>().Any());
        var ready = fixture.Owner.OwnedWindows.OfType<ConfirmationDialog>().Single();
        SetActive(fixture.Owner, false);
        await File.WriteAllTextAsync(Path.Combine(fixture.Directory, Name), "modified after the verified download");
        ready.Close(true);
        await fixture.Session.Completion;
        var secondPanel = fixture.Session.ProgressWindow;
        Assert.NotNull(secondPanel); Assert.NotSame(firstPanel, secondPanel);
        Assert.True(Assert.IsType<UpdateProgressViewModel>(secondPanel.DataContext).CanRetry);
        fixture.Owner.Activate();
        await DrainUntilAsync(() => secondPanel.IsVisible);
    }

    [AvaloniaFact]
    public async Task AutomaticProgressDefersPresentationWhileAnotherDialogOwnsFocus()
    {
        var body = new HeldBody();
        await using var fixture = new Fixture((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Metadata() : Bytes(body)));
        var other = new Window(); var otherResult = other.ShowDialog(fixture.Owner);
        fixture.Session.Request(manual: false);
        await body.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(other.IsDialog);
        Assert.Contains(other, fixture.Owner.OwnedWindows);
        Assert.False(fixture.Session.ProgressWindow!.IsVisible);
        other.Close(); await otherResult; fixture.Owner.Activate();
        await DrainUntilAsync(() => fixture.Session.ProgressWindow!.IsVisible);
        Assert.False(fixture.Session.ProgressWindow!.ShowActivated);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;
        private readonly DirectoryInfo temporary = System.IO.Directory.CreateTempSubdirectory("telepilot-update-ui-");
        public Window Owner { get; }
        public UpdateStartupSession Session { get; }
        public int Requests { get; private set; }
        public string Directory => temporary.FullName;
        public Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, Window? owner = null)
        {
            this.send = send; Owner = owner ?? new Window(); Owner.Show(); Owner.Activate(); Dispatcher.UIThread.RunJobs();
            Session = new(Owner, new(2, 0, 2), Architecture.X64,
                () => new HttpClient(new Handler((request, token) => { Requests++; return this.send(request, token); })), Directory,
                (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        }
        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync(); Owner.Close(); Dispatcher.UIThread.RunJobs(); temporary.Delete(true);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class HeldBody(bool ignoreCancellation = false) : Stream
    {
        private int position;
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => Payload.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Waiting.TrySetResult();
            if (ignoreCancellation) await Release.Task;
            else await Release.Task.WaitAsync(cancellationToken);
            var count = Math.Min(buffer.Length, Payload.Length - position);
            Payload.AsMemory(position, count).CopyTo(buffer); position += count; return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
