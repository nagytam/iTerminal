using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Services;

namespace iConsole.Controls.Buttons;

public class ActionButton(string title, string actionView, ICommand command, IThemeService themeService) : Button(title, command, themeService)
{
    public string ActionView { get; private set; } = actionView;

    public override void Draw(IBuffer buffer)
    {
        // action buttons are drawn by parent control
    }
}
