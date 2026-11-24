using iConsole.Data;
using iConsole.Dialogs;

namespace iConsole.Commands;

/// <summary>F2 in the CSV viewer: writes the marked rows, or else the rows on screen, to a file the person names, as CSV, JSON or XML.</summary>
public class CsvExportCommand(CsvViewerDialog viewer) : Command
{
    public override string Title => iConsole.Localization.UiText.Get("Key.ExportCsv", "F2-Export");
    public override Key? Key { get; init; } = new Key(ConsoleKey.F2, false, false, false);

    /// <summary>Only once the file's rows are read and the grid is showing (not the text view).</summary>
    public override bool CanExecute() => base.CanExecute() && viewer.CanExport;

    /// <summary>
    /// The question is asked without being awaited: this command runs on the input thread, and the prompt's answer arrives
    /// through that same thread, so waiting here would block the answer (the evidence commands do the same).
    /// </summary>
    public override Task ExecuteAsync(object? args)
    {
        _ = viewer.ExportAsync();
        return Task.CompletedTask;
    }
}
