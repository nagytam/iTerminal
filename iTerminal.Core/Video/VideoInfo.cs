namespace iTerminal.Core.Video;

/// <summary>What a video file says about itself: picture size, length and frame rate.</summary>
public sealed record VideoInfo(int Width, int Height, TimeSpan Duration, double FrameRate)
{
    /// <summary>Width over height of the source picture, never zero.</summary>
    public double AspectRatio => Height > 0 && Width > 0 ? (double)Width / Height : 16.0 / 9.0;
}
