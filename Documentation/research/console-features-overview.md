# Console and terminal UI features: survey overview

This document records console and terminal UI libraries from languages the older comparison does not cover, checked against iConsole, and the features they have that iConsole lacks. The older comparison in `Documentation/architecture/ui-framework/tui-library-comparison.md` is a read-only record of earlier surveys. Its highest item is 131, so the items here start at 132.

## Overview

### Where iConsole is already level or ahead

- None of the eight libraries in Survey 1 describes a main-loop watchdog, lending the terminal to a child shell (Ctrl+O), or borders that join across separate boxes (`LineCanvas`, `BorderedGrid`). These are not in the pages read.
- Localization with a completeness test: HermesConsoleUI has internationalization. The other pages read do not describe one.
- Dialogs that move and resize with the keyboard (Ctrl+F7, Ctrl+F8) and the mouse: not described in the pages read.
- Paged data sources with read-ahead, cancellation and a stale-result rule (`PagedDataSource`): SageTUI and Mire have virtual lists and tables, but their pages do not describe paging.
- Level: tree, table, tabs, splitter, progress group, live table, calendar, spinner, Markdown, toasts, context menus, tooltips, popovers, wizard, form, pie, scatter, histogram and candlestick charts, log view with follow and filters, and a test harness with input injection and virtual time (SageTUI and termr describe the same kind of harness).

### Missing features

| Feature | Seen in | Status in iConsole | Fits |
| --- | --- | --- | --- |
| Reorderable list (item 132) | SageTUI, fortran-dialog | Missing; the collection's `MoveItem` is not a control | `ListBox<T>`, `VirtualListBox<T>` |
| Reusable diff view (item 133) | SageTUI | Partly: the diff code is in `iTerminal.Core`, none in `iConsole` | `TextViewerControl`, `Table<T>` |
| Node-and-link diagram (item 134) | ConsoleMonster | Missing | `LineCanvas`, `BorderedGrid` |
| Grouped table rows (item 135) | ConsoleMonster | Missing | `Table<T>`, `VirtualTable<T>` |
| Find in a log view (item 136) | ConsoleMonster | Partly: the line filter exists, a find does not | `LogView`, `SearchEngine` |
| Export of table or list data (item 137) | ConsoleMonster, termr | Partly: screen export only (`ScreenRecorder`) | `Table<T>`, `ScreenRecorder` |
| Chart zoom, pan and crosshair (item 138) | ConsoleMonster | Missing | `ScatterChart`, `ChartCanvas` |
| Metric tile (item 139) | termr | Missing | `BoxPanel`, `Digits` |
| Data profile of a table (item 140) | termr | Missing | `Table<T>`, the CSV viewer |
| Undo and redo in a single-line field (item 141) | SageTUI | Partly: the editor has undo, `TextBox` does not | `TextBox` |
| Crash banner and restore after an unhandled exception (item 142) | SageTUI | Missing as a banner; the normal exit path was not read | `Desktop`, `ConsoleOwnership` |
| Indent and dedent helpers (item 143) | mist | Partly: wrap, cut and expand exist | `StringHelper` |

### Suggested order

Ranked for iTerminal, for the items of Survey 1 only.

1. Item 142 (crash banner and restore): a terminal left broken by a crash is the most visible failure. Read the existing restore path before work starts.
2. Item 132 (reorderable list): the playing queue has no move command (`MoveUp`, `MoveDown` and `Reorder` find nothing in `iTerminal.Core`).
3. Item 136 (find in a log view): git, docker and transfer output is shown in `LogView`.
4. Item 137 (export of table data): search results and comparisons could be saved as CSV or Markdown.
5. Item 133 (diff view in `iConsole`): moves the diff drawing out of `iTerminal.Core` and removes a second copy later.
6. Item 141 (undo in `TextBox`): rename and filter fields.
7. Item 135 (grouped table rows).
8. Item 138 (chart zoom and crosshair).
9. Item 143 (indent and dedent): also gives the duplicate wrap in `HelpDialog` one home.
10. Item 139 (metric tile).
11. Item 134 (node-and-link diagram).
12. Item 140 (data profile): low, only for the CSV viewer.

