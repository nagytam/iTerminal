using iTerminal.Core.Dialogs.NetworkTools;

namespace iTerminal.Core.Commands;

public class NetworkToolsDialogCommand(NetworkToolsDialog networkToolsDialog) : ShowDialogCommand<NetworkToolsDialog>(networkToolsDialog)
{
    public override string Title => "Network tools";
    public override string Description => "Ping, traceroute, DNS and reverse DNS lookups, whois, a port check and web headers, for a host you type.";
}
