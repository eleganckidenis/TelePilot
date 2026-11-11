using TelePilot.Core.Models;

namespace TelePilot.Playback;

internal readonly record struct PlaybackProgress(long Video, long Displayed, long Audio, long Played, long? InputBytes = null);
internal readonly record struct RecoverySample(int State, double Position = -1, double Length = -1,
    PlaybackProgress? Progress = null, bool AudioOnly = false, bool AudioEnabled = true, bool VideoEnabled = true, int? KnownVideoTrackCount = null);
internal enum RecoveryKind { None, Retry, Lost, Finished }
internal readonly record struct RecoveryAction(RecoveryKind Kind, double Delay = 0, bool QuickStart = false);

internal sealed class PlaybackRecoveryPolicy
{
    public PlaybackBufferSettings Settings { get; }
    public double Target { get; private set; } = 2;
    public double Applied { get; private set; } = 2;
    public int Attempts { get; private set; }
    public bool Pending { get; private set; }
    public bool GaveUp { get; private set; }
    private readonly int maximumAttempts;
    private double attemptStart, lastProgress, graceUntil, risk;
    private double? lastObservation, bufferingStart, lastAdjustment, healthyStart, decayStart, clockAnchor, lastClock, inputLastAdvance;
    private long? inputPrevious;
    private bool inputProven, attemptPlayed, wasPaused;
    private bool videoWasVisible = true;
    private long? pendingDecodePrevious;
    private int pendingDecodeIncrements;
    private double? pendingPresentationStart;
    private readonly OutputWatch video = new(), audio = new();
    private readonly List<double> bufferEvents = [];

    public PlaybackRecoveryPolicy(PlaybackOptions options, Uri url, bool dispatcharr, double now)
    {
        Settings = options.Buffering?.Normalize() ?? new();
        maximumAttempts = options.MaximumReconnectAttempts;
        attemptStart = lastProgress = now;
        ResetObservation(now);
    }

    public void BeginAttempt(double now, double? applied = null)
    {
        Applied = Math.Clamp(applied ?? Target, 1, 30);
        Pending = false;
        attemptStart = now;
        attemptPlayed = false;
        bufferingStart = null;
        ResetObservation(now);
    }

    public void ResetObservation(double now)
    {
        ResetTelemetry(now);
        attemptStart = now;
        bufferingStart = null;
        bufferEvents.Clear();
        attemptPlayed = false;
    }

    private void ResetTelemetry(double now)
    {
        video.Reset(); audio.Reset();
        inputPrevious = null; inputLastAdvance = null; inputProven = false;
        clockAnchor = lastClock = null;
        healthyStart = decayStart = null;
        graceUntil = now + 3;
        clockLastAdvance = null;
        ResetPendingPresentation();
    }

    public static double ReconnectDelay(int attempt) => attempt <= 1 ? .4 : Math.Min(Math.Pow(2, attempt - 1), 8);

    private RecoveryAction Terminal(RecoveryKind kind)
    {
        GaveUp = true; Pending = false;
        return new(kind);
    }

    private void Learn(double duration)
    {
        risk = .6 * risk + .4 * Math.Min(duration, 20);
        Target = Math.Clamp(Math.Max(Target * 1.5, Math.Max(Applied + 2, risk * 2 + 1)), 1, 30);
    }

    private RecoveryAction Retry(double now, double? starvation = null)
    {
        if (Pending || GaveUp) return default;
        if (Attempts >= maximumAttempts) return Terminal(RecoveryKind.Lost);
        if (starvation is { } gap) Learn(gap);
        Attempts++; Pending = true; healthyStart = decayStart = null;
        return new(RecoveryKind.Retry, ReconnectDelay(Attempts));
    }

