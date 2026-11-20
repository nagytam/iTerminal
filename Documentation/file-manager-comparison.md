# File manager comparison and feature gaps

A UI/UX review of iTerminal against the file managers a user might compare it with, and a
list of the features those managers have that iTerminal does not yet have. It extends
[mc-parity.md](mc-parity.md), which gives the prioritized gap list; this document goes wider
(GUI shells, more TUI managers, and interaction design) and records what each feature costs in
keystrokes, discoverability and terminal constraints.

**Managers compared.** Terminal: Midnight Commander (mc), Total Commander (TC), Far Manager
(Far), Double Commander (DC), Free Commander (FC), Ranger, Yazi. Desktop: Directory Opus (Opus),
Windows Explorer, Finder, Nautilus and Dolphin (combined as "GNOME/KDE").

**How it was checked.** iTerminal's column was checked against the source on branch `main`
(types, commands, dialogs, settings, services), not by running the app. A cell marked `P`
(partial) means the feature exists in a reduced form, described in the notes. The competitor
columns come from general knowledge of each product and are approximate; they are not
verified against current releases. Where iTerminal's answer depends on a design choice rather
than a missing feature, the notes say so.

Legend: `Y` present, `P` partial, `-` absent, `n/a` not applicable to a terminal, `?` not
verified.

## 1. What the comparison shows

- **iTerminal is strong where most managers are weak:** remote and virtual drives (SSH, SMB,
  FTP/FTPS, WebDAV, MTP, cloud, Docker, git revisions, archives, recycle bin and freedesktop
  Trash), integrity (checksums with automatic verification, signing, a duplicate finder),
  folder comparison with plan-then-confirm sync, and built-in document, media and developer
  tools that other managers do not ship.
- **Most of the classic dual-pane workflow is in place:** function-key bar, marks by pattern,
  Brief and Long listings, a multi-rename dialog with a live preview, a conflict prompt with
  apply-to-all, verify-after-copy, rename undo, a plug-in loader for viewers and editors, and a
  command palette.
- **The biggest gaps are layout and customisation.** iTerminal has one panel layout, one model
  per panel, no tabs, no user keymap, no scripting API for commands or columns, and no way to
  change the arrangement of the screen beyond the workspace manager's saved locations. These
  are what Total Commander, Far and Directory Opus users notice first.
- **Several GUI-shell conveniences are absent for structural reasons,** not oversight: drag and
  drop between windows, shell context-menu verbs, thumbnails in the panel, and graphical quick
  look. The terminal offers only partial substitutes (Sixel images in the viewer, the Preview
  drive, mouse selection). These are design decisions to revisit, not bugs.

## 2. Feature matrix

