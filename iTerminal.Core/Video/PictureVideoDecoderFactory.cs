namespace iTerminal.Core.Video;

/// <summary>
/// The decoder the picture viewers use. An SVG drawing and an icon are rasterized in-process, a Photoshop composite is read
/// in-process and a camera raw shows its embedded JPEG; every other file goes to ffmpeg, which also decodes JPEG 2000. So
/// this factory is always available: a video or a bitmap on a machine without ffmpeg fails when it is opened, with the same
/// message ffmpeg's own factory gives.
/// </summary>
public sealed class PictureVideoDecoderFactory(FfmpegVideoDecoderFactory ffmpeg) : IVideoDecoderFactory
{
    public bool IsAvailable => true;

    public IVideoDecoder Create(string path) =>
        SvgVideoDecoder.IsSvgFile(path) ? new SvgVideoDecoder(path)
        : IconVideoDecoder.IsIconFile(path) ? new IconVideoDecoder(path)
        : PsdVideoDecoder.IsPsdFile(path) ? new PsdVideoDecoder(path)
        : RawPreviewVideoDecoder.IsRawFile(path) ? new RawPreviewVideoDecoder(path)
        : ffmpeg.Create(path);
}
