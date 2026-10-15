namespace iConsole.Services;

public interface IStopwatch
{
    void Start();
    void Stop();
    void Restart();
    TimeSpan Elapsed { get; }
    long ElapsedMilliseconds { get; }
    bool IsRunning { get; }
}
