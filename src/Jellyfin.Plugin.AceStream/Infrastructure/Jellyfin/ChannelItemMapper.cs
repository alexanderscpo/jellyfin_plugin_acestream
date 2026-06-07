using Jellyfin.Plugin.AceStream.Domain;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Maps a domain <see cref="AceChannel"/> to a Jellyfin <see cref="ChannelItemInfo"/>.
/// The item id is the infohash, giving every channel a stable identity so Jellyfin's
/// native favorites/user-data attach correctly across refreshes.
/// </summary>
public static class ChannelItemMapper
{
    /// <summary>
    /// Projects an <see cref="AceChannel"/> into a Jellyfin channel item.
    /// </summary>
    /// <param name="channel">The domain channel.</param>
    /// <param name="now">The reference time used to describe availability freshness.</param>
    /// <returns>The Jellyfin channel item projection.</returns>
    public static ChannelItemInfo ToChannelItemInfo(AceChannel channel, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return new ChannelItemInfo
        {
            Id = channel.Infohash.Value,
            Name = channel.Name,
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.TvExtra,
            IsLiveStream = true,
            Overview = BuildOverview(channel, now),
            Tags = channel.Categories.ToList(),
        };
    }

    private static string BuildOverview(AceChannel channel, DateTimeOffset now)
    {
        var parts = new List<string>
        {
            $"Status: {channel.Status}",
            $"Availability: {(int)Math.Round(channel.Availability.Value * 100)}%",
        };

        if (channel.AvailabilityUpdatedAt is { } updatedAt)
        {
            parts.Add($"Verified {RelativeTime.Describe(updatedAt, now)}");
        }

        if (channel.Countries.Count > 0)
        {
            parts.Add($"Countries: {string.Join(", ", channel.Countries)}");
        }

        if (channel.Languages.Count > 0)
        {
            parts.Add($"Languages: {string.Join(", ", channel.Languages)}");
        }

        return string.Join(" · ", parts);
    }
}
