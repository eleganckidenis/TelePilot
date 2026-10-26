namespace TelePilot.Core.CatchUp;

public enum CatchUpSourceMode
{
    Automatic = 0,
    Disabled = 1,
    FlussonicHls = 2,
    FlussonicTs = 3,
    Shift = 4,
    XtreamCodes = 5,
}

/// <summary>Explicit archive defaults for one playlist source; zero days does not assume retention.</summary>
public sealed record CatchUpSourceSettings
{
    public CatchUpSourceMode Mode { get; init; } = CatchUpSourceMode.Automatic;

    public double RetentionDays { get; init; }

    public double CorrectionHours { get; init; }

    public bool OverridePlaylist { get; init; }
}
