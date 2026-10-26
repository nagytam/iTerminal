using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace iTerminal.Core.Services.Taskbar;

/// <summary>
/// The progress bar on the application's taskbar button, through the shell's <c>ITaskbarList3</c>. The shell's taskbar
/// object is apartment-threaded, so it lives on a thread of its own in a single-threaded apartment, and updates are queued to it;
/// a caller never waits on the shell.
///
/// <para>The window is the console's. A terminal that hosts the console in its own window (Windows Terminal) may not have a
/// console window of its own to show the indicator on, in which case nothing is shown and nothing fails.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTaskbarProgress : ITaskbarProgress, IDisposable
{
    private const int TbpfNoProgress = 0x0;
    private const int TbpfIndeterminate = 0x1;
    private const int TbpfNormal = 0x2;
    private const int TbpfPaused = 0x8;

    private readonly BlockingCollection<(TaskbarState State, long Percentage)> _updates = new();
    private readonly Thread _thread;

    public WindowsTaskbarProgress()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Taskbar progress" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Queues the indicator to show <paramref name="state"/> at <paramref name="percentage"/>. Returns at once.</summary>
    public void Update(TaskbarState state, long percentage)
    {
        if (!_updates.IsAddingCompleted)
        {
            _updates.Add((state, Math.Clamp(percentage, 0, 100)));
        }
    }

    public void Dispose()
    {
        _updates.CompleteAdding();
    }

    private void Run()
    {
        ITaskbarList3? taskbar = null;
        try
        {
            taskbar = (ITaskbarList3)new TaskbarListClass();
            taskbar.HrInit();
        }
        catch (COMException)
        {
            // No shell taskbar here (a server without one): the indicator is simply not shown.
            taskbar = null;
        }

        foreach (var (state, percentage) in _updates.GetConsumingEnumerable())
        {
            if (taskbar is null)
            {
                continue;
            }

            IntPtr window = GetConsoleWindow();
            if (window == IntPtr.Zero)
            {
                continue;
            }

            taskbar.SetProgressState(window, FlagFor(state));
            if (state is TaskbarState.Normal or TaskbarState.Paused)
            {
                taskbar.SetProgressValue(window, (ulong)percentage, 100);
            }
        }

        if (taskbar is not null)
        {
            Marshal.ReleaseComObject(taskbar);
        }
    }

    private static int FlagFor(TaskbarState state) => state switch
    {
        TaskbarState.Indeterminate => TbpfIndeterminate,
        TaskbarState.Normal => TbpfNormal,
        TaskbarState.Paused => TbpfPaused,
        _ => TbpfNoProgress,
    };

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarListClass
    {
    }

    /// <summary>
    /// The parts of <c>ITaskbarList3</c> used here, declared in the shell's vtable order: the base interfaces' methods come
    /// first, so the progress methods are at their true positions.
    /// </summary>
    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr window);
        [PreserveSig] int DeleteTab(IntPtr window);
        [PreserveSig] int ActivateTab(IntPtr window);
        [PreserveSig] int SetActiveAlt(IntPtr window);
        [PreserveSig] int MarkFullscreenWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(IntPtr window, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(IntPtr window, int flags);
    }
}
