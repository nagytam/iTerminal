using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Controls.MenuBar;

/// <summary>
/// Stop and mute for the music player, shown in the top-right bar while something plays. The bar owns
/// its <see cref="iConsole.Controls.Control.IsVisible"/> (it is laid out with the other indicators);
/// this control only draws and handles clicks, and subscribes to nothing.
/// </summary>
public class MusicIndicator(IMusicPlayerService musicPlayerService, TaskManager taskManager, IThemeService themeService) : iConsole.Controls.Control(themeService)
{
    internal const int StopWidth = 6;
    internal const int Separator = 1;
    internal const int MuteWidth = 8;

    /// <summary>The whole widget: the Stop region, a separator, and the Mute or Unmute region.</summary>
    public const int Width = StopWidth + Separator + MuteWidth;

    private readonly IMusicPlayerService _musicPlayerService = musicPlayerService;
    private readonly TaskManager _taskManager = taskManager;

    /// <summary>The text to draw, fitted to <paramref name="width"/>: each region is padded to its own width, so Mute and Unmute take the same space.</summary>
    internal string Describe(int width)
    {
        var stop = Fit(UiText.Get("Indicator.Stop", "Stop"), StopWidth);
        var mute = Fit(
            _musicPlayerService.IsMuted
                ? UiText.Get("Indicator.Unmute", "Unmute")
                : UiText.Get("Indicator.Mute", "Mute"),
            MuteWidth);

        return (stop + new string(' ', Separator) + mute).ExpandRight(width);
    }

    private static string Fit(string text, int width) => (text.Shrink(width) ?? string.Empty).ExpandRight(width);

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (!mouseEvent.LeftClicked || !IsEnabled || !IsVisible || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return await base.HandleMouseEventAsync(mouseEvent);
        }

        int column = (int)(mouseEvent.Position.X - AbsolutePosition.X);
        if (column < StopWidth)
        {
            await _taskManager.Add(new MusicStopTask(_musicPlayerService));
        }
        else if (column >= StopWidth + Separator)
        {
            _musicPlayerService.IsMuted = !_musicPlayerService.IsMuted;
            StateHasChanged();
        }

        return true;
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var text = new ConxelString(Describe((int)ViewSize.Width), colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, text);
    }
}
