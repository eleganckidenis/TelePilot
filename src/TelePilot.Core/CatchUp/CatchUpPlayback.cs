using System.Globalization;
using System.Text;
using TelePilot.Core.Models;

namespace TelePilot.Core.CatchUp;

public enum CatchUpSupportStatus
{
    Supported,
    NotDeclared,
    Disabled,
    MissingRetention,
    InvalidRetention,
    UnsupportedMode,
    InvalidTemplate,
    InvalidCorrection,
    UnsupportedStreamUrl,
}

/// <summary>Builds provider archive requests from playlist metadata or explicit source settings.</summary>
public static class CatchUpPlayback
{
    private const int MaximumTemplateLength = 32768;
    private const string Unsupported = "Playlista nie zawiera obsługiwanych ustawień archiwum dla tego kanału.";
    private static readonly string[] ArchiveAttributes =
    [
        "catchup", "catchup-type", "catchup-source", "catchup-days", "catchup-time",
        "catchup-correction", "timeshift", "tvg-rec", "catchup-enable",
    ];

    /// <summary>Applies defaults chosen for this channel's source without modifying playlist metadata.</summary>
    public static Channel ApplySourceSettings(Channel channel, CatchUpSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Mode == CatchUpSourceMode.Automatic)
        {
            return channel;
        }

        var attributes = new Dictionary<string, string>(channel.Attributes, StringComparer.OrdinalIgnoreCase);
        if (settings.Mode == CatchUpSourceMode.Disabled)
        {
            attributes["catchup"] = "none";
        }
        else
        {
            var mode = settings.Mode switch
            {
                CatchUpSourceMode.FlussonicHls => "flussonic-index",
                CatchUpSourceMode.FlussonicTs => "flussonic-ts-forced",
                CatchUpSourceMode.Shift => "shift",
                CatchUpSourceMode.XtreamCodes => "xc",
                _ => null,
            };
            if (mode is null || !IsValidRetention(settings.RetentionDays) ||
                !double.IsFinite(settings.CorrectionHours) || settings.CorrectionHours is < -24 or > 24)
            {
                return channel;
            }

            if (settings.OverridePlaylist)
            {
                foreach (var key in ArchiveAttributes)
                {
                    attributes.Remove(key);
                }
            }
            else
            {
                var declaredMode = Attribute(channel, "catchup") ?? Attribute(channel, "catchup-type");
                if (HasExplicitOptOut(channel) ||
                    declaredMode is not null && !IsKnownMode(declaredMode.ToLowerInvariant()) ||
                    HasDeclaredRetention(channel) && !TryGetRetentionDays(channel, out _))
                {
                    return channel;
                }
            }

            if (settings.OverridePlaylist ||
                (Attribute(channel, "catchup") ?? Attribute(channel, "catchup-type")) is null &&
                Attribute(channel, "catchup-source") is null)
            {
                attributes["catchup"] = mode;
            }

            if (!HasDeclaredRetention(channel) || settings.OverridePlaylist)
            {
                attributes["catchup-days"] = settings.RetentionDays.ToString(CultureInfo.InvariantCulture);
            }

            if (settings.OverridePlaylist || Attribute(channel, "catchup-correction") is null)
            {
                attributes["catchup-correction"] = settings.CorrectionHours.ToString(CultureInfo.InvariantCulture);
            }
        }

