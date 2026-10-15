# iTerminal

A two-panel, keyboard-driven file manager and developer toolbox for the terminal, written in C# on .NET 10. It runs on Windows and Linux and is built on `iConsole`, a reusable text-UI framework that lives in the same repository.

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
| `archive://`, `zip://`, `gz://`, `targz://` | Archives as folders (ZIP, TAR, TAR.GZ, TAR.BZ2; 7z, RAR, ISO, CAB with 7-Zip) |
| `recycle://` | Recycle bin / trash, restore and delete for good |
| `memdrive://` | In-memory drive |
| `search://`, `preview://` | Search results and quick preview |
| `musiccatalog://`, `musicplaylist://` | Music library and queue |

### Archives
- Compress (Alt+F5): ZIP (AES-256/ZipCrypto), 7z, TAR.GZ, TAR.BZ2, TAR; add to existing archives
- Extract (Shift+F5) with safe path handling and password support

### Music player
- Own audio pipeline: WAV, MP3 (managed), FLAC/OGG/Opus/M4A/WMA via ffmpeg
- WinMM, PulseAudio and ALSA output with device selection
- Searchable, indexed music library, persistent queue, M3U8 playlists, F3 tag viewer

### Documents
- Document manager (Alt+F3) with full-text search (SQLite FTS5) over text, Markup, Office, OpenDocument, PDF and image metadata, optional OCR through Tesseract

### Developer tools
- Git client (Ctrl+G): stage, commit, branches, stashes, push/pull, diffs
- .NET solution viewer with build/test/run
- Interactive shell handover (Ctrl+O) and command log terminal
- Package manager (WinGet on Windows, apt on Linux)
- Local AI assistant bar through Ollama

### Utilities
- Calculator, unit converter, portfolio tracker with online quotes, weather, process/task/disk/network/system managers, network browser, toast notifications

## Requirements
- .NET 10 SDK to build; released packages are self-contained
- Optional: `git`, `docker`, `ffmpeg`, 7-Zip, Tesseract, Ollama for the features that use them

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
| `iConsole.Demo` | Standalone sample app for `iConsole` |
| `iTerminal.Installer`, `iTerminal.Packaging.Linux` | Windows MSI and Linux packages |
| `*.Test`, `iConsole.Test.Core`, `BenchmarkSuite1` | Tests, test helpers, micro-benchmarks |

See [CLAUDE.md](CLAUDE.md) for detailed architecture notes.

## Author
Tamás Nagy — <mail@tamas-nagy.net> — [github.com/nagytam](https://github.com/nagytam)

## License
Released under the [MIT License](LICENSE). Copyright (c) 2024-2026 Tamás Nagy.
