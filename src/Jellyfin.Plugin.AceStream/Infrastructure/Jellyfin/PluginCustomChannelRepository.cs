using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// <see cref="ICustomChannelRepository"/> backed by the live plugin configuration's M3U playlist.
/// Re-parses on every call so changes saved in the settings page take effect without restart.
/// </summary>
public sealed class PluginCustomChannelRepository : ICustomChannelRepository
{
    /// <inheritdoc />
    public IReadOnlyList<CustomChannel> GetAll()
        => M3uPlaylistParser.Parse(Plugin.Instance?.Configuration.M3uPlaylist);
}
