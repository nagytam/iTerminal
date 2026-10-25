### `iConsole` — reusable terminal UI framework
General-purpose TUI infrastructure with no knowledge of file managers. Key subsystems:

- **Controls** (`iConsole/Controls/`): `Window` is the base for all interactive UI panels. Controls handle `HandleKeyEventAsync` and render into a buffer.
- **Commands** (`iConsole/Commands/Command.cs`): User actions. Override `Key`, optionally `CanExecute()`, and `ExecuteAsync(object? args)`. Commands are attached to controls and dispatched by the key router. `Command.RunAsync` wraps execution with `CanExecute` gating, exception capture, and `ICommandLogger` recording — dispatch through it, not `ExecuteAsync` directly.
- **Jobs** (`iConsole/Jobs/Job.cs`): Long-running background loops (rendering, keyboard polling, network info). Each `Job` subclass implements `RunIterationAsync`. Jobs are registered as `IJob` singletons and run on the `Desktop` lifecycle, not the `TaskManager`.
- **TaskManager** (`iConsole/Tasks/TaskManager.cs`): Priority queue (`Rendering=0 > Input=1 > Background=2`) with multiple worker threads. Used for one-shot async work (directory enumeration, file copy). `BackgroundTask` is the base; override `ExecuteAsync()` and `Priority`.
- **Data types** (`iConsole/Data/`): `Url`, `Container` (URL scheme constants), `KeyEvent`, `Position`, `Size`, `ConcurrentObservableCollection<T>` (thread-safe, `ReplaceAll` for atomic batch swaps), `ConxelString` (styled character strings for rendering).
- **Services** (`iConsole/Services/`): `IDateTimeProviderService` (cached `DateTime.Now`), `IFrequentExecutionGuardService` (throttles `PropertyChanged` to max ~20/s), `IStopwatchFactory`/`IStopwatch` (injectable timing), `IThemeService`, `IErrorPromptService`.
- **Notifications** (`iConsole/Services/INotificationService.cs`, `Jobs/NotificationJob.cs`, `Controls/Toast.cs`, `Controls/ToastHost.cs`): self-dismissing toasts posted from any thread — see "Notifications and toasts" below.
- **Diagnostics** (`iConsole/Diagnostics/`): `ICommandLogger`/`IScreenLogger` capture command executions and rendered screen snapshots for agentic/behavioural tests. `AddiConsoleCore` registers the `Null*` no-op implementations by default; `iConsole.Test.Core` provides `InMemoryCommandLogger`/`InMemoryScreenLogger`, and `AppHarness` wires those in so tests can assert on what ran and what was drawn.


The detailed write-ups, one file per subsystem:

| Part | Covers |
| --- | --- |
| [Notifications and toasts](ui-framework/notifications.md) | `INotificationService`, `NotificationJob`, `ToastHost` |
| [Small input and navigation controls](ui-framework/input-and-navigation-controls.md) | Controls for keys, lists, menus and panels |
| [Rendering pipeline](ui-framework/rendering-pipeline.md) | `ControlRendererJob` to `ConsoleBufferRendererJob`; frame dumps |
| [Lending the terminal (Ctrl+O)](ui-framework/terminal-ownership.md) | `ConsoleOwnership` and the child shell |
| [Platform input plug-ins](ui-framework/platform-input-plugins.md) | `iConsole.Inputs.Posix` and `iConsole.Inputs.Windows` |
- **Frequent-execution guard** (`FrequentExecutionGuardService`): a request that arrives within the filter window runs once the window is over, and the newest request is the one that runs. A request that replaces a waiting one does not stop the next from running; an action that throws is traced and the rest still run.
- **Periodic repaint** (`Control.StartAutoRefresh`): starting it again replaces the repaint already running, and `StopAutoRefresh` ends it.
- **Clock** (`DateTimeProviderService`): the cached time is refreshed on the monotonic clock, so setting the wall clock back cannot freeze it.

### Built from the comparison

