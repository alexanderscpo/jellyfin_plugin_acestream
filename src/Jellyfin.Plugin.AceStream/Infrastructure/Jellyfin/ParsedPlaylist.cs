using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// The result of parsing an M3U playlist: resolved <see cref="CustomChannel"/> entries (those
/// with <c>acestream://</c> URIs) and unresolved <see cref="AceLiveEntry"/> entries (those whose
/// URL path ends with <c>.acelive</c>).
/// </summary>
/// <param name="Channels">
/// <see cref="CustomChannel"/> instances parsed from valid <c>acestream://</c> lines.
/// Never null; may be empty.
/// </param>
/// <param name="AceLive">
/// <see cref="AceLiveEntry"/> instances parsed from <c>http(s)://…*.acelive</c> lines.
/// Never null; may be empty.
/// </param>
public sealed record ParsedPlaylist(
    IReadOnlyList<CustomChannel> Channels,
    IReadOnlyList<AceLiveEntry> AceLive);
