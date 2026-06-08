using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;
using Jellyfin.Plugin.AceStream.Tests.TestSupport;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class CachingMediaSourceProbeTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";
    private static readonly DateTimeOffset Start = new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed class CountingProbe : IMediaSourceProbe
    {
        private readonly bool _succeeds;

        public CountingProbe(bool succeeds) => _succeeds = succeeds;

        public int Calls { get; private set; }

        public Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken)
        {
            Calls++;
            if (_succeeds)
            {
                source.MediaStreams = new List<MediaStream> { new() { Type = MediaStreamType.Video, Codec = "h264", Index = 0 } };
                source.Bitrate = 5_000_000;
                source.Container = "mpegts";
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedCacheSettings : IProbeCacheSettings
    {
        public FixedCacheSettings(TimeSpan ttl) => CodecCacheTtl = ttl;

        public TimeSpan CodecCacheTtl { get; }
    }

    private static MediaSourceInfo Source(string? id = Hash, string path = "http://acexy:8080/s", int analyze = 5000)
        => new() { Id = id, Path = path, AnalyzeDurationMs = analyze };

    private static (CachingMediaSourceProbe Probe, MutableTimeProvider Clock) Build(
        IMediaSourceProbe inner, TimeSpan? ttl = null)
    {
        var clock = new MutableTimeProvider(Start);
        var probe = new CachingMediaSourceProbe(
            inner,
            new FixedCacheSettings(ttl ?? TimeSpan.FromMinutes(5)),
            clock,
            NullLogger<CachingMediaSourceProbe>.Instance);
        return (probe, clock);
    }

    [Fact]
    public async Task CacheHit_WithinTtl_DoesNotCallInner_AndCopiesCodecs()
    {
        var inner = new CountingProbe(succeeds: true);
        var (probe, clock) = Build(inner);

        await probe.EnrichAsync(Source(), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        var second = Source();
        await probe.EnrichAsync(second, CancellationToken.None);

        Assert.Equal(1, inner.Calls);
        var stream = Assert.Single(second.MediaStreams);
        Assert.Equal("h264", stream.Codec);
        Assert.Equal(5_000_000, second.Bitrate);
        Assert.Equal("mpegts", second.Container);
    }

    [Fact]
    public async Task CacheHit_DoesNotOverwritePerRequestFields()
    {
        var (probe, _) = Build(new CountingProbe(succeeds: true));

        await probe.EnrichAsync(Source(), CancellationToken.None);
        var second = Source(path: "http://other:9/s", analyze: 9999);
        await probe.EnrichAsync(second, CancellationToken.None);

        Assert.Equal("http://other:9/s", second.Path);
        Assert.Equal(9999, second.AnalyzeDurationMs);
    }

    [Fact]
    public async Task FailedOrEmptyProbe_NotCached_Retries()
    {
        var inner = new CountingProbe(succeeds: false);
        var (probe, _) = Build(inner);

        await probe.EnrichAsync(Source(), CancellationToken.None);
        await probe.EnrichAsync(Source(), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task DifferentInfohashes_ProbeIndependently()
    {
        const string other = "3c86e88beeb52233540afaf9febbb944d958cba6";
        var inner = new CountingProbe(succeeds: true);
        var (probe, _) = Build(inner);

        await probe.EnrichAsync(Source(), CancellationToken.None);
        await probe.EnrichAsync(Source(id: other), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task TtlExpiry_ReProbes()
    {
        var inner = new CountingProbe(succeeds: true);
        var (probe, clock) = Build(inner, ttl: TimeSpan.FromMinutes(5));

        await probe.EnrichAsync(Source(), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));
        await probe.EnrichAsync(Source(), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task ZeroTtl_DisablesCache()
    {
        var inner = new CountingProbe(succeeds: true);
        var (probe, _) = Build(inner, ttl: TimeSpan.Zero);

        await probe.EnrichAsync(Source(), CancellationToken.None);
        await probe.EnrichAsync(Source(), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task NoId_DelegatesWithoutCaching()
    {
        var inner = new CountingProbe(succeeds: true);
        var (probe, _) = Build(inner);

        await probe.EnrichAsync(Source(id: null), CancellationToken.None);
        await probe.EnrichAsync(Source(id: null), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }
}
