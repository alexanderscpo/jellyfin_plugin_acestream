using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// <see cref="ICustomChannelRepository"/> backed by the live plugin configuration's M3U playlist.
/// Parses on the first call and whenever the raw playlist string changes; repeated reads with the
/// same content are served from cache via <see cref="M3uParseCache"/>.
/// </summary>
public sealed class PluginCustomChannelRepository : ICustomChannelRepository
{
    private readonly M3uParseCache _cache = new();

    /// <inheritdoc />
    public IReadOnlyList<CustomChannel> GetAll()
        => _cache.GetOrParse(Plugin.Instance?.Configuration.M3uPlaylist);
}
