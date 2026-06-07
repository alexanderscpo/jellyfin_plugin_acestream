using System.Net;
using System.Text;

namespace Jellyfin.Plugin.AceStream.Tests.TestSupport;

/// <summary>
/// Test double that returns a canned JSON body and records the last request URI,
/// so HTTP adapters can be unit-tested without a network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly string _json;
    private readonly HttpStatusCode _statusCode;

    public StubHttpMessageHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _json = json;
        _statusCode = statusCode;
    }

    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;

        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_json, Encoding.UTF8, "application/json"),
        };

        return Task.FromResult(response);
    }
}
