namespace TelePilot.Core.Models;

// Network cache policy adapted from Accessible-IPTV-Client (MIT), v1.150.58.
public sealed record PlaybackBufferSettings(double Seconds = 25, double MaximumSeconds = 30, double VariantMaxMbps = 0)
{
    public PlaybackBufferSettings Normalize()
    {
        var maximum = double.IsFinite(MaximumSeconds) && MaximumSeconds > 0 ? MaximumSeconds : 30;
        maximum = Math.Min(maximum == 18 ? 30 : maximum, 300);
        return new(double.IsFinite(Seconds) && Seconds > 0 ? Math.Min(Seconds, maximum) : Math.Min(25, maximum),
            maximum, double.IsFinite(VariantMaxMbps) && VariantMaxMbps > 0 ? Math.Clamp(VariantMaxMbps, .25, 500) : 0);
    }

    public string[] MediaOptions(double target, bool live, bool quickStart)
    {
        var settings = Normalize();
        var seconds = Math.Min(settings.MaximumSeconds, Math.Max(.1, target));
        var milliseconds = (int)((live && quickStart ? Math.Min(seconds, .5) : seconds) * 1000);
        return [":http-reconnect=true", ":rtsp-tcp", ":adaptive-use-access=1", ":input-repeat=0",
            $":network-caching={milliseconds}", $":live-caching={milliseconds}", $":file-caching={milliseconds}", $":disc-caching={milliseconds}",
            ..live ? new[] { ":clock-jitter=1500" } : new[] { ":clock-jitter=0", ":clock-synchro=0", ":drop-late-frames=0", ":skip-frames=0" }];
    }
}
