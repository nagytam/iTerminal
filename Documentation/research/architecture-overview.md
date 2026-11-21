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
