# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build entire solution
dotnet build iTerminal.slnx

# Run all tests (all test projects)
dotnet test iTerminal.slnx

# Run a specific test project
dotnet test iTerminal.Core.Test/iTerminal.Core.Test.csproj
dotnet test iConsole.Test/iConsole.Test.csproj

# Run the end-to-end suite: the whole app on a virtual terminal (~4-5 minutes, sequential)
dotnet test iTerminal.Test/iTerminal.Test.csproj

# Run a single test by name filter
dotnet test iTerminal.Core.Test/iTerminal.Core.Test.csproj --filter "FullyQualifiedName~FilePanelRefreshTask"

# Run the app
dotnet run --project iTerminal/iTerminal.csproj

# Build the Windows installer (Windows only; publishes the app first, ~5 minutes)
dotnet build iTerminal.Installer/iTerminal.Installer.wixproj -c Release -p:Version=1.2.3

# Build the Linux packages: tar.gz, deb and rpm (on Windows or Linux; ~5 minutes per runtime)
dotnet build iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj -p:Version=1.2.3
dotnet build iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj -p:Version=1.2.3 -p:Runtimes="linux-x64;linux-arm64"
```

Targets `net10.0`, nullable enabled. The `.slnx` is the source of truth for the project set; `iConsole.Inputs.Posix.Test` is marked non-building there (it only compiles on Linux). `Tools/clean.cmd` wipes all `bin`/`obj`/`TestResults` folders.

Runtime state lives outside the repo: the SQLite database (settings, folder cache, music library index, document index, vault) is at `%APPDATA%/iTerminal/iTerminal.sqlite`, created on first run by `DatabaseInitializationHostedService`. The `DataDirectory` configuration key (`iTerminalApplicationBuilderExtension.DataDirectoryKey`) moves that folder — database, vault keys and playlists together; the end-to-end tests point it at a temporary folder so a run never touches the user's own.

`DatabaseInitializationHostedService` compares a hash of the EF model against the stamp in the `DatabaseMetadata` table and recreates the whole database when they differ — the app carries no migrations, so a model change means the cached tables are dropped. **That stamp must stay out of the `Settings` table.** `SettingsService` owns that table: it reads it whole at start-up and writes its cache back whole, so a stamp kept there is read into the cache before the initializer can replace it and written back over the new value afterwards. The check then fails on *every* run, and everything cached in the database — the music library index, the playing queue, the folder cache — is thrown away at every start-up, while the settings themselves survive in the cache and hide it. `SettingsService` treats any `__`-prefixed key as reserved for exactly this reason: it neither caches nor republishes one.

## Architecture

Dependency chain: `iConsole` ← `iTerminal.Core` ← `iTerminal`. The platform input plug-ins `iConsole.Inputs.Posix` and `iConsole.Inputs.Windows` also depend on `iConsole` and are selected at startup. Reusable primitives belong in `iConsole`; app behaviour belongs in `iTerminal.Core`. `Program.cs` is the composition root: register new services and controls in `iConsoleApplicationBuilderExtension` / `iTerminalApplicationBuilderExtension`, not in `Program.cs`.

The detailed write-ups are in `docs/architecture/`. Read the relevant one before changing a subsystem:

| Area | Summary | Detail |
| --- | --- | --- |
| `iConsole` UI framework: controls, `Command`, `Job`, `TaskManager`, services, diagnostics | `Window` is the base for panels; commands are dispatched by the key router; jobs are `IJob` singletons on the `Desktop` lifecycle; one-shot async work goes to `TaskManager` (priorities `Rendering > Input > Background`). | [ui-framework.md](docs/architecture/ui-framework.md) |
| Notifications and toasts | `INotificationService.Notify` from any thread; `NotificationJob` advances the clock; `ToastHost` draws. | [ui-framework.md](docs/architecture/ui-framework.md) |
| Rendering pipeline | `ControlRendererJob` → `BufferLayer`s → `Buffer.Merge()` → `ConsoleBufferRendererJob` diffs and writes. `ITERMINAL_RENDER_DUMPS=1` dumps frames for visual bugs. | [ui-framework.md](docs/architecture/ui-framework.md) |
| Lending the terminal (Ctrl+O) | `ConsoleOwnership` hands the whole terminal to a child shell; Ctrl+C, Ctrl+\ and Ctrl+Z go to the child. | [ui-framework.md](docs/architecture/ui-framework.md) |
| Key handling and hot keys | Global hot keys are almost always function keys; see the key rules below. | [key-patterns.md](docs/architecture/key-patterns.md) |
| Panels, content models, drives and URL schemes | `ContentModelService` owns the two panels; the URL scheme picks the content model. Tool-backed drives (docker, git, search, preview), archives, WebDAV, MTP, FTP, cloud, recycle bin, AI bar and the `iTerminal.Core` services. | [panels-and-drives.md](docs/architecture/panels-and-drives.md) |
| File operations | Marking, command line, file associations, links, checksums and properties, wipe and signing, file clipboard, drive actions, sorting, folder/file comparison, compress, extract, command palette, user menu. | [file-operations.md](docs/architecture/file-operations.md) |
| Media and documents | Audio decoders, sinks and `MusicPlayerService`; the music drives and F3 music viewer; code pages; document index and extractors. | [media-and-documents.md](docs/architecture/media-and-documents.md) |
| Dialogs | Git client (`GitClientDialog`), folder selector (`FolderSelectorDialog`) and the remote connection manager (`RemoteConnectionManagerDialog`, Alt+F2: saved SSH / PowerShell remoting sessions on the lent terminal). | [dialogs.md](docs/architecture/dialogs.md) |
| Runtime dependencies | Playwright, Ollama and git are downloaded or installed on demand, not shipped. | [runtime-dependencies.md](docs/architecture/runtime-dependencies.md) |
| Packaging | Windows MSI (WiX) and Linux tar.gz / deb / rpm. | [build-and-packaging.md](docs/architecture/build-and-packaging.md) |

Runtime state lives in `%APPDATA%/iTerminal` (SQLite database, vault, playlists); `DataDirectory` moves it, and the end-to-end suite always points it at a temp folder.

## Rules that are easy to break

**Threading and the 200 ms budget**
- Key handlers and `Draw` must return within ~200 ms. Never run processes, HTTP or other blocking I/O there or on a render thread; use the async `I*Service` on a background task and write the result back when it lands.
- Guard every background write into a content model with the stale-result check (`IsCurrentUrl()`). Listings land through `ReplaceEntriesAndRestoreFocus`, and bulk entry swaps use `Entries.ReplaceAll`.
- Dispatch commands through `Command.RunAsync`, not `ExecuteAsync`.
- A job must not block its own thread on a call that cannot be cancelled: shutdown gives jobs five seconds.
- Don't mutate the control tree from a worker thread. Post a notification or poll state from `OnIdle` instead.

**Rendering**
- A control paints its whole rectangle every time it draws, because layers are never cleared between frames. Pad lines to `ViewSize.Width` and use `EmptyText` for empty lists.
- Attach and remove children with `AddControl` / `RemoveControl` and set `IsVisible` through the property, so `TreeVersion` moves and the screen repaints from scratch. Don't touch `Controls` directly.
- Overlays attach to the root, last. A modal popup must be removed from the tree, not hidden, or it keeps grabbing the keyboard.
- `ShowAsync` makes every child visible. A control that is hidden on creation must re-hide its children there and track its own "is it up" flag instead of reading `IsVisible`.
- Take `Buffer.SyncLock` around any resize or merge.

**Terminal ownership**
- A new loop that reads from or writes to the real terminal must bracket each pass with `ConsoleOwnership.TryBeginUse` / `EndUse` and skip the pass when refused. Every console driver is an `IConsoleHandoverParticipant`, so it can restore its state when the shell returns.

**Keys and input**
- `new Key(consoleKey, shift, alt, control)`. The `alt` argument comes before `control`.
- No Alt+letter global hot keys: the focused file panel uses Alt+printable for type-to-search. Ctrl+M is ASCII CR, the same as Enter on a TTY, and terminal hosts often reserve it.
- An input control acts only when it has focus and is enabled. Keys the focused control declines are offered to every control.
- `Dialog` claims Esc before its children. A floating sub-state (a suggestion list) implements `ITransientOverlayHost` so Esc closes one layer at a time.
- Ctrl+Alt together means AltGr, so it is typing, not a chord (`ConsoleKeyHelper.IsTypingKey`).
- Test a new hot key in the real app (`DialogsTest`), not against a stand-in control. `ITERMINAL_KEY_TRACE=1` logs every key received.

**Panels and file operations**
- A command that only applies to one drive gates on `GetFocusedModel()` being reference-equal to its own model, not on the model's type.
- Inside a dialog `GetFocusedModel()` is null. Use `GetLastContentModel()`, or `GetFocusedModel() ?? GetLastContentModel()` for a command that can also run from a menu.
- File operations act on `FocusedEntry.ActionableUrl(s)`. They never act on the `..` row, and never on a container row.
- Build child paths with `UrlPath.Child`. String concatenation turns `memdrive://` into `memdrive:`.
- A new URL scheme needs all of: the constant in `Container`, a branch in `Url`, a `ContainerType` case in `ContentModelService.AddContentModel`, a DI registration, `FolderUrlValidationModel.SupportedSchemes`, and (if users should pick it) the Places page. Add to `FileSystemResolver` if it is served by a shared file-system service.
- Destructive actions ask first with a y/n in the footer. Delete dialogs default to *Move to Recycle Bin* only where one exists.

