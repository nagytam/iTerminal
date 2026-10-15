using iTerminal.Core.Model.Network;

namespace iTerminal.Core.Services;

public interface INetworkProfileService
{
    IReadOnlyList<NetworkConnectionProfile> GetAll();
    NetworkConnectionProfile? Get(string id);
    void Save(NetworkConnectionProfile profile);
    void Delete(string id);
}
