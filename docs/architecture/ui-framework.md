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
| [Comparison with other TUI libraries](ui-framework/tui-library-comparison.md) | Missing features measured against Terminal.Gui, Textual, Ratatui, Bubble Tea, Spectre.Console and others |
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
- **Context menus.** `ContextMenu` (a `Popup`) holds `ContextMenuItem`s: title, shortcut hint, `Execute`, `IsEnabled`, or `ContextMenuItem.Separator()`. Set `Control.ContextMenu` and a right-click on the control opens it at the pointer, and Shift+F10 or the Menu key opens it under the control while it has focus; the deepest control with a menu wins. Up, Down, Home and End move past separators and disabled entries, Enter, Space or a click choose, a letter jumps to the next entry starting with it. The entry runs after the menu has closed and focus is back, so it can open a dialog. Three more things an entry can have: a **hot key** (an `&` before a letter in the title, so `"&Open"` is drawn `Open` with the O underlined and pressing O chooses it; `&&` is a literal ampersand; a letter nobody claims still jumps to the next title that starts with it, and entries sharing a hot key are stepped through rather than chosen), a **check mark** (`IsChecked` true or false makes it a switch, drawn `✓` when true; choosing it turns it over before `Execute` runs, and a check column is reserved for every entry once one has it), and a **submenu** (`Items`; drawn with `▶`, opened with Right, Enter or a click beside the entry or on its left where there is no room, closed with Left or Esc; an entry of a submenu closes the whole chain and runs after it; submenus nest). A click on the parent while a submenu is open goes to the parent, one anywhere else closes the chain. A submenu is not opened by hovering.
- **Splitter.** `Splitter` hosts two panes (`SetFirst`, `SetSecond`) with a one-cell divider; `Orientation.Horizontal` puts them side by side, `Vertical` stacks them. The divider is a focus stop: the arrow keys on the split axis move it one cell, PageUp and PageDown eight, Home centres it; a left-button drag on it moves it too. The position is a `Ratio` (0 to 1), so the proportions survive a resize, held within `MinFirstSize` and `MinSecondSize` (limits that cannot both fit share the space evenly). `SplitChanged` fires on every move.
- **Percent and fill layout.** `StackPanel` lays children out in a row or column, each with a `SizeRule`: `SizeRule.Fixed(cells)`, `SizeRule.Percent(p)` (of the space the gaps leave) or `SizeRule.Fill(weight)` (a weighted share of what is left), each with optional `WithMin` / `WithMax`. `SizeSolver.Solve(length, rules, gap)` does the arithmetic and can be used without the panel: fill shares add up to exactly the space left (largest remainder), a fill that hits a limit is fixed at it and the others share the rest, and when fixed and percent sizes do not fit the last children shrink first. The panel solves again on every draw, so a resize needs no code; a child that is not visible takes no space and no gap. Children span the whole cross axis.

### Virtualized lists and tables

- **`IVirtualDataSource<T>`** (`iConsole/Model/`) is what a virtualized control reads: `Count`, `TryGet(index)`, `Prefetch(start, count)` and `Changed`. `TryGet` is called from `Draw`, so it returns at once; a row that is not loaded yet returns false and the control draws `LoadingText`. `Prefetch` is where a source starts loading what is about to be shown. `Changed` may be raised on any thread; the controls only ask for a repaint from it.
- **Sources.** `ListDataSource<T>` wraps a list already in memory (call `Refresh()`, or pass a list that raises `INotifyCollectionChanged`). `PagedDataSource<T>` loads pages on the thread pool through a `fetchPage(start, count, token)` delegate: it keeps the most recently used pages (`maxCachedPages`), reads ahead past both edges of the screen, never asks twice for a page in flight, limits requests running at once, and keeps a failed page failed (`LastError`) until `Retry()` so a draw loop does not hammer a broken source. `Reset(count)` is for a new query or filter: it cancels what is running and drops a page that was already on its way, so rows of the old query are never shown.
- **Controls.** `VirtualListBox<T>` and `VirtualTable<T>` (columns are the same `ColumnDefinition<T>` as `Table<T>`, with the sort arrow and `SortRequested`) share `VirtualRowsControl<T>`: a cursor and a top row kept as plain row numbers, Up/Down/PageUp/PageDown/Home/End, Enter and a second click on the cursor row raise `ItemActivated`, the wheel moves three rows. A frame and a key press cost the height of the control, not the number of rows: a test drives ten million rows and asserts that only the visible rows are read.
- **Existing controls.** `ListBox<T>` and `Table<T>` are unchanged. They copy the whole list on each draw, so keep them for lists of up to a few thousand rows, where marking, filtering and per-row projection matter. Not in the virtual controls: marking, type-ahead, a scroll bar, cell selection.

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

