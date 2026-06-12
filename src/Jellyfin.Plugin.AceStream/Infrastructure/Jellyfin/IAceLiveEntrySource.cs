namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Infrastructure-only port that provides the list of pending AceLive entries from the current
/// M3U playlist. Kept separate from <see cref="Application.ICustomChannelRepository"/> (ISP):
/// the Application port returns only domain entities; the acelive side-channel lives entirely
/// in Infrastructure where <see cref="AceStreamChannel"/> already depends on concrete plugin types.
/// </summary>
public interface IAceLiveEntrySource
{
    /// <summary>
    /// Returns all pending <see cref="AceLiveEntry"/> instances from the current M3U playlist.
    /// Never null; may be empty.
    /// </summary>
    IReadOnlyList<AceLiveEntry> GetAceLiveEntries();
}
