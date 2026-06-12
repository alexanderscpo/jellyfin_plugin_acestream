namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Caches the parsed result of an M3U playlist string so repeated reads with the same playlist
/// content do not re-parse or re-hash. The cache holds exactly one entry: the last playlist string
/// and its <see cref="ParsedPlaylist"/>. When the raw string changes (the user edits the playlist
/// in settings), the cache is invalidated automatically and the new content is re-parsed on the
/// next call.
/// </summary>
/// <remarks>
/// Thread-safety: <c>volatile</c> snapshot reference ensures readers see a consistent entry
/// without locking (last-write-wins; acceptable for config-reload scenarios where two concurrent
/// writes to the same new value are idempotent).
/// </remarks>
internal sealed class M3uParseCache
{
    private readonly Func<string?, ParsedPlaylist> _parser;
    private volatile CacheEntry? _entry;

    /// <summary>
    /// Initializes a new instance of the <see cref="M3uParseCache"/> class.
    /// </summary>
    /// <param name="parser">The parser delegate; defaults to <see cref="M3uPlaylistParser.Parse"/>.</param>
    public M3uParseCache(Func<string?, ParsedPlaylist>? parser = null)
    {
        _parser = parser ?? M3uPlaylistParser.Parse;
    }

    /// <summary>
    /// Returns the cached <see cref="ParsedPlaylist"/> for <paramref name="rawPlaylist"/>,
    /// re-parsing only when the raw string has changed since the last call.
    /// </summary>
    /// <param name="rawPlaylist">The raw M3U playlist text from the plugin configuration.</param>
    /// <returns>The parsed <see cref="ParsedPlaylist"/> (empty if the playlist is null/blank).</returns>
    public ParsedPlaylist GetOrParse(string? rawPlaylist)
    {
        if (string.IsNullOrEmpty(rawPlaylist))
        {
            return new ParsedPlaylist(Array.Empty<Domain.CustomChannel>(), Array.Empty<AceLiveEntry>());
        }

        var snapshot = _entry;
        if (snapshot is not null && ReferenceEquals(snapshot.RawPlaylist, rawPlaylist))
        {
            return snapshot.Playlist;
        }

        // Content may be the same even if the reference changed (e.g. config reloaded but user
        // did not actually edit the field). Compare by value so we skip the parse in that case too.
        if (snapshot is not null && string.Equals(snapshot.RawPlaylist, rawPlaylist, StringComparison.Ordinal))
        {
            return snapshot.Playlist;
        }

        var playlist = _parser(rawPlaylist);
        _entry = new CacheEntry(rawPlaylist, playlist);
        return playlist;
    }

    private sealed record CacheEntry(string RawPlaylist, ParsedPlaylist Playlist);
}
