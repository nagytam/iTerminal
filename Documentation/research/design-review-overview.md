# Design review overview

UI/UX review of the iConsole controls and the iTerminal dialogs and panels. This document shows the current state only: one section per scope, and every finding numbered once across the whole document. Finding numbers are never reused. A finding that is fixed moves to the **Resolved** list at the end. Earlier references (for example "R2 finding 5") map to the numbers used now in the table under "Earlier finding numbers".

## Scopes

| Scope | Open findings | Resolved findings |
| --- | --- | --- |
| [iConsole list and text-input controls](#iconsole-list-and-text-input-controls) | 1 to 10 | none |
| [Recent UI changes: key map, grid queries, evidence notebook, tab progress, folder selector](#recent-ui-changes-key-map-grid-queries-evidence-notebook-tab-progress-folder-selector) | 12 to 14, 16 to 20, 23 to 28 | 11, 15, 21, 22 |
| [Feedback controls, dialog shell, and the Git, remote, palette and file panel screens](#feedback-controls-dialog-shell-and-the-git-remote-palette-and-file-panel-screens) | 29 to 43 | none |
| [File-operation dialogs: copy and move, delete, rename, multi-rename, conflict prompt, secure wipe](#file-operation-dialogs-copy-and-move-delete-rename-multi-rename-conflict-prompt-secure-wipe) | 44 to 54 | none |
| [Music player: F3 music viewer, playlist and catalog panels, audio output](#music-player-f3-music-viewer-playlist-and-catalog-panels-audio-output) | 55 to 64 | none |
| [Dialog set: tables, live data and labels across the dialogs](#dialog-set-tables-live-data-and-labels-across-the-dialogs) | 68 to 70 | 65 to 67 |

Totals: 70 findings, 63 open and 7 resolved.

## Earlier finding numbers

Each finding from an earlier pass, with its number in this document. "Open (partly fixed)" means a status check found part of the finding fixed; the finding stays open.

| Earlier reference | Number now | State |
| --- | --- | --- |
| R1 finding 1 | 1 | open |
| R1 finding 2 | 2 | open |
| R1 finding 3 | 3 | open |
| R1 finding 4 | 4 | open |
| R1 finding 5 | 5 | open |
| R1 finding 6 | 6 | open |
| R1 finding 7 | 7 | open (partly fixed) |
| R1 finding 8 | 8 | open (partly fixed) |
| R1 finding 9 | 9 | open |
| R1 finding 10 | 10 | open |
| R2 finding 1 | 11 | resolved |
| R2 finding 2 | 12 | open |
| R2 finding 3 | 13 | open |
| R2 finding 4 | 14 | open |
| R2 finding 5 | 15 | resolved |
| R2 finding 6 | 16 | open (partly fixed) |
| R2 finding 7 | 17 | open |
| R2 finding 8 | 18 | open |
| R2 finding 9 | 19 | open |
| R2 finding 10 | 20 | open (partly fixed) |
| R2 finding 11 | 21 | resolved |
| R2 finding 12 | 22 | resolved |
| R2 finding 13 | 23 | open |
| R2 finding 14 | 24 | open |
| R2 finding 15 | 25 | open |
| R2 finding 16 | 26 | open |
| R2 finding 17 | 27 | open |
| R2 finding 18 | 28 | open |
| R3 finding 1 | 29 | open |
| R3 finding 2 | 30 | open |
| R3 finding 3 | 31 | open |
| R3 finding 4 | 32 | open |
| R3 finding 5 | 33 | open |
| R3 finding 6 | 34 | open |
| R3 finding 7 | 35 | open |
| R3 finding 8 | 36 | open |
| R3 finding 9 | 37 | open |
| R3 finding 10 | 38 | open |
| R3 finding 11 | 39 | open |
| R3 finding 12 | 40 | open |
| R3 finding 13 | 41 | open |
| R3 finding 14 | 42 | open |
| R3 finding 15 | 43 | open |
| R4 finding 1 | 44 | open |
| R4 finding 2 | 45 | open |
| R4 finding 3 | 46 | open |
| R4 finding 4 | 47 | open |
| R4 finding 5 | 48 | open |
| R4 finding 6 | 49 | open |
| R4 finding 7 | 50 | open |
| R4 finding 8 | 51 | open |
| R4 finding 9 | 52 | open |
| R4 finding 10 | 53 | open |
| R4 finding 11 | 54 | open |
| R5 finding 19 | 55 | open |
| R5 finding 20 | 56 | open |
| R5 finding 21 | 57 | open |
| R5 finding 22 | 58 | open |
| R5 finding 23 | 59 | open |
| R5 finding 24 | 60 | open |
| R5 finding 25 | 61 | open |
| R5 finding 26 | 62 | open |
| R5 finding 27 | 63 | open |
| R5 finding 28 | 64 | open |

## iConsole list and text-input controls

Scope: the controls that show a list of rows or take a line of text, and that most dialogs are built from: `ListBox<T>`, `PopupListView`, `AutoCompletion` with `CompletionModel`, `TextBox` and `SearchBox`. They were picked as one area because every dialog that has "a list and a text input" is made of them, so a fix here reaches all of those dialogs. Dialog-level reviews (git, folder selector, file panel) are separate scopes. Reviewed on 2026-11-20 at commit 5531f167. Read: `CLAUDE.md`, `Documentation/architecture/ui-framework.md`, `key-patterns.md`, `ui-framework/input-and-navigation-controls.md`, the three research overviews (only the console one touches this scope), and the source of the five controls plus `ListBoxModel`, `CompletionModel`, `TextWidth`, `ConxelString.Reset`, `Dialog.HandleKeyEventAsync` and `Table.HandleMouseEventAsync`. Status was re-checked at commit 74a41e86. Not done: no run-time check, no app launch, no tests run, no render dump. `tui-library-comparison.md` was not read in full; the console overview's item numbers (132 and up) and the item numbers of its "Suggested improvements" list (1 to 12, which sit in `ui-framework.md`) were used. Findings that rest on reading alone are marked **unverified**.

### Summary

- The list and text controls already follow the project's hard rules in the places the bug history points to: whole-rectangle painting, atomic list refresh in `ListBoxModel`, `EmptyText`, `IsFocused` checks, Esc handled one layer at a time. The weak spots are second-order: per-frame cost of `ListBox.Draw`, a non-atomic suggestion model, and column-width handling that stops at the renderer.
- Biggest problems: the text well uses fixed `Green` on `Yellow` and `Gray` placeholder colours (finding 1), `ListBox.Draw` copies the whole list once per visible row (finding 2), and `CompletionModel` still clears then adds (finding 3).
- Wide characters are handled in `ConxelString` and the renderer, but `TextBox` and `PopupListView` still count UTF-16 chars (finding 4). The console research overview says wide-character width is "not found"; that is out of date (see Research cross-reference).
- Biggest win: `AutoCompletion`, `Popup` and `Dialog` Esc handling are well documented and consistent with `ITransientOverlayHost`.

### Findings

1. **Text well uses fixed ANSI colours, not the theme** - Severity: **Medium**. Kind: accessibility.
   - Where: [iConsole/Controls/TextBox.cs:770](../../iConsole/Controls/TextBox.cs) to `:773` (value `Color.Green` on `Color.Yellow`, read-only `Color.DarkYellow`), `:796` (placeholder and completion in `Color.Gray`).
   - Problem: every text field in every dialog is green text on a yellow well, and the placeholder and the grey completion ghost are grey on yellow. Contrast depends on the terminal's own palette, and at 16 colours green on yellow is hard to read; the same colours are used on light and dark themes, so a theme cannot fix it. Heuristic: Accessibility, Consistency (every sibling control, `ListBox` included, takes its colours from `ColorScheme`).
   - Improvement: take the well colours from `ColorScheme` (add `InputForeground`, `InputBackground`, `InputReadOnlyBackground` and use `PlaceholderForeground` or `DisabledForeground` for the ghost text), as `ListBox.GetColor` does. Keep `colorScheme.Error` for invalid. Add a `ScreenBuffer` assertion per theme that the value and placeholder colours differ from the well.
   - Effort: M. **Unverified**: the actual contrast needs a render dump (`ITERMINAL_RENDER_DUMPS=1`) on a 16-colour terminal.
   - Status: open. The grid (finding 21), the query lines and the folder selector rows (finding 25) inherit the same colours.

2. **`ListBox.Draw` copies the whole list once per visible row** - Severity: **Medium**. Kind: performance.
   - Where: [iConsole/Controls/ListBox.cs:249](../../iConsole/Controls/ListBox.cs) (`ListModel.ListItems.ToList()`), `:54` (`Count` reads `ListItems`), `:319` (`GetVisualState` reads `ListModel.ListItems[index]`), [iConsole/Model/ListBoxModel.cs:46](../../iConsole/Model/ListBoxModel.cs) and [iConsole/Data/ConcurrentList.cs:43](../../iConsole/Data/ConcurrentList.cs) (`ListItems` is `AsSnapshot()`, a full copy under a lock).
   - Problem: one frame takes about `visible rows + 2` copies of the full list. With 20 000 rows and 30 visible rows that is 32 arrays of 160 KB, which are large-object-heap allocations, at up to 20 repaints a second while a dialog is open. `ui-framework.md` says the list is "copied on each draw" and to keep it to a few thousand rows, but the real cost grows with the visible height too, and `Count` and the mouse handler (`:196`) copy as well. Heuristic: Performance, Resilience to content.
   - Improvement: in `Draw`, take one snapshot and pass it to an overload `GetVisualState(index, item)`; keep the public `GetVisualState(int)` for subclasses. Add `Count` to `IListBoxModel<T>` so `ListBox.Count` and the mouse path stop copying. Assert with a model that counts `ListItems` reads that one draw reads it once.
   - Effort: S to M. **Unverified**: no timing was taken; confirm with a `RecordingStopwatchFactory` test over 20 000 items.

3. **`CompletionModel.UpdateMatchingValues` empties the list, then adds one value at a time** - Severity: **Medium**. Kind: performance, consistency.
   - Where: [iConsole/Model/CompletionModel.cs:32](../../iConsole/Model/CompletionModel.cs) (`MatchingValues.Clear()`) and `:56` to `:59` (`Add` in a loop); read by [iConsole/Controls/TextBox.cs:833](../../iConsole/Controls/TextBox.cs) from `Draw`.
   - Problem: `ListBoxModel` was changed to swap rows in one step (`ReplaceAll`) because a draw between the clear and the adds showed an empty list (`input-and-navigation-controls.md`, "ListBoxModel refreshes are atomic"). `CompletionModel` has the same shape and runs on a worker on every keystroke. A draw in the gap shows no ghost completion in the text well (flicker), and each `Add` raises its own `CollectionChanged`, so a history of a thousand paths raises a thousand notifications per keystroke. With empty text the whole `Completions` set becomes the list, with no cap. Heuristic: Feedback and visibility, Performance.
   - Improvement: build the list first and call `MatchingValues.ReplaceAll(list)`. Optionally stop at a fixed number of matches (for example 200) since the popup shows five rows. Add a test that a reader polling `MatchingValues` during an update never sees zero rows when both the old and the new result are non-empty.
   - Effort: S.
   - Status: open, unchanged at `CompletionModel.cs:32`. The new `DirectoryCompletionModel` publishes with `ReplaceAll`, which is the fix shape.

4. **Wide characters are column-aware in the renderer but not in the controls** - Severity: **Medium**. Kind: usability (resilience to content).
   - Where: [iConsole/Controls/TextBox.cs:731](../../iConsole/Controls/TextBox.cs) (`EditableWidth` in chars), `:782` to `:797` (`displayText.Length`, `Cut(visibleWidth)`), `:810` (cursor column = char index), [iConsole/Helper/StringHelper.cs:37](../../iConsole/Helper/StringHelper.cs) (`Cut` counts chars), [iConsole/Controls/PopupListView.cs:201](../../iConsole/Controls/PopupListView.cs) (`Fit` counts chars). Console survey item 1.
   - Problem: `TextWidth` and `ConxelString` already store a CJK character as two cells, but the text box cuts the visible slice by chars. A name with CJK or emoji in a rename or path box draws wider than the well, and the cursor sits one column left of the real position for each wide character before it, so editing in the middle lands on the wrong character. A drop-down row with a wide name is cut mid-glyph or runs past the popup edge. Heuristic: Resilience to size and content.
   - Improvement: reuse `TextWidth.Fit` in `PopupListView.Fit` (one-line change, S). In `TextBox`, compute the cursor column with `TextWidth.Of(displayText[scrollX..cursor])` and the visible slice by columns; the scroll position stays in chars. Add a `ScreenBuffer` test with `"漢字abc"` in a box and in a combo list.
   - Effort: M (S for the popup list alone). **Unverified**: not run; the `TextBox.Draw` path was read, not drawn. Whether `ConxelString.Append` pads or clips the overflow was not traced.
   - Status: open. The grid was fixed under finding 21; the folder selector rows (finding 25) still count characters.

5. **Pasting several lines from the terminal presses Enter for each line** - Severity: **Medium**. Kind: usability.
   - Where: no bracketed paste anywhere (`grep 2004` over `iConsole`, `iConsole.Inputs.Windows`, `iConsole.Inputs.Posix` finds nothing). [iConsole/Controls/TextBox.cs:392](../../iConsole/Controls/TextBox.cs) (`InsertText`) already turns line breaks into blanks. Console survey item 2.
   - Problem: Ctrl+V works (clipboard service), but a terminal paste (Ctrl+Shift+V, right-click, middle-click) arrives as keys. Newlines in the pasted text are Enter, which in a dialog is the default button. Pasting two lines of paths can confirm a dialog after the first one. Heuristic: Error prevention, Efficiency. This adds a usability case to the research item, which only lists correctness.
   - Improvement: enable `ESC[?2004h` in the input source, deliver the paste as one event, and route it to the focused control's `InsertText` (or ignore it if the control takes no text). `IInputInjector` should have a paste method so a `ConsoleAppDriver` test can cover it. The existing `TextBox.InsertText` is the sink; no new text logic is needed.
   - Effort: M. **Unverified**: confirm with `ITERMINAL_KEY_TRACE=1` while pasting two lines into a dialog box.
   - Status: open. The query boxes (finding 15) are also affected: their length cap is fixed, the paste is not.

6. **`SearchBox` advertises "Esc: Clear the search", and a `Dialog` never lets it run** - Severity: **Low**. Kind: consistency, keyboard.
   - Where: [iConsole/Controls/SearchBox.cs:74](../../iConsole/Controls/SearchBox.cs), [iConsole/Commands/ClearSearchCommand.cs:9](../../iConsole/Commands/ClearSearchCommand.cs), [iConsole/Dialogs/Dialog.cs:76](../../iConsole/Dialogs/Dialog.cs). The app's document manager uses a plain `TextBox` and its own hint `DialogHint.ClearSearchThenClose` ([DocumentManagerDialog.cs:683](../../iTerminal.Core/Dialogs/Documents/DocumentManagerDialog.cs)); no dialog in `iTerminal.Core` uses `SearchBox`.
   - Problem: `Dialog` takes Esc before its children, so inside a dialog `ClearSearchCommand` is never reached and the footer hint is wrong. The control is only usable outside a dialog. The "clear first, then close" behaviour the document manager wants is the documented `ITransientOverlayHost` pattern. Heuristic: Consistency, Keyboard and focus (Esc closes one layer at a time).
   - Improvement: implement `ITransientOverlayHost` on `SearchBox` (`IsOverlayOpen` = the box has text and focus, `CloseOverlay` = `Clear()`), then switch the document manager to it and drop its special hint. Make `Hint.ClearSearch` accurate in both places.
   - Effort: S.

7. **`AutoCompletion` changes the control tree from a worker thread and waits on a task during initialisation** - Severity: **Medium**. Kind: performance, threading.
   - Where: [iConsole/Controls/AutoCompletion.cs:197](../../iConsole/Controls/AutoCompletion.cs) (`Task.Run` -> `UpdateMatches`), `:236` to `:247` (`ApplyVisibility` sets `IsVisible` and calls `ShowPopupAsync`, which attaches the popup), `:111` and `:215` (`OnInitializeAsync` runs `UpdateMatches` inline, which does `UpdateTask.Wait()`).
   - Problem: `CLAUDE.md` says not to mutate the control tree from a worker thread; here `IsVisible` and `AddControl` run on a pool thread, and the generation counter plus the lock, `Popup` claim and re-check (`key-patterns.md`) exist to contain the races that follows. The inline `Wait()` in `OnInitializeAsync` blocks a dialog's opening for as long as matching takes (the list is the full history when the text is empty). Heuristic: Performance, Keyboard and focus.
   - Improvement: let the worker compute and set a "visible wanted" flag with the generation; apply it in `OnIdle`, the way `ListBox` applies `HasPendingUpdate`. Run the first match in the background, not in `OnInitializeAsync`. This should let some of the generation/lock code go; keep `Hide` idempotent.
   - Effort: M. **Unverified**: the existing tests pass on this design, so a race may be rare; a stress test that types and presses Esc quickly, with `MainLoopWatchDog` events, would show it.
   - Status: partly fixed, still open. The visibility is parked for `OnIdle` (`a022af8d`, `66dc1787`), and a model that does I/O starts its first update on a background task ([AutoCompletion.cs:113](../../iConsole/Controls/AutoCompletion.cs) to `:124`). The inline `UpdateTask.Wait()` ([AutoCompletion.cs:254](../../iConsole/Controls/AutoCompletion.cs), called from `OnInitializeAsync`) remains for in-memory models; the status check rates that remaining part Low.

8. **`ListBox` type-ahead is on in one dialog out of about thirty-four users** - Severity: **Low**. Kind: usability (efficiency).
   - Where: [iConsole/Controls/ListBox.cs:103](../../iConsole/Controls/ListBox.cs) (`EnableTypeAhead`, off by default), [iTerminal.Core/Dialogs/ThemeManagerDialog.cs:47](../../iTerminal.Core/Dialogs/ThemeManagerDialog.cs) (the only user). 34 files in `iConsole` and `iTerminal.Core` mention `ListBox<`.
   - Problem: the default is off because dialogs share letters with hot keys, which is right, but then a long list of branches, packages, recent folders or encodings has no jump-by-letter at all, while the file panel does. Heuristic: Efficiency, Consistency with the file panel and `ComboBox` (which has type-ahead with the list closed).
   - Improvement: review the long lists (git branches and stashes, package manager, encoding choice, favourites) and set `EnableTypeAhead = true` on those where the dialog has no letter hot keys; the hint row already lists "Letters". One `/incremental-improvement` per dialog family.
   - Effort: S per dialog. **Unverified**: which dialogs have letter hot keys was not checked.
   - Status: partly fixed, still open. `EnableTypeAhead` is on for the folder selector's Drives, Recent and Places pages (`FolderSelectorDrivesTabPage.cs:69`, `FolderSelectorHistoryTabPage.cs:45`, `FolderSelectorSpecialFolderTabPage.cs:73`). Favourites, the Git client and the package lists are still off.

9. **`ListBox` acts on a single click and on a right-click; `Table` needs a second click** - Severity: **Low**. Kind: consistency.
   - Where: [iConsole/Controls/ListBox.cs:210](../../iConsole/Controls/ListBox.cs) to `:224` (any left or right click runs `SelectListItemCommand`), [iConsole/Controls/Table.cs:208](../../iConsole/Controls/Table.cs) to `:214` (second click on the cursor row activates). `input-and-navigation-controls.md` says the table behaves "the same as `ListBox`"; it does not.
   - Problem: a click on a list row selects it (as Enter does) at once, and a right-click does too, where `Control.ContextMenu` opens a menu on right-click. A stray click on a row that has a default action runs it. Heuristic: Consistency, Error prevention.
   - Improvement: make a first click move the cursor only and a second click on the cursor row raise the command, as `Table` and `VirtualRowsControl` do; leave right-click to `ContextMenu`. Keep a switch (`ActivateOnClick`) for the lists that rely on the old behaviour; correct the documentation line.
   - Effort: S. **Unverified**: dialogs that depend on single-click selection were not searched.

10. **`PopupListView` builds a new `ConxelString` for every row on every draw** - Severity: **Low**. Kind: performance.
    - Where: [iConsole/Controls/PopupListView.cs:156](../../iConsole/Controls/PopupListView.cs); `ListBox` avoids this with `_rowScratch` ([ListBox.cs:42](../../iConsole/Controls/ListBox.cs)).
    - Problem: a combo-box drop-down allocates a list of cells per row per repaint while open. It is small (six rows by default) and below the 200 ms budget by a wide margin, so this is polish only. Heuristic: Performance.
    - Improvement: keep one scratch `ConxelString` and `Reset` it, as `ListBox.Draw` does; do it together with finding 4.
    - Effort: S.

### What works well

- Every row of a `ListBox` is painted every frame, including the blank ones below the items, and `EmptyText` is wrapped with its indent so the end of the message is not cut ([ListBox.cs:251](../../iConsole/Controls/ListBox.cs), `:292`).
- `ListBox.GetColor` shows the cursor row in the "focused" highlight only while the list has focus, so with several lists in a dialog the active one can be seen without colour alone being needed for state (`:351`).
- `ListBoxModel.Update` swaps the rows in one `ReplaceAll`, and `UpdateListItems` is guarded by a lock and a pending flag ([ListBoxModel.cs:76](../../iConsole/Model/ListBoxModel.cs)).
- `ListBox.GetKeyHints` is localised in all six `.resx` files (`Hint.MoveCursor`, `Hint.JumpToLetter`, `Hint.MarkItemUnderCursor`, `Hint.ClearSearch` checked) and hides itself when the list is empty or disabled.
- `AutoCompletion` follows the documented rules: it needs the owner to have focus, it implements `ITransientOverlayHost`, `Hide` closes the popup unconditionally, and a generation counter beats an update already in flight.
- `SearchBox` only consumes Enter while someone listens, so a dialog's default button still gets it ([SearchBox.cs:92](../../iConsole/Controls/SearchBox.cs)).
- `TextBox` has a text clipboard, selection, a placeholder and `MaxLength`; the dialogs that need long inputs raise `MaxLength` themselves (`FolderSelectorPathTabPage.cs:64`, `FindFilesDialog.cs:87`).
- `TextWidth.Of` and `Fit` have an ASCII fast path with no allocation ([TextWidth.cs:76](../../iConsole/Display/TextWidth.cs)).

### Research cross-reference

- **Console survey item 1 (wide-character width):** the console overview says "no width handling was found". That is out of date: `iConsole/Display/TextWidth.cs` exists and `ConxelString`, `BufferLayer`, `AnsiRenderer` and the console drivers use it. The remaining gap is in the controls (finding 4), and `TextWidth` covers only the East Asian Wide and common emoji ranges, not the full Unicode table, by its own comment. The research document was not edited; whoever owns it should reconcile it.
- **Console survey item 2 (bracketed paste):** confirmed not built; ranked higher on usability grounds because a pasted newline acts as Enter in a dialog (finding 5).
- **Item 141 (undo in `TextBox`):** confirmed, `TextBox.cs` has no undo. Not a finding here; it ranks below findings 1 to 5, since a rename box is short. It needs no layout decision.
- **Item 132 (reorderable list):** needs a layout decision first (a move key and a visible grab state in `ListBox`'s row colours); it is not touched by the findings above, but finding 2 (one snapshot per draw) should land before a mutable list is added.
- **Item 12 (trace scopes) and `MainLoopWatchDog`:** the proof for findings 2, 3 and 7 should use these, not wall-clock tests.
- **AI and security overviews:** no item in them needs a layout change in these five controls.

### Suggested order

1. Quick wins: finding 3 (S, `ReplaceAll`), finding 10 together with the popup part of finding 4 (S), finding 6 (S).
2. Medium severity that touches every dialog: finding 1 (theme colours), then finding 2 (one snapshot per draw), then finding 5 (bracketed paste).
3. Then finding 4 for `TextBox`, finding 7, and the polish items 9 and 8.

Nothing in this review is High: no finding loses data or breaks the 200 ms budget in a way that was shown; findings 2, 3 and 7 could, at scale, and should be measured first.

## Recent UI changes: key map, grid queries, evidence notebook, tab progress, folder selector

Scope: the UI-facing features that landed on `main` after the first design review, taken as one area because each of them adds a new way to reach or change something the user already does with the keyboard. They are: the user key map `keymap.json` (#673), the shared grid query language and AI draft in the CSV, Parquet, HDF5 and SQLite viewers (#675), the evidence notebook panel (#674), tab progress with OSC 9;4 (#684), and the folder selector changes (#668, #672, #677, #679, #681, #682, #683). Reviewed on 2026-11-20 at commit 28715f26 on branch `feature/pdf-signature-verification` (one commit ahead of `main` at `fcc5640e`; the extra commit is the PDF signature viewer and is out of scope). The working tree held uncommitted edits to `AutoCompletion.cs`, three folder selector tab pages and their tests, which were noted where they touch a finding. Read: `CLAUDE.md`, `ui-framework.md`, `key-patterns.md` (including the key map section), `panels-and-drives.md` (evidence section), `dialogs.md` (grid query section), the three research overviews, `tui-library-comparison.md` (items 1, 25, 112 and 129 only), the diffs of the commits above, and the source of `KeyMapFile`, `KeyMapStartupHostedService`, `KeyMap`, `KeyBindingRules`, `HotKeys`, `HelpDialog`, `EvidenceCommands`, `ContentModelEvidenceNotebook`, the four viewers, `GridQuery*`, `GridArray`, `CsvGrid`, `TerminalProgress*`, `TaskbarProgress*`, `SplashDialog` and the folder selector tab pages. Status was last checked at commit 74a41e86 with the working tree at that time. Not done: no app launch, no tests run, no render dump, no key trace. Findings that rest on reading alone are marked **unverified**.

### Summary

- Ctrl+Shift+X was bound twice (finding 11); that is now fixed. The biggest open problem is the key map's edges: footer buttons and the F1 start list keep the compiled-in key text after a remap, a binding onto another command's default key is accepted silently, and the reason a binding was refused is only in the log (findings 12 to 14).
- The shared query line is a good, honest design (queries run in the background, a draft never runs without Enter). The 256-character limit is fixed (finding 15); the query language is still hard to learn from inside the dialog (finding 16).
- The evidence notebook adds files and voice recordings with little feedback while work runs, and it is reachable only by two chords and the palette (findings 17 to 19).
- Wins: OSC 9;4 is small, off by default outside Windows Terminal, and survives terminal hand-over; the folder selector's new empty states and the y/n question on Favourites follow the project rules.

### Findings

12. **After a remap, the footer buttons and the F1 start list still show the old key** - Severity: **Medium**. Kind: consistency.
    - Where: [iTerminal.Core/Controls/StatusBar/HotKeys.cs:211](../../iTerminal.Core/Controls/StatusBar/HotKeys.cs) (the title is a literal, `"F5-Copy"`; `LocalizeButtons` only swaps the label after the last dash), `:581` (the layer a button shows on is now chosen from `ActiveKey`), [iConsole/Dialogs/HelpDialog.cs:134](../../iConsole/Dialogs/HelpDialog.cs) to `:139` (F1, Shift+F1, Tab, Enter, Esc are typed text). The commit message says the footer reads `ActiveKey`; only the choice of layer does.
    - Problem: `CopyCommand` mapped to `Ctrl+F5` appears on the Ctrl layer of the footer as `F5-Copy`, so the footer names a key that now does nothing. A command mapped to `none` disappears from the footer, which is right. The F1 start list states keys that may have been moved. Heuristic: Consistency, Feedback.
    - Improvement: build the footer title from `ActiveKey` (`Command.FormatKey` plus the localised label) when the button is laid out, keeping the literal as the fallback; build the F1 start lines from the commands' `KeyDisplayText` for F1 and Shift+F1. Add a `ScreenBuffer` test next to `iTerminal.Test/KeyMapTest.cs` that reads the footer after a remap.
    - Effort: M. **Unverified**: no render was taken; the title literal and the layer test were read.

13. **A binding onto another command's default key is accepted without a word** - Severity: **Medium**. Kind: usability (error prevention).
    - Where: [iTerminal.Core/Services/KeyBindings/KeyMapFile.cs:105](../../iTerminal.Core/Services/KeyBindings/KeyMapFile.cs) to `:113` (only the file's own keys are checked), `key-patterns.md` ("Known limitation", line 119). Tui comparison item 25 asks for "a keymap file with a conflict report".
    - Problem: `"CopyCommand": "F8"` is accepted, and Delete then competes with Copy for F8; the router picks one and the other becomes unreachable, with no message at start-up or later. This is the same failure as finding 11, created on purpose by the user. Heuristic: Error prevention and recovery.
    - Improvement: after the first layout, walk the tree with the logic of `CommandCatalog.Walk` (it already tracks `keysTaken` per control) and report each `ActiveKey` shared by two commands that can run in the same control chain, through the same notification the start-up service uses. No registry of defaults is needed if the check runs on the built tree.
    - Effort: M.

14. **Writing `keymap.json` needs C# type names the app never shows, and the reasons for a refusal are only in the log** - Severity: **Medium**. Kind: usability (discoverability, error recovery).
    - Where: [KeyMapFile.cs:72](../../iTerminal.Core/Services/KeyBindings/KeyMapFile.cs) (a command is named by its type name), [KeyMapStartupHostedService.cs:190](../../iTerminal.Core/Services/KeyBindings/KeyMapStartupHostedService.cs) to `:200` (`KeyBindingProblem.Reason` goes to `_logger.Warning`; the toast carries only the names and "The log says why"). The help and palette screens list titles and keys, not type names. No menu entry or palette command opens or creates the file.
    - Problem: a user who wants F5 on another key must find `CopyCommand` in the source tree, guess the file's folder, restart to try it, and read the log (whose location is not stated in the toast) to learn why Ctrl+M was refused. Heuristic: Discoverability, Error recovery ("errors say what to do next").
    - Improvement: add a palette command "Write key map template" that writes `keymap.json` with every current binding (use `CommandCatalog.Collect` for command, `FullName`, `ActiveKey`), and put the first refusal's reason in the toast text (the strings already exist in `KeyBindingRules`; localise them with `UiText.Get`). Name the log folder in the toast.
    - Effort: M.

16. **The query language cannot be learned from inside the viewers** - Severity: **Medium**. Kind: usability (discoverability, efficiency).
    - Where: [CsvViewerDialog.cs:71](../../iConsole/Dialogs/CsvViewerDialog.cs) (placeholder) and `:146` (the box is filled with `SELECT * FROM file` at once, so the placeholder is never seen), [ParquetViewerDialog.cs:405](../../iConsole/Dialogs/ParquetViewerDialog.cs) and [HdfViewerDialog.cs:715](../../iConsole/Dialogs/HdfViewerDialog.cs) (`GetKeyHints` lists Enter and `?`, not the operators), [GridQuery.cs:13](../../iConsole/Tabular/GridQuery.cs) (the only place `LIKE`, `CONTAINS`, `IS EMPTY`, quoting rules and "no parentheses" are written down), [CompletionModel.cs:21](../../iConsole/Model/CompletionModel.cs). Tui comparison item 112 (fuzzy and highlighted completion) is related.
    - Problem: the CSV and SQLite boxes open with text, so the example query is hidden; Parquet and HDF5 open empty and show one. Nobody is told that text goes in single quotes, that a name with spaces needs double quotes or brackets, or that there are no parentheses, until a parse error says so. The column names are known to the dialog but are not offered while typing. Heuristic: Discoverability, Consistency (CSV and Parquet differ), Efficiency.
    - Improvement: add one shared `GetKeyHints` block in a small helper (`GridQueryHints`) used by all four viewers: operators, quoting, `?` and the `FROM cells` / `AT` clauses where they apply. Make the first run show the example in the status line instead of the placeholder. Then add a token-aware `ICompletionModel` (column names and keywords for the word before the cursor) to `WithAutoCompletion`; `AutoCompletion` currently replaces the whole line, so it needs a "replace the last word" option first.
    - Effort: S for the hints, M for completion.
    - Status: partly fixed, still open. `GridQueryCompletionModel.Hints()` supplies an operator hint and a Tab hint to the viewers' F1 help, and Tab now completes a column name or keyword. The CSV and SQLite boxes still open filled with `SELECT * FROM file`, so the placeholder example is still not seen, and quoting rules are not in the hints (**unverified**: the hint text at `GridQueryCompletionModel.cs:81` was read).

17. **"Add evidence" copies and hashes whole files with no progress and no way to stop** - Severity: **Medium**. Kind: usability (feedback), performance.
    - Where: [EvidenceCommands.cs:302](../../iTerminal.Core/Commands/Evidence/EvidenceCommands.cs) to `:352` (a bare `Task.Run`, one toast when all files are done), `panels-and-drives.md` ("A file is read once... hashed as written"). OSC 9;4 and the taskbar read only `TaskManager` tasks (`TaskbarProgressCalculator`).
    - Problem: video or disk-image evidence can take minutes. The panel stays unchanged, the tab shows no progress, nothing says which file is being read, Esc does nothing, and a second Ctrl+Shift+X can start a second copy at the same time. Heuristic: Feedback and visibility of status, Efficiency, Keyboard (cancel).
    - Improvement: run the copy as a `BackgroundTask` through `TaskManager` (as `CopyDialogCommand` does), report bytes read as progress and honour a cancellation token in `AddFileAsync`. The tab indicator from #684 then works for free.
    - Effort: M.

18. **Voice recording: the prompt does not move, and the 60 second limit is silent** - Severity: **Medium**. Kind: usability (feedback).
    - Where: [EvidenceCommands.cs:454](../../iTerminal.Core/Commands/Evidence/EvidenceCommands.cs) to `:461` (static text "Recording since HH:mm:ss"), [iTerminal.Core/Speech/PcmBuffer.cs:13](../../iTerminal.Core/Speech/PcmBuffer.cs) (bytes past `Pcm.MaxBytes` are dropped), [Pcm.cs:10](../../iTerminal.Core/Speech/Pcm.cs) (`MaxSeconds = 60`).
    - Problem: the microphone is live behind a text prompt that shows a start time, not an elapsed time or a level. After 60 seconds the buffer stops growing, but the prompt still says "recording", so a speaker keeps talking into nothing. Heuristic: Feedback and visibility of status, Error prevention.
    - Improvement: show elapsed seconds and "of 60" in the prompt text (the voice input in the AI bar already shows a live state; reuse its label), and end the prompt with a message when the cap is reached.
    - Effort: S to M. **Unverified**: how often the prompt text is redrawn was not traced.

19. **The evidence notebook is reachable only by two chords and the palette; Ctrl+Shift+Q may be a terminal host's close key** - Severity: **Medium**. Kind: usability (discoverability).
    - Where: [EvidenceCommands.cs:111](../../iTerminal.Core/Commands/Evidence/EvidenceCommands.cs), `panels-and-drives.md` ("Not yet done: a Places entry and menu items"; Verify is "palette only"), [ToolsMenuItem.cs:139](../../iTerminal.Core/Controls/MenuBar/ToolsMenuItem.cs) (menu layout for comparison).
    - Problem: the feature has no menu entry or Places row, so a new user does not find it. The panel itself shows only the cursor row's origin in the footer. Ctrl+Shift+Q is, in some Linux terminal emulators, the default "close window" key (**unverified**: external; confirm in GNOME Terminal and Konsole), and `key-patterns.md` already asks for chords the host leaves alone.
    - Improvement: add Tools menu entries for Open, Add and Verify (`MenuShortcutTest` then checks the labels against the keys), and a Places row for `evidence://`. Check the chord with a host that reserves it; pick another if it does.
    - Effort: S for menu entries.

20. **CSV and Parquet queries copy every cell through the array layer, and cannot be cancelled** - Severity: **Medium**. Kind: performance.
    - Where: [GridQueryRun.cs:54](../../iConsole/Tabular/GridQueryRun.cs) (`CancellationToken.None`; the comment says the source "must be in memory"), [GridArray.cs:290](../../iConsole/Tabular/GridArray.cs) to `:337` (`QueryRowsAsync` builds a flat cell list per block, then a new `string[]` per row, then filters), `:510` to `:522` (`InMemoryGridArray.ReadBlockAsync` adds each cell to a `List<string>` with no capacity), [CsvViewerDialog.cs:221](../../iConsole/Dialogs/CsvViewerDialog.cs) (`StartQuery`), [CsvDocument.cs:17](../../iConsole/Csv/CsvDocument.cs) (500 000 rows).
    - Problem: before #675 the CSV query filtered the existing rows; now each Enter on a 500 000-row, 20-column file builds roughly ten million cell references and 500 000 new row arrays before filtering, and a second Enter starts another full pass while the first still runs. It is off the key handler, so it stays inside the 200 ms budget, but memory and CPU rise with every press and the older pass cannot be stopped. Heuristic: Performance, Efficiency.
    - Improvement: for `InMemoryGridArray`, let `GridArrayView.QueryAsync` take the rows directly (the old `GridQuery.Run(headers, rows, text)` path still exists in `GridQueryRun.Start`), or pass a token from the dialog (`Close`, new query) and check it per block. At least size the list in `ReadBlockAsync`.
    - Effort: S to M. **Unverified**: no timing; confirm with a `RecordingStopwatchFactory` test over 500 000 rows.
    - Status: partly fixed, still open. `GridQueryRun.RunOverAsync` takes a `CancellationToken` ([GridQueryRun.cs:57](../../iConsole/Tabular/GridQueryRun.cs)), and the CSV dialog cancels the previous query on a new Enter and on an encoding switch, so an older pass no longer keeps running. The per-cell copy in `GridArray` was not re-read: that claim comes from the commit message, so the cost part stays open.

23. **The CSV file is decoded inside `OnInitializeAsync`, before the background parse** - Severity: **Low**. Kind: performance.
    - Where: [CsvViewerDialog.cs:119](../../iConsole/Dialogs/CsvViewerDialog.cs) to `:135` (`ReadFileAsync`, then `TextEncodings.Decode(content, ...)` outside `Task.Run`; the dialog keeps `_bytes`, the string and the rows).
    - Problem: for a file near the 500 000-row cap the decode allocates a string of twice the file size on the calling thread, and three copies stay in memory. Not changed by #675, but the dialog is now the entry to four more features. Heuristic: Performance (content load under 3 000 ms).
    - Improvement: move `Decode` into the `Task.Run` that parses, and keep only the bytes (the string is rebuilt for F8 anyway).
    - Effort: S. **Unverified**: which thread `OnInitializeAsync` runs on, and the timing, were not checked.

24. **AI messages are English literals and name a config key** - Severity: **Low**. Kind: localization, usability.
    - Where: [iTerminal.Core/AI/GridQueryAssistant.cs:17](../../iTerminal.Core/AI/GridQueryAssistant.cs) (`"AI is off (AI:Provider)"`), [iConsole/Tabular/GridSchema.cs:36](../../iConsole/Tabular/GridSchema.cs) (`"AI is not set up"`). The other `Grid.Ai.*` messages use `UiText.Get`.
    - Problem: in German or Hungarian the status line mixes languages, and "AI:Provider" tells the user a setting name without saying where to change it. "Asking the AI model…" has no cancel other than closing the dialog. Heuristic: Consistency, Error recovery.
    - Improvement: move both strings to `UiText.Get` in all six `.resx` files, and say "turn it on in Settings" with the page name used by the settings dialog.
    - Effort: S.

25. **Folder selector rows are shortened by characters, once, at the width known when the row was built** - Severity: **Low**. Kind: usability (resilience to size).
    - Where: [FolderSelectorHistoryTabPage.cs:70](../../iTerminal.Core/Dialogs/FolderSelectorHistoryTabPage.cs), [FolderSelectorSpecialFolderTabPage.cs:182](../../iTerminal.Core/Dialogs/FolderSelectorSpecialFolderTabPage.cs), [FolderSelectorFavouritesTabPage.cs:266](../../iTerminal.Core/Dialogs/FolderSelectorFavouritesTabPage.cs), [StringHelper.cs:176](../../iConsole/Helper/StringHelper.cs) (`Shrink` keeps the first and last chars and drops the middle).
    - Problem: the new "Name  Path" label (#679, #681) is good, but `Shrink` runs inside `TransformItem` with `_listBox.ViewSize.Width` at that moment, so a terminal resize leaves old cuts, and a CJK folder name is counted as one cell per glyph. Heuristic: Resilience to size and content.
    - Improvement: shorten at draw time (a `ListBox` that takes a `Func<T, int, string>`), or rebuild the rows from `OnInitializeLayout` when the width changes; use `TextWidth.Fit`.
    - Effort: S to M. **Unverified**: a resize was not simulated.

26. **The Drives page changes its empty text without asking for a repaint** - Severity: **Low**. Kind: usability (feedback).
    - Where: [FolderSelectorDrivesTabPage.cs:91](../../iTerminal.Core/Dialogs/FolderSelectorDrivesTabPage.cs) to `:95` (`OnIdle` sets `EmptyText` every pass), [ListBox.cs:60](../../iConsole/Controls/ListBox.cs) (a plain auto-property, no `StateHasChanged`).
    - Problem: when the first drive pass ends with no drives, the page can keep saying "Reading the drives…" until something else repaints it. With drives present the list fills and repaints, so this shows only on a machine with none. Heuristic: Feedback.
    - Improvement: call `StateHasChanged()` in `OnIdle` only when the chosen text changes.
    - Effort: S. **Unverified**.

27. **Two small wording and layout inconsistencies on the folder selector** - Severity: **Low**. Kind: consistency.
    - Where: [FolderSelectorFolderTreeTabPage.cs:44](../../iTerminal.Core/Dialogs/FolderSelectorFolderTreeTabPage.cs) (`Indexing... {0} folders so far`, three dots) against `FolderSelector.Drives.Reading` (`…`, one glyph; about 124 strings use `…` and 27 use `...`), [FolderSelectorPathTabPage.cs:213](../../iTerminal.Core/Dialogs/FolderSelectorPathTabPage.cs) to `:217` (when the schemes hint does not fit, the last visible row is replaced by `...`, so a whole row of schemes disappears).
    - Problem: mixed ellipsis styles, and a hint that hides schemes rather than the tail of the last row. Heuristic: Consistency, Accessibility (a basic terminal may lack `…`).
    - Improvement: choose one ellipsis for new strings; end the last visible scheme row with `…` after its words, not instead of them.
    - Effort: S.

28. **Tab progress is invisible to anyone who does not know the environment variable** - Severity: **Low**. Kind: usability (discoverability).
    - Where: [iConsole/Services/TerminalProgress.cs:52](../../iConsole/Services/TerminalProgress.cs) (`WT_SESSION` or `ITERMINAL_PROGRESS=on`), [TerminalProtocols.cs:28](../../iConsole/Services/TerminalProtocols.cs) (shown on the splash for three seconds).
    - Problem: users of WezTerm, Ghostty or ConEmu that take OSC 9;4 get nothing unless they know the variable, and the only place the state is shown is the splash. Heuristic: Discoverability.
    - Improvement: add a `Progress.Mode` setting (auto, on, off) read by `TerminalProgressSequences.SupportsProgress` through `ISettingsService`, and show the line in System Information. Keep the environment variable as the override.
    - Effort: S. **Unverified**: which terminals take the sequence is from the survey, not tested.

### What works well

- The grid query runs on a background task and only the newest result lands on the idle pass ([CsvViewerDialog.cs:239](../../iConsole/Dialogs/CsvViewerDialog.cs), [GridQueryRun.cs](../../iConsole/Tabular/GridQueryRun.cs)); a refused query keeps the old rows and says why on the status line. The status line states "x of y rows" and says when a cap cut the result.
- The AI draft is checked against the real column names before it is shown, it lands in the query line, and it runs only on Enter; no cell value is sent (`GridQueryAssistant.UserMessage`).
- `KeyBindingRules` refuses exactly the chords `CLAUDE.md` forbids (Alt+printable, Ctrl+Alt, Ctrl+M, Esc, Tab), `Parse` skips a bad binding and applies the rest, and the map is an immutable snapshot swapped once, so key handling stays cheap ([KeyMap.cs](../../iConsole/Commands/KeyMap.cs)).
- `TerminalProgressService` writes nothing where the terminal does not take the sequence, remembers the last state across `ConsoleOwnership` hand-over, and `TaskbarProgressJob` clears it on stop.
- The Favourites page asks before removing, defaults to "no", and drops the question on Esc as the first of two presses through `ITransientOverlayHost` ([FolderSelectorFavouritesTabPage.cs:123](../../iTerminal.Core/Dialogs/FolderSelectorFavouritesTabPage.cs)); the delete runs only if the cursor still rests on the favourite the question named.
- The Drives page now tells "still reading" from "none", and the folder tree says "Indexing... N folders so far" while the walk has folders queued.
- `FolderListCodec` keeps a folder name with `;` whole and reads old values unchanged.
- The evidence drive is read-only and its writes throw, every outcome of an add is a toast with a severity, and the commands guard against a second prompt (`_asking`).
- All new strings checked have keys in the six resource files (`KeyMap.*`, `Evidence.*`, `Grid.*`, `FolderSelector.Favourites.*`).

### Research cross-reference

- **Tui comparison item 25 (pluggable key bindings):** now built (`KeyMap`, `KeyMapFile`), but the document and `ui-framework.md` item 5 still say `Command.Key` is fixed. The "conflict report" half is not built (finding 13). `mc-parity.md` item 11 asks for presets; a template writer (finding 14) is the first step. The research document was not edited; whoever owns it should reconcile it.
- **Tui comparison item 129 (OSC 9;4):** says "not found"; it is built (`TerminalProgressService`). Finding 17 adds the usability angle that long file copies must be `TaskManager` tasks to use it.
- **Console survey item 2 (bracketed paste):** raised in priority by finding 5 (a multi-line paste presses Enter) and by finding 15, whose length cap is fixed, so a pasted query is no longer cut at 256 characters.
- **Tui comparison item 112 (fuzzy and highlighted completion) and the `CompletionModel` limits:** finding 16 needs a token-aware completion model first; `CompletionModel` matches the whole line.
- **Console overview item 137 (export of table data):** `dialogs.md` lists "saving a result" as not built for the grid viewers; the query line is now the place that gains most from it. Needs a key decision (which dialog key) before building.
- **Console overview item 140 (data profile of a table):** not built; the shared `GridArrayView` would be the natural source. No layout decision is needed until grouping exists in the language.
- **Console survey item 1 (wide characters):** findings 21 and 25 extend finding 4 to the grid and the selector rows (the grid is fixed, the selector rows are open).
- **Security overview, RFC 3161 timestamp for evidence:** needs a layout decision first (where a time-stamp token and its check result appear on an evidence row); the footer line is already long (see finding 19 and `ContentModelEvidenceNotebook.GetFooter`).
- **AI overview, prior record S (natural-language query):** partly built through `GridQueryAssistant`; structured output (item 1 there) would remove the text cleaning in `GridQueryDrafter.Clean`.

### Suggested order

1. Quick wins: finding 24 (S, AI message strings), finding 16 hints (S), finding 19 menu entries (S).
2. Medium: findings 12, 13 and 14 as one key-map follow-up (footer labels, conflict check, template and reasons), then 17 (evidence progress through `TaskManager`), 20 (finish the query cost part), 18 (voice prompt), 16 (completion).
3. Low: 23, 25, 26, 27, 28.

## Feedback controls, dialog shell, and the Git, remote, palette and file panel screens

Scope: the controls that report or confirm something (`Toast`, `ToastHost`, `ProgressWindow`, `Button`, `CheckBox`, `Table<T>`, the `Dialog` shell), and four heavily used iTerminal screens (the Git client, the remote connection manager, the command palette with the language dialog, and the file panel's footer and marks). It also covers the UI-facing commits after the second pass: folder completion from disk (#697, `DirectoryCompletionModel`) and the query viewer fixes (#696). Reviewed on 2026-11-20 at commit 74a41e86 on branch `feat/directory-completion`. `main` had moved on to `df3d2ad1` (#697, the same work as the two branch commits plus #696); the working tree's edits to the query viewers match #696 and were read as shipped code. Read: `CLAUDE.md`, `key-patterns.md`, `ui-framework/notifications.md`, `ui-framework/input-and-navigation-controls.md`, the first two reviews in full, the commit stats of every commit since `fcc5640e` (`a022af8d`, `6e8fc104`, `66dc1787`, `53321020`, `df3d2ad1`, `dc157228`, `74a41e86`), the console, AI and security research overviews (tables and suggested orders; `tui-library-comparison.md` by grep for the items cited below), and the source of `Dialog`, `Toast`, `ToastHost`, `NotificationService`, `Table`, `Button`, `CheckBox`, `ColorScheme`, `ProgressWindow`, `GitClientDialog` (the key handling, the actions and `RunAction`), `RemoteConnectionManagerDialog` (keys, edit form, drawing), `CommandPaletteDialog`, `CommandCatalog`, `LanguageDialog`, `SettingsDialog`, `ContentBrowser.Draw`, the marks and footer code of `ContentModelBase`, `DirectoryCompletionModel` and `AutoCompleteCommand`. Not done: no app launch, no tests run, no render dump, no key trace, no timing. Everything that rests on reading alone is marked **unverified**.

### Summary

- Biggest problems are about what happens when something goes wrong or is pressed by mistake. Esc on the progress window cancels a running copy, move or delete with no question (finding 29). A failed operation's toast can be neither read in full nor dismissed from the keyboard, and the notification history that exists has no screen (finding 30). In the Git client, Caps Lock turns pull into push and stage into stash, and push and abort-merge never ask (finding 31).
- The file panel does a full scan of the listing in every `Draw` as soon as one entry is marked (finding 33), and the mark itself is shown by colour only (finding 34).
- The remote connection manager's hint says D and Delete confirm a delete; the code asks again instead (finding 35), and its hand-drawn edit form cannot move the cursor inside a field (finding 36).
- Wins: the new `DirectoryCompletionModel` is a model of how to do background I/O in a text box (generation order, cache, timeout, `ReplaceAll`); the progress window, palette and Git client all keep their list areas fully painted and say why a list is empty; destructive Git keys that exist ask first.
- Earlier findings: findings 11, 15, 21 and 22 are fixed (see Resolved). Finding 20 is only partly fixed (cancellation works; the per-cell copy was not re-read) and stays open.

### Findings

29. **Esc on the progress window cancels the running operation without asking, and the keyboard has no key that only dismisses it** - Severity: **Medium**. Kind: usability (error prevention, keyboard).
    - Where: [iTerminal.Core/Controls/PopUpWindows/ProgressWindow.cs:30](../../iTerminal.Core/Controls/PopUpWindows/ProgressWindow.cs) to `:46` (`CancelOrCloseCommand`, `Key` = Esc at `:34`, calls `CancellationTokenSource.Cancel()` while `Started`), `:89` to `:92` (the `[X]` action button is the "dismiss and let it run" path), [iConsole/Commands/CloseParentCommand.cs:7](../../iConsole/Commands/CloseParentCommand.cs) (the `[X]` command is bound to Enter). `key-patterns.md` ("The copy/move/delete dialogs close on Enter") says the operation is "not awaited, and the panel is usable while it runs"; `notifications.md` speaks of a user who "closed it to get on with something else".
    - Problem: the window is modal. Esc is the key every other dialog uses for "close this", and here it stops a move or delete part way, leaving files split between source and target. The documented way to get on with other work is the `[X]` button, which is a mouse target; whether Enter reaches `CloseParentCommand` while the Cancel button holds focus was not traced. The window's footer lists no keys, so nothing says which of the two Esc and Enter does. Heuristic: Error prevention, Consistency (Esc closes one layer in `Dialog`), Keyboard and focus.
    - Improvement: keep Esc as "dismiss, task keeps running" (what `Dialog` users expect) and make Cancel a button, a hot key (`HotKeyChar = 'c'` already exists on it) or an Esc-then-y/n question on the button row, in the project's footer style. Show `Esc: run in background  C: cancel` in the window. The task manager already lists a dismissed task. Add a `ConsoleAppDriver` test that Esc on a long copy leaves the task running.
    - Effort: S to M. **Unverified**: the Enter path and the effect of a cancelled move on the source were read, not run.
    - Status: open (spot-checked at 3ba7401f).

30. **A failure toast is cut at three lines, lasts ten seconds, cannot be dismissed from the keyboard, and the history that keeps it has no screen** - Severity: **Medium**. Kind: usability (feedback, error recovery).
    - Where: [iConsole/Controls/Toast.cs:20](../../iConsole/Controls/Toast.cs) and `:71` to `:75` (`MaxMessageLines` = 3, the rest becomes `…`), `:132` to `:147` (only a click dismisses), [ToastHost.cs:180](../../iConsole/Controls/ToastHost.cs), [iConsole/Services/NotificationService.cs:37](../../iConsole/Services/NotificationService.cs) (`History`, capped at 100) and `:137` (`DismissAll`); a search of `iConsole`, `iTerminal.Core` and `iTerminal` finds no reader of `History` and no caller of `DismissAll` outside tests. `notifications.md`: "It never takes focus or a key"; errors stay 10 s. Console survey item 108 (toast actions) is the related research.
    - Problem: the finished-operation toast is "the only place" a failed copy that the user closed the window on is reported (`TaskNotificationHelper`). A long error such as a path plus an exception text is cut mid-sentence, disappears in ten seconds, and the user who was looking at the other panel cannot call it back. A keyboard-only user cannot clear a stack of four toasts that covers the top-right of the panel. Heuristic: Feedback and visibility, Error recovery, Keyboard.
    - Improvement: add a "Notifications" list (palette command and Tools menu entry) that shows `INotificationService.History` in a `ListBox` with the full text, severity mark and time, plus a "Dismiss all toasts" command calling `DismissAll`. Reuse the severity marks from `Toast.SeverityMark`. Keep the toast itself non-modal.
    - Effort: M.
    - Status: open (spot-checked at 3ba7401f: `History` still has no reader outside the service and tests).

31. **In the Git client Caps Lock turns pull into push, stage into stash and commit into amend; push and abort-merge never ask** - Severity: **Medium**. Kind: usability (error prevention).
    - Where: [iTerminal.Core/Dialogs/Git/GitClientDialog.cs:1053](../../iTerminal.Core/Dialogs/Git/GitClientDialog.cs) to `:1082` (`switch (keyEvent.KeyChar)` is case-sensitive: `'p'` pull, `'P'` push, `'S'` stash, `'C'` amend, `'A'` abort merge), `:898` (`Push`), `:888` (`AbortMerge`), against `:884` (`BeginDeleteBranch` asks y/n) and `:1241` (commit shows a prompt first).
    - Problem: the dialog tells letters apart only by `KeyChar` case. With Caps Lock on, `p` arrives as `P` and publishes the branch to the remote with no question; `a` during a merge becomes `A` and drops the merge state. Shift+D already asks, so the dialog's own rule ("destructive keys ask first") is applied to some capital letters and not the others. Heuristic: Error prevention, Consistency.
    - Improvement: ask a y/n in the footer for Push (`BeginConfirm`, text "Push {branch} to {upstream}? (y/n)") and for Abort merge, as Delete branch does; or require `keyEvent.Shift` for the capital actions, so Caps Lock alone cannot reach them. Add a key-handler test with `KeyChar = 'P'` and `Shift = false`.
    - Effort: S. **Unverified**: whether the Windows and Posix input plug-ins report `Shift = false` with an upper-case `KeyChar` under Caps Lock was not checked (`ITERMINAL_KEY_TRACE=1` would show it).
    - Status: open (spot-checked at 3ba7401f: `GitClientDialog.cs:1061` still switches on `'P'`).

32. **Git client keys pressed while an action runs are dropped without a word** - Severity: **Medium**. Kind: usability (feedback).
    - Where: [GitClientDialog.cs:780](../../iTerminal.Core/Dialogs/Git/GitClientDialog.cs) to `:785` (`RunAction` returns silently when `_busy`), `:712` to `:717` (`ToggleStage` the same), `:804` to `:805` (`_busy` is cleared before `RefreshAllAsync`, so the window is the `git add` itself).
    - Problem: staging is "the key pressed most often in the dialog" (the comment at `:733`). Pressing Space, Down, Space quickly stages the first file and silently ignores the second press, which looks like a successful stage until the file is still in the Unstaged list. There is no beep, no "busy" text. Heuristic: Feedback and visibility, Efficiency.
    - Improvement: queue one pending action (keep the latest key press and run it when `_busy` clears), or at least call `IBeepService` / `SetMessage("Working…")` for a dropped key. Staging several files in a row should work at typing speed.
    - Effort: S to M. **Unverified**: the time one stage takes on a real repository was not measured.

33. **Once one entry is marked, every file panel `Draw` scans the whole listing** - Severity: **Medium**. Kind: performance.
    - Where: [iTerminal.Core/Controls/Contents/ContentBrowser.cs:525](../../iTerminal.Core/Controls/Contents/ContentBrowser.cs) (`GetFooter()` in `Draw`), [iTerminal.Core/Model/ContentModels/ContentModelBase.cs:250](../../iTerminal.Core/Model/ContentModels/ContentModelBase.cs) to `:256` (`EntryCountFooter` calls `GetMarkedEntries()`, then sums sizes) and `:387` (`FilteredEntries.Where(IsMarked)`), `IsMarked` at `:303` to `:315` (a `TrimEnd` string and a lock per entry). `MarkedCount` (`:281`) does the same and is read by `ClearMarksCommand.CanExecute` ([ClearMarksCommand.cs:12](../../iTerminal.Core/Commands/Marks/ClearMarksCommand.cs)), which the command palette and F1 help call for every command.
    - Problem: with no marks the footer is cheap (`_marks.Count == 0` returns early). With one mark in a folder of 100 000 entries, every repaint of either panel (each cursor move) allocates a trimmed string and takes a lock per entry, before drawing 30 rows. This is a frame-budget risk exactly when a user is doing the heavy task (marking many files). Heuristic: Performance, Resilience to content.
    - Improvement: cache `(markedCount, markedBytes)` in the model and invalidate it where the marks or the entries version change (`SetMark`, `ClearMarks`, the listing swap that already moves the entries version), and have `EntryCountFooter` and `MarkedCount` read the cache. Assert with a model whose `FilteredEntries` counts reads that two draws read the listing once.
    - Effort: S to M. **Unverified**: no timing taken; confirm with `RecordingStopwatchFactory` over 100 000 entries.

34. **A marked row differs from an unmarked one only by colour** - Severity: **Medium**. Kind: accessibility.
    - Where: [ContentBrowser.cs:940](../../iTerminal.Core/Controls/Contents/ContentBrowser.cs) to `:950` (`PaintMarked`: yellow bold, dark red under the cursor), [ContentModelBase.cs:253](../../iTerminal.Core/Model/ContentModels/ContentModelBase.cs) (the footer count "N marked" is the only text cue).
    - Problem: the marks are what F5, F6 and F8 act on. On a terminal where bold yellow is close to the normal text colour (a monochrome or high-contrast palette, a light terminal background), the user cannot see which rows will be deleted. mc and Far give marked rows an extra glyph or a different background. Heuristic: Accessibility (no colour-only meaning), Error prevention.
    - Improvement: prefix marked rows with a glyph in the row's first cell (`*`, as mc does; `FormatItemName` already owns the prefix) or add an underline; keep the colour. Add a `ScreenBuffer` assertion that a marked and an unmarked row differ in text, not only colour.
    - Effort: S to M (column alignment of the name).

35. **The remote manager's delete hint lies: D and Delete re-ask, only y deletes** - Severity: **Medium**. Kind: consistency, usability.
    - Where: [iTerminal.Core/Dialogs/Remote/RemoteConnectionManagerDialog.cs:136](../../iTerminal.Core/Dialogs/Remote/RemoteConnectionManagerDialog.cs) to `:140` (F1 hint "Y / D / Delete: Delete the connection", "Any other key: Keep"), `:183` to `:187` (d and Delete keep the question alive), `:206` to `:207` and `:224` to `:226` (both call `AskDelete()` again), `:227` (`'y' when _confirmDelete` is the only delete). The footer text at `:713` says only "y delete". The tests cover `d` then `y` and `d` then Down ([RemoteConnectionManagerDialogTest.cs:230](../../iTerminal.Core.Test/Dialogs/Remote/RemoteConnectionManagerDialogTest.cs) to `:255`), not `d` then `d`.
    - Problem: the F1 help promises a second D or Delete confirms, as the Git client's and package manager's prompts do not; the user presses D twice and sees the same question again. Not data loss, but a confirmation that does not answer to the key it names is a trap in the one dialog that stores credentials. Heuristic: Consistency, Error prevention (the hint must match the key).
    - Improvement: change the hint to "Y: delete" (and keep "any other key keeps it"); or make a second D/Delete confirm and say so in the footer. Pick one, and add the `d`, `d` test.
    - Effort: S.

36. **The connection edit form is hand-drawn: no cursor movement inside a field, no copy, and Esc throws the whole form away** - Severity: **Medium**. Kind: usability, consistency.
    - Where: [RemoteConnectionManagerDialog.cs:353](../../iTerminal.Core/Dialogs/Remote/RemoteConnectionManagerDialog.cs) to `:364` (typing appends to `field.Text`, Backspace removes the last char), `:240` to `:246` (Esc returns to the list without asking), `:264` to `:294` (its own paste, delete-word and Ctrl+U), `:776` to `:820` (`DrawForm`, characters counted by `.Length`; `Put` at `:823` clips by `.Length`). `key-patterns.md` ("Text boxes and the editor have a text clipboard and selection") and tui comparison item 72 (form bound to a model) describe the building blocks that exist.
    - Problem: a typo in the middle of a host name or key-file path can only be fixed by deleting back to it; Left, Right, Home, End, Shift-select and Ctrl+C do nothing. Esc after typing eight fields drops them silently. The password field shows `*` per character, so its length is visible. Heuristic: Consistency (every other form uses `TextBox`), Error prevention, Efficiency.
    - Improvement: build the form from `FormPanel` with `TextBox`, `PasswordBox` and a `ComboBox` or `SegmentedControl` for type and transport, as the Compress dialog does; ask `Discard changes? (y/n)` on Esc only when a field changed. The dialog's tests use `StatusForTest` and the key paths, so keep those seams.
    - Effort: M to L.

37. **Focus on a `Button` or `CheckBox` is shown by a foreground colour change only, and the default scheme makes it low contrast** - Severity: **Medium**. Kind: accessibility.
    - Where: [iConsole/Controls/Buttons/Button.cs:164](../../iConsole/Controls/Buttons/Button.cs) to `:168` (Focused: `SelectedFocusedForeground` on the unchanged `ButtonBackground`), [iConsole/Controls/CheckBox.cs:121](../../iConsole/Controls/CheckBox.cs) to `:127` (the same on the plain background), [iConsole/Display/ColorScheme.cs:24](../../iConsole/Display/ColorScheme.cs) to `:33` (Default scheme: `SelectedFocusedForeground = DarkGreen` on `Blue` and `DarkBlue`; `DisabledForeground = White`, equal to `Foreground`; `DisabledBackground = Gray`). The scheme list has only `Default` and `Dark` ([iConsole/Data/Theme.cs:3](../../iConsole/Data/Theme.cs)); the earlier reviews' phrase "light and dark themes" does not apply, there is no light theme. Tui comparison items 26 and 78 ask for a high-contrast theme.
    - Problem: the only cue for "the Tab key is here" is dark green text on blue. Under many 16-colour palettes that is hard to see, and a disabled control in the Default scheme keeps white text (on grey). The button border `[ ]` and the check mark do not change with focus. Heuristic: Accessibility (readable at 16 colours, no colour-only meaning).
    - Improvement: add a non-colour focus cue: draw the focused button as `>Title<` or reverse video, and a leading `>` before the check mark; set `DisabledForeground` to `DarkGray` in the Default scheme. Add a third `HighContrast` scheme later (the theme manager lists schemes from `ColorSchemes`).
    - Effort: S for the cue, M for a scheme. **Unverified**: contrast depends on the terminal palette; confirm with a render dump on Windows Terminal's Campbell and a Linux xterm palette.

38. **Closing the Git client while a push or pull runs loses its result** - Severity: **Low**. Kind: usability (feedback).
    - Where: [GitClientDialog.cs:1044](../../iTerminal.Core/Dialogs/Git/GitClientDialog.cs) to `:1049` (Esc closes at once, also while `_busy`), `:792` to `:819` (the result goes to `SetMessage` on a closed dialog), no `INotificationService` in the file, while [GitSyncDialog.cs:395](../../iTerminal.Core/Dialogs/Git/GitSyncDialog.cs) notifies on completion.
    - Problem: a slow push over a bad link, Esc to get on, and the user never learns whether it worked or why it failed. Heuristic: Feedback, consistency with the sync dialog.
    - Improvement: inject `INotificationService` (optional, as `GitSyncDialog` does) and notify from `RunAction` when the dialog is no longer open; or refuse to close while `_busy` and say so.
    - Effort: S.

39. **The palette omits commands that cannot run now without saying so, and Enter ignores a command that then refuses** - Severity: **Low**. Kind: usability (feedback).
    - Where: [iTerminal.Core/Services/CommandPalette/CommandCatalog.cs:73](../../iTerminal.Core/Services/CommandPalette/CommandCatalog.cs) (`!canExecute` skips the entry), [CommandPaletteDialog.cs:196](../../iTerminal.Core/Dialogs/CommandPaletteDialog.cs) to `:207` (`Close()` first, then `return await entry.Command.RunAsync(null)`; the result is dropped and the key handler returns true), `:328` ("No command matches.").
    - Problem: typing "extract" with a file panel on a folder shows "No command matches.", which reads as "this app cannot do it". The command exists; it needs a different cursor row. Heuristic: Discoverability, Feedback. The palette also shows no matched letters for a fuzzy hit (research item 22 says `FuzzyMatcher` returns positions), so "cfld" finding "Compare folders" looks arbitrary.
    - Improvement: collect unavailable commands too, flagged `IsAvailable = false`, drawn in `DisabledForeground` with the reason in the description line (`Description` already has a row); Enter on one keeps the palette open and says why. Highlight the matched positions in `PaletteList.Draw`.
    - Effort: M. **Unverified**: how many commands expose a reason for `CanExecute` false is not known; most would show only "not available here".

40. **Folder completion goes quiet when the listing is slow, and offers case-insensitive matches on case-sensitive file systems** - Severity: **Low**. Kind: usability.
    - Where: [iTerminal.Core/Model/DirectoryCompletionModel.cs:247](../../iTerminal.Core/Model/DirectoryCompletionModel.cs) (`listing.Wait(ListTimeout)` returns `[]` after 750 ms), `:104` (`StartsWith(prefix, OrdinalIgnoreCase)`), `:186` to `:195` (`TryParse` needs a `/` or `\`, so `~/` and `%USERPROFILE%\` list nothing), [AutoCompleteCommand.cs:25](../../iConsole/Commands/AutoCompleteCommand.cs).
    - Problem: on a dead share the list stays empty with nothing to say it timed out, which looks like "no subfolders". On Linux, typing `docs/` offers `Docs/` and Tab rewrites the typed case into a name that may not be the one wanted. A typed `~` is not expanded. Heuristic: Feedback, Resilience.
    - Improvement: let a timed-out update publish one placeholder row ("Still reading…", not selectable) through `MatchingValues`, or set the owner's status; compare with `Ordinal` where the file system is case-sensitive (`OperatingSystem.IsLinux()`); expand a leading `~` and environment variables before `TryParse`.
    - Effort: S to M. **Unverified**: the dialogs' target boxes may expand `~` elsewhere; not checked.

41. **`Table<T>`: the header looks like the unfocused cursor row, sorting needs the mouse, and cells are cut by characters** - Severity: **Low**. Kind: consistency, accessibility, localization.
    - Where: [iConsole/Controls/Table.cs:229](../../iConsole/Controls/Table.cs) to `:231` (header in `SelectedForeground`/`SelectedBackground`) against `:256` to `:259` and `:273` to `:276` (an unfocused cursor row uses the same pair), `:170` to `:183` and `:79` (`SortRequested` is raised only by a header click), `:129` to `:143` (no hint for sort), `:288` to `:293` (`Cut(w).PadRight(w)`, LINQ `Select` and `Zip` per row per frame). Only `PowerShellRuntimeDependency` mentions `Table<` outside `iConsole`, so no dialog uses it yet. Console survey item 104 and tui comparison item 17 (table upgrades) are the related research.
    - Problem: in a table that has lost focus the cursor row and the header are the same colours. There is no key to sort. A CJK column misaligns as the grid did before finding 21 was fixed. Heuristic: Consistency, Keyboard, Resilience.
    - Improvement: draw the header in `DisabledForeground` plus bold, or reuse the `Separator` rule colours; raise `SortRequested` from a key (`s` or Ctrl+left/right on the column strip) and list it in `GetKeyHints`; use `TextWidth.Fit` as `CsvGrid` now does. Do these before the first dialog adopts `Table<T>`.
    - Effort: S to M.

42. **Two check-mark styles, and the checked glyph is a near twin of the unchecked one** - Severity: **Low**. Kind: consistency, accessibility.
    - Where: [CheckBox.cs:35](../../iConsole/Controls/CheckBox.cs) to `:36` (`"▣ "` and `"□ "`) against [CheckList.cs:252](../../iConsole/Controls/CheckList.cs) (`"[x] "` and `"[ ] "`). Neither has an ASCII fallback.
    - Problem: `▣` and `□` differ by a small inner square, which many fonts draw nearly alike; the same dialog can show `▣ Move to Recycle Bin` and `[x] item` for the same idea. Heuristic: Consistency, Accessibility (glyphs a basic terminal lacks).
    - Improvement: draw `[x]`/`[ ]` in `CheckBox` too (or `☑`/`☐` with an ASCII fallback chosen once in `TextWidth`/a glyph table); keep the strings in one constants class shared with `CheckList` and `RadioButton`.
    - Effort: S.

43. **Small controls keep fixed colours and char-counted widths: toasts, the language dialog, the remote manager status** - Severity: **Low**. Kind: consistency, accessibility.
    - Where: [Toast.cs:52](../../iConsole/Controls/Toast.cs) to `:58` (`Color.Green`, `Color.Yellow`, `Color.Cyan` per severity; the mark `✓ ! ✗ i` does carry the meaning, which is right) and `:79` to `:86` (`Ellipsize` by `Length`); [LanguageDialog.cs:68](../../iTerminal.Core/Dialogs/LanguageDialog.cs) to `:69` (cursor row `Color.Yellow` text, the row has no background bar, the dialog rebuilds a list from `InfoLines` and has no PageUp), `:53` (height `Languages.Count + 4` is not clamped to the terminal); [RemoteConnectionManagerDialog.cs:708](../../iTerminal.Core/Dialogs/Remote/RemoteConnectionManagerDialog.cs) (`Color.Cyan` status). Extends finding 1.
    - Problem: yellow text on a light terminal background, or a toast message with a CJK folder name, is cut off-grid; the language list cannot be used on a very short terminal. Heuristic: Consistency, Resilience to size and content.
    - Improvement: take the colours from `ColorScheme` (add `Success`, `Warning`, `Info` next to `Error`), use `TextWidth.Fit` in `Toast.Ellipsize`, and build the language list from `ListBox<LanguageInfo>` (it already has the cursor bar, wheel, type-ahead and a scroll bar).
    - Effort: S to M.

### What works well

- `DirectoryCompletionModel` is a good template for I/O behind a text box: one listing at a time per folder, a two-second cache, a 750 ms wait that is never cached as "empty", a `ReplaceAll` publish ordered by sequence, a 200-match cap, and Tab left to the dialog while the list is down (`AutoCompleteCommand.CanExecute`, 74a41e86). It also fixes finding 3's clear-then-add shape for itself.
- `Dialog` keeps the focus rule right: the first control that can be acted on gets the focus (`Dialog.cs:47`), Esc closes one overlay layer per press, and the idle repaint is throttled to 50 ms.
- `ToastHost` touches the control tree only when the notification `Version` moved, takes its layer from what is on screen, and `Toast` pairs colour with a severity mark, so meaning is not colour-only.
- The Git client keeps its status and key hints together in the footer, shows a whole failed command's output in the detail pane (`ShowErrorDetail`), asks before dropping a stash, deleting a branch or discarding changes, and explains the empty states ("Nothing staged — stage a file with Space, or everything with a.").
- The remote manager says what Enter will run ("Runs: ssh …") before it runs it, says "Connecting to {0}…" and reports the exit code afterwards, and takes passwords only for FTP and WebDAV (the vault), never for shells.
- The command palette finds a command by its English or its translated name, ranks recent commands first with an empty query, shows the key next to the title, and every row of its list is painted each frame.
- `ProgressWindow` starts with an indeterminate bar until the first progress arrives, keeps a failed run on screen with its error, and removes itself from `OnIdle` rather than from the worker thread.

### Research cross-reference

- **Tui comparison items 26 and 78 (accessibility, high-contrast theme):** confirmed and made concrete by finding 37 (focus is colour only, the Default scheme's disabled colour equals its normal colour, only two schemes exist) and finding 34 (marks). They need a layout decision first only for the focus cue (a glyph in the button border changes its width by two cells).
- **Console survey item 108 (toast actions) and the missing notification history:** finding 30 argues for the history screen before actions; an Undo button on a toast cannot be reached from the keyboard until toasts can be focused or invoked by a chord.
- **Tui comparison item 72 (form bound to a model) and item 94 (quick modal dialogs):** the remote manager (finding 36) and the Git client's inline prompt (`ShowInput`, `BeginConfirm`) are two more hand-built copies of what those items would share.
- **Tui comparison items 17 and 104 (table upgrades, formatted columns):** finding 41 lists what `Table<T>` needs before a dialog adopts it; no dialog uses it today, so the research gap is not yet felt.
- **Tui comparison item 22 (fuzzy matching):** built and wired into the palette, but the matched positions are not drawn (finding 39).
- **Console survey item 1 (wide characters):** `CsvGrid` now uses `TextWidth`; `Toast`, `Table`, the palette list, the remote manager and the folder selector rows still count chars (findings 41, 43). The research table still says "no width handling found"; it was not edited.
- **Console survey item 2 (bracketed paste):** the remote manager's form has its own paste (`PasteHelper`), so finding 36 would remove one more special case from that work.
- **AI overview, "Approval for writes":** the Git client has no equivalent preview for push (finding 31); `AiActionApprovalService` shows how an action can state what it will do before it does it.
- **Security overview, evidence and RFC 3161:** nothing in this scope needs a layout change; the file panel's footer (findings 33, 34) is already full, so a timestamp state needs its own row first (see finding 19).

### Suggested order

1. Quick wins (S): finding 35 (hint or key), finding 31 (confirm push and abort, or require Shift), finding 42 (check-mark glyphs), finding 38 (notify on close).
2. Medium, high leverage: finding 29 (progress window Esc), finding 30 (notifications list and dismiss-all), finding 33 (cache the marks summary; a frame-budget risk), finding 34 (mark glyph), finding 37 (focus cue), finding 32 (queue or signal the dropped key).
3. Larger: finding 36 (rebuild the connection form from `FormPanel`).
4. Low: findings 39, 40, 41, 43, in that order; 41 before any dialog uses `Table<T>`.

Nothing here is High: no finding shows a loss of data or a measured miss of the 200 ms budget. Findings 29, 31 and 33 could reach that on a real system and should be confirmed first with a key trace, a `ConsoleAppDriver` test and `RecordingStopwatchFactory`.

## File-operation dialogs: copy and move, delete, rename, multi-rename, conflict prompt, secure wipe

Scope: the dialogs a user opens most often to change files, taken as one area because they share `FormDialog`, the Enter-runs-the-command rule and the progress window: `TransferDialog` (Copy, Move), `DeleteDialog`, `RenameDialog`, `MultiRenameDialog`, `ConflictPromptDialog` with `FileConflictResolver`, and `SecureWipeDialog`. Reviewed on 2026-11-20 at commit 3ba7401f on branch `docs/research-security-survey-3`. The working tree held only the three uncommitted research-document edits (survey 3); no source file differs from the commit. Read: `CLAUDE.md`, `key-patterns.md` (destructive keys and the Delete dialog), `file-operations.md` (wipe and the transfer sections, by grep and the wipe section in full), the three research overviews as they are on disk (the survey 3 text and the items on hot keys, quick dialogs, forms and sanitisation), the feedback-controls and Git client section above, and the source of `FormDialog`, `TransferDialog`, `CopyDialog`, `MoveDialog`, `DeleteDialog`, `DeleteCommand`, `TransferCommand` (`ExecuteAsync`, `ConsumeMarks`), `RenameDialog`, `CreateEntryDialog`, `MultiRenameDialog`, `MultiRenameCommand`, `MultiRenamePlanner` (header and statuses), `ConflictPromptDialog`, `ConflictPromptService`, `FileConflictResolver`, `SecureWipeDialog`, `Dialog` (focus, Esc, idle), `PopUpWindow.CenterInParent`, `HotKeys.VisibleIn`, `Control.HandleHotKeyEventAsync`, `Button` (destructive colour) and the German, French, Spanish, Italian and Hungarian strings that the findings quote. Not done: no app launch, no tests run, no render dump, no key trace, no timing. Everything that rests on reading alone is marked **unverified**.

### Summary

- The destructive paths are mostly well guarded: Delete starts on the Recycle Bin or Trash where one exists, copy checks free space, wipe asks twice, rename can be undone. The problems are in what the screens say while they guard.
- The secure wipe footer is one fixed line for all four phases, so it names keys that do nothing (or do the opposite) in three of them; the armed screen does not say how much will be destroyed, and a second Enter that arrives at once passes (findings 44 and 45).
- Multi-rename cannot be reviewed by keyboard: the preview list cannot take focus, so a keyboard user sees the first ten or so rows and cannot scroll to the rows that are blocked or duplicated (finding 46).
- The copy and move "Overwrite" box takes its hot key from the word "Target", so the key is wrong in English and invisible in four translations; it also means "overwrite everything without asking", which the label does not say (finding 48). The conflict prompt gives no sizes or dates for the choices "Keep newer" and "Keep larger" (finding 47).
- Delete looks the same whether the files go to the bin or are gone for good, and the German warning that says so is cut off (findings 49 and 50).

### Findings

44. **The secure wipe footer is one fixed line for all four phases** - Severity: **Medium**. Kind: usability (feedback and visibility of status), localization.
    - Where: [iTerminal.Core/Dialogs/SecureWipeDialog.cs:396](../../iTerminal.Core/Dialogs/SecureWipeDialog.cs) (`Wipe.Footer`, " Enter:wipe  1/3/7 or ↑↓:passes  Esc:close ", drawn in every phase by `DrawChrome`) against `:293` to `:310` (`GetKeyHints` is written per phase) and `:312` to `:364` (what the keys do in each phase).
    - Problem: in the armed phase `1`, `3` and `7` do not choose a method, they withdraw the arming (`:324` to `:329`). While it runs, "Enter:wipe" does nothing and "Esc:close" actually stops the wipe (`:383` to `:388`, which the body text does say). When it is finished, Enter closes. The footer is the line users read last and trust most; here it is wrong for a destroy-data dialog, and the phase-aware text exists only in the F1 help. Heuristic: Feedback and visibility, Consistency (the Git client keeps status and key hints together).
    - Improvement: pick the footer by `Phase` (new keys `Wipe.FooterArmed`, `Wipe.FooterRunning`, `Wipe.FooterFinished` in all six `.resx` files), for example "Enter: wipe for good   any other key: go back" when armed and "Esc: stop (files already wiped stay wiped)" while running. Add a `ScreenBuffer` assertion per phase to `SecureWipeDialogTest`.
    - Effort: S.

45. **The armed wipe screen does not say how much will be destroyed, and a second Enter that follows at once passes** - Severity: **Medium**. Kind: usability (error prevention).
    - Where: [SecureWipeDialog.cs:224](../../iTerminal.Core/Dialogs/SecureWipeDialog.cs) to `:232` (the first line is the names, shrunk to one row), `:249` to `:255` (armed text), `:116` to `:134` (`Arm` and `Confirm`, no time or key-repeat check), `:318` to `:322`. The class comment says the first Enter "says what is about to be destroyed".
    - Problem: with a marked folder or twelve files the armed screen shows "Wipe 12 items: a, b, c, d…" cut at the width; it does not count the files inside folders or give the size, though `FilePropertiesService.FormatSize` is already used while it runs. A held Enter key, a pasted newline pair or a double press arms and confirms within milliseconds, so the "ask twice" rule can be met without reading. Heuristic: Error prevention.
    - Improvement: when the dialog opens, count the files and bytes on a background task (the wipe service already walks the tree for progress) and show "N files, M folders, X in total" on the armed screen; ignore an Enter that arrives less than about half a second after arming, and say "Press Enter again" only then. Test with a fake clock.
    - Effort: M. **Unverified**: that the service's walk can be reused for a count before the run was not traced.

46. **The multi-rename preview cannot be scrolled from the keyboard, and the reason a row is skipped is at the far end of the row** - Severity: **Medium**. Kind: usability (keyboard, error prevention), accessibility.
    - Where: [iTerminal.Core/Dialogs/MultiRenameDialog.cs:90](../../iTerminal.Core/Dialogs/MultiRenameDialog.cs) (`_previewList.CanFocus = false`, so that Enter reaches the Rename command), `:31` (`PreviewLimit` = 500, no "more" row), `:212` to `:217` (`"{old} -> {new}  status"`, plain text, one colour), `:255` (list height = dialog height minus 9, so 11 rows on an 80x24 terminal), `:209` (the count line "{0} to rename, {1} not renamed").
    - Problem: the preview is the safety net for the batch, but only the first rows can be read with the keyboard. A blocked, duplicate or "already exists" row at position 40 is counted in the line above but cannot be found. With long names the status words ("blocked by another entry") fall past the right edge, and rows that will not be renamed look like rows that will. Heuristic: Keyboard, Error prevention, Accessibility (a status carried only by trailing text).
    - Improvement: let the dialog forward Up, Down, PageUp, PageDown and Ctrl+Home/End to the list model while a text box has the focus (a single-line box does not use them), as the folder selector forwards keys to its list; start a not-renamed row with a glyph (`!`) and colour it with `ColorScheme.Error`; end the list with a row "… and N more rows" when the cap cuts it; add a key to jump to the next problem row. A `ListBox<string>` with a `RowColor` callback is enough; no new control.
    - Effort: M.

47. **The conflict prompt names the file but not what differs, and the first key lands on the "Apply to all" tick box** - Severity: **Medium**. Kind: usability.
    - Where: [iTerminal.Core/Services/FileConflictResolver.cs:53](../../iTerminal.Core/Services/FileConflictResolver.cs) (message: "{0} is already in the target folder."), [iConsole/Dialogs/ConflictPromptDialog.cs:43](../../iConsole/Dialogs/ConflictPromptDialog.cs) (the tick box is added before the six buttons) with the rule in `Dialog.OnInitializeLayout` (first focusable control gets the focus, [Dialog.cs:47](../../iConsole/Dialogs/Dialog.cs)), `:115` to `:127` (`Draw` splits the message and builds a `ConxelString` per frame, fixed `White` on `DarkRed`).
    - Problem: the choices include Keep newer and Keep larger, but the prompt shows neither side's date or size, so the user must cancel and look. Opening on the tick box means that Enter, the key every other dialog uses to confirm, does not choose Overwrite, Skip or anything else (what Enter does on a `CheckBox` was not traced; if it toggles, the tick changes silently). Heuristic: Efficiency, Error prevention, Consistency with the other prompts.
    - Improvement: make the message two lines, "Source: 12.3 MB, 2026-11-14 10:02" and "Target: 11.8 MB, 2026-11-19 08:40", from the two entries the resolver already has (`sourceUrl`, `targetUrl`); add the tick box last or give the safest button (Skip) the first focus; draw the message from a cached `ConxelString` array and use the theme's error colours.
    - Effort: S to M. **Unverified**: the Enter behaviour on the tick box; a `ConsoleAppDriver` test (open a conflict, press Enter, read the outcome) would settle it.

48. **The "Overwrite" box in Copy and Move takes its hot key from the wrong word and means more than it says** - Severity: **Medium**. Kind: usability (error prevention), consistency, localization.
    - Where: [iTerminal.Core/Dialogs/TransferDialog.cs:131](../../iTerminal.Core/Dialogs/TransferDialog.cs) to `:135` (`HotKeys.VisibleIn(UiText.Get("Label.Target", ...), 'O')`: the caption passed is "Target", not "Overwrite"), `:83` (`ConflictMode`: ticked is `OverwriteAll`, unticked is `Ask`), [iConsole/Localization/HotKeys.cs:15](../../iConsole/Localization/HotKeys.cs) to `:35`, [Control.cs:911](../../iConsole/Controls/Control.cs) (hot keys fire on Ctrl+letter).
    - Problem: `VisibleIn("Target", 'O')` finds no `O` and returns the first letter of "Target", so the box answers Ctrl+T, not Ctrl+O. The underline lands on the last `t` of "Overwrite" in English. In German ("Ziel" gives `Z`), Spanish and Italian ("Destino", "Destinazione" give `D`) and Hungarian ("Cél" gives `C`) the letter is not in "Überschreiben", "Sobrescribir", "Sovrascrivi" or "Felülírás", so a hot key works that nothing on screen shows. Separately, the word "Overwrite" reads as a setting for the one file; ticking it silently replaces every existing target of a batch and skips the prompt of finding 47. Heuristic: Consistency, Error prevention, Discoverability.
    - Improvement: pass the box's own caption (`Label.Overwrite`) to `VisibleIn`; rename the label to "Overwrite existing files without asking" (all six `.resx` files); add a test that every translated caption of the dialog draws its hot key (the same shape as the existing `CreateButtonHotKey` test, if one exists). The fixed letters in `RenameDialog.cs:97` (`'N'`), `MultiRenameDialog.cs:69` to `:83` (`'P'`, `'F'`, `'E'`) and `CreateEntryDialog.cs:59` have the same flaw: "Muster", "Suchen", "Modèle" and "Rechercher" contain no `P` or `F`. Console survey item 147 (an `&` marker in a caption) would remove the whole class; until then use `VisibleIn` everywhere.
    - Effort: S.

49. **A permanent delete looks the same as a delete to the bin, and it can take a whole folder tree in one Enter** - Severity: **Medium**. Kind: usability (error prevention).
    - Where: [iTerminal.Core/Dialogs/DeleteDialog.cs:108](../../iTerminal.Core/Dialogs/DeleteDialog.cs) (button "Delete", always `IsDestructive`, so always red), `:132` to `:133` and `:141` to `:142` (Recursive is ticked for any folder), `:212` to `:217` (the bin box is the only place that says which kind of delete it is), `:148` to `:157` (title "Delete folder" or "Delete N items" in both cases), [DeleteCommand.cs:20](../../iTerminal.Core/Commands/DeleteCommand.cs) (Enter anywhere runs it). `key-patterns.md` says destructive actions ask first and that Delete defaults to the bin only where one exists; this is met, but the asking dialog does not say what it is asking.
    - Problem: with the bin box unticked by hand on Windows, or on SSH, SMB, FTP and the memory drive, the dialog, title and button are the same as for a recoverable delete. A folder is deleted with everything in it after one Enter, and no count or size is shown until the progress window. The red button also marks a recoverable delete as dangerous, so the colour stops carrying information. Heuristic: Error prevention and recovery.
    - Improvement: when the bin is not used, change the title ("Delete for good"), the button caption ("Delete for good") and draw one line in `ColorScheme.Error` ("This cannot be undone"); keep the red button only in that state. For a folder, show "N items, X in total" once the existing `FolderDeleteDiscoveryTask` has counted (it already walks the tree), on the `_errorLabel` row or a new row.
    - Effort: S for the wording and colour, M for the count.

50. **The German "not available here" line is cut at the point that says the delete is permanent** - Severity: **Low**. Kind: localization, resilience to size.
    - Where: [DeleteDialog.cs:200](../../iTerminal.Core/Dialogs/DeleteDialog.cs) (`NoRecycleBinLabel`), `:169` (dialog 80 wide, so the box is 74 cells), [AppStrings.de.resx](../../iTerminal.Core/Localization/Resources/AppStrings.de.resx) key `Field.MoveToRecycleBinNotHere`. The German text is 77 characters; with the two-cell check mark it needs 79. The other languages are 67 to 69 characters. On a terminal narrower than 80 columns `CenterInParent` shrinks the dialog and every language is cut.
    - Problem: the half of the sentence that is lost is "wird endgültig gelöscht", the warning. Heuristic: Resilience to size and content, Localization.
    - Improvement: shorten the text ("hier nicht verfügbar — endgültig") or move the explanation to its own `Label` row under the box, wrapped by `InfoLines.Wrap`. Add a test that every translation of the line fits in 74 cells.
    - Effort: S.

51. **The wipe dialog builds its whole text on every idle pass, from all the paths** - Severity: **Medium**. Kind: performance.
    - Where: [SecureWipeDialog.cs:218](../../iTerminal.Core/Dialogs/SecureWipeDialog.cs) to `:222` (`OnIdle` calls `UpdateLines`), `:229` to `:232` (`Paths.Select(...)` over every path, `string.Join` of every name, then `Shrink` to one row), `:290`. The comment in [Dialog.cs:98](../../iConsole/Dialogs/Dialog.cs) to `:108` says an idle pass can come hundreds of times a second. The same join is done by `TransferDialog.DescribeSources` ([TransferDialog.cs:293](../../iTerminal.Core/Dialogs/TransferDialog.cs)) on each layout, which includes every resize step, and the result is also stored whole in the read-only name box of `DeleteDialog` (`:128` to `:130`).
    - Problem: with 20 000 marked rows the string is built and thrown away on every idle pass, in all phases including the idle Choosing screen; the repaint itself is skipped when the lines are equal ([InfoLines.cs:57](../../iTerminal.Core/Controls/InfoLines.cs)), but the allocation is not. It costs frame time exactly when the user has selected the most. Heuristic: Performance, Resilience to content.
    - Improvement: build the "Wipe …" line once in `Load` (and the byte total once), call `UpdateLines` from `OnIdle` only while `Phase` is `Running`, and make `DescribeSources` stop after the first dozen names with "… and N more".
    - Effort: S. **Unverified**: no timing; `RecordingStopwatchFactory` over 20 000 paths would confirm.

52. **Several wipe texts are English strings in code** - Severity: **Low**. Kind: localization.
    - Where: [SecureWipeDialog.cs:165](../../iTerminal.Core/Dialogs/SecureWipeDialog.cs) (`"file"`/`"files"` joined with `FormatSize` and used in all three notifications), `:230` to `:232` ("Wipe  " and "items:"), `:277` to `:283` ("Stopped: …", "Done: …", "Failed: …"), `:280` (`Color.Green` for success). Every other line of the dialog goes through `UiText.Get`.
    - Problem: in a German or Hungarian session the result screen and the toast bodies switch to English, and the success colour ignores the scheme. Heuristic: Consistency, Localization.
    - Improvement: add keys `Wipe.Title`, `Wipe.Names`, `Wipe.Stopped`, `Wipe.Done`, `Wipe.FailedItem`, `Wipe.FilesCount` to all six `.resx` files; take the success colour from the scheme (finding 43 proposes `Success`).
    - Effort: S.

53. **The marks are cleared before a delete or rename has run, so a cancelled or failed batch cannot be repeated** - Severity: **Low**. Kind: usability (error recovery).
    - Where: [DeleteCommand.cs:110](../../iTerminal.Core/Commands/DeleteCommand.cs) (batch: `ConsumeMarks` before `taskManager.Add`), `:79` (single: after the progress window opens), [MultiRenameCommand.cs:73](../../iTerminal.Core/Commands/MultiRenameCommand.cs), [TransferCommand.cs:123](../../iTerminal.Core/Commands/TransferCommand.cs) and `:179`; [TransferCommand.cs:293](../../iTerminal.Core/Commands/TransferCommand.cs) to `:300` (`ClearMarks`, `Update`). The reason in the comments is right (stale marks would repeat the batch).
    - Problem: with finding 29 (Esc on the progress window cancels at once) a user who stops a 200-file delete halfway has lost the selection and must mark the rest again from memory. Heuristic: Error recovery.
    - Improvement: clear the marks of the entries that finished, not all of them: `BatchOperationTask` knows each step's status; on a cancelled or failed run re-mark the steps that did not complete (`SetMark`) from the `RefreshWhenFinished` callback. Only for the cancelled and failed cases.
    - Effort: M. **Unverified**: that `BatchOperationTask` exposes the per-step status to the callback was not read.

54. **The rename dialog's key hints replace the form's, and the box accepts a path without saying so** - Severity: **Low**. Kind: consistency, discoverability.
    - Where: [iTerminal.Core/Dialogs/RenameDialog.cs:178](../../iTerminal.Core/Dialogs/RenameDialog.cs) to `:183` (hints Enter, Tab, Esc only; `FormDialog.GetKeyHints` also lists Shift+Tab), `:44` to `:62` (`ResolveTarget`: text with a separator is the full target, so a rename can move the entry), `:94` (label "Name"). Copy and Move label their box "To" and show the resolved target under it ([TransferDialog.cs:256](../../iTerminal.Core/Dialogs/TransferDialog.cs)).
    - Problem: typing `sub/x` into a box called "Name" moves the file; nothing in the dialog says that, and the Overwrite box has no hint either. Heuristic: Discoverability, Consistency.
    - Improvement: call `base.GetKeyHints()` first and add the Overwrite line; show the same resolved-target line as the transfer dialog when the text contains a separator. The three selection details (name without extension) are good and stay.
    - Effort: S.

### What works well

- `TransferSpaceGuard` checks free space before a copy and prompts, and `TransferDialog` shows the resolved destination live under the box (`UpdateTargetPreview`), so a bare name typed into "To" is never a surprise.
- `DeleteDialog` starts on the Recycle Bin (Trash on Linux) only where one exists, disables the box elsewhere and says why (`ApplyRecycleBinAvailability`), and scrolls the read-only path to its end so the name being deleted stays visible.
- `RenameDialog` selects the name without its extension (`NameSelection`), so typing keeps `.docx`; the batch rename is undoable with Ctrl+Z (`RenameUndoService`).
- `MultiRenamePlanner` works on names only, so the preview can recompute on every key without touching a disk; `MultiRenameCommand.CanExecute` is false when no row would be renamed, and non-`Ok` rows are never submitted.
- `FormDialog` clears an error when the user starts typing again, and `CenterInParent` clamps a dialog to the screen, so 80x24 shows every dialog here whole.
- The wipe dialog says plainly what an overwrite cannot reach (SSD remapping, snapshots, copy-on-write), asks twice, and states that Esc keeps what was already wiped.
- The conflict resolver serialises prompts, keeps "apply to all" for the run, and Esc means Cancel of the whole run, as the dialog's comment says.

### Research cross-reference

- **Console survey item 147 (hot-key marker in a caption):** confirmed and given a cost by finding 48. Five dialogs here pass fixed letters, and one passes the wrong caption; an `&` marker in the caption would make the key and its underline come from one string and survive translation.
- **Console survey item 94 (quick modal dialogs):** the conflict prompt and the wipe's four-phase machine are two more hand-built confirmations (the Git client's inline prompt is in the Feedback controls section); the phase-specific footer of finding 44 would come free from a shared "prompt with phases" helper.
- **Tui comparison item 72 (form bound to a model):** `DeleteDialog`, `RenameDialog` and `TransferDialog` each repeat the same Url, label and button wiring on top of `FormDialog`; no change needed before the research item is built.
- **Security survey item 23 (sanitisation category and record for a wipe):** needs a layout decision first. The Choosing screen already uses 11 of its 13 rows (`SecureWipeDialog.cs:236` to `:248`), and finding 45 wants one more line on the armed screen; a category line and a "record saved" line would need a taller dialog or a second page. The research text about what the dialog states matches the source.
- **AI survey item 16 (shared approval shape):** these dialogs hold the preview a human sees before a delete, move or wipe; if the AI approval dialog and these dialogs are to say the same thing ("N items, X in total, permanent"), findings 45 and 49 should be built first so the summary exists as data. Not checked in `AiActionApprovalService`.
- **Console survey item 1 (wide characters):** `TransferDialog`, `DeleteDialog` and `MultiRenameDialog` still count characters in `ScrollableSize`, `Shrink` and `PadRight`; covered by finding 4, not repeated here.

### Suggested order

1. Quick wins (S): finding 48 (caption for the hot key, label wording), finding 44 (footer per phase), finding 50 (German line), finding 52 (wipe strings), finding 54 (rename hints), finding 51 (build the line once).
2. Medium, high leverage: finding 49 (permanent delete wording), finding 46 (preview keyboard and status), finding 47 (conflict details and focus), finding 45 (armed counts and double-Enter guard).
3. Low: finding 53 (re-mark after a cancelled batch), after finding 29 (Esc on the progress window) is decided, because they interact.

Nothing here is High: no finding shows data loss or a measured miss of the 200 ms budget. Findings 45, 47 and 51 could reach that on a real system and should be confirmed first with a `ConsoleAppDriver` test (finding 47), a fake clock (finding 45) and `RecordingStopwatchFactory` (finding 51).

## Music player: F3 music viewer, playlist and catalog panels, audio output

Scope: the music player, the fifth review pass. It was picked as one area because it is the feature with run-time state outside the UI (a pump thread, an audio device, a library index) and it is operated through three screens that must agree: `MusicFileViewerDialog` (F3 on an audio file), `ContentModelMusicPlaylist` (`musicplaylist://`, the queue) and `ContentModelMusicCatalog` (`musiccatalog://`, the library), with the commands in `iTerminal.Core/Commands/Music/` and the device service behind them. The other F3 viewers (CSV, HDF5, Parquet) are covered for their query line by the Recent UI changes section and are left for a later pass. Reviewed on 2026-11-20 at commit 4ad0edba on branch `worktree-agent-adc5a58ad86839686` (clean tree). Read: `CLAUDE.md`, `media-and-documents.md` (audio playback, F3 music viewer, the two panel drives, tag editing), the research overviews and `tui-library-comparison.md` (searched for music, audio, player and playlist), the heading and finding conventions of the earlier sections, and the source of `MusicFileViewerDialog`, `ContentModelMusicBase`, `ContentModelMusicPlaylist`, `ContentModelMusicCatalog`, the music commands, `AudioDeviceService`, the `GetDevices` and `RunLines` code of `PulseAudioSink` and `AplayAudioSink`, `Playlist.Move`, `IPlaylistService`, `MusicLibraryService` (`Entries`, `ExpandResult`, `CompareEntries`), `Dialog.OnIdle`, `ContentBrowser` (command list and footer) and the key hint strings in the six `.resx` files. Not done: no app launch, no tests run, no render dump, no key trace, no timing. Findings that rest on reading alone are marked **unverified**.

### Summary

- The one finding that can break the 200 ms budget: the list of audio devices is built by running `pactl` or `aplay` on the thread that builds the panel rows, the footer and the viewer's `Draw`, with a five second wait per call and an unbounded `ReadToEnd` before it (finding 55).
- The panels have only three direct keys (Space, + and -). Next, previous, seek, stop, repeat and shuffle exist in the model but are reached through the `[Transport]` folder, which takes the queue off the screen (finding 56). The queue cannot be reordered, saved or loaded, though the code for all three exists (finding 58).
- The catalog has two faults a listener meets quickly: the "Unknown artist" and "Unknown album" rows add nothing with F5, and an album is shown in title order but queued in path order (findings 59 and 60).
- The viewer cuts its key line at 80 columns, so `e:tags` and `Esc:close` are never shown (finding 57).
- Strengths to keep: the shared player singleton, the stale-read guard on tag reads, rows rebuilt only when the queue, the cursor or the state moved, and a status line that puts the engine's errors before the viewer's own.

### Findings

55. **The audio device list is built by running external tools on the key, refresh and draw paths** - Severity: **High**. Kind: performance.
    - Where: [ContentModelMusicPlaylist.cs:176](../../iTerminal.Core/Model/ContentModels/ContentModelMusicPlaylist.cs) (`HasAudioOutput`, `ActiveBackendName`) and `:177` and `:488` (`CurrentDeviceName` calls `GetDevices`, from `Refresh` and from `GetFooter`), `:141`, `:280` and `:365` (`Refresh()` of the device service followed by a rebuild), [MusicFileViewerDialog.cs:536](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs) (`CurrentDeviceName` in `DrawPlayer`), [AudioDeviceService.cs:29](../../iTerminal.Core/Services/Music/AudioDeviceService.cs) to `:55`, [PulseAudioSink.cs:248](../../iTerminal.Core/Audio/Sinks/PulseAudioSink.cs) (`GetDefaultSinkName` and `pactl list short sinks`), `:311` to `:336` (`RunLines`: `ReadToEnd` first, `WaitForExit(5000)` after), [AplayAudioSink.cs:202](../../iTerminal.Core/Audio/Sinks/AplayAudioSink.cs) to `:217` and `:277` to `:285` (`IsAvailable` runs `aplay --version`, `GetDevices` runs `aplay -L`).
    - Problem: the first panel build of a session, the first viewer paint and every "Rescan devices" or Ctrl+R in `[Output]` start one to three child processes and wait for them on the UI thread. On a machine where the PulseAudio daemon is stuck, `pactl` does not exit; because the output is read to the end before the timeout is armed, the wait is not bounded at all and the screen freezes. The result is cached afterwards, which hides the cost in a normal run. Heuristic: Performance (CLAUDE.md: no processes in key handlers or `Draw`), Feedback.
    - Improvement: probe once on a background task when the player or the first music panel is created (an `IAudioDeviceService.Preload`, the same shape as `IMusicLibraryService.Preload`), return the cached list, or an empty list and the word "Looking for outputs…", until it lands, and raise `StateChanged` to repaint. In `RunLines` read the output on a task and apply the timeout to the read. "Rescan devices" does the same on `TaskManager` and writes back with the stale check.
    - Effort: M. **Unverified**: no timing and no stuck `pactl` was tried; `RecordingStopwatchFactory` around the first `Refresh` with a fake backend that sleeps would confirm.

56. **The panels have three direct music keys; next, previous, seek, stop, repeat and shuffle need a trip into the `[Transport]` folder** - Severity: **Medium**. Kind: usability (efficiency, discoverability).
    - Where: [ContentBrowser.cs:268](../../iTerminal.Core/Controls/Contents/ContentBrowser.cs) to `:274` (the seven music commands: Enter, Insert, F5, Delete, Space, + and -), [ContentModelMusicBase.cs:69](../../iTerminal.Core/Model/ContentModels/ContentModelMusicBase.cs) to `:117` (`Perform` already implements Next, Previous, SeekBackward, SeekForward, CycleRepeat, ToggleShuffle and Stop), [ContentModelMusicPlaylist.cs:237](../../iTerminal.Core/Model/ContentModels/ContentModelMusicPlaylist.cs) to `:250` (the `[Transport]` rows). The comment on `Perform` speaks of "the direct keys the panels bind so the common controls do not require a trip into a control folder"; only two of the nine actions are bound.
    - Problem: to skip a track the user presses Enter on `[Transport]` (the panel now lists nine action rows, not the queue), moves to "Next track", presses Enter, and goes back up through the `..` row to see the queue again. Seeking 30 seconds is six Enter presses on the "Forward 5s" row. The viewer has Left and Right for seek, so one task has two key sets. Nothing on screen names the three keys that do exist (the footer names only Alt+text). Heuristic: Efficiency, Consistency (viewer against panel), Discoverability.
    - Improvement: add commands in `iTerminal.Core/Commands/Music/` on `MusicPanelCommand` (the same gate as Space) for next, previous, stop, seek back and forward (5 s, and 30 s with Shift, as in the viewer), repeat and shuffle. Choose keys that do not collide with the file-panel keys in `key-patterns.md` and check them with `ITERMINAL_KEY_TRACE=1`; no Alt+letter. List them in the F1 help and the footer, and add palette entries so they are found without the manual. Add a `ConsoleAppDriver` test that presses each key on `musicplaylist://` with a fake player.
    - Effort: M.

57. **The viewer's key line is cut at 80 columns, so `e:tags` and `Esc:close` are never shown** - Severity: **Medium**. Kind: usability (discoverability), localization, resilience to size.
    - Where: [MusicFileViewerDialog.cs:516](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs) (`Hints.MusicViewer`, 101 characters in English), `:403` (the dialog is 90% of the parent, 72 columns on an 80 column terminal, 70 inside the border), `:560` (`text[..w]` cuts), `:506` to `:513`; the German, Hungarian, Spanish and French strings for `Hints.MusicViewer` in `AppStrings.*.resx` are about 110 to 120 characters.
    - Problem: on an 80x24 terminal the line ends after "seek furt". The two keys cut off are the one that edits tags (the only entry point to `MusicTagEditorDialog`) and the one that closes. F1 lists them (`GetKeyHints`, `:303` to `:311`), but a user who does not press F1 never learns that tags can be edited. Heuristic: Discoverability, Resilience to size.
    - Improvement: build the line from `GetKeyHints()` in priority order (Esc, Space, e, seek, volume) and stop at the last whole item that fits, or give the hint row a second line (`PlayerRows` 4 to 5). The resource wording stays; the cut is by item, not by character.
    - Effort: S.

58. **The queue cannot be reordered, saved or loaded, though the code exists** - Severity: **Medium**. Kind: usability (efficiency, error recovery), consistency (documentation against source).
    - Where: [Playlist.cs:213](../../iTerminal.Core/Data/Music/Playlist.cs) (`Move`, no caller in `iTerminal.Core`), [IPlaylistService.cs:23](../../iTerminal.Core/Services/Music/IPlaylistService.cs) to `:29` (`CreateTracks`, `Load`, `Save`; the only caller of the service is `MusicSessionRestoreService.cs:82`, `LoadLast`), [RemoveFromMusicPlaylistCommand.cs](../../iTerminal.Core/Commands/Music/RemoveFromMusicPlaylistCommand.cs). `media-and-documents.md` says a `.m3u8` "can be browsed, copied and opened in the panels".
    - Problem: a listener who adds an album and wants one track first has to remove the others and add them again. The only way to keep a queue is to leave it in the session store: no key writes a `.m3u8`, and no way was found in the source to open one into the queue (Enter on an `.m3u8` in a file panel goes to the file association). The documentation says otherwise; the disagreement is stated here and the page is not edited. "Clear the playlist" (a `[Transport]` row) empties the queue with no question and no undo. Heuristic: Efficiency, Error recovery.
    - Improvement: bind move up and move down on the queue to `Playlist.Move` (commands next to `RemoveFromMusicPlaylistCommand`, gated the same way). Make F5 from the playlist panel to a file panel write an `.m3u8` through `IPlaylistService.Save`, and let Enter on an `.m3u8` offer "Play" through `Load` and `ReplacePlaylist`. Ask y/n in the footer before "Clear the playlist" when more than a handful of tracks are queued, as `key-patterns.md` asks of destructive actions.
    - Effort: M for the move keys, M to L for save and load.

59. **The "Unknown artist" and "Unknown album" rows add nothing with F5** - Severity: **Medium**. Kind: usability (a fault a listener meets with untagged files).
    - Where: [ContentModelMusicCatalog.cs:224](../../iTerminal.Core/Model/ContentModels/ContentModelMusicCatalog.cs) and `:249` (an empty tag is shown as the literal "Unknown artist" or "Unknown album" and the `MusicSearchResult` carries that literal), `:325` to `:333` (the listing maps the literal back to an empty tag), [MusicLibraryService.cs:465](../../iTerminal.Core/Services/Music/MusicLibraryService.cs) to `:469` (`ExpandResult` compares `entry.Artist` and `entry.Album` with the literal, with no such mapping), catalog `:359` to `:369` (the empty result becomes "Nothing to add from that row").
    - Problem: opening the "Unknown artist" folder lists the tracks, but marking that folder (or one of its albums) and pressing F5 reports "Nothing to add from that row". Adding tracks one by one works. The two words are also English in every language. Heuristic: Consistency (the listing and the add disagree about what the row holds).
    - Improvement: share the empty-tag mapping between the listing and `ExpandResult` (one `ArtistMatches` and `AlbumMatches`), and take the two display words from `UiText`. Add a `MusicLibraryServiceTest` that expands an "Unknown artist" result over an untagged entry.
    - Effort: S. **Unverified**: read from source only; a catalog test with one untagged entry would confirm.

60. **An album is listed in title order but queued in path order, and the library has no track number** - Severity: **Medium**. Kind: usability.
    - Where: [ContentModelMusicCatalog.cs:263](../../iTerminal.Core/Model/ContentModels/ContentModelMusicCatalog.cs) to `:272` (`OrderBy(Title)`), [MusicLibraryService.cs:493](../../iTerminal.Core/Services/Music/MusicLibraryService.cs) to `:507` (`CompareEntries`: artist, album, then path) with `:471` (`ExpandResult` keeps that order), [MusicLibraryEntry.cs:37](../../iTerminal.Core/Data/Music/MusicLibraryEntry.cs) and `MusicTrack` (no track or disc field), while `MusicTagReader.ReadAll` and `MusicTagEditorDialog` do read and write a track number.
    - Problem: the album screen shows tracks A to Z by title; F5 on the album queues them by file name. For files named `01 ...`, `02 ...` the queue order is the album order and the screen shows another one; for files without numbers the album plays in an order the user has not seen. A listener cannot tell what "add album" will play. Heuristic: Consistency, Feedback.
    - Improvement: store the disc and track number in `MusicLibraryEntry` (the cache tables are recreated by the schema stamp, so no migration), sort the album listing and `CompareEntries` by disc, track, then path, and show the number in the row. Until then, sort the album screen by path so that screen and queue agree.
    - Effort: S for the interim sort, M for the track number. **Unverified**: the scan order of equal albums was read only in `CompareEntries`.

61. **The catalog rebuilds its whole artist list, from a full copy of the library, on every indexing progress report** - Severity: **Medium**. Kind: performance.
    - Where: [ContentModelMusicCatalog.cs:176](../../iTerminal.Core/Model/ContentModels/ContentModelMusicCatalog.cs) (`OnIndexingProgressChanged` calls `Refresh()` on the scanning thread), `:221` to `:240` (`BuildArtistEntries`: `GroupBy`, `OrderBy`, and per artist `Select/Distinct/Count`), `:322` (`EntriesOfArtist` copies and filters the whole library for each album screen), [MusicLibraryService.cs:149](../../iTerminal.Core/Services/Music/MusicLibraryService.cs) to `:158` (`Entries` returns a new list of every entry on each read). The progress event is rate-limited to 250 ms (`media-and-documents.md`).
    - Problem: while a scan runs, four times a second the list of every track is copied and grouped on the scan thread. With 50 000 tracks that is a lot of allocation for a screen that shows 20 rows, and each `Entries.ReplaceAll` repaints the panel. It competes with the scan and with the user's navigation. Heuristic: Performance (work for off-screen rows, allocation per refresh).
    - Improvement: have the library service keep a ready artist index (a sorted list of artist, album and count) that it rebuilds when `LibraryChanged` fires and once at the end of a scan; `Refresh` during a scan then only repaints the header and footer (`Update()`), which is what the comment on `OnIndexingProgressChanged` gives as its purpose. Search keeps using `Search`.
    - Effort: M. **Unverified**: no timing; a stopwatch over `Refresh` with a 50 000 entry fake library would confirm.

62. **Several viewer and catalog texts are English strings in code, and two colours are fixed** - Severity: **Low**. Kind: localization, accessibility.
    - Where: [MusicFileViewerDialog.cs:187](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs) ("This file carries no tags."), `:460` ("(needs ffmpeg)"), `:464` to `:465` ("Tags"), `:471` ("Player"), `:495` ("Volume" and "Output:"), `:538` ("Default output"), `:450` (`Color.Cyan`), `:492` (`Color.Green`); [ContentModelMusicCatalog.cs:286](../../iTerminal.Core/Model/ContentModels/ContentModelMusicCatalog.cs) and `:296` (the prefixes "A  " and "L  " in search results, with no legend), `:224` and `:249`. The panel models use `UiText` throughout.
    - Problem: in a German or Hungarian session the viewer's labels and the search rows stay English, and the path line and the playing state use colours that ignore the theme (Cyan and Green on a light scheme). The state is also carried by a glyph (`▶ ‖ ■`), so the meaning is not colour-only; that part is fine. Heuristic: Consistency, Localization, Accessibility.
    - Improvement: add keys (for example `MusicViewer.NoTags`, `MusicViewer.NeedsFfmpeg`, `MusicViewer.TagsBand`, `MusicViewer.PlayerBand`, `MusicViewer.Output`, `Music.UnknownArtist`, `Music.UnknownAlbum`) to all six `.resx` files; take the colours from the scheme (a success colour is proposed in finding 43); in search results use the "artist" and "album" words already in the context column and drop the letter prefixes.
    - Effort: S.

63. **The viewer repaints 20 times a second for as long as it is open and allocates on every frame** - Severity: **Low**. Kind: performance.
    - Where: [Dialog.cs:98](../../iConsole/Dialogs/Dialog.cs) to `:120` (`OnIdle` calls `StateHasChanged` every 50 ms), [MusicFileViewerDialog.cs:447](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs) to `:514` (ten interpolated strings and one `ConxelString` each, a `FirstOrDefault` closure in `CurrentDeviceName`), `:592` (`string[] units` allocated in `FormatSize` on every call).
    - Problem: a paused or stopped file, or a file that is not the cued track, has nothing that moves, yet the whole dialog including the tag list is drawn 20 times a second. The elapsed time changes once a second. The cost per frame is small; it is paid by every viewer left open and, on a slow terminal, by the diff writer. Heuristic: Performance.
    - Improvement: let `Dialog` ask a virtual `NeedsIdleRepaint` (default true) that the viewer answers true only while loading or while its file is playing; cache the text of the static rows (path, artist, title, album, format) when `_metadata` or the width changes; make `units` a static readonly array.
    - Effort: S. **Unverified**: no frame timing.

64. **Wide characters, a short terminal and a half-width footer are not handled** - Severity: **Low**. Kind: accessibility, resilience to content.
    - Where: [MusicFileViewerDialog.cs:194](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs) (`Name.Length`), `:543` (`{label,-8}`), `:560` (`text[..w]` and `PadRight` by char count), `:438` to `:441` (nothing drawn when the inner height is 10 rows or fewer, a terminal of 13 rows or less), [ContentModelMusicPlaylist.cs:424](../../iTerminal.Core/Model/ContentModels/ContentModelMusicPlaylist.cs) (`header.Length`) and `:196` (the row text), `:453` to `:483` (a footer of about 80 characters), [ContentBrowser.cs:525](../../iTerminal.Core/Controls/Contents/ContentBrowser.cs) (`Shrink` to the panel width elides the middle).
    - Problem: music libraries are where CJK and emoji titles are most common; the viewer's rows and the panel header count characters, so a wide title overflows or splits a pair (finding 4 covers the control level). On a short terminal the viewer shows an empty frame with no message. In a 40 column panel the footer's middle (volume, repeat, shuffle) is what `Shrink` removes, while the hint text at the end stays. Heuristic: Resilience to size and content.
    - Improvement: use `TextWidth.Of` and `TextWidth.Fit` in `WriteLine` and the header (as `CsvGrid` now does); draw a "Terminal too small" line when the dialog cannot fit its rows; move the hint to the front of the footer so that the state items, not the hint, are the part elided only when needed.
    - Effort: S to M. **Unverified**: all three by reading only; a `ScreenBuffer` test with a CJK title and an 80x12 buffer would show them.

### What works well

- One shared `IMusicPlayerService`: the viewer, the panels and the queue always agree, and the viewer's transport applies only when its own file is cued (`IsThisFileCued`), so seeking in a viewer cannot move another track.
- The tag read runs on a background task with a stale check against `FilePath` (`StartLoading`), shows "Reading tags…", and refuses to queue a track before the read has landed.
- `ContentModelMusicPlaylist` rebuilds rows only when the queue version, the cursor or the state moved (`HasRowStateMoved`), so a progressing track repaints the header and not the list; the header drops the progress bar before the title.
- The status line puts the engine's own trouble (a missing codec, a stopped device) before the viewer's chatter and draws it in the error colour; the footer says how to filter and how to clear.
- Filtering by words in any order, Esc to clear, marks that are per listing and consumed by F5, and a status word for every action (Added N tracks, already in the playlist) make the two panels easy to learn.
- Glyphs carry the playing state in addition to colour (`▶ ‖ ■`), and the volume has both a number and a bar.

### Research cross-reference

- **Tui comparison item 110 (scrolling text):** confirmed by this scope. The playlist header shows state, title, times and bar and cuts the title with `Shrink` when the panel is narrow; a scrolling title is the stated use for a music status line. Not built; no layout decision is needed first.
- **Console survey item 1 (wide characters):** finding 64 adds the music screens to the screens that still count characters; no new item.
- The AI and security overviews have no item about audio, playlists or tags, so no AI or security feature in them needs room in these screens. Voice input in the AI bar does not use the music device service.
- Research against source: no disagreement found in the three overviews. The one disagreement is with an architecture page, not a research page: `media-and-documents.md` says saved playlists can be "opened in the panels"; the source has no caller of `IPlaylistService.Load` or `Save` outside the session restore (finding 58).

### Suggested order

1. Quick wins (S): finding 59 (Unknown artist adds nothing), finding 57 (key line by whole items), finding 62 (strings and colours), finding 63 (idle repaint and static rows), the interim sort of finding 60.
2. High severity: finding 55 (device probe off the UI thread), confirmed first with a fake backend that sleeps.
3. Medium: finding 56 (direct transport keys), finding 58 (move, save, load), finding 61 (catalog index), finding 60 (track number).
4. Low: finding 64.

## Dialog set: tables, live data and labels across the dialogs

Scope: all 128 dialog classes under `iTerminal.Core/Dialogs` and `iConsole/Dialogs`, read for how their tables are built, whether they refresh, and whether their text is localized. Reviewed on 2026-11-21 at commit 7a0b4b3a, with changes in the working tree. Method: a signal pass over all 128 (hand-padded columns with `PadRight`/`PadLeft`, `OnIdle` refresh, own `GetKeyHints`, `UiText` use, hard-coded English), then a close read of the dialogs the signals flagged. Form dialogs get their key hints from their fields (`FormDialog`), so "no hints" alone is not a finding. Not done: no run-time check, no render dump, and not every dialog was read in full. The status of each finding is from reading code, not from a run, except where a test is named.

### Summary

- Most of the set follows the rules: `ListBox`, `Table<T>` and `VirtualTable<T>` are bound to collections, and the live dialogs refresh from `OnIdle`. The weak spot is a family of dialogs that paint a table by hand: a `Put` per row, a `PadRight` per column and a fixed width, with no header that sorts and no binding.
- Biggest problem: the Network manager and the Bluetooth devices were text rows formatted once when a device arrived, so a status went stale (finding 65) or had no column headers at all (finding 66). Both are now bound tables (Resolved).
- Open: the hand-painted tables in the live and the static dialogs (finding 68), the Torrent manager's layout (finding 69) and the System information text (finding 70).

### Findings

68. **Tables are painted by hand in nine dialogs, most of them without a sort or a binding** - Severity: **Medium**. Kind: consistency, maintainability.
   - Where: [PrintDialog.cs:507](../../iTerminal.Core/Dialogs/PrintDialog.cs) to `:513` and `:533` (the print queue; refreshed in `OnIdle`), [TorrentManagerDialog.cs:564](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs) (`Row`) and `:618` (the header), [StreamsDialog.cs:535](../../iTerminal.Core/Dialogs/StreamsDialog.cs) to `:561` (no `OnIdle`), [MailDialog.cs:1056](../../iTerminal.Core/Dialogs/Mail/MailDialog.cs) to `:1059` (the message headers), [ToDoListDialog.cs:31](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs) to `:34` and `:607` (no `OnIdle`), [StockBrowserDialog.cs:50](../../iTerminal.Core/Dialogs/Stocks/StockBrowserDialog.cs) and `:155` (the search hits), [PerformanceBenchmarkDialog.cs:240](../../iTerminal.Core/Dialogs/Benchmark/PerformanceBenchmarkDialog.cs) and `:258` (results), [PasswordManagerDialog.cs:555](../../iTerminal.Core/Dialogs/PasswordManager/PasswordManagerDialog.cs) (the header; no `OnIdle`).
   - Problem: each dialog measures its own columns and pads text to them. A column cannot be sorted by a header click, a row that changes is repainted only because the whole dialog is redrawn, and a value that is longer than its column is cut by hand each time. Dialogs that do refresh (Print, Torrent) still rebuild the text on every pass. Heuristic: Consistency, Data binding. The same job is done by `Table<T>`, which binds to a `ConcurrentObservableCollection`, redraws on `CollectionChanged`, and already has `SortColumn`/`SortRequested`, which `ProcessManagerDialog` uses.
   - Improvement: convert in this order, one dialog per change, each with its own tests: Print queue (live; `Table<PrintJob>`), Torrent list (live; see finding 69), Stock search and Streams (lists of plain records), then Mail headers, the ToDo list, the Benchmark results and the Password list. Keep the sensitive values (passwords) out of the row text, as they are now. Each conversion needs its localized column headers in all six `.resx` files, inserted in sorted order.
   - Effort: M per dialog. **Unverified**: the Print and Torrent conversions have no test that reads a row back from the table yet.
   - Status: open, partly fixed. Converted so far: the network manager and the Bluetooth devices (findings 65 and 66, Resolved), and in this pass the print queue, the torrent list, the streams list and the ToDo list. Still hand-painted: mail headers, the stock search, the benchmark results and the password list.
   - What the conversions cost, and how it was won back: a table row had one colour, so `Table` gained an optional `RowForeground` (an errored print job, an interface that is not up and an overdue or finished task keep their colour), and its empty text now wraps over as many rows as it needs (the torrent hint keeps its wrapped form on a narrow terminal).

69. **The Torrent manager lays its list out with a magic width and has no sort** - Severity: **Low**. Kind: maintainability, usability.
   - Where: [TorrentManagerDialog.cs:617](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs) (`int nameWidth = Math.Max(10, iw - 2 - 2 - 53);`), `:31` (`ListRows = 14`), and the footer: `Hints()` draws the list keys inside `Draw` (`:600`), while `GetKeyHints` (`:114`) lists the same keys for the shared footer.
   - Problem: the 53 is the sum of the other columns and the gaps, so changing a column means changing this number. The list cannot be sorted, which matters once a dozen torrents are in it (the list is fixed at 14 rows and scrolls). The two key lists can drift, and nothing checks that they agree. Heuristic: Consistency, Maintainability.
   - Improvement: the list is now a `Table<TorrentItem>`, so the width comes from its columns. Still to do: wire a sort on Name, State and Done through `Table.SortRequested`, and keep one key source (`GetKeyHints`) instead of `Hints()`. Test: the footer names the same keys `Hints()` names, until `Hints()` is removed.
   - Effort: S. **Unverified**: whether `Draw` and the base footer both paint the key line today; a render dump of the Torrent dialog would settle it.
   - Status: open, partly fixed. The magic width and the hand-painted rows are gone (this pass); the sort and the second key source remain.

70. **The System information dialog is a key/value list written as text, with no key hints of its own** - Severity: **Low**. Kind: consistency, accessibility.
   - Where: [SystemInfoDialog.cs:191](../../iTerminal.Core/Dialogs/SystemInfoDialog.cs) (`Label` pads to a width that grows with the longest label), `:198` (`Header`), `:244` to `:273` (each row is one `AppendLine`), and the class has no `GetKeyHints` override.
   - Problem: the rows are key/value pairs, which `KeyValueList` already draws in its own key column (`KeyWidth`), so the dialog could leave the alignment to the control. The text version guesses a label width instead, and a German label longer than the guess moves the values out of line (the comment at `:189` records one such case). The dialog lists no keys at all, so a user does not learn that Esc closes it or that the text scrolls. Heuristic: Consistency, Discoverability.
   - Improvement: build the rows as `KeyValueList` items with `KeyWidth` set to the widest label, and add the Esc and scroll hints through `GetKeyHints`. The gauge and the trend stay as they are.
   - Effort: S to M. **Unverified**: the scroll keys' hint text needs a check against the text viewer's own hints.
   - Status: open.

### What works well

- The Network manager and the Bluetooth dialog keep their state through a refresh: Bluetooth puts the cursor back on the same device by address after a re-read (`ApplyPendingDevices`), and both use the collection's `ReplaceAll`, which raises one change.
- The dialogs that refresh do so from `OnIdle`, and the workers only write a field that the UI thread takes in. No dialog in the set mutates the control tree from a worker, as far as the signal pass and the reads show.
- Hard-coded labels are rare. The only ones found in the set are the brand titles ("BitTorrent", "Git") and the placeholder examples, which are meant to be literal.

### Research cross-reference

No research overview covers dialog tables. The console overview's table entries (the sortable grid and the virtual table) are the reference for finding 68: the `Table<T>` binding is what those entries describe, and the remaining dialogs are the ones that do not use it.

### Suggested order

1. Quick wins (S): finding 70 (key hints and `KeyValueList`), and finding 69 once finding 68 has converted the Torrent list.
2. Medium: finding 68, Print queue first, since it is live and the queue is what a user watches.

## Resolved

Findings that a status check found fixed. Each keeps its number from the table above.

- **11. Ctrl+Shift+X opens "Add evidence" and also is the password manager's chord** - Scope: Recent UI changes: key map, grid queries, evidence notebook, tab progress, folder selector. Fixed by #695 (`6e8fc104`): Add evidence moved to Ctrl+Shift+Delete ([EvidenceCommands.cs:179](../../iTerminal.Core/Commands/Evidence/EvidenceCommands.cs)), [HotKeyCollisionTest.cs:84](../../iTerminal.Core.Test/Commands/HotKeyCollisionTest.cs) asserts the new key, and the password manager keeps Ctrl+Shift+X ([PasswordManagerDialogCommand.cs:19](../../iTerminal.Core/Commands/PasswordManagerDialogCommand.cs)). Open remark, not a numbered finding: Ctrl+Shift+Delete reads as a delete chord for an add command.
- **15. Every query line stops at 256 characters, and a pasted query is cut silently** - Scope: Recent UI changes: key map, grid queries, evidence notebook, tab progress, folder selector. Fixed: `GridQueryCompletionModel.Attach` sets `MaxLength = 8192` ([GridQueryCompletionModel.cs:26](../../iConsole/Tabular/GridQueryCompletionModel.cs), `:34`) and all four viewers call it ([CsvViewerDialog.cs:75](../../iConsole/Dialogs/CsvViewerDialog.cs), [ParquetViewerDialog.cs:68](../../iConsole/Dialogs/ParquetViewerDialog.cs), [HdfViewerDialog.cs:95](../../iConsole/Dialogs/HdfViewerDialog.cs), [SqliteViewerDialog.cs:76](../../iConsole/Dialogs/SqliteViewerDialog.cs)). The bracketed-paste part is finding 5, which stays open.
- **21. The shared grid still cuts and pads by characters, so CJK and emoji data misalign** - Scope: Recent UI changes: key map, grid queries, evidence notebook, tab progress, folder selector. Fixed in `CsvGrid`, which now uses `TextWidth.Of` and `TextWidth.Fit` (`CsvGrid.cs:75`, `:281`, `:300`). The other controls stay open under finding 4, and the folder selector rows under finding 25.
- **22. Enter runs the query whatever has focus (CSV, Parquet, SQLite); HDF5 checks focus** - Scope: Recent UI changes: key map, grid queries, evidence notebook, tab progress, folder selector. Fixed for CSV, Parquet and SQLite, which now test `_queryBox.IsFocused` (`CsvViewerDialog.cs:433`, `ParquetViewerDialog.cs:383`, `SqliteViewerDialog.cs:425`); HDF5 already did.
- **65. The network interfaces are text rows, and a status goes stale until the dialog is reopened** - Scope: Dialog set: tables, live data and labels across the dialogs. Fixed: `NetworkManagerDialog` is a `Table<NetworkInfo>` bound to `NetworkInfoModel.Networks`, so a status is read on each draw ([NetworkManagerDialog.cs](../../iTerminal.Core/Dialogs/NetworkManagerDialog.cs)). Columns Name, Status and Description carry headers. Tests: [NetworkManagerDialogTest.cs](../../iTerminal.Core.Test/Dialogs/NetworkManagerDialogTest.cs) (an interface added while the dialog is open is drawn without reopening; the columns fit the width at 60 and 120). Open remark: non-Up interfaces used to be dimmed; `Table` has no per-row disabled state, so they are not dimmed now.
- **66. The Bluetooth device list has no column headers, and its rows are formatted once** - Scope: Dialog set: tables, live data and labels across the dialogs. Fixed: the list is a `Table<BluetoothDevice>` bound to the device collection, with Name, Address and State headers ([BluetoothDeviceManagerDialog.cs](../../iTerminal.Core/Dialogs/Bluetooth/BluetoothDeviceManagerDialog.cs)). The state is read per draw. The tests that asserted on the formatted row text now render the dialog and compare the header column with the value column. The highlight still follows the device across a refresh.
- **67. The link target line says "to" in English** - Scope: Dialog set: tables, live data and labels across the dialogs. Fixed: [CreateLinkDialog.cs:138](../../iTerminal.Core/Dialogs/CreateLinkDialog.cs) reads "to" from the existing `Task.To` key, so the German, French, Spanish, Italian and Hungarian builds show the translated word.
