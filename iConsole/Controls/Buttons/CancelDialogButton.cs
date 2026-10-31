using iConsole.Commands;
using iConsole.Services;

namespace iConsole.Controls.Buttons;

public class CancelDialogButton : Button
{
    public CancelDialogButton(IThemeService themeService)
        : base(iConsole.Localization.UiText.Get("Common.Cancel", "Cancel"), new CancelDialogCommand(), themeService)
    {
        HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(iConsole.Localization.UiText.Get("Common.Cancel", "Cancel"), 'c');
    }
}
