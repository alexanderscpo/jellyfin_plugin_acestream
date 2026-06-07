namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

// Wire-format DTOs for the engine's /search response. Internal: they never leak past
// this adapter — they are mapped to the domain AceChannel at the boundary.

internal sealed class SearchResponseDto
{
    public SearchResultDto? Result { get; set; }
}

internal sealed class SearchResultDto
{
    public int Total { get; set; }

    public List<SearchGroupDto>? Results { get; set; }
}

internal sealed class SearchGroupDto
{
    public string? Name { get; set; }

    public List<SearchItemDto>? Items { get; set; }
}

internal sealed class SearchItemDto
{
    public string? Name { get; set; }

    public string? Infohash { get; set; }

    public List<string>? Categories { get; set; }

    public double Availability { get; set; }

    public long AvailabilityUpdatedAt { get; set; }

    public int Status { get; set; }

    public bool Disabled { get; set; }

    public int? ChannelId { get; set; }

    public List<string>? Countries { get; set; }

    public List<string>? Languages { get; set; }
}
