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

