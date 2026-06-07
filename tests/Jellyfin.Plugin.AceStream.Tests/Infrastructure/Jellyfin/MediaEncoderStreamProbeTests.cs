using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;
using Jellyfin.Plugin.AceStream.Tests.TestSupport;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class MediaEncoderStreamProbeTests
{
    private static MediaEncoderStreamProbe ProbeWith(FakeMediaEncoder encoder)
        => new(encoder, NullLogger<MediaEncoderStreamProbe>.Instance);

    private static MediaSourceInfo BareSource() => new() { Path = "http://acexy:8080/ace/getstream?infohash=x" };

    [Fact]
    public async Task EnrichAsync_SpuriousCancellation_LeavesSourceUnchanged()
    {
        // GetMediaInfo throws OCE for a reason unrelated to either token (e.g. ffprobe killed).
        var encoder = new FakeMediaEncoder { OnGetMediaInfo = (_, _) => throw new OperationCanceledException() };
        var source = BareSource();

        // Must not throw — the resilience contract says a failed probe leaves the source unchanged.
        await ProbeWith(encoder).EnrichAsync(source, CancellationToken.None);

        Assert.Empty(source.MediaStreams);
    }

    [Fact]
    public async Task EnrichAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var encoder = new FakeMediaEncoder
        {
            OnGetMediaInfo = (_, _) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ProbeWith(encoder).EnrichAsync(BareSource(), cts.Token));
    }

    [Fact]
    public async Task EnrichAsync_SuccessfulProbe_EnrichesSource()
    {
        var encoder = new FakeMediaEncoder
        {
            OnGetMediaInfo = (_, _) => Task.FromResult(new MediaInfo
            {
                MediaStreams = new List<MediaStream> { new() { Type = MediaStreamType.Video, Codec = "h264", Index = 0 } },
                Bitrate = 5_000_000,
                Container = "mpegts",
            }),
        };
        var source = BareSource();

        await ProbeWith(encoder).EnrichAsync(source, CancellationToken.None);

        var stream = Assert.Single(source.MediaStreams);
        Assert.Equal("h264", stream.Codec);
        Assert.Equal(5_000_000, source.Bitrate);
        Assert.Equal("mpegts", source.Container);
    }

    [Fact]
    public async Task EnrichAsync_ProbeWithNoStreams_LeavesSourceUnchanged()
    {
        var encoder = new FakeMediaEncoder
        {
            OnGetMediaInfo = (_, _) => Task.FromResult(new MediaInfo { MediaStreams = new List<MediaStream>() }),
        };
        var source = BareSource();

        await ProbeWith(encoder).EnrichAsync(source, CancellationToken.None);

        Assert.Empty(source.MediaStreams);
    }
}
