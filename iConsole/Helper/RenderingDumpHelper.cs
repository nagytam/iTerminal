using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using System.Text;

namespace iConsole.Helper;

/// <summary>
/// Instance-per-job rendering dump helper.  All mutable state is held on the instance
/// so multiple jobs (and multiple parallel tests) never share anything.
/// </summary>
public sealed class RenderingDumpHelper : IRenderingDumpHelper
{
    // ── constants ────────────────────────────────────────────────────────────
    public const int MaxDumpHeight = 60;
    public const int MaxDumpWidth  = 160;

    // ── instance state (zero static mutable fields) ──────────────────────────
    private readonly Lock _dumpLock           = new();
    private          bool _sessionInitialized;
    private readonly bool _isEnabled;

    // ── construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the <c>ITERMINAL_RENDER_DUMPS</c> environment variable once at
    /// construction time so every instance gets an independent, immutable flag.
    /// </summary>
    public RenderingDumpHelper()
    {
        var value = Environment.GetEnvironmentVariable("ITERMINAL_RENDER_DUMPS");
        _isEnabled = !string.IsNullOrWhiteSpace(value)
            && (string.Equals(value, "1",    StringComparison.OrdinalIgnoreCase)
             || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
             || string.Equals(value, "yes",  StringComparison.OrdinalIgnoreCase)
             || string.Equals(value, "on",   StringComparison.OrdinalIgnoreCase));
    }

    // ── IRenderingDumpHelper ─────────────────────────────────────────────────

    public bool   IsEnabled        => _isEnabled;
    public string DumpDirectoryPath => Path.Combine(Path.GetTempPath(), "iTerminal", "renderer-dumps");

    public void InitializeSession(IConsoleDriver consoleDriver)
    {
        if (!_isEnabled) return;

        lock (_dumpLock)
        {
            if (_sessionInitialized) return;

            Directory.CreateDirectory(DumpDirectoryPath);
            foreach (var filePath in Directory.EnumerateFiles(DumpDirectoryPath))
                File.Delete(filePath);

            File.WriteAllText(
                Path.Combine(DumpDirectoryPath, "session.txt"),
                $"started={DateTime.Now:O}{Environment.NewLine}" +
                $"base-directory={AppContext.BaseDirectory}{Environment.NewLine}" +
                $"window-size={consoleDriver.WindowSize.Width}x{consoleDriver.WindowSize.Height}{Environment.NewLine}" +
                $"driver={consoleDriver.GetType().FullName}{Environment.NewLine}");

            _sessionInitialized = true;
        }
    }

    public void AppendSessionTrace(string message)
    {
        if (!_isEnabled || !_sessionInitialized) return;

        lock (_dumpLock)
        {
            Directory.CreateDirectory(DumpDirectoryPath);
            File.AppendAllText(
                Path.Combine(DumpDirectoryPath, "rendering-steps.log"),
                $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
    }

    public void DumpBufferStage(string stageName, BufferLayer layer, IReadOnlyCollection<string>? details = null)
    {
        if (!_isEnabled || !_sessionInitialized) return;

        lock (_dumpLock)
        {
            Directory.CreateDirectory(DumpDirectoryPath);
            File.WriteAllText(
                Path.Combine(DumpDirectoryPath, SanitizeFileName(stageName) + ".txt"),
                CreateBufferSummary(stageName, layer, details));
        }
    }

    public void DumpTextArtifact(string fileName, string content)
    {
        if (!_isEnabled || !_sessionInitialized) return;

        lock (_dumpLock)
        {
            Directory.CreateDirectory(DumpDirectoryPath);
            File.WriteAllText(
                Path.Combine(DumpDirectoryPath, SanitizeFileName(fileName)),
                content);
        }
    }

    public void AppendError(Exception exception)
    {
        if (!_isEnabled) return;

        lock (_dumpLock)
        {
            Directory.CreateDirectory(DumpDirectoryPath);
            File.AppendAllText(
                Path.Combine(DumpDirectoryPath, "renderer-errors.log"),
                $"[{DateTime.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
    }

    // ── pure static helpers (no state — safe to share / call from anywhere) ──

    public static string CreateBufferSummary(
        string stageName, BufferLayer layer, IReadOnlyCollection<string>? details = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"stage={stageName}");
        sb.AppendLine($"size={layer.Size.Width}x{layer.Size.Height}");
        sb.AppendLine($"non-space={CountVisibleCharacters(layer)}");
        sb.AppendLine("details:");
        if (details is { Count: > 0 })
            foreach (var d in details) sb.AppendLine(d);
        else
            sb.AppendLine("<none>");

        sb.AppendLine("buffer:");
        var maxH = (int)Math.Min(layer.Size.Height, MaxDumpHeight);
        for (int y = 0; y < maxH; y++)
            sb.AppendLine($"{y:D2}:|{GetLineText(layer[y].Span)}|");
        if (layer.Size.Height > MaxDumpHeight)
            sb.AppendLine($"... truncated after {MaxDumpHeight} rows ...");

        return sb.ToString();
    }

    public static string CreateWindowSummary(IConsoleDriver consoleDriver)
    {
        var windowSize    = TryGet(() => consoleDriver.WindowSize,     new Size(0, 0));
        var foreground    = TryGet(() => consoleDriver.ForegroundColor, Color.White);
        var background    = TryGet(() => consoleDriver.BackgroundColor, Color.Black);
        var cursorVisible = TryGet(() => consoleDriver.IsCursorVisible, false);

        return string.Join(Environment.NewLine,
        [
            $"driver={consoleDriver.GetType().FullName}",
            $"window-size={windowSize.Width}x{windowSize.Height}",
            $"foreground={foreground}",
            $"background={background}",
            $"cursor-visible={cursorVisible}",
            $"base-directory={AppContext.BaseDirectory}"
        ]) + Environment.NewLine;
    }

    public static int CountVisibleCharacters(BufferLayer layer)
    {
        int count = 0;
        int maxH  = Math.Min(layer.Content.Length, MaxDumpHeight);
        for (int y = 0; y < maxH; y++)
            foreach (var c in layer[y].Span)
                if (!char.IsWhiteSpace(c.Char)) count++;
        return count;
    }

    public static string GetLineText(Span<Conxel> line)
    {
        var width = Math.Min(line.Length, MaxDumpWidth);
        var sb    = new StringBuilder(width + 32);
        for (int x = 0; x < width; x++)
        {
            var c = line[x].Char;
            sb.Append(char.IsControl(c) && c != ' ' ? '?' : c);
        }
        if (line.Length > MaxDumpWidth) sb.Append("...");
        return sb.ToString();
    }

    public static string GetLineText(IReadOnlyList<Conxel> line)
    {
        var width = Math.Min(line.Count, MaxDumpWidth);
        var sb    = new StringBuilder(width + 32);
        for (int x = 0; x < width; x++)
        {
            var c = line[x].Char;
            sb.Append(char.IsControl(c) && c != ' ' ? '?' : c);
        }
        if (line.Count > MaxDumpWidth) sb.Append("...");
        return sb.ToString();
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
    }

    private static T TryGet<T>(Func<T> getter, T fallback)
    {
        try   { return getter(); }
        catch { return fallback; }
    }
}