        attributes["telepilot-catchup-profile"] = "manual";
        return new Channel(channel.Id, channel.PlaylistSourceId, channel.Name, channel.StreamUrl,
            channel.TvgId, channel.TvgName, channel.LogoUrl, channel.GroupTitle, attributes);
    }

    public static bool IsSupported(Channel channel)
        => GetSupportStatus(channel) == CatchUpSupportStatus.Supported;

    /// <summary>Explains archive support without exposing provider addresses or credentials.</summary>
    public static CatchUpSupportStatus GetSupportStatus(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return GetConfiguration(channel, out _, out _);
    }

    public static bool TryGetRetentionDays(Channel channel, out double days)
    {
        ArgumentNullException.ThrowIfNull(channel);
        days = 0;
        if (HasExplicitOptOut(channel))
        {
            return false;
        }

        // Explicit invalid values never fall through to a different retention alias.
        var seconds = false;
        string? value;
        if (channel.Attributes.TryGetValue("catchup-days", out var catchupDays))
        {
            value = catchupDays;
        }
        else if (channel.Attributes.TryGetValue("catchup-time", out var catchupTime))
        {
            value = catchupTime;
            seconds = true;
        }
        else if (channel.Attributes.TryGetValue("timeshift", out var timeshift))
        {
            value = timeshift;
        }
        else
        {
            value = Attribute(channel, "tvg-rec");
            // One only advertises capability; the provider has not specified a window.
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var marker) || marker <= 1)
            {
                return false;
            }
        }

        if (!double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out days))
        {
            return false;
        }

        if (seconds)
        {
            days /= 86400;
        }

        return IsValidRetention(days);
    }

    public static bool TryCreatePlaybackChannel(
        Channel channel,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset now,
        out Channel? archiveChannel,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(channel);
        archiveChannel = null;
        error = Unsupported;
        if (GetConfiguration(channel, out var template, out var correctionHours) != CatchUpSupportStatus.Supported ||
            !TryGetRetentionDays(channel, out var days))
        {
            return false;
        }

        if (end <= start || start >= now || end > now)
        {
            error = "Archiwum jest dostępne dla zakończonych audycji z prawidłowym czasem początku i końca.";
            return false;
        }

        if ((now - start).TotalDays > days)
        {
            error = Attribute(channel, "telepilot-catchup-profile") == "manual"
                ? "Audycja znajduje się poza ustawionym okresem archiwum."
                : "Audycja znajduje się poza okresem archiwum podanym w playliście.";
            return false;
        }

        // Offset arithmetic and UTC formatting avoid the computer's timezone and DST ambiguity.
        // Kodi's catchup-correction is subtracted from the timestamps sent to the provider.
        var correction = TimeSpan.FromHours(correctionHours);
        if (!TryCorrectTime(start, correction, out var correctedStart) ||
            !TryCorrectTime(end, correction, out var correctedEnd) ||
            !TryExpand(template, correctedStart, correctedEnd, now, out var url) ||
            !IsHttpUrl(url))
        {
            error = "Ustawienia adresu archiwum w playliście są nieprawidłowe lub nieobsługiwane.";
            return false;
        }

        archiveChannel = new Channel(
            channel.Id, channel.PlaylistSourceId, channel.Name, url,
            channel.TvgId, channel.TvgName, channel.LogoUrl, channel.GroupTitle, channel.Attributes);
        error = null;
        return true;
    }

    private static CatchUpSupportStatus GetConfiguration(Channel channel, out string template, out double correction)
    {
        template = string.Empty;
        correction = 0;
        if (HasExplicitOptOut(channel))
        {
            return CatchUpSupportStatus.Disabled;
        }

        if (!ArchiveAttributes.Any(channel.Attributes.ContainsKey))
        {
            return CatchUpSupportStatus.NotDeclared;
        }

        if (!TryGetRetentionDays(channel, out _))
        {
            return HasDeclaredRetention(channel) ? CatchUpSupportStatus.InvalidRetention : CatchUpSupportStatus.MissingRetention;
        }

        var correctionText = Attribute(channel, "catchup-correction");
        if (correctionText is not null &&
            (!double.TryParse(correctionText, NumberStyles.Float, CultureInfo.InvariantCulture, out correction) ||
             !double.IsFinite(correction) || correction is < -24 or > 24))
        {
            return CatchUpSupportStatus.InvalidCorrection;
        }

        var mode = (Attribute(channel, "catchup") ?? Attribute(channel, "catchup-type"))?.ToLowerInvariant();
        if (mode is null && Attribute(channel, "catchup-source") is not null)
        {
            mode = "default";
        }
        else if (mode is null)
        {
            // Retention is explicit; the provider has not selected another format.
            mode = "shift";
        }

        if (!IsKnownMode(mode))
        {
            return CatchUpSupportStatus.UnsupportedMode;
        }

        // A provider's complete template is more specific than an automatic naming convention.
        // An explicit disabled or unknown mode was already rejected above.
        var explicitSource = Attribute(channel, "catchup-source");
        if (explicitSource is not null && mode != "append")
        {
            template = explicitSource;
            return IsValidTemplate(template) ? CatchUpSupportStatus.Supported : CatchUpSupportStatus.InvalidTemplate;
        }

        // A complete archive URL is independent of the live transport. Only formats
        // derived from the live URL require that URL to be HTTP(S).
        if (!IsHttpUrl(channel.StreamUrl))
        {
            return CatchUpSupportStatus.UnsupportedStreamUrl;
        }

        switch (mode)
        {
            case "default":
                template = Attribute(channel, "catchup-source") ?? string.Empty;
                break;
            case "append":
                var suffix = Attribute(channel, "catchup-source");
                if (suffix is null || suffix[0] is not ('?' or '&') || channel.StreamUrl.Contains('#'))
                {
                    return CatchUpSupportStatus.InvalidTemplate;
                }

                template = AppendQuery(channel.StreamUrl, suffix);
                break;
            case "shift":
            case "timeshift":
                if (channel.StreamUrl.Contains('#'))
                {
                    return CatchUpSupportStatus.InvalidTemplate;
                }

                template = channel.StreamUrl + (channel.StreamUrl.Contains('?') ? "&" : "?") +
                    "utc={utc}&lutc={lutc}";
                break;
            case "nimble":
            case "nimble-dvr":
                if (!TryBuildNimble(channel.StreamUrl, out template))
                {
                    return CatchUpSupportStatus.InvalidTemplate;
                }

                break;
            case "fs":
            case "flussonic":
            case "flussonic-hls":
            case "flussonic-ts":
                if (!TryBuildFlussonic(channel.StreamUrl, out template))
                {
                    return CatchUpSupportStatus.InvalidTemplate;
                }

                break;
            case "flussonic-index":
            case "flussonic-ts-forced":
                if (!TryBuildFlussonic(channel.StreamUrl, out template, mode))
                {
                    return CatchUpSupportStatus.InvalidTemplate;
                }

                break;
            case "xc":
            case "xtream-codes":
                if (!TryBuildXtream(channel.StreamUrl, out template))
                {
                    return CatchUpSupportStatus.InvalidTemplate;
                }

                break;
            default:
                return CatchUpSupportStatus.UnsupportedMode;
        }

        return IsValidTemplate(template) ? CatchUpSupportStatus.Supported : CatchUpSupportStatus.InvalidTemplate;
    }

    private static bool IsValidTemplate(string template)
    {
        var sampleStart = DateTimeOffset.UnixEpoch.AddDays(1);
        return template.Length <= MaximumTemplateLength &&
            TryExpand(template, sampleStart, sampleStart.AddHours(1), sampleStart.AddDays(1), out var sample) &&
            IsHttpUrl(sample);
    }

    private static string AppendQuery(string liveUrl, string suffix)
    {
        var separator = liveUrl.Contains('?') ? "&" : "?";
        if (liveUrl.EndsWith('?') || liveUrl.EndsWith('&'))
        {
            separator = string.Empty;
        }

        return liveUrl + separator + suffix[1..];
    }

    private static bool TryBuildNimble(string liveUrl, out string template)
    {
        template = string.Empty;
        if (liveUrl.Contains('#'))
        {
            return false;
        }

        var query = liveUrl.IndexOf('?');
        var path = query < 0 ? liveUrl : liveUrl[..query];
        var suffix = query < 0 ? string.Empty : liveUrl[query..];
        var separator = path.LastIndexOf('/');
        // The leaf must be a path component, never the URL authority itself.
        if (separator < path.IndexOf("://", StringComparison.Ordinal) + 3)
        {
            return false;
        }

        var file = path[(separator + 1)..];
        var prefix = file switch
        {
            "playlist.m3u8" => "playlist",
            "playlist_fmp4.m3u8" => "playlist_fmp4",
            _ => null,
        };
        if (prefix is null)
        {
            return false;
        }

        // Keep escaped credentials, paths, and authentication queries exactly as supplied.
        // https://blog.wmspanel.com/2015/11/dvr-time-range-fragments-epg.html
        template = path[..(separator + 1)] + prefix + "_dvr_range-{utc}-{duration}.m3u8" + suffix;
        return true;
    }

    private static bool TryBuildFlussonic(string liveUrl, out string template, string? forcedMode = null)
    {
        template = string.Empty;
        if (liveUrl.Contains('#'))
        {
            return false;
        }

        var query = liveUrl.IndexOf('?');
        var path = query < 0 ? liveUrl : liveUrl[..query];
        var suffix = query < 0 ? string.Empty : liveUrl[query..];
        var separator = path.LastIndexOf('/');
        var prefix = path[..(separator + 1)];
        var file = path[(separator + 1)..];
        if (new Uri(path).AbsolutePath.Count(static character => character == '/') < 2)
        {
            return false;
        }

        if (forcedMode == "flussonic-ts-forced")
        {
            template = prefix + "timeshift_abs-{utc}.ts" + suffix;
        }
        else if (forcedMode == "flussonic-index")
        {
            template = prefix + "index-{utc}-{duration}.m3u8" + suffix;
        }
        else if (file == "mpegts")
        {
            template = prefix + "timeshift_abs-{utc}.ts" + suffix;
        }
        else if (file.EndsWith(".m3u8", StringComparison.Ordinal))
        {
            if (file.Length == 5)
            {
                return false;
            }

            // A completed EPG item is a finite VOD window, not a sliding relative delay.
            // https://flussonic.com/doc/fms/protocols/hls/#dvr-catchup-playback
            var extension = file.EndsWith(".fmp4.m3u8", StringComparison.Ordinal) ? ".fmp4.m3u8" :
                file.EndsWith(".ts.m3u8", StringComparison.Ordinal) ? ".ts.m3u8" : ".m3u8";
            var listType = file[..^5];
            // Preserve the provider's legacy MPEG-TS track layout for LibVLC.
            var archiveType = listType == "mono" ||
                (listType.StartsWith("video", StringComparison.Ordinal) && listType[5..].All(char.IsAsciiDigit))
                ? listType
                : "archive";
            template = prefix + archiveType + "-{utc}-{duration}" + extension + suffix;
        }
        else
        {
            return false;
        }

        return true;
    }

    private static bool TryBuildXtream(string liveUrl, out string template)
    {
        template = string.Empty;
        var uri = new Uri(liveUrl);
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }

        var parts = uri.AbsolutePath.TrimStart('/').Split('/');
        var first = parts.Length == 4 && parts[0] == "live" ? 1 : 0;
        if (parts.Length - first != 3 || parts.Any(static part => part.Length == 0))
        {
            return false;
        }

        var stream = parts[first + 2];
        var dot = stream.IndexOf('.');
        var extension = dot < 0 ? ".ts" : stream[dot..];
        var id = dot < 0 ? stream : stream[..dot];
        if (id.Length == 0 || (dot >= 0 && extension is not (".ts" or ".m3u8" or ".m3u")))
        {
            return false;
        }

        template = liveUrl[..liveUrl.IndexOf('/', liveUrl.IndexOf("://", StringComparison.Ordinal) + 3)] +
            "/timeshift/" + parts[first] + "/" + parts[first + 1] +
            "/{duration:60}/{Y}-{m}-{d}:{H}-{M}/" + id + extension;
        return true;
    }

    private static bool TryExpand(
        string template, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, out string result)
    {
        result = string.Empty;
        if (template.Length is 0 or > MaximumTemplateLength)
        {
            return false;
        }

        var builder = new StringBuilder(template.Length);
        var hasStart = false;
        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];
            var dollar = character == '$' && index + 1 < template.Length && template[index + 1] == '{';
            if (character != '{' && !dollar)
            {
                if (character == '}')
                {
                    return false;
                }

                builder.Append(character);
                continue;
            }

            var open = dollar ? index + 1 : index;
            var close = template.IndexOf('}', open + 1);
            if (close < 0 || !TryExpandToken(template[(open + 1)..close], dollar, start, end, now,
                    out var replacement, out var usesStart))
            {
                return false;
            }

            builder.Append(replacement);
            hasStart |= usesStart;
            index = close;
        }

        result = builder.ToString();
        return hasStart;
    }

    private static bool TryExpandToken(
        string token, bool dollar, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now,
        out string replacement, out bool usesStart)
    {
        replacement = string.Empty;
        usesStart = false;
        var colon = token.IndexOf(':');
        var name = colon < 0 ? token : token[..colon];
        var argument = colon < 0 ? null : token[(colon + 1)..];
        if (name is "duration" or "offset")
        {
            if (dollar && argument is not null)
            {
                return false;
            }

            var divider = 1L;
            if (argument is not null &&
                (!long.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out divider) || divider <= 0))
            {
                return false;
            }

            var seconds = name == "duration" ? end.ToUnixTimeSeconds() - start.ToUnixTimeSeconds() :
                now.ToUnixTimeSeconds() - start.ToUnixTimeSeconds();
            replacement = (seconds / divider).ToString(CultureInfo.InvariantCulture);
            usesStart = name == "offset";
            return true;
        }

        if (!dollar && argument is null && name.Length == 1 && "YmdHMS".Contains(name[0]))
        {
            replacement = FormatDatePart(start, name[0]);
            usesStart = true;
            return true;
        }

        DateTimeOffset time;
        switch (name, dollar)
        {
            case ("utc", false):
            case ("start", true):
                time = start;
                usesStart = true;
                break;
            case ("utcend", false):
            case ("end", true):
                time = end;
                break;
            case ("lutc", false):
            case ("now", true):
            case ("timestamp", true):
                time = now;
                break;
            default:
                return false;
        }

        if (argument is null)
        {
            replacement = time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (argument.Length is 0 or > 128 || argument.Any(static value =>
                char.IsLetter(value) && !"YmdHMS".Contains(value) || value is '{' or '}' or '%'))
        {
            return false;
        }

        var builder = new StringBuilder();
        foreach (var character in argument)
        {
            builder.Append("YmdHMS".Contains(character) ? FormatDatePart(time, character) : character.ToString());
        }

        replacement = builder.ToString();
        return true;
    }

    private static string FormatDatePart(DateTimeOffset time, char token) => time.UtcDateTime.ToString(
        token switch { 'Y' => "yyyy", 'm' => "MM", 'd' => "dd", 'H' => "HH", 'M' => "mm", _ => "ss" },
        CultureInfo.InvariantCulture);

    private static bool TryCorrectTime(DateTimeOffset time, TimeSpan correction, out DateTimeOffset result)
    {
        var ticks = time.UtcTicks - correction.Ticks;
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            result = default;
            return false;
        }

        result = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    private static bool IsHttpUrl(string value) =>
        !value.Any(static character => char.IsControl(character) || character is '{' or '}' or '|') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && uri.Host.Length > 0;

    private static string? Attribute(Channel channel, string key) =>
        channel.Attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static bool IsValidRetention(double days) => double.IsFinite(days) && days > 0 && days <= 36500;

    private static bool HasDeclaredRetention(Channel channel) =>
        channel.Attributes.ContainsKey("catchup-days") || channel.Attributes.ContainsKey("catchup-time") ||
        channel.Attributes.ContainsKey("timeshift") ||
        channel.Attributes.TryGetValue("tvg-rec", out var marker) && marker.Trim() != "1";

    private static bool HasExplicitOptOut(Channel channel) =>
        IsDisabledValue(Attribute(channel, "catchup") ?? Attribute(channel, "catchup-type")) ||
        IsDisabledValue(Attribute(channel, "catchup-enable")) ||
        IsDisabledValue(Attribute(channel, "tvg-rec")) ||
        IsDisabledValue(Attribute(channel, "timeshift"));

    private static bool IsDisabledValue(string? value) =>
        value?.ToLowerInvariant() is "0" or "none" or "off" or "disabled" or "false";

    private static bool IsKnownMode(string mode) => mode is
        "default" or "append" or "shift" or "timeshift" or "nimble" or "nimble-dvr" or
        "fs" or "flussonic" or "flussonic-hls" or "flussonic-ts" or "flussonic-index" or
        "flussonic-ts-forced" or "xc" or "xtream-codes";
}
