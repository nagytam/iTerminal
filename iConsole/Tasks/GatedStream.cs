namespace iConsole.Tasks;

/// <summary>
/// A write-only stream that waits at its <see cref="PauseGate"/> before each asynchronous write, so a copy that writes through
/// it stops at its next chunk while its task is paused. Synchronous writes are passed through unchecked: nothing in the copy
/// path uses them. The inner stream is not disposed with this one; whoever made it still owns it.
/// </summary>
public sealed class GatedStream(Stream inner, PauseGate gate) : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await gate.WaitWhilePausedAsync(cancellationToken);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await gate.WaitWhilePausedAsync(cancellationToken);
        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }
}
