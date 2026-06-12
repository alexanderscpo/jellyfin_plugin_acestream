namespace Jellyfin.Plugin.AceStream.Domain;

/// <summary>
/// A searchable/playable AceStream channel. This is the canonical domain entity;
/// its identity is its <see cref="Infohash"/> (two channels with the same infohash are
/// the same channel regardless of any other field).
/// </summary>
public sealed class AceChannel : IEquatable<AceChannel>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AceChannel"/> class.
    /// </summary>
    /// <param name="infohash">The channel identity.</param>
    /// <param name="name">The display name (required, non-blank).</param>
    /// <param name="status">The reliability status.</param>
    /// <param name="availability">The availability fraction.</param>
    /// <param name="categories">The category tags (e.g. "tv"). Never null.</param>
    /// <param name="disabled">Whether the engine marked the channel as disabled.</param>
    /// <param name="channelId">The optional engine channel id.</param>
    /// <param name="countries">The optional country codes.</param>
    /// <param name="languages">The optional language codes.</param>
    /// <param name="availabilityUpdatedAt">When the engine last verified availability, if known.</param>
    /// <exception cref="ArgumentNullException">A required reference argument is null.</exception>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public AceChannel(
        Infohash infohash,
        string name,
        ChannelStatus status,
        Availability availability,
        IReadOnlyList<string> categories,
        bool disabled,
        int? channelId = null,
        IReadOnlyList<string>? countries = null,
        IReadOnlyList<string>? languages = null,
        DateTimeOffset? availabilityUpdatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(infohash);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(categories);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Channel name must not be blank.", nameof(name));
        }

        Infohash = infohash;
        Name = name;
        Status = status;
        Availability = availability;
        Categories = categories;
        Disabled = disabled;
        ChannelId = channelId;
        Countries = countries ?? Array.Empty<string>();
        Languages = languages ?? Array.Empty<string>();
        AvailabilityUpdatedAt = availabilityUpdatedAt;
    }

    /// <summary>Gets the channel identity.</summary>
    public Infohash Infohash { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets the reliability status.</summary>
    public ChannelStatus Status { get; }

    /// <summary>Gets the availability fraction.</summary>
    public Availability Availability { get; }

    /// <summary>Gets the category tags.</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>Gets a value indicating whether the engine marked the channel as disabled.</summary>
    public bool Disabled { get; }

    /// <summary>Gets the optional engine channel id.</summary>
    public int? ChannelId { get; }

    /// <summary>Gets the country codes (empty when not reported).</summary>
    public IReadOnlyList<string> Countries { get; }

    /// <summary>Gets the language codes (empty when not reported).</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>Gets when the engine last verified availability, if known.</summary>
    public DateTimeOffset? AvailabilityUpdatedAt { get; }

    /// <summary>
    /// Gets a value indicating whether the channel is reliable enough to offer for playback:
    /// not disabled and reported as working.
    /// </summary>
    public bool IsReliable => !Disabled && Status == ChannelStatus.Working;

    /// <summary>
    /// Returns <see langword="true"/> when both instances refer to the same infohash identity, or
    /// both are <see langword="null"/>. Consistent with <see cref="Equals(AceChannel?)"/>.
    /// </summary>
    public static bool operator ==(AceChannel? left, AceChannel? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>
    /// Returns <see langword="true"/> when the two instances do not share the same infohash identity.
    /// Consistent with <see cref="Equals(AceChannel?)"/>.
    /// </summary>
    public static bool operator !=(AceChannel? left, AceChannel? right) => !(left == right);

    /// <inheritdoc />
    public bool Equals(AceChannel? other) => other is not null && Infohash.Equals(other.Infohash);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as AceChannel);

    /// <inheritdoc />
    public override int GetHashCode() => Infohash.GetHashCode();
}
