using iTerminal.Core.Model.Mail;

namespace iTerminal.Core.Services.Mail;

/// <summary>
/// Everything the mail dialog asks of a server: the inbox (POP3), sending (SMTP) and the address book
/// (LDAP). Each call is one self-contained conversation with the server, so nothing is held open between
/// them, and each is async by contract — the dialog runs them on a background task.
/// </summary>
public interface IMailService
{
    /// <summary>The most recent messages, newest first, with what the list shows of each.</summary>
    Task<IReadOnlyList<MailListing>> ListAsync(MailAccount account, string password, CancellationToken cancellationToken = default);

    Task<MailItem> ReadAsync(MailAccount account, string password, int number, CancellationToken cancellationToken = default);

    /// <summary>Marks a message for removal; the server removes it when the session ends.</summary>
    Task DeleteAsync(MailAccount account, string password, int number, CancellationToken cancellationToken = default);

    /// <summary>Sends a plain-text message. <paramref name="to"/> may name several addresses, separated by commas.</summary>
    Task SendAsync(MailAccount account, string password, string to, string subject, string body, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DirectoryPerson>> SearchDirectoryAsync(MailAccount account, string password, string term, CancellationToken cancellationToken = default);
}
