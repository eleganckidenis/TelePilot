using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using TelePilot.Core.Models;
using TelePilot.Epg.Matching;
using TelePilot.Epg.Models;
using TelePilot.Epg.Parsing;

namespace TelePilot.Epg.Services;

public sealed class XmlTvEpgService : IEpgSourceManager, IDisposable
{
    private readonly XmlTvParser _parser;
    private readonly ChannelEpgMatcher _matcher;
    private readonly ConcurrentDictionary<ChannelId, string> _manualMappings;
    private readonly TimeZoneInfo _displayTimeZone;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _refreshInterval;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IEpgDataSource[] _sources;
    private EpgSnapshot? _snapshot;
    private EpgServiceStatus _status = new(EpgServiceState.Empty);
    private int _manualMappingVersion;
    private bool _disposed;

    public XmlTvEpgService(
        IEpgDataSource source,
        XmlTvParser? parser = null,
        ChannelEpgMatcher? matcher = null,
        IReadOnlyDictionary<ChannelId, string>? manualMappings = null,
        TimeZoneInfo? displayTimeZone = null,
        TimeProvider? timeProvider = null,
        TimeSpan? refreshInterval = null,
        HttpClient? httpClient = null)
        : this(
            [source ?? throw new ArgumentNullException(nameof(source))],
            httpClient,
            parser,
            matcher,
            manualMappings,
            displayTimeZone,
            timeProvider,
            refreshInterval)
    {
    }

    public XmlTvEpgService(
        HttpClient httpClient,
        XmlTvParser? parser = null,
        ChannelEpgMatcher? matcher = null,
        IReadOnlyDictionary<ChannelId, string>? manualMappings = null,
        TimeZoneInfo? displayTimeZone = null,
        TimeProvider? timeProvider = null,
        TimeSpan? refreshInterval = null)
        : this(
            [],
            httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            parser,
            matcher,
            manualMappings,
            displayTimeZone,
            timeProvider,
            refreshInterval)
    {
    }

