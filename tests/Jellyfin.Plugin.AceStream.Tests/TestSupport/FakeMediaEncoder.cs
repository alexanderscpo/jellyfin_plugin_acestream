using System.Diagnostics;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.AceStream.Tests.TestSupport;

/// <summary>
/// Hand-rolled test double for Jellyfin's <see cref="IMediaEncoder"/>. Only <see cref="GetMediaInfo"/>
/// is configurable (via <see cref="OnGetMediaInfo"/>); every other member throws, so a test can
/// drive the one method under test without pulling in a mocking library.
/// </summary>
internal sealed class FakeMediaEncoder : IMediaEncoder
{
    /// <summary>Gets or sets the behavior of <see cref="GetMediaInfo"/>.</summary>
    public Func<MediaInfoRequest, CancellationToken, Task<MediaInfo>> OnGetMediaInfo { get; set; }
        = (_, _) => throw new NotImplementedException();

    public Task<MediaInfo> GetMediaInfo(MediaInfoRequest request, CancellationToken cancellationToken)
        => OnGetMediaInfo(request, cancellationToken);

    // --- Everything below is unused by the probe and intentionally unimplemented. ---

    public string EncoderPath => throw new NotImplementedException();
    public string ProbePath => throw new NotImplementedException();
    public Version EncoderVersion => throw new NotImplementedException();
    public bool IsPkeyPauseSupported => throw new NotImplementedException();
    public bool IsVaapiDeviceAmd => throw new NotImplementedException();
    public bool IsVaapiDeviceInteliHD => throw new NotImplementedException();
    public bool IsVaapiDeviceInteli965 => throw new NotImplementedException();
    public bool IsVaapiDeviceSupportVulkanDrmModifier => throw new NotImplementedException();
    public bool IsVaapiDeviceSupportVulkanDrmInterop => throw new NotImplementedException();
    public bool IsVideoToolboxAv1DecodeAvailable => throw new NotImplementedException();
    public bool SupportsEncoder(string encoder) => throw new NotImplementedException();
    public bool SupportsDecoder(string decoder) => throw new NotImplementedException();
    public bool SupportsHwaccel(string hwaccel) => throw new NotImplementedException();
    public bool SupportsFilter(string filter) => throw new NotImplementedException();
    public bool SupportsFilterWithOption(FilterOptionType option) => throw new NotImplementedException();
    public bool SupportsBitStreamFilterWithOption(BitStreamFilterOptionType option) => throw new NotImplementedException();
    public Task<string> ExtractAudioImage(string path, int? imageStreamIndex, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string> ExtractVideoImage(string inputFile, string container, MediaSourceInfo mediaSource, MediaStream videoStream, Video3DFormat? threedFormat, TimeSpan? offset, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string> ExtractVideoImage(string inputFile, string container, MediaSourceInfo mediaSource, MediaStream imageStream, int? imageStreamIndex, ImageFormat? targetFormat, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string> ExtractVideoImagesOnIntervalAccelerated(string inputFile, string container, MediaSourceInfo mediaSource, MediaStream imageStream, int maxWidth, TimeSpan interval, bool allowHwAccel, bool enableHwEncoding, int? threads, int? qualityScale, ProcessPriorityClass? priority, bool enableKeyFrameOnlyExtraction, EncodingHelper encodingHelper, CancellationToken cancellationToken) => throw new NotImplementedException();
    public string GetInputArgument(string inputFile, MediaSourceInfo mediaSource) => throw new NotImplementedException();
    public string GetInputArgument(IReadOnlyList<string> inputFiles, MediaSourceInfo mediaSource) => throw new NotImplementedException();
    public string GetExternalSubtitleInputArgument(string inputFile) => throw new NotImplementedException();
    public string GetTimeParameter(long ticks) => throw new NotImplementedException();
    public Task ConvertImage(string inputPath, string outputPath) => throw new NotImplementedException();
    public string EscapeSubtitleFilterPath(string path) => throw new NotImplementedException();
    public bool SetFFmpegPath() => throw new NotImplementedException();
    public IReadOnlyList<string> GetPrimaryPlaylistVobFiles(string path, uint? titleNumber) => throw new NotImplementedException();
    public IReadOnlyList<string> GetPrimaryPlaylistM2tsFiles(string path) => throw new NotImplementedException();
    public string GetInputPathArgument(EncodingJobInfo state) => throw new NotImplementedException();
    public string GetInputPathArgument(string path, MediaSourceInfo mediaSource) => throw new NotImplementedException();
    public void GenerateConcatConfig(MediaSourceInfo source, string concatFilePath) => throw new NotImplementedException();
    public bool CanEncodeToAudioCodec(string codec) => throw new NotImplementedException();
    public bool CanEncodeToSubtitleCodec(string codec) => throw new NotImplementedException();
    public bool CanExtractSubtitles(string codec) => throw new NotImplementedException();
}
