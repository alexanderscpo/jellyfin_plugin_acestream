namespace Jellyfin.Plugin.AceStream.Domain;

/// <summary>
/// Channel availability reported by the engine: a fraction between 0.0 (unavailable)
/// and 1.0 (fully available).
/// </summary>
public sealed record Availability
{
    private Availability(double value) => Value = value;

    /// <summary>
    /// Gets the availability fraction in the inclusive range [0.0, 1.0].
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Creates a validated <see cref="Availability"/>.
    /// </summary>
    /// <param name="value">A fraction in the inclusive range [0.0, 1.0].</param>
    /// <returns>A validated <see cref="Availability"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or outside [0.0, 1.0].</exception>
    public static Availability Create(double value)
    {
        if (double.IsNaN(value) || value < 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Availability must be within [0.0, 1.0].");
        }

        return new Availability(value);
    }
}
