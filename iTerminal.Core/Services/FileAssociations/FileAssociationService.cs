using System.Text.Json;
using System.Text.Json.Serialization;

namespace iTerminal.Core.Services.FileAssociations;

/// <summary>
/// The programs the user has chosen for kinds of files. The order of the list is the priority: of
/// the associations that match a file, the first is the one Enter (or F4) uses, and the rest are
/// what "Open with…" offers beside it.
/// </summary>
public interface IFileAssociationService
{
    /// <summary>Moves whenever the list changes — what an open dialog polls.</summary>
    long Version { get; }

    IReadOnlyList<FileAssociation> GetAll();

    /// <summary>The associations of <paramref name="kind"/> that apply to <paramref name="fileName"/>, the default first.</summary>
    IReadOnlyList<FileAssociation> GetFor(string fileName, FileAssociationKind kind);

    /// <summary>What Enter (or F4) uses for <paramref name="fileName"/>, or null when the user has chosen nothing.</summary>
    FileAssociation? GetDefault(string fileName, FileAssociationKind kind);

    /// <summary>
    /// Adds <paramref name="association"/> at the top — the program just chosen is the one the user
    /// wants from now on — or, when one with its id exists, replaces that one where it stands.
    /// </summary>
    void Save(FileAssociation association);

    bool Remove(string id);

    /// <summary>Moves the association to the top, ahead of every other that matches the same files.</summary>
    bool MakeDefault(string id);
}

/// <summary>
/// Keeps the associations in the <see cref="SettingsKey"/> setting, as JSON. Every read goes back to
/// the setting — parsed again only when its text has changed — so there is one copy of the truth
/// and a second instance of the app, or a restored database, is followed without being told.
/// </summary>
public class FileAssociationService(ISettingsService settingsService) : IFileAssociationService
{
    public const string SettingsKey = "FileAssociations";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _lock = new();
    private string? _parsedText;
    private List<FileAssociation> _parsed = [];
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public IReadOnlyList<FileAssociation> GetAll()
    {
        lock (_lock)
        {
            return [.. Load()];
        }
    }

    public IReadOnlyList<FileAssociation> GetFor(string fileName, FileAssociationKind kind)
    {
        lock (_lock)
        {
            return [.. Load().Where(a => a.Kind == kind && a.Matches(fileName))];
        }
    }

    public FileAssociation? GetDefault(string fileName, FileAssociationKind kind)
    {
        lock (_lock)
        {
            return Load().FirstOrDefault(a => a.Kind == kind && a.Matches(fileName));
        }
    }

    public void Save(FileAssociation association)
    {
        ArgumentNullException.ThrowIfNull(association);

        lock (_lock)
        {
            var associations = Load().ToList();
            var index = associations.FindIndex(a => a.Id == association.Id);
            if (index >= 0)
            {
                associations[index] = association;
            }
            else
            {
                associations.Insert(0, association);
            }

            Store(associations);
        }
    }

    public bool Remove(string id)
    {
        lock (_lock)
        {
            var associations = Load().ToList();
            if (associations.RemoveAll(a => a.Id == id) == 0)
            {
                return false;
            }

            Store(associations);
            return true;
        }
    }

    public bool MakeDefault(string id)
    {
        lock (_lock)
        {
            var associations = Load().ToList();
            var index = associations.FindIndex(a => a.Id == id);
            if (index < 0)
            {
                return false;
            }

            if (index > 0)
            {
                var association = associations[index];
                associations.RemoveAt(index);
                associations.Insert(0, association);
                Store(associations);
            }

            return true;
        }
    }

    private List<FileAssociation> Load()
    {
        var text = settingsService.Get(SettingsKey) ?? string.Empty;
        if (text == _parsedText)
        {
            return _parsed;
        }

        List<FileAssociation> parsed = [];
        if (text.Length > 0)
        {
            try
            {
                parsed = (JsonSerializer.Deserialize<List<FileAssociation>>(text, JsonOptions) ?? [])
                    .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(a.Command))
                    .ToList();
            }
            catch (JsonException)
            {
                // A setting edited by hand into something unreadable: no associations, rather than
                // an Enter key that throws on every file.
            }
        }

        _parsedText = text;
        _parsed = parsed;
        return parsed;
    }

    private void Store(List<FileAssociation> associations)
    {
        var text = JsonSerializer.Serialize(associations, JsonOptions);
        settingsService.Set(SettingsKey, text);
        _parsedText = text;
        _parsed = associations;
        Interlocked.Increment(ref _version);
    }
}
