using System.Text;
using TelePilot.Core.Models;

namespace TelePilot.Playlists;

internal sealed record PlaylistDecodingResult(
    string Text,
    string EncodingName,
    PlaylistDiagnostic[] Diagnostics);

internal static class PlaylistTextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static ReadOnlySpan<byte> Utf32BigEndianBom => [0x00, 0x00, 0xFE, 0xFF];

    private static ReadOnlySpan<byte> Utf32LittleEndianBom => [0xFF, 0xFE, 0x00, 0x00];

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> Utf16BigEndianBom => [0xFE, 0xFF];

    private static ReadOnlySpan<byte> Utf16LittleEndianBom => [0xFF, 0xFE];

    static PlaylistTextDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static PlaylistDecodingResult Decode(
        ReadOnlyMemory<byte> content,
        string? declaredEncoding)
    {
        var bytes = content.Span;
        if (TryGetBomEncoding(bytes, out var bomEncoding, out var preambleLength))
        {
            return new PlaylistDecodingResult(
                bomEncoding.GetString(bytes[preambleLength..]),
                bomEncoding.WebName,
                []);
        }

        PlaylistDiagnostic? declaredEncodingDiagnostic = null;
        if (!string.IsNullOrWhiteSpace(declaredEncoding))
        {
            try
            {
                var encoding = Encoding.GetEncoding(
                    declaredEncoding.Trim().Trim('"', '\''),
                    EncoderFallback.ReplacementFallback,
                    DecoderFallback.ReplacementFallback);
                return new PlaylistDecodingResult(
                    encoding.GetString(bytes),
                    encoding.WebName,
                    []);
            }
            catch (ArgumentException)
            {
                declaredEncodingDiagnostic = new PlaylistDiagnostic(
                    PlaylistDiagnosticCode.InvalidDeclaredEncoding,
                    PlaylistDiagnosticSeverity.Warning,
                    null,
                    "Źródło podało nieznane kodowanie. Zastosowano automatyczne wykrywanie.");
            }
        }

        try
        {
            return new PlaylistDecodingResult(
                StrictUtf8.GetString(bytes),
                StrictUtf8.WebName,
                declaredEncodingDiagnostic is null ? [] : [declaredEncodingDiagnostic]);
        }
        catch (DecoderFallbackException)
        {
            var fallback = SelectLegacyEncoding(bytes);
            var diagnostic = new PlaylistDiagnostic(
                PlaylistDiagnosticCode.FallbackEncodingUsed,
                PlaylistDiagnosticSeverity.Warning,
                null,
                $"Treść nie jest poprawnym UTF-8. Użyto kodowania {fallback.WebName}.");
            return new PlaylistDecodingResult(
                fallback.GetString(bytes),
                fallback.WebName,
                declaredEncodingDiagnostic is null
                    ? [diagnostic]
                    : [declaredEncodingDiagnostic, diagnostic]);
        }
    }

    private static Encoding SelectLegacyEncoding(ReadOnlySpan<byte> bytes)
    {
        var candidates = new[]
        {
            Encoding.GetEncoding(1250),
            Encoding.GetEncoding("iso-8859-2"),
            Encoding.GetEncoding(1252),
        };

        var best = candidates[0];
        var bestScore = Score(best.GetString(bytes));
        foreach (var candidate in candidates.AsSpan(1))
        {
            var score = Score(candidate.GetString(bytes));
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static int Score(string text)
    {
        var score = 0;
        foreach (var character in text)
        {
            if (character == '\uFFFD')
            {
                score -= 20;
            }
            else if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
            {
                score -= 10;
            }
            else if ("ąćęłńóśźżĄĆĘŁŃÓŚŹŻ".Contains(character, StringComparison.Ordinal))
            {
                score += 3;
            }
        }

        return score;
    }

    private static bool TryGetBomEncoding(
        ReadOnlySpan<byte> bytes,
        out Encoding encoding,
        out int preambleLength)
    {
        if (bytes.StartsWith(Utf32BigEndianBom))
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            preambleLength = 4;
            return true;
        }

        if (bytes.StartsWith(Utf32LittleEndianBom))
        {
            encoding = Encoding.UTF32;
            preambleLength = 4;
            return true;
        }

        if (bytes.StartsWith(Utf8Bom))
        {
            encoding = Encoding.UTF8;
            preambleLength = 3;
            return true;
        }

        if (bytes.StartsWith(Utf16BigEndianBom))
        {
            encoding = Encoding.BigEndianUnicode;
            preambleLength = 2;
            return true;
        }

        if (bytes.StartsWith(Utf16LittleEndianBom))
        {
            encoding = Encoding.Unicode;
            preambleLength = 2;
            return true;
        }

        encoding = Encoding.UTF8;
        preambleLength = 0;
        return false;
    }
}