## Surveys

### Survey log

Surveys are appended in order. Survey 1 was run on 2026-11-20.

#### Survey 1: Libraries from F#, PowerShell, R, Scheme, Mojo and Fortran

This survey covers eight libraries and tools from six languages the older comparison does not cover: SageTUI and Mire (F#), ConsoleMonster and HermesConsoleUI (PowerShell), termr (R), guile-ncurses (Scheme, Guile), mist (Mojo) and fortran-dialog (Fortran). It was run on 2026-11-20. The basis is the docs and README of each library (the package page, the repository or the manual), not run-time behaviour. "Not found" means a grep of `iConsole/` and `iTerminal.Core/` (`*.cs`) on branch `docs/research-overviews` at commit `41d889b2`. Several searches also ran over the whole repository, including the tests; where a term matched only tests or unrelated words, that is stated.

**Languages covered before this survey** (from the older comparison, not searched again): C#, Python, Rust, Go, C++, C, JavaScript and TypeScript, Java, Kotlin, Scala, Swift, Dart, Haskell, OCaml, Elixir, Gleam (BEAM), Erlang, Zig, Nim, Ruby, PHP, Julia, Perl, Raku, Common Lisp, Pascal and Delphi, Ada, Haxe, D, V, Lua, Racket, Odin, Clojure, Crystal, Elm, Tcl, Pharo and Vala.

**Research table**

| Library | Language | Repository | Docs read | Version and release date |
| --- | --- | --- | --- | --- |
| SageTUI | F# | [WillEhrendreich/SageTUI](https://github.com/WillEhrendreich/SageTUI) | [NuGet](https://www.nuget.org/packages/SageTUI) | 0.9.10, 5 August 2026 (NuGet page; the README names 0.9.3) |
| Mire | F# | [HelgeSverre/mire](https://github.com/HelgeSverre/mire) | [NuGet](https://www.nuget.org/packages/Mire) | 0.8.0, dated 10/12/2026 on the page (day and month order not stated); pre-1.0 |
| ConsoleMonster | PowerShell (and .NET) | [EvotecIT/ConsoleMonster](https://github.com/EvotecIT/ConsoleMonster) | [site](https://consolemonster.dev/), [NuGet](https://www.nuget.org/packages/ConsoleMonster/2.3.0) | 2.3.0, last updated 11/6/2026 on the page (format not stated); NuGet lists 3.0.0 as newer; no GitHub release |
| HermesConsoleUI | PowerShell | [damdavidprieto/HermesConsoleUI](https://github.com/damdavidprieto/HermesConsoleUI) | [PowerShell Gallery](https://www.powershellgallery.com/packages/HermesConsoleUI) | 2.1.0, 8 February 2026; the licence is not named on the page |
| termr | R | [menshikovs/termr](https://github.com/menshikovs/termr) | [README](https://rdrr.io/cran/termr/f/README.md) | Not confirmed: the README shows development 0.9.0.9000 and says the CRAN release is pending; a search snippet gave 1.0.1 (**unverified**); no release date found |
| guile-ncurses | Scheme (Guile) | GNU project | [manual](https://www.gnu.org/software/guile-ncurses/manual/html_node/index.html) | 2.2, 8 April 2017; no newer release found |
| mist | Mojo | [thatstoasty/mist](https://github.com/thatstoasty/mist) | [README](https://github.com/thatstoasty/mist) | No release on the page; the install text names tag v0.4.0; no date found |
| fortran-dialog | Fortran | [interkosmos/fortran-dialog](https://github.com/interkosmos/fortran-dialog) | [README](https://github.com/interkosmos/fortran-dialog) | No release on the page; described as a work in progress |

**Features read, with sources**

- **SageTUI.** Elm-style program with init, update, view and subscribe; flexbox-like layout with fill, percent, minimum and maximum sizes; 22 widgets, among them orderable lists, a diff view, a form, a fuzzy finder, a split pane and a text editor; mouse hit-testing and bracketed-paste handling; a test harness with keys, clicks and virtual time; add-ons for undo and redo, a debugger overlay and a crash-recovery banner. Source: [NuGet](https://www.nuget.org/packages/SageTUI), [GitHub](https://github.com/WillEhrendreich/SageTUI).
- **Mire.** Stack, dock, box, scroll, overlay and nine-point positioned layout; virtualized list and table, command palette, completion, toast, tooltip, tabs, status bar, Markdown and image preview; Kitty keyboard protocol with press, repeat and release, bracketed paste, focus events and theme change notices; truecolour, synchronized output, grapheme-cluster widths and the Kitty graphics protocol. Source: [NuGet](https://www.nuget.org/packages/Mire), [GitHub](https://github.com/HelgeSverre/mire).
- **ConsoleMonster.** Panels, dashboards, trees, accordions, splitters, diagrams with nodes, links and groups; tables fitted to the window with grouping; interactive charts with crosshair, zoom and pan; log viewers with find; export to Markdown, HTML, SVG and text; recording and replay; `Assert-ConsoleOutput`; plain lines when output is redirected. Source: [site](https://consolemonster.dev/), [NuGet](https://www.nuget.org/packages/ConsoleMonster/2.3.0), [GitHub](https://github.com/EvotecIT/ConsoleMonster).
- **HermesConsoleUI.** About 40 functions for text, visuals, interaction and core; theme support; internationalization and localization; PowerShell 2.0 and later with no dependencies. Source: [PowerShell Gallery](https://www.powershellgallery.com/packages/HermesConsoleUI), [GitHub](https://github.com/damdavidprieto/HermesConsoleUI).
- **termr.** Widgets for buttons, checkboxes, dropdowns, inputs, labels, metrics, sparklines, progress, tabs, trees, Markdown, a JSON viewer, log views, data tables, data profiles, key-value lists and SQL; stacks, grids, split panes and scroll views; typed events and a command palette; modal dialogs and screens; stylesheets, a monochrome mode that honours `NO_COLOR`, a high-contrast theme and reduced motion; changed-cell rendering; static export as text, Markdown, HTML, SVG and JSON; a headless test driver with keys, mouse, paste, resize and time. Source: [README](https://rdrr.io/cran/termr/f/README.md), [repository](https://github.com/menshikovs/termr).
- **guile-ncurses.** Bindings to the ncurses library with menu, form (fields, validation, pagination), panel (overlapping windows) and function-key label modules, plus colour and mouse support. Source: [manual](https://www.gnu.org/software/guile-ncurses/manual/html_node/index.html).
- **mist.** Four colour profiles (ASCII, ANSI 16, ANSI 256, truecolour) with mapping to the nearest colour; text styles (bold, faint, italic, strike-through, underline, overline, reverse, blink); ANSI-aware wrap, word wrap, indent, dedent, padding and truncate. It has no widgets, and terminal control has moved to other projects. Source: [repository](https://github.com/thatstoasty/mist).
- **fortran-dialog.** A wrapper around the `dialog` program with 28 widgets: calendar, checklist, directory and file selection, edit box, form, gauge, info box, input box, menu, mixed form, mixed gauge, message box, pause, password box, program, progress and tail boxes, radio list, range box, text box, time box, tree view and yes/no. It needs `dialog` installed and is a work in progress. Source: [repository](https://github.com/interkosmos/fortran-dialog).

**Considered and skipped**

- PoshTui (PowerShell): alpha, last published 23 July 2025, more than a year before this survey; PowerShell 7 or later; GPL-3.0. Another PowerShell library was available. Source: [PowerShell Gallery](https://www.powershellgallery.com/packages/PoshTui/1.0.0-alpha).
- Microsoft.PowerShell.ConsoleGuiTools and PSTui (PowerShell): grid pickers built on Terminal.Gui, which the first survey covers. Not read.
- rtui (R, two packages): wrappers over Textual (Python) and FTXUI (C++), both covered already. The two project pages returned 404 and were not read.
- M_ncurses and M_fixedform (Fortran): ncurses bindings without widgets. M_fixedform aims at forms; known from a search snippet only, not read.
- fortty (Fortran): a colour helper rather than a widget library. Not read.
- mog (Mojo): style definitions only, from a search snippet. Not read.
- Thuja (F#): a small Elm-style library, from a search snippet. Not read.
- Chicken Scheme: no curses or TUI library found in search.
- pip-tui (Guile): pre-alpha and not usable.
- Mojo: no widget framework found. mist is the only toolkit included.

**Already covered (item numbers in the older comparison, or built in iConsole)**

- Elm-style architecture (SageTUI): item 27.
- Flexbox-like layout, fill, percent and gaps (SageTUI, Mire): items 9, 65 and 66; built as `StackPanel`, `GridPanel`, `DockPanel` and `SizeRule`.
- Six border styles and titled borders (SageTUI): built (`BorderStyle`, `BoxPanel`); items 11 and 41.
- Half-block and Braille canvas modes (SageTUI): item 36 (Braille in `ChartCanvas` only).
- Hyperlinks (OSC 8), clipboard (OSC 52), synchronized output and diff rendering (Mire): built.
- Kitty graphics and image preview (Mire): item 7; Sixel is built for the image viewer.
- Kitty keyboard with press, repeat and release (Mire): item 6, partly built.
- Grapheme-cluster widths (Mire): item 61; `TextWidth` is built (item 1).
- Theme change notice (Mire): item 60.
- Bracketed paste (Mire, SageTUI, termr): item 2.
- Password input (SageTUI): item 130, built since the seventh survey (see the status line below).
- Fuzzy finder (SageTUI): items 22 and 112.
- Debugger overlay (SageTUI): items 31 and 79.
- Command palette, completion, status bar, toast, tooltip, tabs, progress, spinner, split view (Mire, SageTUI): built.
- Form (SageTUI, fortran-dialog mixed form): built (`FormPanel`).
- Radio group, checkbox, slider and time entry (SageTUI, fortran-dialog): built (`RadioGroup`, `CheckBox`, `Slider`, `TimePicker`). A separate toggle control was not found; `CheckBox` covers it.
- Calendar, gauge and mixed gauge (fortran-dialog): built (`Calendar`, `Gauge`, `ProgressGroup`).
- Checklist, input box, yes/no and info box (fortran-dialog): prompt kinds built (item 94).
- Message box, pause and wait dialogs (fortran-dialog): item 94, still open.
- Tail box (fortran-dialog): built as the viewer's follow mode (F6 in `FileViewerDialog`).
- Text box, program, progress and edit boxes (fortran-dialog): built (`TextViewerControl`, `LogView`, `TextEditorControl`).
- File and folder selection (fortran-dialog): built (`FilePicker`, `FolderSelectorDialog`).
- Input menu (fortran-dialog): built as `ComboBox` and `AutoCompletion`.
- Tree view and menu (fortran-dialog): built.
- Separator, badge and key-value list (Mire, termr): built (`Separator`, `Badge`, `KeyValueList`).
- Key-hint footer (Mire `KeyHint`): item 51.
- Typed events, key bindings and named commands (termr): item 25.
- Modal dialogs and screens, grid containers, split panes and scroll views (termr): built.
- `NO_COLOR` (termr): item 3.
- High-contrast theme and reduced motion (termr): items 26 and 78.
- Injected paste and resize in tests (termr): items 2 and 80; `IInputInjector` has keys and mouse only.
- Static export as text, HTML and SVG (termr): built (`ScreenRecorder`); item 77 for the screen export as a command. Markdown and JSON export: item 137.
- Plain text when output is redirected (ConsoleMonster): item 123.
- Colour profiles and the nearest colour (mist): items 3, 34 and 35.
- Text styles (faint, overline, blink, reverse; mist): built (`Conxel`).
- Panels and dashboards, log regions, live progress, context menus (ConsoleMonster): built.
- Recording and replay (ConsoleMonster): item 32.
- `Assert-ConsoleOutput` (ConsoleMonster): item 54.
- Function-key labels, panel stacking and form validation (guile-ncurses): built (footer, `Screens`, `FormPanel`); items 23, 51 and 68.

**Missing features**

**Controls**

132. **Reorderable list.** Move a row up or down with keys or the mouse, and keep the new order in the list's model. *Missing.* `ConcurrentObservableCollection<T>` has a `MoveItem` override, but it moves items in the collection only; no control moves rows. Seen in: SageTUI (`OrderableList`, `OrderableVirtualList`), fortran-dialog (`buildlist`). Search terms that found nothing in a control: `Orderable`, `Reorderable`, `ReorderItem`, `MoveUp(`, `MoveDown(` (also nothing in `iTerminal.Core`). Fits `ListBox<T>` and `VirtualListBox<T>`.

133. **Reusable diff view.** Show two texts side by side or as one list, with changed rows marked, as a control in the library. *Partly.* `iTerminal.Core` has `TextDiff` (`Services/Compare/TextDiff.cs`), `FileDiffDialog`, `CompareFoldersDialog` (with its `DiffList` row control) and `BinaryDiffView` (a hex diff). None is in `iConsole`. Seen in: SageTUI (`DiffView`). Search terms over the whole repository: `DiffView`, `UnifiedDiff`, `LineDiff`, `DiffHunk` (no match in `iConsole`; `Diff` matches only unrelated words and the `iTerminal.Core` classes above). Fits `TextViewerControl` and `Table<T>`.

134. **Node-and-link diagram.** Boxes joined by lines, grouped, drawn in cells. *Missing.* Seen in: ConsoleMonster (diagrams with nodes, links and groups). Search terms in both trees: `Diagram`, `DrawNode`, `GraphNode`, `class *Graph*`, `class *Flow*`, `class *Node*` (only the tree data classes `XmlNodeItem` and `JsonNodeItem`). Fits `LineCanvas`, `BorderedGrid` and `Shadow`.

135. **Grouped table rows.** A `Table<T>` or `VirtualTable<T>` whose rows sit under group headings that can be folded. *Missing.* Seen in: ConsoleMonster (tables with grouping). Search terms in `iConsole/Controls/`: `GroupBy`, `GroupHeader`, `RowGroup`, `Grouping` (no match). `Expander` and `Accordion` fold one control, not the rows of a table. Fits `Table<T>` and `VirtualTable<T>`.

136. **Find in a log view.** Next and previous match in `LogView`, separate from the line filter. *Partly.* `LogView.FilterText` and `MinimumLevel` hide lines, but nothing moves to a match. Seen in: ConsoleMonster (log viewports with find). Search terms in `LogView.cs`: `Find`, `FindNext`, `SearchText`, `FindText` (only `LogHighlighters.Find`). Fits `SearchEngine`, which the viewer already uses for regular-expression search (item 107).

137. **Export of table or list data.** Write the rows of a table, list, search result or log to a file as CSV, Markdown or JSON. *Partly.* `ScreenRecorder` saves the screen as text, HTML and SVG, and `BufferHelper.DumpBufferToHtml` is a debugging aid; item 77 covers the screen export. Seen in: ConsoleMonster (views to Markdown, HTML, SVG and text), termr (Markdown and JSON snapshots). Search terms over the whole repository, including tests: `ToCsv`, `ExportCsv`, `WriteCsv`, `ToMarkdown`, `ExportMarkdown`, `ToJson`, `ExportJson` (no match). Fits `Table<T>`, `ListBox<T>` and `ScreenRecorder`.

138. **Chart zoom, pan and crosshair.** Zoom or pan the value range of a chart, and show the point under the pointer. *Missing.* Seen in: ConsoleMonster (interactive charts with crosshair, zoom and pan). The statistical charts section of `ui-framework.md` also lists zoom and hover values as not built. Search terms in `iConsole/`: `Crosshair`, `Zoom`, `Pan` (no match in the charts). In `iTerminal.Core`, `Zoom` is the panel zoom and the map. Fits `ScatterChart` and `ChartCanvas`.

139. **Metric tile.** A large value with a label, a unit and a change indicator, in a box. *Missing.* Seen in: termr (metrics). Search terms in both trees: `Metric`, `StatCard`, `MetricTile`, `MetricCard` (no match). `Digits`, `Gauge` and `KeyValueList` cover parts of it. Fits `BoxPanel`, `Digits` and `Sparkline`.

140. **Data profile of a table.** For each column: the type, the count of empty values, the count of distinct values, the minimum and the maximum. *Missing.* Seen in: termr (data profiles). Search terms in both trees: `DataProfile`, `ColumnProfile` (no match). Fits `Table<T>` and the CSV viewer in `iTerminal.Core`. Low priority.

141. **Undo and redo in a single-line field.** *Partly.* `TextEditorControl` has undo (its file and `TextEditorControl.LineTools.cs` mention it 15 times); `TextBox` has none, and the one mention in `TextBox.cs` is a comment. Seen in: SageTUI (undo and redo listed as a program add-on; whether it applies to fields is not stated). Search terms in `iConsole/Controls/TextBox.cs`: `undo`, `redo`, `UndoStack`. Fits `TextBox`.

142. **Crash banner and restore after an unhandled exception.** After a crash, put the terminal back and show a banner on the next start that offers the log. *Missing as a banner.* Seen in: SageTUI (a crash-recovery banner and terminal restoration after unhandled exceptions). Search terms in both trees: `UnhandledException`, `CrashRecovery`, `crash`, `RestoreTerminal` (no match). The normal exit path in the drivers was not read in this run, so check it first. Fits `Desktop` shutdown, `ConsoleOwnership` and the composition root.

**Text**

143. **Indent and dedent helpers.** *Partly.* `StringHelper` has `WrapWords`, `Cut` and `Expand`, and `HelpDialog.WrapToWidth` is a second word wrap. Indent and dedent are missing. Seen in: mist (indent, dedent, wrap and truncate). Search terms in `iConsole/Helper/`: `Indent(`, `Dedent(` (no match). Fits `StringHelper`; the copy in `HelpDialog` would move there. Low priority.

**Suggested order** (items of this survey): see the ranked list in the Overview above.

#### Status after the pull of 2026-11-20

- Item 130 (secret text entry, listed as not found in the seventh survey of the older comparison) is **built**: `iConsole/Controls/PasswordBox.cs` is a `TextBox` that draws each character as a dot, offers no autocompletion and does not allow copying out. Source: `PasswordBox.cs`.
- Re-checked and still missing, by a grep of the terms named in the older comparison: item 2 (`2004h`, `bracketed paste`, `PasteEvent`), item 3 and 34 (`NO_COLOR`, `COLORTERM`, `TrueColor`, `24-bit`), item 26 and 78 (`HighContrast`, `ReducedMotion`), item 61 (`StringInfo`, `grapheme`, `VariationSelector`), item 31 and 79 (`DebugOverlay`, `Inspector`, `ShowBounds`), item 89 (`IndentGuide`, `RulerColumn`), item 97 (`MiddleEllipsis`, `PathEllipsis`), item 109 (`ClickCount`, `IsDoubleClick`), item 122 (`CaptureMouse`, `MouseCapture`), item 125 (`EDITOR` and `VISUAL` as launch variables: only test data), item 128 (`class Columns`, `class Rows`, `class Padder`, `Toggle`), item 131 (`Zone`, `HitRegion`, `HitZone`), and item 94 for message and wait dialogs (`MessageDialog`, `MessageBox`, `ShowMessageAsync`, `WaitDialog`).
- Item 80: `IInputInjector` (`iConsole/Inputs/IInputInjector.cs`) has `SendKeyAsync` and `SendMouseAsync` only; paste and resize cannot be injected yet.
- Named types of the items marked built in the older comparison were found (for example `FilePicker`, `SyntaxHighlightingService`, `ContextMenu`, `ProgressGroup`, `LiveTable`, `PieChart`, `MarkdownViewerControl`, `MainLoopWatchDog`, `KittyKeyboardProtocol`). No built status was found to be wrong.