    private XmlTvEpgService(
        IEpgDataSource[] sources,
        HttpClient? httpClient,
        XmlTvParser? parser,
        ChannelEpgMatcher? matcher,
        IReadOnlyDictionary<ChannelId, string>? manualMappings,
        TimeZoneInfo? displayTimeZone,
        TimeProvider? timeProvider,
        TimeSpan? refreshInterval)
    {
        TimeSpan interval = refreshInterval ?? TimeSpan.FromHours(6);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval), interval, "Refresh interval must be positive.");
        }

        _sources = sources;
        _parser = parser ?? new XmlTvParser();
        _matcher = matcher ?? new ChannelEpgMatcher();
        _manualMappings = new ConcurrentDictionary<ChannelId, string>(manualMappings ??
            new Dictionary<ChannelId, string>());
        _displayTimeZone = displayTimeZone ?? TimeZoneInfo.Local;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _refreshInterval = interval;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public string Source => string.Join(", ", Sources);

    public IReadOnlyList<string> Sources => Array.AsReadOnly(
        Volatile.Read(ref _sources)
            .Select(static source => source.Description)
            .ToArray());

    public EpgServiceStatus Status => Volatile.Read(ref _status);

    public IReadOnlyList<EpgChannel> Channels =>
        Volatile.Read(ref _snapshot)?.Document.Channels ?? Array.Empty<EpgChannel>();

    public IReadOnlyList<XmlTvParseWarning> Warnings =>
        Volatile.Read(ref _snapshot)?.Document.Warnings ?? Array.Empty<XmlTvParseWarning>();

    public async Task SetSourcesAsync(
        IEnumerable<string> sources,
        Uri? baseUri = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sources);
        if (baseUri is not null && !baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The EPG base URI must be absolute.", nameof(baseUri));
        }

        IEpgDataSource[] resolvedSources = ResolveSources(sources, baseUri);
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _sources, resolvedSources);
            Volatile.Write(ref _snapshot, null);
            Volatile.Write(ref _status, new EpgServiceStatus(EpgServiceState.Empty));

            if (resolvedSources.Length > 0)
            {
                await RefreshCoreAsync(resolvedSources, Status, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public Task<IReadOnlyList<Programme>> GetProgrammesAsync(
        Channel channel,
        DateOnly scheduleDate,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(channel);
        cancellationToken.ThrowIfCancellationRequested();

        EpgSnapshot? snapshot = Volatile.Read(ref _snapshot);
        ChannelEpgMapping? mapping = ResolveMapping(channel, snapshot);
        if (snapshot is null || mapping is null)
        {
            return Task.FromResult<IReadOnlyList<Programme>>(Array.Empty<Programme>());
        }

        DateTimeOffset dayStart = GetDayBoundary(scheduleDate);
        DateTimeOffset dayEnd = scheduleDate == DateOnly.MaxValue
            ? DateTimeOffset.MaxValue
            : GetDayBoundary(scheduleDate.AddDays(1));
        string? query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        Programme[] channelProgrammes = snapshot.GetProgrammes(mapping.EpgChannel.Id);
        Programme[] programmes = channelProgrammes
            .Where((programme, index) => OverlapsDay(
                programme,
                programme.Stop ?? (index + 1 < channelProgrammes.Length ? channelProgrammes[index + 1].Start : null),
                dayStart,
                dayEnd))
            .Where(programme => query is null || ProgrammeTitleContains(programme, query))
            .ToArray();

        return Task.FromResult<IReadOnlyList<Programme>>(Array.AsReadOnly(programmes));
    }

    public Programme? GetCurrentProgramme(Channel channel, DateTimeOffset at)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(channel);

        EpgSnapshot? snapshot = Volatile.Read(ref _snapshot);
        ChannelEpgMapping? mapping = ResolveMapping(channel, snapshot);
        if (snapshot is null || mapping is null)
        {
            return null;
        }

        Programme[] programmes = snapshot.GetProgrammes(mapping.EpgChannel.Id);

        for (int index = FindLastProgrammeStartingAtOrBefore(programmes, at); index >= 0; index--)
        {
            Programme candidate = programmes[index];
            if (candidate.Start > at)
            {
                continue;
            }

            if (candidate.Stop is DateTimeOffset stop)
            {
                if (stop > at)
                {
                    return candidate;
                }

                continue;
            }

            DateTimeOffset? nextStart = index + 1 < programmes.Length
                ? programmes[index + 1].Start
                : null;
            if (nextStart is null || at < nextStart)
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<EpgRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IEpgDataSource[] sources = Volatile.Read(ref _sources);
            if (sources.Length == 0)
            {
                Volatile.Write(ref _snapshot, null);
                Volatile.Write(ref _status, new EpgServiceStatus(EpgServiceState.Empty));
                return EpgRefreshResult.NoSources;
            }

            return await RefreshCoreAsync(sources, Status, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void SetManualMapping(ChannelId channelId, string epgChannelId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(epgChannelId);
        _manualMappings[channelId] = epgChannelId.Trim();
        Interlocked.Increment(ref _manualMappingVersion);
    }

    public bool RemoveManualMapping(ChannelId channelId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_manualMappings.TryRemove(channelId, out _))
        {
            return false;
        }

        Interlocked.Increment(ref _manualMappingVersion);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _refreshGate.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        _disposed = true;
    }

    private async Task<EpgRefreshResult> RefreshCoreAsync(
        IEpgDataSource[] sources,
        EpgServiceStatus previousStatus,
        CancellationToken cancellationToken)
    {
        Volatile.Write(ref _status, new EpgServiceStatus(EpgServiceState.Refreshing, previousStatus.CacheMetadata));
        List<SourceDocument> documents = [];
        List<SourceFailure> failures = [];

        try
        {
            foreach (IEpgDataSource source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using Stream stream = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);
                    XmlTvDocument document = await _parser.ParseAsync(stream, cancellationToken).ConfigureAwait(false);
                    documents.Add(new SourceDocument(source.Description, document));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(new SourceFailure(source.Description, exception.GetType().Name));
                }
            }

            if (documents.Count == 0)
            {
                string exceptionType = failures.Count == 1 ? failures[0].ExceptionType : "MultipleSources";
                SetFailedStatus($"EPG refresh failed ({exceptionType}).");
                return EpgRefreshResult.Failed;
            }

            XmlTvDocument merged = MergeDocuments(documents, failures);
            DateTimeOffset downloadedAt = _timeProvider.GetUtcNow();
            EpgCacheMetadata cacheMetadata = new(
                string.Join(", ", sources.Select(static source => source.Description)),
                downloadedAt,
                downloadedAt + _refreshInterval,
                EpgRefreshResult.Success,
                merged.Channels.Count,
                merged.Programmes.Count,
                failures.Count == 0 ? null : "Some EPG sources could not be refreshed.");
            Volatile.Write(ref _snapshot, new EpgSnapshot(merged));
            Volatile.Write(
                ref _status,
                new EpgServiceStatus(
                    EpgServiceState.Ready,
                    cacheMetadata,
                    failures.Count == 0 ? null : "Some EPG sources could not be refreshed."));
            return EpgRefreshResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _status, previousStatus);
            throw;
        }
        catch (Exception exception)
        {
            SetFailedStatus($"EPG refresh failed ({exception.GetType().Name}).");
            return EpgRefreshResult.Failed;
        }
    }

    private void SetFailedStatus(string safeError)
    {
        DateTimeOffset failedAt = _timeProvider.GetUtcNow();
        EpgSnapshot? current = Volatile.Read(ref _snapshot);
        string source = Source;
        EpgCacheMetadata? failedMetadata = source.Length == 0
            ? null
            : new EpgCacheMetadata(
                source,
                failedAt,
                failedAt + _refreshInterval,
                EpgRefreshResult.Failed,
                current?.Document.Channels.Count ?? 0,
                current?.Document.Programmes.Count ?? 0);
        Volatile.Write(
            ref _status,
            new EpgServiceStatus(EpgServiceState.Failed, failedMetadata, safeError));
    }

    private IEpgDataSource[] ResolveSources(IEnumerable<string> sources, Uri? baseUri)
    {
        List<IEpgDataSource> resolved = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string? source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            string value = source.Trim();
            if (seen.Add(value))
            {
                resolved.Add(ResolveSource(value, baseUri));
            }
        }

        return [.. resolved];
    }

    private IEpgDataSource ResolveSource(string source, Uri? baseUri)
    {
        if (source.StartsWith("//", StringComparison.Ordinal) &&
            baseUri is not null &&
            (baseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return CreateUriSource(new Uri(baseUri, source));
        }

        if (Path.IsPathRooted(source))
        {
            return new FileEpgDataSource(source);
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? absoluteUri))
        {
            return CreateUriSource(absoluteUri);
        }

        if (baseUri is not null)
        {
            return CreateUriSource(new Uri(baseUri, source));
        }

        return new FileEpgDataSource(source);
    }

    private IEpgDataSource CreateUriSource(Uri uri)
    {
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return new HttpEpgDataSource(_httpClient, uri);
        }

        if (uri.IsFile)
        {
            return new FileEpgDataSource(uri.LocalPath);
        }

        throw new ArgumentException($"Unsupported EPG source scheme '{uri.Scheme}'.", nameof(uri));
    }

    private ChannelEpgMapping? ResolveMapping(Channel channel, EpgSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        ChannelMappingCacheKey cacheKey = new(
            channel.Id,
            channel.Name,
            channel.TvgId,
            channel.TvgName,
            Volatile.Read(ref _manualMappingVersion));
        return snapshot.MappingCache.GetOrAdd(
            cacheKey,
            _ => new ChannelMappingCacheEntry(
                _matcher.Match(channel, snapshot.Document.Channels, _manualMappings))).Mapping;
    }

    private static int FindLastProgrammeStartingAtOrBefore(
        Programme[] programmes,
        DateTimeOffset at)
    {
        int lower = 0;
        int upper = programmes.Length - 1;
        int result = -1;
        while (lower <= upper)
        {
            int middle = lower + ((upper - lower) / 2);
            if (programmes[middle].Start <= at)
            {
                result = middle;
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        return result;
    }

    private DateTimeOffset GetDayBoundary(DateOnly scheduleDate)
    {
        DateTime local = scheduleDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (_displayTimeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        TimeSpan offset = _displayTimeZone.IsAmbiguousTime(local)
            ? _displayTimeZone.GetAmbiguousTimeOffsets(local).Max()
            : _displayTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static XmlTvDocument MergeDocuments(
        List<SourceDocument> sourceDocuments,
        List<SourceFailure> failures)
    {
        Dictionary<string, EpgChannel> channels = new(StringComparer.Ordinal);
        List<Programme> programmes = [];
        HashSet<ProgrammeIdentity> programmeIdentities = [];
        List<XmlTvParseWarning> warnings = [];

        foreach (SourceDocument sourceDocument in sourceDocuments)
        {
            foreach (EpgChannel channel in sourceDocument.Document.Channels)
            {
                if (channels.TryGetValue(channel.Id, out EpgChannel? existing))
                {
                    channels[channel.Id] = MergeChannel(existing, channel);
                }
                else
                {
                    channels.Add(channel.Id, channel);
                }
            }

            foreach (Programme programme in sourceDocument.Document.Programmes)
            {
                ProgrammeIdentity identity = new(
                    programme.ChannelId,
                    programme.Start.UtcTicks,
                    programme.Stop?.UtcTicks,
                    programme.Title);
                if (programmeIdentities.Add(identity))
                {
                    programmes.Add(programme);
                }
            }

            warnings.AddRange(sourceDocument.Document.Warnings.Select(warning => new XmlTvParseWarning(
                warning.LineNumber,
                $"[{sourceDocument.Description}] {warning.Message}")));
        }

        warnings.AddRange(failures.Select(failure => new XmlTvParseWarning(
            null,
            $"Source '{failure.Description}' could not be loaded ({failure.ExceptionType}).")));

        XmlTvDocument first = sourceDocuments[0].Document;
        DateTimeOffset? generatedAt = sourceDocuments
            .Select(static item => item.Document.Metadata.GeneratedAt)
            .Where(static value => value.HasValue)
            .Max();
        XmlTvDocumentMetadata metadata = new(
            generatedAt,
            sourceDocuments.Count == 1 ? first.Metadata.SourceInfoUrl : null,
            sourceDocuments.Count == 1 ? first.Metadata.SourceInfoName : "Combined XMLTV sources",
            first.Metadata.GeneratorInfoName,
            first.Metadata.GeneratorInfoUrl);

        return new XmlTvDocument(
            metadata,
            channels.Values,
            programmes.OrderBy(static programme => programme.Start),
            warnings);
    }

    private static EpgChannel MergeChannel(EpgChannel first, EpgChannel second)
    {
        EpgLocalizedText[] names = first.DisplayNames
            .Concat(second.DisplayNames)
            .Distinct()
            .ToArray();
        EpgIcon[] icons = first.Icons
            .Concat(second.Icons)
            .Distinct()
            .ToArray();
        string[] urls = first.Urls
            .Concat(second.Urls)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new EpgChannel(first.Id, names, icons, urls);
    }

    private static bool OverlapsDay(
        Programme programme,
        DateTimeOffset? effectiveStop,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd) =>
        effectiveStop is DateTimeOffset stop
            ? programme.Start < dayEnd && stop > dayStart
            : programme.Start < dayEnd;

    private static bool ProgrammeTitleContains(Programme programme, string query)
    {
        string normalizedQuery = EpgNameNormalizer.Normalize(query);
        if (normalizedQuery.Length > 0)
        {
            return programme.Titles.Any(title => EpgNameNormalizer.Normalize(title.Value).Contains(
                normalizedQuery,
                StringComparison.Ordinal));
        }

        return programme.Titles.Any(title => CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            title.Value,
            query,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }

    private sealed class EpgSnapshot
    {
        private readonly Dictionary<string, Programme[]> _programmesByChannel;

        public EpgSnapshot(XmlTvDocument document)
        {
            Document = document;
            _programmesByChannel = document.Programmes
                .GroupBy(static programme => programme.ChannelId, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.OrderBy(static programme => programme.Start).ToArray(),
                    StringComparer.Ordinal);
        }

        public XmlTvDocument Document { get; }

        public ConcurrentDictionary<ChannelMappingCacheKey, ChannelMappingCacheEntry> MappingCache { get; } = new();

        public Programme[] GetProgrammes(string channelId) =>
            _programmesByChannel.TryGetValue(channelId, out Programme[]? programmes)
                ? programmes
                : [];
    }

    private readonly record struct ChannelMappingCacheKey(
        ChannelId ChannelId,
        string Name,
        string? TvgId,
        string? TvgName,
        int ManualMappingVersion);

    private sealed record ChannelMappingCacheEntry(ChannelEpgMapping? Mapping);

    private sealed record SourceDocument(string Description, XmlTvDocument Document);

    private sealed record SourceFailure(string Description, string ExceptionType);

    private readonly record struct ProgrammeIdentity(
        string ChannelId,
        long StartUtcTicks,
        long? StopUtcTicks,
        string Title);
}
