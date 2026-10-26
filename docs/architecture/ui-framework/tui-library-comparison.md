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

#### Second survey: more libraries

Compared again with Terminal.Gui v2, Consolonia (Avalonia on a terminal), Spectre.Console prompts, Rich, Urwid, Asciimatics, Blessed / blessed-contrib (Node), tview and tcell, gocui, Bubbles and Glamour (Go), Cursive and the Ratatui add-ons (`tui-textarea`, `ratatui-image`, `tui-tree-widget`), Brick and Vty (Haskell), Lanterna and JLine (Java), ncurses with CDK / Newt, and the Turbo Vision lineage. "Not found" means a grep of `iConsole/` on `github/main` at 84328fe came back empty. Numbering continues from the list above.

**Rendering and terminal capability**

34. **True-colour and 256-colour output.** `Color` is the 16 ANSI colours only. Terminal.Gui, Consolonia, Textual, Rich, Ratatui, tcell, Lanterna and notcurses all draw 24-bit colour. This changes item 3: the first step is a richer palette, then down-sampling for terminals without it. Gradients (item 12), alpha blending and a theme that follows the terminal's light / dark setting depend on it.
35. **Terminal capability record (terminfo, `TERM`, XTGETTCAP).** Not found. tcell, Lanterna, Blessed, Vty and ncurses read terminfo; iConsole checks a few `TERM` values in separate places (hyperlinks, sync output, notifications, Kitty keyboard). One record would feed items 3, 5, 6 and 7 and those switches.
36. **Braille and block-character canvas.** Not found. Ratatui `Canvas`, blessed-contrib and notcurses blitters draw sub-cell graphics (2x4 braille, 2x2 quadrants, half blocks). `LineChart` and `Heatmap` would gain resolution.
37. **Auto-joining line drawing.** Border sets exist, but touching boxes do not merge corners. Terminal.Gui's `LineCanvas` joins the glyphs where panels, tables and group boxes meet; tview and Cursive do the same for split panes.
38. **Big text (FIGlet-style fonts).** `Digits` draws numbers only. Spectre `FigletText`, Asciimatics `FigletText` and Rich + `pyfiglet` give titles and splash screens.
39. **Particle and sprite effects.** Asciimatics (`Sprite`, `Path`, `Particle`) and notcurses. Cosmetic; depends on item 13.
40. **Serving the UI over a network.** Textual (`textual serve`, in a browser) and Charm Wish (a Bubble Tea app over SSH). iConsole has a driver interface and a virtual terminal, so a socket or websocket driver is possible.

**Layout and boxes**

41. **Margin, border and padding as one box model.** Terminal.Gui v2 gives every view `Margin`, `Border` and `Padding` adornments; Textual and Lip Gloss use CSS-style margin and padding. In iConsole each control pads itself. A shared thickness on `Control` would remove per-control offset code and give `GroupBox`, `Alert` and `Popup` one border-title path (item 11). *Built for one container:* `BoxPanel` with `Thickness` margin and padding, six `BorderStyle`s and an aligned title. Not built: the same thickness on every `Control`, and use by `GroupBox`, `Alert` and `Popup`.
42. **Spans and flow layout.** Check `GridPanel` for row and column spans and fractional tracks (Textual `grid`, Brick `hBox` / `vBox`). A wrapping flow panel for chips, badges and toolbars (Lip Gloss `JoinHorizontal`, Terminal.Gui `Pos.Align`) is not found.
43. **Movable, resizable overlapping windows.** Terminal.Gui's `ViewArrangement` and notcurses planes move and resize floating windows by keyboard and mouse. Useful for floating tool windows in the file manager.
44. **Focus groups and spatial navigation.** Tab skips disabled controls today. Lanterna, Cursive, Brick and Terminal.Gui (`NavigationDirection`) add named focus rings and arrow-key movement between neighbours.

**Controls**

45. **Selection prompts as one-call widgets.** Spectre `SelectionPrompt` / `MultiSelectionPrompt` with search and grouping; Bubbles `list` with filtering. `ListBox` plus `SearchBox` covers most of it but is not packaged.
46. **File and folder pickers in the library.** Terminal.Gui `FileDialog`, Bubbles `filepicker`. iTerminal has `FolderSelectorDialog` in `Core`; a generic `IFileSystem`-backed picker could live in `iConsole`. *Built:* `FilePicker` over `IFilePickerSource` (local disk included), with a filter, hidden-file switch and folder mode. Not built: a dialog with a name box, multi-select, an `IFileSystem` source.
47. **Markup for labels.** Rich (`[bold red]x[/]`), Textual `Content`, tview colour tags and Spectre markup colour parts of a label from one string. iConsole has `ConxelString` and an inline-Markdown parser; no public markup-to-`ConxelString` helper was found for labels, tooltips and toasts.
48. **Object and exception tree.** Rich `traceback` / `pretty` and Spectre `WriteException`. `JsonViewerDialog` exists; an exception detail tree for `ErrorPromptDialog` is not found.
49. **Line-editing extras.** prompt_toolkit and JLine: vi / emacs modes, kill ring, persistent history with reverse search, ghost-text completion hints (Fish style), multi-line continuation. `TextBox` has none of these (the only `History` hits are the editor's undo).
50. **Text area features.** Line numbers, soft-wrap toggle, multiple cursors and folding are in Textual `TextArea` and `tui-textarea`. Check `TextEditorControl` first (see item 15).
51. **Reusable key-hint footer.** Bubbles `help` and Textual `Footer` build a footer from the focused control's bindings. The key hints exist per control; a single `Footer` control is not found.
52. **Tree upgrades.** Lazy async children, checkboxes, icons and drag-to-reorder (Terminal.Gui `TreeView`, `tui-tree-widget`, Textual `Tree`). Check `TreeView` for lazy loading and multi-select.

**Framework**

53. **Live-reloading style sheets.** Textual reloads TCSS while the app runs. Needs item 10 first.
54. **Baseline snapshot assertions.** Textual's snapshot plugin and Ratatui's `insta` compare a rendered screen with a stored one and show a diff. iConsole has `ScreenSnapshot` and frame dumps; a stored-baseline helper with a readable diff is not found.
55. **Bubbling events.** Textual messages bubble up the widget tree; Brick and Bubble Tea use one event type. iConsole uses commands and per-control callbacks; a typed bubbling event would simplify dialogs. Complements item 27.
56. **Background-work helper tied to a control.** Textual `work` and Bubble Tea `Cmd`. `TaskManager` covers the running; a helper that drops the result when the control has been removed would encode the stale-result rule from CLAUDE.md.
57. **Extension API.** Terminal.Gui and Textual are hosts for third-party widgets; iTerminal registers controls in its composition root only. A documented extension point for commands and panels is not found.
58. **Windows key release and repeat.** Windows Terminal lacks the Kitty protocol, but `ReadConsoleInput` reports key-up events (tcell, Vty use them). This would finish item 6 on Windows.

#### Suggested order for the second survey

1. Items 34 and 35 (colour depth and one capability record): they unblock items 3, 12 and theme work and are invisible to consumers.
2. Items 41 and 37: one box model and joined borders improve every dialog at once.
3. Items 49, 47 and 55: line editing, markup and bubbling events remove the most repeated code.
4. Items 36, 38 and 54: self-contained additions.
5. Items 40 and 57 only when there is a concrete user.