- **Tooltips.** Set `Control.ToolTipText`. The input consumer reports every pointer event to `IToolTipService`, and a `ToolTipHost` on the screen's root shows the innermost text under the pointer after `Delay` (600 ms by default), measured on `ITimeProvider`. A click, a drag, a wheel turn or a key hides it. Pointer motion without a button is decoded as `MouseEvent.IsHover`; the terminal is asked for it with `?1003h` (any-motion reporting).
- **Pointer-anchored popovers.** `Popup.ShowAtAsync(owner, anchor)`, kept on screen like the other placements.
- **Markdown viewer.** `MarkdownViewerControl` renders headings, paragraphs, nested lists, quotes, fenced code, rules and inline emphasis, code and links. It wraps to the width, scrolls with the arrow, Page, Home and End keys and the wheel, and only acts when focused.
- **`IInputInjector`.** Sends keys and mouse events through the same consumer and routing as a real keyboard and mouse. `VirtualTerminalInput` implements it for the test suite.
- **`ITimeProvider`.** `SystemTimeProvider` for the application and `VirtualTimeProvider` for tests, which moves the clock with `Advance` instead of sleeping.

### Suggested improvements

Compared with Terminal.Gui 2.5.0 (its public API and XML docs, not its runtime behaviour). Items marked as built or partly built are described above; the rest are not built. The "not found" notes are grep-based and need a runtime check before work starts. Terminal.Gui owns the main loop, layout and rendering, so the items below are ideas to adopt, not a reason to replace the framework.

**Worth doing soon**

1. **Wide-character and grapheme width.** No width handling was found, so CJK or emoji file names, and mixed-width text, may misalign panels. Check with a CJK file name in a panel, then add a width function to `ConxelString` with tests.
2. **Bracketed paste.** Not enabled (no `ESC[?2004h` found), so a multi-line paste runs each line as it is typed. Enable it in the input source and deliver a paste to the focused control as one event.
3. **Colour capability detection and quantisation.** Map colours to 256 or 16 when the terminal lacks true colour, and query the default foreground and background (OSC 10/11) at start-up so the theme matches the terminal. Not found in iConsole; Terminal.Gui does both.
4. **Virtual time in the test harness.** *Partly built:* `ITimeProvider` (`SystemTimeProvider`, `VirtualTimeProvider`) now feeds `DateTimeProviderService` and `ToolTipHost`. `NotificationJob` and `FrequentExecutionGuardService` still read the clock and `Thread.Sleep` directly, so they are not yet on it.
5. **Pluggable key bindings.** `Command.Key` is fixed at construction. Read bindings from a keymap file, with a conflict report that enforces the key rules in CLAUDE.md (no Alt+letter; Ctrl+Alt is typing). Matches item 11 in `docs/mc-parity.md`.

**Worth designing**

6. **Popover service.** *Partly built:* `Popup.ShowAtAsync(owner, anchor)` places an overlay at a point in the root's coordinates, and `ToolTipHost` uses it for tooltips. Context menus and the Ctrl+O hint are still placed by hand.
7. **Percent and fill sizing in `Layout`.** Layout is manual, so resizing needs per-control code. `Dim.Percent` / `Dim.Fill` equivalents would make panel ratios (item 9 in `docs/mc-parity.md`) cheap.
8. **Table upgrades.** Multi-select regions, per-column styles and cell colour getters would make the file list and search results more flexible.
9. **Markdown and image controls.** *Markdown built:* `MarkdownViewerControl` (parser in `iConsole/Markdown/`). The image control is not built: it would use the Kitty graphics protocol, with a cell-based fallback, for the image viewer's ASCII-art gap.
10. **Mouse hold and diagnostics.** Mouse hold-to-repeat for scroll buttons, and an overlay that shows control bounds and focus, alongside `ITERMINAL_RENDER_DUMPS` and `ITERMINAL_KEY_TRACE`. Not built.

**Lower priority**

