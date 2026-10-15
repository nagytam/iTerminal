using iConsole.Controls.PopUpWindows;
using iConsole.Data;

namespace iConsole.Commands;

public class DatePopUpWindowCommand(DatePopUpWindow datePopUpWindow) 
    : Command
{
    public override string Title => "Calendar";
    public override string Description => "Show the calendar pop-up.";

    private DatePopUpWindow _datePopUpWindow { get; init; } = datePopUpWindow;

    public override bool CanExecute()
    {
        if (!_datePopUpWindow.IsVisible)
        {
            return true;
        }
        return false;
    }

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F1, true, true, false);

    public override async Task ExecuteAsync(object? arg)
    {
        var window = _datePopUpWindow;
        await window.ShowAsync();
    }
}