### 2.1 Panels and layout

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Dual pane | Y | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Single pane / tree-only view | P (one panel per side, can be a Preview) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Tabs per panel | - | - | Y | - | Y | Y | Y | Y | Y | Y | Y (Ranger) |
| Horizontal split (panels stacked) | - | Y | Y | Y | Y | Y | - | - | - | - | - |
| Adjustable panel width ratio | - | Y | Y | Y | Y | Y | Y | - | - | Y | Y |
| Hide one panel | - | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Brief (multi-column names) | Y (Ctrl+T) | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Long (attributes column) | Y (palette) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Full detail (size, date, attributes) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Info panel (other panel's entry details) | Y (palette) | Y | Y | Y | Y | Y | Y | - | Y | - | - |
| Tree panel (folder tree as a panel) | P (in folder selector only) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Thumbnails in panel | - | - | Y | - | Y | Y | Y | Y | Y | Y | Y (Yazi) |
| Quick view of the cursor file in the other panel | P (Preview drive, Ctrl+F3 toggle) | Y | Y | Y | Y | Y | Y | - | Y (Space) | Y | Y |
| Custom columns (extension, tags, hash, EXIF, owner) | - | P | Y | Y | Y | Y | Y | Y | Y | Y | - |
| Sort by any column, reversed | Y (sort modes, Panels page) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Group by folder, date or type | - | - | P | - | P | Y | - | Y | Y | Y | - |
| Synchronised (mirror) browsing | - | - | Y | - | Y | Y | - | - | - | - | - |
| Breadcrumb path bar, editable | P (path in the title, editable via the folder selector) | - | Y | - | Y | Y | Y | Y | Y | Y | - |
| Per-panel back / forward history | P (folder history in the folder selector) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |

### 2.2 Navigation and bookmarks

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Favourites / hotlist | Y (folder selector) | Y | Y | Y | Y | Y | Y | Y (Quick access) | Y | Y | Y (marks) |
| Jump-to-bookmark by one key | - | - | Y | Y | Y | Y | - | - | - | - | Y (`'x`) |
| Named aliases (`@home`, `docs:`) | - | - | P | P | P | Y | - | - | - | - | - |
| Recent folders list | Y (folder history) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Type-to-search / quick filter | P (type-to-search, Alt+printable) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Command-line path completion | Y | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Quick cd path box on a chord | - | Y | Y | Y | Y | Y | - | - | - | - | Y |
| Go to parent with cursor kept on the child | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Address bar that also accepts URLs | Y (remote URL schemes) | - | Y | - | Y | Y | Y | Y | Y | Y | - |

### 2.3 Selection and batch work

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Marks, invert, mark by pattern | Y | Y | Y | Y | Y | Y | Y | P | P | P | Y |
| Mark by attribute / size / date | P (find large or old) | - | Y | Y | Y | Y | Y | - | - | - | - |
| Multi-rename with tokens | P (`[N]`, `[N:n]`, `[C]`, `[E]`, plain Find/Replace) | - | Y | Y | Y | Y | Y | P | - | Y | - |
| Multi-rename with regex, case change, date and EXIF tokens | - | - | Y | Y | Y | Y | Y | - | - | - | - |
| Multi-rename undo | P (one step) | - | Y | - | Y | Y | Y | - | - | - | - |
| Batch attribute / timestamp change | P (properties, one entry or marks) | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Create many files from a template | - | - | - | - | - | Y | - | - | - | - | - |

### 2.4 File operations and safety

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Copy / move / delete with confirmation | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Recycle bin / Trash with restore | Y | P (Trash) | Y | P | Y | Y | Y | Y | Y | Y | Y |
| Secure wipe | Y | - | Y | - | Y | Y | - | - | - | - | - |
| Per-file conflict prompt (overwrite, skip, rename, keep newer, keep larger) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | - |
| Apply a conflict choice to all | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | - |
| Verify after copy (hash compare) | Y | - | Y | - | Y | Y | - | - | - | - | - |
| Resume interrupted copy | - | - | Y | - | P | Y | - | - | - | - | - |
| Bandwidth limit for transfers | - | - | Y | - | P | Y | - | - | - | - | - |
| Background queue, pause, reorder | Y (Task Manager) | P | Y | P | Y | Y | - | - | - | - | - |
| Free-space check before copy | Y | - | Y | - | Y | Y | Y | Y | Y | Y | - |
| Undo of rename and of dialog moves | P (one step, Ctrl+Z) | - | - | - | - | Y | - | - | - | - | - |
| Undo of copy and delete | - | - | - | - | - | Y | - | - | - | - | - |
| Symbolic and hard link creation | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| NTFS junction creation | - | n/a | P | P | P | Y | P | P | n/a | n/a | n/a |
| NTFS alternate data streams | - | n/a | P | P | P | Y | P | - | n/a | n/a | n/a |
| Hard-link count column | - | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Owner and ACL editing | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | - |
| Split and join files | - | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Checksums with `.sha256` / `SHA256SUMS` verification | Y | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Signing (gpg, Authenticode, key file) | Y | - | - | - | - | - | - | - | - | - | - |
| Duplicate finder | Y | - | Y | Y | Y | Y | Y | - | - | - | - |
| Disk usage | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | - |
| File associations / open with | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |

### 2.5 Comparing and syncing

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Folder compare, marks the differences | Y | - | Y | Y | Y | Y | Y | - | - | - | - |
| One-way and two-way sync with plan | Y | - | Y | Y | Y | Y | Y | - | - | - | - |
| Text diff | Y | P (external) | Y | Y | Y | Y | Y | - | - | - | - |
| Binary diff | Y | - | Y | Y | Y | Y | - | - | - | - | - |
| Three-way merge | - | - | - | - | - | - | - | - | - | - | - |

### 2.6 Search

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Name search (wildcards) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Content search | Y | P | Y | Y | Y | Y | Y | P | Y | Y | Y (fzf) |
| Regex in name and content | P | - | Y | Y | Y | Y | Y | - | - | - | - |
| Index-backed instant search | ? (`FolderIndexJob` exists; Find walks the disk) | - | P | - | - | P | P | Y | Y | Y | - |
| Results as a panel (virtual folder) | P (the search drive lives with the run) | - | Y | Y | Y | Y | Y | P | P | Y | - |
| Saved searches | - | - | Y | - | Y | Y | Y | Y | Y | - | - |
| Search inside archives | Y | P | Y | Y | Y | Y | Y | - | - | - | - |

### 2.7 Archives and remote locations

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Archives as folders (read) | Y (zip, tar, 7z, rar, iso, cab) | Y | Y | Y | Y | Y | Y | P | P | Y | P |
| Write into archives in place | P (rewrites zip and tar) | P | Y | Y | Y | Y | Y | - | - | - | - |
| Create archives, encrypted and split | Y (compress; encryption in file operations) | Y | Y | Y | Y | Y | Y | Y | Y | Y | - |
| SSH / SFTP | Y | Y | Y | Y | Y | Y | Y | - | - | Y | - |
| SMB / network shares | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | - |
| FTP / FTPS / WebDAV | Y | Y | Y | Y | Y | Y | Y | P | P | Y | - |
| Cloud (OneDrive, Google Drive, S3) | Y (OneDrive, Google Drive; S3 drive, no profile form) | - | P (plug-ins) | P | P | Y | - | Y | Y | Y | - |
| Phone / MTP | Y | P | P | - | P | Y | - | Y | Y | Y | - |
| Saved site manager with stored credentials | Y (SSH, PowerShell, FTP, WebDAV; SMB and S3 forms missing) | Y | Y | Y | Y | Y | Y | - | - | Y | - |
| Keep-alive and reconnect | ? | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Git revisions as folders | Y | - | - | - | - | - | - | - | - | - | - |
| Docker containers as folders | Y | - | - | - | - | - | - | - | - | - | - |
| Recycle bin as folder | Y | - | Y | - | Y | Y | Y | Y | Y | Y | - |

### 2.8 Viewing and editing

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Text viewer with search and wrap | Y | Y | Y | Y | Y | Y | Y | - | - | - | Y |
| Hex view with edit | P (hex view, edit not verified) | Y | Y | Y | Y | Y | Y | - | - | - | - |
| Image viewer | Y (ASCII art or Sixel) | P | Y | Y | Y | Y | Y | Y | Y | Y | P (Kitty and Sixel) |
| Built-in editor with syntax colouring | Y | P | Y | Y | Y | Y | Y | - | - | - | - |
| Plug-in viewers and editors (F3, F4) | Y (loader, plug-in folder) | - | Y (WLX, WCX) | Y (Lua) | Y (Lua) | Y | - | - | - | - | - |
| Custom viewer per file type | P (plug-ins) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| PDF, Office, audio and video preview | Y (document index, music and video players) | - | P | P | P | Y | P | Y | Y | Y | P |

### 2.9 Integration and extensibility

| Feature | iTerminal | mc | TC | Far | DC | Opus | FC | Explorer | Finder | GNOME/KDE | Ranger/Yazi |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Command line under the panels | Y | Y | Y | Y | Y | Y | Y | - | - | - | Y |
| User menu / custom commands | Y | Y | Y | Y | Y | Y | Y | - | - | - | Y |
| Command palette | Y | - | Y | Y | Y | Y | - | Y | Y | Y | - |
| Key remapping / keymap file | - | Y | Y | Y | Y | Y | Y | - | - | - | Y (config file) |
| Scripting / plug-in API for commands and columns | - | - | Y (WCX, WFX) | Y (Lua, .NET) | Y (Lua) | Y | - | - | - | - | Y (config files) |
| Themes | Y | Y | Y | Y | Y | Y | Y | P | P | Y | Y |
| Drag and drop to and from other apps | - | - | Y | - | Y | Y | Y | Y | Y | Y | - |
| Shell context menu verbs | - | n/a | Y | - | Y | Y | Y | Y | Y | Y | - |
| Clipboard of files (paste into another panel or app) | Y | - | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Mouse support (select, scroll, click to open) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |
| Headless / CLI automation | - | P | P | P | P | Y | - | - | - | - | P |
| Localisation | Y (6 languages, run-time switch) | P | Y | P | Y | Y | Y | Y | Y | Y | - |
| Accessibility (screen reader, high contrast) | - | - | Y | P | Y | Y | Y | Y | Y | Y | P |
| Help and hints on screen | Y (F1 getting started, footer hints) | Y | Y | Y | Y | Y | Y | Y | Y | Y | Y |

## 3. UX observations

These are the design points that decide whether a feature gap matters to a user. They are
written for whoever plans the next round of work.

1. **Keyboard-first density.** The F-key bar is the main discoverability surface for
   Total Commander, Far and mc users. Anything added should either fit an F-key or appear in the
   command palette with its key shown. The palette already shows keys, so new commands should
   go there first, then into the bar only if they are used every day.
2. **Terminal constraints shape the answers.** Drag and drop, shell verbs and thumbnails depend
   on a GUI process. Where iTerminal cannot match them, the better answer is a terminal-native
   equivalent: a Sixel or Kitty thumbnail strip with an ASCII fallback, a "send to clipboard"
   or "open in default app" action instead of drag, and a quick-view that renders in the other
   panel.
3. **Layout is the most visible gap.** Users of Total Commander and Directory Opus expect to
   stack panels, hide one, or drag a splitter. Workspaces already save locations; they should
   also save geometry so a saved layout restores the whole screen.
4. **Tabs should not be a second navigation model.** Panel tabs need the same key, the same
   footer and the same hotlist as the current panel, with a small tab strip that does not take
   a row from the listing when there is only one tab.
5. **Undo should match what the user sees.** Ctrl+Z currently undoes one rename or one dialog
   move. A user who moves five files with F6 and presses Ctrl+Z expects the batch back. A
   journal with a visible "Undo last N operations" entry would close that expectation gap.
6. **Keymap changes are a trust problem.** Remapping is requested by mc and Total Commander
   users, but the rules in `CLAUDE.md` (no Alt+letter hot keys, Ctrl+Alt is typing, Ctrl+M is
   Enter) must be enforced when a user binds a key. A keymap file with a conflict report is
   safer than free remapping.
7. **Accessibility is a gap to name, not hide.** The console renderer draws everything through
   one buffer, so a screen reader cannot read the panels today. A mode that prints the focused
   entry and the footer as plain lines would be the first step; it is not built.

## 4. Features not available in iTerminal yet

The list is ordered by how often a user who compares managers would notice the gap. "Source
check" says what was found in the code; "Build notes" says what the feature needs.

### 4.1 High priority

| # | Feature | Seen in | Source check | Build notes |
| --- | --- | --- | --- | --- |
| 1 | **Panel tabs** | TC, DC, Opus, FC, Explorer, Finder, Ranger | Confirmed absent: no tab type for panels; `Tab` exists only in `iConsole` for dialogs | A `PanelTabsService` in front of `ContentModelService`, which assumes one model per panel name. Each tab keeps its own model, history and lock. Key chord must follow the no-Alt+letter rule. |
| 2 | **Horizontal split and panel width ratio, hide one panel** | mc, TC, Far, DC, Opus, FC | Confirmed absent: one fixed side-by-side layout; `WorkspaceLayout` stores locations, not geometry | Extend `WorkspaceLayout` with orientation, ratio and a hidden flag; route resize through `Buffer.SyncLock`. |
| 3 | **Key remapping with presets** | mc, TC, Far, DC, Opus, FC, Ranger, Yazi | Confirmed absent: no user keymap; key handling is code-defined | A keymap file with mc, TC and Far presets, plus a conflict report that refuses Alt+letter hot keys and Ctrl+Alt chords (see CLAUDE.md). |
| 4 | **Thumbnails and graphical preview in the panel** | TC, DC, Opus, FC, Explorer, Finder, GNOME/KDE, Yazi | Partial: `SixelOverlay` and `SixelEncoder` exist for splash and image viewer, not for panels | Thumbnail column or strip using Sixel or Kitty graphics, ASCII fallback, cache keyed by path and mtime, decoded off the render thread. |
| 5 | **Scripting / plug-in API for commands and columns** | TC (WCX, WFX), Far (Lua, .NET), DC (Lua), Opus | Partial: `PluginManagerService` loads viewers and editors (F3, F4) only | Extend the plug-in contract to commands and custom columns. Plug-ins must not block key handlers (CLAUDE.md, 200 ms budget). |
| 6 | **Undo journal for copy, move and delete** | Opus, Explorer (recycle) | Partial: `RenameUndoService` is one step, for rename and dialog moves only | A bounded journal of operations with the source and target, with "Undo last N" in the footer. Delete already uses the recycle bin and is recoverable. |

### 4.2 Medium priority

| # | Feature | Seen in | Source check | Build notes |
| --- | --- | --- | --- | --- |
| 7 | **Brief, Long and Full in one place; tree as a panel mode** | mc, TC, Far, DC, Opus, FC, Ranger | Partial: Brief, Long and Info exist; tree exists only in the folder selector | Add tree as a `ContentBrowser` mode that reuses the folder selector's tree tab page. |
| 8 | **Quick view of the cursor file in the other panel** | mc, TC, Far, DC, Opus, FC, Finder, GNOME/KDE, Yazi | Partial: `TogglePreviewCommand` and the Preview drive show a file in a panel; not a key-driven quick view of the other panel | Make the Preview drive a mode that follows the other panel's cursor; keep the viewer's own keys. |
| 9 | **Custom columns and sort by them** | TC, Far, DC, Opus, FC, Explorer | Confirmed absent: fixed columns | Column registry (extension, tags, colour label, EXIF date, hash, owner, hard-link count); sort mode per column. |
| 10 | **Synchronised browsing (mirror panels)** | TC, DC, Opus | Confirmed absent | Toggle that makes the other panel follow the same relative path. Needs a rule for remote and archive panels. |
| 11 | **Aliases and jump-to-bookmark by one key** | TC, Far, DC, Opus, Ranger | Partial: folder selector has favourites and history; no one-key jump | Named aliases (`@name`) and a bookmark key list in the folder selector. Keys must not be Alt+letter. |
| 12 | **Multi-rename with regex, case change, date and EXIF tokens** | TC, Far, DC, Opus, FC | Partial: `MultiRenamePattern` has `[N]`, `[N:n]`, `[C]`, `[E]` and plain ordinal Find/Replace | Add regex Find/Replace with capture groups, case tokens (`[U]`, `[L]`), date tokens (`[d]`) and EXIF or tag tokens. The live preview and undo already exist. |
| 13 | **Resume and bandwidth limit for transfers** | TC, Opus, DC (partial) | Confirmed absent | Resume needs ranged reads on the remote drives (FTP REST, WebDAV range); a per-transfer rate limiter in the copy loop. |
| 14 | **Index-backed instant search and saved searches** | Explorer, Finder, GNOME/KDE, TC, Opus | Unverified: `FolderIndexJob` exists; Find walks the disk | Check whether the index answers name search; add regex, saved searches and results that stay as a panel. |
| 15 | **Shell context menu verbs and drag and drop** | Explorer, Finder, GNOME/KDE, TC, DC, Opus, FC | Confirmed absent | Windows: a shell bridge for verbs. Terminal: drag and drop is not available; offer "copy path" and "send to" instead. |
| 16 | **Accessibility mode** | Explorer, Finder, GNOME/KDE, TC, DC, Opus, FC | Confirmed absent | A linear text mode that prints the focused entry, the footer and the dialog in plain lines; high-contrast theme. |

### 4.3 Lower priority

| # | Feature | Seen in | Source check | Build notes |
| --- | --- | --- | --- | --- |
| 17 | Horizontal list of folders in the panel title (breadcrumb) | Explorer, Finder, GNOME/KDE, TC, DC | Partial: path in title | Clickable breadcrumb with mouse support already in the console. |
| 18 | Group by folder, date or type | Opus, Explorer, Finder, GNOME/KDE | Confirmed absent | Grouping headers in `ContentBrowser`; sort is in place, grouping is not. |
| 19 | Create many files from a template | Opus | Confirmed absent | Template dialog that uses the multi-rename token engine. |
| 20 | NTFS junction creation, alternate data streams, hard-link count column | TC, Far, DC, Opus, FC, Explorer | Confirmed absent | Windows-only; needs a Windows-only code path and tests that run on Windows. |
| 21 | Three-way merge | Opus (via external tools), DC | Confirmed absent | Also tracked in `mc-parity.md` item 17. |
| 22 | Headless CLI (`iterminal sync`, `find`, `dedupe`) | Opus, DC (partial) | Confirmed absent | Reuse the sync, compare and duplicate engines. |
| 23 | Keep-alive and reconnect for remote panels | TC, Far, DC, Opus, FC | Unverified | Check the remote services for reconnect behaviour before building. |
| 24 | Smaller remote gaps: S3 and SMB profile forms, FTP passive and active choice, TLS mode | TC, Far, DC, Opus | Confirmed: profile form exists for SSH, PowerShell, FTP and WebDAV only | Add to `RemoteConnectionManagerDialog`. |
| 25 | Hex view editing | mc, TC, Far, DC, Opus, FC | Partial: hex view exists, no edit | Edit in the hex view with a dirty flag and the existing unsaved-changes prompt. |

## 5. Features iTerminal has that the others mostly lack

For balance, and because these should be kept in any future comparison:

- Remote drives beyond the usual protocol set: Docker containers, git revisions, the recycle bin
  and freedesktop Trash, cloud drives, MTP, the iPhone drive, and a memory drive.
- Integrity tools built in: checksums with automatic verification of `.sha256` and
  `SHA256SUMS` files, signing with gpg, Authenticode and key files, a duplicate finder and disk
  usage.
- Plan-then-confirm folder sync, with a comparison that ends in marks.
- Terminal-native operation: works over SSH on a headless machine and can lend the whole
  terminal to a child shell (Ctrl+O).
- The editor, viewer, document index and media players in one application, with a password
  manager, a translator and a currency converter.

## 6. Suggested sequence

1. Panel tabs, horizontal split and hide-panel, because these decide whether a Total Commander
   or Opus user stays. They change layout code, so they should land with end-to-end coverage in
   `iTerminal.Test`.
2. Key remapping with presets, and a keymap conflict report. Rules in CLAUDE.md are the check.
3. Undo journal and an accessibility text mode.
4. Thumbnails, quick view and tree as a panel mode.
5. Custom columns, aliases, mirror browsing, resume and bandwidth limits.
6. Scripting API and headless CLI, after the command and column registries are stable.

Each item needs end-to-end coverage in `iTerminal.Test` and, if it touches the real machine, a
`SandboxServices` replacement (CLAUDE.md, "Tests must not touch the real machine").

## 7. Corrections to earlier reviews

[mc-parity.md](mc-parity.md) was written against an earlier branch. Checked against `main`:

- Multi-rename is present (`MultiRenameDialog`, `MultiRenamePattern`, `MultiRenamePlanner`),
  with `[N]`, `[N:n]`, `[C]`, `[E]`, plain Find/Replace, a live preview and one-step undo. It
  is not present with regex or case and date tokens.
- The conflict prompt is present: per file, with overwrite, skip, rename, keep newer and keep
  larger, and an apply-to-all choice (`FileConflictResolver`, `Conflict.*` strings).
- Verify-after-copy is present (`TransferDialog`, `TransferVerifier`, `TransferCommand`).
- Brief, Long and Info listings are present (`ContentBrowser.PanelListingMode`).
- Plug-ins are present for viewers and editors (`PluginManagerService`, F3 and F4). They are not
  a command or column API.
- Sixel is present for the splash screen and the image viewer, not for panel thumbnails.
- Undo is one step, for rename and dialog moves. Copy and delete are not undoable here.
