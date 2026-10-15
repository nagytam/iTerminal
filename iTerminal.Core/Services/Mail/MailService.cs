using System.Net;
using System.Net.Mail;
using iTerminal.Core.Model.Mail;

namespace iTerminal.Core.Services.Mail;

/// <summary>
/// The real <see cref="IMailService"/>: POP3 for the inbox, the framework's SMTP client for sending, and
/// <see cref="LdapDirectoryClient"/> for the address book. The inbox lists the newest
/// <see cref="ListLimit"/> messages only — a list of every message in a large inbox would take minutes to
/// read the headers of, and the newest are the ones anyone is looking for.
/// </summary>
public sealed class MailService : IMailService
{
    public const int ListLimit = 50;

    private const int SmtpTimeoutMilliseconds = 30_000;

    public async Task<IReadOnlyList<MailListing>> ListAsync(MailAccount account, string password, CancellationToken cancellationToken = default)
    {
        await using var pop = await OpenInboxAsync(account, password, cancellationToken);
        var messages = await pop.ListAsync();
        var newest = messages.OrderByDescending(m => m.Number).Take(ListLimit).ToList();

        var listings = new List<MailListing>(newest.Count);
        foreach (var (number, size) in newest)
        {
            listings.Add(MimeParser.ParseListing(number, size, await HeadersAsync(pop, number)));
        }

        await pop.QuitAsync();
        return listings;
    }

    public async Task<MailItem> ReadAsync(MailAccount account, string password, int number, CancellationToken cancellationToken = default)
    {
        await using var pop = await OpenInboxAsync(account, password, cancellationToken);
        var raw = await pop.RetrieveAsync(number);
        await pop.QuitAsync();
        return MimeParser.Parse(number, raw);
    }

    public async Task DeleteAsync(MailAccount account, string password, int number, CancellationToken cancellationToken = default)
    {
        await using var pop = await OpenInboxAsync(account, password, cancellationToken);
        await pop.DeleteAsync(number);
        await pop.QuitAsync();
    }

    public async Task SendAsync(MailAccount account, string password, string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.SmtpHost))
        {
            throw new InvalidOperationException("No outgoing (SMTP) server is set up for this account.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(account.FromAddress),
            Subject = subject,
            Body = body,
        };
        foreach (var recipient in to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            message.To.Add(recipient);
        }

        if (message.To.Count == 0)
        {
            throw new InvalidOperationException("The message has no recipient.");
        }

        using var smtp = new SmtpClient(account.SmtpHost, account.SmtpPort)
        {
            EnableSsl = account.SmtpTls,
            Timeout = SmtpTimeoutMilliseconds,
        };
        if (!string.IsNullOrEmpty(account.UserName))
        {
            smtp.Credentials = new NetworkCredential(account.UserName, password);
        }

        await smtp.SendMailAsync(message, cancellationToken);
    }

    public Task<IReadOnlyList<DirectoryPerson>> SearchDirectoryAsync(MailAccount account, string password, string term, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.LdapHost))
        {
            throw new InvalidOperationException("No address book (LDAP) server is set up for this account.");
        }

        return LdapDirectoryClient.SearchAsync(account, password, term, cancellationToken);
    }

    private static async Task<Pop3Client> OpenInboxAsync(MailAccount account, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.PopHost))
        {
            throw new InvalidOperationException("No incoming (POP3) server is set up for this account.");
        }

        var pop = await Pop3Client.ConnectAsync(account.PopHost, account.PopPort, account.PopTls, cancellationToken);
        try
        {
            await pop.LoginAsync(account.UserName, password);
            return pop;
        }
        catch
        {
            await pop.DisposeAsync();
            throw;
        }
    }

    /// <summary>The headers of one message: TOP where the server offers it, else the whole message read and cut at the headers.</summary>
    private static async Task<string> HeadersAsync(Pop3Client pop, int number)
    {
        try
        {
            return await pop.TopAsync(number);
        }
        catch (Pop3Exception)
        {
            var raw = await pop.RetrieveAsync(number);
            return MimeParser.SplitHeaders(raw).Headers;
        }
    }
}
