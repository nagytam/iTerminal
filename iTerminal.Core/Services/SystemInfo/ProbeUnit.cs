namespace iTerminal.Core.Services.SystemInfo;

/// <summary>
/// The time source a probe's budget and the sampler's pauses are measured on. Production code waits with
/// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; tests release each wait by hand.
/// </summary>
public interface ISampleClock
{
    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemSampleClock : ISampleClock
{
    public Task Delay(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

/// <summary>
/// One probe. At most one call of it is outstanding at a time. A call that outlives its budget is abandoned: until it
/// returns, the probe reports <see cref="ProbeState.NotAnswering"/> and starts nothing new, and when it returns its
/// result is discarded and the next run calls the probe again. A call that never returns therefore holds one thread,
/// and no more, for as long as the unit lives.
/// </summary>
public sealed class ProbeUnit<T>(Func<T> probe)
{
    private Task<T>? _abandoned;

    public async Task<Probed<T>> RunAsync(ISampleClock clock, TimeSpan budget, CancellationToken cancellationToken)
    {
        if (_abandoned is { } previous)
        {
            if (!previous.IsCompleted)
            {
                return Probed<T>.NotAnswering;
            }

            // Its late result is discarded; observe any exception so it is not left unobserved.
            _ = previous.Exception;
            _abandoned = null;
        }

        Task<T> call = Task.Run(probe, CancellationToken.None);
        Task waited = await Task.WhenAny(call, clock.Delay(budget, cancellationToken));
        if (waited == call)
        {
            return call.IsCompletedSuccessfully ? Probed<T>.Ok(call.Result) : Probed<T>.NotReported;
        }

        _abandoned = call;
        return Probed<T>.NotAnswering;
    }
}

/// <summary>
/// A value read once, until it answers. The first run starts the probe without waiting for it, so the caller can
/// publish everything else on time; a value that came back NotReported is asked for again on a later run.
/// </summary>
public sealed class IdentityUnit<T>(ProbeUnit<T> unit)
{
    private Task<Probed<T>>? _pending;
    private Probed<T> _value = Probed<T>.Measuring;

    public Probed<T> Current(ISampleClock clock, TimeSpan budget, CancellationToken cancellationToken)
    {
        if (_value.State == ProbeState.Ok)
        {
            return _value;
        }

        _pending ??= unit.RunAsync(clock, budget, cancellationToken);
        if (_pending.IsCompleted)
        {
            _value = _pending.IsCompletedSuccessfully ? _pending.Result : Probed<T>.NotReported;
            _pending = null;
        }

        return _value;
    }
}
