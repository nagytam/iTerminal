using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using iConsole.Certificates;
using iConsole.Net;
using iConsole.Qr;

namespace iTerminal.Core.Services.Sharing;

public interface IFileShareService : IDisposable
{
    /// <summary>The current state. Replaced whole on every change, so a reader never sees a half-written one.</summary>
    FileShareState State { get; }

    /// <summary>Starts a share of the given local paths. A second start while one runs returns <c>FileTransfer.Busy</c>.</summary>
    Task<FileShareStartResult> StartAsync(IReadOnlyList<string> selectedPaths, CancellationToken cancellationToken = default);

    /// <summary>Stops the share: the listener and the connections are closed, and the wait for the accept loop is at most two seconds.</summary>
    Task StopAsync();
}

/// <summary>
/// The HTTPS file share. A session binds one port in 8080 to 8089 on all interfaces, serves the selected local paths
/// under a random token, and ends when stopped. Encoding, binding and the certificate happen on the background start;
/// the dialog only reads <see cref="State"/>.
/// </summary>
public sealed class FileShareService(IFileShareAddressSource addressSource) : IFileShareService
{
    internal const int FirstPort = 8080;
    internal const int LastPort = 8089;
    internal static readonly TimeSpan StopWait = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan CertificateValidity = TimeSpan.FromDays(1);
    private const int MaxConnections = 8;

    private readonly IFileShareAddressSource _addressSource = addressSource ?? throw new ArgumentNullException(nameof(addressSource));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile FileShareState _state = FileShareState.Stopped;
    private int _requests;
    private Task _lastStop = Task.CompletedTask;

    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task? _acceptLoop;
    private X509Certificate2? _certificate;

    public FileShareState State => _state;

