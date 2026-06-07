using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class ReadinessGatedProbeTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";

    private sealed class CountingProbe : IMediaSourceProbe
    {
        public int Calls { get; private set; }

        public Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubReadiness : IStreamReadiness
    {
        private readonly bool _ready;

        public StubReadiness(bool ready) => _ready = ready;

        public Infohash? Asked { get; private set; }

        public Task<bool> IsReadyAsync(Infohash infohash, CancellationToken cancellationToken)
        {
            Asked = infohash;
            return Task.FromResult(_ready);
        }
    }

    private static ReadinessGatedProbe Gated(IMediaSourceProbe inner, IStreamReadiness readiness)
        => new(inner, readiness, NullLogger<ReadinessGatedProbe>.Instance);

    [Fact]
    public async Task Ready_DelegatesToInnerProbe_WithInfohashFromSourceId()
    {
        var inner = new CountingProbe();
        var readiness = new StubReadiness(ready: true);
        var source = new MediaSourceInfo { Id = Hash, Path = "http://acexy:8080/ace/getstream?infohash=" + Hash };

        await Gated(inner, readiness).EnrichAsync(source, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(Hash, readiness.Asked!.Value);
    }

    [Fact]
    public async Task NotReady_SkipsInnerProbe()
    {
        var inner = new CountingProbe();
        var source = new MediaSourceInfo { Id = Hash };

        await Gated(inner, new StubReadiness(ready: false)).EnrichAsync(source, CancellationToken.None);

        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task UnparseableId_FailsOpenAndProbes()
    {
        var inner = new CountingProbe();
        var source = new MediaSourceInfo { Id = "not-a-valid-infohash" };

        // Can't gate on a bad id, so don't block: probe anyway (degrades to today's behavior).
        await Gated(inner, new StubReadiness(ready: false)).EnrichAsync(source, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
    }
}
