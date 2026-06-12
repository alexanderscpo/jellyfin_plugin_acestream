namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// An unresolved <c>.acelive</c> transport-file entry parsed from the M3U playlist.
/// Carries only the display name and the raw URL; resolution to an <see cref="Domain.Infohash"/>
/// is deferred to playback via <see cref="Engine.IAceLiveResolver"/>.
/// </summary>
/// <param name="Name">The display name from the preceding <c>#EXTINF</c> tag, or derived from the URL.</param>
/// <param name="Url">The raw <c>.acelive</c> transport-file URL.</param>
public sealed record AceLiveEntry(string Name, string Url);
