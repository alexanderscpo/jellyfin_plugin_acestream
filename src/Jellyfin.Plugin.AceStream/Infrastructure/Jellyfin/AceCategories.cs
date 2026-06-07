namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// The AceStream categories surfaced as browse folders, with display names.
/// Adult categories (erotic_18_plus, other_18_plus, webcam, amateur) are intentionally
/// excluded from the default browse.
/// </summary>
internal static class AceCategories
{
    /// <summary>
    /// Gets the browseable categories as (engine key, display name) pairs.
    /// </summary>
    public static IReadOnlyList<(string Key, string Display)> Browseable { get; } = new[]
    {
        ("tv", "TV"),
        ("movies", "Movies"),
        ("sport", "Sport"),
        ("documentaries", "Documentaries"),
        ("music", "Music"),
        ("informational", "News & Info"),
        ("entertaining", "Entertainment"),
        ("educational", "Educational"),
        ("regional", "Regional"),
        ("ethnic", "Ethnic"),
        ("religion", "Religion"),
        ("fashion", "Fashion"),
        ("cyber_games", "Cyber Games"),
    };
}
