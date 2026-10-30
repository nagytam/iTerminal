using iConsole.Commands;
using iConsole.Services;

namespace iConsole.Controls.Buttons;

public class CloseButton : Button
{
    public CloseButton(IThemeService themeService)
        : base(iConsole.Localization.UiText.Get("Common.Close", "Close"), new CloseParentCommand(), themeService)
    {
        // 'c' is reserved for Cancel (CancelButton/CancelDialogButton); Close uses 'l' so the two
        // can coexist in the same dialog without one silently swallowing the other's Ctrl+key.
        HotKeyChar = 'l';
    }
}
