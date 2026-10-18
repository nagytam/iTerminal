using iTerminal.Core.Data;
using iTerminal.Core.Model.Remote;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace iTerminal.Core.Services.Remote;

/// <summary>The saved remote connections, in name order.</summary>
public interface IRemoteConnectionService
{
    IReadOnlyList<RemoteConnection> GetAll();
    RemoteConnection? Get(string id);

    /// <summary>Adds or replaces the entry with the same id. Throws <see cref="InvalidOperationException"/> when it is not sound.</summary>
    void Save(RemoteConnection connection);

    void Delete(string id);

    /// <summary>Stamps the entry as connected to now.</summary>
    void MarkConnected(string id);
}

/// <summary>Keeps the connections as JSON in one setting, the way <see cref="NetworkProfileService"/> keeps its profiles.</summary>
public sealed class RemoteConnectionService(ISettingsService settingsService) : IRemoteConnectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly Lock _lock = new();

    public IReadOnlyList<RemoteConnection> GetAll()
    {
        lock (_lock)
        {
            return Read();
        }
    }

    public RemoteConnection? Get(string id)
    {
        lock (_lock)
        {
            return Read().FirstOrDefault(c => c.Id == id);
        }
    }

    public void Save(RemoteConnection connection)
    {
        if (connection.Validate() is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var saved = connection.Clone();
        saved.Name = saved.Name.Trim();
        saved.Host = saved.Host.Trim();
        saved.Username = saved.Username.Trim();

        lock (_lock)
        {
            var connections = Read().Where(c => c.Id != saved.Id).ToList();
            connections.Add(saved);
            Write(connections);
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            Write(Read().Where(c => c.Id != id).ToList());
        }
    }

    public void MarkConnected(string id)
    {
        lock (_lock)
        {
            var connections = Read();
            var connection = connections.FirstOrDefault(c => c.Id == id);
            if (connection is not null)
            {
                connection.LastConnectedUtc = DateTime.UtcNow;
                Write(connections);
            }
        }
    }

    private List<RemoteConnection> Read()
    {
        var json = settingsService.Get(Settings.RemoteConnections);
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            var connections = JsonSerializer.Deserialize<List<RemoteConnection>>(json, JsonOptions) ?? [];
            return [.. connections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Write(List<RemoteConnection> connections) =>
        settingsService.Set(Settings.RemoteConnections, JsonSerializer.Serialize(connections, JsonOptions));
}
