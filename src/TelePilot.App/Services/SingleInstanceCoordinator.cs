using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TelePilot.App.Services;

public enum SingleInstanceStartResult
{
    PrimaryInstance,
    ExistingInstanceActivated,
    ExistingInstanceUnavailable,
}

/// <summary>
/// Coordinates one application instance per user. The operating system owns both
/// the file lock and the named-pipe handles, so a crashed process cannot leave a
/// permanent lock behind.
/// </summary>
public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    public const string TelePilotApplicationId = "TelePilot.873edad3-c012-470d-a2e9-f3b66d236796";

    private static readonly byte[] ActivationRequest = [0x54, 0x50, 0x01, 0x01];
    private static readonly byte[] ActivationResponsePrefix = [0x54, 0x50, 0x01, 0x02];
    private const byte ActivationAcknowledgement = 0x06;
    private static readonly byte[] ActivationAcknowledgementMessage = [ActivationAcknowledgement];

    private readonly object stateGate = new();
    private readonly object serverGate = new();
    private readonly string lockFilePath;
    private readonly string pipeName;
    private readonly TimeSpan activationTimeout;
    private readonly CancellationTokenSource shutdown = new();
    private FileStream? primaryLock;
    private NamedPipeServerStream? activeServer;
    private Task? serverTask;
    private Action? activationHandler;
    private bool activationPending;
    private bool started;
    private bool disposed;

    public SingleInstanceCoordinator(
        string applicationId,
        string lockDirectory,
        TimeSpan? activationTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockDirectory);

        if (applicationId.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException(
                "Identyfikator aplikacji może zawierać wyłącznie litery ASCII, cyfry, kropki, łączniki i podkreślenia.",
                nameof(applicationId));
        }

        this.activationTimeout = activationTimeout ?? TimeSpan.FromMilliseconds(1500);
        if (this.activationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(activationTimeout));
        }

        string normalizedDirectory = Path.GetFullPath(lockDirectory);
        lockFilePath = Path.Combine(normalizedDirectory, $"{applicationId}.lock");

        // Named pipes are machine-wide on Windows. Including a digest of the
        // per-user data directory keeps separate signed-in users independent.
        string pipeScope = OperatingSystem.IsWindows()
            ? normalizedDirectory.ToUpperInvariant()
            : normalizedDirectory;
        byte[] pathDigest = SHA256.HashData(Encoding.UTF8.GetBytes(pipeScope));
        pipeName = $"{applicationId}.{Convert.ToHexString(pathDigest.AsSpan(0, 8))}.activation";
    }

    public bool IsPrimaryInstance { get; private set; }

    public async Task<SingleInstanceStartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
            {
                throw new InvalidOperationException("Koordynator pojedynczej instancji został już uruchomiony.");
            }

            started = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (TryAcquirePrimaryLock())
        {
            StartActivationServer();
            return SingleInstanceStartResult.PrimaryInstance;
        }

        if (await TrySendActivationAsync(cancellationToken).ConfigureAwait(false))
        {
            return SingleInstanceStartResult.ExistingInstanceActivated;
        }

        // The owner may have exited between our first lock attempt and the IPC
        // attempt. Retrying the lock recovers immediately without deleting or
        // trusting the contents of a stale lock file.
        if (TryAcquirePrimaryLock())
        {
            StartActivationServer();
            return SingleInstanceStartResult.PrimaryInstance;
        }

        // Fail closed: when a process still owns the lock but cannot answer,
        // starting another GUI would violate the single-instance guarantee.
        return SingleInstanceStartResult.ExistingInstanceUnavailable;
    }

    /// <summary>
    /// Registers the action that restores and activates the main window. If a
    /// request arrived during application startup, it is delivered once here.
    /// </summary>
    public void SetActivationHandler(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        bool dispatchPending;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activationHandler = handler;
            dispatchPending = activationPending;
            activationPending = false;
        }

        if (dispatchPending)
        {
            DispatchActivation(handler);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? taskToAwait;
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            activationHandler = null;
            activationPending = false;
            shutdown.Cancel();
            taskToAwait = serverTask;
        }

        lock (serverGate)
        {
            activeServer?.Dispose();
            activeServer = null;
        }

        primaryLock?.Dispose();
        primaryLock = null;
        IsPrimaryInstance = false;

        if (taskToAwait is not null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (shutdown.IsCancellationRequested)
            {
            }
        }

        shutdown.Dispose();
    }

    private bool TryAcquirePrimaryLock()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
            var candidate = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 64,
                FileOptions.None);

            try
            {
                candidate.SetLength(0);
                byte[] ownerDescription = Encoding.UTF8.GetBytes(
                    $"pid={Environment.ProcessId};started={DateTimeOffset.UtcNow:O}");
                candidate.Write(ownerDescription);
                candidate.Flush();
                primaryLock = candidate;
                IsPrimaryInstance = true;
                return true;
            }
            catch
            {
                candidate.Dispose();
                throw;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void StartActivationServer()
    {
        serverTask = RunActivationServerAsync(shutdown.Token);
    }

    private async Task RunActivationServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                lock (serverGate)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    activeServer = server;
                }

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleActivationRequestAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await DelayAfterServerFailureAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                await DelayAfterServerFailureAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (serverGate)
                {
                    activeServer = null;
                }
            }
        }
    }

    private async Task HandleActivationRequestAsync(
        NamedPipeServerStream server,
        CancellationToken cancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(1));

        byte[] request = new byte[ActivationRequest.Length];
        await server.ReadExactlyAsync(request, requestTimeout.Token).ConfigureAwait(false);
        if (!request.AsSpan().SequenceEqual(ActivationRequest))
        {
            return;
        }

        byte[] response = new byte[ActivationResponsePrefix.Length + sizeof(int)];
        ActivationResponsePrefix.CopyTo(response, 0);
        BinaryPrimitives.WriteInt32LittleEndian(
            response.AsSpan(ActivationResponsePrefix.Length),
            Environment.ProcessId);
        await server.WriteAsync(response, requestTimeout.Token).ConfigureAwait(false);
        await server.FlushAsync(requestTimeout.Token).ConfigureAwait(false);

        // The secondary instance first grants this process foreground rights on
        // Windows and then acknowledges. Even if that acknowledgement is lost,
        // the user's explicit second launch is still treated as activation.
        byte[] acknowledgement = new byte[1];
        try
        {
            await server.ReadExactlyAsync(acknowledgement, requestTimeout.Token).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        RequestActivation();
    }

    private async Task<bool> TrySendActivationAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < activationTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remaining = activationTimeout - stopwatch.Elapsed;
            TimeSpan attemptTimeout = TimeSpan.FromMilliseconds(
                Math.Min(250, Math.Max(1, remaining.TotalMilliseconds)));

            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(attemptTimeout);

                await client.ConnectAsync(attempt.Token).ConfigureAwait(false);
                await client.WriteAsync(ActivationRequest, attempt.Token).ConfigureAwait(false);
                await client.FlushAsync(attempt.Token).ConfigureAwait(false);

                byte[] response = new byte[ActivationResponsePrefix.Length + sizeof(int)];
                await client.ReadExactlyAsync(response, attempt.Token).ConfigureAwait(false);
                if (!response.AsSpan(0, ActivationResponsePrefix.Length)
                    .SequenceEqual(ActivationResponsePrefix))
                {
                    return false;
                }

                int primaryProcessId = BinaryPrimitives.ReadInt32LittleEndian(
                    response.AsSpan(ActivationResponsePrefix.Length));
                WindowsForegroundPermission.TryGrantTo(primaryProcessId);
                await client.WriteAsync(ActivationAcknowledgementMessage, attempt.Token).ConfigureAwait(false);
                await client.FlushAsync(attempt.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (IsExpectedClientFailure(exception, cancellationToken))
            {
                TimeSpan delay = activationTimeout - stopwatch.Elapsed;
                if (delay <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(
                        TimeSpan.FromMilliseconds(Math.Min(50, delay.TotalMilliseconds)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return false;
    }

    private void RequestActivation()
    {
        Action? handler;
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            handler = activationHandler;
            if (handler is null)
            {
                activationPending = true;
                return;
            }
        }

        DispatchActivation(handler);
    }

    private static void DispatchActivation(Action handler)
    {
        _ = Task.Run(() =>
        {
            try
            {
                handler();
            }
            catch
            {
                // An activation callback must never terminate the IPC listener.
            }
        });
    }

    private static bool IsExpectedClientFailure(
        Exception exception,
        CancellationToken callerCancellationToken)
    {
        return exception is TimeoutException
            or IOException
            || exception is OperationCanceledException && !callerCancellationToken.IsCancellationRequested;
    }

    private static async Task DelayAfterServerFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

internal static class WindowsForegroundPermission
{
    public static void TryGrantTo(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            return;
        }

        try
        {
            _ = AllowSetForegroundWindow((uint)processId);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    // DllImport is used deliberately here: LibraryImport emits unsafe source,
    // while this tiny best-effort call does not justify enabling unsafe code for
    // the entire desktop application.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
#pragma warning restore SYSLIB1054
}
