namespace iTerminal.Core.Services.YouTube;

/// <summary>One video from a search. Only what the list and the details pane show; the picture comes from the stream.</summary>
public sealed record YouTubeVideo(string Id, string Title, string Channel, TimeSpan? Duration, long? Views, DateTime? Published)
{
    /// <summary>The page of the video on YouTube, for the browser.</summary>
    public string PageUrl => $"https://www.youtube.com/watch?v={Id}";
}

/// <summary>A search or a playback that could not be done, with a message fit to show the person.</summary>
public sealed class YouTubeException(string message) : Exception(message);