    public async Task<FileShareStartResult> StartAsync(IReadOnlyList<string> selectedPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths);
        // A stop that was asked for before this start (the dialog closed, then reopened) finishes first.
        await _lastStop.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_listener is not null)
            {
                return FileShareStartResult.Failed("FileTransfer.Busy");
            }

            if (selectedPaths.Count == 0 || selectedPaths.Any(path => !File.Exists(path) && !Directory.Exists(path)))
            {
                SetState(FileShareStatus.Failed, null, null, "FileTransfer.NoSelection");
                return FileShareStartResult.Failed("FileTransfer.NoSelection");
            }

            if (selectedPaths.Any(path => path.Contains("@/", StringComparison.Ordinal)))
            {
                SetState(FileShareStatus.Failed, null, null, "FileTransfer.ContainsAt");
                return FileShareStartResult.Failed("FileTransfer.ContainsAt");
            }

            SetState(FileShareStatus.Starting, null, null, "FileTransfer.Starting");
            IPAddress? address = FileShareAddress.Choose(_addressSource.Candidates());
            if (address is null)
            {
                SetState(FileShareStatus.Failed, null, null, "FileTransfer.BindFailed");
                return FileShareStartResult.Failed("FileTransfer.BindFailed");
            }

            TcpListener? listener = BindFirstFreePort();
            if (listener is null)
            {
                SetState(FileShareStatus.Failed, null, null, "FileTransfer.BindFailed");
                return FileShareStartResult.Failed("FileTransfer.BindFailed");
            }

            X509Certificate2? certificate = null;
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string token = FileShareToken.Create();
                certificate = TlsCertificateFactory.CreateSelfSigned(address, DateTimeOffset.UtcNow, CertificateValidity);
                string url = $"https://{address}:{port}/{token}/";
                bool[,] qr = QrEncoder.Encode(Encoding.UTF8.GetBytes(url));

                CancellationTokenSource cancellation = new();
                FileShareRequestHandler handler = new(selectedPaths, token);
                _listener = listener;
                _certificate = certificate;
                _cancellation = cancellation;
                _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, certificate, handler, cancellation.Token));
                SetState(FileShareStatus.Running, url, qr, null);
                return new FileShareStartResult(true, url, null);
            }
            catch (Exception)
            {
                listener.Stop();
                certificate?.Dispose();
                SetState(FileShareStatus.Failed, null, null, "FileTransfer.BindFailed");
                return FileShareStartResult.Failed("FileTransfer.BindFailed");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StopAsync()
    {
        Task stop = StopSessionAsync();
        _lastStop = stop;
        return stop;
    }

    private async Task StopSessionAsync()
    {
        TcpListener? listener;
        CancellationTokenSource? cancellation;
        Task? acceptLoop;
        X509Certificate2? certificate;
        await _gate.WaitAsync();
        try
        {
            listener = _listener;
            cancellation = _cancellation;
            acceptLoop = _acceptLoop;
            certificate = _certificate;
            _listener = null;
            _cancellation = null;
            _acceptLoop = null;
            _certificate = null;
        }
        finally
        {
            _gate.Release();
        }

        cancellation?.Cancel();
        listener?.Stop();
        if (acceptLoop is not null)
        {
            // The accept loop ends when the listener stops. A read that cannot be cancelled is not awaited.
            await Task.WhenAny(acceptLoop, Task.Delay(StopWait));
        }

        cancellation?.Dispose();
        certificate?.Dispose();
        SetState(FileShareStatus.Stopped, null, null, null);
    }

    /// <summary>
    /// Shutdown: the listener and the session are closed now and nothing is awaited, so the container's synchronous
    /// disposal never blocks. <see cref="StopAsync"/> is the graceful path that the dialog uses.
    /// </summary>
    public void Dispose()
    {
        CancellationTokenSource? cancellation = _cancellation;
        TcpListener? listener = _listener;
        _cancellation = null;
        _listener = null;
        _acceptLoop = null;
        _certificate = null;
        X509Certificate2? certificate = _certificate;
        _certificate = null;
        cancellation?.Cancel();
        listener?.Stop();
        cancellation?.Dispose();
        certificate?.Dispose();
    }

    private static TcpListener? BindFirstFreePort()
    {
        for (int port = FirstPort; port <= LastPort; port++)
        {
            TcpListener listener = new(IPAddress.Any, port);
            try
            {
                listener.Start();
                return listener;
            }
            catch (SocketException)
            {
                listener.Stop();
            }
        }

        return null;
    }

    private async Task AcceptLoopAsync(TcpListener listener, X509Certificate2 certificate, FileShareRequestHandler handler, CancellationToken cancellationToken)
    {
        SemaphoreSlim connections = new(MaxConnections, MaxConnections);
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // A connection reset while accepting is not a reason to stop answering the others.
                await Task.Delay(100, CancellationToken.None);
                continue;
            }

            if (!connections.Wait(0))
            {
                client.Dispose();
                continue;
            }

            _ = Task.Run(() => ServeAsync(client, certificate, handler, cancellationToken))
                .ContinueWith(_ => connections.Release(), TaskScheduler.Default);
        }
    }

    private async Task ServeAsync(TcpClient client, X509Certificate2 certificate, FileShareRequestHandler handler, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                if (DateTimeOffset.UtcNow > certificate.NotAfter)
                {
                    return;
                }

                await using SslStream stream = new(client.GetStream(), leaveInnerStreamOpen: false);
                using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshake.CancelAfter(FileShareHttpTimeouts.Handshake);
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                }, handshake.Token);

                HttpReadResult read = await new HttpRequestReader(8192, FileShareHttpTimeouts.Request).ReadAsync(stream, cancellationToken);
                switch (read.Status)
                {
                    case HttpReadStatus.Ok when read.Request is not null:
                        await handler.WriteResponseAsync(read.Request.Method, read.Request.Target, stream, cancellationToken);
                        break;
                    case HttpReadStatus.TooLarge:
                        await handler.WriteErrorAsync(stream, 413, cancellationToken);
                        break;
                    case HttpReadStatus.Malformed:
                        await handler.WriteErrorAsync(stream, 400, cancellationToken);
                        break;
                }

                Interlocked.Increment(ref _requests);
                SetState(_state.Status, _state.Url, _state.QrModules, null);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A client that drops or sends garbage ends its own connection; the share keeps running.
            }
        }
    }

    private void SetState(FileShareStatus status, string? url, bool[,]? qr, string? message)
    {
        _state = new FileShareState(status, url ?? (status == FileShareStatus.Running ? _state.Url : null), qr ?? (status == FileShareStatus.Running ? _state.QrModules : null), Volatile.Read(ref _requests), message);
    }
}

/// <summary>The time limits of one connection, in one place for the share and its tests.</summary>
internal static class FileShareHttpTimeouts
{
    public static readonly TimeSpan Handshake = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Request = TimeSpan.FromSeconds(10);
}
