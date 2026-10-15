using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using iTerminal.Core.Model.Mail;

namespace iTerminal.Core.Services.Mail;

/// <summary>An LDAP server refused the bind or the search, or its reply was not one the protocol allows.</summary>
public sealed class LdapException(string message) : Exception(message);

/// <summary>
/// Searches an LDAP address book (RFC 4511): a simple bind, then a subtree search for people whose name or
/// mail address contains the text typed. Speaks the protocol directly rather than through a directory
/// library, which keeps the app's dependencies unchanged. Plain or implicit TLS (LDAPS).
/// </summary>
public static class LdapDirectoryClient
{
    private const int ResultSuccess = 0;
    private const int SizeLimit = 50;
    private const int TimeLimitSeconds = 15;

    private const byte BindRequest = 0x60;
    private const byte BindResponse = 0x61;
    private const byte SearchRequest = 0x63;
    private const byte SearchResultEntry = 0x64;
    private const byte SearchResultDone = 0x65;
    private const byte SearchResultReference = 0x73;

    public static async Task<IReadOnlyList<DirectoryPerson>> SearchAsync(
        MailAccount account, string password, string term, CancellationToken cancellationToken = default)
    {
        using var tcp = new TcpClient();
        using var registration = cancellationToken.Register(() => tcp.Dispose());
        await tcp.ConnectAsync(account.LdapHost, account.LdapPort, cancellationToken);

        Stream stream = tcp.GetStream();
        if (account.LdapTls)
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(account.LdapHost);
            stream = ssl;
        }

        await using var _ = stream;

        await stream.WriteAsync(BindMessage(1, account.LdapBindDn, password), cancellationToken);
        await ReadBindResultAsync(stream, cancellationToken);

        await stream.WriteAsync(SearchMessage(2, account.LdapBaseDn, term), cancellationToken);
        return await ReadEntriesAsync(stream, cancellationToken);
    }

    internal static byte[] BindMessage(int messageId, string bindDn, string password) =>
        Ber.Tlv(Ber.Sequence,
            Ber.Int(messageId),
            Ber.Tlv(BindRequest,
                Ber.Int(3),
                Ber.Str(bindDn),
                Ber.Tlv(0x80, Encoding.UTF8.GetBytes(password))));

    /// <summary>
    /// A subtree search for <paramref name="term"/> in the common name, the surname, the given name or the
    /// mail address. Substring filters take the text as-is: BER carries lengths, so nothing needs escaping.
    /// </summary>
    internal static byte[] SearchMessage(int messageId, string baseDn, string term)
    {
        byte[] Contains(string attribute) =>
            Ber.Tlv(0xA4, Ber.Str(attribute), Ber.Tlv(Ber.Sequence, Ber.Tlv(0x81, Encoding.UTF8.GetBytes(term))));

        var filter = Ber.Tlv(0xA1, Contains("cn"), Contains("sn"), Contains("givenName"), Contains("mail"));
        var attributes = Ber.Tlv(Ber.Sequence, Ber.Str("cn"), Ber.Str("mail"), Ber.Str("telephoneNumber"));

        return Ber.Tlv(Ber.Sequence,
            Ber.Int(messageId),
            Ber.Tlv(SearchRequest,
                Ber.Str(baseDn),
                Ber.Tlv(Ber.Enumerated, [2]),          // scope: whole subtree
                Ber.Tlv(Ber.Enumerated, [0]),          // derefAliases: never
                Ber.Int(SizeLimit),
                Ber.Int(TimeLimitSeconds),
                Ber.Bool(false),                       // typesOnly
                filter,
                attributes));
    }

    private static async Task ReadBindResultAsync(Stream stream, CancellationToken cancellationToken)
    {
        var (_, response) = await ReadMessageAsync(stream, cancellationToken);
        if (response.Tag != BindResponse)
        {
            throw new LdapException("The directory did not answer the bind.");
        }

        var code = Ber.Elements(response.Value)[0];
        if (Ber.DecodeInt(code.Value) != ResultSuccess)
        {
            throw new LdapException("The directory refused the bind — check the directory's user name and password.");
        }
    }

    private static async Task<IReadOnlyList<DirectoryPerson>> ReadEntriesAsync(Stream stream, CancellationToken cancellationToken)
    {
        var people = new List<DirectoryPerson>();
        while (true)
        {
            var (_, operation) = await ReadMessageAsync(stream, cancellationToken);
            if (operation.Tag == SearchResultEntry)
            {
                people.Add(PersonFrom(operation.Value));
            }
            else if (operation.Tag == SearchResultDone)
            {
                var code = Ber.DecodeInt(Ber.Elements(operation.Value)[0].Value);
                if (code != ResultSuccess && code != 4)  // 4: sizeLimitExceeded — the people found so far still stand
                {
                    throw new LdapException($"The directory search failed (result {code}).");
                }

                return people;
            }
            else if (operation.Tag != SearchResultReference)
            {
                throw new LdapException("The directory sent a reply the search did not expect.");
            }
        }
    }

    private static DirectoryPerson PersonFrom(byte[] entry)
    {
        var elements = Ber.Elements(entry);
        var dn = Ber.DecodeString(elements[0].Value);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in Ber.Elements(elements[1].Value))
        {
            var parts = Ber.Elements(attribute.Value);
            var name = Ber.DecodeString(parts[0].Value);
            var first = Ber.Elements(parts[1].Value);
            if (first.Count > 0)
            {
                values.TryAdd(name, Ber.DecodeString(first[0].Value));
            }
        }

        var common = values.GetValueOrDefault("cn") ?? dn;
        return new DirectoryPerson(common, values.GetValueOrDefault("mail") ?? string.Empty, values.GetValueOrDefault("telephoneNumber") ?? string.Empty);
    }

    private static async Task<(long MessageId, (byte Tag, byte[] Value) Operation)> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var (tag, value) = await Ber.ReadElementAsync(stream, cancellationToken);
        if (tag != Ber.Sequence)
        {
            throw new LdapException("The directory sent something that is not an LDAP message.");
        }

        var parts = Ber.Elements(value);
        return (Ber.DecodeInt(parts[0].Value), parts[1]);
    }
}
