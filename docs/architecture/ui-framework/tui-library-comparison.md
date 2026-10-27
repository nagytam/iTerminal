### iConsole compared with other TUI libraries

Compared with Terminal.Gui, Textual (Python), Ratatui (Rust), Bubble Tea + Lip Gloss + Bubbles (Go), Spectre.Console (.NET), FTXUI (C++), notcurses, Ink (JS) and prompt_toolkit (Python). Based on `github/main` at 585b37f. "Not found" means a grep of `iConsole/` came back empty; confirm at run time before starting work. The comparison is of public APIs and documentation, not of the other libraries' run-time behaviour.

#### Where iConsole is already level or ahead

| Area | iConsole | Notes |
| --- | --- | --- |
| Widget set | ~60 controls: tables, tree, tabs, wizard, calendar, colour picker, sliders, charts (bar, line, sparkline, gauge, heatmap, breakdown), hex/text/markdown viewers, editor with bracket matching, menu bar, toolbar, toasts, context menus, tooltips, popovers | Wider than Ratatui, Bubble Tea and FTXUI out of the box; close to Terminal.Gui and Textual |
| Virtualized data | `VirtualListBox`, `VirtualTable`, `PagedDataSource` with read-ahead and cancellation | Few libraries have this; Textual's `DataTable` and Terminal.Gui's `TableView` are not paged |
| Test harness | Virtual terminal, `IInputInjector`, `VirtualTimeProvider`, screen snapshots, command log | On a par with Textual's `Pilot`; ahead of Terminal.Gui, Ratatui (`TestBackend` only) and Spectre |
| Terminal features | SGR mouse, any-motion hover, OSC 8 hyperlinks, OSC 52 clipboard, curly/dotted underline with colour, lending the terminal to a child shell | Lending the terminal (Ctrl+O) has no counterpart in the others |
| Diagnostics | Main-loop watchdog, key trace, frame dumps | Textual has devtools; the others have little |
| Localization | `ILocalizer` with resources | Absent from Ratatui, Bubble Tea, FTXUI, Spectre |

#### Missing features

Ordered by value to iTerminal. "Seen in" names the libraries that have it.

**Terminal fidelity**

