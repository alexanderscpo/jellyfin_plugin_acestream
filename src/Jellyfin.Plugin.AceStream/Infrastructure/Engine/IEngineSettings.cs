namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// Provides the current engine base URL. Read per request so configuration changes
/// take effect without rebuilding long-lived consumers (e.g. the singleton channel).
/// </summary>
public interface IEngineSettings
{
    /// <summary>
    /// Gets the engine base URL (e.g. <c>http://engine:6878</c>).
    /// </summary>
    string BaseUrl { get; }
}
