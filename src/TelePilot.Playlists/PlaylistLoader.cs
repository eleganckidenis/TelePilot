using System.Net;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;
using static TelePilot.Core.Localization.Localizer;

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
                T("Nieznany rodzaj źródła playlisty.", "Unknown playlist source type."))),
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
                T("Ścieżka pliku playlisty jest nieprawidłowa.", "The playlist file path is invalid."));
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
                T("Nie znaleziono pliku playlisty.", "The playlist file was not found."));
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(
                PlaylistLoadErrorCode.FileNotFound,
                T("Nie znaleziono folderu zawierającego playlistę.", "The folder containing the playlist was not found."));
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                PlaylistLoadErrorCode.AccessDenied,
                T("Brak uprawnień do odczytu pliku playlisty.", "Permission denied reading the playlist file."));
        }
        catch (ContentTooLargeException)
        {
            return TooLargeFailure();
        }
        catch (IOException)
        {
            return Failure(
                PlaylistLoadErrorCode.ReadFailure,
                T("Nie udało się odczytać pliku playlisty.", "Could not read the playlist file."));
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
                T("Adres źródła playlisty musi być poprawnym adresem HTTP lub HTTPS.", "The playlist source must have a valid HTTP or HTTPS address."));
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
                T("Przekroczono limit czasu pobierania playlisty.", "The playlist download timed out."));
        }
        catch (ContentTooLargeException)
        {
            return TooLargeFailure();
        }
        catch (InsecureRedirectException)
        {
            return Failure(
                PlaylistLoadErrorCode.InsecureRedirect,
                T("Serwer próbował przekierować połączenie HTTPS do niezabezpieczonego HTTP. Przekierowanie zostało zablokowane, aby chronić dane uwierzytelniające.", "The server tried to redirect HTTPS to unsecured HTTP. The redirect was blocked to protect credentials."));
        }
        catch (TooManyRedirectsException)
        {
            return Failure(
                PlaylistLoadErrorCode.HttpFailure,
                T("Serwer przesłał zbyt wiele przekierowań podczas pobierania playlisty.", "The server sent too many redirects while downloading the playlist."));
        }
        catch (HttpRequestException exception)
        {
            return PlaylistLoadResult.Failure(new PlaylistLoadError(
                PlaylistLoadErrorCode.NetworkFailure,
                T("Nie udało się pobrać playlisty z sieci.", "Could not download the playlist."),
                exception.StatusCode is null ? null : (int)exception.StatusCode.Value));
        }
        catch (IOException)
        {
            return Failure(
                PlaylistLoadErrorCode.NetworkFailure,
                T("Połączenie zostało przerwane podczas pobierania playlisty.", "The connection closed while downloading the playlist."));
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
            T("Serwer odmówił dostępu do playlisty.", "The server denied access to the playlist."),
        HttpStatusCode.NotFound =>
            T("Serwer nie znalazł playlisty.", "The server could not find the playlist."),
        _ =>
            T($"Serwer zwrócił błąd HTTP {(int)statusCode}.", $"The server returned HTTP error {(int)statusCode}."),
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
        T("Playlista przekracza dozwolony rozmiar.", "The playlist exceeds the allowed size."));

    private static PlaylistLoadResult Failure(
        PlaylistLoadErrorCode code,
        string message) =>
        PlaylistLoadResult.Failure(new PlaylistLoadError(code, message));

    private sealed class ContentTooLargeException : Exception;

    private sealed class InsecureRedirectException : Exception;

    private sealed class TooManyRedirectsException : Exception;
}
