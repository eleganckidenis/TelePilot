using TelePilot.Recording;

namespace TelePilot.Tests.Recording;

public sealed class FfmpegArgumentBuilderTests
{
    [Fact]
    public void Recording_RefusesToOverwriteExistingOutput()
    {
        var command = new FfmpegArgumentBuilder().Build("ffmpeg",
            new RecordingRequest(new Uri("https://example.invalid/live"), "Test", null,
                DateTimeOffset.UtcNow, Path.GetTempPath(), RecordingMode.StreamCopy),
            Path.Combine(Path.GetTempPath(), "existing.ts"));
        Assert.Contains("-n", command.Arguments);
        Assert.DoesNotContain("-y", command.Arguments);
    }

    private readonly FfmpegArgumentBuilder _builder = new();

    [Fact]
    public void StreamCopy_MapsVideoAllAudioAndSubtitlesWithoutTranscoding()
    {
        var request = CreateRequest(RecordingMode.StreamCopy) with
        {
            TrackSelection = AudioTrackSelection.All,
            Container = StreamContainer.Matroska,
        };

        var command = _builder.Build("ffmpeg", request, "output file.mkv");

        AssertContainsSequence(command.Arguments, "-map", "0:v?");
        AssertContainsSequence(command.Arguments, "-map", "0:a?");
        AssertContainsSequence(command.Arguments, "-map", "0:s?");
        AssertContainsSequence(command.Arguments, "-c", "copy");
        AssertContainsSequence(command.Arguments, "-f", "matroska");
        Assert.DoesNotContain("-nostdin", command.Arguments);
        Assert.Equal("output file.mkv", command.Arguments[^1]);
    }

    [Theory]
    [InlineData(AudioOutputFormat.Mp3, "libmp3lame", "mp3")]
    [InlineData(AudioOutputFormat.M4aAac, "aac", "m4a")]
    [InlineData(AudioOutputFormat.Opus, "libopus", "opus")]
    [InlineData(AudioOutputFormat.Flac, "flac", "flac")]
    [InlineData(AudioOutputFormat.Wav, "pcm_s16le", "wav")]
    public void AudioOnly_DisablesVideoAndSelectsExpectedCodec(
        AudioOutputFormat format,
        string codec,
        string extensionWithoutDot)
    {
        var request = CreateRequest(RecordingMode.AudioOnly) with { AudioFormat = format };

        var command = _builder.Build("ffmpeg", request, $"output.{extensionWithoutDot}");

        Assert.Contains("-vn", command.Arguments);
        AssertContainsSequence(command.Arguments, "-c:a", codec);
        AssertContainsSequence(command.Arguments, "-metadata", "artist=Kanał testowy");
    }

    [Fact]
    public void SpecificAudioTrack_UsesSeparateSafeArgument()
    {
        var request = CreateRequest(RecordingMode.AudioOnly) with
        {
            TrackSelection = AudioTrackSelection.Specific,
            AudioTrackIndex = 2,
        };

        var command = _builder.Build("ffmpeg", request, "output.mp3");

        AssertContainsSequence(command.Arguments, "-map", "0:a:2?");
        Assert.DoesNotContain(command.Arguments, argument => argument.Contains('"', StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidLossyBitrate_IsRejected()
    {
        var request = CreateRequest(RecordingMode.AudioOnly) with { LossyBitrateKbps = 900 };

        Assert.Throws<ArgumentOutOfRangeException>(() => _builder.Build("ffmpeg", request, "output.mp3"));
    }

    [Fact]
    public void LocalFile_UsesNativePathInsteadOfFileUri()
    {
        var localPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "telepilot source.ts"));
        var request = CreateRequest(RecordingMode.StreamCopy) with { InputUri = new Uri(localPath) };

        var command = _builder.Build("ffmpeg", request, "output.ts");

        var inputIndex = command.Arguments.ToList().IndexOf("-i");
        Assert.True(inputIndex >= 0);
        Assert.Equal(localPath, command.Arguments[inputIndex + 1]);
    }

    private static RecordingRequest CreateRequest(RecordingMode mode) => new(
        new Uri("https://example.invalid/stream.m3u8?token=secret"),
        "Kanał testowy",
        "Program testowy",
        new DateTimeOffset(2026, 7, 30, 20, 0, 0, TimeSpan.FromHours(2)),
        Path.GetTempPath(),
        mode);

    private static void AssertContainsSequence(IReadOnlyList<string> arguments, string first, string second)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == first && arguments[index + 1] == second)
            {
                return;
            }
        }

        Assert.Fail($"Nie znaleziono sekwencji '{first}', '{second}'.");
    }
}
