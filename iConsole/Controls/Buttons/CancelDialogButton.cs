using iConsole.Commands;
using iConsole.Services;

namespace iConsole.Controls.Buttons;

public class CancelDialogButton : Button
{
    public CancelDialogButton(IThemeService themeService)
        : base("Cancel", new CancelDialogCommand(), themeService)
    {
        HotKeyChar = 'c';
    }
}
