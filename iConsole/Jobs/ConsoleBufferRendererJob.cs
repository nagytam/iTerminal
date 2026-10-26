using iConsole.Buffers;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Helper;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Buffer = iConsole.Buffers.Buffer;
using iConsole.Diagnostics;

namespace iConsole.Jobs;

public class ConsoleBufferRendererJob : Job, Services.IConsoleHandoverParticipant
{
    public override JobPriority Priority => JobPriority.Rendering;

    private static readonly TimeSpan WindowSizeCheckInterval = TimeSpan.FromMilliseconds(100);
    // Ensure changed buffer contents are forced to redraw within 1 second under all circumstances
    private static readonly TimeSpan FullRedrawFallbackInterval = TimeSpan.FromSeconds(1);

    private const int MaxDumpFrames = 100;
    private const int MaxDumpHeight = 60;
    private const int MaxDumpWidth  = 160;

    private readonly IRenderingDumpHelper _dump;
    private readonly MainLoopWatchDog? _watchDog;
    public string DumpDirectoryPath => _dump.DumpDirectoryPath;

    private IConsoleDriver _consoleDriver { get; set; }
    public IBuffer Buffer { get; init; }
    public BufferLayer LastBuffer { get; init; }
    public Cursor LastBufferCursor { get; init; }
    public event EventHandler<Data.Size>? WindowSizeChanged;

    private Color ConsoleForegroundColor;
    private Color ConsoleBackgroundColor;
    private readonly Lock _dumpLock = new();
    private int _dumpFrameIndex;
    private long _renderIterations;
    private DateTime _lastStatisticsDate = DateTime.Now;
    private DateTime _lastWindowSizeCheck = DateTime.MinValue;
    private readonly Services.ConsoleOwnership? _consoleOwnership;

    /// <param name="dumpHelper">
    /// Optional rendering-dump helper.  Defaults to a new <see cref="RenderingDumpHelper"/>
    /// instance (which is disabled unless the <c>ITERMINAL_RENDER_DUMPS</c> env-var is set).
    /// Pass <see langword="null"/> or omit to use the default.
    /// </param>
    public ConsoleBufferRendererJob(
        IConsoleDriver consoleDriver,
        IBuffer buffer,
        IRenderingDumpHelper? dumpHelper = null,
        Services.ConsoleOwnership? consoleOwnership = null,
        MainLoopWatchDog? watchDog = null)
    {
        _watchDog = watchDog;
        _consoleDriver = consoleDriver ?? Services.NullConsoleDriver.Instance;
        ConsoleForegroundColor = _consoleDriver.ForegroundColor;
        ConsoleBackgroundColor = _consoleDriver.BackgroundColor;
        Buffer = buffer ?? new Buffers.Buffer(Services.NullConsoleDriver.Instance);
        LastBuffer = new BufferLayer(0, Buffer.Size, Color.White, Color.Black);
        LastBufferCursor = new Cursor(Buffer.Size);
        _dump = dumpHelper ?? new RenderingDumpHelper();

        // The driver first: released first, it clears the screen and shows the cursor for the
        // child; taken back last, after this job has forgotten what it believes is on screen.
        _consoleOwnership = consoleOwnership;
        _consoleOwnership?.AddParticipant(_consoleDriver);
        _consoleOwnership?.AddParticipant(this);
    }

    /// <summary>
    /// The child process has had the screen: nothing on it is what <see cref="LastBuffer"/> says.
    /// Every cell is marked unknown, so the next pass redraws all of them — a diff against the old
    /// frame would leave the shell's last screen showing wherever the panels happen to match it.
    /// </summary>
    public void OnConsoleReacquired()
    {
        LastBuffer.Clear(new Conxel('\0', Color.Black, Color.Black));
        LastBufferCursor.CursorPosition?.Reset();
        LastBufferCursor.IsCursorVisible = false;
        _lastWindowSizeCheck = DateTime.MinValue;
    }

