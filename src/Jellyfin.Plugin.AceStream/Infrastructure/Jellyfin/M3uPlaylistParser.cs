using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Parses an M3U playlist that uses <c>acestream://</c> URIs into <see cref="CustomChannel"/> instances.
/// Supports the standard <c>#EXTINF</c> header with optional attributes; the display name is taken
/// from the portion after the last comma. Lines with non-AceStream URLs are skipped. Invalid
/// infohashes are silently ignored.
/// </summary>
public static class M3uPlaylistParser
{
    private const string AcestreamScheme = "acestream://";
    private const string ExtInfPrefix = "#EXTINF:";

    /// <summary>
    /// Parses <paramref name="content"/> and returns all valid AceStream channels found.
    /// </summary>
    /// <param name="content">Raw M3U playlist text. Null or whitespace returns an empty list.</param>
    public static IReadOnlyList<CustomChannel> Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<CustomChannel>();
        }

        var result = new List<CustomChannel>();
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
                    result.Add(new CustomChannel(name, infohash));
                }
                catch (ArgumentException)
                {
                    // skip invalid hash
                }

                pendingName = null;
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                // Non-comment, non-acestream URL — reset so it doesn't leak into the next entry.
                pendingName = null;
            }
        }

        return result;
    }
}
