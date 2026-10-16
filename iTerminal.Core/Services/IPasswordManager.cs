using iTerminal.Core.Model.Passwords;

namespace iTerminal.Core.Services;

/// <summary>
/// The password manager: every password the app keeps for a network or a service, sealed under a key the app
/// generates and keeps on this machine, so nobody types a master password. Features ask for a password by an id
/// they own (<see cref="GetSecret"/>); the dialog lists and edits the whole set. While the manager is locked,
/// nothing can be read or written, and reads answer null rather than throw, so a feature can say "unlock first".
/// </summary>
public interface IPasswordManager
{
    bool IsUnlocked { get; }

    /// <summary>
    /// Opens the store with its generated key, creating the store and the key on the first call. Passwords the
    /// vault held before this are brought across when it is created. False when the key cannot be recovered, for
    /// example because the Data Protection keys it was sealed with are gone.
    /// </summary>
    bool Unlock();

    /// <summary>Forgets the key and the passwords in memory. They are sealed on disk, so the store is unreadable until <see cref="Unlock"/>.</summary>
    void Lock();

    /// <summary>Every entry, by title. Empty while locked.</summary>
    IReadOnlyList<PasswordEntry> List();

    PasswordEntry? Get(string id);

    /// <summary>Adds or replaces an entry. Throws while locked.</summary>
    void Save(PasswordEntry entry);

    void Delete(string id);

    /// <summary>The password saved under <paramref name="profileId"/>, or null when there is none or the manager is locked.</summary>
    string? GetSecret(string profileId);

    /// <summary>Saves a password under <paramref name="profileId"/>, keeping the entry's title when it already has one.</summary>
    void SaveSecret(string profileId, string secret);

    void DeleteSecret(string profileId);
}