11. **Chart annotations.** A legend and scatter series for the existing chart controls.
12. **Trace scopes.** Time key handlers and content loads so the 200 ms and 3 s budgets can be checked directly.

### Comparison with Spectre.Console

Spectre.Console 0.57.2 (its public API and XML docs, not its runtime behaviour) is an output and prompt library. It has no full-screen loop, focus or key routing, so only the overlapping areas are compared. The "not found" notes are grep-based.

| Area | iConsole | Spectre.Console |
| --- | --- | --- |
| Colour depth | 16 ANSI colours only (`Color` enum) | Profile detection: 16, 256 or truecolour, with downsampling |
| Environment detection | `ITERMINAL_RENDER_DUMPS`, `ITERMINAL_KEY_TRACE` only | `NO_COLOR`, `COLORTERM`, `TERM`, redirected output |
| Styled text | Built by hand from `ConxelString` runs | Markup strings (`[bold red]…[/]`) with `Markup.Escape` |
| Prompts | Dialogs, `ComboBox`, `CheckList`, y/n in footers | `TextPrompt` (validation, secret, choices), `SelectionPrompt` (search, paging), `MultiSelectionPrompt`, `ConfirmationPrompt` |
| Tables, trees, calendars, charts | Present | Present, with more border and alignment options |
| Progress | `ProgressBar`, `ProgressTime`, Task Manager | `ProgressContext` with several tasks and columns: spinner, percent, elapsed, remaining, transfer speed, downloaded size |
| Layout | `Layout` helpers, `Window`, `GroupBox` | `Grid`, `Columns`, `Rows`, `Panel` with header, `Padder`, `Align` |
| Charts | Block bar, line, gauge, heatmap, sparkline | `Canvas` with Braille dots for plots |
| Exceptions | `ErrorMessage` control | Pretty-printed traces: trimmed frames, file links, colours |
| Recording and tests | `ScreenBuffer` for assertions; HTML render dumps | `Recorder` exports text, HTML or SVG; `TestConsole` |
| Emoji and wide characters | Not handled (see the Terminal.Gui section) | Emoji tables and cell widths |

**Built from this comparison**

- **Prompts.** `IPromptService` (`AskAsync`, `SelectAsync`, `MultiSelectAsync`, `ConfirmAsync`) shows a dialog per prompt on the first screen, one at a time. `TextPrompt<T>` converts with the type's invariant converter and runs `Validate` rules; `SelectionPrompt<T>` filters as you type; `MultiSelectionPrompt<T>` marks with Space and enforces `MinimumSelections`; `ConfirmationPrompt` answers Y or N, and Enter takes `DefaultValue`. Esc or cancellation returns null. Choice state lives in `ChoiceList<T>`, which has no drawing.
- **Grid.** `Grid` places children in rows and columns with `Place`, with `GridLength.Cells`, `Auto` and `Star` tracks and spans. It arranges on layout and when its size changes.
- **Screen recorder.** `ScreenRecorder.Capture` copies the merged frame; `ToText`, `ToHtml`, `ToSvg` and `Save` (by extension) write it out. Wide characters keep their columns in every format.
- **Wide characters and emoji.** `TextWidth` gives the column count of text. A wide BMP character takes its cell and a continuation cell that is never written to the terminal; an emoji takes a surrogate pair. `BufferLayer.RepairWideCharacters` blanks a half that lost its partner when layers are merged. Zero-width characters are dropped; the width table covers the common East Asian and emoji ranges, not all of Unicode.

**Suggested improvements, in priority order**

