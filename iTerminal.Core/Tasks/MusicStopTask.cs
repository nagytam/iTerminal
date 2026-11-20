using iConsole.Localization;
using iConsole.Tasks;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Stops the music player from the top-right widget. <see cref="IMusicPlayerService.Stop"/> takes the
/// player's request lock and waits on the output device, so it runs here rather than in the click
/// handler, which has to return within the 200 ms budget.
/// </summary>
public class MusicStopTask : BackgroundTask
{
    private readonly IMusicPlayerService _musicPlayerService;

    public MusicStopTask(IMusicPlayerService musicPlayerService)
    {
        _musicPlayerService = musicPlayerService;
        Name = UiText.Get("Indicator.Stop", "Stop");
    }

    public override Task ExecuteAsync() => Task.Run(_musicPlayerService.Stop);
}
