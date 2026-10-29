using iConsole.Data;
using iTerminal.Core.Dialogs.Torrents;

namespace iTerminal.Core.Commands;

public class TorrentManagerDialogCommand(TorrentManagerDialog torrentManagerDialog) : ShowDialogCommand<TorrentManagerDialog>(torrentManagerDialog)
{
    public override string Title => "BitTorrent";
    public override string Description => "Open the BitTorrent manager: download magnet links and .torrent files, pause, resume and remove them.";

    // Ctrl+Shift+F1. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F1, true, false, true);
}
