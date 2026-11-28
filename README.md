# iTerminal BETA

> **BETA:** iTerminal is in beta. Expect rough edges and changes between releases, and keep backups of anything you cannot afford to lose.

A two-panel, keyboard-driven file manager and developer toolbox for the terminal, written in C# on .NET 11. It runs on Windows and Linux and is built on `iConsole`, a reusable text-UI framework that lives in the same repository.

## Features

### File management
- Two panels with per-panel sort modes (name, extension, size, date; Shift+F11), hidden-file toggle and natural number sorting
- Copy, move, delete (with recycle bin), create folders and links (symbolic, junction display, hard), as background tasks with progress windows and completion notifications
- Marking rows (Insert, Shift+arrows, pattern mark) to act on several entries at once
- Compare folders (Shift+F2) and sync by marking differences
- Find files by name and content (F7), with results as a panel drive
- Quick view (Ctrl+F3), built-in viewer (F3) and editor (F4), Open with / Edit with file associations
- Checksums (MD5, SHA-1, SHA-256, SHA-512) and file properties
- Command palette (Shift+F1), folder selector (Ctrl+F1), theme manager, settings dialog

### Drives (URL schemes)
| Scheme | What it is |
| --- | --- |
| `file://` | Local disk |
| `ssh://`, `smb://` | Remote hosts and shares |
| `iphone://` | iPhone file access (AFC) |
| `docker://` | Container file systems, logs, start/stop/restart |
| `git://` | Browse any revision, branch, tag or commit without checkout |
| `archive://`, `zip://`, `gz://`, `targz://` | Archives as folders (ZIP, TAR, TAR.GZ, TAR.BZ2, BZ2, GZ; 7z, RAR, CAB, XZ and the ISO, IMG, VMDK, WIM, DMG disk images with 7-Zip) |
| `recycle://` | Recycle bin / trash, restore and delete for good |
| `memdrive://` | In-memory drive |
| `search://`, `preview://` | Search results and quick preview |
| `musiccatalog://`, `musicplaylist://` | Music library and queue |

### Archives
- Compress (Alt+F5): ZIP (AES-256/ZipCrypto), 7z, TAR.GZ, TAR.BZ2, TAR; add to existing archives. GZIP, BZIP2, XZ and TAR.XZ are single compressed streams: GZIP and BZIP2 hold one file, XZ and TAR.XZ need 7-Zip, and all four replace an existing file (see `Documentation/architecture/file-operations.md`)
- Extract (Shift+F5) with safe path handling and password support

### Music player
- Own audio pipeline: WAV, MP3 (managed), FLAC/OGG/Opus/M4A/WMA via ffmpeg
- WinMM, PulseAudio and ALSA output with device selection
- Searchable, indexed music library, persistent queue, M3U8 playlists, F3 tag viewer

### Documents
- Document manager (Alt+F3) with full-text search (SQLite FTS5) over text, Markup, Office, OpenDocument, PDF and image metadata, optional OCR through Tesseract

### Docker containers
Ctrl+F9 points the focused panel at the containers on this machine. Press it again to go back to the folder you came from.
- **Container list**: every container, running ones first, with its image and state. Enter opens the container's file system.
- **Inside a container**: browse its folders, copy files out with F5 (and in with F5 from the other panel), create folders with Shift+F7, delete with F8, and open a shell with Ctrl+O. The terminal runs commands inside it, in the folder you are in.
- **Operate a container** from its row on the list: Shift+F9 starts a stopped container or stops a running one, Ctrl+Shift+F9 restarts it.
- **Quick view** (Ctrl+F3) on a container row shows its state and the last 200 lines of its log; Ctrl+R there reloads it.
- Requires the `docker` command-line tool and a running daemon (Docker Desktop, or the `docker` service on Linux). A stopped container's folders cannot be listed, so start it before you browse it.
- **If it does not work**: the footer says what is missing. "Docker is not installed" means the `docker` tool is not on the PATH; "Docker is not running" means the daemon is not reachable; "permission denied" on Linux means the user is not in the `docker` group (`sudo usermod -aG docker $USER`, then log in again).

### Developer tools
- Git client (Ctrl+G): stage, commit, branches, stashes, push/pull, diffs
- .NET solution viewer with build/test/run
- Interactive shell handover (Ctrl+O) and command log terminal
- Package manager (WinGet on Windows, apt on Linux)
- Local AI assistant bar through Ollama

### Utilities
- Calculator, unit converter, weather, process/task/disk/network/system managers, network browser, toast notifications

## Requirements
- .NET 11 SDK to build (version pinned by `global.json`; `Tools/ensure-sdk.sh` or `Tools\ensure-sdk.ps1` offers to install it); released packages are self-contained
- Optional: `git`, `docker`, `ffmpeg`, 7-Zip, Tesseract for the features that use them. The AI bar's Ollama server and its model are downloaded by iTerminal (Tools > Runtime Dependencies) unless one already answers at the configured address

## Build, test and run

```bash
dotnet build iTerminal.slnx
dotnet test iTerminal.slnx
dotnet run --project iTerminal/iTerminal.csproj
```

The end-to-end suite (`iTerminal.Test`) drives the whole app on a virtual terminal and takes several minutes.

## Packaging

```bash
# Windows MSI
dotnet build iTerminal.Installer/iTerminal.Installer.wixproj -c Release -p:Version=1.2.3

# Linux tar.gz, deb and rpm
dotnet build iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj -p:Version=1.2.3
```

## Repository layout
| Project | Purpose |
| --- | --- |
| `iConsole` | Reusable terminal UI framework (controls, commands, jobs, rendering) |
| `iConsole.Inputs.Windows`, `iConsole.Inputs.Posix` | Platform input plug-ins |
| `iTerminal.Core` | File-manager behavior, drives, dialogs, services |
| `iTerminal` | Composition root and executable |
| `iTerminal.Installer`, `iTerminal.Packaging.Linux` | Windows MSI and Linux packages |
| `*.Test`, `iConsole.Test.Core` | Tests and test helpers |

See [CLAUDE.md](CLAUDE.md) for detailed architecture notes.

## Author
Tamás Nagy — <mail@tamas-nagy.net> — [github.com/nagytam](https://github.com/nagytam)

## License
Released under the [MIT License](LICENSE). Copyright (c) 2024-2026 Tamás Nagy.
