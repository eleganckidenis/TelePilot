using TelePilot.Playback;

namespace TelePilot.Tests.Playback;

public sealed class NativePlaybackAttemptSignalsTests
{
    [Fact]
    public void ErrorCallbackSurvivesNativeEndedOverwriteAndWinsOverEndCallback()
    {
        var signals = new NativePlaybackAttemptSignals(); signals.BeginAttempt(1);
        Assert.True(signals.RecordError(1));
        Assert.Equal(7, signals.NormalizeState(6, 0));
        Assert.True(signals.RecordEnd(1));
        Assert.Equal(7, signals.NormalizeState(6, 1));
    }

    [Fact]
    public void CleanEndIsTerminalEvenWithoutPlayingOrAnEndedStatePoll()
    {
        var signals = new NativePlaybackAttemptSignals(); signals.BeginAttempt(1);
        Assert.True(signals.RecordEnd(1));
        Assert.Equal(6, signals.NormalizeState(5, 0));
    }

    [Fact]
    public void PreviousInputCallbacksCannotContaminateItsReplacementOrStoppedSession()
    {
        var signals = new NativePlaybackAttemptSignals(); signals.BeginAttempt(1);
        signals.RecordError(1);
        signals.BeginAttempt(2);
        Assert.False(signals.RecordError(1));
        Assert.False(signals.RecordEnd(1));
        Assert.Equal(3, signals.NormalizeState(3, 2));
        Assert.Equal(1, signals.NormalizeState(6, 3));
        Assert.True(signals.RecordEnd(2));
        Assert.Equal(6, signals.NormalizeState(6, 3.1));
    }

    [Fact]
    public void UnclassifiedEndedWaitsOneSecondForNativeFactsThenUsesFailedInputRecovery()
    {
        var signals = new NativePlaybackAttemptSignals(); signals.BeginAttempt(1);
        Assert.Equal(1, signals.NormalizeState(6, 10));
        Assert.Equal(1, signals.NormalizeState(6, 10.999));
        Assert.Equal(7, signals.NormalizeState(6, 11));
        signals.BeginAttempt(2);
        Assert.Equal(1, signals.NormalizeState(6, 20));
        Assert.Equal(3, signals.NormalizeState(3, 20.1));
        Assert.Equal(1, signals.NormalizeState(6, 21));
    }
}
