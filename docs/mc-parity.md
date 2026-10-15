# Midnight Commander (mc) parity review

Reviewed against the repository at branch `feature/dependency-selection`. This is a
survey of commands, menus, dialogs and the viewer/editor controls; nothing was run
interactively, so items marked "partial" or "missing" should be confirmed in the app
before work starts.

## Already covered

| mc feature | iTerminal |
| --- | --- |
| Two panels, Tab to switch, Ctrl+U swap | `FilePanel` x2, `SwitchPanelsCommand` |
| F3 view, F4 edit, F5 copy, F6 move/rename, F7 mkdir, F8 delete, F10 quit | all bound (mkdir is Shift+F7, F7 is find) |
| Multiple selection: Insert, `+`, `-`, `*` | `Commands/Marks/*`, pattern dialog |
| Find file | F7 `FindFilesDialog` (name + content), results as `search://` panel |
| Compare directories | Shift+F2 `CompareFoldersDialog`, ends in marks |
| Sort order, hidden files | Shift+F11, Panels settings page |
| Symlinks / hard links | Ctrl+L `CreateLinkDialog` |
| Properties / chmod | Ctrl+F12 (permission bits as octal on Unix, attributes on Windows) |
| Subshell | Ctrl+O `ConsoleOwnership` lend, Ctrl+Shift+O command log |
| Archives as folders | `archive://` (zip/tar/7z/rar/iso/cab), compress / extract |
| VFS: sftp (`ssh://`), smb, tar/zip | `ssh://`, `smb://`, `archive://` |
| Quick view panel | Ctrl+F3 `preview://` |
| Directory tree | Folder selector "Folder Tree" page |
| Directory hotlist | Folder selector "Favourites" page (Ctrl+F1) |
| Directory history | Folder selector "History" page |
| File associations (mc.ext) | Open with / Edit with, Enter/F4 defaults |
| Skins | Theme manager (Alt+F6) |
| Command palette, menu bar, hot key bar, mouse | present |

## Done since this review

- Hex view in F3 (F4 toggles) - item 1 below.
- User menu (Shift+F10, `usermenu.txt`) - item 5.
- Ctrl+Space folder size - item 7.
- FTP drive (`ftp://`) - item 12.

## Gaps

Priority: **P1** daily-use mc features, small/medium cost; **P2** expected by mc users,
larger; **P3** niche or already adequately substituted.

### P1

1. **Hex view in F3.** `iConsole/Controls/HexViewer.cs` exists with tests but nothing
   outside its own file references it. Wire it into `FileViewerDialog` with an F4
   toggle (mc: F4 hex/ascii), plus Go to offset (F5 in mc).
2. **Viewer: go to line, wrap toggle (F2), search forward/back, search-hit navigation.**
   Search exists (`FindNext/FindPrevious`); no goto-line or wrap toggle found.
3. **Editor completeness (F4 / mcedit).** `TextEditorControl` registers only type,
   backspace, delete char, delete line. Missing: selection (Shift+arrows / F3 mark),
   copy/cut/paste (F5/F6/F8 block ops), undo/redo, replace (F4 in mcedit), go to line
   (Alt+L), insert file, save-as. Syntax highlighting is not present anywhere.
4. **chown.** Properties writes mode bits only. Add owner/group change (Unix), like
   mc's Ctrl+X O / Ctrl+X C pair. Windows: show owner read-only.
5. **User menu (F2).** mc's F2 is a user-defined menu (`mc.menu`: per-file-type and
   global entries with macros `%f %d %s`). Here F2 is the terminal. Add a user menu
   stored in the settings DB or a JSON file, reusing the `FileAssociation` command-line
   builder (`%f %d %n %b`); suggest Shift+F2-adjacent free key or the Tools menu.
6. **Panel listing modes.** mc's Full / Brief (multi-column) / Long / User-defined, and
   the Info panel. Panels have one layout only. Add Brief (2-3 columns) and Long first.
7. **Directory sizes on demand (Ctrl+Space).** mc computes the folder under the cursor
   and shows it in the size column. Reuse `FilePropertiesService.MeasureAsync`.
8. **Command-line niceties.** Alt+Enter / Ctrl+Enter copy file name to the command
   line, Ctrl+X P/T paths, Alt+P/Alt+N command history, Ctrl+Enter, `cd` on the
   command line changing the panel. Confirm what the terminal bar already does.

### P2

9. **Quick cd (Alt+C).** Type a path on the command line. Alt+letter is claimed by the
   panel's type-to-search (see CLAUDE.md "Choosing a global hot key"), so choose
   another chord.
10. **Panelize (external panelize).** Turn a command's output (`find ... | grep`) into
    a panel. `search://` already shows the shape: add an `ExternalPanelizeDialog` that
    fills a `ContentModelSearchResults`-like model from `file://` lines.
11. **Background jobs list (Ctrl+X J).** Task manager (F9) covers running tasks; check
    that copy/move can be sent to background as in mc.
12. **FTP / FISH VFS.** Only SSH (sftp) and SMB exist. FTP/FTPS is the notable missing
    scheme; follows the four-piece "adding a scheme" checklist in CLAUDE.md.
13. **Panel split and layout.** Horizontal split, panel width ratio, hide one panel,
    panels on/off (Ctrl+O in mc, here a shell). Currently fixed 50/50 vertical.
14. **Tree panel and Info panel as panel modes** (mc Ctrl+X T, Ctrl+X I). Folder tree
    exists only inside the folder selector.
15. **Edit symlink / relative symlink retarget.** Create exists; no edit of an
    existing link's target.
16. **Learn keys / key remap.** No user keymap file; Claude Code style keybindings
    for iTerminal would be the equivalent.
17. **Confirmation / appearance options page** (confirm delete, overwrite, exit;
    verbose operation). Partly covered by individual dialogs; no single page.

### P3

18. Undelete (ext2/3 only), mc's `%` macros in the user menu beyond P1's set,
    tar-archive VFS write support, display bits / charset selection, `Ctrl+X A`
    VFS list, spelling in the editor, macros recording in the editor.

## Proposed implementation order

1. Wire `HexViewer` into the F3 viewer (toggle, goto offset) and add goto-line and wrap
   toggle. Smallest change, visible win.
2. Editor: selection + clipboard + undo/redo + replace + goto line. Then syntax
   highlighting as its own task (simple per-extension rules).
3. Ctrl+Space directory size + brief/long listing modes (`ContentBrowser` columns).
4. chown in the properties dialog and service.
5. User menu (F2-equivalent) on the file-association command builder.
6. Command-line helpers (file name insertion, history) after checking the terminal bar.
7. P2 items, FTP first, then panelize, split/layout, tree/info panels.

Each item should carry end-to-end coverage in `iTerminal.Test`, per the existing
suite's conventions (a unit test of the control alone does not prove key routing).

## Verify before starting

- Whether the terminal bar (Ctrl+B) already supports history and `cd`.
- Whether F5/F6 offer a "background" option.
- The exact F3 viewer capabilities beyond search (the `FileViewerDialog` has no
  registered commands of its own; they may come from `TextViewerControl`).
