namespace TelePilot.Core.Models;

// Only the optional HLS quality limit is persisted; cache duration is automatic.
public sealed record PlaybackBufferSettings(double VariantMaxMbps = 0)
{
    public PlaybackBufferSettings Normalize()
    {
        return new(double.IsFinite(VariantMaxMbps) && VariantMaxMbps > 0 ? Math.Clamp(VariantMaxMbps, .25, 500) : 0);
    }

    public static string[] MediaOptions(double target, bool live, bool quickStart)
    {
        var seconds = Math.Clamp(double.IsFinite(target) ? target : 2, 1, 30);
        var milliseconds = (int)(seconds * 1000);
        return [":http-reconnect=true", ":rtsp-tcp", ":adaptive-use-access=1", ":input-repeat=0",
            $":network-caching={milliseconds}", $":live-caching={milliseconds}", $":file-caching={milliseconds}", $":disc-caching={milliseconds}",
            ..live ? new[] { ":clock-jitter=1500" } : new[] { ":clock-jitter=0", ":clock-synchro=0", ":drop-late-frames=0", ":skip-frames=0" }];
    }
}
