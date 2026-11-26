using iConsole.Localization;
using NfsSharp.Protocol;

namespace iTerminal.Core.Services.Nfs;

/// <summary>
/// One mounted export per (host, export) for the run, shared by both panels. A connect runs outside the lock, so an
/// unreachable host delays only its own panel. A server refusal (<see cref="NfsException"/>) leaves the mount alone;
/// a transport failure evicts it, disposes it best-effort and rethrows as an <see cref="IOException"/> naming the host,
/// so the next call mounts again.
/// </summary>
public sealed class NfsMountCache(INfsMountFactory factory) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string Host, string Export), Task<INfsMount>> _mounts = [];

    public Task<T> RunAsync<T>(string host, string export, Func<INfsMount, Task<T>> operation, CancellationToken cancellationToken) =>
        RunCoreAsync(host, export, operation, cancellationToken);

    public async Task ExecuteAsync(string host, string export, Func<INfsMount, Task> operation, CancellationToken cancellationToken) =>
        await RunCoreAsync(host, export, async mount =>
        {
            await operation(mount);
            return true;
        }, cancellationToken);

    private async Task<T> RunCoreAsync<T>(string host, string export, Func<INfsMount, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (host, export);
        var pending = await MountTaskAsync(key);
        INfsMount mount;
        try
        {
            mount = await pending.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await EvictAsync(key, pending);
            throw new IOException(string.Format(UiText.Get("Nfs.ConnectionFailed", "NFS connection to {0} failed: {1}"), host, exception.Message), exception);
        }

        try
        {
            return await operation(mount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NfsException exception)
        {
            // The server answered, so the connection is alive and every other panel on this export keeps its mount.
            throw new IOException(exception.Message, exception);
        }
        catch (Exception exception)
        {
            // A transport failure (socket, timeout, a disposed connection): the mount is dead, so it is evicted.
            await EvictAsync(key, pending);
            throw new IOException(string.Format(UiText.Get("Nfs.ConnectionFailed", "NFS connection to {0} failed: {1}"), host, exception.Message), exception);
        }
    }

    /// <summary>The mount for the key, starting the connect if there is none. The lock covers the dictionary only.</summary>
    private async Task<Task<INfsMount>> MountTaskAsync((string Host, string Export) key)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_mounts.TryGetValue(key, out var pending))
            {
                pending = StartMount(key);
                _mounts[key] = pending;
            }

            return pending;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Starts a connect. A factory that throws synchronously becomes a faulted task, so every failure takes the same path.</summary>
    private Task<INfsMount> StartMount((string Host, string Export) key)
    {
        try
        {
            return factory.MountAsync(key.Host, key.Export, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromException<INfsMount>(exception);
        }
    }

    private async Task EvictAsync((string Host, string Export) key, Task<INfsMount> pending)
    {
        await _gate.WaitAsync();
        try
        {
            if (_mounts.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
            {
                _mounts.Remove(key);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (pending.IsCompletedSuccessfully)
        {
            await DisposeQuietlyAsync(pending.Result);
        }
    }

    private static async Task DisposeQuietlyAsync(INfsMount mount)
    {
        try
        {
            await mount.DisposeAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Deliberately ignored: the connection is already dead, and the caller is told about the failure that killed it.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        List<Task<INfsMount>> pending;
        try
        {
            pending = [.. _mounts.Values];
            _mounts.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var task in pending.Where(task => task.IsCompletedSuccessfully))
        {
            await DisposeQuietlyAsync(task.Result);
        }
    }
}
