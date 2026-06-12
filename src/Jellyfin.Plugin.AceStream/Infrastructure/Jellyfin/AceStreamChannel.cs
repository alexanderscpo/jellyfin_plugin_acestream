using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// The Jellyfin channel for AceStream. The root lists category folders (from the engine's
/// <c>/search</c>) and, when configured, a "Custom" folder populated from the plugin's M3U
/// playlist. Playback media sources are resolved on demand (<see cref="IRequiresMediaInfoCallback"/>)
/// to the acexy proxy.
/// </summary>
/// <remarks>
/// AceLive entries (URLs ending with <c>.acelive</c>) are presented with an <c>acelive:</c>-prefixed
/// Jellyfin item Id whose body is SHA1(url). At playback, the Id is detected before the normal
/// infohash parse and the URL is resolved lazily via <see cref="IAceLiveResolver"/>.
/// </remarks>
public sealed class AceStreamChannel : IChannel, IRequiresMediaInfoCallback
{
    private const string CategoryPrefix = "category:";
    private const string CustomFolderId = "custom";
    private const string AceLiveIdPrefix = "acelive:";
    private const string DataVersionPrefix = "2-";
    private const int DefaultPageSize = 50;

    private readonly ISearchPort _searchPort;
    private readonly IProxySettings _proxySettings;
    private readonly IProbeSettings _probeSettings;
    private readonly IMediaSourceProbe _probe;
    private readonly ICustomChannelRepository _customChannels;
    private readonly IAceLiveEntrySource _aceLiveSource;
    private readonly IAceLiveResolver _resolver;
    private readonly ILogger<AceStreamChannel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AceStreamChannel"/> class.
    /// </summary>
    /// <param name="searchPort">The search port used to list a category's channels.</param>
    /// <param name="proxySettings">Provides the proxy base URL used to build playback sources.</param>
    /// <param name="probeSettings">Provides probe-related settings such as the ffprobe analyze duration.</param>
    /// <param name="probe">Probes the live stream so Jellyfin sees the real codecs.</param>
    /// <param name="customChannels">Provides user-defined channels from the M3U playlist config.</param>
    /// <param name="aceLiveSource">Provides pending AceLive entries for deferred resolution.</param>
    /// <param name="resolver">Resolves <c>.acelive</c> URLs to live infohashes with a TTL cache.</param>
    /// <param name="logger">The logger.</param>
    public AceStreamChannel(
        ISearchPort searchPort,
        IProxySettings proxySettings,
        IProbeSettings probeSettings,
        IMediaSourceProbe probe,
        ICustomChannelRepository customChannels,
        IAceLiveEntrySource aceLiveSource,
        IAceLiveResolver resolver,
        ILogger<AceStreamChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(searchPort);
        ArgumentNullException.ThrowIfNull(proxySettings);
        ArgumentNullException.ThrowIfNull(probeSettings);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(customChannels);
        ArgumentNullException.ThrowIfNull(aceLiveSource);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);
        _searchPort = searchPort;
        _proxySettings = proxySettings;
        _probeSettings = probeSettings;
        _probe = probe;
        _customChannels = customChannels;
        _aceLiveSource = aceLiveSource;
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "AceStream";

    /// <inheritdoc />
    public string Description => "Browse, search and play AceStream channels.";

