using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Parses an M3U playlist that uses <c>acestream://</c> URIs or <c>.acelive</c> transport-file
/// URLs. Returns a <see cref="ParsedPlaylist"/> containing resolved <see cref="CustomChannel"/>
/// entries and unresolved <see cref="AceLiveEntry"/> entries for deferred resolution at playback.
/// Supports the standard <c>#EXTINF</c> header with optional attributes; the display name is taken
/// from the portion after the last comma. Lines with other URLs are skipped. Invalid infohashes are
/// silently ignored.
/// </summary>
public static class M3uPlaylistParser
{
    private const string AcestreamScheme = "acestream://";
    private const string ExtInfPrefix = "#EXTINF:";
    private const string HttpScheme = "http://";
    private const string HttpsScheme = "https://";
    private const string AceLiveExtension = ".acelive";

    private static readonly ParsedPlaylist EmptyResult = new(
        Array.Empty<CustomChannel>(),
        Array.Empty<AceLiveEntry>());

    /// <summary>
    /// Parses <paramref name="content"/> and returns all valid AceStream channels and pending
    /// AceLive entries found.
    /// </summary>
    /// <param name="content">Raw M3U playlist text. Null or whitespace returns an empty result.</param>
    public static ParsedPlaylist Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return EmptyResult;
        }

        var channels = new List<CustomChannel>();
        var aceLive = new List<AceLiveEntry>();
        string? pendingName = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith(ExtInfPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var comma = line.LastIndexOf(',');
                pendingName = comma >= 0 && comma < line.Length - 1
                    ? line[(comma + 1)..].Trim()
                    : null;
            }
            else if (line.StartsWith(AcestreamScheme, StringComparison.OrdinalIgnoreCase))
            {
                var hashStr = line[AcestreamScheme.Length..].Trim();
                try
                {
                    var infohash = Infohash.Create(hashStr);
                    var name = string.IsNullOrEmpty(pendingName) ? infohash.Value : pendingName;
                    channels.Add(new CustomChannel(name, infohash));
                }
                catch (ArgumentException)
                {
                    // skip invalid hash
                }

                pendingName = null;
            }
            else if (IsAceLiveUrl(line))
            {
                var name = string.IsNullOrEmpty(pendingName)
                    ? DeriveNameFromUrl(line)
                    : pendingName;
                aceLive.Add(new AceLiveEntry(name, line));
                pendingName = null;
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                // Non-comment, non-acestream, non-acelive URL — reset pending name so it does
                // not leak into the next entry.
                pendingName = null;
            }
        }

        return new ParsedPlaylist(channels, aceLive);
    }

    private static bool IsAceLiveUrl(string line)
        => (line.StartsWith(HttpScheme, StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith(HttpsScheme, StringComparison.OrdinalIgnoreCase)) &&
           line.EndsWith(AceLiveExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Derives a display name from a URL when no <c>#EXTINF</c> name is available.
    /// Uses the last path segment, stripping the <c>.acelive</c> extension; falls back to the
    /// full URL if the path segment is empty.
    /// </summary>
    private static string DeriveNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            if (segments.Length > 0)
            {
                var last = segments[^1].TrimEnd('/');
                if (last.EndsWith(AceLiveExtension, StringComparison.OrdinalIgnoreCase))
                {
                    last = last[..^AceLiveExtension.Length];
                }

                if (!string.IsNullOrEmpty(last))
                {
                    return last;
                }
            }
        }
        catch (UriFormatException)
        {
            // fall through to full URL
        }

        return url;
    }
}