**Persistence and secrets**
- Keys starting with `__` are reserved for internal use. The schema stamp must never be stored in `Settings`, or every start-up recreates the database (there are no migrations; a model change drops the cached tables).
- `ISettingsService` has no change notification. Jobs that react to a setting poll it.
- Credentials come from the vault or the password manager, never from a URL.

**Tests must not touch the real machine**
- The end-to-end suite replaces anything that would reach outside the sandbox with `SandboxServices` (launcher, recycle bin, Trash, drive actions, clipboard, network, docker, MTP, dependencies). A new service that acts on the real machine needs a sandbox replacement before any test can go through the app.

## Testing

Detailed conventions, including the end-to-end suite and `ConsoleAppDriver` automation, are in [docs/architecture/testing.md](docs/architecture/testing.md). The essentials:
- MSTest with Moq. Test folders mirror source folders.
- Assert on what a control drew with `ScreenBuffer`, not `MockBuffer`, and draw twice to catch stale text.
- `iTerminal.Test` is sequential (`[DoNotParallelize]`), takes ~4–5 minutes, and boots the real app on a virtual terminal.
- `Generated/InstantiateAllTypesTest` requires every public type to have a constructor the container can resolve.
- Performance budgets: key handler < 200 ms, content load < 3 000 ms. Assert on `PhaseDurations`, not wall-clock polling.

## Further reference

- [docs/architecture/key-patterns.md](docs/architecture/key-patterns.md): the full list of UI, navigation and state patterns (why a rule exists, with the bug it fixed).
- [docs/mc-parity.md](docs/mc-parity.md): Midnight Commander feature comparison.

## Code style

Follows the root `.editorconfig` and `.github/copilot-instructions.md`:

- 4 spaces, CRLF, braces on new lines, accessibility modifiers on non-interface members.
- Prefer explicit types over `var` unless the type is obvious from the right-hand side.
- File-scoped namespaces; primary constructors for small command/service classes; top-level statements in `Program.cs`.
- Constructor injection over ad-hoc construction; keep reusable primitives in `iConsole` and app behavior in `iTerminal.Core`.
- Logging in `iTerminal.Core` goes through `Serilog.ILogger`.
- Async-first: don't convert an existing async flow to a blocking call.
