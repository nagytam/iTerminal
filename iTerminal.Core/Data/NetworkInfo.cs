using System.Diagnostics;
using System.Net.NetworkInformation;

namespace iTerminal.Core.Data;

[DebuggerDisplay("{Name}")]
public class NetworkInfo
{
    public string Name { get; set; }
    public NetworkInterface NetworkInterface { get; internal set; }
}
