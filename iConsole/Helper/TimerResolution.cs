using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace iConsole.Helper;

/// <summary>
/// Raises the Windows multimedia timer resolution for the lifetime of this instance.
/// </summary>
/// <remarks>
/// On Windows the default system timer resolution is ~15.6 ms, so a <c>Thread.Sleep(1)</c>
/// in the render and input-polling loops actually parks the thread for a full system tick.
/// That throttles keyboard polling, screen refresh and the clock update to ~64 Hz worst case,
/// which is felt as laggy navigation and a stuttering clock. Requesting a 1 ms period makes
/// those <c>Sleep(1)</c> calls behave as intended. The request is process-wide and must be
/// balanced with <c>timeEndPeriod</c>, which <see cref="Dispose"/> handles.
/// On non-Windows platforms this is a no-op (POSIX <c>Thread.Sleep(1)</c> is already ~1 ms).
/// </remarks>
public sealed class TimerResolution : IDisposable
{
    private const uint TIMERR_NOERROR = 0;

    private readonly uint _period;
    private bool _isActive;

    public TimerResolution(uint milliseconds = 1)
    {
        _period = milliseconds;
        if (OperatingSystem.IsWindows())
        {
            _isActive = TimeBeginPeriod(_period) == TIMERR_NOERROR;
        }
    }

    public void Dispose()
    {
        if (_isActive && OperatingSystem.IsWindows())
        {
            TimeEndPeriod(_period);
            _isActive = false;
        }
    }

    [SupportedOSPlatform("windows")]
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [SupportedOSPlatform("windows")]
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);
}
