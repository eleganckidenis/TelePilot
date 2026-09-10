using System.Globalization;

namespace TelePilot.Recording;

public interface IFfmpegArgumentBuilder
{
    FfmpegCommand Build(string executablePath, RecordingRequest request, string outputPath);
}

public sealed class FfmpegArgumentBuilder : IFfmpegArgumentBuilder
{
    public FfmpegCommand Build(string executablePath, RecordingRequest request, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        Validate(request);

        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel",
            "warning",
            "-rw_timeout",
            "15000000",
            "-i",
            request.InputUri.IsFile ? request.InputUri.LocalPath : request.InputUri.AbsoluteUri,
        };

        if (request.Mode == RecordingMode.StreamCopy)
        {
            BuildStreamCopy(arguments, request);
        }
        else
        {
            BuildAudioOnly(arguments, request);
        }

        // Another process can choose the same timestamp-based name concurrently.
        // Never overwrite a file created after our name availability check.
        arguments.Add("-n");
        arguments.Add(outputPath);
        return new FfmpegCommand(executablePath, arguments);
    }

    private static void BuildStreamCopy(List<string> arguments, RecordingRequest request)
    {
        arguments.AddRange(["-map", "0:v?"]);
        AddAudioMap(arguments, request);
        arguments.AddRange(["-map", "0:s?", "-c", "copy"]);
        arguments.AddRange(request.Container switch
        {
            StreamContainer.Matroska => ["-f", "matroska"],
            StreamContainer.MpegTransportStream => ["-f", "mpegts"],
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        });
    }

    private static void BuildAudioOnly(List<string> arguments, RecordingRequest request)
    {
        arguments.Add("-vn");
        AddAudioMap(arguments, request);
        arguments.AddRange(["-metadata", $"artist={request.ChannelName}"]);
        arguments.AddRange(["-metadata", $"title={request.ProgrammeTitle ?? request.ChannelName}"]);
        arguments.AddRange(["-metadata", $"date={request.StartedAt:yyyy-MM-dd}"]);

        switch (request.AudioFormat)
        {
            case AudioOutputFormat.Mp3:
                arguments.AddRange(["-c:a", "libmp3lame", "-b:a", Bitrate(request)]);
                break;
            case AudioOutputFormat.M4aAac:
                arguments.AddRange(["-c:a", "aac", "-b:a", Bitrate(request), "-f", "ipod"]);
                break;
            case AudioOutputFormat.Opus:
                arguments.AddRange(["-c:a", "libopus", "-b:a", Bitrate(request), "-f", "opus"]);
                break;
            case AudioOutputFormat.Flac:
                arguments.AddRange(["-c:a", "flac", "-f", "flac"]);
                break;
            case AudioOutputFormat.Wav:
                arguments.AddRange(["-c:a", "pcm_s16le", "-f", "wav"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static void AddAudioMap(List<string> arguments, RecordingRequest request)
    {
        var map = request.TrackSelection switch
        {
            AudioTrackSelection.All => "0:a?",
            AudioTrackSelection.Current or AudioTrackSelection.Specific when request.AudioTrackIndex is int index =>
                $"0:a:{index.ToString(CultureInfo.InvariantCulture)}?",
            AudioTrackSelection.Current => "0:a:0?",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        arguments.AddRange(["-map", map]);
    }

    private static string Bitrate(RecordingRequest request) =>
        $"{request.LossyBitrateKbps.ToString(CultureInfo.InvariantCulture)}k";

    private static void Validate(RecordingRequest request)
    {
        if (!request.InputUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Adres źródła nagrania musi być bezwzględny.", nameof(request));
        }

        if (request.TrackSelection == AudioTrackSelection.Specific && request.AudioTrackIndex is null or < 0)
        {
            throw new ArgumentException("Dla konkretnej ścieżki wymagany jest nieujemny indeks.", nameof(request));
        }

        if (request.Mode == RecordingMode.AudioOnly &&
            request.AudioFormat is AudioOutputFormat.Mp3 or AudioOutputFormat.M4aAac or AudioOutputFormat.Opus &&
            request.LossyBitrateKbps is < 32 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Przepływność musi mieścić się w zakresie 32–512 kb/s.");
        }
    }
}
