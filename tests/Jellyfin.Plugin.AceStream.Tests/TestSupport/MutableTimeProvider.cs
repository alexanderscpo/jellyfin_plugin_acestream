namespace Jellyfin.Plugin.AceStream.Tests.TestSupport;

/// <summary>
/// A <see cref="TimeProvider"/> whose "now" is set by the test, so TTL expiry can be exercised
/// deterministically without real waiting.
/// </summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
