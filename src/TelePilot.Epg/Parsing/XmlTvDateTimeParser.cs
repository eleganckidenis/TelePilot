using System.Globalization;

namespace TelePilot.Epg.Parsing;

public static class XmlTvDateTimeParser
{
    private static readonly int[] SupportedDateLengths = [4, 6, 8, 10, 12, 14];

    public static DateTimeOffset Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Split(value.AsSpan().Trim(), out ReadOnlySpan<char> datePart, out ReadOnlySpan<char> zonePart);
        if (!SupportedDateLengths.Contains(datePart.Length) || !ContainsOnlyAsciiDigits(datePart))
        {
            throw new FormatException($"'{value}' is not a supported XMLTV date and time.");
        }

        int year = ParseNumber(datePart, 0, 4);
        int month = datePart.Length >= 6 ? ParseNumber(datePart, 4, 2) : 1;
        int day = datePart.Length >= 8 ? ParseNumber(datePart, 6, 2) : 1;
        int hour = datePart.Length >= 10 ? ParseNumber(datePart, 8, 2) : 0;
        int minute = datePart.Length >= 12 ? ParseNumber(datePart, 10, 2) : 0;
        int second = datePart.Length >= 14 ? ParseNumber(datePart, 12, 2) : 0;

        DateTime localTime;
        try
        {
            localTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FormatException($"'{value}' contains an invalid calendar date or time.", exception);
        }

        TimeSpan offset = ParseOffset(zonePart, value);
        try
        {
            return new DateTimeOffset(localTime, offset);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException($"'{value}' contains an invalid UTC offset.", exception);
        }
    }

    public static bool TryParse(string? value, out DateTimeOffset result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        try
        {
            result = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            result = default;
            return false;
        }
    }

    private static void Split(
        ReadOnlySpan<char> value,
        out ReadOnlySpan<char> date,
        out ReadOnlySpan<char> zone)
    {
        int separator = value.IndexOfAny(" \t\r\n");
        if (separator >= 0)
        {
            date = value[..separator];
            zone = value[separator..].Trim();
            return;
        }

        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            date = value[..^1];
            zone = value[^1..];
            return;
        }

        if (value.Length > 5 && IsOffsetSign(value[^5]) && ContainsOnlyAsciiDigits(value[^4..]))
        {
            date = value[..^5];
            zone = value[^5..];
            return;
        }

        if (value.Length > 6 &&
            IsOffsetSign(value[^6]) &&
            value[^3] == ':' &&
            ContainsOnlyAsciiDigits(value[^5..^3]) &&
            ContainsOnlyAsciiDigits(value[^2..]))
        {
            date = value[..^6];
            zone = value[^6..];
            return;
        }

        date = value;
        zone = ReadOnlySpan<char>.Empty;
    }

    private static TimeSpan ParseOffset(ReadOnlySpan<char> zone, string originalValue)
    {
        if (zone.IsEmpty ||
            zone.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
            zone.Equals("UTC", StringComparison.OrdinalIgnoreCase) ||
            zone.Equals("GMT", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.Zero;
        }

        if (zone.Length is not (5 or 6) || !IsOffsetSign(zone[0]))
        {
            throw new FormatException(
                $"'{originalValue}' uses an unsupported XMLTV timezone. Use a numeric +HHMM offset.");
        }

        ReadOnlySpan<char> hoursText;
        ReadOnlySpan<char> minutesText;
        if (zone.Length == 5)
        {
            hoursText = zone.Slice(1, 2);
            minutesText = zone.Slice(3, 2);
        }
        else
        {
            if (zone[3] != ':')
            {
                throw new FormatException($"'{originalValue}' contains a malformed UTC offset.");
            }

            hoursText = zone.Slice(1, 2);
            minutesText = zone.Slice(4, 2);
        }

        if (!ContainsOnlyAsciiDigits(hoursText) || !ContainsOnlyAsciiDigits(minutesText))
        {
            throw new FormatException($"'{originalValue}' contains a malformed UTC offset.");
        }

        int hours = int.Parse(hoursText, NumberStyles.None, CultureInfo.InvariantCulture);
        int minutes = int.Parse(minutesText, NumberStyles.None, CultureInfo.InvariantCulture);
        if (hours > 14 || minutes > 59 || (hours == 14 && minutes != 0))
        {
            throw new FormatException($"'{originalValue}' contains a UTC offset outside the supported range.");
        }

        TimeSpan result = new(hours, minutes, 0);
        return zone[0] == '-' ? -result : result;
    }

    private static int ParseNumber(ReadOnlySpan<char> text, int start, int length) =>
        int.Parse(text.Slice(start, length), NumberStyles.None, CultureInfo.InvariantCulture);

    private static bool ContainsOnlyAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOffsetSign(char value) => value is '+' or '-';
}
