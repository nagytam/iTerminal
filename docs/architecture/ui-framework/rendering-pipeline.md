### Rendering pipeline
Two `JobPriority.Rendering` jobs run continuously and are deliberately decoupled — nothing renders synchronously from a key handler:

1. `ControlRendererJob` walks each `Screen`, collects controls whose `HasStateChanged` is set, renders them into per-Z-order `BufferLayer`s, then `Buffer.Merge()`s them into `MergedLayer` (only when something actually rendered — merging blank new layers would blank the screen).
2. `ConsoleBufferRendererJob` diffs `MergedLayer` against `LastBuffer` line by line (SIMD byte compare), batches runs of changed cells into single `IConsoleDriver.Write` calls, and detects terminal resizes. Both jobs force a full redraw every second as a safety net.

Set the `ITERMINAL_RENDER_DUMPS` environment variable to dump up to 100 frames (buffer contents, render steps, and a reconstructed "what the console actually received" snapshot) to `%TEMP%/iTerminal/renderer-dumps` — the fastest way to debug a visual glitch.

`Buffer.SyncLock` guards the layer array; take it around any resize or merge.
