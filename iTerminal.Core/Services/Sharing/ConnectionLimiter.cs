using System.Net;

namespace iTerminal.Core.Services.Sharing;

/// <summary>
/// Counts the open connections per client address, so one host on the LAN cannot hold every slot of the share.
/// A connection with no known address is not counted against any host.
/// </summary>
internal sealed class ConnectionLimiter(int maxPerAddress)
{
    private readonly object _sync = new();
    private readonly Dictionary<IPAddress, int> _open = [];

    public bool TryAcquire(IPAddress? address)
    {
        if (address is null)
        {
            return true;
        }

        lock (_sync)
        {
            _open.TryGetValue(address, out int count);
            if (count >= maxPerAddress)
            {
                return false;
            }

            _open[address] = count + 1;
            return true;
        }
    }

    public void Release(IPAddress? address)
    {
        if (address is null)
        {
            return;
        }

        lock (_sync)
        {
            if (_open.TryGetValue(address, out int count))
            {
                if (count <= 1)
                {
                    _open.Remove(address);
                }
                else
                {
                    _open[address] = count - 1;
                }
            }
        }
    }

    internal int OpenFrom(IPAddress address)
    {
        lock (_sync)
        {
            return _open.TryGetValue(address, out int count) ? count : 0;
        }
    }
}
