namespace TelePilot.Epg.Models;

public sealed record EpgIcon
{
    public EpgIcon(string source, int? width = null, int? height = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (width is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Icon width must be positive.");
        }

        if (height is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Icon height must be positive.");
        }

        Source = source.Trim();
        Width = width;
        Height = height;
    }

    public string Source { get; }

    public int? Width { get; }

    public int? Height { get; }
}
