using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;
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
public sealed class AceStreamChannel : IChannel, IRequiresMediaInfoCallback
{
    private const string CategoryPrefix = "category:";
    private const string CustomFolderId = "custom";
    private const int DefaultPageSize = 50;

    private readonly ISearchPort _searchPort;
    private readonly IProxySettings _proxySettings;
    private readonly IMediaSourceProbe _probe;
    private readonly ICustomChannelRepository _customChannels;
    private readonly ILogger<AceStreamChannel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AceStreamChannel"/> class.
    /// </summary>
    /// <param name="searchPort">The search port used to list a category's channels.</param>
    /// <param name="proxySettings">Provides the proxy base URL used to build playback sources.</param>
    /// <param name="probe">Probes the live stream so Jellyfin sees the real codecs.</param>
    /// <param name="customChannels">Provides user-defined channels from the M3U playlist config.</param>
    /// <param name="logger">The logger.</param>
    public AceStreamChannel(ISearchPort searchPort, IProxySettings proxySettings, IMediaSourceProbe probe, ICustomChannelRepository customChannels, ILogger<AceStreamChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(searchPort);
        ArgumentNullException.ThrowIfNull(proxySettings);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(customChannels);
        ArgumentNullException.ThrowIfNull(logger);
        _searchPort = searchPort;
        _proxySettings = proxySettings;
        _probe = probe;
        _customChannels = customChannels;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "AceStream";

    /// <inheritdoc />
    public string Description => "Browse, search and play AceStream channels.";

    /// <inheritdoc />
    /// <remarks>
    /// Jellyfin only re-enumerates a channel's items when this value changes, so it must reflect
    /// the custom-channel set: editing the M3U playlist in settings changes the hash, which forces
    /// the "Custom" folder to rebuild on the next browse.
    /// </remarks>
    public string DataVersion
    {
        get
        {
            var payload = string.Join('\n', _customChannels.GetAll().Select(c => c.Infohash.Value + '|' + c.Name));
            var hash = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
            return "2-" + Convert.ToHexString(hash);
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

        if (!TryParseInfohash(id, out var infohash))
        {
            return Enumerable.Empty<MediaSourceInfo>();
        }

        // Probe the live stream so Jellyfin sees the real codecs and remuxes instead of
        // re-encoding a stream of unknown codecs (which fails decoding a mid-GOP join).
        var source = ProxyMediaSource.Build(proxyBaseUrl, infohash, _proxySettings.ProbeAnalyzeDurationMs);
        await _probe.EnrichAsync(source, cancellationToken).ConfigureAwait(false);
        return new[] { source };
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();

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

        var customCount = _customChannels.GetAll().Count;
        if (customCount > 0)
        {
            _logger.LogDebug("AceStream serving {Count} custom channel(s) from the M3U playlist.", customCount);
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
        var channels = _customChannels.GetAll();
        var items = channels.Select(c => new ChannelItemInfo
        {
            Id = c.Infohash.Value,
            Name = c.Name,
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.TvExtra,
            IsLiveStream = true,
        }).ToList();

        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

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
