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

#### Survey 2: libraries from Bash, Pony, Roc, Objective-C and Emacs Lisp

This survey covers eight libraries and tools from five languages no earlier survey covers: tuish and boxlib (Bash), termax.pony (Pony), roc-tui (Roc), OCCurses (Objective-C), and vui.el, the Emacs Widget Library and tui.el (Emacs Lisp). It was run on 2026-11-20. The basis is the README or manual of each project, read with `WebFetch`, and the repository record (last push, release, archive flag) read from the GitHub API on the same day; it is not run-time behaviour. Several of the eight are older than a year, archived, or the only library found in their language, and each is marked below. "Not found" means a grep of `iConsole/` and `iTerminal.Core/` (`*.cs`) on branch `docs/compare-console-survey-2` at commit `fcc5640e`. The re-check of items 44, 56, 94, 119, 124, 128, 131, 132, 136, 141 and 142, and of items 3 and 34 (no `38;2`, `TrueColor`, `24-bit` or `COLORTERM` terms in `iConsole/`), found no status change; item 142's exit path is still unread.

**Languages covered before this survey** (from Survey 1, the older comparison and the later surveys, not searched again): C#, Python, Rust, Go, C++, C, JavaScript and TypeScript, Java, Kotlin, Scala, Swift, Dart, Haskell, OCaml, Elixir, Gleam (BEAM), Erlang, Zig, Nim, Ruby, PHP, Julia, Perl, Raku, Common Lisp, Pascal and Delphi, Ada, Haxe, D, V, Lua, Racket, Odin, Clojure, Crystal, Elm, Tcl, Pharo, Vala, F#, PowerShell, R, Scheme, Mojo and Fortran.

**Research table**

