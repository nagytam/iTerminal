# Architecture overview

This document holds the architect reviews of iTerminal, one section per scope. A later review of the same scope replaces its section. Finding numbers are unique across the whole document and are never reused; a finding that is fixed moves to the **Resolved** list at the end with its number.

## Whole repository

Reviewed on 2026-11-21, scope "Whole repository" (asked for by name). The review started on branch `fix-application-lifetime` at commit `38ed0b4b`. During the run the working tree was switched, not by this review, to branch `docs/promotion-research-refresh` at `a96d338c`, which adds #803 (one archive path check), #804 (the host disposed, the same change as `38ed0b4b`), #805 (three project references removed) and a research refresh. Every finding was re-checked against `a96d338c` for the files those commits changed, and the document is written on that branch; no review branch was cut, because neither branch is `main`. This pass refreshed the two scopes reviewed before at `70cc25fb` ("Layering and dependencies" and "Dependencies and packages", both below) against the commits since then (#789 to #805: the third-party notices, the moves of the game engines, the audio, video and speech code and the remaining format readers into their own projects, the Watch dialog, the host lifetime). It also added four scopes that no review had covered: "Threading and state", "Drives and content models", "Persistence" and "Composition and DI".

Read: `CLAUDE.md`; `ui-framework.md` and `ui-framework/rendering-pipeline.md`, `terminal-ownership.md`, `testing.md`, `runtime-dependencies.md`, `build-and-packaging.md`, and the parts of `panels-and-drives.md` and `file-operations.md` that the findings cite; the profiling, security and design-review overviews; every project file, `Directory.Build.props` and `iTerminal.slnx`. In the source: `Program.cs`, both builder extensions, `ShowDialogCommand`, `HotKeys`, `TerminalMenuBar`, `FilePanelFactory`, `ViewFileDialogCommand`, `Button`, `Control.GetVisualState`, `Command.RunAsync`, `IFileSystem`, `IContentModel`, `ContentModelRemoteDrive`, `ContentModelAsyncDrive`, the SSH, WebDAV, FTP, SMB and cloud file-system services, `SettingsService`, `DatabaseInitializationHostedService`, `iTerminalDbContext`, `UiService`, `AiChatService` (the dialog tool), `UiText`, `RuntimeDependencyService`, the notice generator and `iTerminal.Test/TerminalApp.cs`. Reference edges, constructor sizes, `static` mutable state, blocking waits and service-locator calls were counted with `grep` and a small parameter-counting script.

Run: `dotnet list iTerminal.slnx package` with `--outdated`, `--vulnerable --include-transitive` and `--deprecated` against nuget.org (read-only; the restore wrote nothing outside `obj/`). Not done: no build, no test run, no run-time check. No SSH, SMB, FTP or WebDAV server was available, so every finding about remote drives rests on reading the code.

**Summary**

