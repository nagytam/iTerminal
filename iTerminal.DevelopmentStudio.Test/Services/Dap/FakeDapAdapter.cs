using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace iTerminal.DevelopmentStudio.Test.Services.Dap;

/// <summary>The test's end of a DAP conversation: reads the client's requests, and writes responses and events in the Content-Length framing.</summary>
internal sealed class FakeAdapter(Stream reader, Stream writer)
{
    public JsonNode ReadRequest()
    {
        int length = -1;
        while (true)
        {
            string line = ReadLine(reader);
            if (line.Length == 0) break;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line["Content-Length:".Length..].Trim());
        }

        var buffer = new byte[length];
        int read = 0;
        while (read < length) read += reader.Read(buffer, read, length - read);
        return JsonNode.Parse(Encoding.UTF8.GetString(buffer))!;
    }

    public void Write(JsonObject message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        writer.Write(header);
        writer.Write(body);
        writer.Flush();
    }

    private static string ReadLine(Stream stream)
    {
        var bytes = new List<byte>();
        int b;
        while ((b = stream.ReadByte()) != -1 && b != '\n')
        {
            if (b != '\r') bytes.Add((byte)b);
        }

        return Encoding.ASCII.GetString([.. bytes]);
    }
}

/// <summary>The pipes wired as the adapter sees them: the client reads what the adapter writes, and the reverse.</summary>
internal sealed class Wiring : IDisposable
{
    private readonly AnonymousPipeServerStream _adapterOut = new(PipeDirection.Out);
    private readonly AnonymousPipeServerStream _adapterIn = new(PipeDirection.In);

    public Wiring()
    {
        ClientReads = new AnonymousPipeClientStream(PipeDirection.In, _adapterOut.ClientSafePipeHandle);
        ClientWrites = new AnonymousPipeClientStream(PipeDirection.Out, _adapterIn.ClientSafePipeHandle);
        AdapterReads = _adapterIn;
        AdapterWrites = _adapterOut;
    }

    public Stream ClientReads { get; }
    public Stream ClientWrites { get; }
    public Stream AdapterReads { get; }
    public Stream AdapterWrites { get; }

    public void Dispose()
    {
        ClientReads.Dispose();
        ClientWrites.Dispose();
        _adapterOut.Dispose();
        _adapterIn.Dispose();
    }
}
