using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// Maps raw engine status codes (wire format) to the domain <see cref="ChannelStatus"/> enum.
/// This knowledge belongs in the Infrastructure layer: only the engine adapter knows the
/// numeric codes emitted by the engine's JSON API.
/// </summary>
internal static class EngineStatusMapper
{
    /// <summary>
    /// Maps a raw engine <c>status</c> code to a <see cref="ChannelStatus"/>.
    /// Unknown codes map to <see cref="ChannelStatus.Unknown"/>.
    /// </summary>
    /// <param name="code">The raw engine status code (2 = working, 1 = unreliable).</param>
    /// <returns>The corresponding <see cref="ChannelStatus"/>.</returns>
    public static ChannelStatus Map(int code) => code switch
    {
        1 => ChannelStatus.Unreliable,
        2 => ChannelStatus.Working,
        _ => ChannelStatus.Unknown,
    };
}
