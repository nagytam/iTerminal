using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands;

public class SwitchPanelsCommand(
    IContentModelService contentModelService
    )
    : Command
{
    public override string Title => "Swap panels";
    public override string Description => "Swap the folders shown in the left and right panels.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.U, false, false, true);

    public override async Task ExecuteAsync(object? arg)
    {
        var left = Content.Left;
        var right = Content.Right;
        var contentModelLeft = contentModelService.ContentModels.First(m => m.Name == left);
        var contentModelRight = contentModelService.ContentModels.First(m => m.Name == right);

        var leftUrl = contentModelLeft.Url;
        var rightUrl = contentModelRight.Url;
        var leftCursorPosition = contentModelLeft.CursorPosition.Y;
        var leftScrollPosition = contentModelLeft.ScrollPosition.Y;
        var rightCursorPosition = contentModelRight.CursorPosition.Y;
        var rightScrollPosition = contentModelRight.ScrollPosition.Y;

        contentModelLeft.CursorPosition.Y = 0;
        contentModelRight.CursorPosition.Y = 0;
        contentModelLeft.ScrollPosition.Y = 0;
        contentModelRight.ScrollPosition.Y = 0;

        await contentModelService.SetUrlAsync(left, rightUrl);
        await contentModelService.SetUrlAsync(right, leftUrl);

        contentModelLeft.CursorPosition.Y = rightCursorPosition;
        contentModelRight.CursorPosition.Y = leftCursorPosition;
        contentModelLeft.ScrollPosition.Y = rightScrollPosition;
        contentModelRight.ScrollPosition.Y = leftScrollPosition;
    }
}
