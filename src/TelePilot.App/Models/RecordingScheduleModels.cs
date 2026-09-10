using TelePilot.Recording;

namespace TelePilot.App.Models;

public enum ScheduledRecordingStatus
{
    Scheduled,
    Recording,
    Completed,
    Cancelled,
    Failed,
}

public sealed record RecordingScheduleRequest(
    string ChannelId,
    string ChannelName,
    DateTimeOffset Start,
    DateTimeOffset End,
    RecordingMode Mode,
    string? ProgrammeTitle);

public sealed record ScheduledRecordingEntry(
    Guid Id,
    Guid PlaylistSourceId,
    Guid ChannelId,
    string ChannelName,
    string? ProgrammeTitle,
    DateTimeOffset Start,
    DateTimeOffset End,
    RecordingMode Mode,
    string ProtectedStreamUrl,
    string OutputDirectory,
    AudioOutputFormat AudioFormat,
    int AudioBitrateKbps,
    string FileNameTemplate,
    ScheduledRecordingStatus Status = ScheduledRecordingStatus.Scheduled,
    string? LastError = null);

public sealed class ScheduledRecordingSettings
{
    public List<ScheduledRecordingEntry> Recordings { get; init; } = [];
}