    protected override void OnStart()
    {
        base.OnStart();
        try
        {
            _dump.InitializeSession(_consoleDriver);
            _dump.AppendSessionTrace($"buffer-renderer:start;buffer-size={Buffer.Size.Width}x{Buffer.Size.Height}");
        }
        catch (Exception exception)
        {
            WriteDiagnosticError(exception);
        }
    }

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        var lastRedraw = DateTime.Now;
        while (!cancellationToken.IsCancellationRequested)
        {
            // Lent to a child process: the terminal is not this job's to write to.
            if (_consoleOwnership is not null && !_consoleOwnership.TryBeginUse())
            {
                Thread.Sleep(10);
                continue;
            }

            try
            {
                var now = DateTime.Now;
                if (now - lastRedraw >= FullRedrawFallbackInterval)
                {
                    ForceRedraw();
                    lastRedraw = now;
                }
                // Timed for the watchdog: a frame past its budget is reported while it is still being drawn.
                bool idle;
                using (_watchDog?.Watch("render"))
                {
                    idle = RenderBuffer();
                }
                DetectWindowSizeChanges(now);
                _renderIterations++;
                if (idle)
                {
                    Thread.Sleep(1);
                    OnIdle();
                }
            }
            catch (Exception exception)
            {
                WriteDiagnosticError(exception);
            }
            finally
            {
                _consoleOwnership?.EndUse();
            }
        }
        return Task.CompletedTask;
    }

    private void ForceRedraw()
    {
        LastBufferCursor.CursorPosition?.Reset();
        LastBuffer.Clear();
    }

    public virtual void OnIdle()
    {
        if (_renderIterations % 10000 == 0)
        {
            var now = DateTime.Now;

            if (now.Subtract(_lastStatisticsDate).TotalSeconds > 10)
            {
                var seconds = now.Subtract(_lastStatisticsDate).TotalSeconds;
                var rate = 10000 / seconds;
                Debug.WriteLine($"Rendered 10000 times in the last {seconds:N2} seconds at rate {rate:N2} per seconds");
                _lastStatisticsDate = now;
            }
        }
    }

    private void DetectWindowSizeChanges(DateTime now)
    {
        if (now - _lastWindowSizeCheck < WindowSizeCheckInterval)
        {
            return;
        }

        _lastWindowSizeCheck = now;
        _consoleDriver.RefreshWindowSize();
        var currentWindowSize = _consoleDriver.WindowSize;
        if (currentWindowSize.Height != Buffer.Size.Height
            || currentWindowSize.Width != Buffer.Size.Width)
        {
            var newSize = currentWindowSize;
            // Hold SyncLock while resizing so ControlRendererJob cannot observe
            // a partially-cleared layer array during a concurrent Merge().
            lock (Buffer.SyncLock)
            {
                Buffer.Resize(newSize);
            }
            WindowSizeChanged?.Invoke(this, newSize);
        }
    }

    private bool RenderBuffer()
    {
        bool isIdle = true;
        bool isCursorVisible = LastBufferCursor.IsCursorVisible;
        List<string>? renderSteps = ShouldDumpFrame() ? [] : null;
        List<ConsoleDisplayWrite>? displayWrites = ShouldDumpFrame() ? [] : null;
        List<(int X, int Y, ConxelString Text)> pendingWrites = [];
        BufferLayer? layerToDump = null;
        var shouldDumpFrame = renderSteps is not null && displayWrites is not null;
        var shouldClearConsole = false;
        var shouldHideCursor = false;
        try
        {
            var minY = 0;
            var maxY = (int)Buffer.Size.Height;
            var minX = 0;
            var maxX = (int)Buffer.Size.Width;
            if (shouldDumpFrame)
            {
                _dump.AppendSessionTrace($"buffer-renderer:frame-start;index={_dumpFrameIndex};size={Buffer.Size.Width}x{Buffer.Size.Height}");
            }
            lock (Buffer.SyncLock)
            {
                // Debug.Assert(!Buffer.IsLocked);
                if (LastBuffer.Size != Buffer.Size)
                {
                    shouldClearConsole = true;
                    LastBuffer.Resize(Buffer.Size);
                    // After a resize, LastBuffer is filled with spaces which may be
                    // identical to a not-yet-rendered MergedLayer, causing a blank screen.
                    // Mark every cell as dirty by clearing LastBuffer to sentinel values so
                    // the diff loop forces a full redraw on the next iteration.
                    LastBuffer.Clear(new Conxel('\0', Color.Black, Color.Black));
                }

                //if (Buffer.IsDirty)
                //{
                //    Buffer.IsDirty = false;
                //}
                var layer = Buffer.MergedLayer;
                layerToDump = layer;

                for (int y = minY; y < maxY; y++)
                {
                    if (y >= layer.Content.Length ||
                        y >= LastBuffer.Content.Length ||
                        IsIdentical(layer[y], LastBuffer[y]))
                    {
                        continue;
                    }
                    if (isCursorVisible)
                    {
                        shouldHideCursor = true;
                        isCursorVisible = false;
                    }
                    isIdle = false;
                    var renderStep = RenderLine(
                        y,
                        minX,
                        maxX,
                        layer[y].Span,
                        LastBuffer[y].Span,
                        pendingWrites,
                        displayWrites,
                        renderSteps is not null);
                    if (renderSteps is not null)
                    {
                        renderSteps.Add(renderStep);
                    }

                    // Copy in-place: no heap allocation, vectorized by the runtime.
                    layer[y].Span.CopyTo(LastBuffer[y].Span);
                }
            }

            if (shouldHideCursor || shouldClearConsole || pendingWrites.Count > 0)
            {
                _consoleDriver.BeginFrame();
            }

            if (shouldHideCursor)
            {
                _consoleDriver.IsCursorVisible = false;
            }

            if (shouldClearConsole)
            {
                _consoleDriver.Clear();
            }

            foreach (var write in pendingWrites)
            {
                _consoleDriver.Write(write.X, write.Y, write.Text);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
        finally
        {
            UpdateCursor(isCursorVisible);
            _consoleDriver.EndFrame();
            if (shouldDumpFrame && layerToDump is not null)
            {
                DumpFrame(layerToDump, renderSteps, displayWrites);
            }
        }

        //if (Buffer.IsDirty)
        //{
        //    isIdle = false;
        //}

        return isIdle;
    }

    private void UpdateCursor(bool isCursorVisible)
    {
        if (Buffer.Cursor.IsCursorVisible)
        {
            var cursor = Buffer.Cursor;
            if (cursor is not null && cursor.CursorPosition is not null &&
                cursor.CursorPosition != LastBufferCursor.CursorPosition)
            {
                _consoleDriver.SetCursorPosition((int)cursor.CursorPosition.X, (int)cursor.CursorPosition.Y);
                LastBufferCursor.CursorPosition = cursor.CursorPosition;
            }

            if (!isCursorVisible)
            {
                _consoleDriver.IsCursorVisible = true;
                LastBufferCursor.IsCursorVisible = true;
            }
        }
    }

    private string RenderLine(
        int y,
        int minX,
        int maxX,
        Span<Conxel> source,
        Span<Conxel> last,
        ICollection<(int X, int Y, ConxelString Text)> pendingWrites,
        ICollection<ConsoleDisplayWrite>? displayWrites,
        bool captureTrace)
    {
        DebugConsoleOutput(y, source, last);
        var changedCharacters = RenderByCharacters(y, minX, maxX, source, last, pendingWrites, displayWrites);
        return captureTrace
            ? $"line={y};mode=chars;changed={changedCharacters};text=|{GetLineText(source)}|"
            : string.Empty;
    }

    private static bool IsIdentical(Memory<Conxel> memory1, Memory<Conxel> memory2)
    {
        var span1 = memory1.Span;
        var span2 = memory2.Span;
        if (span1.Length != span2.Length) return false;
        // Cast to byte spans so the runtime can use its SIMD-accelerated
        // byte-comparison fast path instead of the per-element record-struct equality.
        return MemoryMarshal.Cast<Conxel, byte>(span1)
                            .SequenceEqual(MemoryMarshal.Cast<Conxel, byte>(span2));
    }

    private int RenderByCharacters(
        int y,
        int minX,
        int maxX,
        Span<Conxel> line,
        Span<Conxel> lastLine,
        ICollection<(int X, int Y, ConxelString Text)> pendingWrites,
        ICollection<ConsoleDisplayWrite>? displayWrites)
    {
        var changedCharacters = 0;
        int batchStartX = -1;
        ConxelString? batch = null;

        // Iterate one past maxX so the loop always flushes any open batch at the end.
        for (int x = minX; x <= maxX; x++)
        {
            bool isCellChanged = false;
            if (x < maxX && x < line.Length && x < lastLine.Length)
            {
                ref var conxel = ref line[x];
                ref var lastConxel = ref lastLine[x];
                isCellChanged =
                    conxel.Char             != lastConxel.Char             ||
                    conxel.ForegroundColor  != lastConxel.ForegroundColor  ||
                    conxel.BackgroundColor  != lastConxel.BackgroundColor  ||
                    conxel.IsBold           != lastConxel.IsBold           ||
                    conxel.IsItalic         != lastConxel.IsItalic         ||
                    conxel.IsUnderline      != lastConxel.IsUnderline      ||
                    conxel.IsStrikeThrough  != lastConxel.IsStrikeThrough  ||
                    conxel.IsDim            != lastConxel.IsDim            ||
                    conxel.IsReverse        != lastConxel.IsReverse        ||
                    conxel.IsBlink          != lastConxel.IsBlink          ||
                    conxel.IsOverline       != lastConxel.IsOverline       ||
                    conxel.UnderlineStyle   != lastConxel.UnderlineStyle   ||
                    conxel.UnderlineColor   != lastConxel.UnderlineColor   ||
                    conxel.HasUnderlineColor != lastConxel.HasUnderlineColor ||
                    conxel.LinkId           != lastConxel.LinkId;
            }

            if (isCellChanged)
            {
                if (batchStartX < 0)
                {
                    batchStartX = x;
                    batch = new ConxelString();
                    batch.String.Capacity = maxX - x;
                }
                batch!.Append(line[x]);
                changedCharacters++;
            }
            else if (batchStartX >= 0)
            {
                // Flush the accumulated run of consecutive changed cells as one write.
                pendingWrites.Add((batchStartX, y, batch!));
                displayWrites?.Add(new ConsoleDisplayWrite(
                    batchStartX, y, batch!.Text,
                    batch[0].ForegroundColor, batch[0].BackgroundColor, "batch"));
                batchStartX = -1;
                batch = null;
            }
        }

        return changedCharacters;
    }

    private bool ShouldDumpFrame()
    {
        return _dump.IsEnabled && _dumpFrameIndex < MaxDumpFrames;
    }

    private void DumpFrame(BufferLayer layer, IReadOnlyCollection<string>? renderSteps, IReadOnlyCollection<ConsoleDisplayWrite>? displayWrites)
    {
        try
        {
            lock (_dumpLock)
            {
                if (_dumpFrameIndex >= MaxDumpFrames)
                {
                    return;
                }

                var frameIndex = _dumpFrameIndex++;
                var summary = CreateDumpSummary(layer, renderSteps, displayWrites);
                var frameFileName = Path.Combine(DumpDirectoryPath, $"frame-{frameIndex:D3}.txt");
                File.WriteAllText(frameFileName, summary);
                var validationSummary = CreateDisplayValidationSummary(layer, renderSteps, displayWrites);
                _dump.DumpTextArtifact($"frame-{frameIndex:D3}-display-validation.txt", validationSummary);
                _dump.DumpTextArtifact("latest-rendered-buffer.txt", summary);
                _dump.DumpTextArtifact("latest-displayed-console.txt", validationSummary);
                _dump.DumpTextArtifact(
                    "latest-console-writes.log",
                    string.Join(Environment.NewLine, displayWrites?.Select(write => write.ToString()) ?? ["<none>"]));
                _dump.AppendSessionTrace(
                    $"buffer-renderer:frame-dumped;index={frameIndex};changed-lines={renderSteps?.Count ?? 0};display-writes={displayWrites?.Count ?? 0};display-matches-buffer={DisplayMatchesBuffer(layer, displayWrites)}");
            }
        }
        catch (Exception exception)
        {
            WriteDiagnosticError(exception);
        }
    }

    public static string CreateDumpSummary(BufferLayer layer, IReadOnlyCollection<string>? renderSteps, IReadOnlyCollection<ConsoleDisplayWrite>? displayWrites = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"size={layer.Size.Width}x{layer.Size.Height}");
        builder.AppendLine($"non-space={CountVisibleCharacters(layer)}");
        builder.AppendLine($"changed-lines={renderSteps?.Count ?? 0}");
        builder.AppendLine($"display-writes={displayWrites?.Count ?? 0}");
        builder.AppendLine($"display-matches-buffer={DisplayMatchesBuffer(layer, displayWrites)}");
        builder.AppendLine("render-steps:");
        if (renderSteps is not null && renderSteps.Count > 0)
        {
            foreach (var renderStep in renderSteps)
            {
                builder.AppendLine(renderStep);
            }
        }
        else
        {
            builder.AppendLine("<none>");
        }

        builder.AppendLine("display-operations:");
        if (displayWrites is not null && displayWrites.Count > 0)
        {
            foreach (var displayWrite in displayWrites)
            {
                builder.AppendLine(displayWrite.ToString());
            }
        }
        else
        {
            builder.AppendLine("<none>");
        }

        builder.AppendLine("buffer:");
        var maxHeight = (int)Math.Min(layer.Size.Height, MaxDumpHeight);
        for (int y = 0; y < maxHeight; y++)
        {
            var line = layer[y].Span.ToArray();
            builder.AppendLine($"{y:D2}:|{GetLineText(line)}|");
        }

        if (layer.Size.Height > MaxDumpHeight)
        {
            builder.AppendLine($"... truncated after {MaxDumpHeight} rows ...");
        }

        builder.AppendLine("display-buffer:");
        var displaySnapshot = CreateDisplaySnapshot(layer.Size, displayWrites);
        for (int y = 0; y < displaySnapshot.Length; y++)
        {
            builder.AppendLine($"{y:D2}:|{displaySnapshot[y]}|");
        }

        return builder.ToString();
    }

    public static string CreateDisplayValidationSummary(BufferLayer layer, IReadOnlyCollection<string>? renderSteps, IReadOnlyCollection<ConsoleDisplayWrite>? displayWrites = null)
    {
        var builder = new StringBuilder();
        var displaySnapshot = CreateDisplaySnapshot(layer.Size, displayWrites);
        var displayMatchesBuffer = DisplayMatchesBuffer(layer, displayWrites);
        builder.AppendLine($"display-matches-buffer={displayMatchesBuffer}");
        builder.AppendLine($"changed-lines={renderSteps?.Count ?? 0}");
        builder.AppendLine($"display-writes={displayWrites?.Count ?? 0}");
        builder.AppendLine("buffer-lines:");
        var maxHeight = Math.Min(displaySnapshot.Length, Math.Min(layer.Content.Length, MaxDumpHeight));
        for (int y = 0; y < maxHeight; y++)
        {
            builder.AppendLine($"{y:D2}:|{GetLineText(layer[y].Span.ToArray())}|");
        }

        builder.AppendLine("display-lines:");
        for (int y = 0; y < displaySnapshot.Length; y++)
        {
            builder.AppendLine($"{y:D2}:|{displaySnapshot[y]}|");
        }

        builder.AppendLine("mismatches:");
        var mismatchCount = 0;
        for (int y = 0; y < maxHeight; y++)
        {
            var bufferLine = GetLineText(layer[y].Span.ToArray());
            var displayLine = displaySnapshot[y];
            if (!string.Equals(bufferLine, displayLine, StringComparison.Ordinal))
            {
                mismatchCount++;
                builder.AppendLine($"{y:D2}: buffer=|{bufferLine}| display=|{displayLine}|");
            }
        }

        if (mismatchCount == 0)
        {
            builder.AppendLine("<none>");
        }

        builder.AppendLine($"validation-result={(displayMatchesBuffer ? "pass" : "fail")}");

        return builder.ToString();
    }

    public static bool DisplayMatchesBuffer(BufferLayer layer, IReadOnlyCollection<ConsoleDisplayWrite>? displayWrites)
    {
        var displaySnapshot = CreateDisplaySnapshot(layer.Size, displayWrites);
        var maxHeight = Math.Min(displaySnapshot.Length, Math.Min(layer.Content.Length, MaxDumpHeight));
        for (int y = 0; y < maxHeight; y++)
        {
            var bufferLine = GetLineText(layer[y].Span.ToArray());
            if (!string.Equals(bufferLine, displaySnapshot[y], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static string[] CreateDisplaySnapshot(Data.Size size, IReadOnlyCollection<ConsoleDisplayWrite>? displayWrites)
    {
        var height = (int)Math.Min(size.Height, MaxDumpHeight);
        var width = (int)Math.Min(size.Width, MaxDumpWidth);
        var snapshot = Enumerable.Range(0, height)
            .Select(_ => new string(' ', width).ToCharArray())
            .ToArray();

        if (displayWrites is null)
        {
            return snapshot.Select(chars => new string(chars)).ToArray();
        }

        foreach (var displayWrite in displayWrites)
        {
            if (displayWrite.Y < 0 || displayWrite.Y >= height)
            {
                continue;
            }

            var line = snapshot[displayWrite.Y];
            for (int i = 0; i < displayWrite.Text.Length; i++)
            {
                var x = displayWrite.X + i;
                if (x < 0 || x >= width)
                {
                    continue;
                }

                var c = displayWrite.Text[i];
                line[x] = char.IsControl(c) && c != ' ' ? '?' : c;
            }
        }

        return snapshot.Select(chars => new string(chars)).ToArray();
    }

    private void WriteDiagnosticError(Exception exception)
    {
        try
        {
            _dump.AppendError(exception);
        }
        catch
        {
        }
    }

    private static int CountVisibleCharacters(BufferLayer layer)
        => RenderingDumpHelper.CountVisibleCharacters(layer);

    private static string GetLineText(IReadOnlyList<Conxel> line)
        => RenderingDumpHelper.GetLineText(line);

    private static string GetLineText(Span<Conxel> line)
        => RenderingDumpHelper.GetLineText(line.ToArray());

    private static void DebugConsoleOutput(int y, Span<Conxel> source, Span<Conxel> last)
    {
        //var sourceString = new ConxelString(source);
        //var lastString = new ConxelString(last);
        // Debug.WriteLine($"Update line {y}:{Environment.NewLine}<<<{lastString.Text}>>>{Environment.NewLine}<<<{sourceString.Text}>>>");
    }
}