### Comparison with other TUI libraries

The first comparison above was against Terminal.Gui only. This one widens it to the libraries people pick instead: Textual and prompt_toolkit (Python), Ratatui (Rust), Bubble Tea with Bubbles and Lip Gloss, and tview (Go), FTXUI and Notcurses (C++/C), Spectre.Console (.NET) and Ink (JavaScript). It is based on their documented features, not their runtime behaviour, and on a search of `iConsole` at `github/main` (92fda4d); a "not found" is a grep result and needs a runtime check before work starts. Features that already have an entry under "Suggested improvements" are only referred to by number.

**Where iConsole is level or ahead.** Most of the widget set (tabs, tree, table, virtualized list and table, calendar, wizard, markdown viewer, charts, toasts, tooltips, popovers, context menus with submenus, command palette, splitter, percent/fill panel), hyperlinks (OSC 8), underline styles, OSC 52 clipboard, the Kitty keyboard protocol, synchronized output, localization, a virtual terminal for tests with input injection and virtual time, and a main-loop watchdog. Few of the others have a watchdog or a built-in localizer; none has a file-panel stack on top.

**Gaps, by library that has the feature**

| Feature | Seen in | Status in iConsole | Fits |
| --- | --- | --- | --- |
| Display width of wide characters and grapheme clusters | Notcurses, Textual, Ratatui (`unicode-width`), tcell | Not found (item 1) | `ConxelString`, `Conxel`, the renderer |
| Bracketed paste | prompt_toolkit, Textual, Bubble Tea, tcell | Not found (item 2) | input sources, `IInputInjector` |
| Colour depth detection and quantisation to 256/16 colours; OSC 10/11 default colours | Textual, Notcurses, Ratatui via backends | Not found (item 3) | `AnsiRenderer`, `ThemeService` |
| Focus-in / focus-out reporting (`?1004h`) | Textual, Bubble Tea, tcell | Not found | input sources; pause blinking and auto-refresh while the terminal is in the background |
| Window title (OSC 0/2) and title stack | Textual, Bubble Tea, tcell | Not found | `IConsoleDriver` |
| Inline images: Kitty graphics, iTerm2 and Sixel, with a half-block fallback | Notcurses, Textual (`textual-image`), Ratatui (`ratatui-image`) | Not built (item 9) | new `ImageControl`; the image viewer's ASCII art |
| Sub-cell drawing: braille and half-block canvas for plots and shapes | Ratatui `Canvas`, Notcurses blitters, Bubble Tea (`asciigraph`) | Not found | `LineChart`, `Heatmap`, `Sparkline` would gain resolution |
| Declarative styling (a style sheet or theme file, per-state selectors) | Textual CSS, Lip Gloss, tview themes | Themes exist as code and a theme dialog; no file format | `ColorScheme`, `ThemeService`; a JSON theme file is the small version |
| Grid layout with row/column spans; wrap panel; dock layout | Textual (`grid`, `dock`), FTXUI `gridbox`, Ratatui `Layout` | *Built:* `GridPanel` and `DockPanel` (see below). Wrap panel not built | next to `StackPanel` and `SizeSolver` |
| Scroll container for arbitrary children (clipping plus scroll bars) | Textual `ScrollableContainer`, Terminal.Gui, FTXUI `frame` | Not found; each control scrolls itself | new `ScrollView` over `ScrollBar` |
| Animation and easing (property tweens, fade, slide) on a frame clock | Textual `animate`, Bubble Tea `harmonica`, Notcurses | Not found; `Spinner`, `Skeleton` and `BouncingLabel` animate by hand | `ITimeProvider`, `StartAutoRefresh` |
| Rich inline markup in a label (`[bold red]text[/]`) | Spectre.Console, Textual `Rich` | Not found; attributes are set through `ConxelString` calls | `Label`, `ConxelString` |
| Mouse text selection and copy in viewers and editors | Textual, tview, Terminal.Gui | Not found (drag selection) | `TextViewerControl`, `TextEditorControl`, `ITextClipboardService` |
| Mouse hold to repeat (scroll arrows, spin buttons) | Terminal.Gui, Textual | Not built (item 10) | `ScrollBar`, `NumericUpDown`, `Slider` |
| Drag and drop between controls | Textual, Terminal.Gui, tview | Not found | mouse routing, file panels |
| Tree table (tree with columns) | Textual `DataTable`+`Tree`, Terminal.Gui `TreeTableSource` | Not found | `TreeView`, `Table<T>` |
| Table upgrades: multi-select, cell colour, fixed columns | Textual `DataTable`, tview `Table` | Not built (item 8); virtual table lacks marking and cell selection | `Table<T>`, `VirtualTable<T>` |
| Scatter series and chart legends | Ratatui `Chart`, Textual-plotext | Not found (item 11) | `LineChart` |
| Inline (non-fullscreen) mode: a region that grows under the prompt and leaves scrollback | Ink, Bubble Tea inline, Ratatui `Viewport::Inline`, Spectre `Live` | Not found; the framework owns the whole screen | rendering pipeline; useful for a small CLI built on iConsole |
| Prompt widgets for scripts: select, multi-select, text prompt with validation | Spectre.Console `Prompt`, Bubbles, Inquirer | Dialogs only; no one-call prompt API | thin helpers over the existing dialogs and lists |
| Focus chain control: explicit tab order, focus groups, focus-visible ring | Textual, Terminal.Gui | Tab order follows the tree; no explicit order | `Control`, `TabForwardCommand` |
| Snapshot tests of a screen (golden text/SVG) | Textual `pytest-textual-snapshot`, Ratatui `TestBackend` | `ScreenBuffer` asserts exist; no golden-file helper | `iConsole.Test` helpers |
| Pluggable key bindings from a file | Textual `BINDINGS`, Bubbles `key` | Not found (item 5) | `Command.Key` |
| Debug overlay / inspector (bounds, focus, tree) | Textual devtools, Ratatui `tui-logger` | Not built (item 10) | alongside `ITERMINAL_RENDER_DUMPS` |
| Right-to-left and bidi text | Notcurses (partial), few others | Not found; low priority | text layout |
| Screen reader and accessibility hints | Few; Textual has none beyond a "no colour" mode | Not found; `NO_COLOR` was not checked | `AnsiRenderer` (`NO_COLOR` is cheap to honour) |

