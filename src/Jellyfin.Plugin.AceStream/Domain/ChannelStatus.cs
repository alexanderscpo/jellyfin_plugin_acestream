namespace Jellyfin.Plugin.AceStream.Domain;

/// <summary>
/// Reliability of an AceStream channel as reported by the engine's search node.
/// </summary>
public enum ChannelStatus
{
    /// <summary>Status is unknown or not reported by the engine.</summary>
    Unknown = 0,

    /// <summary>Channel is reachable but unreliable ("yellow").</summary>
    Unreliable = 1,

    /// <summary>Channel is working ("green").</summary>
    Working = 2,
}

/// <summary>
/// Mapping helpers between raw engine status codes and <see cref="ChannelStatus"/>.
/// </summary>
public static class ChannelStatusExtensions
{
    /// <summary>
    /// Maps a raw engine <c>status</c> code to a <see cref="ChannelStatus"/>.
    /// Unknown codes map to <see cref="ChannelStatus.Unknown"/>.
    /// </summary>
    /// <param name="code">The raw engine status code (2 = working, 1 = unreliable).</param>
    /// <returns>The corresponding <see cref="ChannelStatus"/>.</returns>
    public static ChannelStatus ToChannelStatus(this int code) => code switch
    {
        1 => ChannelStatus.Unreliable,
        2 => ChannelStatus.Working,
        _ => ChannelStatus.Unknown,
    };
}
