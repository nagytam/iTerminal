using System.Text.Json;
using iTerminal.Core.Services;

namespace iTerminal.Core.Model.Contacts;

/// <summary>
/// One person or organisation: a name (the only required field), an email address, a phone number, a company
/// and free notes. Immutable, so a change is a <c>with</c> copy written back by <see cref="ContactStore.Update"/>.
/// </summary>
public sealed record Contact(Guid Id, string Name, string Email, string Phone, string Company, string Notes)
{
    /// <summary>True when <paramref name="query"/> (any part of it, any case) appears in one of the fields.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(word =>
            Name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Email.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Phone.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Company.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Notes.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Checks what the person typed: a name is needed, an email needs an @ with text on both sides, a phone number needs a digit.</summary>
    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "A contact needs a name";
            return false;
        }

        if (Email.Length > 0)
        {
            var at = Email.IndexOf('@');
            if (at <= 0 || at == Email.Length - 1 || Email.Contains(' ') || Email.IndexOf('@', at + 1) >= 0)
            {
                error = $"\"{Email}\" is not an email address, e.g. name@example.com";
                return false;
            }
        }

        if (Phone.Length > 0 && (!Phone.Any(char.IsDigit) || Phone.Any(c => !char.IsDigit(c) && "+-() ./".IndexOf(c) < 0)))
        {
            error = $"\"{Phone}\" is not a phone number; use digits with + - ( ) . / or spaces";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>The same contact with every text field trimmed.</summary>
    public Contact Trimmed() => this with
    {
        Name = Name.Trim(),
        Email = Email.Trim(),
        Phone = Phone.Trim(),
        Company = Company.Trim(),
        Notes = Notes.Trim(),
    };
}

/// <summary>
/// The contacts the person keeps, as one JSON list in the settings table under <see cref="SettingsKey"/>. Kept
/// out of the database for the same reason as the tasks and meetings: a new table would change the schema
/// stamp and clear every cached table at start-up.
/// </summary>
public sealed class ContactStore(ISettingsService settingsService)
{
    public const string SettingsKey = "Contacts";

    private static readonly JsonSerializerOptions Json = new();

    private readonly List<Contact> _contacts = [];
    private bool _loaded;

    /// <summary>Every contact whose fields match <paramref name="filter"/> (all of them when it is empty), by name.</summary>
    public IReadOnlyList<Contact> Find(string? filter = null)
    {
        EnsureLoaded();
        return _contacts
            .Where(c => c.Matches(filter ?? string.Empty))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int Count
    {
        get
        {
            EnsureLoaded();
            return _contacts.Count;
        }
    }

    public void Add(Contact contact)
    {
        EnsureLoaded();
        _contacts.Add(contact);
        Save();
    }

    /// <summary>Replaces the contact with the same <see cref="Contact.Id"/>.</summary>
    public void Update(Contact contact)
    {
        EnsureLoaded();
        var index = _contacts.FindIndex(c => c.Id == contact.Id);
        if (index < 0)
        {
            return;
        }

        _contacts[index] = contact;
        Save();
    }

    public void Remove(Guid id)
    {
        EnsureLoaded();
        if (_contacts.RemoveAll(c => c.Id == id) > 0)
        {
            Save();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var json = settingsService.Get(SettingsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                // A null entry ("[null]") is not a contact, and an older entry may lack a field; the rest is still good.
                _contacts.AddRange((JsonSerializer.Deserialize<List<Contact>>(json, Json) ?? [])
                    .Where(contact => contact is not null)
                    .Select(c => c with { Name = c.Name ?? string.Empty, Email = c.Email ?? string.Empty, Phone = c.Phone ?? string.Empty, Company = c.Company ?? string.Empty, Notes = c.Notes ?? string.Empty }));
            }
        }
        catch (JsonException)
        {
            // A damaged value must not stop the list opening; the next save replaces it.
        }
    }

    private void Save()
    {
        settingsService.Set(SettingsKey, JsonSerializer.Serialize(_contacts, Json));
        settingsService.Write();
    }
}
