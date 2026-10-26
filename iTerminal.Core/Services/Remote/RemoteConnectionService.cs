using iTerminal.Core.Data;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Model.Passwords;
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

    /// <summary>
    /// Saves, and for an FTP or WebDAV entry also records the login the drives look up
    /// (<see cref="NetworkConnectionProfile"/> + password manager). A null or empty <paramref name="password"/>
    /// keeps the one already saved; the password never goes into the connection's own JSON.
    /// </summary>
    void Save(RemoteConnection connection, string? password);

    void Delete(string id);

    /// <summary>Stamps the entry as connected to now.</summary>
    void MarkConnected(string id);
}

/// <summary>Keeps the connections as JSON in one setting, the way <see cref="NetworkProfileService"/> keeps its profiles.</summary>
public sealed class RemoteConnectionService(
    ISettingsService settingsService,
    INetworkProfileService? networkProfiles = null,
    IPasswordManager? passwordManager = null) : IRemoteConnectionService
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

    public void Save(RemoteConnection connection) => Save(connection, null);

    public void Save(RemoteConnection connection, string? password)
    {
        if (connection.Validate() is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var saved = connection.Clone();
        saved.Name = saved.Name.Trim();
        saved.Host = saved.Host.Trim();
        saved.Username = saved.Username.Trim();
        saved.RemotePath = string.IsNullOrWhiteSpace(saved.RemotePath) ? null : saved.RemotePath.Trim();

        lock (_lock)
        {
            if (saved.IsFileDrive)
            {
                // Before the entry is written: a locked password manager refuses, and the entry must not
                // then claim a password it does not have.
                SaveLogin(saved, password);
            }
            else
            {
                saved.Secure = false;
                saved.RemotePath = null;
                if (connection.HasSavedCredential || networkProfiles?.Get(saved.Id) is not null)
                {
                    // The entry used to be an FTP or WebDAV one: its login goes with it.
                    RemoveLogin(saved.Id);
                }

                saved.HasSavedCredential = false;
            }

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
            RemoveLogin(id);
        }
    }

    /// <summary>
    /// What <c>ContentModelFtpDrive</c> / <c>ContentModelWebDavDrive</c> look for when a URL names a user: a profile of
    /// the same protocol, host (with the port, when it is not the default) and user, and a secret under its id.
    /// </summary>
    private void SaveLogin(RemoteConnection saved, string? password)
    {
        if (!string.IsNullOrEmpty(password))
        {
            if (passwordManager is null || !(passwordManager.IsUnlocked || passwordManager.Unlock()))
            {
                throw new InvalidOperationException("The password manager could not be opened, so the password was not saved.");
            }

            passwordManager.Save(new PasswordEntry
            {
                Id = saved.Id,
                Title = $"{(saved.Protocol == RemoteProtocol.Ftp ? "FTP" : "WebDAV")} {saved.Username}@{saved.DriveHost}",
                Username = saved.Username,
                Category = PasswordCategory.Network,
                Password = password,
            });
            saved.HasSavedCredential = true;
        }

        if (string.IsNullOrEmpty(saved.Username))
        {
            // Anonymous: the drives look a profile up by user name, so there is nothing to find and nothing to keep.
            if (saved.HasSavedCredential)
            {
                passwordManager?.DeleteSecret(saved.Id);
                saved.HasSavedCredential = false;
            }
        }

        networkProfiles?.Save(new NetworkConnectionProfile
        {
            Id = saved.Id,
            Protocol = saved.Protocol == RemoteProtocol.Ftp ? NetworkProtocol.Ftp : NetworkProtocol.WebDav,
            Host = saved.DriveHost,
            Username = saved.Username,
            HasSavedCredential = saved.HasSavedCredential,
        });
    }

    private void RemoveLogin(string id)
    {
        if (networkProfiles?.Get(id) is not null)
        {
            networkProfiles.Delete(id);
        }

        try
        {
            passwordManager?.DeleteSecret(id);
        }
        catch (InvalidOperationException)
        {
            // Locked: the entry stays in the manager, where it can be removed by hand. Deleting the
            // connection must not fail for it.
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
            // A null entry ("[null]") is not a connection; sorting it threw from the dialog that opened the list.
            return [.. connections.Where(c => c is not null).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Write(List<RemoteConnection> connections) =>
        settingsService.Set(Settings.RemoteConnections, JsonSerializer.Serialize(connections, JsonOptions));
}
