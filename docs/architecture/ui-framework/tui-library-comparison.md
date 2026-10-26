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
4. **Synchronized output (`?2026h`).** Not found. Removes tearing on large repaints. Seen in: Textual, Ratatui (crossterm), Bubble Tea v2, notcurses. Cheap: wrap each frame write.
5. **Focus reporting (`?1004h`).** Not found. Lets the app pause polling and refresh when the terminal regains focus. Seen in: Textual, Bubble Tea, Terminal.Gui.
6. **Kitty keyboard protocol / `modifyOtherKeys`.** Not found. Gives real Ctrl+Shift+letter, Ctrl+Enter, key release and Ctrl+I vs Tab, which the CLAUDE.md key rules work around today. Seen in: Textual, Bubble Tea v2, Ratatui (crossterm), notcurses.
7. **Inline images.** Not found (Kitty graphics, Sixel, iTerm2). The image viewer falls back to cell art. Seen in: notcurses, Textual (`textual-image`), Bubble Tea ecosystem. Add an `ImageControl` with protocol detection and a half-block fallback.
8. **Terminal title, bell and notification (OSC 0/2, BEL, OSC 9/777).** Not found as a service. Seen in: Textual, Terminal.Gui.

**Layout and styling**

9. **Percent, fill and constraint layout.** `Layouts/Layout.cs` is a single file; the docs already list `Dim.Percent`/`Fill` as not built. Seen in: Terminal.Gui (`Dim`/`Pos`), Textual (fr, %, grid), Ratatui (`Constraint`), FTXUI (flex), Lip Gloss. Add stack, dock and grid panels with weights and min/max sizes.
10. **Styling by rules.** Colours are set on controls and a `ColorScheme`. Textual's TCSS and Lip Gloss's composable `Style` let a theme restyle by type, class and state (`:focus`, `:disabled`, `:hover`). A small selector table in `IThemeService` would remove per-control colour code.
11. **Border and box-drawing styles.** `Border.cs` exists; check for rounded, double, thick and dashed sets, per-side borders, and border titles with alignment (Lip Gloss, Textual, Ratatui `Block`).
12. **Gradients and alpha blending.** Not found. Textual and Lip Gloss blend and interpolate colours; useful for shadows under popups and fading toasts.
13. **Animation.** No tweening or easing (only `BouncingLabel` under `Experimental`). Textual (`animate`), Terminal.Gui (timeouts) and Bubble Tea (Harmonica) offer it. A small `Animator` on `ITimeProvider` would make it testable with `VirtualTimeProvider`.
14. **Drop shadows and z-order helpers.** Layers exist; an optional shadow on `Popup` and `Dialog` is cosmetic but common (Terminal.Gui, Textual, Turbo Vision lineage).

**Controls and interaction**

15. **Multi-line rich text editor.** `TextEditorControl` is code-oriented. Textual's `TextArea` adds syntax highlighting by tree-sitter, selections, undo grouping and line numbers; check which of these the editor lacks before building.
16. **Syntax highlighting as a service.** Markdown has a parser; code blocks and the viewers would share one tokenizer (Textual `TextArea`, Spectre via plug-ins, Rich `Syntax`).
17. **Table upgrades.** Cell selection, multi-region select, per-cell style getters, frozen columns and column resize by mouse (Terminal.Gui `TableView`, Textual `DataTable`). Marking, type-ahead and a scroll bar are still missing from the virtual controls.
18. **Splitter, resizable panes and dockable regions.** Not found as a control (Terminal.Gui `TileView`, Textual `Splitter`-style containers, FTXUI `ResizableSplit`). The workspace manager would use it for panel ratios.
19. **Sub-menus and checked entries in `ContextMenu` and `MenuBar`.** Documented as not built. Seen in: Terminal.Gui, Textual (`ContextMenu` add-ons).
20. **Drag and drop inside the app.** Mouse drags select text; there is no drag source or drop target model (Terminal.Gui has limited support, Textual `Drag`). A file-manager use case: drag entries between panels.
21. **Command palette as a framework service.** iTerminal has one in `Core`; Textual ships it in the framework with fuzzy matching and providers. Moving the palette shell into `iConsole` makes it reusable.
22. **Fuzzy matching helper.** Seen in Textual, Bubbles `list`, prompt_toolkit completers. One shared scorer for `AutoCompletion`, `ComboBox`, the palette and type-to-search.
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
