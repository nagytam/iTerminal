# Console and terminal UI features: survey overview

This document records console and terminal UI libraries from languages the older comparison does not cover, checked against iConsole, and the features they have that iConsole lacks. The older comparison in `Documentation/architecture/ui-framework/tui-library-comparison.md` is a read-only record of earlier surveys. Its highest item is 131, so the items here start at 132. The highest item in this document is 156 (Survey 5).

## Overview

### Where iConsole is already level or ahead

- None of the eight libraries in Survey 1 describes a main-loop watchdog, lending the terminal to a child shell (Ctrl+O), or borders that join across separate boxes (`LineCanvas`, `BorderedGrid`). These are not in the pages read.
- Localization with a completeness test: HermesConsoleUI has internationalization. The other pages read do not describe one.
- Dialogs that move and resize with the keyboard (Ctrl+F7, Ctrl+F8) and the mouse: not described in the pages read.
- Paged data sources with read-ahead, cancellation and a stale-result rule (`PagedDataSource`): SageTUI and Mire have virtual lists and tables, but their pages do not describe paging.
- Survey 5 (vim-quickui, tui.mbt, Gambas): a popup text viewer with syntax colours, drop shadows, box borders with padding, flexbox-style stacks, Tab focus traversal, a rendering diff, a setup wizard and a command palette are built.
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
| Editable list (item 144) | Emacs Widget Library | Missing | `ListBox<T>` with `TextBox` rows, or `FormPanel` |
| Row identity in the virtual controls (item 145) | vui.el | Partly: `ListBox<T>` keeps the selection on the item; the virtual controls keep the cursor as a row number | `VirtualRowsControl<T>`, `IVirtualDataSource<T>` |
| Alternate screen buffer (item 146) | termax.pony | Missing; the drivers clear the console instead | `SystemConsoleDriver`, `VirtualSequenceConsoleDriver`, `Desktop` start and stop |
| Hot-key marker in a caption (item 147) | simple_tui | Partly: the `&` marker works in `ContextMenuItem` only | `Button`, `MenuItem`, `MenuBar` |
| Find a control by name (item 148) | purescript-blessed | Missing | `Control`, `IControl` |
| Colour picker with channel sliders and hex input (item 149) | QB64_GJ_LIB | Partly: `ColorPicker` is a grid of the sixteen console colours | `ColorPicker`, `Slider` |
| Colour spaces beyond RGB (item 150) | QB64_GJ_LIB | Missing; depends on items 3 and 34 | `Color`, `AnsiRenderer` |
| Circle, ellipse and triangle primitives (item 151) | Red View text UI backend | Missing | `ChartCanvas`, `LineCanvas` |
| Thumbnail grid for file lists (item 152) | QB64_GJ_LIB | Missing | `FilePicker`, `ListBox<T>`, `VirtualListBox<T>` |
| ANSI art file viewer (item 153) | ANSI-Print-64 | Missing | `TextViewerControl`, the F3 viewer |
| Named areas in the grid layout (item 154) | tui.mbt | Missing | `GridPanel`, `BorderedGrid` |
| Reusable location list, quickfix style (item 155) | vim-quickui | Partly: the search drive and its status bar only | `VirtualListBox<T>`, `VirtualTable<T>`, `TextViewerControl.ShowLine` |
| User object on each control (item 156) | Gambas gb.term.form | Missing | `Control` |

### Suggested order

Ranked for iTerminal, for items 132 to 143 (Survey 1).

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

Items 144 to 153 were ranked inside their own surveys, so the blocks below are not ranked against each other.

**Survey 2 (items 144 to 146)**

1. Item 145 (row identity): removes the hand-written restore code in three dialogs and lets the virtual controls refresh without moving the cursor.
2. Item 144 (editable list): settings pages that list folders, rules or entries would use it instead of building rows by hand.
3. Item 146 (alternate screen): a terminal fidelity fix; read the exit path and the lend path before work starts.

**Survey 3 (items 147 and 148)**

1. Item 147 (caption hot keys): small; menu and button titles could take the `&` marker that the context menu already reads.
2. Item 148 (control lookup): low; useful when a dialog or test needs a control it has not kept in a field.

**Survey 4 (items 149 to 153)**

1. Item 152 (thumbnail grid): a file manager shows image folders as rows only; the image preview exists, the grid does not.
2. Item 149 (colour picker with sliders): the theme editing would use it; it waits for true-colour support (items 3 and 34).
3. Item 153 (ANSI art viewer): a niche file type, but the F3 viewer already opens text, so the work is a parser and a palette.
4. Item 151 (circle, ellipse and triangle primitives): useful for charts, low for a file manager.
5. Item 150 (colour spaces): low; it follows item 149 and the true-colour work.

## Items

### Controls

#### 132. Reorderable list