    /// <inheritdoc />
    /// <remarks>
    /// Jellyfin only re-enumerates a channel's items when this value changes, so it must reflect
    /// both the custom-channel set and the pending AceLive entries: editing the M3U playlist in
    /// settings changes the hash, which forces the "Custom" folder to rebuild on the next browse.
    /// Resolved infohashes are NOT part of DataVersion (runtime state, not config).
    /// </remarks>
    public string DataVersion
    {
        get
        {
            var channels = _customChannels.GetAll();
            var aceLiveEntries = _aceLiveSource.GetAceLiveEntries();

            var lines = channels
                .Select(c => c.Infohash.Value + '|' + c.Name)
                .Concat(aceLiveEntries.Select(e => AceLiveIdPrefix + e.Url + '|' + e.Name));

            var payload = string.Join('\n', lines);
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(payload));
            return DataVersionPrefix + Convert.ToHexString(hash);
        }
    }

    /// <inheritdoc />
    public string HomePageUrl => "https://acestream.org";

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        ContentTypes = new List<ChannelMediaContentType> { ChannelMediaContentType.TvExtra },
        MediaTypes = new List<ChannelMediaType> { ChannelMediaType.Video },
    };

    /// <inheritdoc />
    public bool IsEnabledFor(string userId) => true;

    /// <inheritdoc />
    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrEmpty(query.FolderId))
        {
            return BuildRootFolders();
        }

        if (query.FolderId == CustomFolderId)
        {
            return BuildCustomItems();
        }

        if (query.FolderId.StartsWith(CategoryPrefix, StringComparison.Ordinal))
        {
            return await BuildCategoryItems(query, cancellationToken).ConfigureAwait(false);
        }

        return new ChannelItemResult();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        // A null/blank id is unplayable; degrade to empty like every other failure path here
        // (and so the StartsWith below is safe).
        if (string.IsNullOrWhiteSpace(id))
        {
            return Enumerable.Empty<MediaSourceInfo>();
        }

        if (id.StartsWith(CategoryPrefix, StringComparison.Ordinal))
        {
            return Enumerable.Empty<MediaSourceInfo>();
        }

        var proxyBaseUrl = _proxySettings.BaseUrl;
        if (string.IsNullOrWhiteSpace(proxyBaseUrl))
        {
            return Enumerable.Empty<MediaSourceInfo>();
        }

        // AceLive path: acelive: prefix detected BEFORE normal infohash parse.
        if (id.StartsWith(AceLiveIdPrefix, StringComparison.Ordinal))
        {
            return await GetAceLiveMediaInfo(id, proxyBaseUrl, cancellationToken).ConfigureAwait(false);
        }

        if (!TryParseInfohash(id, out var infohash))
        {
            return Enumerable.Empty<MediaSourceInfo>();
        }

        // Probe the live stream so Jellyfin sees the real codecs and remuxes instead of
        // re-encoding a stream of unknown codecs (which fails decoding a mid-GOP join).
        var source = ProxyMediaSource.Build(proxyBaseUrl, infohash, _probeSettings.ProbeAnalyzeDurationMs);
        await _probe.EnrichAsync(source, cancellationToken).ConfigureAwait(false);
        return new[] { source };
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();

    // ── AceLive resolution ────────────────────────────────────────────────────

    private async Task<IEnumerable<MediaSourceInfo>> GetAceLiveMediaInfo(
        string id,
        string proxyBaseUrl,
        CancellationToken cancellationToken)
    {
        var url = LookupUrlById(id);
        if (url is null)
        {
            _logger.LogWarning("AceLive item {Id} not found in current playlist; returning empty.", id);
            return Enumerable.Empty<MediaSourceInfo>();
        }

        var infohash = await _resolver.ResolveAsync(url, cancellationToken).ConfigureAwait(false);
        if (infohash is null)
        {
            _logger.LogWarning("AceLive URL {Url} could not be resolved; returning empty.", url);
            return Enumerable.Empty<MediaSourceInfo>();
        }

        var source = ProxyMediaSource.Build(proxyBaseUrl, infohash, _probeSettings.ProbeAnalyzeDurationMs);
        await _probe.EnrichAsync(source, cancellationToken).ConfigureAwait(false);

        // If the probe pipeline returned no media streams, the resolved infohash may be stale
        // (broadcast rotation). Evict it and re-resolve once to recover.
        if (source.MediaStreams is null || source.MediaStreams.Count == 0)
        {
            _logger.LogDebug("AceLive URL {Url} yielded empty streams after enrich; evicting and re-resolving.", url);
            _resolver.Evict(url);

            var retryInfohash = await _resolver.ResolveAsync(url, cancellationToken).ConfigureAwait(false);
            if (retryInfohash is null)
            {
                _logger.LogWarning("AceLive URL {Url} re-resolution returned null; returning empty.", url);
                return Enumerable.Empty<MediaSourceInfo>();
            }

            source = ProxyMediaSource.Build(proxyBaseUrl, retryInfohash, _probeSettings.ProbeAnalyzeDurationMs);
            await _probe.EnrichAsync(source, cancellationToken).ConfigureAwait(false);
        }

        return new[] { source };
    }

    /// <summary>
    /// Looks up the original <c>.acelive</c> URL for a given <c>acelive:</c>-prefixed item id by
    /// recomputing the id from each current AceLive entry and matching. The same hash helper used
    /// by <see cref="BuildAceLiveItems"/> ensures the id and the lookup never diverge.
    /// </summary>
    private string? LookupUrlById(string id)
    {
        foreach (var entry in _aceLiveSource.GetAceLiveEntries())
        {
            if (ComputeAceLiveId(entry.Url) == id)
            {
                return entry.Url;
            }
        }

        return null;
    }

    // ── Item helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the stable, bounded Jellyfin item Id for a <c>.acelive</c> URL.
    /// Format: <c>acelive:&lt;40-hex SHA1(url)&gt;</c> — prefixed to avoid collision with the
    /// 40-hex infohash namespace that the normal path accepts.
    /// </summary>
    private static string ComputeAceLiveId(string url)
        => AceLiveIdPrefix + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

    private static bool TryParseInfohash(string id, out Infohash infohash)
    {
        try
        {
            infohash = Infohash.Create(id);
            return true;
        }
        catch (ArgumentException)
        {
            infohash = null!;
            return false;
        }
    }

    private ChannelItemResult BuildRootFolders()
    {
        var items = AceCategories.Browseable
            .Select(category => new ChannelItemInfo
            {
                Id = CategoryPrefix + category.Key,
                Name = category.Display,
                Type = ChannelItemType.Folder,
            })
            .ToList();

        var customChannelCount = _customChannels.GetAll().Count;
        var aceLiveCount = _aceLiveSource.GetAceLiveEntries().Count;

        if (customChannelCount > 0 || aceLiveCount > 0)
        {
            _logger.LogDebug(
                "AceStream serving {ChannelCount} custom channel(s) and {AceLiveCount} AceLive entry/ies from the M3U playlist.",
                customChannelCount,
                aceLiveCount);
            items.Add(new ChannelItemInfo
            {
                Id = CustomFolderId,
                Name = "Custom",
                Type = ChannelItemType.Folder,
            });
        }

        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private ChannelItemResult BuildCustomItems()
    {
        var channelItems = _customChannels.GetAll().Select(c => new ChannelItemInfo
        {
            Id = c.Infohash.Value,
            Name = c.Name,
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.TvExtra,
            IsLiveStream = true,
        });

        var aceLiveItems = BuildAceLiveItems();

        var items = channelItems.Concat(aceLiveItems).ToList();
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private IEnumerable<ChannelItemInfo> BuildAceLiveItems()
        => _aceLiveSource.GetAceLiveEntries().Select(entry => new ChannelItemInfo
        {
            Id = ComputeAceLiveId(entry.Url),
            Name = entry.Name,
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.TvExtra,
            IsLiveStream = true,
        });

    private async Task<ChannelItemResult> BuildCategoryItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var category = query.FolderId![CategoryPrefix.Length..];
        var pageSize = query.Limit is > 0 ? query.Limit.Value : DefaultPageSize;
        var page = pageSize > 0 ? (query.StartIndex ?? 0) / pageSize : 0;

        var result = await _searchPort
            .SearchAsync(new SearchRequest { Category = category, Page = page, PageSize = pageSize }, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var items = result.Channels.Select(channel => ChannelItemMapper.ToChannelItemInfo(channel, now)).ToList();

        return new ChannelItemResult { Items = items, TotalRecordCount = result.Total };
    }
}
