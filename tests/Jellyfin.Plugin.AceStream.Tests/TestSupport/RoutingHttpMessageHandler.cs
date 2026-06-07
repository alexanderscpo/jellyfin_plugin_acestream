using System.Net;
using System.Text;

namespace Jellyfin.Plugin.AceStream.Tests.TestSupport;

/// <summary>
/// Test double that returns a JSON body chosen per request URI (so a multi-step HTTP flow —
/// getstream, stat polling, stop — can be driven from one handler) and records every request URI.
/// </summary>
internal sealed class RoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<Uri, string> _route;

    public RoutingHttpMessageHandler(Func<Uri, string> route) => _route = route;

    public List<Uri> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request.RequestUri!);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_route(request.RequestUri!), Encoding.UTF8, "application/json"),
        };

        return Task.FromResult(response);
    }
}
