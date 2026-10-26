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

### Localization (`iConsole/Localization/`)
`ILocalizer` (registered by `AddiConsoleCore`) turns a resource key into text in the current language. Strings live in
`.resx` files, a neutral English one plus `de`, `fr`, `es`, `it` and `hu`; the framework's own are
`iConsole/Localization/Resources/Strings*.resx` (`Common.*`), and the app adds its own through an `ILocalizationSource`
(`iTerminal.Core/Localization/Resources/AppStrings*.resx`).
- **English is the neutral language and the fallback.** A key a language lacks (or leaves empty) shows the English text; a key no
  file knows shows the key itself, so a missing string is visible. `Get(key, args)` formats with the current culture and never
  throws on a broken placeholder; `TryGet` is the lookup without the key-as-text fallback.
- **The choice is `ILanguageStore`-backed.** `Localizer` reads it at construction and saves on `SetLanguage`; the app's store is the
  `Language` setting (`SettingsLanguageStore`). An unknown saved code leaves English. The default is English, never the OS culture.
- **There is no change notification in `ISettingsService`, so `ILocalizer.LanguageChanged` is the notification.** It fires on the
  caller's thread; a control that keeps looked-up text sets a flag in the handler and re-reads in `OnIdle` (the menu bar and the
  footer do), because the control tree must not be touched from another thread.
- **Menus and the footer are translated by key derived from the English text.** `MenuItem.Localize(localizer)` keeps the text an
  entry was built with and looks up `Menu.` + its label without non-alphanumerics (`Edit With…` → `Menu.EditWith`); the shortcut after the
  tab is never translated. The footer does the same with `Footer.` + the label of `F5-Copy`. An entry without a key stays English, so
  a new menu entry needs a `Menu.*` row in every `AppStrings*.resx` (the completeness test fails otherwise).
- **Adding a string:** add the key to the neutral file and to all five language files; `LocalizationResourcesTest` fails when a
  language lacks a key, has an extra one, or changes the `{n}` placeholders. Strings are extracted in stages: the menu bar, the hot key
  bar and the language dialog are done; dialog bodies, the command palette and the commands' own titles are still English.

### Built from the comparison

- **Tooltips.** Set `Control.ToolTipText`. The input consumer reports every pointer event to `IToolTipService`, and a `ToolTipHost` on the screen's root shows the innermost text under the pointer after `Delay` (600 ms by default), measured on `ITimeProvider`. A click, a drag, a wheel turn or a key hides it. Pointer motion without a button is decoded as `MouseEvent.IsHover`; the terminal is asked for it with `?1003h` (any-motion reporting).
- **Pointer-anchored popovers.** `Popup.ShowAtAsync(owner, anchor)`, kept on screen like the other placements.
- **Markdown viewer.** `MarkdownViewerControl` renders headings, paragraphs, nested lists, quotes, fenced code, rules and inline emphasis, code and links. It wraps to the width, scrolls with the arrow, Page, Home and End keys and the wheel, and only acts when focused.
- **`IInputInjector`.** Sends keys and mouse events through the same consumer and routing as a real keyboard and mouse. `VirtualTerminalInput` implements it for the test suite.
- **`ITimeProvider`.** `SystemTimeProvider` for the application and `VirtualTimeProvider` for tests, which moves the clock with `Advance` instead of sleeping.

### Text attributes, hyperlinks, popovers and context menus

- **Attributes.** `Conxel` carries dim, reverse, blink and overline as well as bold, italic, underline and strike-through, an `UnderlineStyle` (single, double, curly, dotted, dashed) and an optional underline colour. `Label` exposes them; `ConxelString.SetUnderline` sets the underline for a range. `AnsiRenderer` writes them as SGR codes (`2`, `5`, `7`, `53`, `4:3`, `58:5:n`). A terminal that does not know a style falls back to a plain underline. A change in any of them makes the renderer repaint the cell.
- **Attribute reset.** `AnsiRenderer.ConxelString2Ansi` starts a sequence with `0;` when the previous cell had attributes, because a colour/attribute sequence only adds; without it, bold stayed on after a bold cell.
- **Hyperlinks (OSC 8).** A cell holds a `LinkId`, not a string, because the renderer compares cells as raw bytes. `Hyperlinks.Register(target)` interns the target and returns the id; `ConxelString.SetHyperlink(target, start, end)` and `Label.Hyperlink` use it. `LinkLabel` links itself when `Target` is an http(s), ftp(s), sftp, ssh, file or mailto address. A target is percent-encoded outside printable ASCII, so text can not end the escape sequence early. The renderer opens a link with an `id=` so a link written in pieces is one link, and closes it at the end of every write. Switch off with `ITERMINAL_HYPERLINKS=0` (`TERM=linux` and `dumb` are off by default); `AnsiRenderer.HyperlinksEnabled` is the same switch in code. The terminal opens a link on a Ctrl+click (Shift+click while the application has the mouse).
- **Windows.** `SystemConsoleDriver` turns on virtual terminal processing and writes a string as ANSI only when a cell needs it (an attribute or a link); other strings keep the Console colour calls. Before this the Windows driver drew no bold, italic or underline at all.
- **Popovers.** `IPopoverService` keeps one popover open at a time and places it with `PopoverPlacement`: beside a control it turns to the other side when it does not fit (`Popup` alone only pulls an overlay back on screen, over its target), and at a point it opens with its corner on the point or its far edges on it. `ToolTipHost` still places its own tip.
- **Context menus.** `ContextMenu` (a `Popup`) holds `ContextMenuItem`s: title, shortcut hint, `Execute`, `IsEnabled`, or `ContextMenuItem.Separator()`. Set `Control.ContextMenu` and a right-click on the control opens it at the pointer, and Shift+F10 or the Menu key opens it under the control while it has focus; the deepest control with a menu wins. Up, Down, Home and End move past separators and disabled entries, Enter, Space or a click choose, a letter jumps to the next entry starting with it. The entry runs after the menu has closed and focus is back, so it can open a dialog. Not built: sub-menus, checked entries, per-entry hot keys.

