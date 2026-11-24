# Architecture overview

This document holds the architect reviews of iTerminal, one section per scope. A later review of the same scope replaces its section. Finding numbers are unique across the whole document and are never reused; a finding that is fixed moves to the **Resolved** list at the end with its number.

## Whole repository

Reviewed on 2026-11-24, scope "Whole repository" (a full review, asked for by the caller), on branch `fix/build-warnings-3` at commit `c5c693b3`. No review branch was cut, because the branch is not `main`. This pass replaces the whole-repository pass of 2026-11-21 (at `a96d338c`) and re-checks every section of this document against the 49 commits since `b9b81269` (#817): the Tools submenus (#819), the language server client removed (#821), the widgets moved into `iTerminal.Widgets` (#822), the file-security code into `iTerminal.Security` (#823), the extension contracts (#824), the SSH quoting and listing gates (#831), the remote connection form on `FormPanel` (#835), the script launch changes (#838, #839) and the warning fixes of #840, #841 and the 25 commits on this branch. Sections refreshed in full: "Layering and dependencies", "Composition and DI" and "Dependencies and packages". Section added: "Interfaces and seams". The other sections were re-checked finding by finding; their line references were moved to `c5c693b3` with a script that finds each cited line again by its text, and the moves it could not place were checked by hand.

Read: `CLAUDE.md`; `ui-framework.md`, `panels-and-drives.md`, `dialogs.md`, `file-operations.md`, `testing.md`, `runtime-dependencies.md` and `build-and-packaging.md` where the findings cite them; the profiling, design-review and security overviews; every project file, `Directory.Build.props` and `iTerminal.slnx`. In the source, beyond what each section lists: `iTerminal.Widgets/Application`, `iTerminal.Core/Extensions`, `iTerminal.Core/AssemblyInfo.cs`, `CacheService`, `StartupWarmUp`, `UiService`, `ToolsMenuItem`, `HotKeys`, `ExecuteCommandTask` and `ContentModelFileSystem.RunTerminalCommandAsync`. Counted with `grep` and two small scripts (constructor parameters, re-anchoring): reference edges, `internal` types shared across projects, constructor sizes, service-locator calls, blocking waits, process starts, `HttpClient`s, clock calls, Sonar suppressions and copied blocks.

Run: `dotnet list iTerminal.slnx package` with `--outdated`, `--outdated --include-prerelease`, `--vulnerable --include-transitive` and `--deprecated`, against nuget.org (read-only). Not done: no build, no test run, no run-time check; no SSH, SMB, FTP or WebDAV server was available.

**Summary**

- One High finding is open and unchanged: user data (file labels, the playing queue) shares the cache database that any model change deletes (finding 33). No new finding is High.
- The widget split (#822 to #824) keeps the product's layers clean, but left three things behind: the weather and quote cache with the app's only scoped registration in Core (finding 61), contracts that hand over built dialogs and a free-string menu group (finding 62), and tests that reference the layer above them (finding 38).
- The largest structural cost is still the composition: constructors of 125, 78, 68 and 67 parameters build about 200 dialogs before the first frame (finding 32). The new contracts are the way out once they carry factories.
- Two duplication findings gained copies with a real effect: the command-line fallback can hang on a large output because it waits before reading its pipes (finding 43), and five runtime dependencies change the process-wide `PATH` without a lock (finding 42).
- No package has a known vulnerability; `Svg.Skia` 5.2.4 and `ZstdSharp.Port` 0.9.1 are new updates (finding 63), and the analyzer line of #810 is copied into 13 project files (finding 5).
- Strong parts: the dependency chain is the one `CLAUDE.md` states, `iTerminal.Security` really is pure, terminal ownership and the end-to-end sandbox hold, and every one of the 166 Sonar suppressions in product code says why on its own line.

**Findings**

41. **Five documents describe a state the code has left** — **Low**, kind: documentation.
    - **Where:**
      - [CLAUDE.md:43](../../CLAUDE.md) lists the vault among the contents of the database. The password manager keeps its own store and reads the old `VaultEntries` rows only once, to migrate them ([PasswordManager.cs:392](../../iTerminal.Core/Services/PasswordManager.cs)).
      - [panels-and-drives.md:11](../architecture/panels-and-drives.md) says the no-argument `SettingsService()` creates "a temporary in-memory DB". It creates a file in the temp folder ([SettingsService.cs:151](../../iTerminal.Core/Services/SettingsService.cs), finding 34). The same line lists `WeatherService` and `IForecastService` under `iTerminal.Core/Services/`, and `:12` lists `OnlineQuoteJob` and `WeatherJob` under `iTerminal.Core/Jobs/`; all four moved to `iTerminal.Widgets` in #822.
      - No page in `Documentation/architecture/` describes the `iTerminal.Widgets` project or the contracts in `iTerminal.Core.Extensions`, and `file-operations.md` names files in `iTerminal.Security/` without saying what the project holds; only `CLAUDE.md` describes the two projects. The widget sections of [dialogs.md:179](../architecture/dialogs.md) (translator), `:201` (currency), `:252` (radio), `:312` (encyclopedia) and `:322` (YouTube) give folders such as `Services/Translation/` with no project, which a reader resolves under `iTerminal.Core`.
      - [UiText.cs:4](../../iConsole/Localization/UiText.cs) calls itself the localizer for "the few places" that are not handed an `ILocalizer`. It is called 3 636 times in 366 files, so the ambient static is the main path.
      - [iTerminal/iTerminal.csproj:26](../../iTerminal/iTerminal.csproj) says the notice generator is "compiled by RoslynCodeTaskFactory". [ThirdPartyNotices.targets:8](../../iTerminal/Build/ThirdPartyNotices.targets) runs it with `dotnet run`.
    - **Problem:** a developer who trusts these lines looks for the vault and the widgets in the wrong place, expects test databases to disappear, and has no page that tells them how a feature plugs into the menus without Core naming it. Principle: explicit contracts.
    - **Improvement:** correct the sentences: name `PasswordManager`'s own store in `CLAUDE.md`; say "a temporary file" in `panels-and-drives.md` (or follow finding 34) and move the four widget names to a short "Online information widgets (`iTerminal.Widgets`)" paragraph there that lists the eight contracts and `AddWidgets`; prefix the widget folders in `dialogs.md` with `iTerminal.Widgets/`; describe `UiText` as the ambient localizer that `ILocalizer` sets; correct the project comment.
    - **Effort:** S.
    - **Risk of the change:** none (text only).

**What works well**

- The dependency chain that `CLAUDE.md` states is the one the project files build, including the two projects added since the last pass. `iTerminal.Core` names `iTerminal.Widgets` only in its `InternalsVisibleTo`, and the widgets use two of its internal types.
- The warning fixes (#840, #841 and this branch) changed many files without changing a layer edge, and left every suppression justified in place: 166 `#pragma warning disable S…` lines in product code, each with a reason, and no `NoWarn` for a Sonar rule in any project file.
- #831 resolved findings 30 and 31 on `main`, with the no-I/O rule now written on `ICommand.CanExecute`.
- The research documents and the code agree on the threading rules, and where the code departs from them a comment nearby states the rule, so the fixes have a pattern to follow.

**Research cross-reference**

- Profiling overview, items 1 and 17 (render and key loops poll every millisecond; 43 to 46 % of a core with a large folder on screen): finding 56 is their structure and gives the steps. Ranked below finding 33 only because it costs CPU, not data.
- Profiling overview, item 4 (start-up, 2.7 to 3.0 s before the database initializer, 1.1 to 1.4 s of it unexplained): finding 32 and, since #824, finding 62 are candidate causes. Still **unverified** until the `StartupTrace` marks that item asks for are added.
- Profiling overview, item 5 (2.5 s per test for a temporary database): finding 34 adds that those databases are never deleted; 6 009 of them were in `/tmp` on this machine three days after the folder was emptied.
- Profiling overview, item 3 (a `git rev-parse` every 3 s at idle): not reviewed here; no architecture finding rests on it.
- Design review, finding 36 (resolved in #835): `FormPanel` is now the target of finding 45. Design review, finding 68: findings 19, 44 and 45.
- Security overview, item 27: finding 7.
- Design review, section "List-and-form dialogs" (added while this pass ran): findings 88 (append-only forms), 92 (cut by UTF-16 length), 93 (hand-painted tables) and 94 (Esc during a y/n question) are the user-facing side of findings 44 and 45, and support doing finding 45 with `FormPanel`. Its finding 90 (folder checks with blocking calls on the key path when a workspace is restored) would break the 200 ms rule; it is reported there and was not re-checked here.

**Suggested order** (all scopes in this document)

1. Quick wins, S, no dependencies:
   - finding 43 step 4 (the command-line fallback onto `IExternalProcessRunner`, which removes the hang), finding 42 step 1 with the `PATH` helper;
   - finding 61 steps 1 and 2 (the cache to a singleton, then to the widgets), finding 62 step 1 (typed menu group), finding 64;
   - findings 9, 25, 29, 3, 40, 63 (packages, one per pull request), 14 and 15;
   - findings 27, 28, 35 step 1, 39; findings 55, 59, 60;
   - findings 38 (the video fake and FEN first), 41, 47, 48, 49, 51 and 53.
2. High: finding 33 needs a maintainer decision (a second database file) before code. Its hash part, and step 3 of finding 61, come only after the split, because both recreate the cache and would drop the labels with it.
3. Medium:
   - finding 18 (validated container test) after finding 61 step 1, then finding 32 with finding 62 step 2, one consumer per pull request;
   - finding 34 together with profiling item 5;
   - finding 5 (central versions; the analyzer line first), then 6 (audit and lock files);
   - finding 57, then 54, then 56;
   - finding 36, then 37 and 19;
   - finding 43 steps 1 to 3, then finding 42 step 2; finding 44, then 45;
   - findings 46, 4 and 7 after a maintainer decision.
4. Low, when the code is touched anyway: findings 20, 50, 52 and 58.

## Threading and state

Part of the whole-repository pass of 2026-11-21. Re-checked on 2026-11-24 in the whole-repository pass at `c5c693b3`: every open finding below still holds, and its file and line references were moved to that commit (files that #822 moved now point into `iTerminal.Widgets`). No new finding in this scope; the process-wide `PATH` race found in this pass is recorded with its copies in finding 42, and the hang of the command-line fallback in finding 43. Read for this scope:

- `Button`, `Control.GetVisualState`, `Command.RunAsync` and `ICommand`;
- the four commands whose `CanExecute` asks a content model whether a file exists;
- `ContentModelRemoteDrive`, `ContentModelAsyncDrive` and `RuntimeDependencyService`;
- `ConsoleOwnership` and its participants;
- the `static` fields of `iConsole` and `iTerminal.Core`;
- every `.Wait()` and `GetAwaiter().GetResult()` outside the tests (72 matches on 2026-11-21; 118 matches of `.Wait(`, `GetAwaiter().GetResult()` and `.Result` in the product projects on 2026-11-24, 78 of them in `iTerminal.Core`).

**Summary**

- `CanExecute` is on the render path: a `Button` asks it on every draw. The contract was written in two comments but not on `ICommand`, and the F3 and F4 commands broke it on the remote drives (finding 31, resolved in this pass).
- The terminal-ownership rule holds everywhere: the four loops that read or write the terminal bracket each pass, and only the console drivers write to it.
- The worker-to-UI hand-off is still written by hand in 12 dialogs (finding 19, in "Layering and dependencies").

**Findings**

No open findings in this scope. Finding 31 is resolved (see **Resolved**).

**What works well**

- Terminal ownership is applied everywhere it is needed. `ConsoleBufferRendererJob`, `KeyboardEventPublisherJob`, `PosixTerminalInputPublisherJob` and `MouseEventPublisherJob` bracket each pass with `TryBeginUse`. The only direct console writes outside the drivers are in `InteractiveShellService` (writing to the lent terminal) and the key reader.
- `ContentModelAsyncDrive` answers the F3, F5 and F8 checks from the listing and says why in its comment; it is the pattern finding 31 extended to the remote drives.
- `FileHandlerRegistry` publishes one immutable snapshot of its handlers, so a plug-in loaded at run time never shows a half-changed list to the render thread.
- The two ambient statics that tests would trip over have per-flow scopes (`UiText.Scope`, `TextClipboard.Scope`). `DialogRefreshHostedService` takes its static service provider back when the host stops, so the container is not kept alive after the app ends.
- Shutdown takes 0.11 to 0.16 s in the profiling measurements, far inside the five seconds jobs get.

**Research cross-reference**

- Profiling overview, item 1: finding 56 in "Rendering pipeline". It is the rendering-pipeline change that needs a design decision first.
- Design review, finding 68 (hand-painted tables), and finding 19 here: the shared hand-off primitive is still missing.

## Drives and content models

Part of the whole-repository pass of 2026-11-21. Re-checked on 2026-11-24 in the whole-repository pass at `c5c693b3`: every open finding below still holds, and its file and line references were moved to that commit (files that #822 moved now point into `iTerminal.Widgets`). Read for this scope:

- `IFileSystem`, `IContentModel` and `IRemoteFileSystemService`;
- the SSH, SMB, FTP, WebDAV, S3, OneDrive, Google Drive and MTP services, and `HttpFileSystemServiceBase`;
- the content model classes and `ContentModelService.AddContentModel`;
- `Container`, `ContainerType`, `Url` (scheme references only) and `FolderUrlValidationModel`.

**Summary**

- `IFileSystem` is half synchronous, so the network drives block on their own async code with no way to cancel (finding 36).
- Every drive scheme of the app is defined in `iConsole`, so a new drive changes the framework's public enum (finding 37).

**Findings**

36. **`IFileSystem` is synchronous where the drives are remote, so six services block on their own async code without cancellation** — **Medium**, kind: interface, concurrency.
    - **Where:**
      - The interface: [IFileSystem.cs:9](../../iConsole/Interfaces/IFileSystem.cs) to `:15`. `DeleteFile`, `DeleteFolder`, `GetAvailableDiskSpace`, `ExistsFile` and `ExistsFolder` are synchronous, `WriteFileAsync` takes a concrete `MemoryStream`, and only the two stream members at `:22` and `:28` take a `CancellationToken`. [IContentModel.cs:7](../../iConsole/Interfaces/IContentModel.cs) inherits all of it.
      - Blocking calls, all with `CancellationToken.None`: [HttpFileSystemServiceBase.cs:169](../../iTerminal.Core/Services/Cloud/HttpFileSystemServiceBase.cs), [OneDriveFileSystemService.cs:193](../../iTerminal.Core/Services/Cloud/OneDriveFileSystemService.cs) and `:333`, [GoogleDriveFileSystemService.cs:261](../../iTerminal.Core/Services/Cloud/GoogleDriveFileSystemService.cs) and `:444`, [S3FileSystemService.cs:572](../../iTerminal.Core/Services/Cloud/S3FileSystemService.cs) and `:585`, [WebDavFileSystemService.cs:123](../../iTerminal.Core/Services/WebDavFileSystemService.cs) and `:169`, [MtpDriveService.cs:119](../../iTerminal.Core/Services/Mtp/MtpDriveService.cs) and `:199`. The tool-backed drives do the same for their existence checks: [GitDriveService.cs:242](../../iTerminal.Core/Services/Git/GitDriveService.cs), [DockerDriveService.cs:205](../../iTerminal.Core/Services/Docker/DockerDriveService.cs) and [ArchiveDriveService.cs:121](../../iTerminal.Core/Services/Archives/ArchiveDriveService.cs) (counted on 2026-11-24).
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
      - In `iTerminal.Core`: [ContentModelService.cs:137](../../iTerminal.Core/Services/ContentModelService.cs) (`AddContentModel`, 21 `case ContainerType.` branches; the constructor takes 34 parameters, 20 of them optional) and [FolderUrlValidationModel.cs:24](../../iTerminal.Core/Model/Validations/FolderUrlValidationModel.cs) (a hand-kept copy of the list, whose comment says so).
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

**Suggested order**

1. Finding 36, first step.
2. Finding 37, registry step.

## Persistence

Part of the whole-repository pass of 2026-11-21. Re-checked on 2026-11-24 in the whole-repository pass at `c5c693b3`: every open finding below still holds, and its file and line references were moved to that commit (files that #822 moved now point into `iTerminal.Widgets`). Read for this scope:

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
      - The user tables in that file: [iTerminalDbContext.cs:22](../../iTerminal.Core/Data/iTerminalDbContext.cs) (`FileLabels`, the colour labels and tags a user sets, [FileLabelService.cs:29](../../iTerminal.Core/Services/Labels/FileLabelService.cs)) and `:23` (`MusicPlaylistEntries`, the playing queue, [MusicPlaylistStore.cs:69](../../iTerminal.Core/Services/Music/MusicPlaylistStore.cs)).
      - The other user data lives as JSON in the `Settings` table, and it survives because `SettingsService` holds it in memory and writes it back: [ToDoTask.cs:285](../../iTerminal.Core/Model/ToDo/ToDoTask.cs), [Contact.cs:159](../../iTerminal.Core/Model/Contacts/Contact.cs), [Meeting.cs:261](../../iTerminal.Core/Model/Calendar/Meeting.cs), [MailModels.cs:118](../../iTerminal.Core/Model/Mail/MailModels.cs), [RadioStationStore.cs:177](../../iTerminal.Widgets/Model/Radio/RadioStationStore.cs) and [WebBrowserStore.cs:161](../../iTerminal.Core/Services/Web/WebBrowserStore.cs). Each of those saves calls `Write()`, which reads and rewrites the whole table ([SettingsService.cs:107](../../iTerminal.Core/Services/SettingsService.cs)).
      - The hash covers entity and property names and CLR types only ([DatabaseInitializationHostedService.cs:227](../../iTerminal.Core/Application/DatabaseInitializationHostedService.cs)). The 46 key, index and length settings in `OnModelCreating` are not in it.
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
    - **Where:** [SettingsService.cs:31](../../iTerminal.Core/Services/SettingsService.cs) (`public SettingsService()`) and `:151`, which writes a new `iTerminal.Settings.<guid>.sqlite` to `Path.GetTempPath()` and never deletes it; the class is not `IDisposable`. [testing.md:6](../architecture/testing.md) recommends the constructor, and there are 127 call sites in 46 test files (123 at the pass of 2026-11-21; profiling overview, item 5). On the machine of this review, `/tmp` held 29 158 of these files (3.2 GB) on 2026-11-21 and, after the folder was emptied, 6 009 again (681 MB) on 2026-11-24.
    - **Problem:** a production class carries a test-only constructor whose file has no owner. Each test run adds files to every developer's and runner's temp folder until the disk fills, and the same construction is the 2.5 s set-up cost of profiling item 5. Principle: ownership and lifetime, testability seams kept in test code.
    - **Improvement:** move the temporary factory to `iTerminal.Core.Test/Helper` as `TestSettings.Create()`. Back it with an in-memory SQLite connection kept open for the life of the service, or with a copy of the template database that profiling item 5 proposes, deleted in a `[TestCleanup]`. Replace the 127 calls mechanically, and remove the parameterless constructor from `SettingsService`.
    - **Effort:** M (mechanical).
    - **Risk of the change:** `InstantiateAllTypesTest` may use the parameterless constructor to build `SettingsService` for other types. It then needs the container path or a registered factory, and the test shows it. An in-memory database behaves like the file for the settings tests.

**What works well**

- The schema stamp is in its own `DatabaseMetadata` table. `SettingsService` skips `__` keys on read and on write and drops stale reserved rows ([SettingsService.cs:117](../../iTerminal.Core/Services/SettingsService.cs)), and `SettingsServiceTest` asserts both directions.
- The initializer clears the SQLite connection pools before it deletes the file. The clean-up of old cache rows waits until 10 s after start instead of compiling queries on the start-up path.
- `MusicPlaylistStore` states why it rewrites the whole queue (consistent positions). `FileLabelService` keeps an in-memory copy, so the render thread never queries the database.

**Research cross-reference**

- Profiling overview, "What is already fast" (cached tables survive a second start): confirmed for an unchanged model. Finding 33 is about a changed one, which the profiling run did not exercise.
- Profiling overview, item 5: finding 34 is the lifetime side of the same constructor.

**Suggested order**

1. Finding 34 (with profiling item 5).
2. Finding 33: the decision and the split first, the hash change last.

## Composition and DI

Re-checked on 2026-11-24 in the whole-repository pass at `c5c693b3` (see "Whole repository"), replacing the text of 2026-11-21. Since then #822 moved the online information widgets into `iTerminal.Widgets` with their own `AddWidgets()`, and #824 added the extension contracts in `iTerminal.Core.Extensions` through which the widgets reach the menus, the hot-key row, the menu bar, the AI tool and the warm-up. Read for this scope:

- `Program.cs` (which now calls `AddWidgets` after `AddiTerminal`, [Program.cs:57](../../iTerminal/Program.cs)), `iConsoleApplicationBuilderExtension` (52 registrations), `iTerminalApplicationBuilderExtension` (706 lines, 332 registrations), `WidgetsApplicationBuilderExtension` (43 registrations) and `StartupWarmUp`;
- the eight contracts in `iTerminal.Core/Extensions` and their consumers (`ToolsMenuItem`, `HotKeys`, `TerminalMenuBar`, `UiService`, `StartupWarmUp`);
- `ShowDialogCommand` and `DialogRefresh`; `HotKeys`, `TerminalMenuBar`, `ToolsMenuItem`, `FileMenuItem`, `FilePanelFactory` and `ContentBrowser`;
- `UiService`, `AiChatService` (the dialog tool) and `CommandCatalog`;
- the sandbox registrations in `iTerminal.Test/TerminalApp.cs`;
- the service-locator calls outside composition code: 35 in 8 files (43 in 9 at the last pass), 27 of them in `UiService`.

**Summary**

- The constructors that take every dialog shrank only a little: 125, 78, 68 and 67 parameters (133, 80, 67 and 66 before), because the widgets' dialogs left them. Their replacement, the contribution contracts, resolves each dialog as eagerly as before and builds three of them twice (finding 32; the contracts themselves are finding 62 in "Interfaces and seams").
- The AI's dialog tool now has a fourth list, the widgets' `IOpenableDialogContribution` names, beside the description, the switch and the app's commands (finding 35).
- The three media projects still have no registration extension of their own (finding 39).
- `AddWidgets` is called from `Program.cs` and the end-to-end host in the same order ([TerminalApp.cs:204](../../iTerminal.Test/TerminalApp.cs)), and the sandbox replaces the widgets' providers there (translation, weather, quotes, map tiles).

**Findings**

32. **Menus, the footer and the panels take about 200 dialogs as constructor arguments, so every dialog is built at start-up** — **Medium**, kind: coupling, lifetime, performance.
    - **Where:**
      - Constructor sizes at `c5c693b3`, as parameters / optional / dialog-typed (counted with a parameter-counting script): [TerminalMenuBar.cs:42](../../iTerminal.Core/Controls/MenuBar/TerminalMenuBar.cs) 125 / 89 / 93, [HotKeys.cs:31](../../iTerminal.Core/Controls/StatusBar/HotKeys.cs) 78 / 42 / 55, [ContentBrowser.cs:99](../../iTerminal.Core/Controls/Contents/ContentBrowser.cs) 68 / 63 / 47, [FilePanelFactory.cs:14](../../iTerminal.Core/Factories/FilePanelFactory.cs) 67 / 60 / 43. Also [ToolsMenuItem.cs:12](../../iTerminal.Core/Controls/MenuBar/ToolsMenuItem.cs) (54 parameters, 48 of them dialogs) and [FileMenuItem.cs:15](../../iTerminal.Core/Controls/MenuBar/FileMenuItem.cs) (47).
      - `AddiTerminal` registers 109 dialogs as transient and `AddWidgets` 9 more. So `GitClientDialog` ([iTerminalApplicationBuilderExtension.cs:177](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs)) and `SettingsDialog` (`:336`) are each built once for `HotKeys` and once for `TerminalMenuBar`. The widget dialogs follow the same rule through the contracts: `StockBrowserDialog`, `TranslatorDialog` and `CurrencyConverterDialog` are resolved once for the Tools entry ([WidgetsApplicationBuilderExtension.cs:141](../../iTerminal.Widgets/Application/WidgetsApplicationBuilderExtension.cs)) and once for the hot key (`:118` to `:123`), and the hot key and the menu open two different instances.
      - `ShowDialogCommand` holds the dialog it was given ([ShowDialogCommand.cs:58](../../iTerminal.Core/Commands/ShowDialogCommand.cs)). The static `DialogRefresh.Services` (`:13`) exists only to rebuild it after a language change.
      - Two other mechanisms exist beside it: `UiService` resolves on demand ([UiService.cs:30](../../iTerminal.Core/AI/UiService.cs)), and `FilePanelFactory` takes two dialogs as `Func<>` ([FilePanelFactory.cs:42](../../iTerminal.Core/Factories/FilePanelFactory.cs) and `:58`).
      - `FilePanelFactory` is on the warm-up list ([StartupWarmUp.cs:20](../../iTerminal.Core/Application/StartupWarmUp.cs)). Profiling overview, item 4.
    - **Problem:** the start-up path builds every dialog whether or not it is ever opened, and builds some twice. A dialog that keeps state (a search text, a selected tab) shows different state from its hot key and from its menu entry. A new dialog touches up to four constructors in Core; a widget dialog avoids that, but only by registering a factory that builds the dialog when the menu is built. The `= null` defaults hide a missing registration: the container passes `null`, and the hot key or menu entry quietly disappears. A validated container (finding 18) would not catch that either, because an optional parameter with no registration is valid. Principle: ownership and lifetime, evolution cost, performance structure. The start-up cost is **unverified**: profiling item 4 found 1.1 to 1.4 s without a cause, and this is a candidate.
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
      - The contracts of #824 are the way out of the large constructors: once a contribution carries a factory instead of a built command (finding 62), Core's own Tools entries and hot keys can be registered as contributions too, and `ToolsMenuItem` and `HotKeys` lose their dialog parameters one group at a time.
      - The 95 `ShowDialogCommand` subclasses change their constructor parameter type. All are in `iTerminal.Core`, so nothing outside the solution breaks.
    - **Effort:** L in total; M for the factory and `HotKeys`.
    - **Risk of the change:** the first open of each dialog pays its construction on the key path. A dialog whose constructor does I/O would then break the 200 ms rule and must be found: the end-to-end `DialogsTest` opens every dialog by its key, and `DialogKeyHintsTest` checks the footer. `InstantiateAllTypesTest` must accept the open generic.

35. **The AI's `open_dialog` tool keeps its own list of dialogs and reports success for a name it does not know** — **Medium**, kind: consistency, interface.
    - **Where:**
      - [AiChatService.cs:336](../../iTerminal.Core/AI/AiChatService.cs) to `:342`: the tool description names 13 dialogs, and the reply is "Opened {name}." whatever happened.
      - [UiService.cs:30](../../iTerminal.Core/AI/UiService.cs) to `:65`: a `switch` over 24 dialogs and their aliases, resolved through the service provider, then (since #824) the names of `IOpenableDialogContribution` (`:68` to `:75`; four widget dialogs). Each call resolves a new transient instance, so a dialog the AI opens is a third instance beside the hot key's and the menu's.
      - [IUiService.cs:11](../../iTerminal.Core/AI/IUiService.cs): `Task OpenDialogAsync(string name)`, with no result.
      - There are 95 `ShowDialogCommand` subclasses, and [CommandCatalog.cs:32](../../iTerminal.Core/Services/CommandPalette/CommandCatalog.cs) already gathers every command from the control tree for the palette. The Watch dialog of #801 and the print, mail and weather dialogs are among those the tool cannot open.
    - **Problem:** four lists of the same thing drift apart: the description offers 13 names, the switch knows 24, the contributions add 4 (the translator is named in the description and served by a contribution), and the app has about 100 dialogs. An unknown name opens nothing, the tool tells the model it opened, and the model tells the user. Principle: one place per decision, explicit contracts.
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
    - **Where:** [FileFormatsServiceCollectionExtension.cs:39](../../iConsole.Formats/Application/FileFormatsServiceCollectionExtension.cs) (`AddFileFormats`, called from `AddiTerminal` at [iTerminalApplicationBuilderExtension.cs:63](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs)). Against that: the audio sinks at `:246` to `:248` (their order is the fallback order), the video decoder factories at `:269` and `:270`, and the speech recognizer and microphone at `:656` and `:657`. All come from projects split out in #792, #796 and #797.
    - **Problem:** the projects are meant to be reusable beside `iConsole`, but another host must copy decisions that belong to them, such as the sink order. Principle: consistency with the framework (the format project solved the same problem one way, the media projects another).
    - **Improvement:** `AddAudioOutput()` in `iConsole.Media.Audio`, `AddVideoDecoders()` in `iConsole.Media.Video` and `AddSpeech(Func<string, string?> findOnPath)` in `iConsole.Media.Speech`, called from `AddiTerminal` in place of the lines.
    - **Effort:** S.
    - **Risk of the change:** a changed registration order changes which sink is tried first. `iTerminalApplicationBuilderExtensionTest` and `MusicPanelsRegistrationTest` check the registrations.

**What works well**

- One registration point: `Program.cs` calls `AddiConsoleCore` and `AddiTerminal`, and the end-to-end host calls the same `AddiTerminal` and then replaces what reaches outside the machine ([TerminalApp.cs:203](../../iTerminal.Test/TerminalApp.cs)). The app and its test host cannot drift apart.
- The service-locator calls outside composition code are few (35 in 8 files at `c5c693b3`, 27 of them in `UiService`), and each states why: breaking a construction cycle (`UiService`), staying out of the desktop's construction graph (`ErrorPromptService`, `PromptService`, `UpdateCheckJob`), or the language refresh.
- `StartupWarmUp` builds the slow independent singletons on their own threads while the host builds the rest.
- `DialogRefreshHostedService` releases the static it fills, which fixed a 40 MB growth per start in the end-to-end suite.

**Research cross-reference**

- Profiling overview, item 4: finding 32 is a candidate cause. Add the `StartupTrace` marks first, then measure the construction of `HotKeys` and `TerminalMenuBar`.
- AI overview: no item covers the dialog tool's list; finding 35 is the first.

**Suggested order**

1. Finding 35, first step, and finding 39 (S).
2. Finding 18 (validated container test, in "Layering and dependencies"), then finding 32, one consumer per pull request, with finding 62 step 2 (in "Interfaces and seams") for the widget contracts.

## Layering and dependencies

Re-checked on 2026-11-24 in the whole-repository pass at `c5c693b3` on `fix/build-warnings-3`, replacing the refresh of 2026-11-21. The changes since then that touch this scope are #821 (the unused language server client removed from `iConsole`), #822 (the online information widgets moved into `iTerminal.Widgets`), #823 (the file-security code moved into `iTerminal.Security`) and #824 (the extension contracts in `iTerminal.Core.Extensions`). Read: every `*.csproj`, `iTerminal.slnx` and `Directory.Build.props`; the `using` lines of `iTerminal.Widgets` and `iTerminal.Security`; `iTerminal.Core/AssemblyInfo.cs`; the eight extension contracts; `CacheService`, `iTerminalDbContext` and the cache clean-up in `DatabaseInitializationHostedService`; the two `Instantiate*TypesTest` classes. The `ProjectReference`, `namespace` and `using` edges were checked with `grep`; the `internal` types of `iTerminal.Core` were matched against `iTerminal.Widgets` by name. Not done: no build, no test run, no run-time check.

**Summary**

- The dependency direction holds for the product. `iTerminal.Core` names `iTerminal.Widgets` in one place, its `InternalsVisibleTo` ([AssemblyInfo.cs:4](../../iTerminal.Core/AssemblyInfo.cs)), and never in code. `iTerminal.Widgets` references only `iTerminal.Core` and uses two of Core's 80 internal types (`PackageManagerProcess`, `PasteHelper`). `iTerminal.Security` references only `iConsole`, uses only `iConsole.Localization` from it, and has no file, process or network call: the "pure functions" claim of `CLAUDE.md` is true.
- The split left one piece of widget behaviour in Core: the weather and quote cache, with the only scoped registration in the app (finding 61).
- The tests do not follow the product's direction: `iTerminal.Core.Test` references `iTerminal.Widgets`, and `iTerminal.Widgets.Test` references `iTerminal.Core.Test` (finding 38).
- Still open, unchanged in substance: no test builds the real container (finding 18), five dialogs hand-write the worker-to-UI hand-off (finding 19), the converter interface mixes routing with conversion (finding 20; 21 `iConsole.Test` files still use `iConsole.Formats`), the version is read in two places (finding 27), and the WinGet hint is decided in three places (finding 28).

**Findings**

18. **No test builds the real container, and `InstantiateAllTypesTest` does not check what `CLAUDE.md` says it checks** — **Medium**, kind: testability, documentation.
    - **Where:**
      - [iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs:141](../../iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs): any type no constructor can build is allocated with `GetUninitializedObject` and counts as a success; again at `:243`, `:294` and `:312`.
      - [iConsole.Test/Generated/InstantiateIConsoleTypesTest.cs:41](../../iConsole.Test/Generated/InstantiateIConsoleTypesTest.cs): the same fallback.
      - [iTerminal.Core.Test/Application/iTerminalApplicationBuilderExtensionTest.cs:22](../../iTerminal.Core.Test/Application/iTerminalApplicationBuilderExtensionTest.cs): checks registrations by type and builds nothing.
      - The contract is in [CLAUDE.md:117](../../CLAUDE.md) and [testing.md:9](../architecture/testing.md). No `.cs` file passes `ValidateOnBuild`, and the host builder in [iConsoleApplicationBuilder.cs:20](../../iConsole/Application/iConsoleApplicationBuilder.cs) does not either.
    - **Problem:** neither test uses the container, and both fall back to an uninitialised object, so a public type whose dependencies are not registered passes. Since the pass of 2026-11-21, #822 moved the widget registrations into a second extension that a host must remember to call after `AddiTerminal`, and the only scoped registration in the app is consumed by two singleton jobs (finding 61); `ValidateScopes = true` would refuse that today. The dialogs that need them are transient and resolved only when opened, so a host that misses one builds, starts, and fails on first use. Only the end-to-end suite (4 to 5 minutes) reaches that path. A validated container still cannot see an optional parameter whose service is missing (finding 32). Principle: testability seams, explicit contracts.
    - **Improvement:** add `iTerminal.Core.Test/Application/CompositionRootTest`. It registers what `Program.cs` registers (`AddDefaultConsoleDriver`, `AddiConsoleCore`, `AddiTerminal` with a temporary `DataDirectory`, `AddWidgets`, and the sandbox replacements the end-to-end host uses), builds with `new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }`, and resolves every registered non-factory service type once. Then correct `CLAUDE.md:117` and `testing.md:9` to say what `InstantiateAllTypesTest` checks (that each public type can be allocated and, where possible, constructed), and point to the new test for the "the container can resolve it" contract.
    - **Effort:** S to M. The sandbox list exists in [iTerminal.Test/TerminalApp.cs:203](../../iTerminal.Test/TerminalApp.cs); some factories may need a stand-in.
    - **Risk of the change:** the first run may fail on real gaps or on services whose constructor touches the machine (`UpdateService` reads the registry on Windows). Each is either a fix or a sandbox registration. No production change.

61. **The widgets' cache stayed in Core: `ICacheService`, its two tables and their lifetimes, and the app's only scoped registration** — **Medium**, kind: layering, lifetime.
    - **Where:**
      - [CacheService.cs:6](../../iTerminal.Core/Services/CacheService.cs) to `:12`: `ICacheService` has five members, all about the weather and the instrument quotes, and its only users are the widgets' jobs: [WeatherJob.cs:13](../../iTerminal.Widgets/Jobs/WeatherJob.cs), [OnlineQuoteJob.cs:15](../../iTerminal.Widgets/Jobs/OnlineQuoteJob.cs) and [InstrumentQuoteHelper.cs:51](../../iTerminal.Widgets/Jobs/InstrumentQuoteHelper.cs).
      - The tables: [iTerminalDbContext.cs:17](../../iTerminal.Core/Data/iTerminalDbContext.cs) and `:18` (`CachedWeatherInfos`, `CachedInstrumentValues`), with `WeatherInfo` in `iTerminal.Core/Data`.
      - The lifetimes are written twice: 15 minutes and 5 minutes in [CacheService.cs:22](../../iTerminal.Core/Services/CacheService.cs) and `:66`, and again in Core's start-up clean-up ([DatabaseInitializationHostedService.cs:130](../../iTerminal.Core/Application/DatabaseInitializationHostedService.cs) and `:137`).
      - [iTerminalApplicationBuilderExtension.cs:161](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs): `AddScoped<ICacheService, CacheService>()`, the only `AddScoped` in the solution. No code creates a scope, and both jobs are singletons (`IJob`, [WidgetsApplicationBuilderExtension.cs:101](../../iTerminal.Widgets/Application/WidgetsApplicationBuilderExtension.cs) and `:102`), so the service is resolved once from the root provider and lives as a singleton.
    - **Problem:** `CLAUDE.md` puts the widgets in their own project so that a change to a widget does not touch Core, but a change to how the weather or the quotes are cached still edits Core's service, Core's database model and Core's start-up clean-up. The scoped lifetime says something the code does not do, and a container built with `ValidateScopes` (finding 18) refuses a singleton that takes a scoped service, so that test would fail on this first. Principle: dependency direction (app behaviour of a feature in its own layer), ownership and lifetime.
    - **Improvement:** three steps; the first two are behaviour-preserving.
      1. (S) Register `CacheService` as a singleton. It takes an `IDbContextFactory` and makes a context per call, so it holds no per-scope state.
      2. (S) Move `ICacheService`, `CacheService` and `WeatherInfo` to `iTerminal.Widgets` and register them in `AddWidgets`. Give `CacheService` a `PurgeExpiredAsync(CancellationToken)` with the two lifetimes as its own constants, and call it from the widgets (a hosted service or the jobs' first pass) instead of from `DatabaseInitializationHostedService`.
      3. (later) Move the two entity types out of `iTerminalDbContext`. The model hash includes each entity's CLR name ([DatabaseInitializationHostedService.cs:232](../../iTerminal.Core/Application/DatabaseInitializationHostedService.cs)), so this recreates the database and deletes the file labels and the queue (finding 33). Do it only after finding 33's split.
    - **Effort:** S for steps 1 and 2.
    - **Risk of the change:** the clean-up moves from start-up to the widgets, so an install without the widgets keeps stale rows (harmless: the readers check the age). `WeatherJobTest`, `OnlineQuoteJobTest`, `InstrumentQuoteHelperTest` and `DatabaseInitializationHostedServiceTest` cover the paths.

19. **Five dialogs hand-write the same worker-to-UI hand-off, in two styles** — **Medium**, kind: concurrency, consistency.
    - **Where:**
      - With a lock, a generation counter, an in-flight count and `PendingOperation`: [PrintDialog.cs:84](../../iTerminal.Core/Dialogs/PrintDialog.cs) and `:169` (19 `lock (_sync)` blocks), [StreamsDialog.cs:51](../../iTerminal.Core/Dialogs/StreamsDialog.cs) and `:270`, [TorrentManagerDialog.cs:45](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs) and `:355`.
      - With `volatile` fields instead of a lock: [BluetoothDeviceManagerDialog.cs:31](../../iTerminal.Core/Dialogs/Bluetooth/BluetoothDeviceManagerDialog.cs) and `:278`.
      - [ToDoListDialog.cs:42](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs) and `:94`.
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
      - Five call sites: [FileViewerDialog.cs:342](../../iConsole/Dialogs/FileViewerDialog.cs), `:503`, `:523`, `:603`, and [TextFileEditorDialog.cs:639](../../iConsole/Dialogs/TextFileEditorDialog.cs).
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
    - **Where:** [iTerminal.Core/Application/ApplicationInfo.cs:8](../../iTerminal.Core/Application/ApplicationInfo.cs) (fallback `"0.0.0.0"`) and [iTerminal.Core/Dialogs/AboutDialog.cs:81](../../iTerminal.Core/Dialogs/AboutDialog.cs) (fallback `"unknown"`, at `:94`). Users: [UpdateService.cs:69](../../iTerminal.Core/Services/Updates/UpdateService.cs) (the service's current version comes from `AboutDialog`), [TerminalScreen.cs:54](../../iTerminal.Core/Screens/TerminalScreen.cs) (the title, from `ApplicationInfo`) and `:78` (the splash, from `AboutDialog`), and [iTerminal/Program.cs:106](../../iTerminal/Program.cs) (`ApplicationInfo`).
    - **Problem:** one fact has two readers that differ on the edge cases. On a build without an informational version, the window title and the update check can disagree about the running version; `"unknown"` parses to no version, and the check then offers every release. A service that depends on a dialog type also inverts the order inside `iTerminal.Core`. Principle: dependency direction, one place per decision.
    - **Improvement:** keep `ApplicationInfo.Version` as the only reader. Make `AboutDialog.Version` return it (or remove it and change its uses), and pass `ApplicationInfo.Version` in `UpdateService`'s public constructor. No public API changes (`AboutDialog.Version` is `internal`).
    - **Effort:** S.
    - **Risk of the change:** the About dialog shows `0.0.0.0` instead of `unknown` on an unstamped build. `AboutDialogTest`, `ControlIntegrationTest` and `KeyHintGuardTest` compare against the property and stay green.

28. **The WinGet case is decided separately in each consumer of `IUpdateService`** — **Low**, kind: interface, consistency.
    - **Where:**
      - [IUpdateService.cs:47](../../iTerminal.Core/Services/Updates/IUpdateService.cs) (`InstallKind`) and `:50` (`CanInstall`).
      - Three consumers each branch on `InstallKind == UpdateInstallKind.PackageManager` and build the hint themselves:
        - [UpdateDialog.cs:167](../../iTerminal.Core/Dialogs/UpdateDialog.cs);
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

38. **Test projects reference each other and the layer above them, and the shared test helper pulls the video stack into every test** — **Low**, kind: layering, testability.
    - **Where:**
      - [iConsole.Test.Core/iConsole.Test.Core.csproj:16](../../iConsole.Test.Core/iConsole.Test.Core.csproj) references `iConsole.Media.Video` for one fake ([FakeVideoDecoder.cs:1](../../iConsole.Test.Core/Video/FakeVideoDecoder.cs)), used by four tests in `iTerminal.Core.Test/Dialogs/Video` and one in `iConsole.Media.Video.Test`.
      - So every project that uses the helper (`iConsole.Test`, `iConsole.Formats.Test`, `iConsole.Media.Audio.Test`, `iTerminal.Core.Test`, `iTerminal.Test`) also builds `iConsole.Media.Video`, `iConsole.Formats` with SkiaSharp's natives, and `iConsole.Media.Audio`.
      - [iTerminal.Core.Test/iTerminal.Core.Test.csproj:34](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj) references the test project `iTerminal.Core.Games.Test` for one helper, `ChessTest.FromFen` ([GamesDialogTest.cs:279](../../iTerminal.Core.Test/Dialogs/Games/GamesDialogTest.cs)).
      - New since #822: [iTerminal.Core.Test.csproj:37](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj) references `iTerminal.Widgets`, the layer above `iTerminal.Core`, so that the menu and key-hint tests can build widget dialogs (6 files) and so that `InstantiateAllTypesTest`, which loads every `i*.dll` in its output folder ([InstantiateAllTypesTest.cs:35](../../iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs)), sees the widget types. [iTerminal.Widgets.Test.csproj:30](../../iTerminal.Widgets.Test/iTerminal.Widgets.Test.csproj) references the test project `iTerminal.Core.Test` for `UiTestHelper` (19 of its 49 files).
    - **Problem:** the helper project for the framework's tests sits above a media project, so the `iConsole` tests cannot be built without layers that `CLAUDE.md` places above `iConsole`, and a change in the video project rebuilds every test project. The Core tests now build only with the widgets present, so the rule "`iTerminal.Core` never references `iTerminal.Widgets`" holds for the product and not for its tests, and a Core test can come to depend on a widget without anyone noticing. A test project that references another one copies its test assembly into its output; whether the runner then runs those tests twice was not checked (**unverified**). Principle: dependency direction, applied to the tests.
    - **Improvement:** move `FakeVideoDecoder` and `FakeVideoDecoderFactory` to `iTerminal.Core.Test/Helper` (four users), give `iConsole.Media.Video.Test` its own copy or a linked file, and drop the reference from `iConsole.Test.Core`. Move FEN parsing into the engine as `ChessState.FromFen` (it is a domain function, and the engine's own tests use it), and drop the test-to-test reference. Move `UiTestHelper` and its `TestTheme` into a small test-support project for the app (`iTerminal.Test.Support`, beside `iConsole.Test.Core`) that both `iTerminal.Core.Test` and `iTerminal.Widgets.Test` reference. Move the 6 cross-cutting tests that build widget dialogs, and a copy of the instantiation test for the widgets assembly, into `iTerminal.Widgets.Test`, then drop its reference from `iTerminal.Core.Test`.
    - **Effort:** S for the video fake and FEN; M with the widget test moves.
    - **Risk of the change:** none at run time; the build of each test project shows a missing type.

**What works well**

- The edges are the right ones:
  - `iConsole.Formats` → `iConsole` only ([iConsole.Formats.csproj:38](../../iConsole.Formats/iConsole.Formats.csproj));
  - `iConsole.Media.Video` → `iConsole.Formats` and `iConsole.Media.Audio`, as `CLAUDE.md` states ([iConsole.Media.Video.csproj:16](../../iConsole.Media.Video/iConsole.Media.Video.csproj) and `:17`);
  - `iTerminal.Core` → all of them;
  - `iTerminal.Widgets` → `iTerminal.Core` only, and `iTerminal.Security` → `iConsole` only;
  - `iTerminal` reaches them through `iTerminal.Core`, and references `iTerminal.Widgets` itself.
- Each new project says in its project file what it holds and why it is separate, and each has its own test project. For example, [iTerminal.Core.Games.csproj:9](../../iTerminal.Core.Games/iTerminal.Core.Games.csproj) says a change to a game rebuilds that project and its tests, not the whole of `iTerminal.Core`.
- The namespace is the layer: implementations in `iConsole.Formats.*` and `iConsole.Media.*`, contracts in `iConsole.*`. A layering test can be written against namespaces.
- The seams are small and owned by the consumer: `IStructuredDocumentParser`, `IExecutableDisassembler` (with "blocking file I/O: the caller runs it on a worker thread" in the type), `IVideoDecoderFactory`, `ISpeechRecognizer` and `IMicrophoneCaptureFactory`.
- The widget split was done through contracts that Core owns (`iTerminal.Core.Extensions`), not by Core learning the widgets' types, and the `InternalsVisibleTo` comment asks for the list of shared internals to stay short; it is two types.
- #788 put the WinGet decision where it can be tested (`UpdateService.KindFor` takes the registry answer as a `bool`), and `--help` returns before any service, database or terminal is touched.

**Research cross-reference**

- Design review, finding 68 (hand-painted tables): finding 19 is the architecture follow-up, so the remaining conversions do not add more copies of the hand-off.
- Profiling overview, finding 4: still does not measure the `--update` path, which builds every registration before it runs.

**Suggested order**

1. Quick wins: finding 61 steps 1 and 2, finding 27, finding 28, finding 38 (the video fake and FEN first).
2. Finding 18 (a validated container test), after finding 61 step 1 and before finding 32.
3. Finding 20 (routing out of the converter interface): the interface part first, the 21 test files after.
4. Finding 19 (one hand-off primitive), on its own branch; convert the five #778 dialogs in the same pull request.

## Dependencies and packages

Reviewed on 2026-11-24 (fifth pass, within the whole-repository pass, replacing the fourth one of 2026-11-21). Scope "Dependencies and packages", read at `c5c693b3` on `fix/build-warnings-3`. Since the fourth pass the project files changed as follows:

- #810 added `SonarAnalyzer.CSharp` 10.37.0.15768 to every non-test project, with `PrivateAssets="all"` (13 project files, the notice generator included).
- #822 and #823 added `iTerminal.Widgets`, `iTerminal.Security` and their test projects; neither product project adds a package.
- #821 removed the language server client from `iConsole`; no package went with it.

Read: every `*.csproj`, the `.wixproj`, the Linux `.proj`, `Directory.Build.props`, `iTerminal.slnx`, the notice generator, `Overrides.txt` and `ThirdPartyNotices.targets`, `Package.wxs`, `build-and-packaging.md`, `runtime-dependencies.md`, and the build and WinGet workflows.

Run against nuget.org, read-only: `dotnet list iTerminal.slnx package --outdated`, `--outdated --include-prerelease`, `--vulnerable --include-transitive` and `--deprecated` (26 PackageReference projects), and `--vulnerable --include-transitive` for `iTerminal.ThirdPartyNotices`, which is not in the solution. The WiX project is skipped by restore (`NU1503`), and `Packaging.Targets` is restored only when `LinuxPackaging` is `true`, so their latest versions stay **unverified**, as does the stable line of `Serilog.Sinks.File`.

Licence check: `iTerminal/obj/project.assets.json` has the targets `net11.0` and `net11.0/linux-x64`. 94 packages under them carry runtime, native or resource files, every nuspec was in the local cache, and the licence statements are the same as at the fourth pass. The Windows runtime target was not restored here, so the Windows-only natives are **unverified**.

Not done: no build, no test run, no publish. There is still no `global.json`, `Directory.Packages.props`, `Directory.Build.targets`, `nuget.config` or `.github/dependabot.yml`.

**Summary**

- No package has a known vulnerability, direct or transitive, in any of the 27 projects checked. `imobiledevice-net` is still the only deprecated package (finding 4).
- Three updates wait: `OpenMcdf` 3.4.0 (held back on purpose, finding 25), and, new since the fourth pass, `Svg.Skia` 5.2.4 and `ZstdSharp.Port` 0.9.1 (finding 63).
- Licences of the 94 shipped packages are unchanged: 75 MIT, 6 Apache-2.0, 6 licence files (5 MIT texts and SQLite's public-domain notice), one each of MPL-2.0 (`OpenMcdf`), LGPL-3.0-or-later (`SMBLibrary`), BSD-3-Clause (`Snappier`), BSD-2-Clause (`Tomlyn`) and MS-PL (`Svg.Custom`), and two URL-only statements (`K4os.Compression.LZ4`, `iMobileDevice-net`) that `Overrides.txt` covers. No shipped package is under a strong copyleft licence, and every release carries `THIRD-PARTY-NOTICES.txt` and `LICENSE.txt`.
- The audit is not silenced (no `NuGetAuditMode`, `NuGetAuditLevel` or `NoWarn` for `NU1901` to `NU1904` anywhere), but it is not enforced either (finding 6).
- The copied version lines grew: the test packages are now in up to 14 project files, and the analyzer is in 13 (finding 5).

**Findings**

3. **The log sink is a development pre-release** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:35](../../iTerminal.Core/iTerminal.Core.csproj) (`Serilog.Sinks.File` `8.0.0-dev-02318`, there since the first public commit `6a1069a2`). The only use is `.WriteTo.File(path)` in [iTerminal.Core/Logger/Logger.cs:33](../../iTerminal.Core/Logger/Logger.cs).
   - **Problem:** every release ships a CI build of the sink that was never published as stable, and `dotnet list --outdated` cannot report a stable update for it because the stable line is below it. The one call used exists in every stable version. Principle: shipped dependencies are stable unless a feature needs otherwise.
   - **Improvement:** reference the newest stable `Serilog.Sinks.File` (7.x; the exact number was not listed by the commands run, so it is **unverified**) and keep `Serilog` 4.4.0.
   - **Effort:** S.
   - **Risk of the change:** a downgrade of a pre-release. The build and `iTerminal.Core.Test` (the `Logger` tests) show any API gap, and NuGet reports NU1605 if another package needs the newer one.

4. **`imobiledevice-net` is deprecated and its Linux native files are never shipped; the warning that says so is silenced** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:25](../../iTerminal.Core/iTerminal.Core.csproj); [Directory.Build.props:6](../../Directory.Build.props) (`NETSDK1206` suppressed for the whole solution); [iTerminal.Core/Services/IPhoneDeviceService.cs:39](../../iTerminal.Core/Services/IPhoneDeviceService.cs) (a failure to load shows no devices); [panels-and-drives.md:26](../architecture/panels-and-drives.md) (`iphone://`).
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

5. **No Central Package Management: each version is written in up to fourteen project files** — **Medium**, kind: dependency, consistency.
   - **Where:** there is no `Directory.Packages.props`.
     - `MSTest.TestFramework` 4.5.3 is in 14 project files; `MSTest.TestAdapter` 4.5.3 and `Microsoft.NET.Test.Sdk` 18.12.0 in 13; `coverlet.collector` 10.1.1 in 11; `Moq` 4.21.0 in 5.
     - `SonarAnalyzer.CSharp` 10.37.0.15768 is in 13 project files, each with the same `PrivateAssets="all"` line and comment, for example [iConsole/iConsole.csproj:22](../../iConsole/iConsole.csproj). It is the one package that every non-test project shares, which `Directory.Build.props` can hold once with `Condition="'$(IsTestProject)' != 'true'"`.
     - `Microsoft.Playwright` 1.65.1 is in 3 ([iTerminal.Core/iTerminal.Core.csproj:32](../../iTerminal.Core/iTerminal.Core.csproj), [iTerminal.Core.Test/iTerminal.Core.Test.csproj:26](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj), [iTerminal.Widgets.Test/iTerminal.Widgets.Test.csproj:22](../../iTerminal.Widgets.Test/iTerminal.Widgets.Test.csproj)), and `Microsoft.Data.Sqlite` 11.0.0 in 2.
     - Several `Version="11.0.0"` lines cover the Microsoft packages that move with the runtime.
     - The WiX version 5.0.2 appears four times in [iTerminal.Installer/iTerminal.Installer.wixproj:20](../../iTerminal.Installer/iTerminal.Installer.wixproj), `:31`, `:32` and `:57`.
   - **Problem:** an update means editing every copy by hand, and a missed copy is silent. The project splits since the third pass added twelve more project files with copied test-package lines, and the analyzer added 13 copies of one line. The Playwright pair matters most: `PlaywrightRuntimeDependency.LibraryVersion()` ([PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs)) installs the driver for the library version, so a test project on another version tests a driver the app never asks for. Principle: one place per decision.
   - **Improvement:** add `Directory.Packages.props` with `ManagePackageVersionsCentrally` and one `PackageVersion` per package, and remove the `Version` attributes from the `PackageReference` items. Keep the WiX SDK version in one MSBuild property used by both `Import` elements and the two extensions. `Packaging.Targets` can stay conditional.
   - **Effort:** M (about 27 project files, mechanical); S for the analyzer alone in `Directory.Build.props`.
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
    - **Where:** no `global.json`; the build workflow installs `11.0.x` ([.github/workflows/build.yml:32](../../.github/workflows/build.yml) and `:146`); [Directory.Build.props:8](../../Directory.Build.props) (`runtime-async=on`).
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

63. **Two updates wait: `Svg.Skia` 5.2.4 (a patch) and `ZstdSharp.Port` 0.9.1 (a minor release of a 0.x library)** — **Low**, kind: dependency.
    - **Where:** [iConsole.Formats/iConsole.Formats.csproj:25](../../iConsole.Formats/iConsole.Formats.csproj) (`Svg.Skia` 5.2.3; used in [SvgVideoDecoder.cs](../../iConsole.Media.Video/SvgVideoDecoder.cs) and `FileToTextConverterService`) and [iTerminal.Core/iTerminal.Core.csproj:42](../../iTerminal.Core/iTerminal.Core.csproj) (`ZstdSharp.Port` 0.8.8; `DecompressionStream` in [ArchiveExtractionService.cs:279](../../iTerminal.Core/Services/Archives/ArchiveExtractionService.cs), [ArchiveDriveService.cs:270](../../iTerminal.Core/Services/Archives/ArchiveDriveService.cs) and [ReleaseArchive.cs:49](../../iTerminal.Core/Services/RuntimeDependencies/ReleaseArchive.cs), and `CompressionStream` in one test). Both were reported by `--outdated`; neither has an advisory.
    - **Problem:** `ZstdSharp.Port` is below 1.0, so a minor release may change its API under semantic versioning, and it reads release archives in the self-update path, where a regression blocks updates. `Svg.Skia` moves with `SkiaSharp` and `Svg.Custom` (MS-PL); a patch is low risk but changes a shipped binary and the notice file. Principle: keep shipped dependencies current, one reason per update.
    - **Improvement:** two separate pull requests. `Svg.Skia` 5.2.4: check that it does not raise the `SkiaSharp` floor above 4.157.0 (`dotnet list package --include-transitive` before and after) and run `iConsole.Media.Video.Test` and `iConsole.Formats.Test`. `ZstdSharp.Port` 0.9.1: read its release notes for `DecompressionStream` changes, then run the archive tests (`ArchiveExtractionServiceTest`, `ArchiveDriveServiceTest`) and `OllamaRuntimeDependencyTest`, which compresses with it. With finding 6's Dependabot both would arrive on their own.
    - **Effort:** S each.
    - **Risk of the change:** a changed decoder default in `ZstdSharp.Port`; the archive tests with `.tar.zst` fixtures show it. The release-archive reader is covered through `OllamaRuntimeDependencyTest` and `PowerShellRuntimeDependencyTest`.

**Package table**

- "Used by" is the number of source files, in all projects with tests included, that have a `using` of the package's namespace. "native" means the package carries native files only; "build only" means a package used by the build.
- "Latest" is the latest stable version on nuget.org on 2026-11-24, from `dotnet list --outdated`, which reported updates for `OpenMcdf`, `Svg.Skia` and `ZstdSharp.Port` only.
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
| Svg.Skia | iConsole.Formats | 5.2.3 | 5.2.4 | No | MIT | none | 1 | Update (finding 63) |
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
| Microsoft.Extensions.AI | iTerminal.Core | 11.0.1 | 11.0.1 | No | MIT | none | 15 | Keep |
| Microsoft.Extensions.AI.OpenAI | iTerminal.Core | 11.0.1 | 11.0.1 | No | MIT | none | with Microsoft.Extensions.AI | Keep |
| imobiledevice-net | iTerminal.Core | 1.3.17 | 1.3.17 (deprecated) | No | URL only (LGPL-2.1 by override) | none | 2 | Decide the platform contract (finding 4) |
| Microsoft.Mxc.Sdk | iTerminal.Core | 1.1.0 | 1.1.0 | No | MIT (licence file) | none | 2 | Keep |
| Microsoft.Playwright | iTerminal.Core | 1.65.1 | 1.65.1 | No | MIT | none | 2 | Keep (also used by iTerminal.Widgets, transitively) |
| MonoTorrent | iTerminal.Core | 3.0.2 | 3.0.2 | No | MIT (licence file) | none | 1 | Keep |
| Serilog | iTerminal.Core | 4.4.0 | 4.4.0 | No | Apache-2.0 | none | 61 | Keep |
| Serilog.Sinks.File | iTerminal.Core | 8.0.0-dev-02318 | stable 7.x (unverified) | Yes (to stable) | Apache-2.0 | none | 1 | Move to stable (finding 3) |
| SharpZipLib | iTerminal.Core | 1.4.2 | 1.4.2 | No | MIT | none | 10 | Keep |
| SMBLibrary | iTerminal.Core | 1.5.8.1 | 1.5.8.1 | No | LGPL-3.0-or-later | none | 2 | Keep; notice shipped since #789 |
| SSH.NET | iTerminal.Core | 2026.0.1 | 2026.0.1 | No | MIT | none | 1 | Keep; the SSH commands are quoted (finding 30, resolved) |
| ZstdSharp.Port | iTerminal.Core | 0.8.8 | 0.9.1 | Possibly (0.x minor) | MIT | none | 3 | Update after the release notes (finding 63) |
| Packaging.Targets | iTerminal (Linux packaging only) | 0.1.232 | unverified | Unknown | — | not checked | build only | Check by hand |
| WixToolset.Sdk, .UI.wixext, .Util.wixext | iTerminal.Installer | 5.0.2 | unverified | Unknown | — | not checked | build only | Check by hand; one version property (finding 5) |
| SonarAnalyzer.CSharp | 13 non-test project files | 10.37.0.15768 | 10.37.0.15768 | No | — | none | build only | Keep; one line in `Directory.Build.props` (finding 5) |
| MSTest.TestFramework | 14 project files | 4.5.3 | 4.5.3 | No | — | none | all tests | Keep; centralize (finding 5) |
| MSTest.TestAdapter | 13 test projects | 4.5.3 | 4.5.3 | No | — | none | all tests | Keep; centralize (finding 5) |
| Microsoft.NET.Test.Sdk | 13 test projects | 18.12.0 | 18.12.0 | No | — | none | all tests | Keep; centralize (finding 5) |
| coverlet.collector | 11 test projects | 10.1.1 | 10.1.1 | No | — | none | coverage | Keep |
| Moq | iConsole.Test.Core, iConsole.Test, iConsole.Formats.Test, iTerminal.Core.Test, iTerminal.Widgets.Test | 4.21.0 | 4.21.0 | No | — | none | 548 | Keep |
| ZXing.Net | iConsole.Test | 0.16.11 | 0.16.11 | No | — | none | 1 | Keep |
| Reqnroll.MSTest | iTerminal.Specifications.Test | 3.3.4 | 3.3.4 | No | — | none | 75 | Keep |
| Microsoft.Playwright | iTerminal.Core.Test, iTerminal.Widgets.Test | 1.65.1 | 1.65.1 | No | — | none | test helper | Same version as Core (finding 5) |

Which check found what: the vulnerability check found no advisory for any package. The outdated check named `OpenMcdf`, `Svg.Skia` and `ZstdSharp.Port`. The licence check found the LGPL, MPL-2.0 and URL-only statements in the table and the summary. `imobiledevice-net` was named by both the deprecation check and the licence check (URL only).

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

1. Quick wins, no dependencies: findings 9, 25, 29, 3, 40, 14 and 15; finding 63, one package per pull request.
2. Finding 5 (Central Package Management; the analyzer line in `Directory.Build.props` can go first on its own), then finding 6 (audit, lock files, source mapping, Dependabot with the `OpenMcdf` ignore).
3. Finding 4 (the iPhone drive's platform contract) and finding 7 (pinned runtime downloads), which need a decision from the maintainer first.

## Duplication and refactoring

Reviewed on 2026-11-22, scope "Duplication and refactoring" (asked for by name), on branch `feature/ai-panel-tools-more-operations` at commit `cf844281`. No review branch was cut, because the branch is not `main`. Re-checked on 2026-11-24 in the whole-repository pass at `c5c693b3`: every open finding below still holds, and its file and line references were moved to that commit (files that #822 moved now point into `iTerminal.Widgets`). Two findings gained copies in this pass (42: the `PATH` updates; 43: the command-line fallback), finding 44 lost two unused copies (#840), and finding 45 has a better target since #835 (`FormPanel`).

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
      - The copies: [FfmpegRuntimeDependency.cs:19](../../iTerminal.Core/Services/RuntimeDependencies/FfmpegRuntimeDependency.cs), [GitRuntimeDependency.cs:14](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs) and [YtDlpRuntimeDependency.cs:16](../../iTerminal.Widgets/Services/RuntimeDependencies/YtDlpRuntimeDependency.cs). Each repeats the cache tuple, `InstalledVersion`, `InstallAsync`, `Activate`, `Invalidate`, `ListVersionsAsync`, `FindTool` and `SafeWriteTime`. The `InstalledVersion` bodies differ only in a variable name. `SafeWriteTime` is byte-identical in these four files and in [PowerShellRuntimeDependency.cs:405](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs).
      - The drift: [SystemToolRuntimeDependency.cs:119](../../iTerminal.Core/Services/RuntimeDependencies/SystemToolRuntimeDependency.cs) and [FfmpegRuntimeDependency.cs:132](../../iTerminal.Core/Services/RuntimeDependencies/FfmpegRuntimeDependency.cs) treat WinGet's "no applicable update" and "already installed" exit codes (`:151`) as success. [GitRuntimeDependency.cs:116](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs) and [YtDlpRuntimeDependency.cs:128](../../iTerminal.Widgets/Services/RuntimeDependencies/YtDlpRuntimeDependency.cs) test `exitCode != 0` only. yt-dlp's messages are also English-only (`:124`, `:126`), while the other three use the `Dep.NoPackageManager` and `Dep.InstallingWith` keys.
      - `GitInstallPlan` ([GitRuntimeDependency.cs:280](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs) to `:343`) is a line-for-line copy of [SystemPackageInstallPlan.cs:19](../../iTerminal.Core/Services/RuntimeDependencies/SystemPackageInstallPlan.cs), with "git" written in where the general one takes `SystemPackageNames`.
      - Added in this pass (2026-11-24): `Activate` in five of these classes prepends the tool's folder to the process-wide `PATH` with an unguarded read-modify-write ([SystemToolRuntimeDependency.cs:145](../../iTerminal.Core/Services/RuntimeDependencies/SystemToolRuntimeDependency.cs), [GitRuntimeDependency.cs:145](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs), [FfmpegRuntimeDependency.cs:172](../../iTerminal.Core/Services/RuntimeDependencies/FfmpegRuntimeDependency.cs), [PowerShellRuntimeDependency.cs:392](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs) and [YtDlpRuntimeDependency.cs:154](../../iTerminal.Widgets/Services/RuntimeDependencies/YtDlpRuntimeDependency.cs)). Two installs that finish together can drop one folder; one shared `ToolPath.Prepend(string folder)` under a lock, in the base class of step 2, removes the race with the copies.
      - yt-dlp moved to `iTerminal.Widgets` in #822 and reaches `PackageManagerProcess` through `InternalsVisibleTo` ([AssemblyInfo.cs:4](../../iTerminal.Core/AssemblyInfo.cs)). A shared base in `iTerminal.Core` is visible to it the same way.
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

         The base becomes `public abstract`, because `public sealed` subclasses cannot derive from an `internal` base. No caller outside `iTerminal.Core` uses these classes, and `InstantiateAllTypesTest` skips abstract types ([InstantiateAllTypesTest.cs:62](../../iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs)). Move `FindOnPath` out of the git class as part of finding 48.
    - **Copies:** 4 classes (3 copies of the base), 1 copied plan record; about 300 lines would go (63 lines of `GitInstallPlan` and about 80 per class). Drifted: yes (exit codes, localization).
    - **Effort:** S for step 1, M for step 2.
    - **Risk of the change:** a changed install plan or exit-code rule. `GitRuntimeDependencyTest` (it asserts on `GitInstallPlan`, which moves to `SystemPackageInstallPlan`), `FfmpegRuntimeDependencyTest`, `YtDlpRuntimeDependencyTest`, `RarRuntimeDependencyTest` and `RuntimeDependencyServiceTest` cover each class through the existing constructor seams.

43. **Nine hand-written process runs beside `IExternalProcessRunner` differ on UTF-8, standard input and cancellation** — **Medium**, kind: duplication, consistency, testability.
    - **Where:**
      - The shared runner: [ExternalProcessRunner.cs:12](../../iTerminal.Core/Services/Processes/ExternalProcessRunner.cs). It drains both pipes, reads UTF-8 (`:143`), kills the process tree on its 30 s timeout, and rethrows when the caller cancels (`:66`). It is injected in 30 files.
      - [GitService.cs:461](../../iTerminal.Core/Services/Git/GitService.cs) (the Git client dialog) is the same method again, down to `GIT_TERMINAL_PROMPT` (`:479`) and the 30 s timeout. It sets no output encoding, and it returns "git command timed out" when the caller cancels (`:498` to `:503`). The git drive beside it uses the shared runner. Because `GitService` builds its process itself, its 13 tests need a real git (`GitServiceTest`).
      - [PackageManagerProcess.cs:23](../../iTerminal.Core/Services/PackageManager/PackageManagerProcess.cs): the same again with a timeout parameter, and the same "timed out" answer for a cancellation (`:53` to `:58`). It has 7 callers (apt, WinGet, yt-dlp).
      - Five version probes with a 5 s timeout: [GitRuntimeDependency.cs:225](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs), [FfmpegRuntimeDependency.cs:255](../../iTerminal.Core/Services/RuntimeDependencies/FfmpegRuntimeDependency.cs), [SystemToolRuntimeDependency.cs:206](../../iTerminal.Core/Services/RuntimeDependencies/SystemToolRuntimeDependency.cs), [PowerShellRuntimeDependency.cs:329](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs), and [OllamaRuntimeDependency.cs:257](../../iTerminal.Core/Services/RuntimeDependencies/OllamaRuntimeDependency.cs), which blocks in `WaitForExit(3000)` with no token. The git and ffmpeg probes redirect standard error but never read it.
      - [DefenderScanService.cs:55](../../iTerminal.Core/Services/Defender/DefenderScanService.cs): the same capture without a timeout.
      - Added in this pass (2026-11-24): `RunTerminalCommandAsync`, the command-line fallback used when no shell session is available (called from [ExecuteCommandTask.cs:85](../../iTerminal.Core/Tasks/ExecuteCommandTask.cs)), in [ContentModelFileSystem.cs:458](../../iTerminal.Core/Model/ContentModels/ContentModelFileSystem.cs). It is synchronous behind an `async` name, calls `WaitForExit()` with no timeout or token (`:507`) and reads standard output and standard error only after the exit (`:510` and `:511`), and redirects standard input without closing it (`:498`). It starts `powershell` for a `.ps1` on Linux (`:478`), where the drive actions look for `pwsh` ([DriveActionsService.cs:342](../../iTerminal.Core/Services/Drives/DriveActionsService.cs)).
    - **Problem:** each copy settles the same questions differently.
      - Encoding: on Windows, a non-ASCII commit message or author shown by the Git client is decoded in the console code page, because nothing in the app sets the console to UTF-8.
      - Cancellation: closing the Git client or the package manager mid-command is reported as a timeout.
      - Standard input: the probes redirect it and close it, so a tool that waits for input ends. The shared runner does not redirect it unless a writer is given (`:140`), so its child inherits the terminal that the TUI is reading.

            - Deadlock: the command-line fallback waits for the exit before it drains the pipes, so a command that writes more than the pipe buffer (64 KB on Linux) blocks for ever, and nothing can cancel it.

      Which copy a caller gets decides its behaviour. Principle: one solution per problem; testability seams. The mis-decoding, the inherited input and the fallback's hang are **unverified** at run time.
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
    - **Copies:** 9 (GitService, PackageManagerProcess, five probes, Defender, the command-line fallback) in 9 files; about 340 lines would go. Drifted: yes (encoding, cancellation, stdin, stderr drained or not, output read before or after the exit). The fallback in `ContentModelFileSystem` is the most urgent copy: step 4 (S) makes it a call to the runner with the token of `ExecuteCommandTask` (`BackgroundTask.CancellationToken`) and no timeout.
    - **Effort:** M in total.
    - **Risk of the change:** the Git client's output changes from the console code page to UTF-8, which is what git writes. A tool that relied on reading the terminal (none was found among the runner's callers) would see end of input. `GitServiceTest` keeps its real-git tests and gains fakes. `GitDriveServiceTest`, `DockerDriveServiceTest`, the package-manager tests and the `*RuntimeDependencyTest` classes (which inject `readVersion`) cover the callers.

44. **Fourteen dialogs carry the same private `Put`, and about 24 private clip helpers sit beside `TextWidth.Fit`** — **Medium**, kind: duplication, consistency.
    - **Where:**
      - `private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)`, which writes one row inside the border. Compared by hash, four variants:
        - byte-identical in 8 dialogs: [AccessControlDialog.cs:920](../../iTerminal.Core/Dialogs/AccessControlDialog.cs), [ContactManagerDialog.cs:647](../../iTerminal.Core/Dialogs/Contacts/ContactManagerDialog.cs), [EnvironmentVariablesDialog.cs:916](../../iTerminal.Core/Dialogs/EnvVars/EnvironmentVariablesDialog.cs), [MailDialog.cs:1143](../../iTerminal.Core/Dialogs/Mail/MailDialog.cs), [PasswordManagerDialog.cs:648](../../iTerminal.Core/Dialogs/PasswordManager/PasswordManagerDialog.cs), [OnlineRadioDialog.cs:870](../../iTerminal.Widgets/Dialogs/Radio/OnlineRadioDialog.cs), [RemoteConnectionManagerDialog.cs:822](../../iTerminal.Core/Dialogs/Remote/RemoteConnectionManagerDialog.cs) and [ToDoListDialog.cs:670](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs);
        - the same with `string` for `var` in 3: [PrintDialog.cs:625](../../iTerminal.Core/Dialogs/PrintDialog.cs), [ScannerDialog.cs:774](../../iTerminal.Core/Dialogs/Scanning/ScannerDialog.cs) and [StreamsDialog.cs:690](../../iTerminal.Core/Dialogs/StreamsDialog.cs);
        - with an ellipsis in 2: [WorkspaceManagerDialog.cs:750](../../iTerminal.Core/Dialogs/Workspaces/WorkspaceManagerDialog.cs) and [TorrentManagerDialog.cs:706](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs);
        - with a row-bounds check in 1: [CalendarDialog.cs:602](../../iTerminal.Core/Dialogs/Calendar/CalendarDialog.cs) ("a row below the inner area would be drawn on the bottom border").

        All 14 cut by `string.Length`, not by columns.
      - The column-aware helper already exists: [TextWidth.cs:95](../../iConsole/Display/TextWidth.cs) (`Fit`). It is called 4 times. Its sibling for the split-pane dialogs is [SplitPaneDialog.cs:73](../../iTerminal.Core/Dialogs/SplitPaneDialog.cs) (`WriteLine`).
      - Private clip helpers, one or two lines each:
        - `Clip` in 6 dialogs, for example [MailDialog.cs:1158](../../iTerminal.Core/Dialogs/Mail/MailDialog.cs). The two copies in `TorrentManagerDialog` and `StreamsDialog` that had no caller were removed in #840.
        - `Ellipsize` in 4 places, among them [Toast.cs:79](../../iConsole/Controls/Toast.cs) and [SplitPaneDialog.cs:71](../../iTerminal.Core/Dialogs/SplitPaneDialog.cs). Mail and Remote Connections each hold both a `Clip` and an `Ellipsize` that differ.
        - `Fit` in 13 places, for example [ComboBox.cs:294](../../iConsole/Controls/ComboBox.cs), [PopupListView.cs:201](../../iConsole/Controls/PopupListView.cs) and [ImageFileViewerDialog.cs:602](../../iTerminal.Core/Dialogs/Video/ImageFileViewerDialog.cs).
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

      Each of the 14 `Put` bodies becomes a call to `WriteInnerRow`; `Clip` and `Ellipsize` become `TextWidth.Ellipsize`. Both new members are additive. A dialog outside the solution that already has a member of that name gets a hiding warning, not a break (the `dotnet-breaking-changes` concern), so a distinct name is used rather than `Put`.
    - **Copies:** 14 `Put` copies (about 196 lines, down to 14 calls) and about 24 clip helpers in 22 files (two unused copies went in #840). One of the 14 copies, in `OnlineRadioDialog`, is now in `iTerminal.Widgets`, so the shared member belongs in `iConsole`'s `Dialog`, which both projects see. Drifted: yes (bounds check, ellipsis).
    - **Effort:** M (S for the two members; S per dialog).
    - **Risk of the change:** a row that was drawn on the border is no longer drawn, and a wide character now takes two columns. The `ScreenBuffer` tests of each dialog (`ContactManagerDialogTest`, `MailDialogTest`, `TorrentManagerDialogTest`, `CalendarDialogTest` and the others) compare the drawn text, and the end-to-end `DialogsTest` opens each dialog.

45. **The contact manager, the environment-variable editor and the radio station list are one dialog written three times** — **Medium**, kind: duplication.
    - **Where:**
      - The same `private enum Mode { Browse, Form, Filter, ConfirmDelete }`: [ContactManagerDialog.cs:53](../../iTerminal.Core/Dialogs/Contacts/ContactManagerDialog.cs), [EnvironmentVariablesDialog.cs:49](../../iTerminal.Core/Dialogs/EnvVars/EnvironmentVariablesDialog.cs) and [OnlineRadioDialog.cs:47](../../iTerminal.Widgets/Dialogs/Radio/OnlineRadioDialog.cs).
      - The same delete question: `case Mode.ConfirmDelete` at `ContactManagerDialog.cs:130`, `EnvironmentVariablesDialog.cs:298` and `OnlineRadioDialog.cs:152`.
      - The same filter editor, 53 to 57 lines (`HandleFilterKey` at `:306`, `:508` and `:351` of the same three files).
      - The same form editor, 72 to 73 lines (`HandleFormKey` at `:231`, `:417` and `:276`). The contact and radio copies are identical; the environment-variable copy adds a locked name field.
      - The same `Fit` and `Put` (finding 44).
    - **Drift:** the environment-variable filter caps typed text at a literal `200` ([EnvironmentVariablesDialog.cs:550](../../iTerminal.Core/Dialogs/EnvVars/EnvironmentVariablesDialog.cs)), not at its `MaxFieldLength` of 4 000. Its paste has no cap at all (`:519`). The contact and radio copies cap both with `MaxFieldLength` (`ContactManagerDialog.cs:320` and `:352`, `OnlineRadioDialog.cs:365` and `:397`). The radio dialog moved to `iTerminal.Widgets` in #822, so the three copies are now in two projects.
    - **Problem:** a change to how a field edits (a new key, Ctrl+Left, wide characters, an undo) is made three times or drifts, as the paste cap already has. The same y/n delete question is hand-written in To-Do ([ToDoListDialog.cs:222](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs)) and Calendar ([CalendarDialog.cs:172](../../iTerminal.Core/Dialogs/Calendar/CalendarDialog.cs)) too. Principle: DRY. This is the code side of design review finding 68 and of the Tui comparison item 72 ("form bound to a model").
    - **Improvement:** since #835 the target exists in the code. The remote connection manager's edit form is a `FormPanel` of `TextBox` fields ([RemoteConnectionManagerDialog.cs:119](../../iTerminal.Core/Dialogs/Remote/RemoteConnectionManagerDialog.cs)), so the text editing, paste and caps are the `TextBox`'s. Do the same in the three dialogs, one per pull request, starting with the environment-variable editor (the copy that drifted): the form becomes a `FormPanel`, the filter a single `TextBox` with `MaxLength`, and `HandleFormKey` and `HandleFilterKey` go. This revises the step the pass of 2026-11-22 proposed. The line editor below is the fallback only if a dialog cannot host child controls:

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
    - **Risk of the change:** a key that one dialog handled differently changes, and the focus moves into child controls, so the `Dialog` Esc rule and the footer hints must follow (#835 shows how). `ContactManagerDialogTest`, `EnvironmentVariablesDialogTest` and `OnlineRadioDialogTest` (now in `iTerminal.Widgets.Test`), and the end-to-end `EnvVarsFooterTest`, type into the fields.

46. **Three decoders and three binary checks for "text of unknown encoding" give three different answers** — **Medium**, kind: duplication, consistency.
    - **Where:**
      - Decoding:
        - [TextEncodings.cs:127](../../iConsole/Helper/TextEncodings.cs) reads the byte-order mark, then UTF-8, then the system's ANSI code page (`:160`). The F3 viewer, the CSV, Markdown and HAR viewers and the format readers use it (10 callers).
        - [PlainTextExtractor.cs:66](../../iTerminal.Core/Documents/Extractors/PlainTextExtractor.cs), for the document index, falls back to Latin-1 instead (`:89`).
        - [FileDiffDialog.cs:255](../../iTerminal.Core/Dialogs/FileDiffDialog.cs), for file comparison, also falls back to Latin-1 (`:275`).
      - Binary checks:
        - [FileDiffDialog.cs:246](../../iTerminal.Core/Dialogs/FileDiffDialog.cs) looks for a NUL in 8 192 bytes;
        - [FilePreviewService.cs:332](../../iTerminal.Core/Services/Preview/FilePreviewService.cs) looks for a NUL or more than a tenth control characters in 8 000 bytes;
        - [FileSearch.cs:332](../../iTerminal.Core/Services/Search/FileSearch.cs) looks for a NUL in 8 000 bytes.

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
      - [WeatherJob.cs:15](../../iTerminal.Widgets/Jobs/WeatherJob.cs): a static `HttpClient` with the default 100 s timeout and no User-Agent. The same forecast endpoint is called at `:96` and parsed at `:127`.
      - [OpenMeteoForecastProvider.cs:44](../../iTerminal.Widgets/Services/Weather/OpenMeteoForecastProvider.cs): the Weather dialog's client, with a 15 s timeout and a User-Agent. Its `FetchAsync` (`:49`) already returns `CurrentConditions` ([WeatherModels.cs:18](../../iTerminal.Widgets/Services/Weather/WeatherModels.cs)).
      - [iTerminal.Test/TerminalApp.cs:38](../../iTerminal.Test/TerminalApp.cs) omits `WeatherJob`, while the provider has a sandbox replacement (`:243`).
    - **Problem:** two clients and two parsers for one API have already drifted on timeout and identification. The job has no seam, so the end-to-end suite cannot run it, and the status-bar weather is untested in the app. Principle: DRY, testability seams.
    - **Improvement:** let `WeatherJob` take `IForecastProvider` and read `Forecast.Current`, or add `Task<CurrentConditions> FetchCurrentAsync(SavedPlace, CancellationToken)` to the interface if the full forecast is too heavy for a 15-minute poll. Keep the IP lookup in the job behind its own small interface, so the sandbox can answer both.
    - **Copies:** 2 clients, 2 parsers; about 40 lines would go. Drifted: yes (timeout, User-Agent).
    - **Effort:** S.
    - **Risk of the change:** the status bar's request gets the provider's 15 s timeout. `WeatherJobTest` tests the parser it would lose and needs a fake provider. The job could then be run in the end-to-end suite against `SandboxServices.WeatherForecasts`.

48. **Six PATH searches, and the shared one lives in the git dependency** — **Low**, kind: duplication, consistency.
    - **Where:**
      - [GitRuntimeDependency.cs:198](../../iTerminal.Core/Services/RuntimeDependencies/GitRuntimeDependency.cs) (`FindOnPath`, with `PATHEXT` and quoted entries) is used by ffmpeg, PowerShell, rar, unrar and yt-dlp, by `YtDlpYouTubeService`, and by the microphone registration ([iTerminalApplicationBuilderExtension.cs:657](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs)).
      - Its siblings:
        - [ShellLaunch.cs:87](../../iTerminal.Core/Services/Shell/ShellLaunch.cs), with no `PATHEXT`, so its callers pass `pwsh.exe` themselves;
        - [ElevationService.cs:282](../../iTerminal.Core/Services/Elevation/ElevationService.cs), which does not strip quotes;
        - [RarArchiveWriter.cs:153](../../iTerminal.Core/Services/Archives/RarArchiveWriter.cs) (`RarTools.Find`) and [SevenZipArchiveWriter.cs:34](../../iTerminal.Core/Services/Archives/SevenZipArchiveWriter.cs);
        - [DriveActionsService.cs:339](../../iTerminal.Core/Services/Drives/DriveActionsService.cs), which returns the bare name, not the path (`:351`).
    - **Problem:** whether a program "is installed" depends on which feature asks. A quoted PATH entry on Windows finds `pwsh` for the shell but not for elevation. A helper used by the speech registration sits in the git dependency class. Principle: DRY, cohesion.
    - **Improvement:** `internal static class ExecutableSearch { public static string? Find(string program); public static string? FindAny(params string[] names); }` in `iTerminal.Core/Helper`, with the git copy's rules (quotes, `PATHEXT`, a malformed entry skipped). The other five call it, and the remaining delegate parameters take `ExecutableSearch.Find`.
    - **Copies:** 6 in 6 files; about 90 lines would go. Drifted: yes (quotes, `PATHEXT`, return value).
    - **Effort:** S.
    - **Risk of the change:** the elevation check starts finding `sudo` in a quoted entry. `ElevationServiceTest`, `ShellLaunchTest` and the archive-writer tests show any change.

49. **Six stream-copy loops with progress beside `StreamCopyHelper`** — **Low**, kind: duplication.
    - **Where:** [StreamCopyHelper.cs:12](../../iTerminal.Core/Helper/StreamCopyHelper.cs) (`CopyWithProgressAsync`, used by the local, memory and archive drives). The same eight-line loop is written out at [HttpFileSystemServiceBase.cs:197](../../iTerminal.Core/Services/Cloud/HttpFileSystemServiceBase.cs), [WebDavFileSystemService.cs:457](../../iTerminal.Core/Services/WebDavFileSystemService.cs), [ShellMtpBackend.cs:194](../../iTerminal.Core/Services/Mtp/ShellMtpBackend.cs) and `:219`, [RecycleBinService.cs:199](../../iTerminal.Core/Services/Recycle/RecycleBinService.cs) and [EvidenceNotebookService.cs:266](../../iTerminal.Core/Services/Evidence/EvidenceNotebookService.cs).
    - **Problem:** the copies report no initial 0 and use a 64 KB buffer where the helper uses 128 KB. A change to progress pacing (design review finding 17 asks for long copies to report through `TaskManager`) has seven places to go. Principle: DRY; the helper exists.
    - **Improvement:** replace each loop with `await StreamCopyHelper.CopyWithProgressAsync(source, destination, progress, cancellationToken, 1 << 16);`. The SMB loop ([SmbFileSystemService.cs:548](../../iTerminal.Core/Services/SmbFileSystemService.cs)) writes in the server's chunk size and stays as it is.
    - **Copies:** 6 in 5 files; about 45 lines would go. Drifted: slightly (the initial report).
    - **Effort:** S.
    - **Risk of the change:** progress gets one more report at 0. The cloud and WebDAV service tests, `ContentModelCopyPipelineTest` and `FileTransferTest` cover the copies.

50. **Sizes and durations are formatted in about 15 private copies beside the two shared helpers** — **Low**, kind: duplication, consistency.
    - **Where:**
      - Sizes: [FileSizeHelper.cs:5](../../iConsole/Helper/FileSizeHelper.cs) (`ToFileSize`, 32 users) formats every value without decimals, because all three branches of `ThreeNonZeroDigits` (`:26`) use `"0"` (1.5 MB shows as "2 MB"). Five dialogs and models wrote their own one-decimal version, with different unit lists and decimal rules:
        - [DiskManagerDialog.cs:479](../../iTerminal.Core/Dialogs/DiskManagerDialog.cs);
        - [DocumentManagerDialog.cs:933](../../iTerminal.Core/Dialogs/Documents/DocumentManagerDialog.cs);
        - [MusicFileViewerDialog.cs:615](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs);
        - [TorrentManagerDialog.cs:587](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs), which uses `0.0`;
        - [WebBrowserModel.cs:1021](../../iTerminal.Core/Model/Web/WebBrowserModel.cs), which stops at MB.

        A one-line `Megabytes` is in four files ([VerifiedDownload.cs:82](../../iTerminal.Core/Services/RuntimeDependencies/VerifiedDownload.cs), `GitHubReleaseFeed.cs`, `NuGetPackageFeed.cs`, `OllamaServer.cs`). [FilePropertiesService.cs:263](../../iTerminal.Core/Services/Properties/FilePropertiesService.cs) is the copy that builds on the shared helper.
      - Durations: [ProgressGroup.cs:302](../../iConsole/Controls/Progress/ProgressGroup.cs) (`FormatDuration`, public) is written again in [MusicFileViewerDialog.cs:603](../../iTerminal.Core/Dialogs/Music/MusicFileViewerDialog.cs), [VideoFileViewerDialog.cs:557](../../iTerminal.Core/Dialogs/Video/VideoFileViewerDialog.cs), [ContentModelMusicBase.cs:197](../../iTerminal.Core/Model/ContentModels/ContentModelMusicBase.cs), [YouTubeDialog.cs:410](../../iTerminal.Widgets/Dialogs/YouTube/YouTubeDialog.cs), [FilePreviewService.cs:488](../../iTerminal.Core/Services/Preview/FilePreviewService.cs) and [OnlineRadioDialog.cs:863](../../iTerminal.Widgets/Dialogs/Radio/OnlineRadioDialog.cs). A zero length shows as `--:--` in the music copies, `0:00` in YouTube and preview, and `00:00` in the radio.
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
    - **Where:** `Get(key)`, `JsonSerializer.Deserialize`, `catch (JsonException)`, then `Set` and `Write()`, in [ToDoTask.cs:275](../../iTerminal.Core/Model/ToDo/ToDoTask.cs), [Contact.cs:147](../../iTerminal.Core/Model/Contacts/Contact.cs), [Meeting.cs:218](../../iTerminal.Core/Model/Calendar/Meeting.cs), [MailModels.cs:106](../../iTerminal.Core/Model/Mail/MailModels.cs), [RadioStationStore.cs:147](../../iTerminal.Widgets/Model/Radio/RadioStationStore.cs), [WebBrowserStore.cs:150](../../iTerminal.Core/Services/Web/WebBrowserStore.cs), [WorkspaceService.cs:190](../../iTerminal.Core/Services/Workspaces/WorkspaceService.cs), [WatchStore.cs:172](../../iTerminal.Core/Services/Watch/WatchStore.cs), [TorrentService.cs:458](../../iTerminal.Core/Services/Torrents/TorrentService.cs), [RemoteConnectionService.cs:251](../../iTerminal.Core/Services/Remote/RemoteConnectionService.cs), [StockService.cs:174](../../iTerminal.Widgets/Services/Stocks/StockService.cs), [PluginManagerService.cs:366](../../iTerminal.Core/Services/Plugins/PluginManagerService.cs), [NetworkProfileService.cs:56](../../iTerminal.Core/Services/NetworkProfileService.cs), [MapStore.cs:106](../../iTerminal.Widgets/Services/Map/MapStore.cs), [FileAssociationService.cs:153](../../iTerminal.Core/Services/FileAssociations/FileAssociationService.cs) and [ForecastService.cs:235](../../iTerminal.Widgets/Services/Weather/ForecastService.cs). The last one already has a generic `Load<T>`. Some copies log a damaged value and some do not.
    - **Problem:** each store decides again what a damaged value means, whether to log it, and whether to clear it. Finding 33 shows that where user data is stored decides whether it survives an upgrade, and today that decision is spread over 16 classes. Principle: DRY, one place per decision.
    - **Improvement:** one helper, beside `ISettingsService` rather than on it, so the interface does not change:

      ```csharp
      internal static class SettingsJson
      {
          public static T? Read<T>(ISettingsService settings, string key, JsonSerializerOptions? options = null, ILogger? logger = null);
          public static void Write<T>(ISettingsService settings, string key, T value, JsonSerializerOptions? options = null);
      }
      ```

      Move the stores one at a time, starting with `ForecastService` ([ForecastService.cs:235](../../iTerminal.Widgets/Services/Weather/ForecastService.cs)), whose `Load<T>` is the model. Four of the 16 stores (`ForecastService`, `StockService`, `MapStore`, `RadioStationStore`) moved to `iTerminal.Widgets` in #822; an `internal` helper in `iTerminal.Core` still reaches them through the existing `InternalsVisibleTo`. Keep each store's own clean-up of null entries.
    - **Copies:** 16 in 16 files; about 120 lines would go. Drifted: slightly (logging, clearing).
    - **Effort:** S per store, M in total.
    - **Risk of the change:** a store that threw on a damaged value now starts empty. Each store's test (`ToDoTaskTest`, `ContactTest`, `RadioStationStoreTest`, `WorkspaceServiceTest` and the others) covers its load path.

53. **The folder-URL idiom is written 13 times, three times as the join `UrlPath.Child` exists to replace** — **Low**, kind: duplication, consistency.
    - **Where:**
      - "Add a trailing slash unless there is one" as a one-line expression in 10 places, among them [TransferCommand.cs:165](../../iTerminal.Core/Commands/TransferCommand.cs) (the only one that also accepts `\`), `:237` and `:336`, [ContentModelRemoteDrive.cs:74](../../iTerminal.Core/Model/ContentModels/ContentModelRemoteDrive.cs), [MemoryFolder.cs:189](../../iTerminal.Core/Model/ContentModels/MemoryFolder.cs), [FtpFileSystemService.cs:202](../../iTerminal.Core/Services/FtpFileSystemService.cs) and [WebDavFileSystemService.cs:271](../../iTerminal.Core/Services/WebDavFileSystemService.cs).
      - The join that `CLAUDE.md` forbids, `FullUrl.TrimEnd('/') + "/" + name`, at [SmbFileSystemService.cs:192](../../iTerminal.Core/Services/SmbFileSystemService.cs) and `:352`, and [IPhoneFileSystemService.cs:210](../../iTerminal.Core/Services/IPhoneFileSystemService.cs). The helper is [UrlPath.cs:40](../../iTerminal.Core/Helper/UrlPath.cs), with 21 callers.
    - **Problem:** the three joins are correct today only because an SMB share and an iPhone always have a segment after the scheme. The rule exists because the same join broke `memdrive://`. The trailing-slash copies disagree on `\`. Principle: consistency with the framework; one way to build a child URL.
    - **Improvement:** add `public static Url AsFolder(Url url)` to `UrlPath`, with the `TransferCommand` rule for both separators, and replace the 10 expressions. Replace the three joins with `new Url(UrlPath.Child(url.FullUrl, name, isFolder))`.
    - **Copies:** 13 in 10 files; one line each. Drifted: yes (the `\` case).
    - **Effort:** S.
    - **Risk of the change:** a Windows path ending in `\` gets no extra `/`. `UrlPathTest`, `ContentModelSmbDriveTest`, the memory-drive tests and `FileTransferTest` cover the paths.

**Candidates checked and not reported**

- The 88 `ShowDialogCommand<T>` subclasses (most are 13 or 14 lines) differ in key, title, description and `CanExecute`, and the base class already holds the shared part. Their construction cost is finding 32.
- The remote and cloud drive models are already factored: each subclass of `ContentModelRemoteDrive` is 32 to 64 lines and only resolves credentials, and the three cloud services share `HttpFileSystemServiceBase`, including its throttling retry ([HttpFileSystemServiceBase.cs:86](../../iTerminal.Core/Services/Cloud/HttpFileSystemServiceBase.cs)).
- The two `ProgressStream` classes ([ProgressStream.cs:7](../../iTerminal.Core/Helper/ProgressStream.cs) and the private one in `WebDavFileSystemService`) look alike but count writes and reads respectively; merging them would join two things that change for different reasons.
- The ffmpeg processes in `iConsole.Media.Audio` and `iConsole.Media.Video` stream for the life of a playback. They are not run-to-completion calls and are outside finding 43.
- The three file-name sanitisers ([EvidenceNotebookService.cs:427](../../iTerminal.Core/Services/Evidence/EvidenceNotebookService.cs), [WebBrowserModel.cs:993](../../iTerminal.Core/Model/Web/WebBrowserModel.cs), [RenderingDumpHelper.cs:198](../../iConsole/Helper/RenderingDumpHelper.cs)) differ in what they keep. They are short, and the web copy's caller handles the empty result. Use the evidence copy if a fourth is ever needed.
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

Reviewed on 2026-11-22, scope "Rendering pipeline". No review had covered this scope; the caller asked for a full review, and the whole-repository pass of 2026-11-21 had already refreshed the other scopes a day earlier, so this run takes the one scope never reviewed. Branch `docs/architecture-review-rendering-pipeline`, cut from `main` at `b9b81269`. Read: `CLAUDE.md`, `ui-framework.md`, `ui-framework/rendering-pipeline.md`, the profiling and design-review overviews; in the source `ControlRendererJob`, `ConsoleBufferRendererJob`, `Job`, `Desktop`, `IDesktop`, `Buffer`, `BufferLayer`, `IBuffer`, `OffscreenBuffer`, `RenderingDumpHelper`, `Cursor`, `Position`, the render and invalidation members of `Control`, `IConsoleDriver` and the two drivers' frame methods, `TerminalScreen.OnWindowSizeChanged`, the rendering registrations in `iConsoleApplicationBuilderExtension`, and the names of the rendering tests in `iConsole.Test/Jobs` and `iConsole.Test/Buffers`. Run: `dotnet list iTerminal.slnx package` with `--vulnerable --include-transitive`, `--outdated` and `--deprecated` (read-only, nuget.org reachable), to check that the "Dependencies and packages" section still holds: no advisory, `OpenMcdf` 3.4.0 the only update (finding 25), `imobiledevice-net` still deprecated (finding 4); that section is not rewritten. Not done: no build, no test run, no run-time check, no render dump, no resize or clock change tried on a running app. Re-checked on 2026-11-24 in the whole-repository pass at `c5c693b3`: every open finding below still holds, and its file and line references were moved to that commit (files that #822 moved now point into `iTerminal.Widgets`). The rendering files changed only through the warning fixes of #840, #841 and this branch; no finding changed in substance.

**Summary**

- The two render jobs split the work well (compose on one thread, diff and write on the other), and the diff writer is careful: byte compare per row, runs of changed cells per write, synchronized output, Sixel under the same lock.
- The largest structural risk: on a terminal resize the writer thread lays the control tree out and renders the whole screen, while the control renderer draws into the same layers without the lock (finding 54). The lock rule in `CLAUDE.md` is kept on the resize side only.
- The writer loop times its resize check with the wall clock, so after the clock goes back an hour, window resizes are not seen for an hour (finding 55).
- Nothing tells the loops that something changed. Four loops poll, and two independent one-second fallbacks repaint the whole screen (finding 56, the structure behind profiling item 1).
- An exception in `Draw` is swallowed in five places under four policies, and none reaches the log in a release build (finding 57).

**Findings**

54. **On a resize the writer thread lays out and renders the control tree, beside the control renderer, and `Buffer.SyncLock` does not cover the writes** — **Medium**, kind: concurrency.
    - **Where:** [ConsoleBufferRendererJob.cs:209](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) (`DetectWindowSizeChanges`, on the writer thread) resizes the buffer under the lock (line 225) and then raises `WindowSizeChanged` (line 241). [Desktop.cs:270](../../iConsole/Desktops/Desktop.cs) handles it: `Screens[0].OnWindowSizeChanged`, which in [TerminalScreen.cs:180](../../iTerminal.Core/Screens/TerminalScreen.cs) sets `ViewSize` and runs the whole layout, then `RenderInitialState` ([Desktop.cs:276](../../iConsole/Desktops/Desktop.cs)), which calls `Buffer.UpdateLayers` and `screen.Render(Buffer)` for the whole tree. At the same time `ControlRendererJob` calls `UpdateLayers` ([ControlRendererJob.cs:81](../../iConsole/Jobs/ControlRendererJob.cs)) and `control.Render` (line 101) outside the lock; it takes the lock only around `Merge` (line 130) and the layer wipe (line 180). `Buffer.Write` resizes the layer it writes to without the lock ([Buffer.cs:140](../../iConsole/Buffers/Buffer.cs)), and `BufferLayer.Resize` replaces `Size` and `Content` in two steps ([BufferLayer.cs:88](../../iConsole/Buffers/BufferLayer.cs)). [rendering-pipeline.md:13](../architecture/ui-framework/rendering-pipeline.md) says the lock "guards the layer array", which `UpdateLayers` changes without it.
    - **Problem:** two threads draw into the same layers and one of them moves controls while the other draws them. The rule "don't mutate the control tree from a worker thread" is broken by the framework itself, and the code knows it: the comment at [ConsoleBufferRendererJob.cs:234](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) says the layout handler can throw because "the tree can change under it", and retries it up to five times. A user who resizes the window can see a frame mixed from the old and new layout until the next full repaint, and an `IndexOutOfRangeException` in a write lands in an empty `catch` (finding 57). Principles: ownership (one thread owns the tree), explicit contracts.
    - **Improvement:** keep the writer thread to "measure and report". It records the new size in `_layoutPending` (already there) and stops; `ControlRendererJob` applies it at the start of its next pass, on the thread that owns the tree:

      ```csharp
      // IDesktop (iConsole), called by ControlRendererJob at the top of Render()
      void ApplyPendingResize();   // Buffer.Resize under SyncLock, screen.OnWindowSizeChanged, then RepaintFromScratch
      ```

      `ConsoleBufferRendererJob` keeps reading `MergedLayer` under the lock and keeps its own `LastBuffer` resize (line 281). Then correct the sentence on the architecture page: the lock guards the merged layer and the layer set against the writer; drawing is single-threaded by design.
    - **Effort:** M.
    - **Risk of the change:** a resize that is applied one pass later (about a millisecond). The resize tests in `ConsoleBufferRendererJobRunTest` and `DesktopTest`, which drive `WindowSizeChanged` directly, need to move to the new entry point; the end-to-end suite resizes the virtual terminal.
    - **unverified:** no torn frame or exception was observed; the race is read from the code.

55. **The writer loop uses the wall clock, so after the clock goes back the resize check stops** — **Medium**, kind: consistency.
    - **Where:** [ConsoleBufferRendererJob.cs:116](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) and line 128 take `DateTime.Now`; `DetectWindowSizeChanges` returns early while `now - _lastWindowSizeCheck < 100 ms` (line 211). This is the only place that notices a resize (`RefreshWindowSize()` is called from here and once in `Desktop.RunAsync`). The sibling job uses `Environment.TickCount64` ([ControlRendererJob.cs:22](../../iConsole/Jobs/ControlRendererJob.cs)), and `MainLoopWatchDog` uses `ITimeProvider` ([ITimeProvider.cs](../../iConsole/Services/ITimeProvider.cs)).
    - **Problem:** `DateTime.Now` is local time. When daylight saving ends (or the clock is set back), `now - _lastWindowSizeCheck` is negative for an hour, so for that hour a resized window keeps its old layout and the one-second fallback repaint (line 129) stops too. Lending the terminal (Ctrl+O) resets the check (line 96), which hides the fault for a user who happens to do that. Principle: consistency with the framework (two clocks in two sibling loops).
    - **Improvement:** use `Environment.TickCount64` for `lastRedraw`, `_lastWindowSizeCheck` and `_lastStatisticsDate`, as `ControlRendererJob` does, or take `ITimeProvider` through the constructor (it is registered already) so a test can move the clock back. `_lastWindowSizeCheck = DateTime.MinValue` becomes `long.MinValue / 2` or a "check now" flag.
    - **Effort:** S.
    - **Risk of the change:** none beyond the timing tests in `RendererJobRedrawTimingTest` and `ConsoleBufferRendererJobRunTest`; with `ITimeProvider` a new test can set the clock back and assert that a resize is still seen.
    - **unverified:** not reproduced by changing the clock of a running app.

56. **No change signal: four loops poll, and two independent one-second fallbacks repaint the whole screen** — **Medium**, kind: performance, consistency.
    - **Where:** profiling item 1 (measured: 25 % of a core and 6.9 kB/s of output at idle). `Thread.Sleep(1)` in [ControlRendererJob.cs:36](../../iConsole/Jobs/ControlRendererJob.cs), [ConsoleBufferRendererJob.cs:151](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) and [KeyboardEventPublisherJob.cs:45](../../iConsole/Jobs/KeyboardEventPublisherJob.cs); `Thread.Sleep(10)` on a thread-pool thread for the whole life of the app in [Desktop.cs:205](../../iConsole/Desktops/Desktop.cs). The fallbacks: `FullRedrawFallbackInterval` in [ControlRendererJob.cs:10](../../iConsole/Jobs/ControlRendererJob.cs) marks the whole tree changed, and the one in [ConsoleBufferRendererJob.cs:19](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) clears `LastBuffer` (line 187), so every cell is written again. The Sixel code has to work around the second one ([ConsoleBufferRendererJob.cs:404](../../iConsole/Jobs/ConsoleBufferRendererJob.cs)). On Windows the 1 ms sleeps need the machine-wide timer resolution raised for the life of the app ([Desktop.cs:72](../../iConsole/Desktops/Desktop.cs)). The tree version that the renderer compares is one process-wide static ([Control.cs:35](../../iConsole/Controls/Control.cs)). The tests that pin the fallback are named `TriggersRedrawEvery500ms` ([RendererJobRedrawTimingTest.cs:12](../../iConsole.Test/Jobs/RendererJobRedrawTimingTest.cs), line 34) for an interval of one second.
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
    - **Where:** [ControlRendererJob.cs:40](../../iConsole/Jobs/ControlRendererJob.cs) (empty `catch`); [Control.cs:371](../../iConsole/Controls/Control.cs) (`Debug.WriteLine`, compiled out of a release build; the call uses the `(message, category)` overload, so the text is never formatted); [Control.cs:430](../../iConsole/Controls/Control.cs) (written to the render dump, which is off unless `ITERMINAL_RENDER_DUMPS` is set); [ConsoleBufferRendererJob.cs:361](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) (`Debug.WriteLine`); [Job.cs:23](../../iConsole/Jobs/Job.cs) (empty `catch` around the whole job). The only diagnostic that reaches Serilog from the render path is the stall report: `MainLoopWatchDog` raises `Stalled` and `Recovered`, and [MainLoopWatchDogLoggingHostedService.cs](../../iTerminal.Core/Services/Diagnostics/MainLoopWatchDogLoggingHostedService.cs) logs them.
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
    - **Where:** `IRenderingDumpHelper` is not registered (no line in [iConsoleApplicationBuilderExtension.cs:86](../../iConsole/Application/iConsoleApplicationBuilderExtension.cs) or elsewhere), so `Desktop` ([Desktop.cs:40](../../iConsole/Desktops/Desktop.cs)) and `ConsoleBufferRendererJob` ([ConsoleBufferRendererJob.cs:77](../../iConsole/Jobs/ConsoleBufferRendererJob.cs)) each build their own. `Desktop.RunAsync` starts the session (line 152) and writes the initial-state dumps (line 168); then the render job starts, and its `OnStart` (line 100) starts a second session, which deletes every file in the folder ([RenderingDumpHelper.cs:54](../../iConsole/Helper/RenderingDumpHelper.cs)). The folder is the same for every process ([RenderingDumpHelper.cs:43](../../iConsole/Helper/RenderingDumpHelper.cs)). The dump text is built in two classes: five public static methods, about 150 lines, sit in [ConsoleBufferRendererJob.cs:597](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) to line 767 beside `RenderingDumpHelper`, and `MaxDumpHeight` and `MaxDumpWidth` are declared in both (line 22 and [RenderingDumpHelper.cs:16](../../iConsole/Helper/RenderingDumpHelper.cs)).
    - **Problem:** the developer who sets `ITERMINAL_RENDER_DUMPS=1` to debug a blank start-up screen does not get the `buffer-initial-state` files that exist for that case, and two instances append to one `session` trace under two different locks. The render job is a third of a frame-dump formatter. Principle: ownership (one instance per session), single responsibility.
    - **Improvement:** register `services.AddSingleton<IRenderingDumpHelper, RenderingDumpHelper>()` in `AddiConsoleCore` (both constructors already take it as an optional argument), and move the five static methods and the two constants into `RenderingDumpHelper`. Callers to move: [Desktop.cs:303](../../iConsole/Desktops/Desktop.cs) and nine calls in `iConsole.Test/Jobs/RenderingJobsTest.cs`.
    - **Copies:** 2 helper instances; 2 pairs of constants; about 150 lines move, none are deleted. Not drifted.
    - **Effort:** S.
    - **Risk of the change:** `RenderingJobsTest` (the dump formatting); a dump run by hand confirms that the initial-state files survive.
    - **unverified:** read from the start-up order, not from a dump folder.

60. **The desktop keeps a list of screens, but only the first is ever used** — **Low**, kind: interface.
    - **Where:** `Screens` is a `ConcurrentList<IScreen>` on `IDesktop` ([IDesktop.cs:16](../../iConsole/Desktops/IDesktop.cs)), filled only in the constructor ([Desktop.cs:60](../../iConsole/Desktops/Desktop.cs)). Key routing takes `screens[0]` (line 337), resize takes `Screens[0]` (line 272), and three prompt services do the same. `ControlRendererJob` loops over all screens ([ControlRendererJob.cs:76](../../iConsole/Jobs/ControlRendererJob.cs)) and merges each into the one `MergedLayer`, so a second screen would overwrite the first.
    - **Problem:** the type promises several screens and the pipeline supports one; a developer who adds a second gets a screen that is drawn and then overdrawn. Principle: explicit contracts.
    - **Improvement:** state the single-screen contract: add `IScreen Screen { get; }` to `IDesktop`, move the `First()` callers to it, and keep `Screens` (used by the screen manager dialog) with an XML comment that it holds one entry. Removing `Screens` is a breaking change and is not needed.
    - **Effort:** S.
    - **Risk of the change:** none in behaviour; the compiler finds the callers.

**What works well**

- The split of the two jobs: controls draw into per-Z-order layers on one thread, and the writer only reads the merged layer under `Buffer.SyncLock`, so the terminal is never written from a key handler.
- The diff writer: a SIMD byte compare per row, one write per run of changed cells, synchronized output bracketing each frame, and `LastBuffer` marked unknown (not blank) after a resize or a lend, so a matching blank never hides a cell ([ConsoleBufferRendererJob.cs:289](../../iConsole/Jobs/ConsoleBufferRendererJob.cs), line 93).
- Compositing by the cells each row actually wrote (`BufferLayer` row extents), with a comment that names the two bugs it fixed. `RepairWideCharacters` runs once on the merged layer, the one place it can see both halves.
- The Sixel overlay is drawn only while the cells under it are still the blank the control painted, so a toast or a menu over it never leaves pixels behind.
- `IConsoleDriver` is small, and the frame, graphics and cursor-shape members have default bodies, so a test driver needs only the basics. The loops that write to the terminal bracket each pass with `ConsoleOwnership`.
- The second start-up wave (background jobs after `FirstFrameRendered`) and the per-pass reuse of the collections in both jobs.

**Research cross-reference**

- Profiling overview, item 1: finding 56 is its structural side and gives the steps; the measured cost is the profiling item's. Ranked Medium here because it costs CPU and traffic, not data; it is still the first change to make in this scope.
- The whole-repository pass of 2026-11-21, research cross-reference (the per-root change signal and the static `Control._treeVersion`, [Control.cs:35](../../iConsole/Controls/Control.cs)): finding 56 step 5 is that decision, and it waits for finding 54 so that one thread owns the tree.
- Design review, finding 25 (folder selector rows cut once at a width and not again after a resize): a control-level resize gap; finding 54 does not fix it, but after 54 a resize reaches every control on the render thread, where a re-cut can run safely.
- Console features overview: synchronized output and diff rendering are recorded as built; this review agrees.

**Suggested order**

1. Quick wins, S: finding 55 (one clock), finding 59 (one dump helper), finding 60 (single-screen contract).
2. Finding 57 (render errors reach the log) before finding 54, so that the change of thread in 54 has a log that shows what breaks.
3. Finding 54 (resize on the render thread).
4. Finding 56, steps 1 and 2, then 3 and 4; step 5 after 54.
5. Finding 58 when the buffer types are touched anyway; it does not depend on the others.

## Interfaces and seams

Reviewed on 2026-11-24, scope "Interfaces and seams", within the whole-repository pass at `c5c693b3` on `fix/build-warnings-3` (see that section). No review had covered this scope. Read: the central contracts of `iConsole` (`IContentModel` with `IFileSystem`, `IContentModelService`, `IDesktop`, `IBuffer`, `IControl`, `ICommand`), the eight contracts in `iTerminal.Core/Extensions` with their one implementation each (`WidgetContributions.cs`) and their consumers (`ToolsMenuItem`, `HotKeys`, `TerminalMenuBar`, `UiService`, `StartupWarmUp`), `ISettingsService`, `IExternalProcessRunner`, and the tests that touch the contracts (`WidgetsApplicationBuilderExtensionTest`, `ToolsMenuItemTest`). Counted with `grep` across the eight product projects: the clock, process and HTTP seams, the literal panel names, and the members of each interface. Not done: no build, no test run, no run-time check.

**Summary**

- The seams to the outside world exist and are mostly used. Processes go through `IExternalProcessRunner` in 30 files, but 18 files of `iTerminal.Core` still start 39 processes themselves (finding 43 names the copies that matter). The 21 `HttpClient`s in 17 files each have a constructor seam (an injected client or handler), which is what the tests and the sandbox use; no `IHttpClientFactory` is needed for that. `DateTime.Now` and `UtcNow` appear 87 times in `iTerminal.Core` against 23 files that take `ITimeProvider` or `TimeProvider`; the one place where that causes a fault is finding 55.
- The large or leaky interfaces already have findings: `IFileSystem` and `IContentModel` (36), `IBuffer` (58), `IFileToTextConverterService` (20), `IUiService` (35), `IUpdateService` (28).
- New in this pass: the extension contracts of #824 carry built dialogs and a free-string menu group (finding 62), and a panel is named by a string defined in two classes (finding 64).

**Findings**

62. **The extension contracts hand over built dialogs and a free-string menu group, so one widget dialog exists three times and a mistyped group drops its entry without a word** — **Medium**, kind: interface, lifetime.
    - **Where:**
      - [IToolsMenuContribution.cs:11](../../iTerminal.Core/Extensions/IToolsMenuContribution.cs): `string Group`. Its comment (`:7`) lists five names; the menu knows six ([ToolsMenuItem.cs:93](../../iTerminal.Core/Controls/MenuBar/ToolsMenuItem.cs), with "media"), and two widgets use the sixth ([WidgetsApplicationBuilderExtension.cs:115](../../iTerminal.Widgets/Application/WidgetsApplicationBuilderExtension.cs) and `:116`). A contribution whose group is not in the dictionary is skipped ([ToolsMenuItem.cs:138](../../iTerminal.Core/Controls/MenuBar/ToolsMenuItem.cs)), and no test asserts that each contribution lands in a submenu (`WidgetsApplicationBuilderExtensionTest` has two tests, neither about groups).
      - Built commands, not factories: `AddToolsMenuEntry` resolves the dialog when the contribution is resolved ([WidgetsApplicationBuilderExtension.cs:141](../../iTerminal.Widgets/Application/WidgetsApplicationBuilderExtension.cs)), and the three hot keys resolve the same transient dialogs again (`:118` to `:123`; registrations at `:55`, `:83` and `:88`).
      - [IOpenableDialogContribution.cs:12](../../iTerminal.Core/Extensions/IOpenableDialogContribution.cs): `Dialog Resolve(IServiceProvider services)`. The only implementation calls `GetRequiredService` ([WidgetContributions.cs:23](../../iTerminal.Widgets/Application/WidgetContributions.cs)), so the contract passes the container to the plug-in, and [UiService.cs:72](../../iTerminal.Core/AI/UiService.cs) gets a new transient instance on every AI call.
    - **Problem:** the contracts exist so that a feature can plug in without Core naming it, but they copy the two weaknesses of finding 32 into a public seam. `TranslatorDialog`, `CurrencyConverterDialog` and `StockBrowserDialog` are built before the first frame, twice each, and a third time when the AI opens one, so the text typed into the translator from its hot key is not there when the menu or the AI opens it. A widget that writes "network" instead of "net" never appears in the menu, and nothing fails. `Resolve(IServiceProvider)` lets an implementer do anything with the container, which makes the contract hard to fake and hides what it needs. Principle: explicit contracts, ownership and lifetime, interface segregation.
    - **Improvement:** two steps, each one pull request. Both change only `iTerminal.Core.Extensions`, which has one implementer (`iTerminal.Widgets`) and no caller outside the solution, so nothing outside breaks.
      1. (S) Type the group and test it:

         ```csharp
         // before
         public interface IToolsMenuContribution { string Group { get; } ICommand Command { get; } }
         // after
         public enum ToolsMenuGroup { Files, Media, Network, Office, Games, System }
         public interface IToolsMenuContribution { ToolsMenuGroup Group { get; } ICommand Command { get; } }
         ```

         `ToolsMenuItem` keys its dictionary by the enum; the nine `AddToolsMenuEntry` calls change their second argument.
      2. (M) Share one command per dialog and resolve it on first use. Register each widget command once (`services.AddSingleton<TranslatorDialogCommand>()`), let the Tools entry and the hot key take that instance, and give the command the dialog factory of finding 32 (`IDialogFactory<TDialog>`) instead of the dialog. Replace `Resolve` by a type:

         ```csharp
         // before
         Dialog Resolve(IServiceProvider services);
         // after
         Type DialogType { get; }   // UiService resolves it once and runs the shared command
         ```

         Callers: 1 (`UiService`); implementations: 1 (`OpenableWidgetDialog<TDialog>`).
    - **Effort:** S for step 1, M for step 2.
    - **Risk of the change:** step 2 moves a dialog's construction to its first open, so a constructor that does I/O shows on the key path; the end-to-end `DialogsTest` opens the widget dialogs by key, and `ToolsMenuItemTest` and `TerminalMenuBarTest` (which build widget dialogs today) need the factory.

64. **A panel is named by a string, defined in two classes in `iTerminal.Core` and in none in the framework whose interface takes it** — **Low**, kind: interface, consistency.
    - **Where:** [IContentModelService.cs:10](../../iConsole/Interfaces/IContentModelService.cs) and `:11` (`string FocusedPanel`, `string LastFocusedPanel`), `:16`, `:17` and `:21` (`AddContentBrowser(string name, …)`, `AddContentModel(string name, Url url)`, `SetUrlAsync(string name, Url …)`). The values are defined twice, as `Content.Left`/`Right` ([Content.cs:17](../../iTerminal.Core/Controls/Contents/Content.cs), 20 uses) and `ContentModelService.Left`/`Right` ([ContentModelService.cs:478](../../iTerminal.Core/Services/ContentModelService.cs), 23 uses), and written out as `"RightPanel"` in [WorkspaceManagerDialog.cs:685](../../iTerminal.Core/Dialogs/Workspaces/WorkspaceManagerDialog.cs). The tests write the literals 104 times in 27 files. [AiPanelTools.cs:143](../../iTerminal.Core/AI/AiPanelTools.cs) maps the names to "left" and "right" for the model, and `:108` back.
    - **Problem:** a mistyped name compiles and finds no panel, the two constant pairs can drift, and `iConsole`, which owns the interface, has no name to pass. Principle: explicit contracts, one place per decision.
    - **Improvement:** one constant class beside the interface, and the two existing pairs forward to it (additive, so no public member goes):

      ```csharp
      // iConsole/Interfaces/PanelNames.cs (new)
      public static class PanelNames
      {
          public const string Left = "LeftPanel";
          public const string Right = "RightPanel";
      }
      // Content and ContentModelService
      public const string Left = PanelNames.Left;
      public const string Right = PanelNames.Right;
      ```

      Replace the literal in `WorkspaceManagerDialog` and the test literals when the files are touched. Keep the strings rather than an enum: workspace layouts store `FocusedPanel` as text ([WorkspaceLayoutService.cs:62](../../iTerminal.Core/Services/Workspaces/WorkspaceLayoutService.cs)), and an enum would change both `IContentModelService` and the saved files.
    - **Effort:** S.
    - **Risk of the change:** none in behaviour; the values stay the same. `WorkspaceLayoutServiceTest` and `AiPanelToolsTest` cover the two places that read the names.

**What works well**

- `ICommand.CanExecute` states its contract in the type since #831: answer from memory, no I/O, no process, no wait ([ICommand.cs:20](../../iConsole/Commands/ICommand.cs)).
- `ISettingsService` is five members, and `IConsoleDriver`, `IExecutableDisassembler` and `IStructuredDocumentParser` give default bodies or state their threading in the type, so a fake implements only what it needs.
- The extension contracts are small (one to three members each) and owned by the layer that consumes them, which is the right direction; finding 62 is about what they carry, not about where they are.
- Every outside-world service the end-to-end suite must not reach has an interface and a `SandboxServices` replacement, including the widget providers added in #822.

**Research cross-reference**

- Design review, finding 36 (resolved in #835 with `FormPanel`): the same release shows that a shared control removes hand-written key handling; finding 45 now builds on it.
- AI overview: no item covers the AI's dialog tool; findings 35 and 62 together decide which dialogs the AI can open and which instance it gets.

**Suggested order**

1. Finding 62 step 1 and finding 64 (S each, no dependencies).
2. Finding 62 step 2 together with the factory of finding 32, after finding 18.

## Resolved

- **Finding 31 (`CanExecute` reaches the network on every draw; the contract is not on `ICommand`)** — resolved in #831 (`076107da`), merged to `main` on 2026-11-23.
  - The F3 and F4 gates (`ViewFileDialogCommand`, `EditTextFileDialogCommand`) and the F6 rename gate (`RenameDialogCommand`, through `FocusedEntry.ActionableUrl(model, fromListing: true)`) ask `ContentModelListing`. A remote drive answers from its listing with no I/O; the other drives answer as before.
  - `ContentModelAsyncDrive` uses the same `ContentModelListing.FindListed`, so the lookup exists once.
  - The contract is on `ICommand.CanExecute`: answer from memory, with no I/O, no process and no wait.
  - `IRuntimeDependencyService.IsReady` lets `RuntimeDependencyDialog.HasWork` answer `false` until the first probe has landed, so the Tools command no longer waits for it.
  - Changed from the finding: `ContentModelRemoteDrive.ExistsFile` and `ExistsFolder` still ask the server. The copy, move and compress tasks read them on a background task as the overwrite check; answering from the listing would let a file made after the last listing be overwritten without a prompt. The gates above are the only callers that changed.
  - Not run: a real SSH, SMB, FTP or WebDAV server, so the draw-time cost is confirmed by reading the code, not measured.
  - Verification: the solution builds (0 errors); `iTerminal.Core.Test` 5896 passed, 51 skipped; `iTerminal.Test` 482 passed, 2 skipped.
- **Finding 30 (the SSH drive puts unquoted paths into shell commands)** — resolved in #831 (`076107da`), merged to `main` on 2026-11-23.
  - `iTerminal.Core/Services/SshCommands.cs` builds every remote command line. `Quote` puts each path in POSIX single quotes (an embedded `'` becomes `'\''`), and `--` ends the options before each path.
  - Folder deletes without recursion use `rmdir`, so a folder with contents is refused instead of being removed by a bare `rm` that failed on it. The `cd` before a user's command quotes the working folder too.
  - The user's own command text is not quoted; it stays the user's shell text.
  - Verification: `SshCommandsTest` covers the quoting, the dashed and the spaced names, and the copy and move flags. Not run: an SSH server, so the remote behaviour is confirmed by the strings only.
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
