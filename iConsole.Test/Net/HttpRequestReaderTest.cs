using System.Text;
using iConsole.Net;

namespace iConsole.Test.Net;

[TestClass]
public class HttpRequestReaderTest
{
    [TestMethod]
    public async Task ReadAsync_ValidGet_ParsesMethodAndTarget()
    {
        using MemoryStream stream = Head("GET /token/file.txt HTTP/1.1\r\nHost: x\r\n\r\n");

        HttpReadResult result = await new HttpRequestReader().ReadAsync(stream);

        Assert.AreEqual(HttpReadStatus.Ok, result.Status);
        Assert.AreEqual("GET", result.Request!.Method);
        Assert.AreEqual("/token/file.txt", result.Request.Target);
    }

    [TestMethod]
    public async Task ReadAsync_Head_IsParsedAsAnyMethod()
    {
        using MemoryStream stream = Head("HEAD / HTTP/1.1\r\n\r\n");

        HttpReadResult result = await new HttpRequestReader().ReadAsync(stream);

        Assert.AreEqual(HttpReadStatus.Ok, result.Status);
        Assert.AreEqual("HEAD", result.Request!.Method);
    }

    [TestMethod]
    public async Task ReadAsync_HeadLongerThanTheCap_IsTooLarge()
    {
        string head = "GET / HTTP/1.1\r\nX-Pad: " + new string('a', 200) + "\r\n\r\n";
        using MemoryStream stream = Head(head);

        HttpReadResult result = await new HttpRequestReader(maxBytes: 64).ReadAsync(stream);

        Assert.AreEqual(HttpReadStatus.TooLarge, result.Status);
        Assert.IsNull(result.Request);
    }

    [TestMethod]
    [DataRow("GET HTTP/1.1\r\n\r\n")]
    [DataRow("GET / SPDY/3\r\n\r\n")]
    [DataRow("GET relative HTTP/1.1\r\n\r\n")]
    [DataRow("GET / HTTP/1.1 extra\r\n\r\n")]
    public async Task ReadAsync_MalformedRequestLine_IsMalformed(string head)
    {
        using MemoryStream stream = Head(head);

        HttpReadResult result = await new HttpRequestReader().ReadAsync(stream);

        Assert.AreEqual(HttpReadStatus.Malformed, result.Status);
    }

    [TestMethod]
    public async Task ReadAsync_ClientSilent_TimesOutInsteadOfWaiting()
    {
        HttpRequestReader reader = new(readTimeout: TimeSpan.FromMilliseconds(50));

        HttpReadResult result = await reader.ReadAsync(new SilentStream());

        Assert.AreEqual(HttpReadStatus.TimedOut, result.Status);
    }

    [TestMethod]
    public async Task ReadAsync_ClientClosesBeforeTheBlankLine_TimesOut()
    {
        using MemoryStream stream = Head("GET / HTTP/1.1\r\nHost: x");

        HttpReadResult result = await new HttpRequestReader().ReadAsync(stream);

        Assert.AreEqual(HttpReadStatus.TimedOut, result.Status);
    }

    private static MemoryStream Head(string text) => new(Encoding.ASCII.GetBytes(text));

    /// <summary>A client that sends nothing and never closes.</summary>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
