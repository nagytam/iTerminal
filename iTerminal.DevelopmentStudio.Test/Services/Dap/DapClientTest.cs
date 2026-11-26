using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using iTerminal.DevelopmentStudio.Services.Dap;

namespace iTerminal.DevelopmentStudio.Test.Services.Dap;

/// <summary>
/// The client over two anonymous pipes: the test plays the adapter, reading the requests and writing responses and events in
/// the Content-Length framing the Debug Adapter Protocol uses.
/// </summary>
[TestClass]
public class DapClientTest
{
    private sealed class FakeAdapter(Stream reader, Stream writer)
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
    private sealed class Wiring : IDisposable
    {
        private readonly AnonymousPipeServerStream _adapterOut = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _adapterIn = new(PipeDirection.In);
        public Stream ClientReads { get; }
        public Stream ClientWrites { get; }
        public Stream AdapterReads { get; }
        public Stream AdapterWrites { get; }

        public Wiring()
        {
            ClientReads = new AnonymousPipeClientStream(PipeDirection.In, _adapterOut.ClientSafePipeHandle);
            ClientWrites = new AnonymousPipeClientStream(PipeDirection.Out, _adapterIn.ClientSafePipeHandle);
            AdapterReads = _adapterIn;
            AdapterWrites = _adapterOut;
        }

        public void Dispose()
        {
            ClientReads.Dispose();
            ClientWrites.Dispose();
            _adapterOut.Dispose();
            _adapterIn.Dispose();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARequestIsAnsweredWithItsResponseBody()
    {
        using var wiring = new Wiring();
        await using var client = new DapClient(wiring.ClientReads, wiring.ClientWrites, _ => { });
        client.Start();
        var adapter = new FakeAdapter(wiring.AdapterReads, wiring.AdapterWrites);

        var adapterTask = Task.Run(() =>
        {
            JsonNode request = adapter.ReadRequest();
            adapter.Write(new JsonObject
            {
                ["seq"] = 1,
                ["type"] = "response",
                ["request_seq"] = request["seq"]!.GetValue<int>(),
                ["success"] = true,
                ["command"] = request["command"]!.GetValue<string>(),
                ["body"] = new JsonObject { ["supportsConfigurationDoneRequest"] = true },
            });
        });

        JsonElement body = await client.RequestAsync("initialize", new JsonObject { ["clientID"] = "iterminal" });
        await adapterTask;

        Assert.IsTrue(body.GetProperty("supportsConfigurationDoneRequest").GetBoolean());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARefusedRequestThrowsWithTheAdaptersMessage()
    {
        using var wiring = new Wiring();
        await using var client = new DapClient(wiring.ClientReads, wiring.ClientWrites, _ => { });
        client.Start();
        var adapter = new FakeAdapter(wiring.AdapterReads, wiring.AdapterWrites);

        var adapterTask = Task.Run(() =>
        {
            JsonNode request = adapter.ReadRequest();
            adapter.Write(new JsonObject
            {
                ["type"] = "response",
                ["request_seq"] = request["seq"]!.GetValue<int>(),
                ["success"] = false,
                ["command"] = "launch",
                ["message"] = "No such file: tool.py",
            });
        });

        var exception = await Assert.ThrowsExactlyAsync<DapException>(() => client.RequestAsync("launch", new JsonObject()));
        await adapterTask;

        Assert.AreEqual("No such file: tool.py", exception.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AStoppedEventReachesTheCallback()
    {
        using var wiring = new Wiring();
        var events = new List<DapEvent>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new DapClient(wiring.ClientReads, wiring.ClientWrites, dapEvent =>
        {
            lock (events) events.Add(dapEvent);
            received.TrySetResult();
        });
        client.Start();
        var adapter = new FakeAdapter(wiring.AdapterReads, wiring.AdapterWrites);

        adapter.Write(new JsonObject
        {
            ["seq"] = 1,
            ["type"] = "event",
            ["event"] = "stopped",
            ["body"] = new JsonObject { ["threadId"] = 7, ["reason"] = "breakpoint" },
        });
        await received.Task.WaitAsync(TimeSpan.FromSeconds(20));

        DapEvent stopped;
        lock (events) stopped = events.Single();
        Assert.AreEqual("stopped", stopped.Name);
        Assert.AreEqual(7, stopped.Body.GetProperty("threadId").GetInt32());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AReverseRequestIsRefusedSoTheAdapterIsNotLeftWaiting()
    {
        using var wiring = new Wiring();
        await using var client = new DapClient(wiring.ClientReads, wiring.ClientWrites, _ => { });
        client.Start();
        var adapter = new FakeAdapter(wiring.AdapterReads, wiring.AdapterWrites);

        adapter.Write(new JsonObject { ["seq"] = 5, ["type"] = "request", ["command"] = "runInTerminal" });
        JsonNode refusal = await Task.Run(adapter.ReadRequest).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.AreEqual("response", refusal["type"]!.GetValue<string>());
        Assert.IsFalse(refusal["success"]!.GetValue<bool>());
        Assert.AreEqual(5, refusal["request_seq"]!.GetValue<int>());
    }
}
