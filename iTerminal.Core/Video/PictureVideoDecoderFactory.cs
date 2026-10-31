namespace iTerminal.Core.Video;

/// <summary>
/// The decoder the picture viewers use. An SVG drawing and an icon are rasterized in-process and need no ffmpeg; every
/// other file goes to ffmpeg. So this factory is always available: a video or a bitmap on a machine without ffmpeg fails
/// when it is opened, with the same message ffmpeg's own factory gives.
/// </summary>
public sealed class PictureVideoDecoderFactory(FfmpegVideoDecoderFactory ffmpeg) : IVideoDecoderFactory
{
    public bool IsAvailable => true;

    public IVideoDecoder Create(string path) =>
        SvgVideoDecoder.IsSvgFile(path) ? new SvgVideoDecoder(path)
        : IconVideoDecoder.IsIconFile(path) ? new IconVideoDecoder(path)
        : ffmpeg.Create(path);
}
