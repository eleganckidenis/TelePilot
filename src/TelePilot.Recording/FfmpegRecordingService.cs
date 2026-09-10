using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TelePilot.Recording;

public sealed partial class FfmpegRecordingService : IAsyncDisposable
{
    private readonly IFfmpegArgumentBuilder _argumentBuilder;
    private readonly ILogger<FfmpegRecordingService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private TaskCompletionSource<RecordingProgress>? _completionSource;
    private DateTimeOffset _startedAt;
    private string? _outputPath;
    private bool _disposed;
    private volatile bool _isRecording;

    public FfmpegRecordingService(
        IFfmpegArgumentBuilder argumentBuilder,
        ILogger<FfmpegRecordingService> logger)
    {
        _argumentBuilder = argumentBuilder;
        _logger = logger;
    }

    public event EventHandler<RecordingProgress>? ProgressChanged;

    public bool IsRecording => _isRecording;

    public async Task<string> StartAsync(
        string ffmpegPath,
        RecordingRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is { HasExited: false })
            {
                throw new InvalidOperationException("Nagrywanie już trwa.");
            }

            if (_process is not null)
            {
                CompleteProcess(_process);
                DisposeProcess();
            }

            _completionSource = new TaskCompletionSource<RecordingProgress>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _startedAt = DateTimeOffset.UtcNow;
            _outputPath = null;
            try
            {
                Report(RecordingState.Preparing, "Przygotowywanie nagrywania.");
                Directory.CreateDirectory(request.OutputDirectory);
                var baseName = SafeFileNameGenerator.CreateBaseName(request);
                _outputPath = CreateUniquePath(request.OutputDirectory, baseName, SafeFileNameGenerator.GetExtension(request));
                var command = _argumentBuilder.Build(ffmpegPath, request, _outputPath);

                var startInfo = new ProcessStartInfo(command.ExecutablePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                foreach (var argument in command.Arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.Exited += OnProcessExited;
                try
                {
                    if (!process.Start())
                    {
                        throw new InvalidOperationException("Nie udało się uruchomić FFmpeg.");
                    }
                }
                catch
                {
                    process.Exited -= OnProcessExited;
                    process.Dispose();
                    throw;
                }

                _process = process;
                _ = DrainAsync(process.StandardOutput, CancellationToken.None);
                _ = DrainAsync(process.StandardError, CancellationToken.None);
                Report(RecordingState.Recording, "Rozpoczęto nagrywanie.");
                var outputFileName = Path.GetFileName(_outputPath);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    LogRecordingStarted(_logger, request.ChannelName, outputFileName);
                }
                return _outputPath;
            }
            catch
            {
                Report(RecordingState.Error, "Nie udało się rozpocząć nagrywania.");
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecordingProgress> WaitForCompletionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task<RecordingProgress> completion;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            completion = _completionSource?.Task
                ?? throw new InvalidOperationException("Nagrywanie nie zostało rozpoczęte.");
        }
        finally
        {
            _gate.Release();
        }

        return await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = _process;
            if (process is null)
            {
                return;
            }

            if (!process.HasExited)
            {
                Report(RecordingState.Stopping, "Zatrzymywanie nagrywania.");
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    try
                    {
                        await process.StandardInput.WriteLineAsync("q".AsMemory(), timeout.Token).ConfigureAwait(false);
                        await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // FFmpeg may close stdin while finishing or failing. Still
                        // reap the process and report its actual exit status.
                    }
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    LogForcedTermination(_logger);
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            CompleteProcess(process);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("FFmpeg zakończył nagrywanie błędem; plik może być niekompletny.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (_process is { HasExited: true })
        {
            // The terminal error has already been published and the process is
            // reaped. Disposing must not report the same failure a second time.
        }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                DisposeProcess();
            }
            finally
            {
                // An Exited callback can already be waiting for this gate.
                // WaitAsync does not allocate a native wait handle.
                _gate.Release();
            }
        }
    }

    private async void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not Process process)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            CompleteProcess(process);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void CompleteProcess(Process process)
    {
        if (!ReferenceEquals(_process, process) || _completionSource?.Task.IsCompleted != false)
        {
            return;
        }

        var state = process.ExitCode == 0 ? RecordingState.Completed : RecordingState.Error;
        Report(state, state == RecordingState.Completed
            ? "Nagrywanie zakończone." : "FFmpeg zakończył nagrywanie błędem.");
    }

    private void Report(RecordingState state, string message)
    {
        var elapsed = _startedAt == default ? TimeSpan.Zero : DateTimeOffset.UtcNow - _startedAt;
        var progress = new RecordingProgress(state, elapsed, _outputPath, message);
        _isRecording = state is RecordingState.Preparing or RecordingState.Recording or RecordingState.Stopping;
        if (state is RecordingState.Completed or RecordingState.Cancelled or RecordingState.Error)
        {
            _completionSource?.TrySetResult(progress);
        }

        ProgressChanged?.Invoke(this, progress);
    }

    private void DisposeProcess()
    {
        if (_process is null)
        {
            return;
        }

        _process.Exited -= OnProcessExited;
        _process.Dispose();
        _process = null;
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new char[4096];
            while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
            {
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string CreateUniquePath(string directory, string baseName, string extension)
    {
        var path = Path.Combine(directory, baseName + extension);
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
        }

        return path;
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Rozpoczęto nagrywanie kanału {ChannelName} do {OutputFileName}.")]
    private static partial void LogRecordingStarted(ILogger logger, string channelName, string outputFileName);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "FFmpeg nie zakończył się w limicie czasu; proces zostanie zakończony.")]
    private static partial void LogForcedTermination(ILogger logger);
}
