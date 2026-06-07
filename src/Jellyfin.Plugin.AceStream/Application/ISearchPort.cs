namespace Jellyfin.Plugin.AceStream.Application;

/// <summary>
/// Port for discovering channels (search and browse). Implemented by an adapter that
/// talks to the AceStream engine's search node. Kept separate from playback (ISP).
/// </summary>
public interface ISearchPort
{
    /// <summary>
    /// Searches or browses the catalog.
    /// </summary>
    /// <param name="request">The search/browse parameters.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching channels for the requested page.</returns>
    Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}
