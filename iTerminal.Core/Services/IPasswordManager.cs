using iTerminal.Core.Model.Passwords;

namespace iTerminal.Core.Services;

/// <summary>
/// The password manager: every password the app keeps for a network or a service, sealed under a master
/// password the person types when the app starts. Features ask for a password by an id they own
/// (<see cref="GetSecret"/>); the dialog lists and edits the whole set. While the manager is locked, nothing
/// can be read or written, and reads answer null rather than throw, so a feature can say "unlock first".
/// </summary>
public interface IPasswordManager
{
    /// <summary>True once a master password has been chosen.</summary>
    bool IsSetUp { get; }

    bool IsUnlocked { get; }

    /// <summary>
    /// Chooses the master password and creates the store. Passwords the vault held before this — written
    /// before there was a master password — are brought across at the same time.
    /// </summary>
    void Setup(string masterPassword);

    /// <summary>False when the master password is wrong.</summary>
    bool Unlock(string masterPassword);

    /// <summary>
    /// Keeps the key of the unlocked store sealed on this machine for <paramref name="lifetime"/>, so
    /// <see cref="UnlockRemembered"/> can open it without the master password until then. Throws while locked.
    /// </summary>
    void Remember(TimeSpan lifetime);

    /// <summary>
    /// Unlocks from a remembered key that has not expired. An expired or unreadable one is forgotten and false
    /// is returned, so the master password is asked for again.
    /// </summary>
    bool UnlockRemembered();

    /// <summary>Forgets the key and the passwords in memory. They are sealed on disk, so the store is unreadable again. A remembered key is forgotten too.</summary>
    void Lock();

    /// <summary>Re-seals every entry under a key from the new master password. The current one must be given.</summary>
    void ChangeMasterPassword(string currentPassword, string newPassword);

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
