using iConsole.Commands;
using iConsole.Controls;
using iConsole.Desktops;
using iConsole.Drivers;
using iConsole.Services;

namespace iConsole.Screens;

public class Screen : Control, IScreen
{
    private readonly IConsoleDriver _consoleDriver;
    public IDesktop Desktop { get; set; } = null!;

    public string Title
    {
        set => _consoleDriver.Title = value;
    }

    public Screen(IConsoleDriver consoleDriver, IThemeService themeService) : base(themeService)
    {
        _consoleDriver = consoleDriver ?? Services.NullConsoleDriver.Instance;
        // Clear and initialize only if driver is available (null-object will be no-op)
        _consoleDriver.Clear();
        _consoleDriver.IsCursorVisible = false;
        _consoleDriver.SetCursorPosition(0, 0);

        IsModal = true;
        CanFocus = true;
        Title = "iConsole";

        SetFocus();
    }

    public override void OnInitializeCommands()
    {
        base.OnInitializeCommands();

        AddCommand(new TabBackwardCommand());
        AddCommand(new TabForwardCommand());
    }

    public override void OnInitializeLayout()
    {
        foreach (var control in Controls)
        {
            control.OnInitializeLayout();
        }
        StateHasChanged();
    }
}
