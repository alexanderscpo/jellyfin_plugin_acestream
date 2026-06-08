using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Application;

/// <summary>
/// Port that provides the user-defined channels from the plugin's M3U playlist configuration.
/// </summary>
public interface ICustomChannelRepository
{
    /// <summary>
    /// Returns all valid custom channels from the current plugin configuration.
    /// Invalid entries (malformed infohash, missing name) are silently skipped.
    /// </summary>
    IReadOnlyList<CustomChannel> GetAll();
}
