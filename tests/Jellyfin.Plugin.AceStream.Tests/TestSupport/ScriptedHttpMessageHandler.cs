namespace Jellyfin.Plugin.AceStream.Tests.TestSupport;

/// <summary>
/// Test double that delegates each request to a supplied async function, so a test can model any
/// behavior — a normal response, an empty body, a hang that honors cancellation, or a thrown
/// exception — without a network.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public ScriptedHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        => _responder = responder;

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        return _responder(request, cancellationToken);
    }
}
