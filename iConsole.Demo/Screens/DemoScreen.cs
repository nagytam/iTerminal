using iConsole.Dialogs;
using iConsole.Drivers;
using iConsole.Layouts;
using iConsole.Services;

namespace iConsole.Demo.Screens;

public class DemoScreen : iConsole.Screens.Screen
{
    private readonly IConsoleDriver _consoleDriver;

    public DemoScreen(
        IConsoleDriver consoleDriver,
        IThemeService themeService)
        : base(consoleDriver, themeService)
    {
        _consoleDriver = consoleDriver;
        AddControl(new SplashDialog(themeService));
    }

    public override void OnInitializeLayout()
    {
        Layout.Fill(_consoleDriver, this);
        base.OnInitializeLayout();
    }
}
