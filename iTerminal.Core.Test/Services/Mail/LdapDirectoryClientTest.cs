using System.Net;
using System.Net.Sockets;
using System.Text;
using iTerminal.Core.Model.Mail;
using iTerminal.Core.Services.Mail;

namespace iTerminal.Core.Test.Services.Mail;

[TestClass]
public class LdapDirectoryClientTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow(0, new byte[] { 0x00 })]
    [DataRow(127, new byte[] { 0x7F })]
    [DataRow(128, new byte[] { 0x00, 0x80 })]
    [DataRow(256, new byte[] { 0x01, 0x00 })]
    [DataRow(-1, new byte[] { 0xFF })]
    [DataRow(-129, new byte[] { 0xFF, 0x7F })]
    public void IntegersAreEncodedMinimallyAsTwosComplement(long value, byte[] content)
    {
        var encoded = Ber.Int(value);

        CollectionAssert.AreEqual(new byte[] { 0x02, (byte)content.Length }.Concat(content).ToArray(), encoded);
        Assert.AreEqual(value, Ber.DecodeInt(content));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LongLengthsUseTheLongForm()
    {
        var encoded = Ber.Tlv(Ber.OctetString, new byte[200]);

        CollectionAssert.AreEqual(new byte[] { 0x04, 0x81, 0xC8 }, encoded[..3]);
        Assert.AreEqual(203, encoded.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BindsAndReturnsThePeopleFound()
    {
        using var server = new FakeLdapServer();
        var account = new MailAccount { LdapHost = "127.0.0.1", LdapPort = server.Port, LdapTls = false, LdapBaseDn = "dc=example,dc=test", LdapBindDn = "cn=reader" };

        var people = await LdapDirectoryClient.SearchAsync(account, "readpw", "ada");

        Assert.AreEqual(1, people.Count);
        Assert.AreEqual(new DirectoryPerson("Ada Lovelace", "ada@example.test", "+44 20 0000"), people[0]);
        Assert.AreEqual("readpw", server.BindPassword);
        Assert.AreEqual("cn=reader", server.BindName);
        StringAssert.Contains(server.SearchRequestText, "ada");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARefusedBindIsReportedAsSuch()
    {
        using var server = new FakeLdapServer { RefuseBind = true };
        var account = new MailAccount { LdapHost = "127.0.0.1", LdapPort = server.Port, LdapTls = false, LdapBaseDn = "dc=example,dc=test" };

        LdapException? error = null;
        try
        {
            await LdapDirectoryClient.SearchAsync(account, "wrong", "ada");
        }
        catch (LdapException ex)
        {
            error = ex;
        }

        Assert.IsNotNull(error);
        StringAssert.Contains(error.Message, "refused the bind");
    }

    /// <summary>An LDAP server on the loopback interface that answers one bind and one search.</summary>
    private sealed class FakeLdapServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serve;

        public FakeLdapServer()
        {
            _listener.Start();
            _serve = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public bool RefuseBind { get; init; }
        public string? BindName { get; private set; }
        public string? BindPassword { get; private set; }
        public string SearchRequestText { get; private set; } = string.Empty;

        private async Task ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var cancellation = CancellationToken.None;

            var (_, bind) = await ReadMessageAsync(stream, cancellation);
            var bindParts = Ber.Elements(bind.Value);
            BindName = Encoding.UTF8.GetString(Ber.Elements(bind.Value)[1].Value);
            BindPassword = Encoding.UTF8.GetString(bindParts[2].Value);
            var code = RefuseBind ? 49 : 0;
            await stream.WriteAsync(Ber.Tlv(Ber.Sequence, Ber.Int(1), Ber.Tlv(0x61, Ber.Tlv(Ber.Enumerated, [(byte)code]), Ber.Str(string.Empty), Ber.Str(string.Empty))), cancellation);
            if (RefuseBind)
            {
                return;
            }

            var (_, search) = await ReadMessageAsync(stream, cancellation);
            SearchRequestText = Encoding.UTF8.GetString(search.Value);

            var attributes = Ber.Tlv(Ber.Sequence,
                Ber.Tlv(Ber.Sequence, Ber.Str("cn"), Ber.Tlv(Ber.Set, Ber.Str("Ada Lovelace"))),
                Ber.Tlv(Ber.Sequence, Ber.Str("mail"), Ber.Tlv(Ber.Set, Ber.Str("ada@example.test"))),
                Ber.Tlv(Ber.Sequence, Ber.Str("telephoneNumber"), Ber.Tlv(Ber.Set, Ber.Str("+44 20 0000"))));
            var entry = Ber.Tlv(Ber.Sequence, Ber.Int(2), Ber.Tlv(0x64, Ber.Str("cn=Ada,dc=example,dc=test"), attributes));
            var done = Ber.Tlv(Ber.Sequence, Ber.Int(2), Ber.Tlv(0x65, Ber.Tlv(Ber.Enumerated, [0]), Ber.Str(string.Empty), Ber.Str(string.Empty)));
            await stream.WriteAsync(Ber.Concat(entry, done), cancellation);
        }

        private static async Task<(long Id, (byte Tag, byte[] Value) Operation)> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
        {
            var (_, value) = await Ber.ReadElementAsync(stream, cancellationToken);
            var parts = Ber.Elements(value);
            return (Ber.DecodeInt(parts[0].Value), parts[1]);
        }

        public void Dispose()
        {
            _listener.Stop();
            try
            {
                _serve.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // The script stops early when the client does; that is the outcome the test wanted.
            }
        }
    }
}