    public RecoveryAction Evaluate(RecoverySample sample, double now, bool paused = false)
    {
        if (GaveUp) return default;
        // EOF is an explicit terminal event, including while a retry is pending.
        if (sample.State == 6) return Terminal(RecoveryKind.Finished);
        if (!double.IsFinite(now) || lastObservation is { } observed && now <= observed) return default;
        var discontinuity = lastObservation is { } previous && now - previous > 5;
        lastObservation = now;
        if (discontinuity)
        {
            ResetTelemetry(now);
            bufferingStart = null;
        }
        var isPaused = sample.State == 4 || paused && sample.State == 3;
        if (isPaused)
        {
            if (!wasPaused) ResetObservation(now);
            wasPaused = true;
            lastProgress = now;
            bufferingStart = null;
            return default;
        }
        if (wasPaused)
        {
            wasPaused = false;
            lastProgress = now;
            ResetObservation(now);
        }
        if (now - lastProgress >= 90) return Terminal(RecoveryKind.Lost);
        if (Pending) return default;
        if (sample.State is 5 or 7) return Retry(now);

        if (sample.State == 2)
        {
            ResetPendingPresentation();
            healthyStart = decayStart = null;
            if (attemptPlayed)
            {
                bufferingStart ??= now;
                if (now - bufferingStart >= Math.Max(8, Applied + 4)) return Retry(now, now - bufferingStart);
            }
        }
        else if (bufferingStart is { } start)
        {
            bufferingStart = null;
            ResetTelemetry(now);
            var duration = now - start;
            if (duration >= .75)
            {
                Learn(duration);
                bufferEvents.RemoveAll(time => now - time > 30);
                if (bufferEvents.Count == 16) bufferEvents.RemoveAt(0);
                bufferEvents.Add(now);
                if (bufferEvents.Count >= 3 && (lastAdjustment is null || now - lastAdjustment >= 20))
                {
                    lastAdjustment = now;
                    return Retry(now);
                }
            }
        }
        if (sample.State != 3)
        {
            healthyStart = decayStart = null;
            if (!attemptPlayed && now - attemptStart >= Math.Max(8, Applied + 6)) return Retry(now);
            return default;
        }

        if (sample.VideoEnabled != videoWasVisible)
        {
            video.Reset(); healthyStart = decayStart = null; graceUntil = now + 3;
            videoWasVisible = sample.VideoEnabled;
            ResetPendingPresentation();
        }
        var counterReset = false;
        var outputProgress = false;
        if (sample.AudioOnly || !sample.VideoEnabled) video.Reset();
        if (!sample.AudioEnabled) audio.Reset();
        if (sample.Progress is { } stats)
        {
            if (!sample.AudioOnly && sample.VideoEnabled) outputProgress |= video.Observe(stats.Displayed, now, out counterReset);
            if (sample.AudioEnabled)
            {
                outputProgress |= audio.Observe(stats.Played, now, out var audioReset);
                counterReset |= audioReset;
            }
            if (stats.InputBytes is >= 0 and var bytes)
            {
                if (inputPrevious is { } old)
                {
                    if (bytes > old) { inputProven = true; inputLastAdvance = now; }
                    else if (bytes < old) { inputProven = false; inputLastAdvance = null; counterReset = true; }
                }
                inputPrevious = bytes;
            }
            else { inputPrevious = null; inputProven = false; inputLastAdvance = null; }
        }
        else { video.Reset(); audio.Reset(); inputPrevious = null; inputProven = false; inputLastAdvance = null; }

        if (counterReset)
        {
            ResetPendingPresentation();
            healthyStart = decayStart = null;
            graceUntil = now + 3;
        }
        if (!counterReset && sample.Progress is { Displayed: 0, Video: >= 0 } decoding
            && !sample.AudioOnly && sample.VideoEnabled && sample.KnownVideoTrackCount is > 0)
        {
            if (pendingDecodePrevious is { } decoded)
            {
                if (decoding.Video > decoded)
                {
                    pendingDecodeIncrements++;
                    if (pendingDecodeIncrements >= 2) pendingPresentationStart ??= now;
                }
                else if (decoding.Video < decoded) ResetPendingPresentation();
            }
            pendingDecodePrevious = decoding.Video;
        }
        else ResetPendingPresentation();
        if (pendingPresentationStart is { } presentationStart)
        {
            healthyStart = decayStart = null;
            if (now >= graceUntil && now - presentationStart >= Math.Max(30, Applied + 6)) return Retry(now);
        }
        var clockProgress = false;
        if (double.IsFinite(sample.Position) && sample.Position >= 0)
        {
            if (lastClock is { } clock && sample.Position < clock - 1)
            {
                ResetTelemetry(now);
                clockAnchor = lastClock = sample.Position;
                return default;
            }
            clockAnchor ??= sample.Position;
            if (sample.Position - clockAnchor >= .25) { clockProgress = true; clockAnchor = sample.Position; }
            lastClock = sample.Position;
        }
        else clockAnchor = lastClock = null;

        var armed = video.Armed || audio.Armed;
        var progress = !counterReset && (outputProgress || !armed && clockProgress);
        var frozenSince = video.Armed && now - video.LastAdvance >= 8 ? video.LastAdvance
            : audio.Armed && now - audio.LastAdvance >= 8 ? audio.LastAdvance : (double?)null;
        if (!armed && pendingPresentationStart is null && clockAnchor is not null && !progress)
        {
            // Clock observations share the same cumulative threshold as output health.
            clockLastAdvance ??= now;
            if (now - clockLastAdvance >= 8) frozenSince = clockLastAdvance;
        }
        else if (progress) clockLastAdvance = now;
        if (now >= graceUntil && frozenSince is { } frozen)
        {
            var networkStarved = inputProven && inputLastAdvance is { } input && now - input >= 3;
            return Retry(now, networkStarved ? now - frozen : null);
        }
        if (now - lastProgress > 3) healthyStart = decayStart = null;
        if (progress)
        {
            attemptPlayed = true;
            lastProgress = now;
            if (!counterReset && pendingPresentationStart is null && now >= graceUntil)
            {
                healthyStart ??= now;
                decayStart ??= now;
                if (now - healthyStart >= 120) Attempts = 0;
                if (now - decayStart >= 60)
                {
                    Target = Math.Max(1, Target * .85); risk *= .8; decayStart = now;
                }
            }
        }
        if (!attemptPlayed && pendingPresentationStart is null
            && now - attemptStart >= Math.Max(8, Applied + 6)) return Retry(now);
        return default;
    }

    private void ResetPendingPresentation()
    {
        pendingDecodePrevious = null;
        pendingDecodeIncrements = 0;
        pendingPresentationStart = null;
    }

    private double? clockLastAdvance;

    private sealed class OutputWatch
    {
        private long? previous;
        private int increments;
        private bool unavailable;
        public bool Armed => increments >= 2;
        public double LastAdvance { get; private set; }
        public void Reset() { previous = null; increments = 0; LastAdvance = 0; unavailable = false; }
        public bool Observe(long value, double now, out bool reset)
        {
            reset = false;
            if (value < 0)
            {
                // Missing output statistics are a transition, not a new
                // discontinuity on every poll of the healthy other stream.
                reset = !unavailable;
                Reset(); unavailable = true;
                return false;
            }
            unavailable = false;
            if (previous is { } old && value < old)
            {
                Reset(); previous = value; reset = true; return false;
            }
            var positive = previous is { } former && value > former;
            previous = value;
            if (positive) { increments++; LastAdvance = now; }
            return positive;
        }
    }
}
