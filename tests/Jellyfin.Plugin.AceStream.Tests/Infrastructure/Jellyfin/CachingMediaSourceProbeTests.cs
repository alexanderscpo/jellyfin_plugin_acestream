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

    private sealed class FixedCacheSettings : IProbeSettings
    {
        public FixedCacheSettings(TimeSpan ttl) => CodecCacheTtl = ttl;

        public int ProbeAnalyzeDurationMs => 5000;

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

    // Concurrent-miss coalescing tests
    [Fact]
    public async Task ConcurrentMiss_SameKey_InnerProbeRunsOnce()
    {
        var gate = new TaskCompletionSource();
        int innerCalls = 0;

        var blockingProbe = new ScriptedProbe(async (source, ct) =>
        {
            Interlocked.Increment(ref innerCalls);
            await gate.Task.WaitAsync(ct);
            source.MediaStreams = new List<MediaStream> { new() { Type = MediaStreamType.Video, Codec = "h264", Index = 0 } };
            source.Bitrate = 1_000_000;
            source.Container = "mpegts";
        });

        var (probe, _) = Build(blockingProbe);

        // Launch two concurrent enrichments for the same key; both arrive before gate opens.
        var t1 = probe.EnrichAsync(Source(), CancellationToken.None);
        var t2 = probe.EnrichAsync(Source(), CancellationToken.None);

        gate.SetResult(); // let the probe proceed
        await Task.WhenAll(t1, t2);

        // The inner probe should have been invoked exactly once despite two concurrent misses.
        Assert.Equal(1, innerCalls);
    }

    [Fact]
    public async Task ConcurrentMiss_SameKey_BothCallersReceiveCodecs()
    {
        var gate = new TaskCompletionSource();

        var blockingProbe = new ScriptedProbe(async (source, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            source.MediaStreams = new List<MediaStream> { new() { Type = MediaStreamType.Video, Codec = "h264", Index = 0 } };
            source.Bitrate = 5_000_000;
            source.Container = "mpegts";
        });

        var (probe, _) = Build(blockingProbe);

        var s1 = Source();
        var s2 = Source();
        var t1 = probe.EnrichAsync(s1, CancellationToken.None);
        var t2 = probe.EnrichAsync(s2, CancellationToken.None);

        gate.SetResult();
        await Task.WhenAll(t1, t2);

        Assert.Equal("h264", Assert.Single(s1.MediaStreams).Codec);
        Assert.Equal("h264", Assert.Single(s2.MediaStreams).Codec);
    }

    // Defensive copy test
    [Fact]
    public async Task CacheHit_MutationByConsumer_DoesNotLeakIntoSubsequentHits()
    {
        var inner = new CountingProbe(succeeds: true);
        var (probe, _) = Build(inner);

        // Prime the cache.
        var s1 = Source();
        await probe.EnrichAsync(s1, CancellationToken.None);

        // First hit — mutate the returned list in-place.
        var s2 = Source();
        await probe.EnrichAsync(s2, CancellationToken.None);
        // Mutate the list that was assigned to s2.MediaStreams
        if (s2.MediaStreams is List<MediaStream> list)
        {
            list.Add(new MediaStream { Type = MediaStreamType.Audio, Codec = "aac", Index = 1 });
        }
        else
        {
            // If already read-only the mutation test is still meaningful below
            s2.MediaStreams = s2.MediaStreams.Concat(new[] { new MediaStream { Type = MediaStreamType.Audio, Codec = "aac", Index = 1 } }).ToList();
        }

        // Second hit — should return the original one stream, unaffected by s2's mutation.
        var s3 = Source();
        await probe.EnrichAsync(s3, CancellationToken.None);

        Assert.Equal(1, s3.MediaStreams.Count);
        Assert.Equal("h264", s3.MediaStreams[0].Codec);
        Assert.Equal(1, inner.Calls); // still only one inner call
    }

    // Cancellation-decoupling tests (Fix 1)

    /// <summary>
    /// When caller 1 cancels mid-probe, caller 2 (with a live token) must still receive the
    /// enriched result. The inner probe must run exactly once.
    /// </summary>
    [Fact]
    public async Task Caller1CancelsMidProbe_Caller2ReceivesResult_InnerProbeRanOnce()
    {
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeProceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int innerCalls = 0;

        var blockingProbe = new ScriptedProbe(async (source, ct) =>
        {
            Interlocked.Increment(ref innerCalls);
            probeStarted.TrySetResult();
            await probeProceed.Task; // block until the test unblocks us
            source.MediaStreams = new List<MediaStream> { new() { Type = MediaStreamType.Video, Codec = "h264", Index = 0 } };
            source.Bitrate = 5_000_000;
            source.Container = "mpegts";
        });

        var (probe, _) = Build(blockingProbe);

        using var cts1 = new CancellationTokenSource();
        var s1 = Source();
        var s2 = Source();

        // Launch both callers before the probe finishes.
        var t1 = probe.EnrichAsync(s1, cts1.Token);
        var t2 = probe.EnrichAsync(s2, CancellationToken.None);

        // Wait until the inner probe has actually started, then cancel caller 1.
        await probeStarted.Task;
        cts1.Cancel();

        // Let the probe finish (unblock probeProceed).
        probeProceed.TrySetResult();

        // t1 should be cancelled; t2 should succeed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t1);
        await t2; // must not throw

        Assert.Equal("h264", Assert.Single(s2.MediaStreams).Codec);
        Assert.Equal(1, innerCalls);
    }

    /// <summary>
    /// A caller whose own token is cancelled while it is awaiting the in-flight task
    /// receives OperationCanceledException promptly.
    /// </summary>
    [Fact]
    public async Task CallerTokenCancelled_WhileAwaiting_ThrowsOperationCanceledException()
    {
        var probeProceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var blockingProbe = new ScriptedProbe(async (source, _) =>
        {
            await probeProceed.Task;
            source.MediaStreams = new List<MediaStream> { new() { Type = MediaStreamType.Video, Codec = "h264", Index = 0 } };
        });

        var (probe, _) = Build(blockingProbe);

        using var cts = new CancellationTokenSource();
        var s = Source();

        var t = probe.EnrichAsync(s, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t);

        // Cleanup: let the background probe finish so no unobserved task exceptions remain.
        probeProceed.TrySetResult();
        await Task.Delay(50); // give the background task a tick to settle
    }

    // Helper scripted probe for concurrency tests
    private sealed class ScriptedProbe : IMediaSourceProbe
    {
        private readonly Func<MediaSourceInfo, CancellationToken, Task> _body;

        public ScriptedProbe(Func<MediaSourceInfo, CancellationToken, Task> body) => _body = body;

        public Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken)
            => _body(source, cancellationToken);
    }
}
