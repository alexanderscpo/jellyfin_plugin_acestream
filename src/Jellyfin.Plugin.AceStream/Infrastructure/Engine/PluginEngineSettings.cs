namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// <see cref="IEngineSettings"/> backed by the live plugin configuration.
/// </summary>
public sealed class PluginEngineSettings : IEngineSettings
{
    /// <inheritdoc />
    public string BaseUrl => Plugin.Instance?.Configuration.EngineUrl ?? string.Empty;

    /// <inheritdoc />
    public int ReadinessTimeoutSeconds => Plugin.Instance?.Configuration.ReadinessTimeoutSeconds ?? 30;
}
