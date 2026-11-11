namespace TelePilot.Playback;

// VLC 3 can overwrite ERROR_S with Ended when INPUT_EVENT_DEAD is delivered.
// Preserve callback facts for this input instead of relying on its final state.
internal sealed class NativePlaybackAttemptSignals
{
    private readonly Lock gate = new();
    private long generation;
    private bool error, ended;
    private double? ambiguousEndStart;

    public void BeginAttempt(long attemptGeneration)
    {
        lock (gate)
        {
            generation = attemptGeneration;
            error = ended = false;
            ambiguousEndStart = null;
        }
    }

    public bool RecordError(long attemptGeneration)
    {
        lock (gate)
        {
            if (generation != attemptGeneration) return false;
            error = true;
            return true;
        }
    }

    public bool RecordEnd(long attemptGeneration)
    {
        lock (gate)
        {
            if (generation != attemptGeneration) return false;
            ended = true;
            return true;
        }
    }

    public int NormalizeState(int nativeState, double now)
    {
        lock (gate)
        {
            if (error) return 7;
            if (ended) return 6;
            if (nativeState != 6)
            {
                ambiguousEndStart = null;
                return nativeState;
            }
            // Native state changes can precede their callbacks. Give the clean
            // EOF/error event one second to arrive; an unclassified dead input
            // then consumes the ordinary failed-input retry budget.
            if (!double.IsFinite(now)) return 1;
            ambiguousEndStart ??= now;
            return now - ambiguousEndStart >= 1 ? 7 : 1;
        }
    }
}
