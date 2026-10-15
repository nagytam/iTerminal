using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Music;

/// <summary>
/// Shared plumbing for the two volume keys. Both accept a second chord because <c>+</c> and
/// <c>-</c> arrive as <see cref="ConsoleKey.Add"/>/<see cref="ConsoleKey.Subtract"/> from the numeric
/// keypad and as <see cref="ConsoleKey.OemPlus"/>/<see cref="ConsoleKey.OemMinus"/> from the main
/// row, and a volume key that only works on one of the two is a bug report waiting to happen.
/// </summary>
public abstract class MusicVolumeCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : MusicPanelCommand(contentModel, contentModelService)
{
    /// <summary>The second chord that means the same thing as <see cref="Command.Key"/>.</summary>
    protected abstract Key AlternateKey { get; }

    protected abstract MusicAction Action { get; }

    public override string KeyDisplayText => $"{FormatKey(Key)} / {FormatKey(AlternateKey)}";

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (AlternateKey.IsIdentical(keyEvent))
        {
            return await RunAsync(null);
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override Task ExecuteAsync(object? args)
    {
        FocusedMusicModel?.Perform(Action);
        return Task.CompletedTask;
    }
}

/// <summary>Turns the output up by five points from either music panel.</summary>
public class MusicVolumeUpCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : MusicVolumeCommand(contentModel, contentModelService)
{
    public override string Title => "Volume up";
    public override string Description => "Raise the playback volume by 5%.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Add, false, false, false);

    protected override Key AlternateKey { get; } =
        new(ConsoleKey.OemPlus, false, false, false);

    protected override MusicAction Action => MusicAction.VolumeUp;
}

/// <summary>Turns the output down by five points from either music panel.</summary>
public class MusicVolumeDownCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : MusicVolumeCommand(contentModel, contentModelService)
{
    public override string Title => "Volume down";
    public override string Description => "Lower the playback volume by 5%.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Subtract, false, false, false);

    protected override Key AlternateKey { get; } =
        new(ConsoleKey.OemMinus, false, false, false);

    protected override MusicAction Action => MusicAction.VolumeDown;
}