**Suggested order.** The first four rows protect correct output on real terminals (width, paste, colour depth, focus), and are small. After those, the biggest gain per line of code is the `ScrollView` and grid layout (every dialog that lays out by hand shrinks), then the sub-cell canvas and image control (the charts and image viewer look much better), then animation and mouse selection. Inline mode and the prompt helpers are only worth doing if iConsole is meant to be used as a library by other programs, which this repository does not need yet.

### Grid and dock layout

- **`GridPanel`** has column and row tracks, each a `SizeRule` (`SetColumns`, `SetRows`; one filling track each by default), `ColumnGap` and `RowGap`. `Add(control, row, column, rowSpan, columnSpan)` puts a child in a cell or across several tracks; a span covers the gaps between its tracks. Tracks are solved with `SizeSolver` on every draw, so a resize needs no code. A hidden child leaves its cells empty (the grid keeps its shape), and a child placed past the last track is clamped to it.
- **`DockPanel`** attaches children to edges in the order added (`DockSide.Top`, `Bottom`, `Left`, `Right`, `Fill`). Each takes a strip of the space the earlier ones left, with a thickness given as a `SizeRule` (one cell by default; a percentage is of the space left at that point). `Fill` takes the rest and children added after it get no space. `Gap` separates a strip from what follows it, and a hidden child takes neither space nor gap.
- Both are unfocusable containers like `StackPanel`; nest them (a dock whose fill is a grid) for full screens.
