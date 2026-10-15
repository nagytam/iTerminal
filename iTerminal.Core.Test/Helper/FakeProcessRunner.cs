using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// An <see cref="IExternalProcessRunner"/> that answers from a table instead of running anything.
/// A response is chosen by the first rule whose predicate matches the command line (the tool name
/// followed by its arguments, space-joined); every call is recorded, with any stdin it was given.
/// Calls come from background tasks, so the record is locked.
/// </summary>
internal sealed class FakeProcessRunner : IExternalProcessRunner
{
    public sealed record Call(string FileName, IReadOnlyList<string> Arguments, byte[] StandardInput)
    {
        public string CommandLine => string.Join(' ', [FileName, .. Arguments]);
    }

    private readonly List<(Func<string, bool> Matches, Func<ProcessOutput> Text, Func<byte[]>? Bytes)> _rules = [];
    private readonly List<Call> _calls = [];

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public FakeProcessRunner Answer(string commandLinePrefix, string stdout, int exitCode = 0, string stderr = "")
    {
        _rules.Add((line => line.StartsWith(commandLinePrefix, StringComparison.Ordinal), () => new ProcessOutput(exitCode, stdout, stderr), null));
        return this;
    }

    /// <summary>Answers with whatever <paramref name="stdout"/> returns at the time of the call — for a tool whose answer changes, like <c>docker ps</c> after a stop.</summary>
    public FakeProcessRunner AnswerWith(string commandLinePrefix, Func<string> stdout)
    {
        _rules.Add((line => line.StartsWith(commandLinePrefix, StringComparison.Ordinal), () => new ProcessOutput(0, stdout(), string.Empty), null));
        return this;
    }

    public FakeProcessRunner AnswerBytes(string commandLinePrefix, byte[] stdout, int exitCode = 0, string stderr = "")
    {
        _rules.Add((line => line.StartsWith(commandLinePrefix, StringComparison.Ordinal), () => new ProcessOutput(exitCode, string.Empty, stderr), () => stdout));
        return this;
    }

    public Task<ProcessOutput> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        var call = Record(fileName, arguments, []);
        var rule = Find(call);
        return Task.FromResult(rule?.Text() ?? new ProcessOutput(1, string.Empty, $"no fake answer for: {call.CommandLine}"));
    }

    public async Task<ProcessOutput> RunToStreamAsync(string fileName, IReadOnlyList<string> arguments, Stream destination, string? workingDirectory = null, Func<Stream, Task>? writeStandardInput = null, CancellationToken cancellationToken = default)
    {
        byte[] stdin = [];
        if (writeStandardInput is not null)
        {
            using var buffer = new MemoryStream();
            await writeStandardInput(buffer);
            stdin = buffer.ToArray();
        }

        var call = Record(fileName, arguments, stdin);
        var rule = Find(call);
        if (rule is null)
        {
            return new ProcessOutput(1, string.Empty, $"no fake answer for: {call.CommandLine}");
        }

        if (rule.Value.Bytes is not null)
        {
            await destination.WriteAsync(rule.Value.Bytes(), cancellationToken);
        }

        return rule.Value.Text();
    }

    private Call Record(string fileName, IReadOnlyList<string> arguments, byte[] stdin)
    {
        var call = new Call(fileName, [.. arguments], stdin);
        lock (_calls)
        {
            _calls.Add(call);
        }

        return call;
    }

    private (Func<string, bool> Matches, Func<ProcessOutput> Text, Func<byte[]>? Bytes)? Find(Call call)
    {
        foreach (var rule in _rules)
        {
            if (rule.Matches(call.CommandLine))
            {
                return rule;
            }
        }

        return null;
    }
}
