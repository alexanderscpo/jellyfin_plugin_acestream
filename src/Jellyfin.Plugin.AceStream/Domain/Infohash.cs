namespace Jellyfin.Plugin.AceStream.Domain;

/// <summary>
/// AceStream content identity: a 40-character SHA-1 infohash, normalized to lowercase.
/// This is the canonical identity of an AceStream channel and the single source of truth
/// used for equality across the domain.
/// </summary>
public sealed record Infohash
{
    private const int HashLength = 40;

    private Infohash(string value) => Value = value;

    /// <summary>
    /// Gets the normalized (lowercase, 40-character hexadecimal) infohash value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a validated <see cref="Infohash"/> from a raw string.
    /// </summary>
    /// <param name="value">A 40-character hexadecimal SHA-1 hash (case-insensitive).</param>
    /// <returns>A normalized <see cref="Infohash"/>.</returns>
    /// <exception cref="ArgumentException">The value is null, empty, the wrong length, or not hexadecimal.</exception>
    public static Infohash Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Infohash must not be null or empty.", nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length != HashLength)
        {
            throw new ArgumentException($"Infohash must be exactly {HashLength} characters.", nameof(value));
        }

        if (!IsHexadecimal(trimmed))
        {
            throw new ArgumentException("Infohash must contain only hexadecimal characters.", nameof(value));
        }

        return new Infohash(trimmed.ToLowerInvariant());
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsHexadecimal(string value)
    {
        foreach (var c in value)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }
}