### Suggested improvements

Compared with Terminal.Gui 2.5.0 (its public API and XML docs, not its runtime behaviour). Items marked as built or partly built are described above; the rest are not built. The "not found" notes are grep-based and need a runtime check before work starts. Terminal.Gui owns the main loop, layout and rendering, so the items below are ideas to adopt, not a reason to replace the framework.

**Worth doing soon**

1. **Wide-character and grapheme width.** No width handling was found, so CJK or emoji file names, and mixed-width text, may misalign panels. Check with a CJK file name in a panel, then add a width function to `ConxelString` with tests.
2. **Bracketed paste.** Not enabled (no `ESC[?2004h` found), so a multi-line paste runs each line as it is typed. Enable it in the input source and deliver a paste to the focused control as one event.
3. **Colour capability detection and quantisation.** Map colours to 256 or 16 when the terminal lacks true colour, and query the default foreground and background (OSC 10/11) at start-up so the theme matches the terminal. Not found in iConsole; Terminal.Gui does both.
4. **Virtual time in the test harness.** *Partly built:* `ITimeProvider` (`SystemTimeProvider`, `VirtualTimeProvider`) now feeds `DateTimeProviderService` and `ToolTipHost`. `NotificationJob` and `FrequentExecutionGuardService` still read the clock and `Thread.Sleep` directly, so they are not yet on it.
5. **Pluggable key bindings.** `Command.Key` is fixed at construction. Read bindings from a keymap file, with a conflict report that enforces the key rules in CLAUDE.md (no Alt+letter; Ctrl+Alt is typing). Matches item 11 in `docs/mc-parity.md`.

**Worth designing**

6. **Popover service.** *Built* (see above): `IPopoverService`, `PopoverPlacement` and `ContextMenu`. The Ctrl+O hint and `ToolTipHost` still place themselves.
7. **Percent and fill sizing in `Layout`.** Layout is manual, so resizing needs per-control code. `Dim.Percent` / `Dim.Fill` equivalents would make panel ratios (item 9 in `docs/mc-parity.md`) cheap.
8. **Table upgrades.** Multi-select regions, per-column styles and cell colour getters would make the file list and search results more flexible.
9. **Markdown and image controls.** *Markdown built:* `MarkdownViewerControl` (parser in `iConsole/Markdown/`). The image control is not built: it would use the Kitty graphics protocol, with a cell-based fallback, for the image viewer's ASCII-art gap.
10. **Mouse hold and diagnostics.** Mouse hold-to-repeat for scroll buttons, and an overlay that shows control bounds and focus, alongside `ITERMINAL_RENDER_DUMPS` and `ITERMINAL_KEY_TRACE`. Not built.

**Lower priority**

11. **Chart annotations.** A legend and scatter series for the existing chart controls.
12. **Trace scopes.** Time key handlers and content loads so the 200 ms and 3 s budgets can be checked directly.

### Main-loop watchdog and terminal clipboard

- **`MainLoopWatchDog`** (`iConsole/Diagnostics/`) is a job that times the passes that must stay inside the 200 ms budget: key and mouse handlers (`TerminalInputConsumerJob`) and render passes (`ConsoleBufferRendererJob`). A pass past `Threshold` is reported once through `Stalled` while it is still running, and `Recovered` reports the total when it ends. It measures and reports; it cannot interrupt a handler that never returns. Its events are raised on the watchdog's thread, so a handler must post a notification, not touch the control tree. The application logs both events through Serilog (`MainLoopWatchDogLoggingHostedService`).
- **OSC 52 clipboard** (`Osc52TextClipboardService`) sets the clipboard of the machine the person is at by writing an OSC 52 sequence to the terminal. It is write-only and refuses a text larger than `MaxEncodedLength` or a copy made while the terminal is lent to a child process. `LayeredTextClipboardService` (iTerminal.Core) orders the layers: the system clipboard, then the terminal, then the application's own copy. Over SSH the terminal goes first, since a clipboard program on the remote machine would set a clipboard the person cannot reach. Paste reads the system clipboard and then the application's own copy. tmux needs `set-clipboard on` for OSC 52 to reach the outer terminal.

### Reflow in the text viewer and editor, and the taskbar progress indicator

- **Reflow.** When the width changes, or wrapping is switched on or off, the viewer keeps the reading position as text: the row at the top of the screen is found again by its line and column after the rows are rebuilt (`TextViewerControl.RebuildRows`, `RowOf`). The caret follows the wrapped row (`CursorScreenRow`), and `KeepCursorInText` scrolls in rows when wrapped.
- **Editor wrap.** The editor inherits the viewer's wrap. F6 toggles it (F2 is save in the editor). Wrapped, Up and Down move by screen row and keep the column within the row (`MoveCursorByScreenRow`); unwrapped they move by line as before. Home and End still work by line.
- **Taskbar progress.** `TaskbarProgressJob` polls the background tasks every 250 ms, works out the state with `TaskbarProgressCalculator` (average progress of running tasks; paused when every running task is paused; indeterminate before any progress is reported), and tells `ITaskbarProgress` only when that changes. `WindowsTaskbarProgress` drives `ITaskbarList3` on a thread in a single-threaded apartment, against the console window. Elsewhere `NullTaskbarProgress` is used. The Windows path is compiled on Linux but has not been run on Windows. Windows Terminal may host the console in a window that has no taskbar button to show the indicator on.