- Status: Missing. `ConcurrentObservableCollection<T>` has a `MoveItem` override, but it moves items in the collection only; no control moves rows.
- What it is: move a row up or down with keys or the mouse, and keep the new order in the list's model.
- Sources: [SageTUI](https://github.com/WillEhrendreich/SageTUI) (`OrderableList`, `OrderableVirtualList`), [fortran-dialog](https://github.com/interkosmos/fortran-dialog) (`buildlist`).
- Search terms with no match in a control: `Orderable`, `Reorderable`, `ReorderItem`, `MoveUp(`, `MoveDown(` (also nothing in `iTerminal.Core`).
- Fits: `ListBox<T>`, `VirtualListBox<T>`.
- Size: not estimated in the surveys.

#### 133. Reusable diff view

- Status: Partly. `iTerminal.Core` has `TextDiff` (`Services/Compare/TextDiff.cs`), `FileDiffDialog`, `CompareFoldersDialog` (with its `DiffList` row control) and `BinaryDiffView` (a hex diff). None is in `iConsole`.
- What it is: show two texts side by side or as one list, with changed rows marked, as a control in the library.
- Sources: [SageTUI](https://github.com/WillEhrendreich/SageTUI) (`DiffView`).
- Search terms over the whole repository: `DiffView`, `UnifiedDiff`, `LineDiff`, `DiffHunk` (no match in `iConsole`; `Diff` matches only unrelated words and the `iTerminal.Core` classes above).
- Fits: `TextViewerControl`, `Table<T>`.
- Size: not estimated in the surveys.

#### 134. Node-and-link diagram

- Status: Missing.
- What it is: boxes joined by lines, grouped, drawn in cells.
- Sources: [ConsoleMonster](https://github.com/EvotecIT/ConsoleMonster) (diagrams with nodes, links and groups).
- Search terms in both trees: `Diagram`, `DrawNode`, `GraphNode`, `class *Graph*`, `class *Flow*`, `class *Node*` (only the tree data classes `XmlNodeItem` and `JsonNodeItem`).
- Fits: `LineCanvas`, `BorderedGrid`, `Shadow`.
- Size: not estimated in the surveys.

#### 135. Grouped table rows

- Status: Missing.
- What it is: a `Table<T>` or `VirtualTable<T>` whose rows sit under group headings that can be folded.
- Sources: [ConsoleMonster](https://github.com/EvotecIT/ConsoleMonster) (tables with grouping).
- Search terms in `iConsole/Controls/`: `GroupBy`, `GroupHeader`, `RowGroup`, `Grouping` (no match). `Expander` and `Accordion` fold one control, not the rows of a table.
- Fits: `Table<T>`, `VirtualTable<T>`.
- Size: not estimated in the surveys.

#### 136. Find in a log view

- Status: Partly. `LogView.FilterText` and `MinimumLevel` hide lines, but nothing moves to a match.
- What it is: next and previous match in `LogView`, separate from the line filter.
- Sources: [ConsoleMonster](https://github.com/EvotecIT/ConsoleMonster) (log viewports with find).
- Search terms in `LogView.cs`: `Find`, `FindNext`, `SearchText`, `FindText` (only `LogHighlighters.Find`).
- Fits: `SearchEngine`, which the viewer already uses for regular-expression search (item 107).
- Size: not estimated in the surveys.

#### 137. Export of table or list data

- Status: Partly. `ScreenRecorder` saves the screen as text, HTML and SVG, and `BufferHelper.DumpBufferToHtml` is a debugging aid; item 77 covers the screen export.
- What it is: write the rows of a table, list, search result or log to a file as CSV, Markdown or JSON.
- Sources: [ConsoleMonster](https://github.com/EvotecIT/ConsoleMonster) (views to Markdown, HTML, SVG and text), [termr](https://github.com/menshikovs/termr) (Markdown and JSON snapshots).
- Search terms over the whole repository, including tests: `ToCsv`, `ExportCsv`, `WriteCsv`, `ToMarkdown`, `ExportMarkdown`, `ToJson`, `ExportJson` (no match).
- Fits: `Table<T>`, `ListBox<T>`, `ScreenRecorder`.
- Size: not estimated in the surveys.

#### 138. Chart zoom, pan and crosshair

- Status: Missing.
- What it is: zoom or pan the value range of a chart, and show the point under the pointer.
- Sources: [ConsoleMonster](https://github.com/EvotecIT/ConsoleMonster) (interactive charts with crosshair, zoom and pan).
- Notes: the statistical charts section of `ui-framework.md` also lists zoom and hover values as not built.
- Search terms in `iConsole/`: `Crosshair`, `Zoom`, `Pan` (no match in the charts). In `iTerminal.Core`, `Zoom` is the panel zoom and the map.
- Fits: `ScatterChart`, `ChartCanvas`.
- Size: not estimated in the surveys.

#### 139. Metric tile

- Status: Missing.
- What it is: a large value with a label, a unit and a change indicator, in a box.
- Sources: [termr](https://github.com/menshikovs/termr) (metrics).
- Search terms in both trees: `Metric`, `StatCard`, `MetricTile`, `MetricCard` (no match). `Digits`, `Gauge` and `KeyValueList` cover parts of it.
- Fits: `BoxPanel`, `Digits`, `Sparkline`.
- Size: not estimated in the surveys.

#### 140. Data profile of a table

- Status: Missing.
- What it is: for each column, the type, the count of empty values, the count of distinct values, the minimum and the maximum.
- Sources: [termr](https://github.com/menshikovs/termr) (data profiles).
- Search terms in both trees: `DataProfile`, `ColumnProfile` (no match).
- Fits: `Table<T>` and the CSV viewer in `iTerminal.Core`.
- Size or priority: low priority.

#### 141. Undo and redo in a single-line field

- Status: Partly. `TextEditorControl` has undo (its file and `TextEditorControl.LineTools.cs` mention it 15 times); `TextBox` has none, and the one mention in `TextBox.cs` is a comment (lines 609 and 617 in the Survey 4 re-check).
- What it is: undo and redo in a single-line field.
- Sources: [SageTUI](https://github.com/WillEhrendreich/SageTUI) (undo and redo listed as a program add-on; whether it applies to fields is not stated). Also seen in [QB64_GJ_LIB](https://github.com/grymmjack/QB64_GJ_LIB) (`TEXT_INPUT`, with undo and redo).
- Search terms in `iConsole/Controls/TextBox.cs`: `undo`, `redo`, `UndoStack`.
- Fits: `TextBox`.
- Size: not estimated in the surveys.

#### 142. Crash banner and restore after an unhandled exception

- Status: Missing as a banner. Restoring the terminal after an unhandled exception is not confirmed: the normal exit path in the drivers was not read, so check it first.
- What it is: after a crash, put the terminal back and show a banner on the next start that offers the log.
- Sources: [SageTUI](https://github.com/WillEhrendreich/SageTUI) (a crash-recovery banner and terminal restoration after unhandled exceptions).
- Search terms in both trees: `UnhandledException`, `CrashRecovery`, `crash`, `RestoreTerminal` (no match).
- Fits: `Desktop` shutdown, `ConsoleOwnership` and the composition root.
- Size: not estimated in the surveys.

#### 144. Editable list

- Status: Missing.
- What it is: a list whose rows can be added, removed and edited in place, each row a control of its own, with an add row at the end.
- Sources: [Emacs Widget Library](https://www.gnu.org/software/emacs/manual/html_mono/widget.html) (`editable-list`, where each item is itself a widget).
- Search terms that found nothing in `iConsole/` and `iTerminal.Core/`: `EditableList`, `ListEditor`, `StringListEditor`, `EditList`, `RowEditor`, `AddItem`, `RemoveItem`. The one `RemoveItem` match is an override in `ConcurrentObservableCollection<T>` (`iConsole/Data/ConcurrentObservableCollection.cs:73`), which is not a control.
- Fits: `ListBox<T>` with `TextBox` rows, or `FormPanel`.
- Notes: some dialogs may build such lists by hand; that was not searched (**unverified**).
- Size: not estimated in the surveys.

#### 145. Row identity in the virtual controls

- Status: Partly. `ListBox<T>` keeps the selection on the item (`ListBoxModel.SelectedItem` reads an `IsSelected` flag, `iConsole/Model/ListBoxModel.cs:60`), so the selection follows an item whose object is kept. `VirtualListBox<T>` and `VirtualTable<T>` keep the cursor as a row number (`iConsole/Controls/VirtualRowsControl.cs`, `_cursor`), clamped to the new count (line 311), so a refresh can move it to another row.
- What it is: a cursor that stays on the same row, found by key, when the rows are replaced, re-sorted or filtered, and a lookup of a row by its key.
- Application code restores a position by hand in three places: `ContentModelBase.RestoreFocusedEntry` (by URL, `iTerminal.Core/Model/ContentModels/ContentModelBase.cs:523`), `VirtualMachineManagerDialog.RestoreCursor` (by name) and `DocumentManagerDialog.ApplyResults` (by path).
- Sources: [vui.el](https://github.com/d12frosted/vui.el) (key-based dynamic lists, and queries that find a keyed row).
- Search terms with no match in `iConsole/`: `PreserveSelection`, `KeepSelection`, `RestoreSelection`, `SelectByKey`, `KeyOf`, `RestoreCursor`. `KeepSelectionVisible` in `MenuItem.cs` keeps a row in view and is not identity.
- Fits: `VirtualRowsControl<T>`, `IVirtualDataSource<T>`.
- Size: not estimated in the surveys.

#### 147. Hot-key marker in a caption

- Status: Partly. An `&` before a letter makes that letter the hot key and underlines it, in `ContextMenuItem` only (`iConsole/Controls/ContextMenuItem.cs:20-22`, `HotKey`, `HotKeyIndex`); `&&` is a literal ampersand. `Button`, `CheckBox`, `TextBox`, `Tab`, `MenuBar` and `MenuItem` take a `HotKeyChar` set in code (`Control.cs:909`, `Button.cs:131`), and their titles are not parsed.
- What it is: the caption marker on those controls and on menu titles.
- Sources: [simple_tui](https://github.com/simple-eiffel/simple_tui) (`&` markers on menu bar items and drop-down menus).
- Search terms in the title parse: `'&'` matches only `ContextMenuItem.cs` and the input-mask character in `InputMask.cs`; `HotKeyIndex` matches only `ContextMenuItem.cs`.
- Notes: a marker on a menu bar title must keep Alt+letter out of the global hot keys (CLAUDE.md); a caption hot key that acts only while its control has focus does not break that rule. Item 19 (hot keys in `ContextMenu`) still matches the source (Survey 3 status check).
- Fits: `Button`, `MenuItem`, `MenuBar`.
- Size or priority: small.

#### 148. Find a control by name

- Status: Missing. `AddControl(control, name)` stores the name in `Control.Name` (`iConsole/Controls/Control.cs:292-297`), and `HelpDialog` reads it for its "Focused control" line, but no method returns a control by that name.
- What it is: look up a control by its name.
- Sources: [purescript-blessed](https://github.com/shamansir/purescript-blessed) (every component instance has a unique key, and its methods are called through that key).
- Search terms with no match in `iConsole/` or `iTerminal.Core/`: `FindControl`, `FindByName`, `GetControlByName`, `ControlByName`, `FindChild`, `FindDescendant`, `GetControl(`. The `.Name ==` matches are for drives, networks, content models (`ContentModelService.Left` and `Right`), file rows and XML, not for controls.
- Fits: `Control`, `IControl`. A lookup walks the tree, so it must stay off the render path.
- Size or priority: low priority.

#### 149. Colour picker with channel sliders and hex input

- Status: Partly. `ColorPicker` (`iConsole/Controls/ColorPicker.cs`) is a grid of the sixteen console colours and nothing else; a continuous picker needs a true-colour value first (items 3 and 34).
- What it is: a picker that sets one colour from red, green and blue, or from hue, saturation and value, sliders, with a hex field and a history of recent colours.
- Sources: [QB64_GJ_LIB](https://github.com/grymmjack/QB64_GJ_LIB) (`COLOR_PICKER`, and `ADV_COLOR_PICKER` with a hue ring and history).
- Search terms in `iConsole/`: `HueRing`, `HueWheel`, `SwatchPicker` (no match); `ColorPicker` matches only the sixteen-swatch control.
- Fits: `ColorPicker`, `Slider`.
- Size or priority: not estimated in the surveys; the theme editing would use it.

#### 150. Colour spaces beyond RGB

- Status: Missing. It depends on items 3 and 34, because no true-colour type exists yet.
- What it is: conversion between RGB, HSV, HSL and the perceptual spaces CIELAB and OKLab, with gamut mapping into a palette.
- Sources: [QB64_GJ_LIB](https://github.com/grymmjack/QB64_GJ_LIB) (`COLOR`: OKLab, OKLCh, CIELAB, XYZ and gamut mapping; `COLOR_PICKER`: HSV, RGB and HSL).
- Search terms in `iConsole/` and `iTerminal.Core/`: `HsvColor`, `HslColor`, `ToHsv`, `ToHsl`, `FromHsv`, `OkLab`, `CIELAB`, `XyzColor`, `Rgb` and `RgbColor` (no match; the `Rgba8888` hits are image bitmaps in `SKBitmap`, not a colour type).
- Fits: `Color` (`iConsole/Display/Color.cs`) and `AnsiRenderer`.
- Size or priority: low priority.

#### 151. Circle, ellipse and triangle primitives

- Status: Missing.
- What it is: outlines or filled shapes of circles, ellipses and triangles drawn on the character canvas.
- Sources: [Red View text UI backend](https://red-lang.org/2024/06/text-ui-view-backend.html) (drawing of triangle, circle and ellipse, block-based).
- Search terms in `iConsole/` and `iTerminal.Core/`: `DrawCircle`, `DrawEllipse`, `DrawTriangle`, `MidpointCircle`, `FillCircle`, `Ellipse`, `Circle` (the only match is a comment in `PieChart.cs`, which sizes a pie). `ChartCanvas` (`iConsole/Controls/Charts/ChartCanvas.cs`) has `Set`, `Text` and `Dot` only, and `LineCanvas` (`iConsole/Display/LineCanvas.cs`) draws box lines.
- Fits: `ChartCanvas` (item 36) and `LineCanvas`.
- Size or priority: low priority.

#### 152. Thumbnail grid for file lists

- Status: Missing.
- What it is: the files of a folder shown as a grid of image thumbnails, next to the list and detail views.
- Sources: [QB64_GJ_LIB](https://github.com/grymmjack/QB64_GJ_LIB) (`FILE_DIALOG`, with list, detail and thumbnail views).
- Search terms in `iConsole/` and `iTerminal.Core/`: `Thumbnail` (the only match is `iTerminal.Core/Video/RawPreviewVideoDecoder.cs`), `IconView`, `GridView`, `ThumbView`, `ThumbnailView`, `ImageGrid`, `TileView` (no match). The single-file image preview exists (item 7) and is not a grid.
- Fits: `FilePicker`, `ListBox<T>` and `VirtualListBox<T>`, with the image preview of item 7.
- Size or priority: not estimated in the surveys.

### Terminal protocol

#### 146. Alternate screen buffer

- Status: Missing. The drivers clear the console instead: `SystemConsoleDriver.OnConsoleReleased` (`iConsole/Drivers/SystemConsoleDriver.cs:161`), `OnConsoleReacquired` (line 183) and `Clear()` (line 223) call `Console.Clear()`. The exit path was not read, so what the screen shows after exit is not known.
- What it is: switch to the terminal's alternate screen when the application starts (`CSI ? 1049 h`) and back when it ends, so the shell's own screen returns as it was.
- Sources: [termax.pony](https://github.com/nogginly/termax.pony) (switches between the normal and alternate screen buffers, per its README).
- Search terms with no match in `iConsole/`, `iConsole.Inputs.Posix`, `iConsole.Inputs.Windows` or `iTerminal.Core/`: `1049`, `?47h`, `?1047`, `alternate`, `altscreen`, `CreateConsoleScreenBuffer`, `SetConsoleActiveScreenBuffer`.
- Fits: `SystemConsoleDriver`, `VirtualSequenceConsoleDriver` and the `Desktop` start and stop. A switch must also be undone when the terminal is lent with Ctrl+O (`ConsoleOwnership`), or the child shell would run on the alternate screen.
- Size or priority: a terminal fidelity fix; read the exit path and the lend path before work starts.

### Text

#### 143. Indent and dedent helpers

- Status: Partly. `StringHelper` has `WrapWords`, `Cut` and `Expand`, and `HelpDialog.WrapToWidth` is a second word wrap. Indent and dedent are missing.
- What it is: indent and dedent helpers for text.
- Sources: [mist](https://github.com/thatstoasty/mist) (indent, dedent, wrap and truncate).
- Search terms in `iConsole/Helper/`: `Indent(`, `Dedent(` (no match).
- Fits: `StringHelper`; the copy in `HelpDialog` would move there.
- Size or priority: low priority.

#### 153. ANSI art file viewer

- Status: Missing.
- What it is: open `.ans`, `.asc`, `.diz` and `.nfo` files with their code-page glyphs, read the SAUCE record at the end of the file (the metadata block used for ANSI art), and play ANSI animation.
- Sources: [ANSI-Print-64](https://github.com/a740g/ANSI-Print-64) (loads ANS, ASC, DIZ, NFO and TXT files and plays ANSImation; its README does not name SAUCE).
- Search terms in `iConsole/` and `iTerminal.Core/`: `SAUCE`, `AnsiArt`, `AnsiViewer`, `AnsiFile`, `AnsiDocument`, `ANSImation`, `PabloDraw`, `CP437` and `\.ans"`, `\.diz"`, `\.nfo"` (no match; the `.asc` hits are PGP signature files in `SignatureService.cs`). `AnsiRenderer` (`iConsole/Drivers/AnsiRenderer.cs`) draws shell output and `AnsiText` (`iConsole/Helper/LogHighlighting.cs`) colours logs; neither reads a file.
- Fits: `TextViewerControl` and the F3 viewer.
- Size or priority: low to medium priority.

## Already covered

Items and built types that the surveys found already in iConsole or `iTerminal.Core`. Item numbers are from the older comparison unless marked otherwise.

**Survey 1**

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
- Password input (SageTUI): item 130, built (see the re-checks below).
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

**Survey 2**

- Built, no item: `Breadcrumb`, `Chips`, `Tab`, `Pagination`, `CheckList`, `NumericUpDown`, `RadioGroup`, `ComboBox`, `Button`, `LinkLabel`, `TextBox` (`iConsole/Controls/TextBox.cs`), `TextEditorControl`, `Expander`, `Accordion`, `Calendar`, `TimePicker`, `Slider`, `FilePicker`, `TreeView`, `ProgressGroup`, `LogView` (follow, cap and streaming), `Wizard`, `GroupBox`, `FormPanel`, `GridPanel`, `StackPanel` (with `Wrap`, line 154), `BoxPanel`, `SizeRule.Percent`, `Popup`, `Window` (a `Control`, `iConsole/Controls/Window.cs:8-9`), `ColorScheme` (`iConsole/Display/ColorScheme.cs`), the `Desktop` lifecycle, `ConsoleInterruptShield` (Ctrl+C, Ctrl+\ and Ctrl+Z while the terminal is lent), `Cursor.IsCursorVisible`, `OnIdle` and `OnWindowSizeChanged`.
- Wide characters (tuish, item 1, built as `TextWidth`); Kitty keyboard (tuish, item 6, partly built); box styles and joins (tuish, items 11 and 37); theme styles (vui.el, items 10 and 98); effects (roc-tui, items 13 and 76); typed inputs and validation (vui.el, item 23); reactive state (vui.el, item 27, partly built as `ObservableValue`); fixed-region and grow modes (tuish, items 28 and 69; item 28 is still missing, 69 is built as `LogView`); named bindings (tuish, item 25); key-hint help on focus (Emacs, item 51; hints exist per control, the footer does not); named focus rings (Emacs and vui.el, item 44, still missing); background work tied to a control (vui.el, item 56, still missing; `RemoveControl` (`Control.cs:822`) has no hook, and `OnBeforeClose` runs only from `Close()`, line 831); embedded pane (tuish, item 64, a child process); pause, message and wait boxes (boxlib, item 94, still missing); typed dialog results (boxlib, item 119; prompts built, `ShowDialogAsync<T>` missing); typed value editors (Emacs, item 124, still missing); clickable spans inside text (Emacs `%[` buttons, item 131, still missing; no match for `ClickableSpan`, `InlineButton`, `ClickableText`, `SpanClick`, `TextAction`, `ClickRegion`, `LinkSpan`, `InlineLink`).
- Not added: a value shared down the control tree (vui.el's context), because services reach controls through constructors and the DI container; responsive breakpoints (roc-tui), because `OnWindowSizeChanged` is the hook and the layout is application code; a wrapped paragraph (roc-tui), because `Label` does not wrap but `TextViewerControl` does; an in-process nested application (tuish), because a `Window` is a `Control` and can sit in a panel, which is present by construction but not tested.

**Survey 3**

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

**Survey 4**

- Message box with common buttons and icons (QB64_GJ_LIB `MSG_BOX`): item 94, still missing.
- Pipe codes and inline colour tags (QB64_GJ_LIB `PIPEPRINT`, Red rich text): item 47, still missing.
- Text field with undo and redo (QB64_GJ_LIB `TEXT_INPUT`): item 141, still missing (see item 141 above).
- Text field with selection, clipboard and multiline editing (QB64_GJ_LIB `TEXT_INPUT`): built (`TextBox` selection and `Clipboard`; `TextEditorControl` for multiline).
- Lightbar menus and text boxes (QB64_GJ_LIB `INPUT`), text list (Red): built (`ListBox`, `TextBox`).
- Bounding box with resize under keyboard and mouse (QB64_GJ_LIB `BBX`): built (Ctrl+F7 and Ctrl+F8; window drag under item 43).
- Flexbox-style layout solver with floating windows (QB64_GJ_LIB `LAYOUT`): items 9, 65 and 66; built as `StackPanel`, `GridPanel`, `DockPanel` and `SizeRule`.
- Image widget and image drawing (Red): item 7 (Kitty and Sixel).
- Progress bar, group box with frame styles, password field (Red): built (`ProgressGroup`, `GroupBox`, `BoxPanel`; item 130 for the password field).
- File and folder requesters (Red, QB64_GJ_LIB `FILE_DIALOG` list and detail views): built (`FilePicker`; the panel listings).
- Timers (Red): built (`IJob` on the `Desktop` lifecycle).
- Debugging console object (QB64_GJ_LIB `CONSOLE`): items 31 and 79.
- ANSI.SYS and escape-sequence output (ANSI-Print-64): built for the terminal (`AnsiRenderer`); a file viewer is item 153.
- Pie chart: built (`PieChart`).

**Re-checks of older-comparison items (no new items)**

- Item 130 (secret text entry, listed as not found in the seventh survey of the older comparison) is **built**: `iConsole/Controls/PasswordBox.cs` is a `TextBox` that draws each character as a dot, offers no autocompletion and does not allow copying out (Survey 1 status, 2026-11-20).
- Still missing, by a grep of the terms named in the older comparison (Survey 1): item 2 (`2004h`, `bracketed paste`, `PasteEvent`), item 3 and 34 (`NO_COLOR`, `COLORTERM`, `TrueColor`, `24-bit`), item 26 and 78 (`HighContrast`, `ReducedMotion`), item 61 (`StringInfo`, `grapheme`, `VariationSelector`), item 31 and 79 (`DebugOverlay`, `Inspector`, `ShowBounds`), item 89 (`IndentGuide`, `RulerColumn`), item 97 (`MiddleEllipsis`, `PathEllipsis`), item 109 (`ClickCount`, `IsDoubleClick`), item 122 (`CaptureMouse`, `MouseCapture`), item 125 (`EDITOR` and `VISUAL` as launch variables: only test data), item 128 (`class Columns`, `class Rows`, `class Padder`, `Toggle`), item 131 (`Zone`, `HitRegion`, `HitZone`), and item 94 for message and wait dialogs (`MessageDialog`, `MessageBox`, `ShowMessageAsync`, `WaitDialog`).
- Item 80: `IInputInjector` (`iConsole/Inputs/IInputInjector.cs`) has `SendKeyAsync` and `SendMouseAsync` only; paste and resize cannot be injected yet.
- Named types of the items marked built in the older comparison were found (for example `FilePicker`, `SyntaxHighlightingService`, `ContextMenu`, `ProgressGroup`, `LiveTable`, `PieChart`, `MarkdownViewerControl`, `MainLoopWatchDog`, `KittyKeyboardProtocol`). No built status was found to be wrong.
- Survey 2 re-check of items 44, 56, 94, 119, 124, 128, 131, 132, 136, 141 and 142, and of items 3 and 34 (no `38;2`, `TrueColor`, `24-bit` or `COLORTERM` terms in `iConsole/`): no status change; item 142's exit path is still unread.
- Survey 3 re-check of items 13, 28, 34, 47, 71 and 94: each still missing; item 19 still matches the source.
- Survey 4 re-check of items 3 and 34 (no `38;2`, `38;5`, `?1049` or RGB colour type in `iConsole/`), item 94 (no `MessageBox`, `ShowMessageAsync`, `MessageDialog` or `WaitDialog`) and item 141 (`undo` and `redo` in `TextBox.cs` only in comments): no status change.

## Considered and skipped

- PoshTui (PowerShell): alpha, last published 23 July 2025, more than a year before the survey; PowerShell 7 or later; GPL-3.0. Source: [PowerShell Gallery](https://www.powershellgallery.com/packages/PoshTui/1.0.0-alpha).
- Microsoft.PowerShell.ConsoleGuiTools and PSTui (PowerShell): grid pickers built on Terminal.Gui, which the first survey covers. Not read.
- rtui (R): wrappers over Textual (Python) and FTXUI (C++), both covered already. The two project pages returned 404 and were not read.
- M_ncurses and M_fixedform (Fortran): ncurses bindings without widgets. M_fixedform aims at forms; known from a search snippet only, not read.
- fortty (Fortran): a colour helper rather than a widget library. Not read.
- mog (Mojo): style definitions only, from a search snippet. Not read.
- Thuja (F#): a small Elm-style library, from a search snippet. Not read.
- Chicken Scheme: no curses or TUI library found in search.
- pip-tui (Guile): pre-alpha and not usable.
- Mojo: no widget framework found; mist is the only toolkit included.
- lister (Bash, GPL-3.0): last push 12 October 2023, and its README was last updated 25 August 2021. Source: [repository](https://github.com/angeltoast/lister).
- basil (Bash): a work in progress with few widgets, no release, last push 12 April 2025. Source: [repository](https://github.com/ekahPruthvi/basil).
- EasyBashGUI and the other Bash results (bash-tui by SAANN3, and sri-arjuna/tui): EasyBashGUI wraps yad, zenity, dialog and whiptail, and was not read; the bash-tui link went to a mirror on an unfamiliar host and was not followed; sri-arjuna/tui is described as a browser-style framework, not a widget library. These come from search snippets only, so their claims are **unverified** and they are not included.
- Janet: jermbox (archived by its owner, last push 10 December 2023, its README points to Sourcehut); journo (a prompt-and-answer library, not full-screen); joule (an editor application); jtermbox (termbox bindings, not read).
- Lean 4: lean-tui is an application (an infoview for the Lean prover, hosted on Codeberg) and Leansi formats output only.
- Groovy, Fennel, Hare, Prolog and Forth: no terminal widget library found. The Groovy hits are Groovy's own console tool; the Fennel and Hare hits are for other languages; the Prolog and Forth hits are generic curses pages.
- Pony, Roc and Objective-C: termax.pony, roc-tui and OCCurses are the only libraries found in their languages, so they are included although they are old or archived.
- Emacs Lisp: vui.el is the newer React-style library; tui.el is kept as a second source for that style and is older.
- marionette-commander (PureScript, MIT): last published 5 January 2023, more than three years before the survey; it maps text output and keys onto a state, update and view loop, which item 27 already covers. Read on the [Pursuit page](https://pursuit.purescript.org/packages/purescript-marionette-commander/0.1.1). It is the only other PureScript option, so blessed is kept.
- ncurses-idris and the gallais fork (Idris 2): bindings to libncurses with no widgets; last push 3 January 2023. Source: [repository](https://github.com/gallais/ncurses-idris). Idris 2 is not covered before, so it is recorded here.
- Micro Focus COBOL (Panels and the Dialog System): the vendor documentation pages returned HTTP 403 to `WebFetch` on two attempts, so the features are known from a search snippet only and are not listed. COBOL is not covered before.
- libvt (FreeBASIC, a text screen library with an optional widget layer): its documentation pages returned HTTP 404 (`rbreitinger.github.io/libvt`) on both fetches, and no GitHub repository under that name was found (404). Known from a search snippet only, so its features are not listed. FreeBASIC is not covered before.
- EiffelVision (Eiffel GUI toolkit) and the Gobo project: a GUI toolkit and a library set, not a terminal UI.
- ReScript bindings for OpenTUI and Ink (opentui-rescript, rescript-ink): bindings to JavaScript libraries, which are covered.
- Fennel, Carp, Hy, Hare, Chapel, Ballerina, Wren, Ring, Factor, Standard ML, Modula-2, Logo, Oberon, Smalltalk, GNU APL and Icon: searched once each, no terminal widget library found; the results were other languages or GUI toolkits.
- Symfony Tui (PHP), TamboUI (Java) and gotui (Go): new in 2026, but their languages are covered, so none was added.
- Visual Basic .NET: no VB-specific terminal library found. The .NET results (Terminal.Gui, gui.cs, Console.Lib, SharpTerm.Core, TUIKit) are C# libraries and C# is covered. Source: [search](https://www.nuget.org/packages/SharpTerm.Core), not read further.
- Rexx/Curses (Rexx): a binding to curses with no widgets; the project page says version 2.0 and was last updated on 3 February 2015. Source: [project page](https://rexxcurses.sourceforge.net/).
- Euphoria "Console widgets" (Jean-Marc Duro, 2015): the archive entry describes a graphical toolkit, not a text-mode library. Source: [rapideuphoria.com](https://www.rapideuphoria.com/use.htm).
- Mercury: two curses bindings from 2000 and 2001 on the mailing list; no current library. Pike: ncurses and widget modules discussed in 2004-era posts; no maintained module found.
- GnuCOBOL (COBOL): its Screen Section uses ncurses or PDCurses, with no third-party widget library; cobcurses targets OpenCOBOL and has no Screen Section support. The Micro Focus pages were not read.
- Seed7: a console type only, no widget library (search only). Standard ML, Dylan, Factor, AWK, Clean, Hylo, Modula-3, Oberon, Unison, Lux, Nemerle, Ring, Dyalog APL, PowerBASIC and Io: searched once each, no terminal widget library found. C3: the one candidate, tessela, is a C library, and C3 use was not verified.
- ocurses (C++, LGPL-2.0) and the other .NET and Go candidates: their languages are covered. Source: [ocurses](https://ocurses.sourceforge.net).
- Not applicable to a console UI library, and so not listed as gaps: pen pressure and tilt (QB64_GJ_LIB `PRESSURE_DEVICE`), shaders (`GLSL`), Steam achievements, Aseprite and Photoshop file reading, image adjustments and palette files. iTerminal reads Photoshop files already (`iConsole/Photoshop/PsdDocument.cs`).

## Sources surveyed

- Survey 1, 2026-11-20: F#, PowerShell, R, Scheme, Mojo and Fortran (SageTUI, Mire, ConsoleMonster, HermesConsoleUI, termr, guile-ncurses, mist, fortran-dialog). Basis: the docs and README of each library, not run-time behaviour.
- Survey 2, 2026-11-20: Bash, Pony, Roc, Objective-C and Emacs Lisp (tuish, boxlib, termax.pony, roc-tui, OCCurses, vui.el, Emacs Widget Library, tui.el). Basis: the README or manual, plus the repository record.
- Survey 3, 2026-11-20: Gren, PureScript and Eiffel (gren-tui, purescript-blessed, simple_tui). Only three libraries were read. Basis: the README or repository record.
- Survey 4, 2026-11-20: Red and QB64-PE (Red View text UI backend, QB64_GJ_LIB, ANSI-Print-64). Only three libraries were read. Basis: the docs and README, with the repository record.

Languages covered before the surveys (from the older comparison and the surveys): C#, Python, Rust, Go, C++, C, JavaScript and TypeScript, Java, Kotlin, Scala, Swift, Dart, Haskell, OCaml, Elixir, Gleam (BEAM), Erlang, Zig, Nim, Ruby, PHP, Julia, Perl, Raku, Common Lisp, Pascal and Delphi, Ada, Haxe, D, V, Lua, Racket, Odin, Clojure, Crystal, Elm, Tcl, Pharo, Vala, F#, PowerShell, R, Scheme, Mojo, Fortran, Bash, Pony, Roc, Objective-C, Emacs Lisp, Gren, PureScript and Eiffel. Gren is treated as its own language, although it is an Elm dialect.

### Libraries surveyed

| Library | Language | Licence | Date checked | Release (as found) | Survey | Links |
| --- | --- | --- | --- | --- | --- | --- |
| SageTUI | F# | Not stated | 2026-11-20 | 0.9.10, 5 August 2026 (NuGet page; the README names 0.9.3) | 1 | [GitHub](https://github.com/WillEhrendreich/SageTUI), [NuGet](https://www.nuget.org/packages/SageTUI) |
| Mire | F# | Not stated | 2026-11-20 | 0.8.0, dated 10/12/2026 on the page (day and month order not stated); pre-1.0 | 1 | [GitHub](https://github.com/HelgeSverre/mire), [NuGet](https://www.nuget.org/packages/Mire) |
| ConsoleMonster | PowerShell (and .NET) | Not stated | 2026-11-20 | 2.3.0, last updated 11/6/2026 on the page (format not stated); NuGet lists 3.0.0 as newer; no GitHub release | 1 | [site](https://consolemonster.dev/), [GitHub](https://github.com/EvotecIT/ConsoleMonster), [NuGet](https://www.nuget.org/packages/ConsoleMonster/2.3.0) |
| HermesConsoleUI | PowerShell | Not named on the page | 2026-11-20 | 2.1.0, 8 February 2026 | 1 | [PowerShell Gallery](https://www.powershellgallery.com/packages/HermesConsoleUI), [GitHub](https://github.com/damdavidprieto/HermesConsoleUI) |
| termr | R | Not stated | 2026-11-20 | Not confirmed: the README shows development 0.9.0.9000 and says the CRAN release is pending; a search snippet gave 1.0.1 (**unverified**); no release date found | 1 | [README](https://rdrr.io/cran/termr/f/README.md), [repository](https://github.com/menshikovs/termr) |
| guile-ncurses | Scheme (Guile) | Not stated | 2026-11-20 | 2.2, 8 April 2017; no newer release found | 1 | [manual](https://www.gnu.org/software/guile-ncurses/manual/html_node/index.html) |
| mist | Mojo | Not stated | 2026-11-20 | No release on the page; the install text names tag v0.4.0; no date found | 1 | [GitHub](https://github.com/thatstoasty/mist) |
| fortran-dialog | Fortran | Not stated | 2026-11-20 | No release on the page; described as a work in progress | 1 | [GitHub](https://github.com/interkosmos/fortran-dialog) |
| tuish | Bash | ISC | 2026-11-20 | No release; last push 14 August 2026 | 2 | [GitHub](https://github.com/alganet/tuish) |
| boxlib | Bash | MIT | 2026-11-20 | No release; last push 22 October 2025 | 2 | [GitHub](https://github.com/iusmac/boxlib) |
| termax.pony | Pony | BSD-2-Clause | 2026-11-20 | 0.2.2, 13 July 2022; the README says it is not ready for production | 2 | [GitHub](https://github.com/nogginly/termax.pony) |
| roc-tui | Roc | UPL-1.0 | 2026-11-20 | No release; archived by its owner on 30 March 2025 | 2 | [GitHub](https://github.com/lukewilliamboswell/roc-tui) |
| OCCurses | Objective-C | Not stated | 2026-11-20 | No release; last push 9 October 2011 | 2 | [GitHub](https://github.com/itaiferber/OCCurses) |
| vui.el | Emacs Lisp | GPL-3.0 | 2026-11-20 | v1.4.0, 18 August 2026; last push 16 November 2026 | 2 | [GitHub](https://github.com/d12frosted/vui.el) |
| Emacs Widget Library | Emacs Lisp | Not stated (built into GNU Emacs) | 2026-11-20 | Not stated on the page; the manual ships with Emacs | 2 | [manual](https://www.gnu.org/software/emacs/manual/html_mono/widget.html) |
| tui.el | Emacs Lisp | GPL-3.0 | 2026-11-20 | No release; last push 28 October 2024; the README says it is experimental | 2 | [GitHub](https://github.com/ebpa/tui.el) |
| gren-tui | Gren | BSD-3-Clause | 2026-11-20 | 5.0.0 (tag dated 8 January 2026; last push 8 January 2026); the registry page read shows 4.0.0 | 3 | [GitHub](https://github.com/blaix/gren-tui), [Gren package page](https://packages.gren-lang.org/package/blaix/gren-tui/version/4.0.0/overview) |
| purescript-blessed | PureScript | MIT | 2026-11-20 | No release; tag v1.0.0 dated 10 July 2025; last push 5 August 2025 | 3 | [GitHub](https://github.com/shamansir/purescript-blessed) (the Pursuit page returned a bot check and was not read) |
| simple_tui | Eiffel | MIT per the README badge; the GitHub licence field is empty | 2026-11-20 | No release or tag; last commit 8 November 2026; the README calls it "Phase 2" (core widgets complete) | 3 | [GitHub](https://github.com/simple-eiffel/simple_tui) |
| Red View text UI backend | Red | BSL-1.0 | 2026-11-20 | Red v0.6.6, 19 March 2025 (latest release); repository last pushed 20 November 2026; the TUI backend was announced as merged but incomplete | 4 | [blog](https://red-lang.org/2024/06/text-ui-view-backend.html), [GitHub](https://github.com/red/red) |
| QB64_GJ_LIB | QB64-PE (BASIC) | MIT | 2026-11-20 | No GitHub release; last pushed 19 November 2026; requires QB64-PE 4 or later; the README says it is usable but not finished | 4 | [GitHub](https://github.com/grymmjack/QB64_GJ_LIB) |
| ANSI-Print-64 | QB64-PE (BASIC) | MIT | 2026-11-20 | No GitHub release; last pushed 27 July 2026; needs the latest QB64-PE and a 32-bit graphics screen | 4 | [GitHub](https://github.com/a740g/ANSI-Print-64) |

## Consolidated

Consolidated on 2026-11-20 from Surveys 1 to 4 into this one document. Each item (132 to 153, 22 in all) appears once, under its group, with its number unchanged. No two surveys described the same item under different numbers, so no "also" notes were needed. The "Status after the pull" block of Survey 1 is applied (item 130 built, and the re-checks of older items) and the dated block is dropped. The survey log, the per-survey narratives and the per-library feature descriptions are removed; their item facts are kept above.

### Later surveys

#### Survey 5: Vim script, MoonBit and Gambas

Survey 5, dated 2026-11-21: vim-quickui (Vim script), tui.mbt (MoonBit, a fork of mizchi/tui) and the terminal form classes of Gambas (gb.term.form). The basis is the docs and README of each library, not run-time behaviour, plus the GitHub record for licence, release and push dates. "Not found" means a grep of `iConsole/` and `iTerminal.Core/` (`--include=*.cs`) on branch `fix-application-lifetime` at commit `38ed0b4b`. The brief asked for six to ten libraries from languages no survey covers; only three had a readable feature list, because the other candidates were archived, unreadable, low-level bindings or had no widget set (see "Considered and skipped"). Vim script, MoonBit and Gambas are new to the document.

Libraries read:

| Library | Language | Licence | Date checked | Release (as found) | Survey | Links |
| --- | --- | --- | --- | --- | --- | --- |
| vim-quickui | Vim script | MIT | 2026-11-21 | 1.5.6, released 3 May 2026; last push 30 June 2026 | 5 | [GitHub](https://github.com/skywind3000/vim-quickui), [manual](https://raw.githubusercontent.com/skywind3000/vim-quickui/master/MANUAL.md) |
| tui.mbt (fork of mizchi/tui) | MoonBit | None shown by GitHub | 2026-11-21 | Tag v0.5.0, commit dated 4 February 2026; no GitHub release; last push 6 April 2026 | 5 | [GitHub](https://github.com/xrr2016/tui.mbt) |
| gb.term.form (Gambas) | Gambas (BASIC dialect) | Not stated | 2026-11-21 | Not stated on the pages; partial read | 5 | [TermForm](https://gambaswiki.org/wiki/comp/gb.term.form/termform), [TermButton](https://gambaswiki.org/wiki/comp/gb.term.form/termbutton) |

Feature sources: vim-quickui from its manual (the dialog guide, DIALOG.md, was not read); tui.mbt from its README; gb.term.form from the TermForm and TermButton class pages. The Gambas classes TermFrame and TermLabel are named by a search snippet only, so they are **unverified**, and no entry, list or check-box class was confirmed. Its gb.ncurses component was read and is listed under "Considered and skipped".

**Layout**

154. **Named areas in the grid layout.** *Missing.* `GridPanel` places a child by row, column and span numbers (`Add(control, row, column, rowSpan, columnSpan)`), and no track or cell can be given a name and used to place a child. Search terms that found nothing in `iConsole/` and `iTerminal.Core/`: `GridArea`, `NamedArea`, `TemplateArea`, `AreaName`. **Seen in:** tui.mbt (a grid with named areas and spanning cells, from its README). Fits: `GridPanel`, `BorderedGrid`. Priority: low, because numbered spans cover the current dialogs.

**Controls**

155. **Reusable location list (quickfix style).** *Partly.* A list of places, each a file, a line and a text, with next and previous jumps that open the line in a viewer or editor, is not a control. The search drive shows hits in a panel and `SearchResultsIndicator` (`iConsole/Controls/StatusBar/SearchResultsIndicator.cs`) shows the count in the status bar, but no list of hits exists as a reusable control. Search terms that found nothing: `Quickfix`, `QuickFix`, `LocationList`, `ProblemList`, `DiagnosticList`, `ErrorList`. **Seen in:** vim-quickui (a quickfix preview built on its popups). Fits: `VirtualListBox<T>` or `VirtualTable<T>` for the list, `TextViewerControl.ShowLine(line, column)` for the jump. Build, git and search output would share it. Priority: medium.

**Framework**

156. **A user object on each control.** *Missing.* `Control` has a `Name`, but no property holds an application object. Search terms that found nothing in `iConsole/Controls/`: `UserData`, `UserObject`, `object? Tag`. **Seen in:** Gambas gb.term.form (`Tag` on the TermButton class and, by inheritance, on each terminal control, per its class page). Fits: `Control`. Priority: low, because typed fields and `Name` cover most uses.

#### Suggested order for Survey 5

1. Item 155 (location list): one list for search, build and git output, with next and previous jumps.
2. Item 156 (user object on a control): small; it removes the side tables that dialogs keep for their rows, where they have them.
3. Item 154 (named grid areas): a convenience over the numbered spans.

#### Already covered (Survey 5 candidates)

- Spacer element (tui.mbt): `SizeRule.Fill(weight)` on an empty child, in `iConsole/Layouts/SizeRule.cs`. No separate type.
- Toggle switch (tui.mbt): `CheckBox` covers it. `ToggleSwitch` and `OnOffSwitch` found nothing.
- Signals and reactive state (tui.mbt): item 27 (partly built, `ObservableValue`).
- Stat and meter displays (tui.mbt): item 139 (metric tile); `Gauge` is built.
- Terminal popup that runs a TUI program with an exit callback (vim-quickui): item 64, still missing (see the re-checks below).
- Preview popup that closes when the cursor moves, unless pinned (vim-quickui): item 113 (a list with a preview pane).
- Data-driven dialog from a definition with seven control types (vim-quickui): item 121 (declarative layout files) and item 119 (typed results).
- Input box with history (vim-quickui): item 49. Registers are a Vim feature and are not added.
- Confirm dialog with lettered choices (vim-quickui): item 147 (caption hot keys, partly built) and item 94 (prompt dialogs, built).
- Colour groups for hot keys, disabled items and help text (vim-quickui): items 98 and 120. `ColorScheme` has disabled, selected and focused-border colours, and no hot-key or help colour.
- Maximised window (Gambas `Maximized`): item 68 (window management).
- Buffer switcher (vim-quickui): item 68. Function list (vim-quickui): item 118.
- Text popup with syntax highlighting (vim-quickui): built (`Popup` with `TextViewerControl` and `ISyntaxHighlightingService`).
- Rendering that writes only the changes (tui.mbt): built (`ConsoleBufferRendererJob`).
- Flexbox-style rows and columns with wrap (tui.mbt): built (`StackPanel`, `SizeRule`).
- Shadow, border, padding and fill (Gambas): built (`Shadow`, `BorderStyle`, `BoxPanel`, `SizeRule`).
- Focus traversal (Gambas `GetNextFocusedControl`): built (Tab).
- Setup wizard, command palette, form and Kitty graphics demos (tui.mbt): built (`Wizard`, the command palette, `FormPanel`; items 7 and the Sixel work).
- Tabs, progress bars, spinners, sparklines and gauges (tui.mbt): built.
- Reverse video for a whole control (Gambas `Invert`): not added. `Conxel` has reverse video, and a per-control switch is cosmetic.

#### Considered and skipped (Survey 5)

- jtermbox (Janet): a termbox binding with cell drawing, colours, keys and mouse, but no widget layer. Last push 12 March 2021 (GitHub record). Source: [repository](https://github.com/Alligator/jtermbox).
- jermbox (Janet): known from a search snippet only; described as archived and moving to Sourcehut. Not read.
- journo (Janet): a question-and-answer prompt library, not a full-screen one (search snippet). Not read.
- cobcurses (COBOL, LGPL-2.1): last push 5 November 2017. Its page names no widgets, menus, fields, colours or mouse, and the only changelog entry is from 2017. It is the only open-source COBOL curses option read; cobol-ncurses-lib is described as unstable (search snippet, not read). Source: [repository](https://github.com/ve3wwg/cobcurses).
- libvt (FreeBASIC): the docs site and its API page returned HTTP 404 on two fetches, and no GitHub repository was found under the same name (HTTP 404 from the GitHub API). Its TUI section is known only from a search snippet, so it is **unverified**.
- fbUI (FreeBASIC): GitHub shows it archived, last push 24 February 2016. Its page names no controls and does not say whether it draws in text mode. Not counted.
- gb.ncurses (Gambas): read. It offers windows, a screen, borders, colour pairs, attributes, a cursor, keys and input modes, and no widgets or mouse. It is a set of low-level windows.
- gb.form.terminal (Gambas): a VT-100 terminal emulator control, not a widget set (search snippet, not read).
- mizchi/tui (upstream of tui.mbt): the GitHub API returned HTTP 404 for the upstream path, so the fork was read instead. The fork's README names the upstream package at version 0.3.0.
- onebit-tui (labelled MoonBit in search): the snippet describes an OpenTUI project in TypeScript, which is covered. Not counted.
- Ceylon, Hare, Cangjie and J: the searches found no terminal widget library in these languages; the results named other languages.
- Smalltalk: searched for Squeak and Pharo terminal widgets. Pharo is covered already; no terminal widget library was found for Squeak.
- Octave and MATLAB: Octave's UI is graphical. A 2016 mailing-list post suggested CDK for text widgets; no Octave binding was found. Not counted.
- PureBasic: the console has no gadgets, according to a community forum thread. ConsoleX appears in a search snippet only, with no features. Not counted.

#### Status re-checks (Survey 5)

Items 64, 113, 121, 49, 98 and 147 were checked by search, with no change of status. Item 64 is still missing: `TerminalPanel` runs commands, and no pseudo-terminal or escape-sequence parser was found (`forkpty`, `openpty`, `CreatePseudoConsole`, `VtParser`, `TerminalEmulator` match nothing). Item 113 has no preview control in `iConsole/Controls` or `iConsole/Dialogs`. Item 121 has no layout loader (`LayoutLoader`, `FromJson`, `LoadLayout`). Item 49 has no history type in the text controls (`History` appears only in a comment in `AutoCompletion.cs`). Item 98 has no colour derivation helper. Item 147 still parses `&` only in `ContextMenuItem.cs`. No status change was found, so no dated block was added.

#### Survey 6: Fantom and Yabasic

Survey 6, dated 2026-11-22: ansi-terminal (afTerminal, Fantom) and clasquinator (yabasiclibs, Yabasic). The target was six to ten libraries from languages no survey covers yet, and only these two had a feature list to read: the other candidates were searched and rejected (see "Considered and skipped (Survey 6)"). Both are the only option in their language and both are unmaintained, which the table records. The basis is the README and docs of each library, fetched on 2026-11-22, plus the repository record for licence, release and last commit; it is not run-time behaviour. "Not found" means a grep of `iConsole/` and `iTerminal.Core/` (`--include=*.cs`) on `main` at commit `b9b81269`, the commit this run read. The highest item is now 157.

| Library | Language | Licence | Date checked | Release (as found) | Survey | Links |
| --- | --- | --- | --- | --- | --- | --- |
| afTerminal (ANSI Terminal) | Fantom | ISC (README badge) | 2026-11-22 | 0.9.0 in the README and on Eggbox; no GitHub release or tag; last commit 13 November 2017; unmaintained | 6 | [GitHub](https://github.com/Fantom-Factory/afTerminal), [API docs](https://eggbox.fantomfactory.org/pods/afTerminal/doc) |
| clasquinator (yabasiclibs) | Yabasic | MIT | 2026-11-22 | No version given; copyright 2018; repository archived on 11 June 2022 and read-only; the README says it is no longer maintained and still alpha | 6 | [docs](https://clasqm.github.io/yabasiclibs/clasquinator), [GitHub](https://github.com/clasqm/yabasiclibs) |

Features of afTerminal, from its README: an `AnsiTerminal` control that wraps a `RichText` widget and prints ANSI sequences, an `AnsiBuf` helper that builds them, a 256-colour palette or full colour, foreground and background colours, bold, italic, underline and crossed-out text, cursor movement (up, down, left, right and absolute), save and restore of the cursor, and clearing of the line and the screen. Features of clasquinator, from its documentation page: message, input, password, one-line menu and submenu, radio, string display, text file viewer, wait, yes/no, and file and directory choosers, each with four looks. The dialogs are drawn inside the library; the choosers call Linux or Windows utilities (`ls`, `grep`, `dir` and similar). The page lists no list, checklist or progress dialog. Both lists are summaries in our own words.

**Terminal protocol**

157. **Cursor save and restore.** *Missing.* No save or restore of the cursor position is found: no `ESC 7` and `ESC 8` (DECSC and DECRC), and no `CSI s` or `CSI u`. The renderer positions every cell with an absolute `CSI row;col H` (`iConsole/Drivers/SystemConsoleDriver.cs:297`), so the sequence is needed only for output that moves the cursor away from the screen. Search terms that found nothing in `iConsole/` and `iTerminal.Core/`: `SaveCursor`, `RestoreCursor` (the one match is `VirtualMachineManagerDialog.RestoreCursor`, which restores a list row and not the terminal cursor), `DECSC`, `DECRC`, `\u001b7`, `\u001b8` and `\u001b[s`. **Seen in:** afTerminal (cursor save and restore, from its README). Fits: `AnsiRenderer` and `IConsoleDriver`. A restore should use `ESC 8`, because the Kitty keyboard protocol already uses `CSI u` (`iConsole/Inputs/KittyKeyboardProtocol.cs`). Item 28 (inline output) is the feature that would need it. Priority: low.

#### Suggested order for Survey 6

1. Item 157 (cursor save and restore): low; build it only with item 28 (inline output), which would be its first user.

#### Already covered (Survey 6 candidates)

- Bold, italic, underline and crossed-out text (afTerminal): built (`Conxel`, with strike-through; see "Text attributes" in `ui-framework.md`).
- 256-colour palette and full colour (afTerminal): item 34, still missing (no `38;5` or `38;2` in `iConsole/`). The sixteen colours are built (`Color`).
- Absolute cursor positioning (afTerminal): built (`SystemConsoleDriver.cs:297`, `VirtualSequenceConsoleDriver.cs:287`).
- Clearing the screen (afTerminal): built (`IConsoleDriver.Clear`, `SystemConsoleDriver.cs:223`). A line is cleared by painting it, because a control paints its whole rectangle.
- A string builder for ANSI text (afTerminal `AnsiBuf`): built as `ConxelString`, written out by `AnsiRenderer.ConxelString2Ansi`.
- A control that shows text with ANSI colour codes (afTerminal `AnsiTerminal`): partly, under item 47; see the status block below.
- Message dialog (clasquinator): item 94, still missing.
- Wait dialog (clasquinator): item 94, still missing. `Spinner` and `ProgressGroup` exist, but no dialog wraps them.
- Input, password, yes/no and radio prompts (clasquinator): built (prompt kinds of item 94, `PasswordBox` of item 130, `RadioGroup`).
- One-line menu and submenu (clasquinator): built (`ContextMenu` with submenus, `MenuBar`).
- Text file viewer (clasquinator): built (`TextViewerControl`, the F3 viewer).
- File and directory choosers (clasquinator): built (`FilePicker`, `FolderSelectorDialog`).
- Capital letters in yes/no labels as hot keys (clasquinator): item 147, partly built.

#### Considered and skipped (Survey 6)

- Jai, Carbon, Factor and Hare: one search each, no widget library found; the results were for other languages.
- C3: one search; the only hit is `tessela`, a C library. C is covered.
- Wolfram Language: one search, no library found.
- Wren: one search; the only hit is Wren CLI, a runtime with file and network modules and no widget layer. Not read.
- ReasonML: Matcha, bs-ink and reason-ink were found. Matcha is an OCaml library (OCaml is covered), and the other two are bindings to Ink. Not read.
- ReScript: a search found only bindings (`opentui-rescript`, `rescript-opentui-react`, `rescript-ink`), which the document already excludes. Not read.
- Lean 4: Leansi (already skipped as output-only in an earlier survey), Pigment (a colour library), ProofWidgets (for the editor's graphical infoview) and lean-tui (an application). Not read again.
- Elvish: a Go shell; its `tk` widgets are part of the shell (from a search snippet, **unverified**). Go is covered.
- ArkTS (HarmonyOS): ArkUI is graphical. `libghostty-ohos` embeds a terminal surface and has no widgets; it was found on one unofficial mirror, so it is **unverified** and was not read.
- SWI-Prolog `library(tty)`: a termcap helper with a `menu/3` predicate and a terminal-size predicate (from a search snippet of the manual, **unverified**). It is a helper, not a widget library.
- Liberty BASIC: its text windows, buttons and menus are part of the language (from a search snippet, **unverified**). No third-party console library was found.
- Nushell: the Rust crates it uses (`nu-term-grid` is internal to Nushell) are Rust, which is covered.
- yabasic-libs-2 (Yabasic): its OpenEditor library launches text editors and is not a widget set (from a search snippet, **unverified**).

#### Status after the pull of 2026-11-22

Items 2, 3, 34, 94 and 132 to 156 were searched again. Items 2, 3, 34, 94 and 146 (no `2004h`, `NO_COLOR`, `38;5`, `38;2` or `?1049` in `iConsole/`) and the others not listed below kept their status. Three items changed:

- **Item 47 (inline colour and style tags): now partly.** `AnsiText.Parse` (`iConsole/Helper/LogHighlighting.cs:133`) turns the SGR colour, bold, italic and underline codes into spans, and only `LogView` (`iConsole/Controls/LogView.cs:196`) calls it. Labels and the other text controls take no escape codes, and no markup parser was found. Earlier status: still missing.
- **Item 137 (export of table or list data): still partly, and narrower.** `TableExport.Build` (`iConsole/Csv/TableExport.cs:29`) writes rows as CSV, JSON or XML, and the F2 command of the CSV viewer (`iConsole/Commands/CsvExportCommand.cs`) uses it. No Markdown writer exists, and no table or list control calls it. The search terms named in the item (`ToCsv`, `ExportCsv` and so on) still find nothing; the names in use are `CsvExport.Build` and `TableExport.Build`.
- **Item 143 (indent and dedent helpers): still partly, and the text is out of date.** `TextEditorControl.Indent(bool outdent)` (`iConsole/Controls/Editors/TextEditorControl.cs:221`) indents and outdents the selected lines in the editor. No `Indent(` or `Dedent(` helper exists in `iConsole/Helper/`, so `StringHelper` still has none.

Rows outside this document, in the comparison table of `Documentation/architecture/ui-framework.md` (not edited by this run; that file needs the update):

- **Focus-in and focus-out reporting (`?1004h`)**: built. `TerminalFocusReporting` (`iConsole/Inputs/TerminalFocusReporting.cs`) and `TerminalFocusService`; the Posix input job switches it on and off (`iConsole.Inputs.Posix/Jobs/PosixTerminalInputPublisherJob.cs`). The table says "Not found".
- **Window title (OSC 0/2) and title stack**: built. `TerminalSequences.SetTitle` (OSC 2), `PushTitle` and `PopTitle` (`CSI 22;2t` and `CSI 23;2t`), used by `TerminalTitleService`. The table says "Not found".
- **Cursor shape (DECSCUSR) and pointer shape (OSC 22)**: built (`iConsole/Display/CursorShape.cs`, `PointerShapeService`). Neither is in the table.

#### Survey 7: Harbour-CUI and HBTui (Harbour)

Survey 7, dated 2026-11-24: Harbour-CUI (omm, a fork of digikv/Harbour-CUI) and HBTui (tfonrouge), both written in Harbour, the Clipper dialect. No earlier survey covers Harbour. The brief asked for six to ten libraries from uncovered languages, but eighteen language or dialect names were searched (Harbour, Squirrel, newLISP, Nelua, Pico Lisp, Oz and Mozart, GNU Smalltalk, Hack, BQN, Shen, Unicon, AngelScript, Gosu, Fennel, Jakt, Inko, Boo and Xojo), and Harbour was the only one with a readable widget list, so this survey is short. The basis is the README of each library, fetched on 2026-11-24 through a page summary (the wording was not checked line by line against the file), plus the GitHub repository record for licence, archive flag and last push. It is not run-time behaviour. "Not found" means a grep of `iConsole/` and `iTerminal.Core/` (`--include=*.cs`) on branch `fix/build-warnings-3` at commit `c5c693b3`.

| Library | Language | Licence | Date checked | Release (as found) | Survey | Links |
| --- | --- | --- | --- | --- | --- | --- |
| Harbour-CUI (omm fork of digikv/Harbour-CUI) | Harbour | MIT | 2026-11-24 | No release or tag shown; last push 19 November 2026 (GitHub record) | 7 | [GitHub](https://github.com/omm/Harbour-CUI) |
| HBTui | Harbour | No licence file (the README says so) | 2026-11-24 | No release; last push 2 April 2026; not archived (GitHub record) | 7 | [GitHub](https://github.com/tfonrouge/HBTui) |

Features of Harbour-CUI, from its README: about thirty widgets (a table of 33 names; the text says 30) for windows, inputs, buttons, lists, menus, layouts, charts and toasts; text fields that enforce a picture and validation rules; overlapping windows with title-bar move, resize, maximise, minimise and close, click-to-raise and Ctrl+F6 cycling; seven built-in themes with 55 colour categories; wide characters that take two cells; buffered repaints; and headless tests. Features of HBTui, from its README: the same widget names, each widget painting in its own viewport, a singleton event loop with three priorities, four built-in themes, and a note that no licence file is included.

**Input**

158. **Overwrite mode in a text field.** *Missing.* A text field inserts at the cursor only. The Insert key does not switch to overwrite, and the file panel binds Insert to invert selection (`iConsole/Commands/InvertSelectionCommand.cs`), so a switch would have to apply only while a text field has focus. Search terms that found nothing in `iConsole/` and `iTerminal.Core/`: `OverwriteMode`, `InsertMode`, `OverType`, and `ConsoleKey.Insert` in the text controls (`iConsole/Controls/TextBox*.cs`). The only inserting path found is `TypeCommand` (`iConsole/Commands/TypeCommand.cs:35`). **Seen in:** Harbour-CUI (HTLineEdit, where Ins toggles insert mode, from its README). Fits: `TextBox`, `TypeCommand`, `TextEditorControl`. Priority: low.

159. **Mouse capture for a drag.** *Partly.* `Control.HandleMouseEventAsync` offers an event to each child in turn until one takes it, so a control with its own drag state (`Splitter` and `Slider`, which keep a `_dragging` flag) still gets the moves after the pointer leaves it. No capture exists, though: a pressed control does not own the pointer until release, and a sibling that takes the event first wins. Search terms that found nothing in `iConsole/` and `iTerminal.Core/`: `MouseCapture`, `CapturedControl`, `DragCapture`, `PointerCapture`, `BeginDrag`. **Seen in:** Harbour-CUI (its mouse section says widgets opt in to moves and the release while a button is held, even outside their bounds, from its README). Fits: `Control.HandleMouseEventAsync` and `TerminalInputConsumerJob`. Priority: low, because the splitter, the slider and window drag already work.

**Framework**

160. **One action in a menu, a toolbar and a context menu.** *Partly.* A menu entry takes an `ICommand` (`MenuItem`), and `ContextMenuItem` runs an `Execute` delegate. `ToolbarItem` carries only a caption, a key hint and an enabled flag, and `Toolbar` reports the chosen index through `ItemInvoked`, so a toolbar cannot share a command with a menu without extra wiring. Search terms that found nothing in `iConsole/` and `iTerminal.Core/`: `IAction`, `ActionItem`, `ToolbarItem.Command`, and any class ending in `Action` (none in `iConsole/`; `ActionButton` is a button, not an action). **Seen in:** Harbour-CUI (HTAction, one action object used by the menu bar, toolbar and context menu, from its README). Fits: `Toolbar`, `ToolbarItem`, `ContextMenuItem`. Priority: low.

#### Suggested order for Survey 7

1. Item 158 (overwrite mode): small and visible in the editor and the command line; it must leave Insert alone in the file panels.
2. Item 160 (one action in several places): removes separate wiring where a dialog toolbar repeats a menu command.
3. Item 159 (mouse capture): only when a new drag control needs a pointer that leaves its bounds.

#### Already covered (Survey 7 candidates)

- Windows and the desktop (HTMainWindow, HTDesktop): built (`Window`, `Desktop`).
- Dialogs and message boxes (HTDialog, HTMessageBox): built (`Dialog`; the message box is `ErrorPromptDialog`, and the prompt kinds are item 94).
- Line edit and get fields (HTLineEdit, HTGet): built (`TextBox`). The picture rule is `InputMask` (`iConsole/Model/InputMask.cs`, `TextBox.Masking.cs`) and the validation rule is `IInputValidator` (`iConsole/Model/Validations/InputValidators.cs`). A rule that enables a field from another field's value is not a separate type.
- Multi-line text edit (HTTextEdit): built (`TextEditorControl`).
- Spin box (HTSpinner): built (`NumericUpDown`).
- Check list, list box and combo box (HTCheckList, HTListBox, HTComboBox): built (`CheckList`, `ListBox<T>`, `ComboBox<T>`).
- Browse grid (HTBrowse): built as `Table<T>` and `VirtualTable<T>`. Its inline cell editing is not built (no `CellEditor`, `EditCell` or `BeginEdit` found), and that is item 104.
- Push button, check box and radio button: built (`Button`, `CheckBox`, `RadioGroup`).
- Menu bar, menus and context menu (HTMenuBar, HTMenu, HTContextMenu): built (`MenuBar`, `ContextMenu`, with submenus).
- Box, stack and grid layouts (HTVBoxLayout, HTHBoxLayout, HTGridLayout): built (`StackPanel`, `GridPanel`, `SizeRule`).
- Label, frame, separator and scroll bar (HTLabel, HTFrame, HTSeparator, HTScrollBar): built (`Label`, `BoxPanel`, `Separator`, `ScrollBar`).
- Progress bar, status bar and tab widget (HTProgressBar, HTStatusBar, HTTabWidget): built (`ProgressBar`, `StatusBar`, `Tab`).
- Sparkline and bar chart (HTSparkline, HTBarChart): built (`Sparkline`, `BarChart`).
- Toast (HTToast): built (`Toast`, `ToastHost`; see "Notifications and toasts" in `ui-framework.md`).
- Tool bar (HTToolBar): built as `Toolbar`; its items are not commands, which is item 160.
- Drop shadows: built (`Shadow`).
- Overlapping windows with title-bar move and resize: partly, under item 43. Click-to-raise was not checked. Maximise and minimise, Ctrl+F6 cycling and a window list: item 68, still missing (no `ConsoleKey.F6` binding in `Desktop`, and no tile, cascade or window list was found).
- Clipboard keys and Shift+arrow selection in a text field: built (`ClipboardKeys`, `TextBox.SelectionTextModel`).
- Seven or four built-in themes and colour categories (Harbour-CUI, HBTui): built as `ColorScheme` and `ThemeService`. Saving a custom theme to a file is item 10 (styling by rules) and item 98 (theme design tokens).
- Custom pixel fonts (Harbour-CUI, 8 by 8 glyphs): the font part is item 38 (big text), still missing. Sprites are not added, because no iTerminal feature asks for them.
- Held-key detection (Harbour-CUI): partly. `WindowsPhysicalKeyboard` polls the modifier keys with `GetAsyncKeyState` (`iConsole/Interop/WindowsPhysicalKeyboard.cs`), and key release on a terminal is item 6 (not built).
- Wide characters that take two cells (Harbour-CUI): item 1, partly built (see the status block below).
- Priority event loop (HBTui `HTEventLoop`): built as `TaskManager` priorities (`Rendering > Input > Background`) and `Desktop` jobs.
- Headless test driver (`gtnul`): built (`VirtualSequenceConsoleDriver`, `AppHarness`, `iConsole.Test`).
- Viewport per widget, with (0,0) at its top left (HBTui): built, since every control has positions relative to its parent.

#### Considered and skipped (Survey 7)

- libtui (e-Lama, the README names it libgui; Harbour): GPL-3.0; last push 9 October 2020, so abandoned; its page lists no widgets. Skipped because the two libraries above are the readable Harbour options. Source: [repository](https://github.com/e-Lama/libtui).
- harbr (dev-town, "Harbour TUI"): an application for Git projects and terminal sessions, not a library; its repository has no release, and its language is not stated on the page. Not read further. Source: [repository](https://github.com/dev-town/harbr).
- Harbour's terminal drivers (gtcrs, gtsln, gtwvt, gtxwc, gttrm, gtnul): the platform layer under a widget set, not widgets. Codepage translation and the X11 pixel-font build do not apply to iConsole.
- GTWVG and GTWVW (Windows-only): found in a forum thread from 2016; not read.
- The 2016 Harbour community TUI project (Diego Pego): found in a forum post only, with no current status; not read.
- Clipper ACHOICE and GET commands: the native commands of the language, not a library.
- Hack: facebook/hh-clilib (argument parsing and TTY detection, marked abandoned on Packagist) and kilahm/hack-clio (prompts and output with ANSI codes; last release v1.1.0 from 2014). Neither is a widget library. Source: [hh-clilib](https://github.com/hhvm/hh-clilib).
- newLISP, Squirrel, Nelua, Pico Lisp, Oz and Mozart, Shen, Unicon, Fennel, Jakt, Inko and Boo: searched once each, no terminal widget library found.
- GNU Smalltalk: its GUI library, BLOX, is graphical; no terminal library found.
- BQN: bqed is an editor application and the other hits are graphical or other languages.
- AngelScript and Gosu: no console widget library found (Gosu's add-ons are graphical).
- Xojo: no text user interface library found; XUI is a desktop UI kit and OptionParser handles arguments only.

#### Status after the pull of 2026-11-24

Items 1, 2, 6, 34, 36, 58, 146 and 157 were searched again, and the Harbour items were checked against items 6, 10, 38, 43, 68, 94, 98 and 104. Two items changed:

- **Item 1 (wide-character and grapheme width): now partly.** `TextWidth` (`iConsole/Display/TextWidth.cs`) gives the columns of a character from the East Asian Wide ranges and the common emoji ranges, stores a wide character with a continuation cell, and drops zero-width marks. `ConxelString`, `AnsiRenderer` and `SystemConsoleDriver` call it. Grapheme clusters (joined emoji) are not handled, and no per-control clipping in `Table`, `ListBox` or `TextBox` was found. Earlier status: missing.
- **Item 36 (braille and block-character canvas): now partly.** `SchematicWorldMap.RenderBraille` (`iConsole/Display/SchematicWorldMap.cs:142`) draws a map in 2 by 4 Braille dots per cell. It is one control's renderer, not a reusable canvas, so the item stays open. Earlier status: missing.

Unchanged: item 2 (no `2004h` or `BracketedPaste`), item 6 (`KittyKeyboardProtocol.cs` pushes flag 1 only, so key release is not reported), item 34 (no `38;5` or `38;2`), item 146 (no `1049`), item 157 (`CursorShape.cs` has only DECSCUSR) and item 58 (the repeat count is built).

Outside this document, the following text is now out of date and was not edited (it is read-only or belongs to another file): the item 1 entry of `Documentation/architecture/ui-framework/tui-library-comparison.md` (read-only) and the "Suggested improvements" item 1 in `Documentation/architecture/ui-framework.md` ("No width handling was found"), and the comparison table row for wide characters in `ui-framework.md`. The header line of this document still gives 156 as the highest item; the highest item is now 160.
