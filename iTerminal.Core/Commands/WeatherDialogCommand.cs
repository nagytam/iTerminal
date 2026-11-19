using iConsole.Data;
using iConsole.Localization;
using iTerminal.Core.Dialogs.Weather;

namespace iTerminal.Core.Commands;

public class WeatherDialogCommand(WeatherDialog weatherDialog) : ShowDialogCommand<WeatherDialog>(weatherDialog)
{
    public override string Title => "Weather";
    public override string Description => UiText.Describe("Show the weather for the saved places: now, the next 24 hours and the next 7 days.");

    // No global hot key: every Ctrl+Shift+F-key is taken, so the dialog opens from the Tools menu.
    public override Key? Key { get; init; } = null;
}
