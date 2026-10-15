using iConsole.Data;

namespace iConsole.Helper;

/// <summary>
/// Appends every key event the app receives to <c>%TEMP%/iTerminal/key-trace.log</c> when the
/// <c>ITERMINAL_KEY_TRACE</c> environment variable is set, mirroring how
/// <see cref="RenderingDumpHelper"/> is gated by <c>ITERMINAL_RENDER_DUMPS</c>.
///
/// <para>This exists because a hot key that does nothing has two indistinguishable causes from the
/// outside: the binding is wrong, or the keystroke never reached the process because the terminal
/// host consumed it first. The trace separates them — a missing line means the key never arrived,
/// a line with unexpected modifiers means the binding needs to match what the terminal actually
/// sends.</para>
/// </summary>
public static class KeyTraceHelper
{
    private static readonly Lock _syncLock = new();
    private static bool? _isEnabled;
    private static bool _headerWritten;

    /// <summary>Whether tracing is on. Read once from the environment and cached.</summary>
    public static bool IsEnabled
    {
        get
        {
            if (_isEnabled is bool cached)
            {
                return cached;
            }

            bool enabled;
            try
            {
                var value = Environment.GetEnvironmentVariable("ITERMINAL_KEY_TRACE");
                enabled = !string.IsNullOrWhiteSpace(value)
                    && !value.Equals("0", StringComparison.Ordinal)
                    && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                enabled = false;
            }

            _isEnabled = enabled;
            return enabled;
        }
    }

    public static string TraceFilePath => Path.Combine(Path.GetTempPath(), "iTerminal", "key-trace.log");

    /// <summary>Test seam: forces tracing on or off, or back to reading the environment with null.</summary>
    public static void SetEnabledForTest(bool? enabled)
    {
        _isEnabled = enabled;
        _headerWritten = false;
    }

    /// <summary>
    /// Records one key event and whether the control tree handled it. Never throws — a diagnostic
    /// must not be able to break input handling.
    /// </summary>
    public static void Trace(KeyEvent? keyEvent, bool isHandled)
    {
        if (!IsEnabled || keyEvent is null)
        {
            return;
        }

        try
        {
            var line = Format(keyEvent, isHandled);
            lock (_syncLock)
            {
                var directory = Path.GetDirectoryName(TraceFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!_headerWritten)
                {
                    File.AppendAllText(
                        TraceFilePath,
                        $"{Environment.NewLine}=== session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                    _headerWritten = true;
                }

                File.AppendAllText(TraceFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    /// <summary>Renders one trace line, e.g. <c>12:01:02.345 key=M chord=Ctrl+Shift+M char=0x0d handled=True</c>.</summary>
    public static string Format(KeyEvent keyEvent, bool isHandled)
    {
        var modifiers = new List<string>(3);
        if (keyEvent.Control) modifiers.Add("Ctrl");
        if (keyEvent.Alt) modifiers.Add("Alt");
        if (keyEvent.Shift) modifiers.Add("Shift");
        modifiers.Add(keyEvent.ConsoleKey.ToString());

        return $"{DateTime.Now:HH:mm:ss.fff} key={keyEvent.ConsoleKey} ({(int)keyEvent.ConsoleKey}) "
             + $"chord={string.Join('+', modifiers)} "
             + $"char=0x{(int)keyEvent.KeyChar:x2} handled={isHandled}";
    }
}
