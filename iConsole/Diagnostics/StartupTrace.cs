using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace iConsole.Diagnostics;

/// <summary>
/// Times the phases of start-up. Marks are cheap and always recorded; when the
/// <c>ITERMINAL_STARTUP_TRACE</c> environment variable names a file, <see cref="Flush"/> writes them there
/// as <c>name;milliseconds since the process started</c>, one per line.
/// </summary>
public static class StartupTrace
{
    public const string EnvironmentVariable = "ITERMINAL_STARTUP_TRACE";

    private static readonly Lock Gate = new();
    private static readonly List<(string Name, double Milliseconds)> Marks = [];
    private static readonly long StartedAt = Stopwatch.GetTimestamp();
    private static readonly double ProcessAgeAtStart = GetProcessAgeMilliseconds();
    private static bool _flushed;

    /// <summary>Records that <paramref name="name"/> happened now. Ignored once the trace has been written.</summary>
    public static void Mark(string name)
    {
        lock (Gate)
        {
            if (_flushed)
            {
                return;
            }

            Marks.Add((name, ProcessAgeAtStart + Stopwatch.GetElapsedTime(StartedAt).TotalMilliseconds));
        }
    }

    /// <summary>Writes the marks to the file the environment variable names, once; does nothing if it is unset.</summary>
    public static void Flush()
    {
        string? path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        lock (Gate)
        {
            if (_flushed)
            {
                return;
            }

            _flushed = true;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var text = new StringBuilder();
            foreach ((string name, double milliseconds) in Marks)
            {
                text.Append(name).Append(';').AppendLine(milliseconds.ToString("F0", CultureInfo.InvariantCulture));
            }

            try
            {
                File.WriteAllText(path, text.ToString());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A trace that cannot be written must never stop the app.
            }
        }
    }

    private static double GetProcessAgeMilliseconds()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return Math.Max(0, (DateTime.Now - process.StartTime).TotalMilliseconds);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }
}
