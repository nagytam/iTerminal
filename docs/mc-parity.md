# File manager parity review

Originally a Midnight Commander (mc) comparison; now a comparison with the file managers a
user might leave for iTerminal: mc, Total Commander (TC), Far Manager (Far), Double Commander
(DC), Directory Opus (Opus), Free Commander (FC), and the desktop shells (Explorer, Finder,
Nautilus/Dolphin). The file keeps its name because other documents link to it.

**How it was checked.** Refreshed on branch `ux-fixes-batch-10` (after #167) from the
architecture docs and by looking for the feature in `iTerminal.Core` and `iConsole` (types,
commands, dialogs, settings). Nothing was run interactively. A gap marked **confirmed** means
no type, command or setting for it exists; **unverified** means it may exist under a name the
search did not find, or may behave differently from the competitor's version. The previous
review's mc gaps are all reconciled in "Closed since the first review".

## What iTerminal has that most managers lack

| Area | Detail |
| --- | --- |
| Drives | `ssh`, `smb`, `ftp`/FTPS, `dav`/`davs`, `mtp`, `s3`, `onedrive`, `gdrive`, `iphone`, `docker`, `git` (browse any revision), `archive` (zip/tar/7z/rar/iso/cab), `recycle`, `search`, `preview`, memory drive |
| Safety | Recycle-bin and freedesktop-Trash browsing with restore, secure wipe, free-space guard before a copy, no recursive delete through links, y/n on destructive actions, SSH/FTPS host pinning |
| Integrity | Checksums (MD5/SHA-1/256/512) with automatic `.sha256` / `SHA256SUMS` verification, signing (gpg, Authenticode, key file), duplicate finder, disk usage |
| Comparison | Folder compare ending in marks, two-way and one-way sync with plan-then-confirm, text and binary diff |
| Metadata | Colour labels and tags with filtering, EXIF/XMP/IPTC stripping, image metadata editor, music tag editor |
| Beyond files | Git client, Docker, solution viewer, package manager, AI bar, translator, currency converter, mail, web browser, map, music/video players, document index, password manager |
| Terminal-native | Works over SSH on a headless box; lends the whole terminal to a shell (Ctrl+O) |

## Closed since the first review

Hex view in F3 (F4 toggle, go to offset), user menu (Shift+F10), Ctrl+Space folder size, FTP,
edit link, panel sort modes and the Panels settings page, command-line helpers (name
insertion, history, `cd`), folder selector (hotlist, history, tree, places), background-task
queue (Task Manager: pause, reorder), editor undo/redo/replace/bookmarks/syntax colouring and
viewer scrolling fixes (#164), workspace manager (named layouts of panel locations, sort,
bottom bar), colour labels, file clipboard, find large/old files, git pull --rebase.

## Feature matrix

`Y` present, `P` partial, `-` absent (confirmed), `?` unverified. Competitor columns are from
general knowledge of each product and are approximate.

| Feature | iTerminal | mc | TC | Far | DC | Opus | Explorer |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Dual panes, F-key workflow | Y | Y | Y | Y | Y | Y | - |
| **Tabs per panel** | **-** | - | Y | - | Y | Y | Y |
| **Multi-rename (patterns, counters, regex, preview)** | **-** (single rename only) | - | Y | Y | Y | Y | P |
| Marks by pattern, invert | Y | Y | Y | Y | Y | Y | P |
| **Conflict prompt (overwrite/skip/rename/newer/all)** | **P** (Overwrite checkbox, error prompt) | Y | Y | Y | Y | Y | Y |
| Verify after copy | **-** (noted as missing) | - | Y | - | Y | Y | - |
| Resume / bandwidth limit for transfers | - | - | Y | - | P | Y | - |
| Undo of rename / move / copy | **-** | - | - | - | - | Y | Y |
| Queue, pause, reorder | Y | P | Y | P | Y | Y | - |
| **Brief / Long / Thumbnail panel modes** | **-** (one layout) | Y | Y | Y | Y | Y | Y |
| Tree panel, Info panel as panel modes | - (tree only in selector) | Y | Y | Y | Y | Y | Y |
| Custom columns (EXIF, tags, hash, ...) | - | P | Y | Y | Y | Y | Y |
| Horizontal split, panel ratio, hide a panel | **?** (workspaces save locations, not geometry) | Y | Y | Y | Y | Y | - |
| Synchronised browsing / mirror panels | - | - | Y | - | Y | Y | - |
| Folder hotlist | Y (Favourites) | Y | Y | Y | Y | Y | Y |
| Aliases (`@home`, `docs:`) / jump-by-key bookmarks | - | - | P | P | P | Y | - |
| Quick filter while typing | P (type-to-search) | Y | Y | Y | Y | Y | Y |
| Index-backed instant search | **?** (`FolderIndexJob` exists; Find walks the disk) | - | P | - | - | P | Y |
| Saved searches / virtual result panels | P (`search://` lives with the run) | - | Y | - | Y | Y | Y |
| Archives as folders | Y (read), compress/extract | Y | Y | Y | Y | Y | P |
| Write into an archive in place | P (rewrites ZIP/TAR) | P | Y | Y | Y | Y | P |
| Remote protocols | Y (8 + phone) | Y | Y | Y | Y | Y | P |
| Site manager UI for saved connections | Y (SSH, PowerShell, FTP, WebDAV with a stored password; no S3/SMB form yet) | Y | Y | Y | Y | Y | P |
| Viewer: text, hex, search, image | Y | Y | Y | Y | Y | Y | P |
| Editor with syntax colouring (also in the viewer, wrapped or not) | Y (indent, auto-indent, undo, replace, bookmarks, save as read, unsaved-changes prompt) | Y | P | Y | Y | - | - |
| Internal 3-way merge | - | - | - | - | - | - | - |
| Command-line integration | Y | Y | Y | Y | Y | Y | P |
| User menu / custom commands | Y | Y | Y | Y | Y | Y | - |
| **Scripting / plugin API** | **-** | - | Y (WCX/WFX/WLX/WDX) | Y (Lua, .NET) | Y (Lua) | Y | - |
| **Key remapping / profiles** | **-** (no user keymap) | Y | Y | Y | Y | Y | - |
| Themes | Y | Y | Y | Y | Y | Y | P |
| chown / owner / ACL editing | Y (NTFS ACL + owner; POSIX ACL, owner and group) | Y | Y | Y | Y | Y | Y |
| NTFS streams, junction creation, VSS | - | n/a | P | P | P | Y | P |
| Shell context menu (Explorer verbs) | - | n/a | Y | - | Y | Y | Y |
| Drag and drop to/from other apps | - | - | Y | - | Y | Y | Y |
| Thumbnails / graphical preview | - (ASCII art in the video/image viewer) | - | Y | - | Y | Y | Y |
| Headless / CLI automation | - | P | P | P | P | Y | - |
| Accessibility (screen reader) | - | - | Y | P | Y | Y | Y |

## Gaps, in priority order

Priority: **P1** the reason people stay with Total Commander, Opus or Explorer; **P2** expected
by power users; **P3** polish or niche.

### P1

1. **Multi-rename.** Confirmed absent: `RenameDialog` renames one entry. Needs masks (`[N]`, `[E]`,
   `[C]`, `[d]` dates, EXIF/tag tokens), search/replace with regex, case changes, a live
   old/new preview, conflict detection, and one-key undo. Acts on marks, so it fits `ActionableUrls`.
2. **Panel tabs.** Confirmed absent. The workspace manager swaps whole layouts; tabs are many
   locations per panel with history, lock and duplicate. Probably a `PanelTabsService` in
   front of the `ContentModelService` (which assumes one model per panel name).
3. **Conflict handling in copy/move.** Only the "Overwrite" checkbox and an error prompt were
   found. Add per-file Overwrite / Skip / Rename / Keep newer / Keep larger / Resume, each with
   "all", and a verify-after-copy option (hash compare, reusing `ChecksumService`).
4. **Undo journal for rename, move and copy.** Delete is already recoverable; the rest is not.
5. **Panel listing modes.** Brief (multi-column), Long (dates, attributes, owner), Info and Tree
   panels. `ContentBrowser` draws one layout. Thumbnails could use the Kitty/Sixel graphics
   protocols with an ASCII fallback.
6. ~~**Owner and ACL editing.**~~ **Done**: Ctrl+P in Properties opens `AccessControlDialog` (NTFS entries with
   allow/deny and inheritance, owner; POSIX ACL entries, owner and group). See file-operations.md.
7. **Site manager for remote profiles.** FTP and WebDAV (HTTP/HTTPS) are done in `RemoteConnectionManagerDialog`, with
   the password in the password manager. Still to do: S3 and SMB forms, FTP passive/active and TLS-mode choices.

### P2

8. **Search that is fast and reusable.** Check whether `FolderIndexJob`'s index can answer a
   name search instantly; add regex, saved searches, and results that stay as a panel.
9. **Panel geometry.** Horizontal split, width ratio, hide one panel; extend `WorkspaceLayout`
   to carry them so a workspace restores the whole screen.
10. **Custom columns and sort by them** (extension, tags, label, EXIF date, hash, owner).
11. **Key remapping with presets** (mc, Total Commander, Far). Needs a keymap file and a
    conflict report against the rules in CLAUDE.md (no Alt+letter, Ctrl+Alt is typing).
12. **Aliases and synchronised browsing.** `@name` / `home:` jump targets and a mirror-panels toggle.
13. **Scripting / plugin layer** over commands, drives and columns (Lua or a .NET plug-in
    folder). Keep the existing rule that plug-ins never block key handlers.
14. **Windows depth:** NTFS alternate data streams, junction creation, hard-link count column,
    shadow-copy browsing, Explorer context-menu verbs through a shell bridge.
15. **Transfer extras:** bandwidth limits and resume for remote copies.
16. **Quick cd path box** on a chord that is not Alt+letter (mc P2 item 9).

### P3

17. 3-way merge inside the file diff, and in the Git client (the docs list in-pane conflict
    resolution as not built); more cloud providers (Dropbox, Box, B2, Azure Blob) on
    `HttpFileSystemServiceBase`; drag and drop; accessibility mode; a headless CLI
    (`iterminal sync|find|dedupe`) over the sync, compare and duplicate engines.
18. Real-device verification of what is marked unrun in the architecture docs: MTP backends,
    Unix elevation, live OneDrive/Google/S3, gpg and certificate signing, Linux clipboard.

## Suggested order

1. Multi-rename, conflict prompt with verify, panel tabs. These decide whether a Total Commander
   or Opus user stays.
2. Listing modes (Brief/Long first), owner editing, undo journal.
3. Site manager, then search and saved searches.
4. Panel geometry, custom columns, key remapping.
5. Scripting layer, aliases, Windows depth.

Each item needs end-to-end coverage in `iTerminal.Test` (a unit test of a control alone does
not prove key routing) and, if it touches the real machine, a `SandboxServices` replacement.
