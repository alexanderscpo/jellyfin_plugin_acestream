namespace Jellyfin.Plugin.AceStream.Domain;

/// <summary>
/// A user-defined AceStream channel identified by infohash, stored in the plugin's M3U playlist.
/// </summary>
public sealed record CustomChannel(string Name, Infohash Infohash);
