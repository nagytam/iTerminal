using iConsole.Data;
using iTerminal.Core.Dialogs.Map;

namespace iTerminal.Core.Commands;

public class MapDialogCommand(MapDialog mapDialog) : ShowDialogCommand<MapDialog>(mapDialog)
{
    public override string Title => "Map";
    public override string Description => "Open the Map: OpenStreetMap drawn as ASCII art, online or offline, with zoom, panning and place search.";

    // Alt+F10. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F10, false, true, false);
}
