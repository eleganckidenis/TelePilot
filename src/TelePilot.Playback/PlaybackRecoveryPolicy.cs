using TelePilot.Core.Models;

namespace TelePilot.Playback;

internal readonly record struct PlaybackProgress(long Video, long Displayed, long Audio, long Played);
internal readonly record struct RecoverySample(int State, double Position = -1, double Length = -1, PlaybackProgress? Progress = null, bool AudioOnly = false);
internal enum RecoveryKind { None, Retry, Lost, Finished }
internal readonly record struct RecoveryAction(RecoveryKind Kind, double Delay = 0, bool QuickStart = false);

internal sealed class PlaybackRecoveryPolicy
{
    private static readonly string[] ArchivePaths = ["/timeshift/", "/movie/", "/series/", "/archive/", "catchup", "recording"];
    private static readonly string[] TsPaths = ["/auth/", "/stream/", "/play/"];
    public PlaybackBufferSettings Settings { get; }
    public double Target { get; private set; }
    public int Attempts { get; private set; }
    public bool Pending { get; private set; }
    public bool GaveUp { get; private set; }
    private readonly bool live, dispatcharr, linearTs;
    private readonly int maximumAttempts;
    private readonly double requestStart;
    private double attemptStart;
    private bool everPlayed, attemptPlayed;
    private double? lastRestart, bufferingStart, lastAdjustment, lastVideo, lastAudio, lastPosition, clockStallStart;
    private int refreshes, videoSamples, audioSamples;
    private PlaybackProgress? previousProgress;
    private readonly List<double> bufferEvents = [];

    public PlaybackRecoveryPolicy(PlaybackOptions options, Uri url, bool dispatcharr, double now)
    {
        Settings = options.Buffering?.Normalize() ?? new PlaybackBufferSettings(options.NetworkCachingMilliseconds / 1000d).Normalize();
        Target = Settings.Seconds; live = options.IsLive; this.dispatcharr = dispatcharr; linearTs = IsLinearTs(url);
        maximumAttempts = options.MaximumReconnectAttempts; requestStart = attemptStart = now;
    }
    public void BeginAttempt(double now)
    {
        Pending = false; attemptStart = now; attemptPlayed = false; bufferingStart = null; bufferEvents.Clear(); ResetProgress();
    }
    private void ResetProgress()
    {
        previousProgress = null; videoSamples = audioSamples = 0; lastVideo = lastAudio = lastPosition = clockStallStart = null;
    }
    public static double ReconnectDelay(int attempt) => attempt <= 1 ? .4 : Math.Min(Math.Pow(2, attempt - 1), 8);
    private RecoveryAction Retry(double now, bool increase)
    {
        if (Pending || GaveUp) return default;
        if (lastRestart is { } previous)
        {
            if (now - previous >= 120) Attempts = 0;
            if (now - previous < 2) return default;
        }
        if ((!everPlayed && now - requestStart >= 30) || Attempts >= maximumAttempts)
        { GaveUp = true; return new(RecoveryKind.Lost); }
        Attempts++; lastRestart = now; Pending = true;
        if (increase && !(live && dispatcharr)) Target = Math.Min(Target + 1, Settings.MaximumSeconds);
        return new(RecoveryKind.Retry, ReconnectDelay(Attempts));
    }
    public RecoveryAction Evaluate(RecoverySample sample, double now, bool paused = false)
    {
        if (GaveUp) return default;
        if (!Pending && sample.State is 3 or 4) { everPlayed = attemptPlayed = true; refreshes = 0; }
        if (!everPlayed && now - requestStart >= 30) { GaveUp = true; Pending = false; return new(RecoveryKind.Lost); }
        if (Pending) return default;
        if (sample.State is 5 or 6 or 7)
        {
            if (live && linearTs && attemptPlayed)
            {
                if (refreshes >= 6) { GaveUp = true; return new(RecoveryKind.Lost); }
                refreshes++; Pending = true; lastRestart = now; return new(RecoveryKind.Retry, .2, true);
            }
            if (sample.State == 6 && sample.Length > 0 && sample.Position >= 0 && sample.Length - sample.Position <= 10)
                return new(RecoveryKind.Finished);
            return Retry(now, sample.State == 7);
        }
        if (sample.State == 4 || paused && sample.State == 3) { bufferingStart = null; ResetProgress(); return default; }
        if (sample.State == 2)
        {
            ResetProgress(); bufferingStart ??= now;
            if (attemptPlayed && now - bufferingStart >= 10) return Retry(now, true);
        }
        else if (bufferingStart is { } start)
        {
            bufferingStart = null;
            if (now - start >= 1.25)
            {
                bufferEvents.Add(now); bufferEvents.RemoveAll(t => now - t > 60);
                if (attemptPlayed && bufferEvents.Count >= 4 && (lastAdjustment is null || now - lastAdjustment >= 20))
                {
                    lastAdjustment = now; var action = Retry(now, true); if (action.Kind != RecoveryKind.None) return action;
                }
            }
        }
        if (!attemptPlayed && sample.State is >= 0 and <= 2 && now - attemptStart >= 25) return Retry(now, false);
        if (sample.State != 3) { ResetProgress(); return default; }
        var armed = false;
        if (live && sample.Progress is { } current)
        {
            if (previousProgress is { } previous)
            {
                if (current.Video != previous.Video || current.Displayed != previous.Displayed) { videoSamples++; lastVideo = now; }
                if (current.Audio != previous.Audio || current.Played != previous.Played) { audioSamples++; lastAudio = now; }
            }
            else { lastVideo = lastAudio = now; }
            previousProgress = current;
            var videoArmed = !sample.AudioOnly && videoSamples >= 2; var audioArmed = audioSamples >= 2;
            armed = videoArmed || audioArmed;
            if (now - attemptStart >= 12 + Target && ((videoArmed && now - lastVideo >= 10) || (audioArmed && now - lastAudio >= 10))) return Retry(now, true);
        }
        else { previousProgress = null; videoSamples = audioSamples = 0; lastVideo = lastAudio = null; }
        if (armed) { lastPosition = sample.Position; clockStallStart = null; return default; }
        if (now - attemptStart < 12 + Target || sample.Position < 0) { lastPosition = clockStallStart = null; return default; }
        if (lastPosition is { } position && sample.Position >= position - 1 && sample.Position <= position + .2)
        {
            clockStallStart ??= now;
            if (now - clockStallStart >= 10) return Retry(now, true);
        }
        else clockStallStart = null;
        lastPosition = sample.Position; return default;
    }
    internal static bool IsLinearTs(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (ArchivePaths.Any(path.Contains)) return false;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var numeric = parts.Length > 0 && long.TryParse(parts[^1].Split('.')[0], out _);
        return parts.Length >= 2 && numeric && !path.EndsWith(".m3u8", StringComparison.Ordinal)
            || parts.Length >= 4 && path.EndsWith(".ts", StringComparison.Ordinal) && long.TryParse(parts[^2], out _)
            || TsPaths.Any(path.Contains);
    }
}
