namespace iTerminal.Core.Services.YouTube;

public interface IYouTubeService
{
    /// <summary>The videos that match <paramref name="query"/>, best match first. An empty term finds nothing.</summary>
    Task<IReadOnlyList<YouTubeVideo>> SearchAsync(string query, int count, CancellationToken cancellationToken = default);

    /// <summary>The address of a stream that ffmpeg can play directly, for the video with <paramref name="videoId"/>.</summary>
    Task<string> ResolvePlaybackUrlAsync(string videoId, CancellationToken cancellationToken = default);
}
