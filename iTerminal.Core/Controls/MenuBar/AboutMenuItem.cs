using iConsole.Commands;
using iConsole.Controls;
using iConsole.Dialogs;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Controls.MenuBar;

public class AboutMenuItem : MenuItem
{
    public AboutMenuItem(
        HelpDialog helpDialog,
        AboutDialog aboutDialog,
        IThemeService themeService,
        CommandPaletteDialog? commandPaletteDialog = null)
        : base("Help", null, [], themeService)
    {
        HotKeyChar = 'H';
        AddMenuItem(new MenuItem("Help\tF1",  new HelpDialogCommand(helpDialog), [], themeService));
        if (commandPaletteDialog is not null)
        {
            AddMenuItem(new MenuItem("Command Palette\tShift+F1", new CommandPaletteDialogCommand(commandPaletteDialog), [], themeService));
        }
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("About\tCtrl+Shift+Alt+F1", new AboutDialogCommand(aboutDialog), [], themeService));
    }
}

