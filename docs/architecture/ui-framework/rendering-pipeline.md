### Rendering pipeline
Two `JobPriority.Rendering` jobs run continuously and are deliberately decoupled — nothing renders synchronously from a key handler:

1. `ControlRendererJob` walks each `Screen`, collects controls whose `HasStateChanged` is set, renders them into per-Z-order `BufferLayer`s, then `Buffer.Merge()`s them into `MergedLayer` (only when something actually rendered — merging blank new layers would blank the screen).
2. `ConsoleBufferRendererJob` diffs `MergedLayer` against `LastBuffer` line by line (SIMD byte compare), batches runs of changed cells into single `IConsoleDriver.Write` calls, and detects terminal resizes. Both jobs force a full redraw every second as a safety net.

Set the `ITERMINAL_RENDER_DUMPS` environment variable to dump up to 100 frames (buffer contents, render steps, and a reconstructed "what the console actually received" snapshot) to `%TEMP%/iTerminal/renderer-dumps` — the fastest way to debug a visual glitch.

Set `ITERMINAL_STARTUP_TRACE` to a file path to time start-up: `StartupTrace.Mark(name)` calls (process start, `main`, `built`, `run`, the database initialisation, screen initialisation, `first-frame`) are written there as `name;milliseconds since the process started` when the first frame is drawn. Compare builds by the intervals (`first-frame` minus `run`), not the absolute times: process creation varies by seconds from one launch to the next on a machine with a virus scanner. Anything that takes a long time before the first frame belongs on a background task — a process started, or a PATH walked, by a service constructor is paid at every launch.

`Buffer.SyncLock` guards the layer array; take it around any resize or merge.
