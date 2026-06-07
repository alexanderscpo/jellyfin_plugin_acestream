namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Formats a point in time relative to a reference "now" (e.g. "5m ago").
/// </summary>
internal static class RelativeTime
{
    /// <summary>
    /// Describes how long ago <paramref name="when"/> was, relative to <paramref name="now"/>.
    /// </summary>
    /// <param name="when">The past instant.</param>
    /// <param name="now">The reference instant.</param>
    /// <returns>A short human phrase such as "just now", "5m ago", "3h ago", or "2d ago".</returns>
    public static string Describe(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalHours < 1)
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta.TotalDays < 1)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        return $"{(int)delta.TotalDays}d ago";
    }
}
