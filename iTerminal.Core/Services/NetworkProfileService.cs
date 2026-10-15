using iTerminal.Core.Data;
using iTerminal.Core.Model.Network;
using System.Text.Json;

namespace iTerminal.Core.Services;

public class NetworkProfileService(ISettingsService settingsService) : INetworkProfileService
{
    private readonly Lock _lock = new();

    public IReadOnlyList<NetworkConnectionProfile> GetAll()
    {
        lock (_lock)
        {
            return Read();
        }
    }

    public NetworkConnectionProfile? Get(string id)
    {
        lock (_lock)
        {
            return Read().FirstOrDefault(p => p.Id == id);
        }
    }

    public void Save(NetworkConnectionProfile profile)
    {
        lock (_lock)
        {
            var profiles = Read().Where(p => p.Id != profile.Id).ToList();
            profiles.Add(profile);
            Write(profiles);
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            var profiles = Read().Where(p => p.Id != id).ToList();
            Write(profiles);
        }
    }

    private List<NetworkConnectionProfile> Read()
    {
        var json = settingsService.Get(Settings.NetworkConnectionProfiles);
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<NetworkConnectionProfile>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Write(List<NetworkConnectionProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles);
        settingsService.Set(Settings.NetworkConnectionProfiles, json);
    }
}
