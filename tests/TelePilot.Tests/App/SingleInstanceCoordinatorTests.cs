using TelePilot.App.Services;

namespace TelePilot.Tests.App;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondCoordinator_ActivatesPrimary_WithoutBecomingPrimary()
    {
        using var directory = new TemporaryDirectory();
        string applicationId = CreateApplicationId();
        await using var primary = CreateCoordinator(applicationId, directory.Path);
        var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.SetActivationHandler(() => activation.TrySetResult());

        Assert.Equal(
            SingleInstanceStartResult.PrimaryInstance,
            await primary.StartAsync(TestContext.Current.CancellationToken));

        await using var secondary = CreateCoordinator(applicationId, directory.Path);
        Assert.Equal(
            SingleInstanceStartResult.ExistingInstanceActivated,
            await secondary.StartAsync(TestContext.Current.CancellationToken));
        Assert.False(secondary.IsPrimaryInstance);
        await activation.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ActivationBeforeHandlerRegistration_IsDeliveredOnce()
    {
        using var directory = new TemporaryDirectory();
        string applicationId = CreateApplicationId();
        await using var primary = CreateCoordinator(applicationId, directory.Path);
        Assert.Equal(
            SingleInstanceStartResult.PrimaryInstance,
            await primary.StartAsync(TestContext.Current.CancellationToken));

        await using var secondary = CreateCoordinator(applicationId, directory.Path);
        Assert.Equal(
            SingleInstanceStartResult.ExistingInstanceActivated,
            await secondary.StartAsync(TestContext.Current.CancellationToken));

        var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int activationCount = 0;
        primary.SetActivationHandler(() =>
        {
            Interlocked.Increment(ref activationCount);
            activation.TrySetResult();
        });

        await activation.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref activationCount));
    }

    [Fact]
    public async Task DisposedPrimary_LeavesRecoverableLockFile()
    {
        using var directory = new TemporaryDirectory();
        string applicationId = CreateApplicationId();
        var original = CreateCoordinator(applicationId, directory.Path);
        Assert.Equal(
            SingleInstanceStartResult.PrimaryInstance,
            await original.StartAsync(TestContext.Current.CancellationToken));
        await original.DisposeAsync();

        Assert.True(Directory.EnumerateFiles(directory.Path, "*.lock").Any());

        await using var replacement = CreateCoordinator(applicationId, directory.Path);
        Assert.Equal(
            SingleInstanceStartResult.PrimaryInstance,
            await replacement.StartAsync(TestContext.Current.CancellationToken));
        Assert.True(replacement.IsPrimaryInstance);
    }

    [Fact]
    public async Task LockedFileWithoutPipe_FailsClosedWithinBoundedTime()
    {
        using var directory = new TemporaryDirectory();
        string applicationId = CreateApplicationId();
        string lockFile = System.IO.Path.Combine(directory.Path, $"{applicationId}.lock");
        await using var orphanedOwner = new FileStream(
            lockFile,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read);
        await using var coordinator = CreateCoordinator(
            applicationId,
            directory.Path,
            TimeSpan.FromMilliseconds(150));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        SingleInstanceStartResult result = await coordinator.StartAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(SingleInstanceStartResult.ExistingInstanceUnavailable, result);
        Assert.False(coordinator.IsPrimaryInstance);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ThrowingActivationHandler_DoesNotStopFollowingRequests()
    {
        using var directory = new TemporaryDirectory();
        string applicationId = CreateApplicationId();
        await using var primary = CreateCoordinator(applicationId, directory.Path);
        int activationCount = 0;
        var secondActivation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.SetActivationHandler(() =>
        {
            int count = Interlocked.Increment(ref activationCount);
            if (count == 1)
            {
                throw new InvalidOperationException("Test callback failure.");
            }

            secondActivation.TrySetResult();
        });
        Assert.Equal(
            SingleInstanceStartResult.PrimaryInstance,
            await primary.StartAsync(TestContext.Current.CancellationToken));

        await using (var firstSecondary = CreateCoordinator(applicationId, directory.Path))
        {
            Assert.Equal(
                SingleInstanceStartResult.ExistingInstanceActivated,
                await firstSecondary.StartAsync(TestContext.Current.CancellationToken));
        }

        await WaitUntilAsync(
            () => Volatile.Read(ref activationCount) == 1,
            TestContext.Current.CancellationToken);

        await using (var secondSecondary = CreateCoordinator(applicationId, directory.Path))
        {
            Assert.Equal(
                SingleInstanceStartResult.ExistingInstanceActivated,
                await secondSecondary.StartAsync(TestContext.Current.CancellationToken));
        }

        await secondActivation.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, Volatile.Read(ref activationCount));
    }

    private static SingleInstanceCoordinator CreateCoordinator(
        string applicationId,
        string path,
        TimeSpan? timeout = null)
    {
        return new SingleInstanceCoordinator(
            applicationId,
            path,
            timeout ?? TimeSpan.FromSeconds(2));
    }

    private static string CreateApplicationId()
    {
        return $"TelePilot.Tests.{Guid.NewGuid():N}";
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"TelePilot.SingleInstance.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