1. **Wide-character and grapheme width.** Not found (`GetStringWidth`, `StringInfo`, East Asian width). CJK and emoji names misalign every panel. Seen in: Terminal.Gui, Textual (`rich.cells`), Ratatui (`unicode-width`), notcurses, Bubble Tea (`go-runewidth`). Add `ConxelString` width and column-aware clipping, then use it in `Table`, `ListBox`, `TextBox` and the editor.
2. **Bracketed paste.** Only `OfficeTextRenderer` mentions it; the input path does not enable `?2004h`. A multi-line paste is typed line by line. Seen in: Terminal.Gui, Textual, prompt_toolkit, Bubble Tea. Deliver one `PasteEvent` to the focused control.
3. **Colour capability detection and down-sampling.** No `COLORTERM`, 256- or 16-colour mapping. On a 16-colour terminal (Linux console, some SSH clients) themes render wrongly. Seen in: Terminal.Gui, Textual, Lip Gloss (`termenv`), Spectre (`ColorSystem`), notcurses. Also query the terminal's default colours (OSC 10/11) so a theme can follow light/dark.
4. **Synchronized output (`?2026h`).** *Built.* `ConsoleBufferRendererJob` calls `IConsoleDriver.BeginFrame()` / `EndFrame()` around each frame's writes; `VirtualSequenceConsoleDriver` and `SystemConsoleDriver` (when virtual-terminal processing is on) write `SynchronizedOutput.Begin` / `End`. Off with `ITERMINAL_SYNC_OUTPUT=0`, and for `TERM=linux` or `dumb`. Seen in: Textual, Ratatui (crossterm), Bubble Tea v2, notcurses.
5. **Focus reporting (`?1004h`).** Not found. Lets the app pause polling and refresh when the terminal regains focus. Seen in: Textual, Bubble Tea, Terminal.Gui.
6. **Kitty keyboard protocol.** *Built for the first flag* (disambiguate): `KittyKeyboardProtocol` queries with `CSI ? u`, and pushes `CSI > 1 u` only after the terminal answers, then pops it on exit and before a lend (Ctrl+O). `TerminalInputUtf8Decoder` reads `CSI code ; mods u` into Ctrl+I vs Tab, Ctrl+Shift+letter, Ctrl+Enter and Esc without a timeout. Switch off with `ITERMINAL_KITTY_KEYBOARD=0`. Not built: key release and repeat reporting (releases are swallowed), function and keypad codes, text-as-codepoints, and the Windows input plug-in (Windows Terminal does not support the protocol yet).
7. **Inline images.** Not found (Kitty graphics, Sixel, iTerm2). The image viewer falls back to cell art. Seen in: notcurses, Textual (`textual-image`), Bubble Tea ecosystem. Add an `ImageControl` with protocol detection and a half-block fallback.
8. **Terminal title, bell and notification (OSC 0/2, BEL, OSC 9/777).** *Built.* `ITerminalTitleService` sets the title with OSC 2, saves the terminal's own title first (`CSI 22;2 t`) and puts it back with `Restore()` (`CSI 23;2 t`); `TerminalTitleJob` in `iTerminal.Core` keeps it on the focused panel's folder and restores it on exit. `ITerminalNotificationService` sends `Bell()` and `Notify(title, body)` as OSC 9 (iTerm2, Windows Terminal, Ghostty), OSC 777 (VTE, Konsole, WezTerm, foot) or the bell (unknown terminals, tmux, screen, Linux console), chosen by `TerminalSequences.DetectNotificationStyle`. `ITERMINAL_NOTIFY=off|bell|osc9|osc777` overrides it; `ITERMINAL_TERMINAL_TITLE=0` switches the title off. Text is stripped of control characters and semicolons (so OSC 9 cannot become Windows Terminal's `9;4` progress sequence); nothing is written while the terminal is lent. Not built: Kitty's OSC 99, tmux passthrough, and calling `Notify` from the toast service when the terminal has lost focus (needs item 5). Seen in: Textual, Terminal.Gui.

**Layout and styling**

9. **Percent, fill and constraint layout.** *Stack layout built:* `SizeRule` (`Fixed`, `Percent`, `Fill(weight)`, each with `WithMin` / `WithMax`), `SizeSolver.Solve` (exact cell counts, no cell lost to rounding, last children shrink first on overflow) and `StackPanel` (row or column, gap, hidden children take no space, re-laid out on every draw). Not built: dock and grid panels, and use by the dialogs and the file panels. Seen in: Terminal.Gui (`Dim`/`Pos`), Textual (fr, %, grid), Ratatui (`Constraint`), FTXUI (flex), Lip Gloss.
10. **Styling by rules.** Colours are set on controls and a `ColorScheme`. Textual's TCSS and Lip Gloss's composable `Style` let a theme restyle by type, class and state (`:focus`, `:disabled`, `:hover`). A small selector table in `IThemeService` would remove per-control colour code.
11. **Border and box-drawing styles.** `Border.cs` exists; check for rounded, double, thick and dashed sets, per-side borders, and border titles with alignment (Lip Gloss, Textual, Ratatui `Block`).
12. **Gradients and alpha blending.** Not found. Textual and Lip Gloss blend and interpolate colours; useful for shadows under popups and fading toasts.
13. **Animation.** No tweening or easing (only `BouncingLabel` under `Experimental`). Textual (`animate`), Terminal.Gui (timeouts) and Bubble Tea (Harmonica) offer it. A small `Animator` on `ITimeProvider` would make it testable with `VirtualTimeProvider`.
14. **Drop shadows and z-order helpers.** Layers exist; an optional shadow on `Popup` and `Dialog` is cosmetic but common (Terminal.Gui, Textual, Turbo Vision lineage).

**Controls and interaction**

15. **Multi-line rich text editor.** `TextEditorControl` is code-oriented. Textual's `TextArea` adds syntax highlighting by tree-sitter, selections, undo grouping and line numbers; check which of these the editor lacks before building.
16. **Syntax highlighting as a service.** Markdown has a parser; code blocks and the viewers would share one tokenizer (Textual `TextArea`, Spectre via plug-ins, Rich `Syntax`).
17. **Table upgrades.** Cell selection, multi-region select, per-cell style getters, frozen columns and column resize by mouse (Terminal.Gui `TableView`, Textual `DataTable`). Marking, type-ahead and a scroll bar are still missing from the virtual controls.
18. **Splitter, resizable panes and dockable regions.** *Splitter built:* `Splitter` holds two panes (side by side or stacked) with a divider that moves by arrow keys, PageUp/PageDown, Home and mouse drag, keeps a `Ratio` across resizes and honours `MinFirstSize` / `MinSecondSize`. Not built: more than two panes, nested-splitter persistence, and use by the workspace manager. Seen in: Terminal.Gui `TileView`, Textual, FTXUI `ResizableSplit`.
19. **Sub-menus and checked entries in `ContextMenu` and `MenuBar`.** Documented as not built. Seen in: Terminal.Gui, Textual (`ContextMenu` add-ons).
20. **Drag and drop inside the app.** Mouse drags select text; there is no drag source or drop target model (Terminal.Gui has limited support, Textual `Drag`). A file-manager use case: drag entries between panels.
21. **Command palette as a framework service.** iTerminal has one in `Core`; Textual ships it in the framework with fuzzy matching and providers. Moving the palette shell into `iConsole` makes it reusable.
22. **Fuzzy matching helper.** *Helper built:* `FuzzyMatcher.Match` / `Rank` score a subsequence match (word-start, camel-case and consecutive bonuses) and return the matched positions for highlighting. Wired into the command palette as a fallback after substring matches (three or more letters); not yet in `AutoCompletion`, `ComboBox` or type-to-search. Seen in Textual, Bubbles `list`, prompt_toolkit completers.
23. **Input masks and validation.** `TextBox` has no validator pipeline or mask; Textual `Input` has validators with live error text, Terminal.Gui `TextValidateField` has masks.
24. **Scroll containers.** A generic `ScrollView` that hosts arbitrary children with a scroll bar (Terminal.Gui, Textual `VerticalScroll`).

**Framework**

25. **Pluggable key bindings.** `Command.Key` is fixed at construction (docs item 5). Textual `BINDINGS`, Terminal.Gui `KeyBindings` and Bubbles `key.Binding` allow a keymap file with a conflict report and help generated from it.
26. **Accessibility.** Nothing found. Options: a high-contrast theme, a "no animation / no Unicode art" mode, and a plain linear text dump of the focused control for screen readers (Textual has none either; notcurses has a plain mode).
27. **Declarative or reactive UI.** Controls are imperative. Textual's reactive attributes and Ink/React-style diffing, or Elm-style `Model-Update-View` in Bubble Tea, make state-driven UI simpler. `ObservableValue` exists; a binding helper (`control.Bind(x => x.Text, source)`) is a small first step.
28. **Inline (non-alt-screen) mode.** Check whether apps can render in the scrollback without taking the whole screen (Spectre `Live`, Bubble Tea inline, Ink, Textual inline). Useful for small prompts and progress views.
29. **Progress and live display parity with Spectre.** Multi-task progress groups, status spinners with named styles, live-updating tables and trees outside a full application.
30. **Mouse polish.** Hold-to-repeat on scroll buttons, double and triple click events, wheel with Shift for horizontal scroll (some listed in docs item 10).
31. **Layout inspector / debug overlay.** Control bounds, focus path, dirty regions (Textual devtools, React DevTools-style). Listed in docs item 10 as not built.
32. **Recording and replay.** Dump a session as a cast file (asciinema v2) from the screen logger, and replay input scripts. The virtual terminal and `IScreenLogger` already have the data.
33. **Right-to-left and bidirectional text.** Not found; rare in TUIs (notcurses and Textual do not fully support it either). Lowest priority.

#### Suggested order

1. Items 1, 2, 3, 4: terminal fidelity, each small to medium, no API change for consumers.
2. Items 9, 18, 25: layout, splitter and key maps, which unblock panel ratios and `docs/mc-parity.md` items 9 and 11.
3. Items 7, 6, 5: protocol additions that need a capability query at start-up; share one detection step.
4. Items 17, 19, 20, 22, 23: control upgrades the file manager can use at once.
5. The rest as needed.

Each item should land with tests on the virtual terminal; items 1 to 8 also need a fall-back for terminals that do not answer a capability query.

#### Third survey: libraries from other languages

Compared with libraries in languages not yet covered, besides those in the first two surveys: libvaxis (Zig), Ink and OpenTUI (Node, with a Yoga flexbox core), terminal-kit and ora / cli-spinners (Node), Jexer (Java), tvision, the C++ port of Turbo Vision, Dear ImGui and ImTui (C++), Mosaic and Kotter (Kotlin), SwiftTUI and TermKit (Swift), Ratatouille (Elixir), Notty (OCaml), nocterm (Dart), illwill (Nim), termui (Go), and in Rust tachyonfx (effects), tui-logger, tui-realm, indicatif and inquire; huh and Harmonica from the Charm family (Go); questionary and py-cui (Python). "Not found" means a grep of `iConsole/` on `github/main` at 803f5c1 came back empty. Numbering continues from the second survey (items 34 to 58, in the pull request that adds it); items 1 to 58 are not repeated.

**Terminal protocol**

59. **Cursor shape (DECSCUSR, `CSI n SP q`).** Not found. Ratatui, crossterm, libvaxis, Terminal.Gui and notcurses set a bar in text boxes, a block for overwrite mode and an underline for read-only. `Cursor` only carries position and visibility.
60. **In-band resize (mode 2048) and colour-scheme change notice (mode 2031).** Not found. libvaxis, Kitty, Ghostty and Contour tell the program when the window is resized and when the terminal switches between light and dark, with no polling and no SIGWINCH. iConsole polls the window size; the theme could follow the terminal's scheme (see item 34).
61. **Explicit character width (Kitty text sizing, OSC 66) and grapheme clusters.** Not found. libvaxis asks the terminal for the width of a cell instead of guessing from tables, which also fixes emoji with zero-width joiners and variation selectors. Complements item 1, which needs a width table first.
62. **Mouse pointer shape (OSC 22).** Not found. libvaxis shows a hand over links and buttons, a text cursor over text boxes and a resize arrow over splitters. Only a few terminals honour it; the sequence is ignored elsewhere.
63. **Shell integration marks (OSC 7 working directory, OSC 133 prompt and command marks).** Not found. iTerminal lends the terminal to a child shell (Ctrl+O); reading the child's reported directory would let the panels follow its `cd`, as mc does with its subshell, and OSC 133 marks give command start, end and exit status for a status line. Seen in: iTerm2, Kitty, WezTerm and VS Code terminals as senders; Jexer and Urwid's `Terminal` as receivers.
64. **Embedded terminal pane.** Not found. Run a child process inside a pane with a VT parser and a cell grid (Urwid `Terminal`, blessed `Terminal`, Jexer, notcurses `ncsubproc`, Textual-terminal, tmux). Today the child owns the whole screen. A pane would allow a shell under the file panels or a command output that stays on screen. Needs a VT100/xterm parser with scroll regions, alternate screen and mouse pass-through; the biggest item in this survey.

**Layout**

65. **Content-based sizing.** Not found. `SizeRule` has fixed, percent and fill, but a control cannot report its preferred size. Terminal.Gui `Dim.Auto`, prompt_toolkit `Dimension(preferred, min, max)`, Ink / Yoga auto, FTXUI natural size and Brick's `Widget` sizing let a label, button row or table size to what it holds. Add `SizeRule.Auto` backed by a `GetPreferredSize(available)` virtual on `Control`.
66. **Cross-axis alignment and main-axis justification.** `StackPanel` stretches. Yoga (Ink, OpenTUI), Textual `align` and Lip Gloss `Place` offer start, centre, end and stretch on the cross axis and space-between / space-around on the main axis, plus wrap. Needs item 65 for centre and end to mean anything.
67. **Wrapper views that constrain a child.** Cursive `ResizedView`, `PaddedView`, `ShadowView` and `LayerView`; Terminal.Gui `Pos.Center`, `Pos.AnchorEnd`. A `Center`, `MinMax` (minimum and maximum size) and `Shadow` wrapper would replace the hand-placed offsets in the dialogs.
68. **Window management for overlapping windows.** Not found (`Tile`, `Cascade`). Turbo Vision (tvision) and Jexer offer tile, cascade, maximise, minimise, next and previous window, and a window list. Far's screens list is the closest in file managers; `Screens/` has the structure but no tile or window list. Extends item 43.

**Controls**

69. **Streaming log view.** Not found. Textual `RichLog` / `Log`, blessed `Log`, tui-logger and Kotter append lines from a running task, follow the tail until the user scrolls up, cap the line count, colour by level and filter. Useful for the transfer, git and docker output now shown in viewers.
70. **More chart types.** `BarChart`, `BreakdownChart`, `Gauge`, `Heatmap`, `LineChart` and `Sparkline` exist; no pie / donut, scatter, histogram, candlestick, stacked bars, axes with tick labels or multi-series legend (termui, blessed-contrib, Ratatui `Chart`, plotext). Pair with the braille canvas (item 36) for resolution.
71. **Named spinner sets.** `Spinner` has one braille frame set. cli-spinners (80 sets), Spectre `Spinner.Known`, Bubbles `spinner` and throbber-widgets give dots, line, arrow, bounce, clock and moon sets, plus a fall-back to ASCII (`|/-\`) for terminals without braille.
72. **Form bound to a model object.** `FormPanel` exists. huh, Asciimatics `Frame.data`, Terminal.Gui and Cursive `Dialog` bind fields to a record and its validators, group fields into pages, and show or hide a field from another field's value. Add `FormPanel.Bind(model)` with reflection or a source generator and per-field validation (see item 23).
73. **Immediate-mode helpers.** Dear ImGui and ImTui build a UI by calling functions every frame, with no control tree to manage. Out of scope as a framework change, but a small `Ui.Button(...)` helper over a temporary `BoxPanel` suits tools and debug panels. Low priority.
74. **Utility dialogs from Turbo Vision.** tvision and Jexer ship an ASCII / character-map table, a calculator and a calendar, as well as `THelp` hypertext help with links between topics. `HelpDialog` and `Calendar` exist; check whether help topics can link to each other. Low priority.

**Framework**

75. **Thread-safe post to the UI loop.** Not found (`Dispatcher`, `Invoke`). Cursive `cb_sink`, Terminal.Gui `Application.Invoke`, Textual `call_from_thread`, Brick `BChan` and Mosaic's coroutine scope marshal a callback onto the UI loop. CLAUDE.md tells workers to post a notification or poll state from `OnIdle`, which each control re-implements. An `IUiDispatcher.Post(Action)` run once per frame would replace those polls and make the stale-result check (`IsCurrentUrl()`) one helper.
76. **Post-render effects.** tachyonfx (fade, dissolve, sweep, slide, colour shift), Asciimatics effects, notcurses fades and Textual `animate` change the cells of a rectangle for a time after drawing. Needs animation (item 13) and, for fades, colour depth (item 34). Harmonica-style spring motion is a variant of the same clock.
77. **Screenshot export for users.** Only `BufferHelper.DumpBufferToHtml` for debugging. Rich `export_svg` / `export_html`, Textual `save_screenshot`, blessed `screenshot` and tmux `capture-pane` give the user a command that saves the screen as SVG, HTML, ANSI text or plain text, with the clipboard as a target.
78. **Accessibility roles and a plain mode.** Not found (`Aria`, `ScreenReader`, `HighContrast`). Ink has `aria-role` and `aria-label` and `isScreenReaderEnabled`; huh has an accessible mode that swaps the full-screen form for line prompts. Controls could expose a role, a label and a state string, and a switch could make the UI print the focused control's text on every focus change. Extends item 26.
79. **Frame-time overlay.** Dear ImGui's metrics window, Textual's devtools and Terminal.Gui's `Application.Diagnostics` show frame time, redraws and dirty cells. `MainLoopWatchDog` and frame dumps exist; an on-screen overlay (F-key) would show the render and key-handler times that the 200 ms rule depends on. Extends item 31.
80. **Typed message loop for tests and tools.** tui-realm, Bubble Tea `teatest`, Ratatouille and Notty drive an application with a stream of messages and assert on the model. `IInputInjector` and `ConsoleAppDriver` do this for keys; messages for non-key events (resize, focus, paste, timer tick) would extend it once items 2, 5 and 60 exist.

#### Suggested order for the third survey

1. Item 75 (UI dispatcher): small, removes polling in several controls, and the effects, log and chart items rely on it.
2. Items 65 and 66 (preferred size, alignment): they make `BoxPanel`, dialogs and forms lay out without fixed sizes.
3. Items 69, 71 and 70: the log view, spinner sets and extra charts are self-contained controls.
4. Items 59, 60 and 63: three small protocol additions that share the capability query of items 35 and 5.
5. Items 64 and 68 last: large, and only worth it for a shell pane and multi-window editing.