1. **Colour profile and environment detection.** Detect `NO_COLOR`, `COLORTERM=truecolor` and `TERM`, and map colours to the terminal's depth. The `Color` enum can't express 256-colour or truecolour themes, so a profile type (and an RGB colour) is needed before theme work. Same gap as Terminal.Gui item 3.
2. **Markup for styled strings.** A small `[bold red]…[/]` parser with an `Escape` helper would simplify status messages, dialog text and help screens, and stop user text such as a file name containing `[` being read as styling. Build it on `ConxelString`.
3. **A prompt facade.** An `IPromptService` with `Ask<T>`, `Select<T>`, `MultiSelect<T>` and `Confirm`, built on the existing controls, gives dialog flows one API and makes them testable. Start with search-as-you-type selection, which Spectre's `SelectionPrompt` has.
4. **Recording the screen.** Expose the `ScreenBuffer` as a text, HTML or SVG recording. `BufferHelper.DumpBufferToHtml` already produces HTML. An end-to-end test could attach the screen on failure.
5. **Progress columns for transfers.** Optional columns for transfer speed, remaining time and downloaded size on `ProgressBar` or a multi-task progress control. Check the copy and sync dialogs first: if they lack speed and ETA, this is the cheapest visible win.
6. **Grid and columns layout.** A `Grid` with ratio and auto columns, and a `Columns` control, for cases `Layout.StackVertical` and manual positioning handle awkwardly. Overlaps Terminal.Gui item 7.
7. **Canvas with Braille dots.** Doubles the vertical resolution of line and scatter plots over block charts. Lower priority.
8. **Exception view.** Trimmed frames and file links in the `ErrorMessage` control. Low priority.
9. **Figlet banners** for splash and about screens. Low priority and cosmetic.

Items 1 and 2 are the largest gaps: item 1 blocks theme work, and item 2 removes repetitive `ConxelString` code.

### Main-loop watchdog and terminal clipboard

- **`MainLoopWatchDog`** (`iConsole/Diagnostics/`) is a job that times the passes that must stay inside the 200 ms budget: key and mouse handlers (`TerminalInputConsumerJob`) and render passes (`ConsoleBufferRendererJob`). A pass past `Threshold` is reported once through `Stalled` while it is still running, and `Recovered` reports the total when it ends. It measures and reports; it cannot interrupt a handler that never returns. Its events are raised on the watchdog's thread, so a handler must post a notification, not touch the control tree. The application logs both events through Serilog (`MainLoopWatchDogLoggingHostedService`).
- **OSC 52 clipboard** (`Osc52TextClipboardService`) sets the clipboard of the machine the person is at by writing an OSC 52 sequence to the terminal. It is write-only and refuses a text larger than `MaxEncodedLength` or a copy made while the terminal is lent to a child process. `LayeredTextClipboardService` (iTerminal.Core) orders the layers: the system clipboard, then the terminal, then the application's own copy. Over SSH the terminal goes first, since a clipboard program on the remote machine would set a clipboard the person cannot reach. Paste reads the system clipboard and then the application's own copy. tmux needs `set-clipboard on` for OSC 52 to reach the outer terminal.

### Reflow in the text viewer and editor, and the taskbar progress indicator

- **Reflow.** When the width changes, or wrapping is switched on or off, the viewer keeps the reading position as text: the row at the top of the screen is found again by its line and column after the rows are rebuilt (`TextViewerControl.RebuildRows`, `RowOf`). The caret follows the wrapped row (`CursorScreenRow`), and `KeepCursorInText` scrolls in rows when wrapped.
- **Editor wrap.** The editor inherits the viewer's wrap. F6 toggles it (F2 is save in the editor). Wrapped, Up and Down move by screen row and keep the column within the row (`MoveCursorByScreenRow`); unwrapped they move by line as before. Home and End still work by line.
- **Taskbar progress.** `TaskbarProgressJob` polls the background tasks every 250 ms, works out the state with `TaskbarProgressCalculator` (average progress of running tasks; paused when every running task is paused; indeterminate before any progress is reported), and tells `ITaskbarProgress` only when that changes. `WindowsTaskbarProgress` drives `ITaskbarList3` on a thread in a single-threaded apartment, against the console window. Elsewhere `NullTaskbarProgress` is used. The Windows path is compiled on Linux but has not been run on Windows. Windows Terminal may host the console in a window that has no taskbar button to show the indicator on.