| Library | Language | Repository | Docs read | Version and release date |
| --- | --- | --- | --- | --- |
| tuish | Bash | [alganet/tuish](https://github.com/alganet/tuish) | [README](https://github.com/alganet/tuish) | No release; last push 14 August 2026; ISC licence |
| boxlib | Bash | [iusmac/boxlib](https://github.com/iusmac/boxlib) | [README](https://github.com/iusmac/boxlib) | No release; last push 22 October 2025, more than a year before this run; MIT licence |
| termax.pony | Pony | [nogginly/termax.pony](https://github.com/nogginly/termax.pony) | [README](https://github.com/nogginly/termax.pony) | 0.2.2, 13 July 2022; the README says it is not ready for production; BSD-2-Clause licence |
| roc-tui | Roc | [lukewilliamboswell/roc-tui](https://github.com/lukewilliamboswell/roc-tui) | [README](https://github.com/lukewilliamboswell/roc-tui) | No release; archived by its owner on 30 March 2025; UPL-1.0 licence |
| OCCurses | Objective-C | [itaiferber/OCCurses](https://github.com/itaiferber/OCCurses) | [README](https://github.com/itaiferber/OCCurses) | No release; last push 9 October 2011 |
| vui.el | Emacs Lisp | [d12frosted/vui.el](https://github.com/d12frosted/vui.el) | [README](https://github.com/d12frosted/vui.el) | v1.4.0, 18 August 2026; last push 16 November 2026; GPL-3.0 licence |
| Emacs Widget Library | Emacs Lisp | Built into GNU Emacs; no separate repository | [Emacs manual](https://www.gnu.org/software/emacs/manual/html_mono/widget.html) | Not stated on the page; the manual ships with Emacs |
| tui.el | Emacs Lisp | [ebpa/tui.el](https://github.com/ebpa/tui.el) | [README](https://github.com/ebpa/tui.el) | No release; last push 28 October 2024; the README says it is experimental; GPL-3.0 licence |

**Features read, with sources**

- **tuish.** Keyboard and mouse events with named dispatch (for example a chord, a click, resize and idle); the VT and kitty keyboard protocols; key bindings declared by name; 256-colour and truecolour with text attributes; box drawing in four line styles with mixed junctions; display width for CJK, emoji and combining marks; fullscreen, fixed-region and grow (streaming output) modes; coalesced redraw; one app running inside a region of another; pure shell, needing only `stty`. Source: [README](https://github.com/alganet/tuish).
- **boxlib.** Dialog-style boxes: calendar, confirm, edit, form, info, input, list (checkbox, radio and tree variants), menu with an input menu, pause, program, progress (with a mixed variant), range, file and directory selector, text and time picker; timebox, calendar and tail boxes emulated on Whiptail; callbacks that pass an exit status up the chain; breadcrumb navigation, looping boxes and percentage sizing. It draws through Dialog or Whiptail and draws nothing itself. Requires Bash 4.3 (4.4 recommended). Source: [README](https://github.com/iusmac/boxlib).
- **termax.pony.** Mouse input; switching between the normal and alternate screen buffers; cursor hiding; capture of SIGINT and SIGTSTP so that Ctrl+C and Ctrl+Z reach the program as ordinary input; box-drawing line styles. It has no widgets, and its API may change. Source: [README](https://github.com/nogginly/termax.pony).
- **roc-tui.** Built on tui-rs 0.19.0. The README lists a block, paragraph and list widget, popup and modal support, styling, responsive layouts and mouse input under "things I'm working on or thinking about", and says the canvas, tabs, table, chart and sparkline widgets are blocked on a Roc issue. The owner archived it and recommends another project. Source: [README](https://github.com/lukewilliamboswell/roc-tui).
- **OCCurses.** A wrapper that starts and stops ncurses with the application; windows (`OCWindow`) with attributes, printing, reordering and scanning; no widgets, panels or colour in the README; built as a Cocoa framework in Release mode. Source: [README](https://github.com/itaiferber/OCCurses).
- **vui.el.** Components with props and local state; primitives for text, buttons, text fields, checkboxes and selects; collapsible sections; typed inputs with validation; semantic text styles; re-rendering when state changes; hooks for effects with cleanup, refs, intervals, memoised values and async loading; context and lifecycle hooks; horizontal and vertical stacks; a flex row with optional wrap; a responsive grid; fixed-width boxes; tables with optional sticky headers and borders; keyed dynamic lists; Tab and Shift-Tab between widgets and Return to activate; drawn with Emacs's own `button.el` and `widget.el`; needs Emacs 29.1 or later; the README calls the API stable. Source: [README](https://github.com/d12frosted/vui.el).
- **Emacs Widget Library.** Buttons and links (push-button, link and specialised links); single-line and multi-line text fields; menu choice and radio buttons; toggle, checkbox and checklist; an editable list in which each item is itself a widget; a group; collapsible documentation strings; widgets for Lisp values (symbols, strings, files, integers, floats, colours, keys and composites such as lists and alists); widgets drawn as text through format strings; layout by indent, offset and fixed size; `widget-forward` and `widget-backward`, which skip inactive widgets when `widget-skip-inactive` is set; tab order only partly implemented. Source: [manual](https://www.gnu.org/software/emacs/manual/html_mono/widget.html).
- **tui.el.** HTML-like elements (div, heading, section, span, link, icon, buffer, line, fixed-width and expander) written in a JSX-like syntax; the README calls it experimental and needs Emacs 26.1 or later; a grid layout is listed as future work. Source: [README](https://github.com/ebpa/tui.el).

**Considered and skipped**

- lister (Bash, GPL-3.0): last push 12 October 2023, and its README was last updated 25 August 2021. tuish and boxlib cover Bash. Source: [repository](https://github.com/angeltoast/lister).
- basil (Bash): a work in progress with few widgets, no release, last push 12 April 2025. Source: [repository](https://github.com/ekahPruthvi/basil).
- EasyBashGUI and the other Bash results (bash-tui by SAANN3, and sri-arjuna/tui): EasyBashGUI wraps yad, zenity, dialog and whiptail, and was not read; the bash-tui link went to a mirror on an unfamiliar host and was not followed; sri-arjuna/tui is described as a browser-style framework, not a widget library. These come from search snippets only, so their claims are **unverified** and they are not included.
- Janet: jermbox (archived by its owner, last push 10 December 2023, its README points to Sourcehut); journo (a prompt-and-answer library, not full-screen); joule (an editor application); jtermbox (termbox bindings, not read).
- Lean 4: lean-tui is an application (an infoview for the Lean prover, hosted on Codeberg) and Leansi formats output only.
- Groovy, Fennel, Hare, Prolog and Forth: no terminal widget library found. The Groovy hits are Groovy's own console tool; the Fennel and Hare hits are for other languages; the Prolog and Forth hits are generic curses pages.
- Pony, Roc and Objective-C: termax.pony, roc-tui and OCCurses are the only libraries found in their languages, so they are included although they are old or archived.
- Emacs Lisp: vui.el is the newer React-style library; tui.el is kept as a second source for that style and is older.

**Missing features**

**Controls**

144. **Editable list.** A list whose rows can be added, removed and edited in place, each row a control of its own, with an add row at the end. *Missing.* Seen in: Emacs Widget Library (`editable-list`, where each item is itself a widget). Search terms that found nothing in `iConsole/` and `iTerminal.Core/`: `EditableList`, `ListEditor`, `StringListEditor`, `EditList`, `RowEditor`, `AddItem`, `RemoveItem`; the one `RemoveItem` match is an override in `ConcurrentObservableCollection<T>` (`iConsole/Data/ConcurrentObservableCollection.cs:73`), which is not a control. Fits `ListBox<T>` with `TextBox` rows, or `FormPanel`. Some dialogs may build such lists by hand; that was not searched.

145. **Row identity in the virtual controls.** A cursor that stays on the same row, found by key, when the rows are replaced, re-sorted or filtered, and a lookup of a row by its key. *Partly.* `ListBox<T>` keeps the selection on the item (`ListBoxModel.SelectedItem` reads an `IsSelected` flag, `iConsole/Model/ListBoxModel.cs:60`), so the selection follows an item whose object is kept. `VirtualListBox<T>` and `VirtualTable<T>` keep the cursor as a row number (`iConsole/Controls/VirtualRowsControl.cs`, `_cursor`), clamped to the new count (line 311), so a refresh can move it to another row. Application code restores a position by hand in three places: `ContentModelBase.RestoreFocusedEntry` (by URL, `iTerminal.Core/Model/ContentModels/ContentModelBase.cs:523`), `VirtualMachineManagerDialog.RestoreCursor` (by name) and `DocumentManagerDialog.ApplyResults` (by path). Search terms with no match in `iConsole/`: `PreserveSelection`, `KeepSelection`, `RestoreSelection`, `SelectByKey`, `KeyOf`, `RestoreCursor`; `KeepSelectionVisible` in `MenuItem.cs` keeps a row in view and is not identity. Seen in: vui.el (key-based dynamic lists, and queries that find a keyed row). Fits `VirtualRowsControl<T>` and `IVirtualDataSource<T>`.

**Terminal protocol**

146. **Alternate screen buffer.** Switch to the terminal's alternate screen when the application starts (`CSI ? 1049 h`) and back when it ends, so the shell's own screen returns as it was. *Missing.* Seen in: termax.pony (switches between the normal and alternate screen buffers, per its README). Search terms with no match in `iConsole/`, `iConsole.Inputs.Posix`, `iConsole.Inputs.Windows` or `iTerminal.Core/`: `1049`, `?47h`, `?1047`, `alternate`, `altscreen`, `CreateConsoleScreenBuffer`, `SetConsoleActiveScreenBuffer`. The drivers clear the console instead: `SystemConsoleDriver.OnConsoleReleased` (`iConsole/Drivers/SystemConsoleDriver.cs:161`), `OnConsoleReacquired` (line 183) and `Clear()` (line 223) call `Console.Clear()`. The exit path was not read, so what the screen shows after exit is not known. Fits `SystemConsoleDriver`, `VirtualSequenceConsoleDriver` and the `Desktop` start and stop. A switch must also be undone when the terminal is lent with Ctrl+O (`ConsoleOwnership`), or the child shell would run on the alternate screen.

**Suggested order** (new items of this survey only)

1. Item 145 (row identity): removes the hand-written restore code in three dialogs and lets the virtual controls refresh without moving the cursor.
2. Item 144 (editable list): settings pages that list folders, rules or entries would use it instead of building rows by hand.
3. Item 146 (alternate screen): a terminal fidelity fix; read the exit path and the lend path before work starts.

**Candidates already covered** (item numbers, or built type names)

- **Built, no item:** `Breadcrumb`, `Chips`, `Tab`, `Pagination`, `CheckList`, `NumericUpDown`, `RadioGroup`, `ComboBox`, `Button`, `LinkLabel`, `TextBox` (`iConsole/Controls/TextBox.cs`), `TextEditorControl`, `Expander`, `Accordion`, `Calendar`, `TimePicker`, `Slider`, `FilePicker`, `TreeView`, `ProgressGroup`, `LogView` (follow, cap and streaming), `Wizard`, `GroupBox`, `FormPanel`, `GridPanel`, `StackPanel` (with `Wrap`, line 154), `BoxPanel`, `SizeRule.Percent`, `Popup`, `Window` (a `Control`, `iConsole/Controls/Window.cs:8-9`), `ColorScheme` (`iConsole/Display/ColorScheme.cs`), the `Desktop` lifecycle, `ConsoleInterruptShield` (Ctrl+C, Ctrl+\ and Ctrl+Z while the terminal is lent), `Cursor.IsCursorVisible`, `OnIdle` and `OnWindowSizeChanged`.
- **Wide characters** (tuish, item 1, built as `TextWidth`); **Kitty keyboard** (tuish, item 6, partly built); **box styles and joins** (tuish, items 11 and 37); **theme styles** (vui.el, items 10 and 98); **effects** (roc-tui, items 13 and 76); **typed inputs and validation** (vui.el, item 23); **reactive state** (vui.el, item 27, partly built as `ObservableValue`); **fixed-region and grow modes** (tuish, items 28 and 69; item 28 is still missing, 69 is built as `LogView`); **named bindings** (tuish, item 25); **key-hint help on focus** (Emacs, item 51; hints exist per control, the footer does not); **named focus rings** (Emacs and vui.el, item 44, still missing); **background work tied to a control** (vui.el, item 56, still missing; `RemoveControl` (`Control.cs:822`) has no hook, and `OnBeforeClose` runs only from `Close()`, line 831); **embedded pane** (tuish, item 64, a child process); **pause, message and wait boxes** (boxlib, item 94, still missing); **typed dialog results** (boxlib, item 119; prompts built, `ShowDialogAsync<T>` missing); **typed value editors** (Emacs, item 124, still missing); **clickable spans inside text** (Emacs `%[` buttons, item 131, still missing; no match for `ClickableSpan`, `InlineButton`, `ClickableText`, `SpanClick`, `TextAction`, `ClickRegion`, `LinkSpan`, `InlineLink`).
- **Not added:** a value shared down the control tree (vui.el's context), because services reach controls through constructors and the DI container; responsive breakpoints (roc-tui), because `OnWindowSizeChanged` is the hook and the layout is application code; a wrapped paragraph (roc-tui), because `Label` does not wrap but `TextViewerControl` does; an in-process nested application (tuish), because a `Window` is a `Control` and can sit in a panel, which is present by construction but not tested.

#### Survey 3: libraries from Gren, PureScript and Eiffel

This survey covers three libraries from three languages no earlier survey covers: gren-tui (Gren), purescript-blessed (PureScript) and simple_tui (Eiffel). It was run on 2026-11-20. Only three libraries were read, not the six to ten the brief asks for, because the other candidates in the languages searched were bindings, GUI toolkits, pages that could not be read, or had no terminal widget library (see "Considered and skipped"). The basis is the README or repository record of each project (GitHub API for tags, commits and the licence field, the registry page where one exists), read on the same day; it is not run-time behaviour. "Not found" means a grep of `iConsole/` and `iTerminal.Core/` (`*.cs`) on branch `docs/compare-console-survey-3` at commit `3ba7401f`. Phase 2 found no status change in the existing items it re-checked (see the last paragraph of this survey).

**Languages covered before this survey** (from Survey 1, Survey 2, the older comparison and its later surveys, not searched again): C#, Python, Rust, Go, C++, C, JavaScript and TypeScript, Java, Kotlin, Scala, Swift, Dart, Haskell, OCaml, Elixir, Gleam (BEAM), Erlang, Zig, Nim, Ruby, PHP, Julia, Perl, Raku, Common Lisp, Pascal and Delphi, Ada, Haxe, D, V, Lua, Racket, Odin, Clojure, Crystal, Elm, Tcl, Pharo, Vala, F#, PowerShell, R, Scheme, Mojo, Fortran, Bash, Pony, Roc, Objective-C and Emacs Lisp. Gren is treated as its own language, although it is an Elm dialect, because Elm is the only related entry and Gren is a separate language.

**Research table**

| Library | Language | Repository | Docs read | Version and release date |
| --- | --- | --- | --- | --- |
| gren-tui | Gren | [blaix/gren-tui](https://github.com/blaix/gren-tui) | [README](https://github.com/blaix/gren-tui), [Gren package page](https://packages.gren-lang.org/package/blaix/gren-tui/version/4.0.0/overview) | 5.0.0 (tag dated 8 January 2026; last push 8 January 2026); the registry page read shows 4.0.0; BSD-3-Clause |
| purescript-blessed | PureScript | [shamansir/purescript-blessed](https://github.com/shamansir/purescript-blessed) | [README](https://github.com/shamansir/purescript-blessed); the [Pursuit page](https://pursuit.purescript.org/packages/purescript-blessed) returned a bot check and was not read | No release; tag v1.0.0 dated 10 July 2025; last push 5 August 2025, more than a year before this run; MIT |
| simple_tui | Eiffel | [simple-eiffel/simple_tui](https://github.com/simple-eiffel/simple_tui) | [README](https://github.com/simple-eiffel/simple_tui#readme), with its CHANGELOG and source tree | No release or tag; last commit 8 November 2026; the README badge says MIT, the GitHub licence field is empty; the README calls it "Phase 2" (core widgets complete) |

**Features read, with sources**

- **gren-tui.** Terminal apps written in The Elm Architecture (model, update and view) in a pure style; a layout DSL inspired by elm-ui for reactive layouts; the module list on the registry page names attributes, borders, spinners with selectable variants and a text-field module; the README examples are a menu, a progress bar and a game. Source: [README](https://github.com/blaix/gren-tui), [Gren package page](https://packages.gren-lang.org/package/blaix/gren-tui/version/4.0.0/overview). Mouse support and colour are not described. Unverified (from a search summary, not the README): the view is printed and redrawn by clearing only the lines it printed before, and Ctrl+C is intercepted to exit. A search summary says it runs on the Node platform only (unverified).
- **purescript-blessed.** A draft port of the Node.js Blessed library, per its README: windows (boxes) that can be dragged, lists, a file manager, buttons and most of the original components, options, methods and styles; text tagging for colour and style; events are typed through a unique key per component instance. The README lists as not yet done the decoding of event subjects from JSON and "a lot of things". It is not on Spago; it is installed with npm and a custom Dhall entry. Source: [README](https://github.com/shamansir/purescript-blessed).
- **simple_tui.** Per the README and the source tree: bordered frames with titles; buttons with a press animation; a single-line text field with a cursor; check boxes, radio buttons, a list and a combo box with a popup; labels, a progress bar, separators and a modal message box with buttons; boxes, vertical and horizontal boxes, tabs and a radio group; a drop-down menu bar with Alt+key shortcuts and `&` markers on menu titles; global shortcuts with Ctrl, Alt and Shift; a per-frame tick callback; mouse click, drag and hover; a 256-colour palette with bold, reverse and underline; Unicode box drawing; a Windows and a POSIX backend (`platform/windows`, `platform/posix`). The API is described as EiffelVision2-inspired. Source: [README](https://github.com/simple-eiffel/simple_tui#readme), `platform/` in the source tree.

**Considered and skipped**

- marionette-commander (PureScript, MIT): last published 5 January 2023, more than three years before this run; it maps text output and keys onto a state, update and view loop, which item 27 already covers. Read on the [Pursuit page](https://pursuit.purescript.org/packages/purescript-marionette-commander/0.1.1). It is the only other PureScript option, so blessed is kept.
- ncurses-idris and the gallais fork (Idris 2): bindings to libncurses with no widgets; last push 3 January 2023. Source: [repository](https://github.com/gallais/ncurses-idris). Idris 2 is not covered before, so it is recorded here.
- Micro Focus COBOL (Panels and the Dialog System): the vendor documentation pages returned HTTP 403 to `WebFetch` on two attempts, so the features are known from a search snippet only and are not listed. COBOL is not covered before.
- libvt (FreeBASIC, a text screen library with an optional widget layer): its documentation site returned HTTP 404 on two URLs, and the repository was not found, so it is known from a search snippet only. FreeBASIC is not covered before.
- EiffelVision (Eiffel GUI toolkit) and the Gobo project: a GUI toolkit and a library set, not a terminal UI.
- ReScript bindings for OpenTUI and Ink (opentui-rescript, rescript-ink): bindings to JavaScript libraries, which are covered.
- Fennel, Carp, Hy, Hare, Chapel, Ballerina, Wren, Ring, Factor, Standard ML, Modula-2, Logo, Oberon, Smalltalk, GNU APL and Icon: searched once each, no terminal widget library found; the results were other languages or GUI toolkits.
- Extended search for new 2026 libraries: Symfony Tui (PHP), TamboUI (Java) and gotui (Go) are new, but their languages are covered, so none was added.

**Missing features**

**Controls**

147. **Hot-key marker in a caption.** *Partly.* An `&` before a letter makes that letter the hot key and underlines it, in `ContextMenuItem` only (`iConsole/Controls/ContextMenuItem.cs:20-22`, `HotKey`, `HotKeyIndex`); `&&` is a literal ampersand. `Button`, `CheckBox`, `TextBox`, `Tab`, `MenuBar` and `MenuItem` take a `HotKeyChar` set in code (`Control.cs:909`, `Button.cs:131`), and their titles are not parsed. Missing: the caption marker on those controls and on menu titles. Seen in: simple_tui (`&` markers on menu bar items and drop-down menus). Search terms in the title parse: `'&'` matches only `ContextMenuItem.cs` and the input-mask character in `InputMask.cs`; `HotKeyIndex` matches only `ContextMenuItem.cs`. A marker on a menu bar title must keep Alt+letter out of the global hot keys (CLAUDE.md); a caption hot key that acts only while its control has focus does not break that rule. Fits `Button`, `MenuItem` and `MenuBar`.

148. **Find a control by name.** *Missing.* `AddControl(control, name)` stores the name in `Control.Name` (`iConsole/Controls/Control.cs:292-297`), and `HelpDialog` reads it for its "Focused control" line, but no method returns a control by that name. Seen in: purescript-blessed (every component instance has a unique key, and its methods are called through that key). Search terms with no match in `iConsole/` or `iTerminal.Core/`: `FindControl`, `FindByName`, `GetControlByName`, `ControlByName`, `FindChild`, `FindDescendant`, `GetControl(`; the `.Name ==` matches are for drives, networks, content models (`ContentModelService.Left` and `Right`), file rows and XML, not for controls. Fits `Control` and `IControl`. A lookup walks the tree, so it must stay off the render path. Low priority.

**Suggested order** (new items of this survey only)

1. Item 147 (caption hot keys): small; menu and button titles could take the `&` marker that the context menu already reads.
2. Item 148 (control lookup): low; useful when a dialog or test needs a control it has not kept in a field.

**Candidates already covered** (item numbers, or built type names)

- Elm architecture with model, update and view (gren-tui): item 27.
- Layout DSL with attributes, reactive layout and nested boxes (gren-tui): items 9, 65, 66 and 128; built as `StackPanel`, `GridPanel`, `DockPanel`, `SizeRule`, `BoxPanel`.
- Spinners with selectable variants (gren-tui): item 71, still missing; `Spinner` has one frame set (`iConsole/Controls/Spinner.cs:9`).
- Inline output that redraws only its own lines (gren-tui, unverified): item 28, still missing; no `Inline`, `scrollback` or line-clearing term found in `iConsole/` or `iTerminal.Core/`.
- Text field with a cursor (gren-tui, simple_tui): built (`TextBox`).
- Ctrl+C that exits the program (gren-tui, unverified): built (the key router and `ConsoleInterruptShield`).
- Draggable boxes, lists, a file manager and buttons (purescript-blessed): built (`Window` drag under item 43, `ListBox`, `FilePicker`, `Button`).
- Inline colour and style tags (purescript-blessed): item 47, still missing; `MarkupTextExtractor` reads documents and gives no label markup.
- Per-component styles (purescript-blessed): items 10 and 98.
- Mouse click, drag and hover (simple_tui): built (SGR mouse, any-motion hover).
- 256-colour palette (simple_tui): item 34, still missing; `Color` has the 16 `ConsoleColor` values (`iConsole/Display/Color.cs`), and no `38;5` or 256-colour term was found.
- Modal message box with buttons and quick dialogs (simple_tui `TUI_MESSAGE_BOX`, `TUI_QUICK`): item 94; prompts are built, message and wait dialogs are still missing (no `MessageBox`, `ShowMessageAsync` or `WaitDialog`).
- Button press animation and timed effects (simple_tui): item 13, still missing; no `Animator`, `Tween` or `Easing` type was found.
- Per-frame tick callback (simple_tui `set_on_tick`): built (`OnIdle`).
- Global shortcuts with Ctrl, Alt and Shift (simple_tui `register_shortcut`): built (the command router).
- Tabs, radio group, combo box with a popup, progress bar, separator and scroll bar (simple_tui): built (`Tab`, `RadioGroup`, `ComboBox`, `ProgressGroup`, `Separator`, `ScrollBar`).
- Windows and POSIX terminal backends (simple_tui): built (`iConsole.Inputs.Windows`, `iConsole.Inputs.Posix`, `SystemConsoleDriver`).

**Status check**

Phase 2 re-checked items 13, 28, 34, 47, 71 and 94 by the searches above; each is still missing, and no item changed status, so no status line was added. Item 19 (hot keys in `ContextMenu`) still matches the source.
