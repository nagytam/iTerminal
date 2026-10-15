using System.Net;
using System.Net.Sockets;
using System.Text;
using iTerminal.Core.Services.Mail;

namespace iTerminal.Core.Test.Services.Mail;

/// <summary>
/// Talks to a POP3 server on the loopback interface that answers from a script — so the client is tested over
/// real sockets, with the protocol's own replies, dot-stuffing and multi-line answers included.
/// </summary>
[TestClass]
public class Pop3ClientTest
{
    private sealed class FakePop3Server : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serve;
        public List<string> Received { get; } = [];

        public FakePop3Server()
        {
            _listener.Start();
            _serve = Task.Run(ServeOnce);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task ServeOnce()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await WriteAsync(stream, "+OK fake POP3 ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                Received.Add(line);
                var reply = line switch
                {
                    "USER ada" => "+OK user accepted",
                    "PASS secret" => "+OK logged in",
                    "PASS wrong" => "-ERR invalid password",
                    "LIST" => "+OK 2 messages\r\n1 420\r\n2 9000\r\n.",
                    "TOP 2 0" => "+OK headers follow\r\nSubject: Second\r\n\r\n.",
                    "RETR 1" => "+OK 420 octets\r\nSubject: First\r\n\r\n..a line that starts with a dot\r\n.",
                    "DELE 1" => "+OK marked for deletion",
                    "QUIT" => "+OK bye",
                    _ => "-ERR unknown command",
                };
                await WriteAsync(stream, reply);
                if (line == "QUIT")
                {
                    return;
                }
            }
        }

        private static Task WriteAsync(Stream stream, string text) => stream.WriteAsync(Encoding.UTF8.GetBytes(text + "\r\n")).AsTask();

        public void Dispose()
        {
            _listener.Stop();
            try
            {
                _serve.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // The script ends early when a test stops talking; that is what the test wanted.
            }
        }
    }

    [TestMethod]
    public async Task ListsTheMessagesWithTheirSizes()
    {
        using var server = new FakePop3Server();
        await using var pop = await Pop3Client.ConnectAsync("127.0.0.1", server.Port, implicitTls: false);
        await pop.LoginAsync("ada", "secret");

        var messages = await pop.ListAsync();

        CollectionAssert.AreEqual(new[] { (1, 420L), (2, 9000L) }, messages.ToArray());
        await pop.QuitAsync();
    }

    [TestMethod]
    public async Task ReadsTheHeadersOfOneMessageWithTop()
    {
        using var server = new FakePop3Server();
        await using var pop = await Pop3Client.ConnectAsync("127.0.0.1", server.Port, implicitTls: false);
        await pop.LoginAsync("ada", "secret");

        var headers = await pop.TopAsync(2);

        Assert.AreEqual("Subject: Second\r\n", headers);
        await pop.QuitAsync();
    }

    [TestMethod]
    public async Task UnstuffsDotsInARetrievedMessage()
    {
        using var server = new FakePop3Server();
        await using var pop = await Pop3Client.ConnectAsync("127.0.0.1", server.Port, implicitTls: false);
        await pop.LoginAsync("ada", "secret");

        var raw = await pop.RetrieveAsync(1);

        StringAssert.Contains(raw, "Subject: First\r\n\r\n.a line that starts with a dot");
        await pop.QuitAsync();
    }

    [TestMethod]
    public async Task AServerRefusalBecomesAnException()
    {
        using var server = new FakePop3Server();
        await using var pop = await Pop3Client.ConnectAsync("127.0.0.1", server.Port, implicitTls: false);

        Pop3Exception? error = null;
        try
        {
            await pop.LoginAsync("ada", "wrong");
        }
        catch (Pop3Exception ex)
        {
            error = ex;
        }

        Assert.IsNotNull(error);
        Assert.AreEqual("invalid password", error.Message);
    }

    [TestMethod]
    public async Task DeletionIsSentAndTheSessionEndsWithQuit()
    {
        using var server = new FakePop3Server();
        await using var pop = await Pop3Client.ConnectAsync("127.0.0.1", server.Port, implicitTls: false);
        await pop.LoginAsync("ada", "secret");

        await pop.DeleteAsync(1);
        await pop.QuitAsync();

        CollectionAssert.Contains(server.Received, "DELE 1");
        Assert.AreEqual("QUIT", server.Received[^1]);
    }
}
