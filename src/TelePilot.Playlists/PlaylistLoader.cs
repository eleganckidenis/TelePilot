using System.Net;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;

namespace TelePilot.Playlists;

public sealed class PlaylistLoader : IPlaylistLoader, IPlaylistService
{
    private const int MaximumManualRedirects = 10;

    private readonly HttpClient _httpClient;
    private readonly IM3uPlaylistParser _parser;
    private readonly PlaylistLoaderOptions _options;

    public PlaylistLoader(
        HttpClient httpClient,
        IM3uPlaylistParser parser,
        PlaylistLoaderOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _options = options ?? new PlaylistLoaderOptions();
    }

    public Task<PlaylistLoadResult> LoadAsync(
        PlaylistSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Kind switch
        {
            PlaylistSourceKind.File => LoadFileAsync(source, cancellationToken),
            PlaylistSourceKind.Url => LoadUrlAsync(source, cancellationToken),
            _ => Task.FromResult(Failure(
                PlaylistLoadErrorCode.InvalidSource,
                "Nieznany rodzaj źródła playlisty.")),
        };
    }

    private async Task<PlaylistLoadResult> LoadFileAsync(
        PlaylistSource source,
        CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(source.Location);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                PlaylistLoadErrorCode.InvalidSource,
                "Ścieżka pliku playlisty jest nieprawidłowa.");
        }

        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > _options.MaximumContentBytes)
            {
                return TooLargeFailure();
            }

            var content = await ReadLimitedAsync(
                stream,
                _options.MaximumContentBytes,
                cancellationToken).ConfigureAwait(false);
            var baseUri = source.GetResolutionBaseUri() ?? new Uri(fullPath, UriKind.Absolute);
            var parsed = _parser.Parse(
                content,
                new PlaylistParseContext(source.Id, baseUri),
                cancellationToken);
            return PlaylistLoadResult.Success(parsed);
        }
        catch (FileNotFoundException)
        {
            return Failure(
                PlaylistLoadErrorCode.FileNotFound,
                "Nie znaleziono pliku playlisty.");
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(
                PlaylistLoadErrorCode.FileNotFound,
                "Nie znaleziono folderu zawierającego playlistę.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                PlaylistLoadErrorCode.AccessDenied,
                "Brak uprawnień do odczytu pliku playlisty.");
        }
        catch (ContentTooLargeException)
        {
            return TooLargeFailure();
        }
        catch (IOException)
        {
            return Failure(
                PlaylistLoadErrorCode.ReadFailure,
                "Nie udało się odczytać pliku playlisty.");
        }
    }

    private async Task<PlaylistLoadResult> LoadUrlAsync(
        PlaylistSource source,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source.Location, UriKind.Absolute, out var requestUri) ||
            requestUri.Scheme is not ("http" or "https"))
        {
            return Failure(
                PlaylistLoadErrorCode.InvalidSource,
                "Adres źródła playlisty musi być poprawnym adresem HTTP lub HTTPS.");
        }

        using var timeoutSource = new CancellationTokenSource(source.DownloadTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            using var response = await SendFollowingRedirectsAsync(
                requestUri,
                linkedSource.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return PlaylistLoadResult.Failure(new PlaylistLoadError(
                    PlaylistLoadErrorCode.HttpFailure,
                    DescribeHttpFailure(response.StatusCode),
                    (int)response.StatusCode));
            }

            if (response.Content.Headers.ContentLength is { } length &&
                length > _options.MaximumContentBytes)
            {
                return TooLargeFailure();
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(linkedSource.Token)
                .ConfigureAwait(false);
            var content = await ReadLimitedAsync(
                stream,
                _options.MaximumContentBytes,
                linkedSource.Token).ConfigureAwait(false);
            var finalUri = response.RequestMessage?.RequestUri ?? requestUri;
            var declaredEncoding = response.Content.Headers.ContentType?.CharSet;
            var parsed = _parser.Parse(
                content,
                new PlaylistParseContext(source.Id, finalUri, declaredEncoding),
                cancellationToken);
            return PlaylistLoadResult.Success(parsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure(
                PlaylistLoadErrorCode.Timeout,
                "Przekroczono limit czasu pobierania playlisty.");
        }
        catch (ContentTooLargeException)
        {
            return TooLargeFailure();
        }
        catch (InsecureRedirectException)
        {
            return Failure(
                PlaylistLoadErrorCode.InsecureRedirect,
                "Serwer próbował przekierować połączenie HTTPS do niezabezpieczonego HTTP. Przekierowanie zostało zablokowane, aby chronić dane uwierzytelniające.");
        }
        catch (TooManyRedirectsException)
        {
            return Failure(
                PlaylistLoadErrorCode.HttpFailure,
                "Serwer przesłał zbyt wiele przekierowań podczas pobierania playlisty.");
        }
        catch (HttpRequestException exception)
        {
            return PlaylistLoadResult.Failure(new PlaylistLoadError(
                PlaylistLoadErrorCode.NetworkFailure,
                "Nie udało się pobrać playlisty z sieci.",
                exception.StatusCode is null ? null : (int)exception.StatusCode.Value));
        }
        catch (IOException)
        {
            return Failure(
                PlaylistLoadErrorCode.NetworkFailure,
                "Połączenie zostało przerwane podczas pobierania playlisty.");
        }
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (IsInsecureRedirect(currentUri, response) &&
                !_options.AllowHttpsToHttpRedirects)
            {
                response.Dispose();
                throw new InsecureRedirectException();
            }

            var effectiveUri = response.RequestMessage?.RequestUri ?? currentUri;
            if (!TryGetRedirectTarget(effectiveUri, response, out var targetUri))
            {
                return response;
            }

            if (redirectCount >= MaximumManualRedirects)
            {
                response.Dispose();
                throw new TooManyRedirectsException();
            }

            response.Dispose();
            currentUri = targetUri;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(
        Stream stream,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81_920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new ContentTooLargeException();
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static string DescribeHttpFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "Serwer odmówił dostępu do playlisty.",
        HttpStatusCode.NotFound =>
            "Serwer nie znalazł playlisty.",
        _ =>
            $"Serwer zwrócił błąd HTTP {(int)statusCode}.",
    };

    private static bool IsInsecureRedirect(Uri requestUri, HttpResponseMessage response)
    {
        if (requestUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (response.RequestMessage?.RequestUri is { } finalUri &&
            finalUri.Scheme == Uri.UriSchemeHttp)
        {
            return true;
        }

        if ((int)response.StatusCode is < 300 or >= 400 ||
            response.Headers.Location is not { } location)
        {
            return false;
        }

        var redirectBaseUri = response.RequestMessage?.RequestUri ?? requestUri;
        return Uri.TryCreate(redirectBaseUri, location, out var target) &&
               target.Scheme == Uri.UriSchemeHttp;
    }

    private static bool TryGetRedirectTarget(
        Uri requestUri,
        HttpResponseMessage response,
        out Uri targetUri)
    {
        targetUri = null!;
        if ((int)response.StatusCode is < 300 or >= 400 ||
            response.Headers.Location is not { } location ||
            !Uri.TryCreate(requestUri, location, out var resolvedUri) ||
            resolvedUri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        targetUri = resolvedUri;
        return true;
    }

    private static PlaylistLoadResult TooLargeFailure() => Failure(
        PlaylistLoadErrorCode.ContentTooLarge,
        "Playlista przekracza dozwolony rozmiar.");

    private static PlaylistLoadResult Failure(
        PlaylistLoadErrorCode code,
        string message) =>
        PlaylistLoadResult.Failure(new PlaylistLoadError(code, message));

    private sealed class ContentTooLargeException : Exception;

    private sealed class InsecureRedirectException : Exception;

    private sealed class TooManyRedirectsException : Exception;
}
