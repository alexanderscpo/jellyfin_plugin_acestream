using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Application;

/// <summary>
/// The outcome of a search/browse: the total number of matches reported by the engine
/// and the channels on the current page (flattened from the engine's grouped results).
/// </summary>
/// <param name="Total">The total number of matches across all pages.</param>
/// <param name="Channels">The channels on the current page.</param>
public sealed record SearchResult(int Total, IReadOnlyList<AceChannel> Channels);
