using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Proxy;

public class ProxyMediaSourceTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";

    private const int AnalyzeMs = 5000;

    [Fact]
    public void Build_PointsAtProxyGetStreamWithInfohash()
    {
        var source = ProxyMediaSource.Build("http://acexy:8080", Infohash.Create(Hash), AnalyzeMs);

        Assert.Equal($"http://acexy:8080/ace/getstream?infohash={Hash}", source.Path);
    }

    [Fact]
    public void Build_TrimsTrailingSlashOnProxyUrl()
    {
        var source = ProxyMediaSource.Build("http://acexy:8080/", Infohash.Create(Hash), AnalyzeMs);

        Assert.Equal($"http://acexy:8080/ace/getstream?infohash={Hash}", source.Path);
    }

    [Fact]
    public void Build_ConfiguresLiveMpegTsStream()
    {
        var source = ProxyMediaSource.Build("http://acexy:8080", Infohash.Create(Hash), AnalyzeMs);

        Assert.Equal(Hash, source.Id);
        Assert.Equal(MediaProtocol.Http, source.Protocol);
        Assert.True(source.IsInfiniteStream);
        Assert.True(source.IsRemote);
        Assert.Equal("ts", source.Container);
    }

    [Fact]
    public void Build_UsesProvidedAnalyzeDuration()
    {
        var source = ProxyMediaSource.Build("http://acexy:8080", Infohash.Create(Hash), 3000);

        // Jellyfin's global -analyzeduration (200s) never returns on an infinite live stream;
        // the bound comes from configuration so it can be tuned per deployment.
        Assert.Equal(3000, source.AnalyzeDurationMs);
    }

    [Fact]
    public void Build_FallsBackToDefaultWhenAnalyzeDurationNotPositive()
    {
        var source = ProxyMediaSource.Build("http://acexy:8080", Infohash.Create(Hash), 0);

        // A non-positive value would make Jellyfin use its global default (which hangs);
        // guard against misconfiguration by falling back to a safe bound.
        Assert.NotNull(source.AnalyzeDurationMs);
        Assert.InRange(source.AnalyzeDurationMs!.Value, 1, 10_000);
    }

    [Fact]
    public void Build_LeavesMediaStreamsEmptyForTheProbeToFill()
    {
        var source = ProxyMediaSource.Build("http://acexy:8080", Infohash.Create(Hash), AnalyzeMs);

        // The bare source carries no codecs; IMediaSourceProbe fills MediaStreams later so
        // Jellyfin can decide remux vs transcode. Remux/transcode stay open here.
        Assert.True(source.MediaStreams is null || source.MediaStreams.Count == 0);
        Assert.True(source.SupportsDirectStream);
        Assert.True(source.SupportsTranscoding);
    }
}
