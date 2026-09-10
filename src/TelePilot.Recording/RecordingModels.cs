namespace TelePilot.Recording;

public enum RecordingMode
{
    StreamCopy,
    AudioOnly,
}

public enum StreamContainer
{
    Matroska,
    MpegTransportStream,
}

public enum AudioOutputFormat
{
    Mp3,
    M4aAac,
    Opus,
    Flac,
    Wav,
}

public enum AudioTrackSelection
{
    Current,
    All,
    Specific,
}

public enum RecordingState
{
    Idle,
    Preparing,
    Recording,
    Stopping,
    Completed,
    Cancelled,
    Error,
}

public sealed record RecordingRequest(
    Uri InputUri,
    string ChannelName,
    string? ProgrammeTitle,
    DateTimeOffset StartedAt,
    string OutputDirectory,
    RecordingMode Mode,
    StreamContainer Container = StreamContainer.Matroska,
    AudioOutputFormat AudioFormat = AudioOutputFormat.Mp3,
    AudioTrackSelection TrackSelection = AudioTrackSelection.Current,
    int? AudioTrackIndex = null,
    int LossyBitrateKbps = 192,
    string FileNameTemplate = SafeFileNameGenerator.DefaultTemplate);

public sealed record RecordingProgress(
    RecordingState State,
    TimeSpan Elapsed,
    string? OutputPath,
    string Message);

public sealed record FfmpegCommand(string ExecutablePath, IReadOnlyList<string> Arguments);

public sealed record FfmpegInstallation(string ExecutablePath, string VersionDescription);