- Three High findings, all new. The SSH drive puts unquoted paths into shell commands, so deleting a file with a space in its name removes other files (finding 30). `CanExecute` runs on every draw of a footer button, and the F3 and F4 checks reach the network on the SSH, SMB, FTP and WebDAV drives (finding 31). User data (file labels, the playing queue) shares the cache database that any model change deletes (finding 33).
- The largest structural cost is the composition: four constructors take 133, 80, 67 and 66 parameters and build about 200 dialogs before the first frame, some of them twice (finding 32). This is probably part of the start-up gap in profiling item 4.
- The layer edges still hold. The four new projects (#790, #792, #796, #797) reference only `iConsole` and each other in the stated order, and none uses a UI namespace. The new weak spots are the test projects (finding 38) and the drive schemes that the framework defines for the app (finding 37).
- No package has a known vulnerability. `OpenMcdf` 3.4.0 is the only update and is held back on purpose. No shipped package is under a strong copyleft licence, and since #789 every release carries the notices.
- Strong parts: terminal ownership (every loop that touches the terminal brackets its pass), the sandbox in the end-to-end suite, the schema stamp kept out of `Settings` in both directions, and `ContentModelAsyncDrive`, which already answers existence checks from the listing.

**Findings**

41. **Four documents describe a state the code has left** — **Low**, kind: documentation.
    - **Where:**
      - [CLAUDE.md:40](../../CLAUDE.md) lists the vault among the contents of the database. The password manager now keeps its own store and reads the old `VaultEntries` rows only once, to migrate them ([PasswordManager.cs:391](../../iTerminal.Core/Services/PasswordManager.cs)).
      - [panels-and-drives.md:11](../architecture/panels-and-drives.md) says the no-argument `SettingsService()` creates "a temporary in-memory DB". It creates a file in the temp folder ([SettingsService.cs:154](../../iTerminal.Core/Services/SettingsService.cs), finding 34).
      - [UiText.cs:4](../../iConsole/Localization/UiText.cs) calls itself the localizer for "the few places" that are not handed an `ILocalizer`. It is called 3 587 times in 375 files, so the ambient static is the main path.
      - [iTerminal/iTerminal.csproj:25](../../iTerminal/iTerminal.csproj) says the notice generator is "compiled by RoslynCodeTaskFactory". [ThirdPartyNotices.targets:8](../../iTerminal/Build/ThirdPartyNotices.targets) runs it with `dotnet run`.
    - **Problem:** a developer who trusts these lines looks for the vault in the wrong place, expects test databases to disappear, and assumes injected localizers are the rule. Principle: explicit contracts.
    - **Improvement:** correct the four sentences. Name `PasswordManager`'s own store in `CLAUDE.md`, say "a temporary file" in `panels-and-drives.md` (or follow finding 34), describe `UiText` as the ambient localizer that `ILocalizer` sets, and correct the project comment.
    - **Effort:** S.
    - **Risk of the change:** none (text only).

**What works well**

- The dependency chain that `CLAUDE.md` states is the one the project files build, including the four projects split out since the last pass.
- The research documents and the code agree on the threading rules. Where the code departs from them (findings 31 and 36), a comment nearby already states the rule (`ValidationModel.cs:59`, `IFileHandler.cs:19`, `ContentModelAsyncDrive.cs:236`), so the fixes have a pattern to follow.

**Research cross-reference**

- Profiling overview, item 1 (render and key loops poll every millisecond, 25 % of a core at idle): the largest rendering cost, and it needs an architecture decision before code. The decision: one change signal owned by `Desktop`, set by `Control.StateHasChanged`, by `TreeVersion` moves and by input events, which the three loops wait on. `Control._treeVersion` is a process-wide static ([Control.cs:35](../../iConsole/Controls/Control.cs)), so in a process with two roots (the end-to-end suite) a change in one tree also repaints the other; a per-root signal would remove that as well. Ranked here below findings 30, 31 and 33 only because it costs CPU, not data.
- Profiling overview, item 4 (start-up, 1.1 to 1.4 s unexplained before the database initializer): finding 32 names a likely cause (about 200 dialogs built in four constructors). This stays **unverified** until the `StartupTrace` marks that item asks for are added.
- Profiling overview, item 5 (2.5 s per test for a temporary database): finding 34 adds that those databases are never deleted.
- Security overview: no item covers the SSH command construction of finding 30. It belongs in that overview's next survey of the app's own surface.

**Suggested order** (all scopes in this document)

1. Quick wins, S, no dependencies:
   - finding 30 (quote the SSH paths, High);
   - findings 9 and 25 (project-file lines), 29 (pin `wingetcreate`), 3 (stable log sink);
   - findings 27 and 28 (version reader, update hint), 35 (dialog tool answer);
   - findings 38, 39, 40 and 41 (tidiness), 14 and 15 (SDK pin, packaging text).
2. High:
   - finding 31 (existence from the listing, the `CanExecute` contract). It touches the same SSH methods as finding 30, so do it after 30.
   - finding 33 needs a maintainer decision (a second database file) before code. Do its hash part only after the split: changing the hash recreates the cache once and would drop the labels with it.
3. Medium:
   - finding 18 (validated container test) before finding 32 (dialogs built on first use), so the constructor changes have a safety net;
   - finding 34 together with profiling item 5;
   - finding 5 (central versions), then 6 (audit and lock files);
   - finding 36 after 31; then finding 37 and finding 19;
   - findings 4 and 7 after a maintainer decision.
4. Low, when the code is touched anyway: finding 20.

## Threading and state

Part of the whole-repository pass of 2026-11-21 (see that section for the commits, what was read and what was not run). Read for this scope:

- `Button`, `Control.GetVisualState`, `Command.RunAsync` and `ICommand`;
- the four commands whose `CanExecute` asks a content model whether a file exists;
- `ContentModelRemoteDrive`, `ContentModelAsyncDrive` and `RuntimeDependencyService`;
- `ConsoleOwnership` and its participants;
- the `static` fields of `iConsole` and `iTerminal.Core`;
- every `.Wait()` and `GetAwaiter().GetResult()` outside the tests (72 matches).

**Summary**

- `CanExecute` is on the render path: a `Button` asks it on every draw. The contract is written in two comments but not on `ICommand`, and the F3 and F4 commands break it on four drive types (finding 31).
- The terminal-ownership rule holds everywhere: the four loops that read or write the terminal bracket each pass, and only the console drivers write to it.
- The worker-to-UI hand-off is still written by hand in 12 dialogs (finding 19, in "Layering and dependencies").

**Findings**

31. **`CanExecute` runs on every draw, but the contract is not on `ICommand`, and the F3 and F4 checks reach the network** — **High**, kind: concurrency, interface.
    - **Where:**
      - The draw path: [Button.cs:151](../../iConsole/Controls/Buttons/Button.cs) (`Draw` calls `GetVisualState`) → [Control.cs:576](../../iConsole/Controls/Control.cs) (`IsEnabled`) → [Button.cs:108](../../iConsole/Controls/Buttons/Button.cs) (`_command.CanExecute()`).
      - The key path: [Command.cs:62](../../iConsole/Commands/Command.cs).
      - The commands: [ViewFileDialogCommand.cs:91](../../iTerminal.Core/Commands/ViewFileDialogCommand.cs) and [EditTextFileDialogCommand.cs:38](../../iTerminal.Core/Commands/EditTextFileDialogCommand.cs) call `model.ExistsFile(url)`, which [ContentModelRemoteDrive.cs:144](../../iTerminal.Core/Model/ContentModels/ContentModelRemoteDrive.cs) passes to the service.
      - What the service does per call: a remote `test -f` process at [SshFileSystemService.cs:299](../../iTerminal.Core/Services/SshFileSystemService.cs), a PROPFIND under a lock at [WebDavFileSystemService.cs:313](../../iTerminal.Core/Services/WebDavFileSystemService.cs), and a request at [FtpFileSystemService.cs:239](../../iTerminal.Core/Services/FtpFileSystemService.cs) and [SmbFileSystemService.cs:198](../../iTerminal.Core/Services/SmbFileSystemService.cs).
      - The rule is stated only in comments: [ValidationModel.cs:59](../../iConsole/Model/Validations/ValidationModel.cs) ("a `CanExecute` that a button reads on every draw") and [IFileHandler.cs:19](../../iConsole/Viewers/IFileHandler.cs). [ICommand.cs:18](../../iConsole/Commands/ICommand.cs) says nothing.
      - A second case, on the key path: [RuntimeDependencyDialogCommand.cs:15](../../iTerminal.Core/Commands/RuntimeDependencyDialogCommand.cs) → [RuntimeDependencyDialog.cs:54](../../iTerminal.Core/Dialogs/RuntimeDependencyDialog.cs) → [RuntimeDependencyService.cs:62](../../iTerminal.Core/Services/RuntimeDependencies/RuntimeDependencyService.cs) waits for the first probe build. [runtime-dependencies.md:15](../architecture/runtime-dependencies.md) says that build takes seconds and that reads "must not come from a key handler".
    - **Problem:** with an SSH, SMB, FTP or WebDAV panel focused, the F3 and F4 footer buttons make a network round trip on the render thread each time the footer is drawn. A slow or dead server stops the whole screen, and on SSH each draw starts a process on the server. This breaks the 200 ms rule by design, not by a slip. The next command that checks a file in `CanExecute` will repeat it, because nothing in the type says where `CanExecute` runs. Principle: explicit contracts for background work, dependency on the real network without a seam. **Unverified** at run time (no server was available); the call chain is read from the code.
    - **Improvement:** two steps, one branch.
      1. Answer the two existence checks of `ContentModelRemoteDrive` from the current listing, as `ContentModelAsyncDrive` already does ([ContentModelAsyncDrive.cs:236](../../iTerminal.Core/Model/ContentModels/ContentModelAsyncDrive.cs)). Move its `FindListed` to `ContentModelBase` as `protected Url? FindListed(Url url)` and use it in both classes. The services keep their network `ExistsFile` for the copy and move walkers, which run on background tasks.
      2. State the contract on the interface (no signature change):

         ```csharp
         /// <summary>
         /// Whether the command can run now. Called on the render thread each time a button bound to it is drawn,
         /// and on the input thread before every run: answer from memory, with no I/O, no process and no wait.
         /// </summary>
         bool CanExecute();
         ```

         Then make `RuntimeDependencyDialog.HasWork` answer `false` until the first build has landed, through a new `bool IsReady` on `IRuntimeDependencyService` that is read without waiting.
    - **Effort:** S for step 1, S for step 2.
    - **Risk of the change:** a file created on the server since the last listing cannot be opened with F3 until the panel refreshes. The docker, git and MTP drives already behave this way. `ContentModelSshDriveTest`, the SMB, FTP and WebDAV model tests, and `RuntimeDependencyDialogTest` would show a changed answer. Tools > Runtime Dependencies is greyed out for the first seconds after start.

**What works well**

- Terminal ownership is applied everywhere it is needed. `ConsoleBufferRendererJob`, `KeyboardEventPublisherJob`, `PosixTerminalInputPublisherJob` and `MouseEventPublisherJob` bracket each pass with `TryBeginUse`. The only direct console writes outside the drivers are in `InteractiveShellService` (writing to the lent terminal) and the key reader.
- `ContentModelAsyncDrive` answers the F3, F5 and F8 checks from the listing and says why in its comment; it is the pattern finding 31 extends.
- `FileHandlerRegistry` publishes one immutable snapshot of its handlers, so a plug-in loaded at run time never shows a half-changed list to the render thread.
- The two ambient statics that tests would trip over have per-flow scopes (`UiText.Scope`, `TextClipboard.Scope`). `DialogRefreshHostedService` takes its static service provider back when the host stops, so the container is not kept alive after the app ends.
- Shutdown takes 0.11 to 0.16 s in the profiling measurements, far inside the five seconds jobs get.

**Research cross-reference**

- Profiling overview, item 1: see "Whole repository". It is the rendering-pipeline change that needs a design decision first.
- Design review, finding 68 (hand-painted tables), and finding 19 here: the shared hand-off primitive is still missing.

**Suggested order**

1. Finding 31, step 1 then step 2, after finding 30 (same SSH file).

## Drives and content models

Part of the whole-repository pass of 2026-11-21. Read for this scope:

- `IFileSystem`, `IContentModel` and `IRemoteFileSystemService`;
- the SSH, SMB, FTP, WebDAV, S3, OneDrive, Google Drive and MTP services, and `HttpFileSystemServiceBase`;
- the content model classes and `ContentModelService.AddContentModel`;
- `Container`, `ContainerType`, `Url` (scheme references only) and `FolderUrlValidationModel`.

**Summary**

- The SSH drive writes paths into shell commands without quoting (finding 30). It is a data-loss bug with a small fix.
- `IFileSystem` is half synchronous, so the network drives block on their own async code with no way to cancel (finding 36).
- Every drive scheme of the app is defined in `iConsole`, so a new drive changes the framework's public enum (finding 37).

**Findings**

30. **The SSH drive puts unquoted paths into shell commands** — **High**, kind: interface, consistency (data loss).
    - **Where:** [SshFileSystemService.cs:127](../../iTerminal.Core/Services/SshFileSystemService.cs) (`cp -r {source} {target}`), `:140` (`rm {path}`), `:151` (`rm {path} -R`), `:203` (`ls -la ` + path), `:268` (`mv`), `:299` and `:309` (`test -f`, `test -d`), `:316` (`mkdir`), `:365` (`cd {folder};` before a user command). The SCP client built in the same file quotes its paths ([SshFileSystemService.cs:113](../../iTerminal.Core/Services/SshFileSystemService.cs), `RemotePathTransformation.ShellQuote`). The file has not changed since #45.
    - **Problem:** the remote shell splits every path at spaces and runs anything after `;`, `|` or `$(`.
      - Deleting `/home/u/my file` runs `rm /home/u/my file`. That removes `/home/u/my` and `file` in the login folder if they exist, and reports success.
      - A folder named `a;b` breaks the listing.
      - A file whose name contains a command runs that command on the server, as the user. Because `ExistsFile` runs on every footer draw (finding 31), this needs no key press: the cursor resting on the row is enough.

      Principle: one solution per problem (the quoting exists in the same class), explicit contracts. **Unverified** at run time; no research item covers it.
    - **Improvement:** one helper in the service, used for every operand, with `--` before the operands so a name starting with `-` is not read as an option:

      ```csharp
      // POSIX single-quote form: ' becomes '\''
      internal static string Quote(string path) => "'" + path.Replace("'", "'\\''") + "'";

      // before
      _client.RunCommand($"rm {url.Path}");
      // after
      _client.RunCommand($"rm -- {Quote(url.Path)}");
      ```

      Use `rmdir` for a non-recursive folder delete (today `rm` without `-r` fails on a folder). To test the strings without a server, build them in one `internal static class SshCommands` (`Remove`, `RemoveTree`, `Copy`, `Move`, `List`, `TestFile`, `TestFolder`, `MakeFolder`) and assert on its output in `SshFileSystemServiceTest`.
    - **Effort:** S.
    - **Risk of the change:** a caller that relied on the shell expanding a pattern in the path would stop working; no such caller was found. The new `SshCommands` tests and `ContentModelSshDriveTest` cover the change. A manual check against one SSH server is still needed.

36. **`IFileSystem` is synchronous where the drives are remote, so six services block on their own async code without cancellation** — **Medium**, kind: interface, concurrency.
    - **Where:**
      - The interface: [IFileSystem.cs:9](../../iConsole/Interfaces/IFileSystem.cs) to `:15`. `DeleteFile`, `DeleteFolder`, `GetAvailableDiskSpace`, `ExistsFile` and `ExistsFolder` are synchronous, `WriteFileAsync` takes a concrete `MemoryStream`, and only the two stream members at `:22` and `:28` take a `CancellationToken`. [IContentModel.cs:7](../../iConsole/Interfaces/IContentModel.cs) inherits all of it.
      - Blocking calls, all with `CancellationToken.None`: [HttpFileSystemServiceBase.cs:167](../../iTerminal.Core/Services/Cloud/HttpFileSystemServiceBase.cs), [OneDriveFileSystemService.cs:193](../../iTerminal.Core/Services/Cloud/OneDriveFileSystemService.cs) and `:333`, [GoogleDriveFileSystemService.cs:261](../../iTerminal.Core/Services/Cloud/GoogleDriveFileSystemService.cs) and `:444`, [S3FileSystemService.cs:572](../../iTerminal.Core/Services/Cloud/S3FileSystemService.cs) and `:585`, [WebDavFileSystemService.cs:122](../../iTerminal.Core/Services/WebDavFileSystemService.cs) and `:168`, [MtpDriveService.cs:129](../../iTerminal.Core/Services/Mtp/MtpDriveService.cs) and `:209`.
      - Stubs: in `iTerminal.Core/Model/ContentModels` (35 files), 10 one-line overrides answer `false` or nothing for file operations the model does not have, and 15 lines throw `NotSupportedException` or `NotImplementedException`.
      - `ExistsFile` and `ExistsFolder` have 84 call sites in `iTerminal.Core`.
    - **Problem:** a delete on a cloud drive cannot be cancelled and holds a pool thread for the whole request. Each new drive decides again whether to block. The music, preview and search models must implement file operations they cannot do, and a consumer that needs only existence and reading gets the full write surface. Principle: interface segregation, explicit contracts.
    - **Improvement:** add async members with a token to `IFileSystem`, with default bodies that call the synchronous ones so no implementer breaks. Implement them natively in the six network services, and move the copy, move and delete walkers to them:

      ```csharp
      Task<bool> DeleteFileAsync(Url url, bool moveToRecycleBin, CancellationToken cancellationToken)
          => Task.FromResult(DeleteFile(url, moveToRecycleBin));
      Task<bool> DeleteFolderAsync(Url url, bool recursive, bool moveToRecycleBin, CancellationToken cancellationToken)
          => Task.FromResult(DeleteFolder(url, recursive, moveToRecycleBin));
      Task<bool> ExistsAsync(Url url, PathType kind, CancellationToken cancellationToken)
          => Task.FromResult(kind == PathType.Folder ? ExistsFolder(url) : ExistsFile(url));
      ```

      Mark the synchronous `Delete*` members `[Obsolete]` once no caller is left. `IFileSystem` is in `iConsole`, which is meant for other projects. The default bodies keep any implementer outside the solution compiling (none is known in this repository); removing the old members later is a breaking change for them.
    - **Effort:** L in total; M for the first step (the three async members and the six services).
    - **Risk of the change:** a walker that now awaits may interleave differently with the progress dialog. The `TransferTask` tests, the cloud service tests (with their fake `HttpMessageHandler`) and the end-to-end `FileOperationsTest` and `FileTransferTest` cover the paths.

37. **The URL schemes of the app's drives are defined in the framework** — **Medium**, kind: layering, evolution cost.
    - **Where:**
      - In `iConsole`: [Container.cs:5](../../iConsole/Data/Container.cs) to `:73` (24 scheme constants, among them `musicplaylist://`, `musiccatalog://`, `evidence://`, `recycle://` and `gdrive://`), [ContainerType.cs:3](../../iConsole/Data/ContainerType.cs) (25 values, `EvidenceNotebook` and `MusicCatalog` among them), and 70 `Container.` references in `iConsole/Data/Url.cs` (1 129 lines).
      - In `iTerminal.Core`: [ContentModelService.cs:127](../../iTerminal.Core/Services/ContentModelService.cs) (`AddContentModel`, 21 `case ContainerType.` branches; the constructor takes 34 parameters, 20 of them optional) and [FolderUrlValidationModel.cs:24](../../iTerminal.Core/Model/Validations/FolderUrlValidationModel.cs) (a hand-kept copy of the list, whose comment says so).
      - The checklist in `CLAUDE.md` ("A new URL scheme needs all of"): six places in two projects.
    - **Problem:** a new drive in `iTerminal.Core` changes `iConsole`'s public `Container` and `ContainerType` and its `Url` parser. That is app behaviour in the framework, against the split `CLAUDE.md` states, and any project outside the solution that switches on `ContainerType` breaks at each new drive. Each drive costs edits in six files and a reviewer who knows the checklist. Principle: dependency direction, evolution cost.
    - **Improvement:** a drive registry in `iTerminal.Core`, first used by the two app-side lists:

      ```csharp
      public sealed record DriveScheme(string Prefix, ContainerType Type, bool OfferedInPathBox);
      // one per drive, beside its other registrations
      services.AddSingleton(new DriveScheme(Container.MTP, ContainerType.Mtp, OfferedInPathBox: true));
      ```

      `FolderUrlValidationModel.SupportedSchemes` reads `IEnumerable<DriveScheme>`, and a test checks that each `ContainerType` that `AddContentModel` handles has exactly one `DriveScheme`. A later step moves the app-only schemes out of `Container`, lets `Url` parse any `scheme://` generically, and replaces `ContainerType` for those drives with the registry key. That step breaks `iConsole`'s public API and needs a decision.
    - **Effort:** M for the registry and the two lists; L for moving the schemes out of `iConsole`.
    - **Risk of the change:** `Url` treats some schemes specially (archives nested in another drive). The `Url` tests in `iConsole.Test` and `FolderUrlValidationModelTest` cover the parser and the path box.

**What works well**

- `ContentModelAsyncDrive` (docker, git, MTP and the other tool-backed drives) keeps every tool call off the key path and answers checks from the listing.
- The WebDAV service escapes each path segment when it builds a request, unescapes the server's hrefs, and has `internal` hooks for its tests ([panels-and-drives.md:57](../architecture/panels-and-drives.md)).
- Credentials come from saved profiles, never from the URL, on every remote drive.
- `FolderUrlValidationModel`'s comment names the two other lists it must match, so the cost of finding 37 is at least visible.

**Research cross-reference**

- Security overview: no item covers finding 30. The overview's surveys look at features to add, not at the app's own command construction.

**Suggested order**

1. Finding 30 (S, High).
2. Finding 36, first step, after finding 31.
3. Finding 37, registry step.

## Persistence

Part of the whole-repository pass of 2026-11-21. Read for this scope:

- `SettingsService`, `ISettingsService`, `DatabaseInitializationHostedService` and `iTerminalDbContext`;
- `FileLabelService`, `MusicPlaylistStore`, and `PasswordManager` (the migration from the old vault);
- the models that save through the settings service: to-dos, contacts, meetings, mail accounts, radio stations, the browser store and plug-ins;
- `SettingsServiceTest` and the profiling measurements of the database.

**Summary**

- The schema-stamp rule holds. The stamp has its own table, `SettingsService` neither reads nor writes `__` keys, a test covers both directions, and the profiling run saw the cached tables survive a second start.
- User data is stored in three ways with three different lifetimes, and two of its tables are deleted with the caches on every model change (finding 33).
- The test constructor of `SettingsService` leaves a database file behind on every call (finding 34).

**Findings**

33. **User data shares the cache database and is deleted with it on any model change** — **High**, kind: persistence (data loss).
    - **Where:**
      - The recreate: [DatabaseInitializationHostedService.cs:90](../../iTerminal.Core/Application/DatabaseInitializationHostedService.cs) to `:101`. A different model hash deletes and recreates the whole file.
      - The user tables in that file: [iTerminalDbContext.cs:20](../../iTerminal.Core/Data/iTerminalDbContext.cs) (`FileLabels`, the colour labels and tags a user sets, [FileLabelService.cs:29](../../iTerminal.Core/Services/Labels/FileLabelService.cs)) and `:23` (`MusicPlaylistEntries`, the playing queue, [MusicPlaylistStore.cs:72](../../iTerminal.Core/Services/Music/MusicPlaylistStore.cs)).
      - The other user data lives as JSON in the `Settings` table, and it survives because `SettingsService` holds it in memory and writes it back: [ToDoTask.cs:285](../../iTerminal.Core/Model/ToDo/ToDoTask.cs), [Contact.cs:159](../../iTerminal.Core/Model/Contacts/Contact.cs), [Meeting.cs:261](../../iTerminal.Core/Model/Calendar/Meeting.cs), [MailModels.cs:118](../../iTerminal.Core/Model/Mail/MailModels.cs), [RadioStationStore.cs:177](../../iTerminal.Core/Model/Radio/RadioStationStore.cs) and [WebBrowserStore.cs:160](../../iTerminal.Core/Services/Web/WebBrowserStore.cs). Each of those saves calls `Write()`, which reads and rewrites the whole table ([SettingsService.cs:107](../../iTerminal.Core/Services/SettingsService.cs)).
      - The hash covers entity and property names and CLR types only ([DatabaseInitializationHostedService.cs:223](../../iTerminal.Core/Application/DatabaseInitializationHostedService.cs)). The 46 key, index and length settings in `OnModelCreating` are not in it.
      - `CLAUDE.md` says "a model change means the cached tables are dropped", but labels and the queue are not caches.
    - **Problem:** every release that adds or changes any entity (the music index, the document index, a new cache) deletes every user's file labels and playing queue without a word, while to-dos and contacts survive. Which data survives an upgrade depends on where its author happened to store it. An added index never reaches an existing database, because it changes nothing the hash reads, so old and new installs run different schemas. If a model change lands before a user's first unlock after the vault migration, the old vault rows that `PasswordManager` still has to read are deleted too (**unverified**; it depends on the release order). Principle: ownership and lifetime of data, explicit contracts.
    - **Improvement:**
      1. Decide which entities are caches.
      2. Move the user-data entities (`FileLabelEntry`, `MusicPlaylistEntry`) into a second context on a second file, `iTerminal.user.sqlite`. Create it with EF migrations (or a version row and hand-written upgrades) and never recreate it. Keep the hash-and-recreate for the cache file.
      3. Copy the two tables across once, on the first start after the change and before the cache check runs.
      4. Only after that, add the keys and indexes to `ComputeModelHash` (`GetKeys()`, `GetIndexes()`), which recreates the cache once.
      5. Write the rule in `CLAUDE.md`: a cache table goes in `iTerminalDbContext`, user data in the user context.
    - **Effort:** L (M for the hash part once the split exists).
    - **Risk of the change:** the one-off copy is the step that can lose data if it runs after the recreate. `DatabaseInitializationHostedServiceTest` and `DatabaseInitializationRecoveryTest` need a case "labels survive a model change". The `DataDirectory` key must move both files together; the end-to-end tests point it at a temporary folder and would show a missing file.

34. **The test constructor of `SettingsService` leaves a database file in the temp folder on every call** — **Medium**, kind: testability, lifetime.
    - **Where:** [SettingsService.cs:31](../../iTerminal.Core/Services/SettingsService.cs) (`public SettingsService()`) and `:154`, which writes a new `iTerminal.Settings.<guid>.sqlite` to `Path.GetTempPath()` and never deletes it; the class is not `IDisposable`. [testing.md:6](../architecture/testing.md) recommends the constructor, and there are 123 call sites in 46 test files (profiling overview, item 5). On the machine of this review, `/tmp` holds 29 158 of these files, 3.2 GB.
    - **Problem:** a production class carries a test-only constructor whose file has no owner. Each test run adds files to every developer's and runner's temp folder until the disk fills, and the same construction is the 2.5 s set-up cost of profiling item 5. Principle: ownership and lifetime, testability seams kept in test code.
    - **Improvement:** move the temporary factory to `iTerminal.Core.Test/Helper` as `TestSettings.Create()`. Back it with an in-memory SQLite connection kept open for the life of the service, or with a copy of the template database that profiling item 5 proposes, deleted in a `[TestCleanup]`. Replace the 123 calls mechanically, and remove the parameterless constructor from `SettingsService`.
    - **Effort:** M (mechanical).
    - **Risk of the change:** `InstantiateAllTypesTest` may use the parameterless constructor to build `SettingsService` for other types. It then needs the container path or a registered factory, and the test shows it. An in-memory database behaves like the file for the settings tests.

**What works well**

- The schema stamp is in its own `DatabaseMetadata` table. `SettingsService` skips `__` keys on read and on write and drops stale reserved rows ([SettingsService.cs:119](../../iTerminal.Core/Services/SettingsService.cs)), and `SettingsServiceTest` asserts both directions.
- The initializer clears the SQLite connection pools before it deletes the file. The clean-up of old cache rows waits until 10 s after start instead of compiling queries on the start-up path.
- `MusicPlaylistStore` states why it rewrites the whole queue (consistent positions). `FileLabelService` keeps an in-memory copy, so the render thread never queries the database.

**Research cross-reference**

- Profiling overview, "What is already fast" (cached tables survive a second start): confirmed for an unchanged model. Finding 33 is about a changed one, which the profiling run did not exercise.
- Profiling overview, item 5: finding 34 is the lifetime side of the same constructor.

**Suggested order**

1. Finding 34 (with profiling item 5).
2. Finding 33: the decision and the split first, the hash change last.

## Composition and DI

Part of the whole-repository pass of 2026-11-21. Read for this scope:

- `Program.cs`, `iConsoleApplicationBuilderExtension` (52 registrations), `iTerminalApplicationBuilderExtension` (758 lines, 353 registrations) and `StartupWarmUp`;
- `ShowDialogCommand` and `DialogRefresh`;
- `HotKeys`, `TerminalMenuBar`, `ToolsMenuItem`, `FileMenuItem`, `FilePanelFactory`, `ContentBrowser` and `ViewFileDialogCommand`;
- `FileHandlerRegistry`, `UiService`, `AiChatService` (the dialog tool) and `CommandCatalog`;
- the registration extension of `iConsole.Formats`, and the sandbox registrations in `iTerminal.Test/TerminalApp.cs`;
- the 43 service-locator calls outside composition code, in 9 files.

**Summary**

- About 200 dialogs reach the menus, the footer and the panel factory through constructor parameters, so they are all built before the first frame, some of them twice (finding 32).
- The AI's dialog tool keeps a third list of dialogs and tells the model a dialog opened when it did not (finding 35).
- The three media projects have no registration extension of their own (finding 39).
- The sandbox replaces every service that reaches outside the machine, including the ones added since the last pass (printing, scanning, Defender, virtual machines, Bluetooth, the microphone and speech recognizer).

**Findings**

32. **Menus, the footer and the panels take about 200 dialogs as constructor arguments, so every dialog is built at start-up** — **Medium**, kind: coupling, lifetime, performance.
    - **Where:**
      - Constructor sizes, as parameters / optional / dialog-typed: [TerminalMenuBar.cs:47](../../iTerminal.Core/Controls/MenuBar/TerminalMenuBar.cs) 133 / 95 / 101, [HotKeys.cs:30](../../iTerminal.Core/Controls/StatusBar/HotKeys.cs) 80 / 44 / 58, [ContentBrowser.cs:97](../../iTerminal.Core/Controls/Contents/ContentBrowser.cs) 67 / 62 / 47, [FilePanelFactory.cs:14](../../iTerminal.Core/Factories/FilePanelFactory.cs) 66 / 59 / 43. Also [ToolsMenuItem.cs:10](../../iTerminal.Core/Controls/MenuBar/ToolsMenuItem.cs) (61 parameters) and [FileMenuItem.cs:15](../../iTerminal.Core/Controls/MenuBar/FileMenuItem.cs) (47).
      - `AddiTerminal` registers 117 dialogs as transient. So `GitClientDialog` ([iTerminalApplicationBuilderExtension.cs:170](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs)), `SettingsDialog` (`:340`) and `TranslatorDialog` (`:415`) are each built once for `HotKeys` and once for `TerminalMenuBar`, and the hot key and the menu open two different instances.
      - `ShowDialogCommand` holds the dialog it was given ([ShowDialogCommand.cs:58](../../iTerminal.Core/Commands/ShowDialogCommand.cs)). The static `DialogRefresh.Services` (`:13`) exists only to rebuild it after a language change.
      - Two other mechanisms exist beside it: `UiService` resolves on demand ([UiService.cs:29](../../iTerminal.Core/AI/UiService.cs)), and `FilePanelFactory` takes two dialogs as `Func<>` (`:42` and `:58`).
      - `FilePanelFactory` is on the warm-up list ([StartupWarmUp.cs:21](../../iTerminal.Core/Application/StartupWarmUp.cs)). Profiling overview, item 4.
    - **Problem:** the start-up path builds every dialog whether or not it is ever opened, and builds some twice. A dialog that keeps state (a search text, a selected tab) shows different state from its hot key and from its menu entry. A new dialog touches up to four constructors. The `= null` defaults hide a missing registration: the container passes `null`, and the hot key or menu entry quietly disappears. A validated container (finding 18) would not catch that either, because an optional parameter with no registration is valid. Principle: ownership and lifetime, evolution cost, performance structure. The start-up cost is **unverified**: profiling item 4 found 1.1 to 1.4 s without a cause, and this is a candidate.
    - **Improvement:** one open-generic factory, which `ShowDialogCommand` takes instead of the dialog:

      ```csharp
      public interface IDialogFactory<out TDialog> where TDialog : Dialog { TDialog Create(); }
      internal sealed class DialogFactory<TDialog>(IServiceProvider services) : IDialogFactory<TDialog> where TDialog : Dialog
      {
          public TDialog Create() => services.GetRequiredService<TDialog>();
      }
      // AddiTerminal
      services.AddSingleton(typeof(IDialogFactory<>), typeof(DialogFactory<>));

      // before
      public abstract class ShowDialogCommand<TDialog>(TDialog dialog) : Command where TDialog : Dialog
      // after: the dialog is built on first open and kept, so its state lasts as it does today
      public abstract class ShowDialogCommand<TDialog>(IDialogFactory<TDialog> dialogs) : Command where TDialog : Dialog
      ```

      - Rebuilding after a language change becomes `dialogs.Create()` in the command, and `DialogRefresh` with its hosted service can go.
      - Convert one consumer per pull request, starting with `HotKeys`, and measure each with `StartupTrace`.
      - Let the menu and the footer share one command per dialog, so both open the same instance.
      - The 95 `ShowDialogCommand` subclasses change their constructor parameter type. All are in `iTerminal.Core`, so nothing outside the solution breaks.
    - **Effort:** L in total; M for the factory and `HotKeys`.
    - **Risk of the change:** the first open of each dialog pays its construction on the key path. A dialog whose constructor does I/O would then break the 200 ms rule and must be found: the end-to-end `DialogsTest` opens every dialog by its key, and `DialogKeyHintsTest` checks the footer. `InstantiateAllTypesTest` must accept the open generic.

35. **The AI's `open_dialog` tool keeps its own list of dialogs and reports success for a name it does not know** — **Medium**, kind: consistency, interface.
    - **Where:**
      - [AiChatService.cs:335](../../iTerminal.Core/AI/AiChatService.cs) to `:341`: the tool description names 13 dialogs, and the reply is "Opened {name}." whatever happened.
      - [UiService.cs:29](../../iTerminal.Core/AI/UiService.cs) to `:63`: a `switch` over 28 dialogs and their aliases, resolved through the service provider.
      - [IUiService.cs:11](../../iTerminal.Core/AI/IUiService.cs): `Task OpenDialogAsync(string name)`, with no result.
      - There are 95 `ShowDialogCommand` subclasses, and [CommandCatalog.cs:22](../../iTerminal.Core/Services/CommandPalette/CommandCatalog.cs) already gathers every command from the control tree for the palette. The Watch dialog of #801 and the print, mail and weather dialogs are among those the tool cannot open.
    - **Problem:** three lists of the same thing drift apart: the description offers 13 names, the switch knows 28, and the app has about 100 dialogs. An unknown name opens nothing, the tool tells the model it opened, and the model tells the user. Principle: one place per decision, explicit contracts.
    - **Improvement:** first return the result:

      ```csharp
      // before
      Task OpenDialogAsync(string name);
      // after
      Task<bool> OpenDialogAsync(string name);
      ```

      and let the tool answer "No dialog is called {name}. Available: …" when it is false (1 caller, 1 implementation plus 4 test doubles in `iTerminal.Core.Test`). Then build the names from `CommandCatalog`: every entry whose command is a `ShowDialogCommand<>`, by its English title, and run that command. A new dialog command is then reachable by the AI with no change to `UiService`.
    - **Effort:** S for the result, M for the catalog.
    - **Risk of the change:** the AI tool tests assert the reply text and need the new wording. The catalog gathers commands from the controls on screen, so a dialog whose command is not on any screen is not offered (today's switch can open it).

39. **The three media projects have no registration extension; `AddiTerminal` registers their types one by one** — **Low**, kind: consistency.
    - **Where:** [FileFormatsServiceCollectionExtension.cs:39](../../iConsole.Formats/Application/FileFormatsServiceCollectionExtension.cs) (`AddFileFormats`, called from `AddiTerminal` at [iTerminalApplicationBuilderExtension.cs:55](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs)). Against that: the audio sinks at `:250` to `:252` (their order is the fallback order), the video decoder factories at `:273` and `:274`, and the speech recognizer and microphone at `:707` and `:708`. All come from projects split out in #792, #796 and #797.
    - **Problem:** the projects are meant to be reusable beside `iConsole`, but another host must copy decisions that belong to them, such as the sink order. Principle: consistency with the framework (the format project solved the same problem one way, the media projects another).
    - **Improvement:** `AddAudioOutput()` in `iConsole.Media.Audio`, `AddVideoDecoders()` in `iConsole.Media.Video` and `AddSpeech(Func<string, string?> findOnPath)` in `iConsole.Media.Speech`, called from `AddiTerminal` in place of the lines.
    - **Effort:** S.
    - **Risk of the change:** a changed registration order changes which sink is tried first. `iTerminalApplicationBuilderExtensionTest` and `MusicPanelsRegistrationTest` check the registrations.

**What works well**

- One registration point: `Program.cs` calls `AddiConsoleCore` and `AddiTerminal`, and the end-to-end host calls the same `AddiTerminal` and then replaces what reaches outside the machine ([TerminalApp.cs:200](../../iTerminal.Test/TerminalApp.cs)). The app and its test host cannot drift apart.
- The service-locator calls outside composition code are few (43 in 9 files), and each states why: breaking a construction cycle (`UiService`), staying out of the desktop's construction graph (`ErrorPromptService`, `PromptService`, `UpdateCheckJob`), or the language refresh.
- `StartupWarmUp` builds the slow independent singletons on their own threads while the host builds the rest.
- `DialogRefreshHostedService` releases the static it fills, which fixed a 40 MB growth per start in the end-to-end suite.

**Research cross-reference**

- Profiling overview, item 4: finding 32 is a candidate cause. Add the `StartupTrace` marks first, then measure the construction of `HotKeys` and `TerminalMenuBar`.
- AI overview: no item covers the dialog tool's list; finding 35 is the first.

**Suggested order**

1. Finding 35, first step, and finding 39 (S).
2. Finding 18 (validated container test, in "Layering and dependencies"), then finding 32, one consumer per pull request.

## Layering and dependencies

Reviewed on 2026-11-21 as a refresh within the whole-repository pass, replacing the pass at `70cc25fb`. Scope "Layering and dependencies"; read at `38ed0b4b` on `fix-application-lifetime` and re-checked at `a96d338c` on `docs/promotion-research-refresh` (see "Whole repository"). The refresh re-checked every open finding against the code and read the changes since `70cc25fb`: #790 (`iTerminal.Core.Games`), #792 (`iConsole.Media.Audio`), #796 (`iConsole.Media.Video`), #797 (`iConsole.Media.Speech`), #802 (the remaining format readers into `iConsole.Formats`), #804 (the host disposed, `--update` cancellable) and #805 (three references removed). Read: every `*.csproj`; the `using` lines of each new project; the two `Instantiate*TypesTest` classes; the converter fakes in `iConsole.Test`; `iConsole.Test.Core`; `GamesDialogTest`; `UpdateService`, `AboutDialog`, `UpdateCommandLine`, `UpdateCheckJob` and `UpdateDialog`. The `ProjectReference`, `namespace` and `using` edges were checked with `grep`. Not done: no build, no test run, no run-time check.

**Summary**

- The dependency direction holds.
  - No file in `iConsole`, `iConsole.Formats`, the three media projects, `iTerminal.Core.Games` or the input plug-ins uses an `iTerminal` namespace.
  - `iConsole` uses no `iConsole.Formats` or `iConsole.Media` namespace, and `iConsole.Formats` uses no media namespace.
  - The media projects and the game engines use only `iConsole.Display`, `iConsole.Localization` and, for video, the audio project's decoders.

  The edges are the ones `CLAUDE.md` states, and #805 removed the unused `iTerminal.Core` → `iConsole.Inputs.Windows` edge (finding 10, resolved).
- The new test projects add two edges that the source layers do not have (finding 38). `iConsole.Test` now has 21 files that use `iConsole.Formats.*` (finding 20; 15 at the last pass).
- Still open, unchanged in substance: no test builds the real container (finding 18), five dialogs hand-write the worker-to-UI hand-off (finding 19), the version is read in two places (finding 27), and the WinGet hint is decided in three places (finding 28).
- Finding 23 is resolved by #804 (see Resolved).

**Findings**

18. **No test builds the real container, and `InstantiateAllTypesTest` does not check what `CLAUDE.md` says it checks** — **Medium**, kind: testability, documentation.
    - **Where:**
      - [iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs:140](../../iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs): any type no constructor can build is allocated with `GetUninitializedObject` and counts as a success; again at `:242`, `:306` and `:324`.
      - [iConsole.Test/Generated/InstantiateIConsoleTypesTest.cs:41](../../iConsole.Test/Generated/InstantiateIConsoleTypesTest.cs): the same fallback.
      - [iTerminal.Core.Test/Application/iTerminalApplicationBuilderExtensionTest.cs:22](../../iTerminal.Core.Test/Application/iTerminalApplicationBuilderExtensionTest.cs): checks registrations by type and builds nothing.
      - The contract is in [CLAUDE.md:114](../../CLAUDE.md) and [testing.md:9](../architecture/testing.md). No `.cs` file passes `ValidateOnBuild`, and the host builder in [iConsoleApplicationBuilder.cs:19](../../iConsole/Application/iConsoleApplicationBuilder.cs) does not either.
    - **Problem:** neither test uses the container, and both fall back to an uninitialised object, so a public type whose dependencies are not registered passes. Since the last pass, #790 to #802 moved registrations for four more projects. The dialogs that need them are transient and resolved only when opened, so a host that misses one builds, starts, and fails on first use. Only the end-to-end suite (4 to 5 minutes) reaches that path. A validated container still cannot see an optional parameter whose service is missing (finding 32). Principle: testability seams, explicit contracts.
    - **Improvement:** add `iTerminal.Core.Test/Application/CompositionRootTest`. It registers what `Program.cs` registers (`AddDefaultConsoleDriver`, `AddiConsoleCore`, `AddiTerminal` with a temporary `DataDirectory`, and the sandbox replacements the end-to-end host uses), builds with `new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }`, and resolves every registered non-factory service type once. Then correct `CLAUDE.md:114` and `testing.md:9` to say what `InstantiateAllTypesTest` checks (that each public type can be allocated and, where possible, constructed), and point to the new test for the "the container can resolve it" contract.
    - **Effort:** S to M. The sandbox list exists in [iTerminal.Test/TerminalApp.cs:200](../../iTerminal.Test/TerminalApp.cs); some factories may need a stand-in.
    - **Risk of the change:** the first run may fail on real gaps or on services whose constructor touches the machine (`UpdateService` reads the registry on Windows). Each is either a fix or a sandbox registration. No production change.

19. **Five dialogs hand-write the same worker-to-UI hand-off, in two styles** — **Medium**, kind: concurrency, consistency.
    - **Where:**
      - With a lock, a generation counter, an in-flight count and `PendingOperation`: [PrintDialog.cs:84](../../iTerminal.Core/Dialogs/PrintDialog.cs) and `:168` (19 `lock (_sync)` blocks), [StreamsDialog.cs:51](../../iTerminal.Core/Dialogs/StreamsDialog.cs) and `:267`, [TorrentManagerDialog.cs:45](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs) and `:355`.
      - With `volatile` fields instead of a lock: [BluetoothDeviceManagerDialog.cs:31](../../iTerminal.Core/Dialogs/Bluetooth/BluetoothDeviceManagerDialog.cs) and `:278`.
      - [ToDoListDialog.cs:42](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs) and `:93`.
      - Across `iTerminal.Core/Dialogs` and `iConsole/Dialogs`, 12 files expose a `PendingOperation` (unchanged), and `iConsole/Tasks` still has no shared primitive for it. Design review, finding 68.
    - **Problem:** each dialog re-implements the threading rule "don't mutate the control tree from a worker" with its own fields, lock and stale-result check. A slip in one copy (a missed generation check, a write outside the lock) is a race that only shows under load, and the tests poke `ListCountForTest`-style internals rather than a shared contract. Principle: consistency with the framework, explicit contracts for background work.
    - **Improvement:** one primitive in `iConsole/Tasks`, for example:

      ```csharp
      public sealed class UiHandoff<T>
      {
          public int Begin();                         // any thread: starts a new request, returns its generation
          public void Post(int generation, T value);  // any thread: kept only if generation is still current
          public bool TryTake(out T value);           // UI thread (OnIdle, key, mouse): the latest value, once
          public Task Pending { get; }                // what tests await instead of PendingOperation
      }
      ```

      Convert the five dialogs of #778 first (they apply with `ConcurrentObservableCollection.ReplaceAll` already), then the rest as they are touched. Document it in `ui-framework.md` beside `TaskManager`.
    - **Effort:** M (the primitive is S; each dialog is S).
    - **Risk of the change:** a dialog that relied on a side effect of its own lock may change order. The five dialog test classes (`PrintDialogTest`, `StreamsDialogTest`, `TorrentManagerDialogTest`, `BluetoothDeviceManagerDialogTest`, `ToDoListDialogTest`) await `PendingOperation` and would show it.

20. **`IFileToTextConverterService` mixes extension routing with conversion, so two fakes forward to the real converter, and `iConsole.Test` depends on `iConsole.Formats`** — **Low**, kind: interface, testability.
    - **Where:**
      - [iConsole/Services/IFileToTextConverterService.cs:32](../../iConsole/Services/IFileToTextConverterService.cs) to `:41`: four predicates.
      - Five call sites: [FileViewerDialog.cs:305](../../iConsole/Dialogs/FileViewerDialog.cs), `:453`, `:473`, `:553`, and [TextFileEditorDialog.cs:641](../../iConsole/Dialogs/TextFileEditorDialog.cs).
      - The two fakes that forward the predicates to the real converter: [FileViewerDialogDisassemblyTest.cs:17](../../iConsole.Test/Dialogs/FileViewerDialogDisassemblyTest.cs) and [FileViewerDialogPdfSignaturesTest.cs:22](../../iConsole.Test/Dialogs/FileViewerDialogPdfSignaturesTest.cs).
      - [iConsole.Test/iConsole.Test.csproj:30](../../iConsole.Test/iConsole.Test.csproj) (the reference) and `:40` (fixtures linked from the other test project).
      - 21 files in `iConsole.Test` use an `iConsole.Formats.*` namespace (15 at the last pass; #802 moved more readers).
      - `TextFileEditorDialogTest` now uses a mock ([TextFileEditorDialogTest.cs:18](../../iConsole.Test/Dialogs/TextFileEditorDialogTest.cs)), so that part of the earlier finding no longer applies.
    - **Problem:** the predicates are lookups in extension tables and need no format package. On the interface, though, every fake must implement them, and the only way to get them right is to call the production class. So the `iConsole` tests need `iConsole.Formats` and its packages, which the seam was meant to avoid. Principle: interface segregation.
    - **Improvement:** move the extension tables the predicates read into `iConsole` as a static `ViewerFileKinds` class, following the existing `*FileTypes` classes there, and call it from the two dialogs:

      ```csharp
      // before (on IFileToTextConverterService)
      bool IsEncodingRelevant(Url? url);
      bool HasDisassembly(Url? url);
      bool HasPdbSummary(Url? url);
      bool HasPdfSignatures(Url? url);
      // after (iConsole/Services/ViewerFileKinds.cs)
      public static class ViewerFileKinds
      {
          public static bool IsEncodingRelevant(Url? url);
          public static bool HasDisassembly(Url? url);
          public static bool HasPdbSummary(Url? url);
          public static bool HasPdfSignatures(Url? url);
      }
      ```

      The converter keeps using the same tables. The two fakes lose their forwarding lines. The tests in the 21 files that need the real readers either move to `iConsole.Formats.Test` or get a fake, so the `iConsole.Formats` reference can leave `iConsole.Test.csproj`. Callers: 5 in two dialogs; implementations: 3 (one real, two fakes). It removes interface members, so any implementer outside this solution breaks (none is known in this repository).
    - **Effort:** S for the interface; M with the 21 test files.
    - **Risk of the change:** a table that differs between the static class and the converter would show a wrong code-page switch or a missing disassembly. `FileViewerDialogDisassemblyTest`, `FileViewerDialogPdfSignaturesTest`, `TextFileEditorDialogTest` and `FileToTextConverterServiceFixtureTest` cover each predicate.

27. **The app version is read in two places with two fallbacks, and `UpdateService` takes it from a dialog** — **Low**, kind: layering, consistency.
    - **Where:** [iTerminal.Core/Application/ApplicationInfo.cs:8](../../iTerminal.Core/Application/ApplicationInfo.cs) (fallback `"0.0.0.0"`) and [iTerminal.Core/Dialogs/AboutDialog.cs:81](../../iTerminal.Core/Dialogs/AboutDialog.cs) (fallback `"unknown"`, at `:94`). Users: [UpdateService.cs:69](../../iTerminal.Core/Services/Updates/UpdateService.cs) (the service's current version comes from `AboutDialog`), [TerminalScreen.cs:54](../../iTerminal.Core/Screens/TerminalScreen.cs) (the title, from `ApplicationInfo`) and `:78` (the splash, from `AboutDialog`), and [iTerminal/Program.cs:104](../../iTerminal/Program.cs) (`ApplicationInfo`).
    - **Problem:** one fact has two readers that differ on the edge cases. On a build without an informational version, the window title and the update check can disagree about the running version; `"unknown"` parses to no version, and the check then offers every release. A service that depends on a dialog type also inverts the order inside `iTerminal.Core`. Principle: dependency direction, one place per decision.
    - **Improvement:** keep `ApplicationInfo.Version` as the only reader. Make `AboutDialog.Version` return it (or remove it and change its uses), and pass `ApplicationInfo.Version` in `UpdateService`'s public constructor. No public API changes (`AboutDialog.Version` is `internal`).
    - **Effort:** S.
    - **Risk of the change:** the About dialog shows `0.0.0.0` instead of `unknown` on an unstamped build. `AboutDialogTest`, `ControlIntegrationTest` and `KeyHintGuardTest` compare against the property and stay green.

28. **The WinGet case is decided separately in each consumer of `IUpdateService`** — **Low**, kind: interface, consistency.
    - **Where:**
      - [IUpdateService.cs:47](../../iTerminal.Core/Services/Updates/IUpdateService.cs) (`InstallKind`) and `:50` (`CanInstall`).
      - Three consumers each branch on `InstallKind == UpdateInstallKind.PackageManager` and build the hint themselves:
        - [UpdateDialog.cs:165](../../iTerminal.Core/Dialogs/UpdateDialog.cs);
        - [UpdateCheckJob.cs:116](../../iTerminal.Core/Jobs/UpdateCheckJob.cs), with the same localized key and English fallback as the dialog, copied;
        - [UpdateCommandLine.cs:53](../../iTerminal.Core/Services/Updates/UpdateCommandLine.cs), with English text and `UpdateService.WinGetPackageId`, checked before `CanInstall` at `:59`.
    - **Problem:** "why this copy cannot install, and what to do instead" is a decision of the service, but each consumer re-derives it from the enum. The next install variant touches three files plus the enum, and the copies of the text can drift. Principle: explicit contracts, evolution cost.
    - **Improvement:** add one member and use it in the three places:

      ```csharp
      // IUpdateService (new)
      /// <summary>Why this copy does not install a release itself and what the user does instead; null when CanInstall is true.</summary>
      string? ManualUpdateHint { get; }
      ```

      `UpdateService` builds it from its install kind, `WinGetPackageId` and `ReleasesAddress` with the existing `Update.WinGetManaged` and `Update.DownloadFrom` keys. Callers: 3. Implementations: `UpdateService` plus the test doubles in `UpdateCommandLineTest`, `UpdateCheckJobTest`, `UpdateDialogProgressTest` and `DialogKeyHintsTest`. The interface is internal to the app.
    - **Effort:** S.
    - **Risk of the change:** the command line's output text changes if it starts using the localized string; `UpdateCommandLineTest` and `UpdateServiceTest` assert on it.

38. **Test projects reference each other, and the shared test helper pulls the video stack into every test** — **Low**, kind: layering, testability.
    - **Where:**
      - [iConsole.Test.Core/iConsole.Test.Core.csproj:16](../../iConsole.Test.Core/iConsole.Test.Core.csproj) references `iConsole.Media.Video` for one fake ([FakeVideoDecoder.cs:1](../../iConsole.Test.Core/Video/FakeVideoDecoder.cs)), used by four tests in `iTerminal.Core.Test/Dialogs/Video` and one in `iConsole.Media.Video.Test`.
      - So every project that uses the helper (`iConsole.Test`, `iConsole.Formats.Test`, `iConsole.Media.Audio.Test`, `iTerminal.Core.Test`, `iTerminal.Test`) also builds `iConsole.Media.Video`, `iConsole.Formats` with SkiaSharp's natives, and `iConsole.Media.Audio`.
      - [iTerminal.Core.Test/iTerminal.Core.Test.csproj:34](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj) references the test project `iTerminal.Core.Games.Test` for one helper, `ChessTest.FromFen` ([GamesDialogTest.cs:279](../../iTerminal.Core.Test/Dialogs/Games/GamesDialogTest.cs)).
    - **Problem:** the helper project for the framework's tests sits above a media project, so the `iConsole` tests cannot be built without layers that `CLAUDE.md` places above `iConsole`, and a change in the video project rebuilds every test project. A test project that references another one copies its test assembly into its output; whether the runner then runs those tests twice was not checked (**unverified**). Principle: dependency direction, applied to the tests.
    - **Improvement:** move `FakeVideoDecoder` and `FakeVideoDecoderFactory` to `iTerminal.Core.Test/Helper` (four users), give `iConsole.Media.Video.Test` its own copy or a linked file, and drop the reference from `iConsole.Test.Core`. Move FEN parsing into the engine as `ChessState.FromFen` (it is a domain function, and the engine's own tests use it), and drop the test-to-test reference.
    - **Effort:** S.
    - **Risk of the change:** none at run time; the build of each test project shows a missing type.

**What works well**

- The edges are the right ones:
  - `iConsole.Formats` → `iConsole` only ([iConsole.Formats.csproj:38](../../iConsole.Formats/iConsole.Formats.csproj));
  - `iConsole.Media.Video` → `iConsole.Formats` and `iConsole.Media.Audio`, as `CLAUDE.md` states ([iConsole.Media.Video.csproj:16](../../iConsole.Media.Video/iConsole.Media.Video.csproj) and `:17`);
  - `iTerminal.Core` → all of them;
  - `iTerminal` reaches them through `iTerminal.Core`.
- Each new project says in its project file what it holds and why it is separate, and each has its own test project. For example, [iTerminal.Core.Games.csproj:9](../../iTerminal.Core.Games/iTerminal.Core.Games.csproj) says a change to a game rebuilds that project and its tests, not the whole of `iTerminal.Core`.
- The namespace is the layer: implementations in `iConsole.Formats.*` and `iConsole.Media.*`, contracts in `iConsole.*`. A layering test can be written against namespaces.
- The seams are small and owned by the consumer: `IStructuredDocumentParser`, `IExecutableDisassembler` (with "blocking file I/O: the caller runs it on a worker thread" in the type), `IVideoDecoderFactory`, `ISpeechRecognizer` and `IMicrophoneCaptureFactory`.
- #788 put the WinGet decision where it can be tested (`UpdateService.KindFor` takes the registry answer as a `bool`), and `--help` returns before any service, database or terminal is touched.

**Research cross-reference**

- Design review, finding 68 (hand-painted tables): finding 19 is the architecture follow-up, so the remaining conversions do not add more copies of the hand-off.
- Profiling overview, finding 4: still does not measure the `--update` path, which builds every registration before it runs.

**Suggested order**

1. Quick wins: finding 38, finding 27, finding 28.
2. Finding 18 (a validated container test), before finding 32.
3. Finding 20 (routing out of the converter interface): the interface part first, the 21 test files after.
4. Finding 19 (one hand-off primitive), on its own branch; convert the five #778 dialogs in the same pull request.

## Dependencies and packages

Reviewed on 2026-11-21 (fourth pass, within the whole-repository pass, replacing the third one at `70cc25fb`). Scope "Dependencies and packages"; read at `38ed0b4b` and re-checked at `a96d338c` after #805 removed three references (see "Whole repository"). Since the third pass the project files changed as follows:

- #789 added the notice generator `iTerminal.ThirdPartyNotices` and `iTerminal/Build/ThirdPartyNotices.targets`.
- #790, #792, #796 and #797 added four projects and their test projects. `NLayer` moved to `iConsole.Media.Audio`, and `Whisper.net` with its runtime to `iConsole.Media.Speech`.
- #802 moved the remaining format readers.
- #805 removed `Svg.Skia` and the Windows plug-in reference from `iTerminal.Core`, and `ZXing.Net` from `iConsole.Formats.Test`.

Read: every `*.csproj`, the `.wixproj`, the Linux `.proj`, `Directory.Build.props`, `iTerminal.slnx`, the notice generator and its overrides, `Package.wxs`, `build-and-packaging.md`, `runtime-dependencies.md`, and the release and WinGet workflows.

Run against nuget.org, read-only, at `38ed0b4b`: `dotnet list iTerminal.slnx package --outdated`, `--vulnerable --include-transitive` and `--deprecated` (22 PackageReference projects), and `--vulnerable --include-transitive` for `iTerminal.ThirdPartyNotices`, which is not in the solution. #805 only removed references, so the results still hold at `a96d338c`. The WiX project is not a PackageReference project, and `Packaging.Targets` is restored only when `LinuxPackaging` is `true`, so their latest versions stay **unverified**, as does the stable line of `Serilog.Sinks.File`.

Licence check: `iTerminal/obj/project.assets.json` has the targets `net11.0` and `net11.0/linux-x64`. 94 packages under them carry runtime, native or resource files, and every nuspec was in the local cache. The Windows runtime target was not restored here, so the Windows-only natives are **unverified**.

Not done: no build, no test run, no publish. There is still no `global.json`, `Directory.Packages.props`, `Directory.Build.targets`, `nuget.config` or `.github/dependabot.yml`.

**Summary**

- No package has a known vulnerability, direct or transitive, in any of the 23 projects checked. `OpenMcdf` 3.4.0 is the only update and is held back on purpose (finding 25); `imobiledevice-net` is the only deprecated package (finding 4).
- Licences of the 94 shipped packages:
  - 75 state MIT, 6 Apache-2.0, and 6 a licence file (5 MIT texts and SQLite's public-domain notice);
  - one each: MPL-2.0 (`OpenMcdf`), LGPL-3.0-or-later (`SMBLibrary`), BSD-3-Clause (`Snappier`), BSD-2-Clause (`Tomlyn`) and MS-PL (`Svg.Custom`);
  - two state a URL only (`K4os.Compression.LZ4`, `iMobileDevice-net`); the generator's `Overrides.txt` covers both (MIT and LGPL-2.1).

  No shipped package is under a strong copyleft licence. Since #789 every release carries `THIRD-PARTY-NOTICES.txt` and `LICENSE.txt`, and the MSI harvests the whole publish folder ([Package.wxs:105](../../iTerminal.Installer/Package.wxs)). The publish is ReadyToRun, not single-file, so the two LGPL libraries stay separate DLLs that a user can replace.
- The audit is not silenced (no `NuGetAuditMode`, `NuGetAuditLevel` or `NoWarn` for `NU1901` to `NU1904` anywhere), but it is not enforced either (finding 6).
- Changed since the third pass: #805 resolved findings 10, 24 and 26; the test packages are now repeated in up to 12 project files (finding 5); the RPM licence field is now `MIT` (part of finding 15).
- New and small: the notice generator is outside the solution and only warns on a missing licence (finding 40).

**Findings**

3. **The log sink is a development pre-release** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:35](../../iTerminal.Core/iTerminal.Core.csproj) (`Serilog.Sinks.File` `8.0.0-dev-02318`, there since the first public commit `6a1069a2`). The only use is `.WriteTo.File(path)` in [iTerminal.Core/Logger/Logger.cs:33](../../iTerminal.Core/Logger/Logger.cs).
   - **Problem:** every release ships a CI build of the sink that was never published as stable, and `dotnet list --outdated` cannot report a stable update for it because the stable line is below it. The one call used exists in every stable version. Principle: shipped dependencies are stable unless a feature needs otherwise.
   - **Improvement:** reference the newest stable `Serilog.Sinks.File` (7.x; the exact number was not listed by the commands run, so it is **unverified**) and keep `Serilog` 4.4.0.
   - **Effort:** S.
   - **Risk of the change:** a downgrade of a pre-release. The build and `iTerminal.Core.Test` (the `Logger` tests) show any API gap, and NuGet reports NU1605 if another package needs the newer one.

4. **`imobiledevice-net` is deprecated and its Linux native files are never shipped; the warning that says so is silenced** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:25](../../iTerminal.Core/iTerminal.Core.csproj); [Directory.Build.props:6](../../Directory.Build.props) (`NETSDK1206` suppressed for the whole solution); [iTerminal.Core/Services/IPhoneDeviceService.cs:37](../../iTerminal.Core/Services/IPhoneDeviceService.cs) (a failure to load shows no devices); [panels-and-drives.md:26](../architecture/panels-and-drives.md) (`iphone://`).
   - **Problem:** `dotnet list --deprecated` still reports the package as "Legacy" with no alternative. Its native files are for `win-x86`, `win-x64`, `osx-x64` and `ubuntu.16.04-x64` only. .NET 8 and later ignore the Ubuntu identifier, so the Linux packages carry no `libimobiledevice`, and on Linux the `iphone://` drive can only work with a system library of a matching name. The suppressed warning is the build's only signal of that. Whether a system library is picked up was not tried: **unverified**.
   - **Improvement:** decide the platform contract of the iPhone drive first. If it is Windows only (and macOS x64), say so in `panels-and-drives.md`, hide the drive elsewhere through `OperatingSystem` checks, and scope the `NoWarn` to `iTerminal.Core.csproj` with a comment that names the effect. If Linux matters, the replacement needs a maintained binding, which is a new dependency decision for the maintainer.
   - **Effort:** M.
   - **Risk of the change:** hiding the drive changes the drive list on Linux. The drive-selector tests in `iTerminal.Core.Test` and the Places page tests in `iTerminal.Test` show it.

29. **The WinGet workflow runs an unpinned, unverified `wingetcreate` with the publishing token** — **Medium**, kind: dependency.
   - **Where:** [.github/workflows/winget.yml:52](../../.github/workflows/winget.yml) (`Invoke-WebRequest https://aka.ms/wingetcreate/latest -OutFile wingetcreate.exe`), `:59` (run without the token) and `:82` (run with `--token $env:WINGET_PAT`, a `public_repo` token, [build-and-packaging.md:15](../architecture/build-and-packaging.md)). The actions in the workflows are pinned by major tag, not by commit.
   - **Problem:** what runs depends on the day. A redirect chooses the binary, nothing records which version produced a submitted manifest, and nothing checks the file before it receives a token that can push to a fork and open pull requests on `microsoft/winget-pkgs`. The workflow runs only when started by hand, which limits the exposure. Principle: reproducibility, explicit contracts.
   - **Improvement:** download a fixed release from `github.com/microsoft/winget-create/releases/download/v<version>/wingetcreate.exe` and compare `Get-FileHash -Algorithm SHA256` with a hash written in the workflow, failing the job on a mismatch; note the version in the run summary. Pinning the actions by commit belongs with finding 6.
   - **Effort:** S.
   - **Risk of the change:** a version bump needs the hash updated in the same edit. A stale hash fails the run before anything is submitted.

5. **No Central Package Management: each version is written in up to twelve project files** — **Medium**, kind: dependency, consistency.
   - **Where:** there is no `Directory.Packages.props`.
     - `MSTest.TestFramework` 4.5.3 is in 12 project files; `MSTest.TestAdapter` 4.5.3 and `Microsoft.NET.Test.Sdk` 18.12.0 in 11; `coverlet.collector` 10.1.1 in 9; `Moq` 4.21.0 in 4.
     - `Microsoft.Data.Sqlite` 11.0.0 and `Microsoft.Playwright` 1.65.1 are in 2 each ([iTerminal.Core/iTerminal.Core.csproj:32](../../iTerminal.Core/iTerminal.Core.csproj), [iTerminal.Core.Test/iTerminal.Core.Test.csproj:26](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj)).
     - Several `Version="11.0.0"` lines cover the Microsoft packages that move with the runtime.
     - The WiX version 5.0.2 appears four times in [iTerminal.Installer/iTerminal.Installer.wixproj:20](../../iTerminal.Installer/iTerminal.Installer.wixproj), `:31`, `:32` and `:57`.
   - **Problem:** an update means editing every copy by hand, and a missed copy is silent. The four project splits since the third pass added eight more project files with copied test-package lines. The Playwright pair matters most: `PlaywrightRuntimeDependency.LibraryVersion()` ([PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs)) installs the driver for the library version, so a test project on another version tests a driver the app never asks for. Principle: one place per decision.
   - **Improvement:** add `Directory.Packages.props` with `ManagePackageVersionsCentrally` and one `PackageVersion` per package, and remove the `Version` attributes from the `PackageReference` items. Keep the WiX SDK version in one MSBuild property used by both `Import` elements and the two extensions. `Packaging.Targets` can stay conditional.
   - **Effort:** M (about 20 project files, mechanical).
   - **Risk of the change:** restore resolves exactly as before if the versions are copied as they are; `dotnet list package --include-transitive` before and after must match. Build and run all test projects; the Windows job checks the installer.

6. **Restore has no guard rails: no lock files, no audit gate, no update automation, no source mapping** — **Medium**, kind: dependency.
   - **Where:** no `packages.lock.json`; no `NuGetAuditMode`, `NuGetAuditLevel` or `WarningsAsErrors` for `NU1901`–`NU1904` in any project file; no `nuget.config`; no `.github/dependabot.yml`. The build workflows restore from the default feed ([.github/workflows/build.yml:32](../../.github/workflows/build.yml)).
   - **Problem:** today `--vulnerable --include-transitive` is clean, but nothing makes a future advisory fail a pull request: the SDK's audit only warns. Without lock files a release build can resolve a different transitive set from the one tested. Without package source mapping, any feed added later is searched for every package. Principle: explicit contracts for the build.
   - **Improvement:**
     - in `Directory.Build.props`, set `NuGetAuditMode` to `all` and turn `NU1903` and `NU1904` (high and critical) into errors;
     - add `RestorePackagesWithLockFile`, with `--locked-mode` in the workflows;
     - add a `nuget.config` with one source and `packageSourceMapping`;
     - add a Dependabot `nuget` entry (with an `ignore` rule for `OpenMcdf` 3.4.x, finding 25) and a `github-actions` entry, which would also propose commit pins for the actions (finding 29).
   - **Effort:** S for audit and source mapping, M with lock files.
   - **Risk of the change:** lock files fail a restore when a version changes without the file, and the build workflow shows it at once. An audit error can block a release on an advisory that has no fix, so the level is the maintainer's call.

7. **Runtime downloads follow four different version policies, and two of them float** — **Medium**, kind: dependency, consistency.
   - **Where:**
     - [OllamaRuntimeDependency.cs:27](../../iTerminal.Core/Services/RuntimeDependencies/OllamaRuntimeDependency.cs) and `:75`: `Built` 0.40.4, any 0.40.x accepted, the newest offered;
     - [PowerShellRuntimeDependency.cs:24](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs) and `:84`: `Built` 7.7.0, any 7.x;
     - [WhisperRuntimeDependency.cs:29](../../iTerminal.Core/Services/RuntimeDependencies/WhisperRuntimeDependency.cs): pinned URL, size and SHA-256;
     - [PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs): the library's own version, NuGet SHA-512;
     - the package-manager tools (git, ffmpeg, yt-dlp, rar), [runtime-dependencies.md:5](../architecture/runtime-dependencies.md) to `:22`.
   - **Problem:** for Ollama and PowerShell, what a user installs depends on the day: the newest release of the line, checked only against the digest GitHub records next to the file. A release cannot say which version it was tested with, and one compromised account changes the file and its digest together (security overview, item 27). Principle: explicit contracts and reproducibility. The Playwright and Whisper policies are the sound ones.
   - **Improvement:** give `OllamaRuntimeDependency` and `PowerShellRuntimeDependency` a pinned default, installed by default: a version plus the SHA-256 of each platform asset, verified once by a real download as for Whisper. Keep "newer on the same line" as an offered update that the user accepts. Put the pins of all four downloaded dependencies in one internal `RuntimeDependencyPins` class.
   - **Effort:** M.
   - **Risk of the change:** a wrong pinned hash refuses the install; `VerifiedDownload` already deletes a mismatch. The `*RuntimeDependencyTest` classes fake the feeds and cover the line rule.

9. **`Microsoft.FeatureManagement` is referenced and never used** — **Low**, kind: dependency.
   - **Where:** [iConsole/iConsole.csproj:17](../../iConsole/iConsole.csproj), one of only two packages `iConsole` references. The only feature flag is read as plain configuration in [iConsole.Inputs.Windows/Extensions/BuilderExtension.cs:27](../../iConsole.Inputs.Windows/Extensions/BuilderExtension.cs). No source file uses the `Microsoft.FeatureManagement` namespace, `AddFeatureManagement` or `IFeatureManager`.
   - **Problem:** every consumer of `iConsole` restores and ships the package for nothing, and that now includes the three media projects and the game engines. Principle: no unused dependencies.
   - **Improvement:** remove the reference. Keep the `FeatureManagement` section name in `appSettings.json`, so existing settings still apply.
   - **Effort:** S.
   - **Risk of the change:** a transitive package it brought may be needed elsewhere; the build shows a missing type. `BuilderExtensionCoverageTest` covers the flag.

14. **The SDK floats, and a compiler feature is switched on for every project without a note in the architecture pages** — **Low**, kind: dependency, documentation.
    - **Where:** no `global.json`; the build workflow installs `11.0.x` ([.github/workflows/build.yml:32](../../.github/workflows/build.yml) and `:140`); [Directory.Build.props:8](../../Directory.Build.props) (`runtime-async=on`).
    - **Problem:** a new SDK feature band changes analyzers and the compiler under the same commit, so a red build on `main` can come from the runner image, not the code. `runtime-async` changes how every async method runs (stack traces, profiler output, the `Job` and `TaskManager` behaviour the threading rules rely on), yet no page in `Documentation/architecture/` mentions it. Whether the current SDK still needs the switch was not checked: **unverified**.
    - **Improvement:** add a `global.json` with `"version": "11.0.100"` and `"rollForward": "latestPatch"`. Add one paragraph in `ui-framework.md` (threading) that says runtime async is on, why, and how to turn it off for a diagnosis.
    - **Effort:** S.
    - **Risk of the change:** a machine without that feature band fails to build until it installs it; the error says which version.

15. **The packaging files and pages disagree on the default version, the licence page and arm64** — **Low**, kind: documentation.
    - **Where:**
      - The default version: [Directory.Build.props:4](../../Directory.Build.props) (`0.0.0.0`); [iTerminal.Installer/iTerminal.Installer.wixproj:22](../../iTerminal.Installer/iTerminal.Installer.wixproj) and [iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj:27](../../iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj) (`1.0.0`); [build-and-packaging.md:10](../architecture/build-and-packaging.md) ("default `1.0.0`").
      - The licence: [Package.wxs:37](../../iTerminal.Installer/Package.wxs) says "the repository has no licence text", while [build-and-packaging.md:12](../architecture/build-and-packaging.md) says the `LICENSE` file is MIT.
      - arm64: [build-and-packaging.md:58](../architecture/build-and-packaging.md) says "`linux-arm64` is mapped but has not been built here", while the release workflow builds it.
      - Fixed since the third pass: the RPM licence field is now `MIT` ([iTerminal.Packaging.Linux.proj:30](../../iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj)).
    - **Problem:** three defaults for one number, and two sentences that are no longer true. Releases always pass `-p:Version`, so only local builds and readers are affected.
    - **Improvement:** keep the default in `Directory.Build.props` only, give the Linux `.proj` (not an SDK project) the same `0.0.0.0`, correct the `Package.wxs` comment, and correct the two sentences in `build-and-packaging.md`.
    - **Effort:** S.
    - **Risk of the change:** a local `.deb` built without `-p:Version` gets version `0.0.0.0`, which dpkg accepts.

25. **`OpenMcdf` is held at 3.3.0 for a known defect that is recorded only in a commit message** — **Low**, kind: dependency, documentation.
    - **Where:** [iConsole.Formats/iConsole.Formats.csproj:21](../../iConsole.Formats/iConsole.Formats.csproj) has no comment, while the references beside it each have one. The reason is in the message of `74982a77` (#777): 3.4.0 throws "Commit or revert the changes to a transacted storage before consolidating it" when the `.msg` and legacy Office readers dispose their read-only storage. `dotnet list --outdated` still reports 3.4.0 as the latest.
    - **Problem:** the next update pass, by hand or by automation (finding 6), sees an outdated package and no reason not to take it. Principle: a pin says why.
    - **Improvement:** add an XML comment above the reference with the error text, the two readers, and the condition for lifting the pin: a release after 3.4.0 that disposes a read-only storage without committing, checked with the `.msg`, `.doc`, `.xls` and `.ppt` fixtures.
    - **Effort:** S.
    - **Risk of the change:** none (a comment).

40. **The notice generator is outside the solution, has no test, and a package with no licence is only a warning** — **Low**, kind: dependency.
    - **Where:**
      - `iTerminal.slnx` does not list `iTerminal.ThirdPartyNotices`, so `dotnet build iTerminal.slnx` in CI never compiles it. It is first compiled by `dotnet run` inside a publish ([ThirdPartyNotices.targets:28](../../iTerminal/Build/ThirdPartyNotices.targets)).
      - A package without a licence statement is reported with `Warn`, and the publish goes on ([iTerminal.ThirdPartyNotices/Program.cs:244](../../iTerminal.ThirdPartyNotices/Program.cs)).
      - [Overrides.txt](../../iTerminal.ThirdPartyNotices/Licenses/Overrides.txt) is keyed by package id and version, so an update of `iMobileDevice-net` or `K4os.Compression.LZ4` makes its line stop matching.
      - [build-and-packaging.md:38](../architecture/build-and-packaging.md) documents the warning.
    - **Problem:** a change that breaks the generator shows only in a release build, and a package update can ship a notice that says "not stated" for an LGPL library without failing anything. Principle: explicit contracts for the release.
    - **Improvement:** add the project to `iTerminal.slnx`, and add a `--strict` switch that makes a missing licence an error, passed by the release workflow's publish (local publishes keep the warning).
    - **Effort:** S.
    - **Risk of the change:** a release fails on a new package with no licence statement until its override line is added, which is the intent.

**Package table**

- "Used by" is the number of source files, in all projects with tests included, that have a `using` of the package's namespace. "native" means the package carries native files only; "build only" means a package used by the build.
- "Latest" is the latest stable version on nuget.org on 2026-11-21, from `dotnet list --outdated`, which reported no update for any package other than `OpenMcdf`.
- "Licence" is the nuspec's statement, and `—` for a build-only or test-only package.
- "Advisories" is the result of `--vulnerable --include-transitive`.

| Package | Project | Current | Latest | Major change | Licence | Advisories | Used by (count) | Action |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Microsoft.Extensions.Hosting | iConsole | 11.0.0 | 11.0.0 | No | MIT | none | 14 | Keep (the host builder) |
| Microsoft.FeatureManagement | iConsole | 4.8.0 | 4.8.0 | No | MIT | none | 0 | Remove (finding 9) |
| HtmlAgilityPack | iConsole.Formats | 1.13.1 | 1.13.1 | No | MIT | none | 2 | Keep |
| Iced | iConsole.Formats | 1.22.0 | 1.22.0 | No | MIT | none | 1 | Keep |
| JetDatabaseReader | iConsole.Formats | 3.0.0 | 3.0.0 | No | MIT | none | 1 | Keep |
| DocumentFormat.OpenXml | iConsole.Formats | 3.5.1 | 3.5.1 | No | MIT | none | 5 | Keep |
| OpenMcdf | iConsole.Formats | 3.3.0 | 3.4.0 | No | MPL-2.0 | none | 3 | Hold, write the reason down (finding 25) |
| PdfPig | iConsole.Formats | 0.1.16 | 0.1.16 | No | Apache-2.0 | none | 1 | Keep |
| SkiaSharp | iConsole.Formats | 4.157.0 | 4.157.0 | No | MIT | none | 20 | Keep |
| SkiaSharp.NativeAssets.Linux.NoDependencies | iConsole.Formats | 4.157.0 | 4.157.0 | No | MIT | none | native | Keep with SkiaSharp |
| Svg.Skia | iConsole.Formats | 5.2.3 | 5.2.3 | No | MIT | none | 1 | Keep (owner; used by iConsole.Media.Video) |
| System.Security.Cryptography.Pkcs | iConsole.Formats | 11.0.0 | 11.0.0 | No | MIT | none | 5 | Keep |
| Tomlyn | iConsole.Formats | 2.11.1 | 2.11.1 | No | BSD-2-Clause | none | 2 | Keep |
| YamlDotNet | iConsole.Formats | 18.1.0 | 18.1.0 | No | MIT | none | 2 | Keep |
| PureHDF | iConsole.Formats | 2.2.0 | 2.2.0 | No | MIT | none | 2 | Keep; watch the 3.0 line |
| Parquet.Net | iConsole.Formats | 6.1.0 | 6.1.0 | No | MIT | none | 3 | Keep |
| Microsoft.Data.Sqlite | iConsole.Formats, iTerminal.Core | 11.0.0 | 11.0.0 | No | MIT | none | 12 | Keep in both (the comment says why); centralize (finding 5) |
| NLayer | iConsole.Media.Audio | 3.0.1 | 3.0.1 | No | MIT | none | 1 | Keep (moved in #792) |
| Whisper.net | iConsole.Media.Speech | 1.9.1 | 1.9.1 | No | MIT (licence file) | none | 1 | Keep (moved in #797) |
| Whisper.net.Runtime | iConsole.Media.Speech | 1.9.1 | 1.9.1 | No | MIT | none | native | Keep |
| System.CommandLine | iTerminal.Core | 3.0.0 | 3.0.0 | No | MIT | none | 1 | Keep |
| FluentFTP | iTerminal.Core | 55.1.0 | 55.1.0 | No | MIT | none | 2 | Keep |
| Microsoft.AspNetCore.DataProtection | iTerminal.Core | 11.0.0 | 11.0.0 | No | MIT | none | 2 | Optional drop (brought by .Extensions) |
| Microsoft.AspNetCore.DataProtection.Extensions | iTerminal.Core | 11.0.0 | 11.0.0 | No | MIT | none | 2 | Keep |
| Microsoft.EntityFrameworkCore | iTerminal.Core | 11.0.0 | 11.0.0 | No | MIT | none | 35 | Keep |
| Microsoft.EntityFrameworkCore.Sqlite | iTerminal.Core | 11.0.0 | 11.0.0 | No | MIT | none | provider of EF | Keep |
| Microsoft.Extensions.AI | iTerminal.Core | 11.0.1 | 11.0.1 | No | MIT | none | 14 | Keep |
| Microsoft.Extensions.AI.OpenAI | iTerminal.Core | 11.0.1 | 11.0.1 | No | MIT | none | with Microsoft.Extensions.AI | Keep |
| imobiledevice-net | iTerminal.Core | 1.3.17 | 1.3.17 (deprecated) | No | URL only (LGPL-2.1 by override) | none | 2 | Decide the platform contract (finding 4) |
| Microsoft.Mxc.Sdk | iTerminal.Core | 1.1.0 | 1.1.0 | No | MIT (licence file) | none | 2 | Keep |
| Microsoft.Playwright | iTerminal.Core | 1.65.1 | 1.65.1 | No | MIT | none | 2 | Keep |
| MonoTorrent | iTerminal.Core | 3.0.2 | 3.0.2 | No | MIT (licence file) | none | 1 | Keep |
| Serilog | iTerminal.Core | 4.4.0 | 4.4.0 | No | Apache-2.0 | none | 61 | Keep |
| Serilog.Sinks.File | iTerminal.Core | 8.0.0-dev-02318 | stable 7.x (unverified) | Yes (to stable) | Apache-2.0 | none | 1 | Move to stable (finding 3) |
| SharpZipLib | iTerminal.Core | 1.4.2 | 1.4.2 | No | MIT | none | 10 | Keep |
| SMBLibrary | iTerminal.Core | 1.5.8.1 | 1.5.8.1 | No | LGPL-3.0-or-later | none | 2 | Keep; notice shipped since #789 |
| SSH.NET | iTerminal.Core | 2026.0.1 | 2026.0.1 | No | MIT | none | 1 | Keep; quote the commands (finding 30) |
| ZstdSharp.Port | iTerminal.Core | 0.8.8 | 0.8.8 | No | MIT | none | 3 | Keep (no zstd in the BCL) |
| Packaging.Targets | iTerminal (Linux packaging only) | 0.1.232 | unverified | Unknown | — | not checked | build only | Check by hand |
| WixToolset.Sdk, .UI.wixext, .Util.wixext | iTerminal.Installer | 5.0.2 | unverified | Unknown | — | not checked | build only | Check by hand; one version property (finding 5) |
| MSTest.TestFramework | 12 project files | 4.5.3 | 4.5.3 | No | — | none | all tests | Keep; centralize (finding 5) |
| MSTest.TestAdapter | 11 test projects | 4.5.3 | 4.5.3 | No | — | none | all tests | Keep; centralize (finding 5) |
| Microsoft.NET.Test.Sdk | 11 test projects | 18.12.0 | 18.12.0 | No | — | none | all tests | Keep; centralize (finding 5) |
| coverlet.collector | 9 test projects | 10.1.1 | 10.1.1 | No | — | none | coverage | Keep |
| Moq | iConsole.Test.Core, iConsole.Test, iConsole.Formats.Test, iTerminal.Core.Test | 4.21.0 | 4.21.0 | No | — | none | about 550 | Keep |
| ZXing.Net | iConsole.Test | 0.16.11 | 0.16.11 | No | — | none | 1 | Keep |
| Reqnroll.MSTest | iTerminal.Specifications.Test | 3.3.4 | 3.3.4 | No | — | none | 75 | Keep |
| Microsoft.Playwright | iTerminal.Core.Test | 1.65.1 | 1.65.1 | No | — | none | test helper | Same version as Core (finding 5) |

Which check found what: the vulnerability check found no advisory for any package. The licence check found the LGPL, MPL-2.0 and URL-only statements in the table and the summary. `imobiledevice-net` was named by both the deprecation check and the licence check (URL only).

**What works well**

- Since #789 every installable form carries `THIRD-PARTY-NOTICES.txt` and `LICENSE.txt`, generated per runtime from `project.assets.json` and the nuspecs, with hand-checked overrides that name their source.
- The Playwright pair is on one version (1.65.1 in both projects), so the driver the tests use is the one the app installs. The library is shipped and its 130 MB driver is not ([iTerminal.Core/iTerminal.Core.csproj:27](../../iTerminal.Core/iTerminal.Core.csproj)).
- The Whisper model is pinned by commit URL, size and SHA-256 and written through `VerifiedDownload`, and every GitHub download refuses an asset without a published digest ([GitHubReleaseFeed.cs:39](../../iTerminal.Core/Services/RuntimeDependencies/GitHubReleaseFeed.cs)).
- The native-heavy packages have one owner each, and each project file says why: `iConsole.Formats` for SkiaSharp and the format libraries, `iConsole.Media.Speech` for Whisper, `iConsole.Media.Audio` for NLayer. Since #805, `iTerminal.Core.csproj` says in a comment that SkiaSharp and Svg.Skia arrive through those projects.
- Non-free and large tools (rar, unrar, 7-Zip, ffmpeg, git, yt-dlp) are left to the system package manager and never bundled.
- Every project targets `net11.0`, and the Microsoft packages that move with the runtime are on 11.0.0 or its first patch.

**Research cross-reference**

- Security overview, item 27 (TUF signed metadata for self-update): confirmed, and finding 7 extends the same weakness to the Ollama and PowerShell downloads.
- Security overview, "security-tools 3.6" (OSV package check): finding 6 is the same check for iTerminal's own build.
- No research document covers the release workflows; findings 29 and 40 are the items on that pipeline.

**Suggested order**

1. Quick wins, no dependencies: findings 9, 25, 29, 3, 40, 14 and 15.
2. Finding 5 (Central Package Management), then finding 6 (audit, lock files, source mapping, Dependabot with the `OpenMcdf` ignore).
3. Finding 4 (the iPhone drive's platform contract) and finding 7 (pinned runtime downloads), which need a decision from the maintainer first.

## Duplication and refactoring

Reviewed on 2026-11-22, scope "Duplication and refactoring" (asked for by name), on branch `feature/ai-panel-tools-more-operations` at commit `cf844281`. No review branch was cut, because the branch is not `main`.

Read: `CLAUDE.md`; `ui-framework.md`, `runtime-dependencies.md` and `testing.md`; the design-review, profiling and security overviews and the earlier sections of this document. In the source: the runtime-dependency classes, `SystemPackageInstallPlan`, `ExternalProcessRunner` and `IExternalProcessRunner`, `GitService`, `PackageManagerProcess` and `DefenderScanService`; the dialogs that hand-paint their rows (Contacts, Environment Variables, Online Radio, To-Do, Calendar, Mail, Password Manager, Remote Connections, Workspaces, Access Control, Print, Scanner, Streams, Torrents); `SplitPaneDialog`; `TextWidth`, `StringHelper`, `FileSizeHelper`, `TextEncodings`, `ProgressGroup`, `StreamCopyHelper` and `UrlPath`; `WeatherJob` and `OpenMeteoForecastProvider`; the settings-backed JSON stores; the stream-copy loops of the drive services; and the new AI panel tools.

Method: candidate blocks were found with `grep` on a distinctive line, the bodies were cut out with `awk` and compared by hash and `diff`, and the helpers that already exist were searched for by name and by job. Not done: no build, no test run, no run-time check. `dotnet list package` was not run, because no finding in this scope rests on a package. The `.claude/worktrees` folder holds copies of the source and was left out of every count.

**Summary**

- The strongest findings are drifted copies, where one copy has the fix and the others do not:
  - git and yt-dlp do not accept WinGet's "already installed" answer that ffmpeg and rar accept (finding 42);
  - `GitService` decodes git's output in the console code page, while the shared runner reads UTF-8 (finding 43);
  - the F3 viewer, the file comparison and the document index decode the same file differently (finding 46).
- In most cases the helper already exists and the copies do not use it: `TextWidth.Fit`, `ProgressGroup.FormatDuration`, `StreamCopyHelper`, `SystemPackageInstallPlan`, `IExternalProcessRunner`, `UrlPath.Child`. The cheapest fixes point copies at the helper that is already there.
- The largest families are hand-copied dialogs: 14 copies of the same 14-line `Put` (finding 44), and three record-manager dialogs written three times (finding 45).
- No finding is High. None loses data or causes a race, and each refactoring can be done one behaviour-preserving step at a time.
- Strong parts: the remote content models, the cloud services and the split-pane dialogs each share one base class. The AI panel tools added on this branch reuse the properties, checksum and search services instead of copying them.

**Findings**

42. **ffmpeg, git and yt-dlp are hand copies of `SystemToolRuntimeDependency`, and git and yt-dlp lost the WinGet "already installed" rule** — **Medium**, kind: duplication, consistency.
    - **Where:**
      - The base, used only by rar and unrar: [SystemToolRuntimeDependency.cs:14](../../iTerminal.Core/Services/RuntimeDependencies/SystemToolRuntimeDependency.cs). Its comment calls it "the same contract as `FfmpegRuntimeDependency` for one program", but it hard-codes RAR: version 5 at `:72` and `:74`, `https://www.rarlab.com/` at `:115`.
      - The copies: [FfmpegRuntimeDependency.cs:19](../../iTerminal.Core/Services/RuntimeDependencies/FfmpegRuntimeDependency.cs), [GitRuntimeDependency.cs:14](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs) and [YtDlpRuntimeDependency.cs:15](../../iTerminal.Core/Services/RuntimeDependencies/YtDlpRuntimeDependency.cs). Each repeats the cache tuple, `InstalledVersion`, `InstallAsync`, `Activate`, `Invalidate`, `ListVersionsAsync`, `FindTool` and `SafeWriteTime`. The `InstalledVersion` bodies differ only in a variable name. `SafeWriteTime` is byte-identical in these four files and in [PowerShellRuntimeDependency.cs:403](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs).
      - The drift: [SystemToolRuntimeDependency.cs:119](../../iTerminal.Core/Services/RuntimeDependencies/SystemToolRuntimeDependency.cs) and [FfmpegRuntimeDependency.cs:132](../../iTerminal.Core/Services/RuntimeDependencies/FfmpegRuntimeDependency.cs) treat WinGet's "no applicable update" and "already installed" exit codes (`:151`) as success. [GitRuntimeDependency.cs:116](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs) and [YtDlpRuntimeDependency.cs:127](../../iTerminal.Core/Services/RuntimeDependencies/YtDlpRuntimeDependency.cs) test `exitCode != 0` only. yt-dlp's messages are also English-only (`:123`, `:125`), while the other three use the `Dep.NoPackageManager` and `Dep.InstallingWith` keys.
      - `GitInstallPlan` ([GitRuntimeDependency.cs:278](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs) to `:340`) is a line-for-line copy of [SystemPackageInstallPlan.cs:19](../../iTerminal.Core/Services/RuntimeDependencies/SystemPackageInstallPlan.cs), with "git" written in where the general one takes `SystemPackageNames`.
    - **Problem:** on Windows, Tools > Runtime Dependencies reports a failed install of git or yt-dlp when WinGet answers that the package is already installed. ffmpeg and rar report success in the same case. The fix exists in the base class, but the copies never received it. The next system tool will copy whichever class its author opens first. Principle: one solution per problem (DRY), consistency. The failure is **unverified** at run time; it is read from the exit-code checks.
    - **Improvement:** two steps.
      1. (S) Delete `GitInstallPlan` and use `SystemPackageInstallPlan.For(GitPackageNames, …)`, with `new SystemPackageNames("Git.Git", "git", "git", "git", "git", "git", "git")`. Add the WinGet exit-code check to git and yt-dlp, and the two localized keys to yt-dlp.
      2. (M) Turn the RAR constants of the base into data and derive the three classes from it:

         ```csharp
         internal sealed record SystemToolSpec(
             string Id, string Name, string PurposeKey, string PurposeFallback,
             string UnixTool, string WindowsTool, IReadOnlyList<string> AlsoRequired, // ffprobe
             SystemPackageNames Packages, string DownloadPage, Version Minimum,
             IReadOnlyList<string> VersionArguments, Func<string, string?> ParseVersion);

         public abstract class SystemToolRuntimeDependency(SystemToolSpec spec, /* the existing seams */) : IRuntimeDependency
         ```

         The base becomes `public abstract`, because `public sealed` subclasses cannot derive from an `internal` base. No caller outside `iTerminal.Core` uses these classes, and `InstantiateAllTypesTest` skips abstract types ([InstantiateAllTypesTest.cs:61](../../iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs)). Move `FindOnPath` out of the git class as part of finding 48.
    - **Copies:** 4 classes (3 copies of the base), 1 copied plan record; about 300 lines would go (63 lines of `GitInstallPlan` and about 80 per class). Drifted: yes (exit codes, localization).
    - **Effort:** S for step 1, M for step 2.
    - **Risk of the change:** a changed install plan or exit-code rule. `GitRuntimeDependencyTest` (it asserts on `GitInstallPlan`, which moves to `SystemPackageInstallPlan`), `FfmpegRuntimeDependencyTest`, `YtDlpRuntimeDependencyTest`, `RarRuntimeDependencyTest` and `RuntimeDependencyServiceTest` cover each class through the existing constructor seams.

43. **Seven hand-written process runs beside `IExternalProcessRunner` differ on UTF-8, standard input and cancellation** — **Medium**, kind: duplication, consistency, testability.
    - **Where:**
      - The shared runner: [ExternalProcessRunner.cs:12](../../iTerminal.Core/Services/Processes/ExternalProcessRunner.cs). It drains both pipes, reads UTF-8 (`:143`), kills the process tree on its 30 s timeout, and rethrows when the caller cancels (`:66`). It is injected in 30 files.
      - [GitService.cs:447](../../iTerminal.Core/Services/Git/GitService.cs) (the Git client dialog) is the same method again, down to `GIT_TERMINAL_PROMPT` (`:463`) and the 30 s timeout. It sets no output encoding, and it returns "git command timed out" when the caller cancels (`:482` to `:485`). The git drive beside it uses the shared runner. Because `GitService` builds its process itself, its 13 tests need a real git (`GitServiceTest`).
      - [PackageManagerProcess.cs:20](../../iTerminal.Core/Services/PackageManager/PackageManagerProcess.cs): the same again with a timeout parameter, and the same "timed out" answer for a cancellation (`:50` to `:53`). It has 7 callers (apt, WinGet, yt-dlp).
      - Five version probes with a 5 s timeout: [GitRuntimeDependency.cs:225](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs), [FfmpegRuntimeDependency.cs:255](../../iTerminal.Core/Services/RuntimeDependencies/FfmpegRuntimeDependency.cs), [SystemToolRuntimeDependency.cs:206](../../iTerminal.Core/Services/RuntimeDependencies/SystemToolRuntimeDependency.cs), [PowerShellRuntimeDependency.cs:329](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs), and [OllamaRuntimeDependency.cs:257](../../iTerminal.Core/Services/RuntimeDependencies/OllamaRuntimeDependency.cs), which blocks in `WaitForExit(3000)` with no token. The git and ffmpeg probes redirect standard error but never read it.
      - [DefenderScanService.cs:55](../../iTerminal.Core/Services/Defender/DefenderScanService.cs): the same capture without a timeout.
    - **Problem:** each copy settles the same questions differently.
      - Encoding: on Windows, a non-ASCII commit message or author shown by the Git client is decoded in the console code page, because nothing in the app sets the console to UTF-8.
      - Cancellation: closing the Git client or the package manager mid-command is reported as a timeout.
      - Standard input: the probes redirect it and close it, so a tool that waits for input ends. The shared runner does not redirect it unless a writer is given (`:140`), so its child inherits the terminal that the TUI is reading.

      Which copy a caller gets decides its behaviour. Principle: one solution per problem; testability seams. The mis-decoding and the inherited input are **unverified** at run time.
    - **Improvement:** the shared runner is the right copy, except on standard input, where the probes are right. Three steps:
      1. (S) In `ExternalProcessRunner.Start`, always redirect standard input and close it when no writer is given. Add an options overload with a default body, so the two test fakes (`FakeProcessRunner`, and the one in `PrintServiceTest`) keep compiling:

         ```csharp
         public sealed record ProcessRunOptions(
             string? WorkingDirectory = null,
             TimeSpan? Timeout = null,                              // null: ExternalProcessRunner.Timeout
             IReadOnlyDictionary<string, string>? Environment = null);

         // IExternalProcessRunner (new)
         Task<ProcessOutput> RunAsync(string fileName, IReadOnlyList<string> arguments, ProcessRunOptions options, CancellationToken cancellationToken = default)
             => RunAsync(fileName, arguments, options.WorkingDirectory, cancellationToken);
         ```

      2. (M) `GitService(IExternalProcessRunner runner)`: there are 14 `new GitService()` calls in the tests, and the registration resolves the runner already. `PackageManagerProcess.RunCaptureAsync` becomes a call to the runner with `Timeout = timeout`.
      3. (S) One `internal static Task<string?> ToolVersionProbe.ReadAsync(IExternalProcessRunner runner, string path, IReadOnlyList<string> arguments, TimeSpan timeout, Func<string, string?> parse)` for the five probes. It also removes the blocking Ollama wait.
    - **Copies:** 8 (GitService, PackageManagerProcess, five probes, Defender) in 8 files; about 300 lines would go. Drifted: yes (encoding, cancellation, stdin, stderr drained or not).
    - **Effort:** M in total.
    - **Risk of the change:** the Git client's output changes from the console code page to UTF-8, which is what git writes. A tool that relied on reading the terminal (none was found among the runner's callers) would see end of input. `GitServiceTest` keeps its real-git tests and gains fakes. `GitDriveServiceTest`, `DockerDriveServiceTest`, the package-manager tests and the `*RuntimeDependencyTest` classes (which inject `readVersion`) cover the callers.

44. **Fourteen dialogs carry the same private `Put`, and about 26 private clip helpers sit beside `TextWidth.Fit`** — **Medium**, kind: duplication, consistency.
    - **Where:**
      - `private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)`, which writes one row inside the border. Compared by hash, four variants:
        - byte-identical in 8 dialogs: [AccessControlDialog.cs:890](../../iTerminal.Core/Dialogs/AccessControlDialog.cs), [ContactManagerDialog.cs:647](../../iTerminal.Core/Dialogs/Contacts/ContactManagerDialog.cs), [EnvironmentVariablesDialog.cs:912](../../iTerminal.Core/Dialogs/EnvVars/EnvironmentVariablesDialog.cs), [MailDialog.cs:1143](../../iTerminal.Core/Dialogs/Mail/MailDialog.cs), [PasswordManagerDialog.cs:646](../../iTerminal.Core/Dialogs/PasswordManager/PasswordManagerDialog.cs), [OnlineRadioDialog.cs:870](../../iTerminal.Core/Dialogs/Radio/OnlineRadioDialog.cs), [RemoteConnectionManagerDialog.cs:823](../../iTerminal.Core/Dialogs/Remote/RemoteConnectionManagerDialog.cs) and [ToDoListDialog.cs:667](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs);
        - the same with `string` for `var` in 3: [PrintDialog.cs:624](../../iTerminal.Core/Dialogs/PrintDialog.cs), [ScannerDialog.cs:776](../../iTerminal.Core/Dialogs/Scanning/ScannerDialog.cs) and [StreamsDialog.cs:687](../../iTerminal.Core/Dialogs/StreamsDialog.cs);
        - with an ellipsis in 2: [WorkspaceManagerDialog.cs:736](../../iTerminal.Core/Dialogs/Workspaces/WorkspaceManagerDialog.cs) and [TorrentManagerDialog.cs:706](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs);
        - with a row-bounds check in 1: [CalendarDialog.cs:602](../../iTerminal.Core/Dialogs/Calendar/CalendarDialog.cs) ("a row below the inner area would be drawn on the bottom border").

        All 14 cut by `string.Length`, not by columns.
      - The column-aware helper already exists: [TextWidth.cs:95](../../iConsole/Display/TextWidth.cs) (`Fit`). It is called 4 times. Its sibling for the split-pane dialogs is [SplitPaneDialog.cs:73](../../iTerminal.Core/Dialogs/SplitPaneDialog.cs) (`WriteLine`).
      - Private clip helpers, one or two lines each:
        - `Clip` in 7 dialogs, for example [MailDialog.cs:1158](../../iTerminal.Core/Dialogs/Mail/MailDialog.cs). The copies in [TorrentManagerDialog.cs:721](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs) and [StreamsDialog.cs:704](../../iTerminal.Core/Dialogs/StreamsDialog.cs) have no caller.
        - `Ellipsize` in 4 places, among them [Toast.cs:79](../../iConsole/Controls/Toast.cs) and [SplitPaneDialog.cs:71](../../iTerminal.Core/Dialogs/SplitPaneDialog.cs). Mail and Remote Connections each hold both a `Clip` and an `Ellipsize` that differ.
        - `Fit` in 13 places, for example [ComboBox.cs:294](../../iConsole/Controls/ComboBox.cs), [PopupListView.cs:201](../../iConsole/Controls/PopupListView.cs) and [ImageFileViewerDialog.cs:601](../../iTerminal.Core/Dialogs/Video/ImageFileViewerDialog.cs).
    - **Problem:** each dialog decides again how a row is cut, padded and kept off the border. The fixes so far landed in one copy each: the bounds check in Calendar, the ellipsis in Workspaces and Torrents. A row with CJK text or emoji is cut by characters, so it overflows its column in every one of these dialogs (design review, finding 4). Principle: DRY, consistency with the framework (the column-aware primitive is in `iConsole` and unused).
    - **Improvement:** two additive members in `iConsole`, then one dialog per commit:

      ```csharp
      // iConsole/Display/TextWidth.cs (new)
      public static string Ellipsize(string? text, int columns);   // Fit, with "…" in the last column when cut

      // iConsole/Dialogs/Dialog.cs (new, protected)
      /// <summary>One row inside the border at (x, y) of the inner area, cut or padded to width columns;
      /// a row outside the inner area is not drawn.</summary>
      protected void WriteInnerRow(IBuffer buffer, int x, int y, int width, string? text, Color foreground, Color background, bool ellipsis = false);
      ```

      Each of the 14 `Put` bodies becomes a call to `WriteInnerRow`; `Clip` and `Ellipsize` become `TextWidth.Ellipsize`. Delete the two unused `Clip` copies at once. Both new members are additive. A dialog outside the solution that already has a member of that name gets a hiding warning, not a break (the `dotnet-breaking-changes` concern), so a distinct name is used rather than `Put`.
    - **Copies:** 14 `Put` copies (about 196 lines, down to 14 calls) and about 26 clip helpers in 24 files. Drifted: yes (bounds check, ellipsis).
    - **Effort:** M (S for the two members; S per dialog).
    - **Risk of the change:** a row that was drawn on the border is no longer drawn, and a wide character now takes two columns. The `ScreenBuffer` tests of each dialog (`ContactManagerDialogTest`, `MailDialogTest`, `TorrentManagerDialogTest`, `CalendarDialogTest` and the others) compare the drawn text, and the end-to-end `DialogsTest` opens each dialog.

45. **The contact manager, the environment-variable editor and the radio station list are one dialog written three times** — **Medium**, kind: duplication.
    - **Where:**
      - The same `private enum Mode { Browse, Form, Filter, ConfirmDelete }`: [ContactManagerDialog.cs:53](../../iTerminal.Core/Dialogs/Contacts/ContactManagerDialog.cs), [EnvironmentVariablesDialog.cs:49](../../iTerminal.Core/Dialogs/EnvVars/EnvironmentVariablesDialog.cs) and [OnlineRadioDialog.cs:47](../../iTerminal.Core/Dialogs/Radio/OnlineRadioDialog.cs).
      - The same delete question: `case Mode.ConfirmDelete` at `:153`, `:322` and `:181`.
      - The same filter editor, 53 to 57 lines (`HandleFilterKey` at `:306`, `:508` and `:351`).
      - The same form editor, 72 to 73 lines (`HandleFormKey` at `:231`, `:417` and `:276`). The contact and radio copies are identical; the environment-variable copy adds a locked name field.
      - The same `Fit` and `Put` (finding 44).
    - **Drift:** the environment-variable filter caps typed text at a literal `200` ([EnvironmentVariablesDialog.cs:550](../../iTerminal.Core/Dialogs/EnvVars/EnvironmentVariablesDialog.cs)), not at its `MaxFieldLength` of 4 000. Its paste has no cap at all (`:519`). The contact and radio copies cap both with `MaxFieldLength` (`:352` and `:397`).
    - **Problem:** a change to how a field edits (a new key, Ctrl+Left, wide characters, an undo) is made three times or drifts, as the paste cap already has. The same y/n delete question is hand-written in To-Do ([ToDoListDialog.cs:221](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs)) and Calendar ([CalendarDialog.cs:172](../../iTerminal.Core/Dialogs/Calendar/CalendarDialog.cs)) too. Principle: DRY. This is the code side of design review finding 68 and of the Tui comparison item 72 ("form bound to a model").
    - **Improvement:** extract the line editor first. It is the part that drifted, and it is smaller than a base dialog:

      ```csharp
      // iTerminal.Core/Controls/InlineTextField.cs (uses PasteHelper, which is in iTerminal.Core)
      internal sealed class InlineTextField(int maxLength)
      {
          public string Text { get; set; }
          public bool IsLocked { get; set; }
          /// <summary>Backspace, Ctrl+W, Ctrl+U, paste and printable keys; true when the key was used.</summary>
          public bool HandleKey(KeyEvent keyEvent, Action changed);
      }
      ```

      `HandleFilterKey` keeps only Enter and Esc, and `HandleFormKey` keeps only field navigation and saving. A `RecordListDialog<TRecord>` base for the four modes is a second, separate decision; do it only if a fourth dialog of this shape appears.
    - **Copies:** 3 dialogs; about 250 lines would go (the two key handlers). Drifted: yes (paste and typing caps).
    - **Effort:** M.
    - **Risk of the change:** a key that one dialog handled differently changes. `ContactManagerDialogTest`, `EnvironmentVariablesDialogTest` and `OnlineRadioDialogTest`, and the end-to-end `EnvVarsFooterTest`, type into the fields. Add one `InlineTextFieldTest` for the caps.

46. **Three decoders and three binary checks for "text of unknown encoding" give three different answers** — **Medium**, kind: duplication, consistency.
    - **Where:**
      - Decoding:
        - [TextEncodings.cs:127](../../iConsole/Helper/TextEncodings.cs) reads the byte-order mark, then UTF-8, then the system's ANSI code page (`:160`). The F3 viewer, the CSV, Markdown and HAR viewers and the format readers use it (10 callers).
        - [PlainTextExtractor.cs:66](../../iTerminal.Core/Documents/Extractors/PlainTextExtractor.cs), for the document index, falls back to Latin-1 instead (`:89`).
        - [FileDiffDialog.cs:261](../../iTerminal.Core/Dialogs/FileDiffDialog.cs), for file comparison, also falls back to Latin-1 (`:281`).
      - Binary checks:
        - [FileDiffDialog.cs:252](../../iTerminal.Core/Dialogs/FileDiffDialog.cs) looks for a NUL in 8 192 bytes;
        - [FilePreviewService.cs:330](../../iTerminal.Core/Services/Preview/FilePreviewService.cs) looks for a NUL or more than a tenth control characters in 8 000 bytes;
        - [FileSearch.cs:339](../../iTerminal.Core/Services/Search/FileSearch.cs) looks for a NUL in 8 000 bytes.

        All three repeat the UTF-16 byte-order-mark test.
    - **Problem:** on a Windows machine whose ANSI code page is 1250, a Hungarian text file without a byte-order mark shows "ő" in F3. The comparison shows "õ", and the document index stores "õ", so searching the index for the word finds nothing. Preview can call a file binary that search still searches. One question gets three answers, and the user sees the difference between features. Principle: one solution per problem, consistency with the framework (`iConsole` has the decoder). **Unverified** at run time; the difference is read from the fallbacks.
    - **Improvement:** one decision, then two moves:
      1. Decide the fallback. `TextEncodings` states why it uses ANSI, and the index and the comparison give no reason for Latin-1.
      2. Point `PlainTextExtractor.Decode` and `FileDiffDialog.Decode` at `TextEncodings.Decode(bytes, out _)`. Keep the extractor's partial-UTF-8 trimming by moving it into `TextEncodings`, so the shared decoder gains it.
      3. Add `public static bool LooksBinary(ReadOnlySpan<byte> head)` to `TextEncodings`, with the preview rule (NUL or control characters, the byte-order mark excepted), and call it from the three places.
    - **Copies:** 3 decoders and 3 binary checks in 5 files; about 70 lines would go. Drifted: yes (fallback, control-character rule, sample size).
    - **Effort:** S once the fallback is decided.
    - **Risk of the change:** text already in the document index keeps its old decoding until the file is indexed again, so old and new entries can differ until a rescan. `PlainAndMarkupExtractorTest`, `FileDiffDialogTest`, the preview and search tests, and the `TextEncodings` tests in `iConsole.Test` cover each path.

47. **The status-bar weather calls Open-Meteo itself beside `IForecastProvider`, so the end-to-end suite has to leave the job out** — **Low**, kind: duplication, testability.
    - **Where:**
      - [WeatherJob.cs:12](../../iTerminal.Core/Jobs/WeatherJob.cs): a static `HttpClient` with the default 100 s timeout and no User-Agent. The same forecast endpoint is called at `:91` and parsed at `:122`.
      - [OpenMeteoForecastProvider.cs:43](../../iTerminal.Core/Services/Weather/OpenMeteoForecastProvider.cs): the Weather dialog's client, with a 15 s timeout and a User-Agent. Its `FetchAsync` (`:48`) already returns `CurrentConditions` ([WeatherModels.cs:18](../../iTerminal.Core/Services/Weather/WeatherModels.cs)).
      - [iTerminal.Test/TerminalApp.cs:35](../../iTerminal.Test/TerminalApp.cs) omits `WeatherJob`, while the provider has a sandbox replacement (`:239`).
    - **Problem:** two clients and two parsers for one API have already drifted on timeout and identification. The job has no seam, so the end-to-end suite cannot run it, and the status-bar weather is untested in the app. Principle: DRY, testability seams.
    - **Improvement:** let `WeatherJob` take `IForecastProvider` and read `Forecast.Current`, or add `Task<CurrentConditions> FetchCurrentAsync(SavedPlace, CancellationToken)` to the interface if the full forecast is too heavy for a 15-minute poll. Keep the IP lookup in the job behind its own small interface, so the sandbox can answer both.
    - **Copies:** 2 clients, 2 parsers; about 40 lines would go. Drifted: yes (timeout, User-Agent).
    - **Effort:** S.
    - **Risk of the change:** the status bar's request gets the provider's 15 s timeout. `WeatherJobTest` tests the parser it would lose and needs a fake provider. The job could then be run in the end-to-end suite against `SandboxServices.WeatherForecasts`.

48. **Six PATH searches, and the shared one lives in the git dependency** — **Low**, kind: duplication, consistency.
    - **Where:**
      - [GitRuntimeDependency.cs:198](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs) (`FindOnPath`, with `PATHEXT` and quoted entries) is used by ffmpeg, PowerShell, rar, unrar and yt-dlp, by `YtDlpYouTubeService`, and by the microphone registration ([iTerminalApplicationBuilderExtension.cs:714](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs)).
      - Its siblings:
        - [ShellLaunch.cs:87](../../iTerminal.Core/Services/Shell/ShellLaunch.cs), with no `PATHEXT`, so its callers pass `pwsh.exe` themselves;
        - [ElevationService.cs:275](../../iTerminal.Core/Services/Elevation/ElevationService.cs), which does not strip quotes;
        - [RarArchiveWriter.cs:153](../../iTerminal.Core/Services/Archives/RarArchiveWriter.cs) (`RarTools.Find`) and [SevenZipArchiveWriter.cs:34](../../iTerminal.Core/Services/Archives/SevenZipArchiveWriter.cs);
        - [DriveActionsService.cs:340](../../iTerminal.Core/Services/Drives/DriveActionsService.cs), which returns the bare name, not the path (`:352`).
    - **Problem:** whether a program "is installed" depends on which feature asks. A quoted PATH entry on Windows finds `pwsh` for the shell but not for elevation. A helper used by the speech registration sits in the git dependency class. Principle: DRY, cohesion.
    - **Improvement:** `internal static class ExecutableSearch { public static string? Find(string program); public static string? FindAny(params string[] names); }` in `iTerminal.Core/Helper`, with the git copy's rules (quotes, `PATHEXT`, a malformed entry skipped). The other five call it, and the remaining delegate parameters take `ExecutableSearch.Find`.
    - **Copies:** 6 in 6 files; about 90 lines would go. Drifted: yes (quotes, `PATHEXT`, return value).
    - **Effort:** S.
    - **Risk of the change:** the elevation check starts finding `sudo` in a quoted entry. `ElevationServiceTest`, `ShellLaunchTest` and the archive-writer tests show any change.

49. **Six stream-copy loops with progress beside `StreamCopyHelper`** — **Low**, kind: duplication.
    - **Where:** [StreamCopyHelper.cs:12](../../iTerminal.Core/Helper/StreamCopyHelper.cs) (`CopyWithProgressAsync`, used by the local, memory and archive drives). The same eight-line loop is written out at [HttpFileSystemServiceBase.cs:195](../../iTerminal.Core/Services/Cloud/HttpFileSystemServiceBase.cs), [WebDavFileSystemService.cs:456](../../iTerminal.Core/Services/WebDavFileSystemService.cs), [ShellMtpBackend.cs:194](../../iTerminal.Core/Services/Mtp/ShellMtpBackend.cs) and `:219`, [RecycleBinService.cs:197](../../iTerminal.Core/Services/Recycle/RecycleBinService.cs) and [EvidenceNotebookService.cs:266](../../iTerminal.Core/Services/Evidence/EvidenceNotebookService.cs).
    - **Problem:** the copies report no initial 0 and use a 64 KB buffer where the helper uses 128 KB. A change to progress pacing (design review finding 17 asks for long copies to report through `TaskManager`) has seven places to go. Principle: DRY; the helper exists.
    - **Improvement:** replace each loop with `await StreamCopyHelper.CopyWithProgressAsync(source, destination, progress, cancellationToken, 1 << 16);`. The SMB loop ([SmbFileSystemService.cs:534](../../iTerminal.Core/Services/SmbFileSystemService.cs)) writes in the server's chunk size and stays as it is.
    - **Copies:** 6 in 5 files; about 45 lines would go. Drifted: slightly (the initial report).
    - **Effort:** S.
    - **Risk of the change:** progress gets one more report at 0. The cloud and WebDAV service tests, `ContentModelCopyPipelineTest` and `FileTransferTest` cover the copies.

50. **Sizes and durations are formatted in about 15 private copies beside the two shared helpers** — **Low**, kind: duplication, consistency.
    - **Where:**
      - Sizes: [FileSizeHelper.cs:5](../../iConsole/Helper/FileSizeHelper.cs) (`ToFileSize`, 32 users) formats every value without decimals, because all three branches of `ThreeNonZeroDigits` (`:26`) use `"0"` (1.5 MB shows as "2 MB"). Five dialogs and models wrote their own one-decimal version, with different unit lists and decimal rules:
        - [DiskManagerDialog.cs:479](../../iTerminal.Core/Dialogs/DiskManagerDialog.cs);
        - [DocumentManagerDialog.cs:934](../../iTerminal.Core/Dialogs/Documents/DocumentManagerDialog.cs);
        - [MusicFileViewerDialog.cs:615](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs);
        - [TorrentManagerDialog.cs:587](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs), which uses `0.0`;
        - [WebBrowserModel.cs:1018](../../iTerminal.Core/Model/Web/WebBrowserModel.cs), which stops at MB.

        A one-line `Megabytes` is in four files ([VerifiedDownload.cs:82](../../iTerminal.Core/Services/RuntimeDependencies/VerifiedDownload.cs), `GitHubReleaseFeed.cs`, `NuGetPackageFeed.cs`, `OllamaServer.cs`). [FilePropertiesService.cs:262](../../iTerminal.Core/Services/Properties/FilePropertiesService.cs) is the copy that builds on the shared helper.
      - Durations: [ProgressGroup.cs:295](../../iConsole/Controls/Progress/ProgressGroup.cs) (`FormatDuration`, public) is written again in [MusicFileViewerDialog.cs:603](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs), [VideoFileViewerDialog.cs:554](../../iTerminal.Core/Dialogs/Video/VideoFileViewerDialog.cs), [ContentModelMusicBase.cs:197](../../iTerminal.Core/Model/ContentModels/ContentModelMusicBase.cs), [YouTubeDialog.cs:409](../../iTerminal.Core/Dialogs/YouTube/YouTubeDialog.cs), [FilePreviewService.cs:485](../../iTerminal.Core/Services/Preview/FilePreviewService.cs) and [OnlineRadioDialog.cs:863](../../iTerminal.Core/Dialogs/Radio/OnlineRadioDialog.cs). A zero length shows as `--:--` in the music copies, `0:00` in YouTube and preview, and `00:00` in the radio.
    - **Problem:** the same file has a different size in the panel, the disk manager and the torrent list, and the same track length looks different in three places. The copies exist because the shared size helper rounds away the decimal, and because the duration helper sits on a progress control where nobody looks. Principle: DRY, consistency with the framework.
    - **Improvement:** in `iConsole/Helper`, add `public static string ToFileSize(this long value, int decimals)` beside the existing overload (additive), and move `FormatDuration` to a `DurationText` helper, keeping a forwarding member on `ProgressGroup` because it is public. Add a `zeroAsUnknown` flag for the music rule. Point the copies at them, and remove the dead branch of `ThreeNonZeroDigits`. The panel's size column keeps its whole-number format, which is tested ([FileSizeHelperTest.cs:13](../../iConsole.Test/Helper/FileSizeHelperTest.cs)).
    - **Copies:** 5 size and 4 megabyte formatters, 6 duration formatters, in 14 files; about 90 lines would go. Drifted: yes (units, decimals, zero).
    - **Effort:** S.
    - **Risk of the change:** displayed numbers change by one decimal in some dialogs. `TorrentManagerDialogTest` and `FilePropertiesServiceTest` assert on formatted sizes, and the music viewer tests on times.

51. **Two copies of one test assertion that MSTest already has** — **Low**, kind: duplication (tests).
    - **Where:** [AssertAsync.cs:6](../../iTerminal.Core.Test/Helper/AssertAsync.cs) (25 uses in 10 files) and [AsyncAssert.cs:4](../../iTerminal.Core.Test/Helper/AsyncAssert.cs) (13 uses in 6 files), in the same folder with the same body. Both say "this MSTest version has no" awaitable assertion, but the test projects use MSTest 4.5.3, and its `Assert.ThrowsAsync<T>` and `Assert.ThrowsExactlyAsync<T>` are already called 154 times.
    - **Problem:** two helpers and the framework method do the same job, and the comments state something that is no longer true. Principle: DRY.
    - **Improvement:** replace the 38 calls with `Assert.ThrowsAsync<T>`, which also accepts derived exceptions as both helpers do, and delete both files.
    - **Copies:** 2 helpers, about 45 lines. Drifted: no.
    - **Effort:** S (mechanical).
    - **Risk of the change:** none at run time; the build shows a missed call. **Unverified** that `Assert.ThrowsAsync` in 4.5.3 matches derived types exactly as the helpers do; check one test that expects a subclass.

52. **Sixteen stores read and write a JSON list in `Settings` with the same ten lines** — **Low**, kind: duplication, persistence.
    - **Where:** `Get(key)`, `JsonSerializer.Deserialize`, `catch (JsonException)`, then `Set` and `Write()`, in [ToDoTask.cs:275](../../iTerminal.Core/Model/ToDo/ToDoTask.cs), [Contact.cs:147](../../iTerminal.Core/Model/Contacts/Contact.cs), [Meeting.cs:218](../../iTerminal.Core/Model/Calendar/Meeting.cs), [MailModels.cs:106](../../iTerminal.Core/Model/Mail/MailModels.cs), [RadioStationStore.cs:147](../../iTerminal.Core/Model/Radio/RadioStationStore.cs), [WebBrowserStore.cs:149](../../iTerminal.Core/Services/Web/WebBrowserStore.cs), [WorkspaceService.cs:188](../../iTerminal.Core/Services/Workspaces/WorkspaceService.cs), [WatchStore.cs:168](../../iTerminal.Core/Services/Watch/WatchStore.cs), [TorrentService.cs:461](../../iTerminal.Core/Services/Torrents/TorrentService.cs), [RemoteConnectionService.cs:254](../../iTerminal.Core/Services/Remote/RemoteConnectionService.cs), [StockService.cs:173](../../iTerminal.Core/Services/Stocks/StockService.cs), [PluginManagerService.cs:368](../../iTerminal.Core/Services/Plugins/PluginManagerService.cs), [NetworkProfileService.cs:56](../../iTerminal.Core/Services/NetworkProfileService.cs), [MapStore.cs:102](../../iTerminal.Core/Services/Map/MapStore.cs), [FileAssociationService.cs:153](../../iTerminal.Core/Services/FileAssociations/FileAssociationService.cs) and [ForecastService.cs:232](../../iTerminal.Core/Services/Weather/ForecastService.cs). The last one already has a generic `Load<T>`. Some copies log a damaged value and some do not.
    - **Problem:** each store decides again what a damaged value means, whether to log it, and whether to clear it. Finding 33 shows that where user data is stored decides whether it survives an upgrade, and today that decision is spread over 16 classes. Principle: DRY, one place per decision.
    - **Improvement:** one helper, beside `ISettingsService` rather than on it, so the interface does not change:

      ```csharp
      internal static class SettingsJson
      {
          public static T? Read<T>(ISettingsService settings, string key, JsonSerializerOptions? options = null, ILogger? logger = null);
          public static void Write<T>(ISettingsService settings, string key, T value, JsonSerializerOptions? options = null);
      }
      ```

      Move the stores one at a time, starting with `ForecastService`, whose `Load<T>` is the model. Keep each store's own clean-up of null entries.
    - **Copies:** 16 in 16 files; about 120 lines would go. Drifted: slightly (logging, clearing).
    - **Effort:** S per store, M in total.
    - **Risk of the change:** a store that threw on a damaged value now starts empty. Each store's test (`ToDoTaskTest`, `ContactTest`, `RadioStationStoreTest`, `WorkspaceServiceTest` and the others) covers its load path.

53. **The folder-URL idiom is written 13 times, three times as the join `UrlPath.Child` exists to replace** — **Low**, kind: duplication, consistency.
    - **Where:**
      - "Add a trailing slash unless there is one" as a one-line expression in 10 places, among them [TransferCommand.cs:164](../../iTerminal.Core/Commands/TransferCommand.cs) (the only one that also accepts `\`), `:236` and `:335`, [ContentModelRemoteDrive.cs:73](../../iTerminal.Core/Model/ContentModels/ContentModelRemoteDrive.cs), [MemoryFolder.cs:188](../../iTerminal.Core/Model/ContentModels/MemoryFolder.cs), [FtpFileSystemService.cs:199](../../iTerminal.Core/Services/FtpFileSystemService.cs) and [WebDavFileSystemService.cs:270](../../iTerminal.Core/Services/WebDavFileSystemService.cs).
      - The join that `CLAUDE.md` forbids, `FullUrl.TrimEnd('/') + "/" + name`, at [SmbFileSystemService.cs:179](../../iTerminal.Core/Services/SmbFileSystemService.cs) and `:339`, and [IPhoneFileSystemService.cs:207](../../iTerminal.Core/Services/IPhoneFileSystemService.cs). The helper is [UrlPath.cs:40](../../iTerminal.Core/Helper/UrlPath.cs), with 21 callers.
    - **Problem:** the three joins are correct today only because an SMB share and an iPhone always have a segment after the scheme. The rule exists because the same join broke `memdrive://`. The trailing-slash copies disagree on `\`. Principle: consistency with the framework; one way to build a child URL.
    - **Improvement:** add `public static Url AsFolder(Url url)` to `UrlPath`, with the `TransferCommand` rule for both separators, and replace the 10 expressions. Replace the three joins with `new Url(UrlPath.Child(url.FullUrl, name, isFolder))`.
    - **Copies:** 13 in 10 files; one line each. Drifted: yes (the `\` case).
    - **Effort:** S.
    - **Risk of the change:** a Windows path ending in `\` gets no extra `/`. `UrlPathTest`, `ContentModelSmbDriveTest`, the memory-drive tests and `FileTransferTest` cover the paths.

**Candidates checked and not reported**

- The 88 `ShowDialogCommand<T>` subclasses (most are 13 or 14 lines) differ in key, title, description and `CanExecute`, and the base class already holds the shared part. Their construction cost is finding 32.
- The remote and cloud drive models are already factored: each subclass of `ContentModelRemoteDrive` is 32 to 64 lines and only resolves credentials, and the three cloud services share `HttpFileSystemServiceBase`, including its throttling retry ([HttpFileSystemServiceBase.cs:85](../../iTerminal.Core/Services/Cloud/HttpFileSystemServiceBase.cs)).
- The two `ProgressStream` classes ([ProgressStream.cs:7](../../iTerminal.Core/Helper/ProgressStream.cs) and the private one in `WebDavFileSystemService`) look alike but count writes and reads respectively; merging them would join two things that change for different reasons.
- The ffmpeg processes in `iConsole.Media.Audio` and `iConsole.Media.Video` stream for the life of a playback. They are not run-to-completion calls and are outside finding 43.
- The three file-name sanitisers ([EvidenceNotebookService.cs:427](../../iTerminal.Core/Services/Evidence/EvidenceNotebookService.cs), [WebBrowserModel.cs:990](../../iTerminal.Core/Model/Web/WebBrowserModel.cs), [RenderingDumpHelper.cs:199](../../iConsole/Helper/RenderingDumpHelper.cs)) differ in what they keep. They are short, and the web copy's caller handles the empty result. Use the evidence copy if a fourth is ever needed.
- The six `.resx` files and their checklist are guarded by `LocalizationResourcesTest`; the URL-scheme checklist is finding 37.

**What works well**

- `ContentModelRemoteDrive` and `ContentModelAsyncDrive` hold the listing, caching and stale-result logic once for 13 drive models, so the per-drive classes stay small.
- `IExternalProcessRunner` and `ProcessOutput` give the drives one process seam, with arguments passed through `ArgumentList`, and 30 files use it. Finding 43 extends it rather than adding another.
- `SplitPaneDialog` gives 16 dialogs one header, footer and status row. It is the model for the inner-row helper of finding 44.
- The AI panel tools of this branch (`AiPanelTools.MorePanelOperations.cs`) call `FilePropertiesService`, `ChecksumService`, `FileSearch` and the content-model service instead of copying them.
- `ArchivePaths.Inside` (finding 16, resolved) and `UrlPath.Child` show the pattern that works here: one helper, with a comment that names the bug it prevents.

**Research cross-reference**

- Design review, finding 4 (wide characters counted as one column): finding 44 fixes it for 14 dialogs at once, through `TextWidth.Fit`.
- Design review, finding 68 (hand-painted tables), and the Tui comparison item 72 (a form bound to a model): finding 45 is the first step. Finding 19 (the worker-to-UI hand-off) is the same problem on the threading side and is still open.
- Earlier findings of this document that are duplications: finding 28 (the WinGet hint built in three consumers), finding 35 (three lists of dialogs) and finding 37 (the URL scheme in six places). Finding 30 (SSH quoting) can reuse `ShellLaunch.QuotePosix` ([ShellLaunch.cs:85](../../iTerminal.Core/Services/Shell/ShellLaunch.cs)) instead of adding a third quoting function.
- Profiling overview: no item measures any of these paths, so no finding here claims a speed gain.

**Suggested order**

1. Quick wins, S, no dependencies: finding 51 (test helpers), finding 42 step 1 (one install plan and the WinGet rule), finding 48 (one PATH search), finding 49 (copy loops), finding 53 (folder URLs) and finding 47 (weather).
2. Finding 43, step 1 then 2 then 3. Do it before finding 42 step 2, because the version probes move to the runner and the base class then takes the probe as data.
3. Finding 44 (the inner-row helper and `TextWidth.Ellipsize`), then finding 45 (the line editor), because the three dialogs of 45 use the `Put` that 44 replaces.
4. Finding 46 after a maintainer decision on the fallback encoding, then findings 50 and 52, one store or dialog at a time when it is touched anyway.

## Rendering pipeline

Reviewed on 2026-11-22, scope "Rendering pipeline". No review had covered this scope; the caller asked for a full review, and the whole-repository pass of 2026-11-21 had already refreshed the other scopes a day earlier, so this run takes the one scope never reviewed. Branch `docs/architecture-review-rendering-pipeline`, cut from `main` at `b9b81269`. Read: `CLAUDE.md`, `ui-framework.md`, `ui-framework/rendering-pipeline.md`, the profiling and design-review overviews; in the source `ControlRendererJob`, `ConsoleBufferRendererJob`, `Job`, `Desktop`, `IDesktop`, `Buffer`, `BufferLayer`, `IBuffer`, `OffscreenBuffer`, `RenderingDumpHelper`, `Cursor`, `Position`, the render and invalidation members of `Control`, `IConsoleDriver` and the two drivers' frame methods, `TerminalScreen.OnWindowSizeChanged`, the rendering registrations in `iConsoleApplicationBuilderExtension`, and the names of the rendering tests in `iConsole.Test/Jobs` and `iConsole.Test/Buffers`. Run: `dotnet list iTerminal.slnx package` with `--vulnerable --include-transitive`, `--outdated` and `--deprecated` (read-only, nuget.org reachable), to check that the "Dependencies and packages" section still holds: no advisory, `OpenMcdf` 3.4.0 the only update (finding 25), `imobiledevice-net` still deprecated (finding 4); that section is not rewritten. Not done: no build, no test run, no run-time check, no render dump, no resize or clock change tried on a running app.

**Summary**

- The two render jobs split the work well (compose on one thread, diff and write on the other), and the diff writer is careful: byte compare per row, runs of changed cells per write, synchronized output, Sixel under the same lock.
- The largest structural risk: on a terminal resize the writer thread lays the control tree out and renders the whole screen, while the control renderer draws into the same layers without the lock (finding 54). The lock rule in `CLAUDE.md` is kept on the resize side only.
- The writer loop times its resize check with the wall clock, so after the clock goes back an hour, window resizes are not seen for an hour (finding 55).
- Nothing tells the loops that something changed. Four loops poll, and two independent one-second fallbacks repaint the whole screen (finding 56, the structure behind profiling item 1).
- An exception in `Draw` is swallowed in five places under four policies, and none reaches the log in a release build (finding 57).

**Findings**

54. **On a resize the writer thread lays out and renders the control tree, beside the control renderer, and `Buffer.SyncLock` does not cover the writes** — **Medium**, kind: concurrency.
    - **Where:** [ConsoleBufferRendererJob.cs:212](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) (`DetectWindowSizeChanges`, on the writer thread) resizes the buffer under the lock (line 228) and then raises `WindowSizeChanged` (line 244). [Desktop.cs:269](../../iConsole/Desktops/Desktop.cs) handles it: `Screens.First().OnWindowSizeChanged`, which in [TerminalScreen.cs:180](../../iTerminal.Core/Screens/TerminalScreen.cs) sets `ViewSize` and runs the whole layout, then `RenderInitialState` ([Desktop.cs:275](../../iConsole/Desktops/Desktop.cs)), which calls `Buffer.UpdateLayers` and `screen.Render(Buffer)` for the whole tree. At the same time `ControlRendererJob` calls `UpdateLayers` ([ControlRendererJob.cs:80](../../iConsole/Jobs/ControlRendererJob.cs)) and `control.Render` (line 100) outside the lock; it takes the lock only around `Merge` (line 130) and the layer wipe (line 180). `Buffer.Write` resizes the layer it writes to without the lock ([Buffer.cs:140](../../iConsole/Buffers/Buffer.cs)), and `BufferLayer.Resize` replaces `Size` and `Content` in two steps ([BufferLayer.cs:88](../../iConsole/Buffers/BufferLayer.cs)). [rendering-pipeline.md:13](../architecture/ui-framework/rendering-pipeline.md) says the lock "guards the layer array", which `UpdateLayers` changes without it.
    - **Problem:** two threads draw into the same layers and one of them moves controls while the other draws them. The rule "don't mutate the control tree from a worker thread" is broken by the framework itself, and the code knows it: the comment at [ConsoleBufferRendererJob.cs:237](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) says the layout handler can throw because "the tree can change under it", and retries it up to five times. A user who resizes the window can see a frame mixed from the old and new layout until the next full repaint, and an `IndexOutOfRangeException` in a write lands in an empty `catch` (finding 57). Principles: ownership (one thread owns the tree), explicit contracts.
    - **Improvement:** keep the writer thread to "measure and report". It records the new size in `_layoutPending` (already there) and stops; `ControlRendererJob` applies it at the start of its next pass, on the thread that owns the tree:

      ```csharp
      // IDesktop (iConsole), called by ControlRendererJob at the top of Render()
      void ApplyPendingResize();   // Buffer.Resize under SyncLock, screen.OnWindowSizeChanged, then RepaintFromScratch
      ```

      `ConsoleBufferRendererJob` keeps reading `MergedLayer` under the lock and keeps its own `LastBuffer` resize (line 285). Then correct the sentence on the architecture page: the lock guards the merged layer and the layer set against the writer; drawing is single-threaded by design.
    - **Effort:** M.
    - **Risk of the change:** a resize that is applied one pass later (about a millisecond). The resize tests in `ConsoleBufferRendererJobRunTest` and `DesktopTest`, which drive `WindowSizeChanged` directly, need to move to the new entry point; the end-to-end suite resizes the virtual terminal.
    - **unverified:** no torn frame or exception was observed; the race is read from the code.

55. **The writer loop uses the wall clock, so after the clock goes back the resize check stops** — **Medium**, kind: consistency.
    - **Where:** [ConsoleBufferRendererJob.cs:120](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) and line 132 take `DateTime.Now`; `DetectWindowSizeChanges` returns early while `now - _lastWindowSizeCheck < 100 ms` (line 214). This is the only place that notices a resize (`RefreshWindowSize()` is called from here and once in `Desktop.RunAsync`). The sibling job uses `Environment.TickCount64` ([ControlRendererJob.cs:22](../../iConsole/Jobs/ControlRendererJob.cs)), and `MainLoopWatchDog` uses `ITimeProvider` ([ITimeProvider.cs](../../iConsole/Services/ITimeProvider.cs)).
    - **Problem:** `DateTime.Now` is local time. When daylight saving ends (or the clock is set back), `now - _lastWindowSizeCheck` is negative for an hour, so for that hour a resized window keeps its old layout and the one-second fallback repaint (line 133) stops too. Lending the terminal (Ctrl+O) resets the check (line 100), which hides the fault for a user who happens to do that. Principle: consistency with the framework (two clocks in two sibling loops).
    - **Improvement:** use `Environment.TickCount64` for `lastRedraw`, `_lastWindowSizeCheck` and `_lastStatisticsDate`, as `ControlRendererJob` does, or take `ITimeProvider` through the constructor (it is registered already) so a test can move the clock back. `_lastWindowSizeCheck = DateTime.MinValue` becomes `long.MinValue / 2` or a "check now" flag.
    - **Effort:** S.
    - **Risk of the change:** none beyond the timing tests in `RendererJobRedrawTimingTest` and `ConsoleBufferRendererJobRunTest`; with `ITimeProvider` a new test can set the clock back and assert that a resize is still seen.
    - **unverified:** not reproduced by changing the clock of a running app.

56. **No change signal: four loops poll, and two independent one-second fallbacks repaint the whole screen** — **Medium**, kind: performance, consistency.
    - **Where:** profiling item 1 (measured: 25 % of a core and 6.9 kB/s of output at idle). `Thread.Sleep(1)` in [ControlRendererJob.cs:36](../../iConsole/Jobs/ControlRendererJob.cs), [ConsoleBufferRendererJob.cs:155](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) and [KeyboardEventPublisherJob.cs:45](../../iConsole/Jobs/KeyboardEventPublisherJob.cs); `Thread.Sleep(10)` on a thread-pool thread for the whole life of the app in [Desktop.cs:204](../../iConsole/Desktops/Desktop.cs). The fallbacks: `FullRedrawFallbackInterval` in [ControlRendererJob.cs:10](../../iConsole/Jobs/ControlRendererJob.cs) marks the whole tree changed, and the one in [ConsoleBufferRendererJob.cs:19](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) clears `LastBuffer` (line 190), so every cell is written again. The Sixel code has to work around the second one ([ConsoleBufferRendererJob.cs:416](../../iConsole/Jobs/ConsoleBufferRendererJob.cs)). On Windows the 1 ms sleeps need the machine-wide timer resolution raised for the life of the app ([Desktop.cs:72](../../iConsole/Desktops/Desktop.cs)). The tree version that the renderer compares is one process-wide static ([Control.cs:35](../../iConsole/Controls/Control.cs)). The tests that pin the fallback are named `TriggersRedrawEvery500ms` ([RendererJobRedrawTimingTest.cs:12](../../iConsole.Test/Jobs/RendererJobRedrawTimingTest.cs), line 34) for an interval of one second.
    - **Problem:** the pipeline does not trust its own change tracking, so it pays for it twice: every millisecond on three threads, and a full repaint once a second, which over SSH is a frame of traffic per second for nothing. The same "has anything changed" question is answered three ways (`HasStateChanged`, `TreeVersion`, the fallback timers). The whole-repository section gave the decision (one signal owned by `Desktop`); this finding turns it into steps. Principle: performance structure, consistency.
    - **Improvement:** add one small primitive in `iConsole` and move the loops onto it one at a time:

      ```csharp
      // iConsole/Jobs/RenderSignal.cs, one per Desktop
      public sealed class RenderSignal
      {
          public void Set();                                         // any thread
          public bool Wait(TimeSpan timeout, CancellationToken token); // the loop's thread
      }
      ```

      Step 1 (S): `Control.StateHasChanged`, `InvalidateTree` and `Refresh` set it; `ControlRendererJob` waits on it with a timeout of one second instead of `Sleep(1)`, and keeps `OnIdle` on the timeout. Step 2 (S): `Buffer.Merge` sets a second signal that `ConsoleBufferRendererJob` waits on (with its 100 ms resize check as the timeout). Step 3: lengthen both fallbacks (the profiling item suggests 30 s), drop the Sixel workaround if it is no longer needed, rename the two tests. Step 4: replace the `Desktop.RunAsync` loop with `await Task.Delay(Timeout.Infinite, token)`, and drop `TimerResolution` once no loop sleeps 1 ms (the key reader has to be on its own signal first; the Posix publisher also sleeps 1 ms). Step 5, after finding 54: a tree version per root instead of the static.
    - **Effort:** M overall; steps 1, 2 and 4 are S each.
    - **Risk of the change:** a path that changes the screen without setting the signal shows the change only at the fallback. `ControlRendererJobTest`, `ConsoleBufferRendererJobRunTest` and the end-to-end suite (which waits on drawn text) catch a missed wake-up; keep the fallback at one second until the suite is green with the signal.

57. **A failing `Draw` is swallowed in five places under four policies, and none reaches the log** — **Medium**, kind: consistency, testability.
    - **Where:** [ControlRendererJob.cs:40](../../iConsole/Jobs/ControlRendererJob.cs) (empty `catch`); [Control.cs:339](../../iConsole/Controls/Control.cs) (`Debug.WriteLine`, compiled out of a release build; the call uses the `(message, category)` overload, so the text is never formatted); [Control.cs:394](../../iConsole/Controls/Control.cs) (written to the render dump, which is off unless `ITERMINAL_RENDER_DUMPS` is set); [ConsoleBufferRendererJob.cs:369](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) (`Debug.WriteLine`); [Job.cs:23](../../iConsole/Jobs/Job.cs) (empty `catch` around the whole job). The only diagnostic that reaches Serilog from the render path is the stall report: `MainLoopWatchDog` raises `Stalled` and `Recovered`, and [MainLoopWatchDogLoggingHostedService.cs](../../iTerminal.Core/Services/Diagnostics/MainLoopWatchDogLoggingHostedService.cs) logs them.
    - **Problem:** a control whose `Draw` throws keeps `HasStateChanged` set, so `GetControlsToRender` picks it up again and it throws on every pass, about a thousand times a second, while its rectangle shows whatever was there before. In a release build nothing is written anywhere, so a user report "this panel stopped updating" has no trace in the log. Principle: explicit contracts (error behaviour), consistency.
    - **Improvement:** one reporting path in `iConsole`, logged by the app the same way as the stalls:

      ```csharp
      // iConsole/Diagnostics/MainLoopWatchDog.cs (or a separate RenderErrors class, registered beside it)
      public event EventHandler<RenderErrorEventArgs>? RenderFailed;   // raised once per control type until it draws again
      public void ReportRenderError(IControl control, Exception exception);
      ```

      `Control.Render`, `Control.Draw` and both render jobs call it (keeping the dump line where there is one), and `MainLoopWatchDogLoggingHostedService` writes it with Serilog. `Control` reaches it the way it reaches `Dump` today, through the property the desktop sets.
    - **Effort:** S to M.
    - **Risk of the change:** a log flood if the once-per-type rule is wrong; a unit test that makes a control throw twice and asserts one log entry covers it. `ControlRendererJobTest` covers the loop.

58. **`IBuffer` is the drawing surface and the compositor in one interface, so `OffscreenBuffer` stubs half of it** — **Low**, kind: interface.
    - **Where:** [IBuffer.cs:6](../../iConsole/Buffers/IBuffer.cs) has 11 members. Controls use `Size`, `Cursor`, `Write` and `SetCursorPosition`; `SyncLock`, `Layers`, `MergedLayer`, `Merge`, `UpdateLayers` and `Resize` are used only by the two jobs, `Desktop` and `Buffer` itself, plus `ScreenRecorder.Capture` ([ScreenRecorder.cs:37](../../iConsole/Diagnostics/ScreenRecorder.cs)), which reads `MergedLayer`. `OffscreenBuffer` (the `ScrollView` target) implements the compositor half as stubs: an empty `Layers`, a `MergedLayer` that allocates a new blank layer on every read, an empty `Merge` and an `UpdateLayers` that returns nothing ([OffscreenBuffer.cs:32](../../iConsole/Buffers/OffscreenBuffer.cs), lines 34, 96 and 100). `IBuffer` appears on 386 lines in 205 files of the three product projects, nearly all of them `Draw(IBuffer buffer)`.
    - **Problem:** a new offscreen or recording target has to invent answers for members it has no meaning for, and a control can reach `Layers` or `Merge` from `Draw`, which would break the single-merge rule of the control renderer. Principle: interface segregation.
    - **Improvement:** split the interface; `Draw(IBuffer)` keeps its signature.

      ```csharp
      public interface IBuffer            // what a control draws on
      {
          Size Size { get; }
          Cursor Cursor { get; set; }
          void Write(int zOrder, Position position, Size size, ConxelString conxelString);
          void SetCursorPosition(Position position, bool isVisible);
          void Clear();
      }

      public interface ICompositeBuffer : IBuffer   // what the desktop and the render jobs compose
      {
          Lock SyncLock { get; }
          ConcurrentList<BufferLayer> Layers { get; }
          BufferLayer MergedLayer { get; }
          void Resize(Size newSize);
          void Merge();
          IEnumerable<int> UpdateLayers(int maxZOrder);
      }
      ```

      `Buffer`, `MockBuffer` and `ScreenBuffer` implement `ICompositeBuffer`; `OffscreenBuffer` only `IBuffer`. `IDesktop.Buffer`, `ConsoleBufferRendererJob.Buffer` and `ScreenRecorder.Capture` take `ICompositeBuffer`. Register `Buffer` once and forward both interfaces to it, so `InstantiateAllTypesTest` still resolves every constructor.
    - **Effort:** M (mechanical; most of the work is in tests that call `Merge` through an `IBuffer`, to be counted with `Grep` first).
    - **Risk of the change:** breaking for any project outside the solution that calls a compositor member through `IBuffer` or implements `IDesktop` (none is known in this repository; **unverified** for the other users of iConsole). The compiler finds every caller inside the solution.

59. **Two `RenderingDumpHelper` instances, and the second deletes what the first wrote** — **Low**, kind: lifetime, cohesion.
    - **Where:** `IRenderingDumpHelper` is not registered (no line in [iConsoleApplicationBuilderExtension.cs:85](../../iConsole/Application/iConsoleApplicationBuilderExtension.cs) or elsewhere), so `Desktop` ([Desktop.cs:40](../../iConsole/Desktops/Desktop.cs)) and `ConsoleBufferRendererJob` ([ConsoleBufferRendererJob.cs:81](../../iConsole/Jobs/ConsoleBufferRendererJob.cs)) each build their own. `Desktop.RunAsync` starts the session (line 151) and writes the initial-state dumps (line 167); then the render job starts, and its `OnStart` (line 109) starts a second session, which deletes every file in the folder ([RenderingDumpHelper.cs:54](../../iConsole/Helper/RenderingDumpHelper.cs)). The folder is the same for every process ([RenderingDumpHelper.cs:43](../../iConsole/Helper/RenderingDumpHelper.cs)). The dump text is built in two classes: five public static methods, about 150 lines, sit in [ConsoleBufferRendererJob.cs:610](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) to line 762 beside `RenderingDumpHelper`, and `MaxDumpHeight` and `MaxDumpWidth` are declared in both (line 22 and [RenderingDumpHelper.cs:16](../../iConsole/Helper/RenderingDumpHelper.cs)).
    - **Problem:** the developer who sets `ITERMINAL_RENDER_DUMPS=1` to debug a blank start-up screen does not get the `buffer-initial-state` files that exist for that case, and two instances append to one `session` trace under two different locks. The render job is a third of a frame-dump formatter. Principle: ownership (one instance per session), single responsibility.
    - **Improvement:** register `services.AddSingleton<IRenderingDumpHelper, RenderingDumpHelper>()` in `AddiConsoleCore` (both constructors already take it as an optional argument), and move the five static methods and the two constants into `RenderingDumpHelper`. Callers to move: [Desktop.cs:302](../../iConsole/Desktops/Desktop.cs) and nine calls in `iConsole.Test/Jobs/RenderingJobsTest.cs`.
    - **Copies:** 2 helper instances; 2 pairs of constants; about 150 lines move, none are deleted. Not drifted.
    - **Effort:** S.
    - **Risk of the change:** `RenderingJobsTest` (the dump formatting); a dump run by hand confirms that the initial-state files survive.
    - **unverified:** read from the start-up order, not from a dump folder.

60. **The desktop keeps a list of screens, but only the first is ever used** — **Low**, kind: interface.
    - **Where:** `Screens` is a `ConcurrentList<IScreen>` on `IDesktop` ([IDesktop.cs:16](../../iConsole/Desktops/IDesktop.cs)), filled only in the constructor ([Desktop.cs:60](../../iConsole/Desktops/Desktop.cs)). Key routing takes `screens[0]` (line 336), resize takes `Screens.First()` (line 271), and three prompt services do the same. `ControlRendererJob` loops over all screens ([ControlRendererJob.cs:75](../../iConsole/Jobs/ControlRendererJob.cs)) and merges each into the one `MergedLayer`, so a second screen would overwrite the first.
    - **Problem:** the type promises several screens and the pipeline supports one; a developer who adds a second gets a screen that is drawn and then overdrawn. Principle: explicit contracts.
    - **Improvement:** state the single-screen contract: add `IScreen Screen { get; }` to `IDesktop`, move the `First()` callers to it, and keep `Screens` (used by the screen manager dialog) with an XML comment that it holds one entry. Removing `Screens` is a breaking change and is not needed.
    - **Effort:** S.
    - **Risk of the change:** none in behaviour; the compiler finds the callers.

**What works well**

- The split of the two jobs: controls draw into per-Z-order layers on one thread, and the writer only reads the merged layer under `Buffer.SyncLock`, so the terminal is never written from a key handler.
- The diff writer: a SIMD byte compare per row, one write per run of changed cells, synchronized output bracketing each frame, and `LastBuffer` marked unknown (not blank) after a resize or a lend, so a matching blank never hides a cell ([ConsoleBufferRendererJob.cs:293](../../iConsole/Jobs/ConsoleBufferRendererJob.cs), line 97).
- Compositing by the cells each row actually wrote (`BufferLayer` row extents), with a comment that names the two bugs it fixed. `RepairWideCharacters` runs once on the merged layer, the one place it can see both halves.
- The Sixel overlay is drawn only while the cells under it are still the blank the control painted, so a toast or a menu over it never leaves pixels behind.
- `IConsoleDriver` is small, and the frame, graphics and cursor-shape members have default bodies, so a test driver needs only the basics. The loops that write to the terminal bracket each pass with `ConsoleOwnership`.
- The second start-up wave (background jobs after `FirstFrameRendered`) and the per-pass reuse of the collections in both jobs.

**Research cross-reference**

- Profiling overview, item 1: finding 56 is its structural side and gives the steps; the measured cost is the profiling item's. Ranked Medium here because it costs CPU and traffic, not data; it is still the first change to make in this scope.
- Whole repository section, research cross-reference (the per-root change signal and the static `Control._treeVersion`): finding 56 step 5 is that decision, and it waits for finding 54 so that one thread owns the tree.
- Design review, finding 25 (folder selector rows cut once at a width and not again after a resize): a control-level resize gap; finding 54 does not fix it, but after 54 a resize reaches every control on the render thread, where a re-cut can run safely.
- Console features overview: synchronized output and diff rendering are recorded as built; this review agrees.

**Suggested order**

1. Quick wins, S: finding 55 (one clock), finding 59 (one dump helper), finding 60 (single-screen contract).
2. Finding 57 (render errors reach the log) before finding 54, so that the change of thread in 54 has a log that shows what breaks.
3. Finding 54 (resize on the render thread).
4. Finding 56, steps 1 and 2, then 3 and 4; step 5 after 54.
5. Finding 58 when the buffer types are touched anyway; it does not depend on the others.

## Resolved

- **Finding 10 (`iTerminal.Core` references the Windows input plug-in)** — resolved in #805 (`8724e369`): the `ProjectReference` to `iConsole.Inputs.Windows` is removed from `iTerminal.Core.csproj`. Nothing in `iTerminal.Core` used it. Verification: `dotnet build iTerminal.slnx` succeeds (0 errors, 12 warnings); the test suites and the end-to-end suite were not run.
- **Finding 24 (`Svg.Skia` repeated in `iTerminal.Core`)** — resolved in #805 (`8724e369`), with the first option. The `Svg.Skia` line is removed from `iTerminal.Core.csproj`, with a comment that SkiaSharp and Svg.Skia arrive through `iConsole.Media.Video` and `iConsole.Formats`, which own their versions and natives. The version stays 5.2.3 in `iConsole.Formats`. Verification: the solution build succeeds; the decoder tests were not run.
- **Finding 26 (unused `ZXing.Net` in `iConsole.Formats.Test`)** — resolved in #805 (`8724e369`): the `PackageReference` is removed from `iConsole.Formats.Test.csproj`. Verification: the solution build succeeds; the tests were not run.
- **Finding 16 (one "stays inside the folder" check for archives)** — resolved in #803 (`8cedca9a`). `iTerminal.Core/Helper/ArchivePaths.Inside` is the single check, and `ArchiveExtractionService` (the former `SafePath`), `ReleaseArchive` and `UpdateService` (which still throws on a refused entry) all call it; both libraries are kept.
  - The rule is the `ArchiveExtractionService` rule: backslashes are separators, leading slashes are dropped (the entry stays inside), names with a colon are refused, and case is ignored on Windows only.
  - Behaviour changes on the two BCL extractors: a case-only folder difference on Linux now counts as a different folder (the update check was looser), and a colon in a release entry name is refused on Linux too.
  - The escape cases are covered by `ArchiveExtractionServiceTest.OnlyPathsInsideTheTargetAreSafe`, which now calls the helper.
  - Run on Linux: the `iTerminal.Core.Test` tests whose names match `Archive`, `ReleaseArchive` or `UpdateService` (256 passed, 0 failed). Not run: the Windows path (the case-insensitive branch), which the Windows CI job covers.
- **Finding 23 (the host is never disposed, and `--update` ignores Ctrl+C)** — resolved in #804 (`d158ad1c`; the same change as `38ed0b4b` on `fix-application-lifetime`).
  - `iConsoleApplication` implements `IAsyncDisposable` and disposes the host without stopping it.
  - `Program.cs` disposes the app on the `--update` path and on the run path, after `RunAsync` has stopped the host and before `PendingRelaunch` can replace the process.
  - `--update` takes Ctrl+C through `Console.CancelKeyPress`: the first press cancels the check or the download, a second one ends the process. `UpdateCommandLine` reports a cancelled run as a failure (exit code 1, "The update was cancelled") instead of "up to date".
  - Verification: `iConsole.Test` (5073) and `iTerminal.Core.Test` (6341) pass; the end-to-end suite and a real Ctrl+C during a download were not run.
- **Finding 2 (third-party notices in the releases)** — resolved in #789 (`a8cd3721`). `iTerminal/Build/ThirdPartyNotices.targets` runs after `Publish` and calls `iTerminal.ThirdPartyNotices`. That tool writes `THIRD-PARTY-NOTICES.txt` for the runtime being published, with packages from `project.assets.json`, licences from the nuspecs, and `Licenses/Overrides.txt` for the two packages that state none, and copies the project `LICENSE` beside it. Every installable form publishes first, so the MSI, the deb, the rpm, the tarballs and the portable archives carry both files, and the RPM licence field is `MIT`. What remains around it is finding 40 (the generator outside the solution, warnings only) and the `Package.wxs` comment in finding 15.
- **Finding 22 (launch-option contract and mistyped switches)** — resolved in #788.
  - `LaunchOptions.UnknownSwitches` lists the unmatched tokens that start with `-`; `Program.cs` writes one warning line per switch to standard error and still starts the interface.
  - `ElevationService.SelfCommand` drops `--wait-for-parent` together with its value.
  - The `LaunchOptions` XML comment and `build-and-packaging.md` now give the true reason.
  - Left as it is, and documented on that page: `iConsoleApplication.CreateBuilder(string[] args)` does not read `args`.
  - Not checked at run time: whether the warning stays visible once the interface takes over the screen (**unverified**).
- **Finding 21 (namespaces of `iConsole.Formats`)** — resolved in #787: the format implementations moved from `iConsole.*` to `iConsole.Formats.*`, and their tests to `iConsole.Formats.Test.*`. The contracts that `iConsole` declares keep their namespaces, so a `using iConsole.Services;` now names the assembly that holds it.
- **Finding 17 (format implementations in two homes)** — resolved in #781: the Parquet, HDF5 and SQLite readers, the executable analyzer with its PE, ELF and Mach-O parsers, and their packages moved from `iTerminal.Core` to `iConsole.Formats`. Their tests moved to `iConsole.Formats.Test`, and their registrations into `AddFileFormats`. #802 later moved the remaining readers and parsers.
- **Finding 1 (`Svg.Skia` at two major versions)** — resolved by finding 8: `Svg.Skia` is 5.2.3 everywhere, and since #805 it is in one project file.
- **Finding 8 (`iConsole` carries the file-format packages)** — resolved in #779: the format readers, renderers, converter and their packages moved to `iConsole.Formats`, and their unit tests to `iConsole.Formats.Test`. `iConsole` keeps only the interfaces its dialogs use, and the readers of formats with no third-party package (Csv, Dbf, Har, Json, Xml, Markdown and the XPS document model).
- **Finding 11 (patch and minor updates waiting)** — resolved in #777: `coverlet.collector` 10.1.1, `Microsoft.Extensions.AI` and `.OpenAI` 11.0.1, `SSH.NET` 2026.0.1, `SkiaSharp` and its Linux natives 4.157.0, and `Tomlyn` 2.11.1. `OpenMcdf` stays at 3.3.0 on purpose (finding 25).
- **Finding 12 (Playwright 1.65 makes every user download the driver again)** — resolved in #777: both references are on 1.65.1 together.
- **Finding 13 (YamlDotNet two major versions behind)** — resolved in #777: 18.1.0, now in `iConsole.Formats`.
