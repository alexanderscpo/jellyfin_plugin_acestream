namespace Jellyfin.Plugin.AceStream.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="IHttpClientFactory"/> that hands out a fresh
/// <see cref="HttpClient"/> built over the given handler on every call, recording how often
/// (and under which name) clients were requested. Lets us assert that an adapter resolves a
/// client per request instead of capturing one for its lifetime.
/// </summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

    public int CreateClientCallCount { get; private set; }

    public string? LastRequestedName { get; private set; }

    public HttpClient CreateClient(string name)
    {
        CreateClientCallCount++;
        LastRequestedName = name;
        return new HttpClient(_handler);
    }
}
