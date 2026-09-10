using System.Security.Cryptography;
using System.Text;

namespace TelePilot.Core.Models;

public readonly record struct PlaylistSourceId
{
    public PlaylistSourceId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identyfikator źródła playlisty nie może być pusty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static PlaylistSourceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public readonly record struct ChannelId
{
    public ChannelId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identyfikator kanału nie może być pusty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ChannelId New() => new(Guid.NewGuid());

    public static ChannelId CreateStable(
        PlaylistSourceId sourceId,
        string stableKey,
        int occurrence = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableKey);
        ArgumentOutOfRangeException.ThrowIfNegative(occurrence);

        var input = string.Concat(
            sourceId.Value.ToString("D"),
            "\n",
            stableKey,
            "\n",
            occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new ChannelId(new Guid(hash.AsSpan(0, 16)));
    }

    public override string ToString() => Value.ToString("D");
}
