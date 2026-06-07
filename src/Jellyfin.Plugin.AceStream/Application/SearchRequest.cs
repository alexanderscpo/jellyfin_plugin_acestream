namespace Jellyfin.Plugin.AceStream.Application;

/// <summary>
/// A request to search or browse the AceStream catalog. Browsing is just a search
/// scoped by <see cref="Category"/>; free-text search uses <see cref="Query"/>.
/// </summary>
public sealed record SearchRequest
{
    /// <summary>Gets the free-text search term, if any.</summary>
    public string? Query { get; init; }

    /// <summary>Gets the category filter, if any.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the zero-based page index.</summary>
    public int Page { get; init; }

    /// <summary>Gets the requested page size (the engine caps this at 200).</summary>
    public int PageSize { get; init; } = 10;
}
