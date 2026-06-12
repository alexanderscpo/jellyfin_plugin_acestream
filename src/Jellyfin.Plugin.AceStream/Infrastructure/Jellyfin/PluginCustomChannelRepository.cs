using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// <see cref="ICustomChannelRepository"/> backed by the live plugin configuration's M3U playlist.
/// Parses on the first call and whenever the raw playlist string changes; repeated reads with the
/// same content are served from cache via <see cref="M3uParseCache"/>.
/// </summary>
/// <remarks>
/// Exposes an Infrastructure-only accessor <see cref="GetAceLiveEntries"/> consumed directly by
/// <see cref="AceStreamChannel"/>, keeping the <see cref="ICustomChannelRepository"/> port clean
/// (ISP: the Application port returns only domain entities; the acelive side-channel lives in
/// Infrastructure).
/// </remarks>
public sealed class PluginCustomChannelRepository : ICustomChannelRepository, IAceLiveEntrySource
{
    private readonly M3uParseCache _cache = new();

    /// <inheritdoc />
    public IReadOnlyList<CustomChannel> GetAll()
        => _cache.GetOrParse(Plugin.Instance?.Configuration.M3uPlaylist).Channels;

    /// <summary>
    /// Returns all pending AceLive entries parsed from the current M3U playlist.
    /// This accessor is an Infrastructure-only side-channel; it is NOT part of
    /// <see cref="ICustomChannelRepository"/> so the Application port stays pure.
    /// </summary>
    public IReadOnlyList<AceLiveEntry> GetAceLiveEntries()
        => _cache.GetOrParse(Plugin.Instance?.Configuration.M3uPlaylist).AceLive;
}
