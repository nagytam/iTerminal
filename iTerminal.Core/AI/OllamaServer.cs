using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace iTerminal.Core.AI;

/// <summary>
/// An Ollama server at one address: whether it answers, starting one from an installed binary when nothing
/// does, downloading a model into it, and stopping the one this object started. A server that was already
/// running (a system service, or another iTerminal) is used as it is and never stopped.
/// </summary>
public sealed class OllamaServer
{
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);
    private const int MaxRecentLines = 20;

    private readonly HttpClient _http;
    private readonly Uri _address;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _gate = new();
    private readonly Queue<string> _recent = new();
    private Process? _started;

    public OllamaServer(Uri apiBaseUrl, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUrl);
        // The configured URL is the OpenAI-compatible ".../v1/"; the native API lives at the same authority.
        _address = new Uri(apiBaseUrl.GetLeftPart(UriPartial.Authority) + "/");
        // Pulls take minutes; each probe carries its own short timeout instead.
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>True when the address is this computer, the only place a server is started.</summary>
    public bool IsLocal => _address.IsLoopback;

    /// <summary>Whether something answers the native API at the address.</summary>
    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            using HttpResponseMessage response = await _http.GetAsync(new Uri(_address, "api/version"), timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Makes sure a server answers: returns false at once when one already does, otherwise starts
    /// <c>ollama serve</c> from <paramref name="binaryPath"/> with its models in <paramref name="modelsDirectory"/>
    /// and waits for it to answer. Returns true when this call started it.
    /// </summary>
    public async Task<bool> EnsureRunningAsync(string binaryPath, string modelsDirectory, CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (await IsReachableAsync(cancellationToken))
            {
                return false;
            }

            if (!IsLocal)
            {
                throw new InvalidOperationException($"No Ollama server answers at {_address.GetLeftPart(UriPartial.Authority)}, and that is not this computer, so it is not started here.");
            }

            Process process = Start(binaryPath, modelsDirectory);
            lock (_gate)
            {
                _started = process;
            }

            DateTime deadline = DateTime.UtcNow + StartupTimeout;
            while (!await IsReachableAsync(cancellationToken))
            {
                if (process.HasExited)
                {
                    Stop();
                    throw new InvalidOperationException($"Ollama stopped while starting: {RecentOutput()}");
                }

                if (DateTime.UtcNow > deadline)
                {
                    Stop();
                    throw new TimeoutException($"Ollama did not answer within {StartupTimeout.TotalSeconds:0} seconds.");
                }

                await Task.Delay(PollInterval, cancellationToken);
            }

            return true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <summary>
    /// Downloads a model into the server, as <c>ollama pull</c> does, and reports its progress. Ollama reports
    /// failures (an unknown model, no network) as a line of its own, which is raised here.
    /// </summary>
    public async Task PullAsync(string model, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_address, "api/pull"))
        {
            Content = JsonContent.Create(new { model, stream = true }),
        };
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        bool succeeded = false;
        var clock = Stopwatch.StartNew();
        TimeSpan? lastReport = null;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("error", out JsonElement error))
            {
                throw new InvalidOperationException($"Ollama could not download {model}: {error.GetString()}");
            }

            string status = root.TryGetProperty("status", out JsonElement statusElement) ? statusElement.GetString() ?? string.Empty : string.Empty;
            if (status == "success")
            {
                succeeded = true;
            }

            if (progress is null || (lastReport is not null && clock.Elapsed - lastReport.Value < ProgressInterval && status != "success"))
            {
                continue;
            }

            lastReport = clock.Elapsed;
            if (root.TryGetProperty("total", out JsonElement total) && root.TryGetProperty("completed", out JsonElement completed) && total.GetInt64() > 0)
            {
                progress.Report($"{status} {Megabytes(completed.GetInt64())} of {Megabytes(total.GetInt64())} MB");
            }
            else
            {
                progress.Report(status);
            }
        }

        if (!succeeded)
        {
            throw new InvalidOperationException($"Ollama did not finish downloading {model}.");
        }
    }

    /// <summary>Stops the server this object started, if it is still running. A server that was already there is left alone.</summary>
    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            process = _started;
            _started = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // It exited between the check and the kill.
        }
        finally
        {
            process.Dispose();
        }
    }

    private Process Start(string binaryPath, string modelsDirectory)
    {
        Directory.CreateDirectory(modelsDirectory);
        var start = new ProcessStartInfo(binaryPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(binaryPath),
        };
        start.ArgumentList.Add("serve");
        start.Environment["OLLAMA_HOST"] = $"{_address.Host}:{_address.Port}";
        start.Environment["OLLAMA_MODELS"] = modelsDirectory;

        Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"The Ollama program could not be started: {binaryPath}");

        // The server writes its log to both streams; they are read to the end so it never blocks on a full pipe.
        process.OutputDataReceived += (_, e) => Remember(e.Data);
        process.ErrorDataReceived += (_, e) => Remember(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void Remember(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_gate)
        {
            _recent.Enqueue(line.Trim());
            while (_recent.Count > MaxRecentLines)
            {
                _recent.Dequeue();
            }
        }
    }

    private string RecentOutput()
    {
        lock (_gate)
        {
            return _recent.Count == 0 ? "no output" : string.Join(" | ", _recent.TakeLast(3));
        }
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0");
}
