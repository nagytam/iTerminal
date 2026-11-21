# Architecture overview

This document holds the architect reviews of iTerminal, one section per scope. A later review of the same scope replaces its section. Finding numbers are unique across the whole document and are never reused; a finding that is fixed moves to the **Resolved** list at the end with its number.

## Layering and dependencies

Reviewed on 2026-11-21 (refresh, replacing the pass of the same day at `de1026af`), scope "Layering and dependencies", branch `docs/architecture-review-refresh` cut from `main` at commit `70cc25fb`. The refresh re-checked every open finding against the code and read the changes since `de1026af`: #781 (the Parquet, HDF5, SQLite and executable readers moved to `iConsole.Formats`), #787 (`85406d83`, the `iConsole.Formats.*` namespaces) and #788 (`70cc25fb`, unknown switches reported, the elevation relaunch argument, WinGet-managed installs), plus the WinGet manifests and workflow of #782 to #786. Read: `CLAUDE.md`, every `*.csproj`, `iTerminal/Program.cs`, `LaunchOptions`, `ElevationService`, `UpdateService`, `UpdateCommandLine`, `UpdateCheckJob`, `UpdateDialog`, `AboutDialog`, `ApplicationInfo`, `FileFormatsServiceCollectionExtension`, `iConsoleApplication`, both `Instantiate*TypesTest` classes, the converter fakes in `iConsole.Test`, the five dialogs of finding 19 (the hand-off code only), and `build-and-packaging.md`, `testing.md`, `dialogs.md`. The `ProjectReference`, `namespace` and `using` edges were checked with `grep`. Not done: no build, no test run, no run-time check.

**Summary**

- The dependency direction holds at `70cc25fb`: no file in `iConsole`, `iConsole.Formats` or the input plug-ins uses an `iTerminal` namespace; `iConsole` references neither the `iConsole.Formats` project nor its namespaces (the seven hits for the name in `iConsole` are comments and one `InternalsVisibleTo`); `iConsole.Formats` references only `iConsole`.
- #787 resolved finding 21: every namespace in `iConsole.Formats` is now `iConsole.Formats.*`, so a `using` line names the assembly. It also made finding 20 visible: 15 files in `iConsole.Test` now carry a `using iConsole.Formats.*` line.
- #788 resolved finding 22 (unknown switches reported on standard error, the relaunch drops `--wait-for-parent` with its value, the page and the XML comment corrected).
- Still open: no test builds the real container (finding 18), five dialogs hand-write the same worker-to-UI hand-off (finding 19), and the host is never disposed (finding 23).
- New and small: the app version is read in two places with two fallbacks, and `UpdateService` takes it from a dialog (finding 27); the WinGet case of #788 is decided separately in three consumers of `IUpdateService` (finding 28).

**Findings**

18. **No test builds the real container, and `InstantiateAllTypesTest` does not check what `CLAUDE.md` says it checks** — **Medium**, kind: testability, documentation.
    - **Where:** [iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs:140](../../iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs) (any type no constructor can build is allocated with `GetUninitializedObject` and counts as a success), [iConsole.Test/Generated/InstantiateIConsoleTypesTest.cs:41](../../iConsole.Test/Generated/InstantiateIConsoleTypesTest.cs) (the same fallback), [iTerminal.Core.Test/Application/iTerminalApplicationBuilderExtensionTest.cs:22](../../iTerminal.Core.Test/Application/iTerminalApplicationBuilderExtensionTest.cs) (checks registrations by type, builds nothing); the contract in [CLAUDE.md:114](../../CLAUDE.md) and [testing.md:9](../architecture/testing.md). No test passes `ValidateOnBuild` (no match for it in any `.cs` file at `70cc25fb`); the host builder in [iConsoleApplicationBuilder.cs:19](../../iConsole/Application/iConsoleApplicationBuilder.cs) does not either. The registrations that #781 moved now sit in [FileFormatsServiceCollectionExtension.cs:27](../../iConsole.Formats/Application/FileFormatsServiceCollectionExtension.cs) to `:34`.
    - **Problem:** neither test uses the container, and both fall back to an uninitialised object, so a public type whose dependencies are not registered passes. #779 and #781 moved seven registrations between projects, and #787 changed every `using` line of the registration file; the dialogs that need them are transient and resolved only when opened, so a host that misses `AddFileFormats` builds, starts, and fails on the first F3. Only the end-to-end suite (4 to 5 minutes) reaches that path. Principle: testability seams, explicit contracts.
    - **Improvement:** add `iTerminal.Core.Test/Application/CompositionRootTest`: register what `Program.cs` registers (`AddDefaultConsoleDriver`, `AddiConsoleCore`, `AddiTerminal` with a `TemporaryDataDirectory`, the sandbox replacements the end-to-end host uses), build with `new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }`, and resolve every registered non-factory service type once. Then correct `CLAUDE.md:114` and `testing.md:9` to say what `InstantiateAllTypesTest` checks (that each public type can be allocated and, where possible, constructed), and point to the new test for the "the container can resolve it" contract.
    - **Effort:** S to M (the sandbox list exists in [iTerminal.Test/TerminalApp.cs:200](../../iTerminal.Test/TerminalApp.cs); some factories may need a stand-in).
    - **Risk of the change:** the first run may fail on real gaps or on services whose constructor touches the machine (since #788 `UpdateService` reads the registry on Windows); each is either a fix or a sandbox registration. No production change.

19. **Five dialogs hand-write the same worker-to-UI hand-off, in two styles** — **Medium**, kind: concurrency, consistency.
    - **Where:** [PrintDialog.cs:60](../../iTerminal.Core/Dialogs/PrintDialog.cs) and `:281` (lock, generation counter, in-flight count, `PendingOperation`; 19 `lock (_sync)` blocks), [StreamsDialog.cs:51](../../iTerminal.Core/Dialogs/StreamsDialog.cs) and `:267` (the same), [TorrentManagerDialog.cs:45](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs) and `:355` (the same), [BluetoothDeviceManagerDialog.cs:31](../../iTerminal.Core/Dialogs/Bluetooth/BluetoothDeviceManagerDialog.cs) and `:278` (`volatile` fields instead of a lock), [ToDoListDialog.cs:42](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs) and `:93`. Across `iTerminal.Core/Dialogs` and `iConsole/Dialogs`, 12 files expose a `PendingOperation`. `iConsole/Tasks` still has no shared primitive for it. Design review, finding 68.
    - **Problem:** each dialog re-implements the threading rule "don't mutate the control tree from a worker" with its own fields, lock and stale-result check, and each conversion in #778 added another copy. A slip in one copy (a missed generation check, a write outside the lock) is a race that only shows under load, and the tests poke `ListCountForTest`-style internals rather than a shared contract. Principle: consistency with the framework, explicit contracts for background work.
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

      and convert the five dialogs of #778 first (they apply with `ConcurrentObservableCollection.ReplaceAll` already), then the rest as they are touched. Document it in `ui-framework.md` beside `TaskManager`.
    - **Effort:** M (the primitive is S; each dialog is S).
    - **Risk of the change:** a dialog that relied on a side effect of its own lock may change order; the five dialog test classes (`PrintDialogTest`, `StreamsDialogTest`, `TorrentManagerDialogTest`, `BluetoothDeviceManagerDialogTest`, `ToDoListDialogTest`) await `PendingOperation` and would show it.

20. **`IFileToTextConverterService` mixes extension routing with conversion, so every fake forwards to the real converter, and `iConsole.Test` depends on `iConsole.Formats`** — **Low**, kind: interface, testability.
    - **Where:** [iConsole/Services/IFileToTextConverterService.cs:32](../../iConsole/Services/IFileToTextConverterService.cs) to `:41` (four predicates that were `static` before #779); five call sites in [FileViewerDialog.cs:305](../../iConsole/Dialogs/FileViewerDialog.cs), `:453`, `:473`, `:553` and [TextFileEditorDialog.cs:641](../../iConsole/Dialogs/TextFileEditorDialog.cs); the two fakes that forward them to the real converter, [FileViewerDialogDisassemblyTest.cs:17](../../iConsole.Test/Dialogs/FileViewerDialogDisassemblyTest.cs) and [FileViewerDialogPdfSignaturesTest.cs:17](../../iConsole.Test/Dialogs/FileViewerDialogPdfSignaturesTest.cs); [TextFileEditorDialogTest.cs:44](../../iConsole.Test/Dialogs/TextFileEditorDialogTest.cs) (uses the real converter because a mock answers `false`); [iConsole.Test/iConsole.Test.csproj:30](../../iConsole.Test/iConsole.Test.csproj) (the reference) and `:40` (fixtures linked from the other test project). Since #787 the dependency is visible in the source: 15 files in `iConsole.Test` use an `iConsole.Formats.*` namespace (among them the viewer, editor, data-tree and executable-analysis dialog tests and five control review tests), and [iConsole/AssemblyInfo.cs:4](../../iConsole/AssemblyInfo.cs) opens `iConsole`'s internals to `iConsole.Formats.Test`.
    - **Problem:** the predicates are lookups in extension tables and need no format package, but on the interface they must be implemented by every fake, and the only way to get them right is to call the production class, so the `iConsole` tests need `iConsole.Formats` and its packages, which the seam was meant to avoid. The interface has eight members; the editor uses two. Adding a format means keeping the predicates and the `ReadFileAsync` branch in step in one class. Principle: interface segregation.
    - **Improvement:** move the extension tables the predicates read into `iConsole` as a static `ViewerFileKinds` class, following the existing `*FileTypes` classes there (`XpsFileTypes`, `HdfFileTypes`, `ParquetFileTypes`, `SqliteFileTypes`), and call it from the two dialogs:

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

      The converter keeps using the same tables. Then the two fakes lose their forwarding lines, `TextFileEditorDialogTest` can use a mock again, and the tests in the 15 files that need the real readers either move to `iConsole.Formats.Test` or get a fake, so the `iConsole.Formats` reference can leave `iConsole.Test.csproj`. Callers: 5 in two dialogs, 3 implementations (one real, two fakes). It removes interface members, so any implementer outside this solution breaks; #779 already broke them by adding the members, and #787 broke every `using` of the implementations, so doing it before a release that publishes `iConsole` costs nothing extra (no external caller is known in this repository).
    - **Effort:** S for the interface; M with the 15 test files.
    - **Risk of the change:** a table that differs between the static class and the converter would show a wrong code-page switch or a missing disassembly; `FileViewerDialogDisassemblyTest`, `FileViewerDialogPdfSignaturesTest`, `TextFileEditorDialogTest` and `FileToTextConverterServiceFixtureTest` cover each predicate.

23. **`iConsoleApplication` owns the host but cannot dispose it, and `--update` returns without stopping it or listening for Ctrl+C** — **Low**, kind: lifetime.
    - **Where:** [iConsole/Application/iConsoleApplication.cs:6](../../iConsole/Application/iConsoleApplication.cs) (holds an `IHost`, implements neither `IDisposable` nor `IAsyncDisposable`), [iTerminal/Program.cs:60](../../iTerminal/Program.cs) (`var app = builder.Build()` with no `using`), `:64` to `:66` (`--update` with `CancellationToken.None`, after `DatabaseWarmUp.Start()` at `:41` and the full registration). 25 files in `iTerminal.Core/Services` implement `IDisposable`. Unchanged by #788, which added a WinGet branch to the same `--update` path.
    - **Problem:** the singletons' `Dispose` never runs on either path, so any service that would flush or release something there (a file handle, a pooled SQLite connection, a temporary folder) relies on process exit. On the `--update` path the host is not even stopped, and Ctrl+C during the download ends the process mid-step; `VerifiedDownload`'s `.partial` file keeps that safe today, but nothing in the type says so. The cost of building every registration to reach one service was not measured: **unverified**. Principle: ownership and lifetime.
    - **Improvement:** make `iConsoleApplication` `IAsyncDisposable` (dispose the host) and write `await using var app = builder.Build();` in `Program.cs`; for `--update`, pass a `CancellationTokenSource` cancelled from `Console.CancelKeyPress` and return exit code 1 on cancellation.
    - **Effort:** S.
    - **Risk of the change:** a `Dispose` that was never called may now throw or block at exit, which the shutdown budget (five seconds) and the end-to-end suite, which stops the app after every test, would show. `iConsoleApplicationBuilderTest` covers the builder.

27. **The app version is read in two places with two fallbacks, and `UpdateService` takes it from a dialog** — **Low**, kind: layering, consistency.
    - **Where:** [iTerminal.Core/Application/ApplicationInfo.cs:8](../../iTerminal.Core/Application/ApplicationInfo.cs) (cut at `plus >= 0`, fallback `"0.0.0.0"`) and [iTerminal.Core/Dialogs/AboutDialog.cs:81](../../iTerminal.Core/Dialogs/AboutDialog.cs) (cut at `plus > 0`, fallback `"unknown"`); users: [UpdateService.cs:69](../../iTerminal.Core/Services/Updates/UpdateService.cs) (the service's current version comes from `AboutDialog`), [TerminalScreen.cs:54](../../iTerminal.Core/Screens/TerminalScreen.cs) (the title from `ApplicationInfo`) and `:78` (the splash from `AboutDialog`), [iTerminal/Program.cs:80](../../iTerminal/Program.cs) (`ApplicationInfo`). `UpdateService.cs` is one of four files in `iTerminal.Core/Services` that use a UI namespace, and the only one that depends on a dialog.
    - **Problem:** one fact has two readers that differ on the edge cases, so the window title and the update check can disagree about the running version on a build without an informational version; `"unknown"` parses to no version, and the check then offers every release (`version > null` is true). A service that depends on a dialog type also inverts the order inside `iTerminal.Core` (services below dialogs), which the next move of `UpdateService` (for example into a headless updater) would have to undo. Principle: dependency direction, one place per decision.
    - **Improvement:** keep `ApplicationInfo.Version` as the only reader; make `AboutDialog.Version` return it (or remove it and change its 6 uses: 2 in source, 4 in tests), and pass `ApplicationInfo.Version` in `UpdateService`'s public constructor. No public API changes (`AboutDialog.Version` is `internal`).
    - **Effort:** S.
    - **Risk of the change:** the About dialog shows `0.0.0.0` instead of `unknown` on an unstamped build; `AboutDialogTest`, `ControlIntegrationTest` and `KeyHintGuardTest` compare against the property and stay green.

28. **The WinGet case is decided separately in each consumer of `IUpdateService`** — **Low**, kind: interface, consistency.
    - **Where:** [IUpdateService.cs:47](../../iTerminal.Core/Services/Updates/IUpdateService.cs) (`InstallKind`) and `:50` (`CanInstall`, which since #788 is false for `PackageManager`); three consumers that each branch on `InstallKind == UpdateInstallKind.PackageManager` and build the hint themselves: [UpdateDialog.cs:164](../../iTerminal.Core/Dialogs/UpdateDialog.cs), [UpdateCheckJob.cs:116](../../iTerminal.Core/Jobs/UpdateCheckJob.cs) (the same localized key and English fallback as the dialog, copied), and [UpdateCommandLine.cs:33](../../iTerminal.Core/Services/Updates/UpdateCommandLine.cs) (English text with `UpdateService.WinGetPackageId`, checked before `CanInstall` at `:39`). The dialog and the job write the package id literally inside the fallback text; the command line uses the constant.
    - **Problem:** "why this copy cannot install, and what to do instead" is a decision of the service, but each consumer re-derives it from the enum, so the next install variant (a Linux package that `apt` or `dnf` updates, which today is `Unsupported`, or a Scoop or Homebrew install) touches three files plus the enum, and the copies of the text can drift. The order of the two checks matters in the command line and not in the others, which a reader cannot see from the interface. Principle: explicit contracts, evolution cost.
    - **Improvement:** add one member and use it in the three places:

      ```csharp
      // IUpdateService (new)
      /// <summary>Why this copy does not install a release itself and what the user does instead; null when CanInstall is true.</summary>
      string? ManualUpdateHint { get; }
      ```

      `UpdateService` builds it from `_kind`, `WinGetPackageId` and `ReleasesAddress` with the existing `Update.WinGetManaged` and `Update.DownloadFrom` keys. Callers: 3; implementations: `UpdateService`, plus the test doubles in the four test classes that use `IUpdateService` (`UpdateCommandLineTest`, `UpdateCheckJobTest`, `UpdateDialogProgressTest`, `DialogKeyHintsTest`); a Moq mock needs a setup for the new member, a hand-written fake one line. The interface is internal to the app (no caller outside the solution).
    - **Effort:** S.
    - **Risk of the change:** the command line's output text changes if it starts using the localized string; `UpdateCommandLineTest` and `UpdateServiceTest` assert on it and need the new wording.

**What works well**

- The edges are the right ones: `iConsole.Formats` → `iConsole` only ([iConsole.Formats.csproj:38](../../iConsole.Formats/iConsole.Formats.csproj)), `iTerminal.Core` → `iConsole.Formats` ([iTerminal.Core.csproj:53](../../iTerminal.Core/iTerminal.Core.csproj)), and `iTerminal` reaches it through `iTerminal.Core`. No file in `iConsole.Formats` uses `iConsole.Controls`, `.Dialogs`, `.Screens`, `.Desktops` or `.Jobs`, so the format layer has no UI in it.
- After #787 the namespace is the layer: implementations are in `iConsole.Formats.*` (17 namespaces), the contracts stay in `iConsole.*` (`Services`, `Executables`, `Structured`, `Sqlite`, `Parquet`, `Hdf`), and the project comment says so ([iConsole.Formats.csproj:13](../../iConsole.Formats/iConsole.Formats.csproj)). A layering test can now be written against namespaces.
- The seams are small and owned by the consumer: `IStructuredDocumentParser`, `IExecutableDisassembler` (with "blocking file I/O: the caller runs it on a worker thread" in the type), and the reader interfaces of the Parquet, HDF5 and SQLite viewers, whose XML comments name where the implementation lives.
- One registration point: `AddFileFormats()` is called from `AddiTerminal`, so `Program.cs` and the end-to-end host (`iTerminal.Test/TerminalApp.cs:200`) cannot drift apart.
- #788 put the WinGet decision where it can be tested: `UpdateService.KindFor` takes the registry answer as a `bool`, so `UpdateServiceTest` covers it without a registry, and the registry read is confined to one `SupportedOSPlatform("windows")` method.
- `--help` returns before any service, database or terminal is touched, and `LaunchOptions` and `UpdateCommandLine` live in `iTerminal.Core`, so `UpdateCommandLineTest` covers the switches (including the new unknown-switch case) without starting the app.
- The dialogs converted in #778 apply background data on the UI thread through `ConcurrentObservableCollection.ReplaceAll` and bind a `Table<T>`.

**Research cross-reference**

- Design review, finding 68 (hand-painted tables): confirmed as fixed for the five dialogs; finding 19 here is the architecture follow-up, so the remaining conversions do not add more copies of the hand-off.
- Profiling overview, finding 4 (start-up 5.4 to 6.0 s): does not measure the `--update` path or the registry read #788 added to `UpdateService`'s construction; a `StartupTrace` mark before `UpdateCommandLine.RunAsync` would show the cost finding 23 marks unverified.
- AI overview, item 2 (in-process model, large native packages): the decision is made and applied; native-heavy format packages are in `iConsole.Formats` since #781.

**Suggested order**

1. Quick wins: finding 27 (one version reader), finding 28 (`ManualUpdateHint`), finding 23 (dispose the host, cancel `--update`).
2. Finding 18 (a validated container test): it checks the seven `AddFileFormats` registrations and the `UpdateService` constructor in one place.
3. Finding 20 (routing out of the converter interface, `iConsole.Test` without `iConsole.Formats`); the interface part first, the 15 test files after.
4. Finding 19 (one hand-off primitive), on its own branch; convert the five #778 dialogs in the same pull request.

## Dependencies and packages

Reviewed on 2026-11-21 (third pass, replacing the second one of the same day at `de1026af`), scope "Dependencies and packages", branch `docs/architecture-review-refresh` at commit `70cc25fb`. Since the second pass the project files changed only by #781 (`PureHDF`, `Parquet.Net` and a second `Microsoft.Data.Sqlite` reference moved to `iConsole.Formats`), and a new workflow `.github/workflows/winget.yml` arrived (#784). Read: every `*.csproj`, the `.wixproj` and the Linux `.proj`, `Directory.Build.props`, the three workflows, `Package.wxs`, `runtime-dependencies.md`, `build-and-packaging.md`. Run again: `dotnet list iTerminal.slnx package` with `--outdated`, `--vulnerable --include-transitive` and `--deprecated` against nuget.org; the restore found everything up to date and wrote nothing outside `obj/`. Result: `OpenMcdf` 3.3.0 to 3.4.0 is the only update, none of the 14 PackageReference projects has a vulnerable package, and `imobiledevice-net` is the only deprecated one ("Legacy"). Not done: no build, no test run, no run-time check. Not covered by those commands: the WiX packages (not a PackageReference project), `Packaging.Targets` (referenced only when `LinuxPackaging` is `true`) and the stable `Serilog.Sinks.File` line, so their latest versions stay **unverified**. There is still no `global.json`, no `Directory.Packages.props`, no `Directory.Build.targets`, no `nuget.config` and no `.github/dependabot.yml`.

**Summary**

- No package has a known vulnerability, direct or transitive; every project targets `net11.0`; `OpenMcdf` 3.4.0 is the only update and is held back on purpose (finding 25). No package finding was fixed since the second pass.
- The structural risks remain: no Central Package Management (finding 5), no audit gate or lock files (finding 6), no third-party notices (finding 2), a pre-release log sink (finding 3), and floating runtime downloads (finding 7).
- New: the WinGet publish workflow downloads `wingetcreate` from a "latest" redirect with no version or hash and then hands it the `WINGET_PAT` secret (finding 29).
- `build-and-packaging.md` now states the MIT licence correctly; the installer source comment still says there is none (findings 2 and 15).
- Strong parts, unchanged: the Playwright driver is kept out of the packages and installed for the exact library version with a NuGet SHA-512 check, the Whisper model is pinned by URL, size and hash, and the format packages have one owner.

**Findings**

2. ~~**The releases ship third-party code under notice-requiring licences without any notice file**~~ — **Medium**, kind: dependency, documentation. **Resolved** (see Resolved below).
   - **Where:** the licence fields of the cached packages (`nuspec`): `SMBLibrary` 1.5.8.1 LGPL-3.0-or-later, `OpenMcdf` 3.3.0 MPL-2.0, `PdfPig` 0.1.16 and `Serilog.Sinks.File` Apache-2.0, `Tomlyn` BSD-2-Clause, `imobiledevice-net` 1.3.17 a licence URL only (its native `libimobiledevice` files are LGPL), most others MIT. Release artifacts: [iTerminal.Installer/Package.wxs:37](../../iTerminal.Installer/Package.wxs) (licence page skipped because "the repository has no licence text"), [build-and-packaging.md:36](../architecture/build-and-packaging.md) (RPM licence field "iterminal License"), while [LICENSE](../../LICENSE) is an MIT licence and [build-and-packaging.md:12](../architecture/build-and-packaging.md) now says so. No `THIRD-PARTY-NOTICES` or similar file exists in the repository or the publish output. The WinGet manifests (#782 to #786) publish the same MSI to a wider audience.
   - **Problem:** MIT, BSD and Apache-2.0 ask for the copyright and licence text to travel with a binary distribution, MPL-2.0 and LGPL ask for more (where the source is, and for LGPL that the library can be replaced). The MSI, the deb, the rpm and the portable archives carry none of it, and the installer and the package metadata state the project's own licence wrongly. This is a release obligation that grows with every package added; it is a reading of the licence fields, not legal advice.
   - **Improvement:** generate a `THIRD-PARTY-NOTICES.txt` at publish time from the restored packages (the `project.assets.json` of `iTerminal` lists every package and path; a small MSBuild target or a step in `release.yml` copies each package's licence file), place it next to `iTerminal.exe`, and show the project licence in the installer (`License.rtf`) and the RPM licence field (`MIT`). The SBOM writer in `DotnetSolutionService.GenerateSbom` (security overview, inventory item 4) already walks a solution's packages and could be the source of the list.
   - **Effort:** M.
   - **Risk of the change:** none for the app; a packaging step can fail a release build, which the "Release builds" pipeline shows. Check the MSI with `msiexec /a` and the deb with `dpkg -c` for the new file.

3. **The log sink is a development pre-release** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:36](../../iTerminal.Core/iTerminal.Core.csproj) (`Serilog.Sinks.File` `8.0.0-dev-02318`, since the first public commit `6a1069a2`). The only use is `.WriteTo.File(path)` in [iTerminal.Core/Logger/Logger.cs:33](../../iTerminal.Core/Logger/Logger.cs).
   - **Problem:** every release ships a CI build of the sink that was never published as stable; nuget.org lists a different pre-release on the same number, so "8.0.0" is a moving name, and `dotnet list --outdated` cannot report a stable update for it because the stable line is below it. The one call used exists in every stable version. Principle: shipped dependencies are stable unless a feature needs otherwise.
   - **Improvement:** reference the newest stable `Serilog.Sinks.File` (7.x; the exact number was not listed by the commands run, so it is **unverified**) and keep `Serilog` 4.4.0.
   - **Effort:** S.
   - **Risk of the change:** a downgrade of a pre-release; the build and `iTerminal.Core.Test` (the `Logger` tests) show any API gap. NuGet reports NU1605 if another package needs the newer one.

4. **`imobiledevice-net` is deprecated and its Linux native files are never shipped; the warning that says so is silenced** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:25](../../iTerminal.Core/iTerminal.Core.csproj), [Directory.Build.props:6](../../Directory.Build.props) (`NETSDK1206` suppressed for the whole solution), [iTerminal.Core/Services/IPhoneDeviceService.cs:37](../../iTerminal.Core/Services/IPhoneDeviceService.cs) (a failure to load shows no devices), [panels-and-drives.md:26](../architecture/panels-and-drives.md) (`iphone://`).
   - **Problem:** `dotnet list --deprecated` still reports the package as "Legacy" with no alternative (re-run at `70cc25fb`). Its native files are for `win-x86`, `win-x64`, `osx-x64` and `ubuntu.16.04-x64` only; .NET 8 and later ignore the Ubuntu identifier, so the `linux-x64` and `linux-arm64` packages carry no `libimobiledevice`, and on Linux the `iphone://` drive can only work with a system library of a matching name. The suppressed warning is the build's only signal of that, and the catch-all in the service turns it into an empty device list. Whether a system library is picked up was not tried: **unverified**.
   - **Improvement:** decide the platform contract of the iPhone drive first. If it is Windows only (and macOS x64), say so in `panels-and-drives.md` and hide the drive elsewhere through `OperatingSystem` checks, and scope the `NoWarn` to `iTerminal.Core.csproj` with a comment that names the effect. If Linux matters, the replacement needs a maintained binding, which is a new dependency decision for the maintainer.
   - **Effort:** M.
   - **Risk of the change:** hiding the drive changes the drive list on Linux; the drive-selector tests in `iTerminal.Core.Test` and the Places page tests in `iTerminal.Test` show it.

29. **The WinGet workflow runs an unpinned, unverified `wingetcreate` with the publishing token** — **Medium**, kind: dependency.
   - **Where:** [.github/workflows/winget.yml:52](../../.github/workflows/winget.yml) (`Invoke-WebRequest https://aka.ms/wingetcreate/latest -OutFile wingetcreate.exe`), `:59` (run without the token) and `:82` (run with `--token $env:WINGET_PAT`, a `public_repo` token, [build-and-packaging.md:15](../architecture/build-and-packaging.md)). The actions in the three workflows are pinned by major tag (`actions/checkout@v7`, `actions/upload-artifact@v7`), not by commit.
   - **Problem:** what runs depends on the day: a redirect chooses the binary, nothing records which version produced a submitted manifest, and nothing checks the file before it receives a token that can push to a fork and open pull requests on `microsoft/winget-pkgs`. A changed `wingetcreate` release can also change the manifest it writes (schema version, fields) between two submissions, which `winget-pkgs` validation then rejects. This is the same floating-download pattern as finding 7, in the release pipeline instead of the app. The workflow runs only when started by hand, which limits the exposure. Principle: reproducibility, explicit contracts.
   - **Improvement:** download a fixed release from `github.com/microsoft/winget-create/releases/download/v<version>/wingetcreate.exe` and compare `Get-FileHash -Algorithm SHA256` with a hash written in the workflow (an `env:` value beside the version), failing the job on a mismatch; note the version in the run summary. Pinning the actions by commit belongs with finding 6.
   - **Effort:** S.
   - **Risk of the change:** a version bump needs the hash updated in the same edit; a stale hash fails the run before anything is submitted. Verify with one run without `submit` and compare the manifests with the previous version folder.

5. **No Central Package Management: each version is written in up to eight project files** — **Medium**, kind: dependency, consistency.
   - **Where:** no `Directory.Packages.props`. `MSTest.TestFramework` 4.5.3 in 8 project files, `MSTest.TestAdapter` 4.5.3 and `Microsoft.NET.Test.Sdk` 18.12.0 in 7, `coverlet.collector` 10.1.1 in 5, `Moq` 4.21.0 in 4, `ZXing.Net` 0.16.11, `Svg.Skia` 5.2.3, `Microsoft.Data.Sqlite` 11.0.0 and `Microsoft.Playwright` 1.65.1 in 2 each ([iTerminal.Core/iTerminal.Core.csproj:32](../../iTerminal.Core/iTerminal.Core.csproj), [iTerminal.Core.Test/iTerminal.Core.Test.csproj:26](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj)), several `Version="11.0.0"` lines for the Microsoft packages that move with the runtime, and the WiX version 5.0.2 four times in [iTerminal.Installer/iTerminal.Installer.wixproj:20](../../iTerminal.Installer/iTerminal.Installer.wixproj), `:31`, `:32` and `:57`.
   - **Problem:** an update means editing every copy by hand, and a missed copy is silent: finding 1 was that failure, and #777 had to touch six project files for nine package updates. #781 added another repeated line (`Microsoft.Data.Sqlite` in `iConsole.Formats` and `iTerminal.Core`). The Playwright pair matters more than the rest, because `PlaywrightRuntimeDependency.LibraryVersion()` ([PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs)) installs the driver for the library version, so a test project on another version tests a driver the app never asks for. Principle: one place per decision.
   - **Improvement:** add `Directory.Packages.props` with `ManagePackageVersionsCentrally` and one `PackageVersion` per package, remove the `Version` attributes from the `PackageReference` items, and keep the WiX SDK version in one MSBuild property used by both `Import` elements and the two extensions. `Packaging.Targets` can stay conditional; CPM allows a `PackageVersion` that a build does not use.
   - **Effort:** M (15 project files, mechanical).
   - **Risk of the change:** restore resolves exactly as before if the versions are copied as they are; `dotnet list package --include-transitive` before and after must match. Build and run all test projects; the Windows job checks the installer.

6. **Restore has no guard rails: no lock files, no audit gate, no update automation, no source mapping** — **Medium**, kind: dependency.
   - **Where:** no `packages.lock.json`, no `NuGetAuditMode`, `NuGetAuditLevel` or `WarningsAsErrors` for `NU1901`–`NU1904` in any project file, no `nuget.config`, no `.github/dependabot.yml`; both build workflows restore from the default feed ([.github/workflows/build.yml:32](../../.github/workflows/build.yml)).
   - **Problem:** today `--vulnerable --include-transitive` is clean, but nothing makes a future advisory fail a pull request: the SDK's audit only warns, and a warning in a 40-package restore is easy to miss. Without lock files a release build can resolve a different transitive set from the one tested, and without package source mapping any feed added later is searched for every package. The research proposals for an OSV package check (security overview, "security-tools 3.6") look at users' projects; this is the same question for iTerminal's own build.
   - **Improvement:** in `Directory.Build.props` set `NuGetAuditMode` to `all` and turn `NU1903` and `NU1904` (high and critical) into errors; add `RestorePackagesWithLockFile` with `--locked-mode` in the two workflows; add a `nuget.config` with one source and `packageSourceMapping`; add a Dependabot `nuget` entry (with an `ignore` rule for `OpenMcdf` 3.4.x, finding 25) and a `github-actions` entry, which would also propose commit pins for the actions (finding 29).
   - **Effort:** S for audit and source mapping, M with lock files (one per project and runtime identifier for the self-contained publishes).
   - **Risk of the change:** lock files fail a restore when a version changes without the file; the build workflow shows it at once. An audit error can block a release on an advisory without a fix, so the level is the maintainer's call.

7. **Runtime downloads follow four different version policies, and two of them float** — **Medium**, kind: dependency, consistency.
   - **Where:** [OllamaRuntimeDependency.cs:27](../../iTerminal.Core/Services/RuntimeDependencies/OllamaRuntimeDependency.cs) and `:75` (`Built` 0.40.4, any 0.40.x accepted, the newest offered), [PowerShellRuntimeDependency.cs:24](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs) and `:84` (`Built` 7.7.0, any 7.x), [WhisperRuntimeDependency.cs:24](../../iTerminal.Core/Services/RuntimeDependencies/WhisperRuntimeDependency.cs) (pinned URL, size and SHA-256), [PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs) (the library's own version, NuGet SHA-512), and the package-manager tools (git, ffmpeg, yt-dlp, rar), [runtime-dependencies.md:5](../architecture/runtime-dependencies.md) to `:22`.
   - **Problem:** for Ollama and PowerShell what a user installs depends on the day: the newest release of the line, checked only against the digest GitHub records next to the file, so a release cannot say which version it was tested with, and one compromised account changes the file and its digest together (security overview, item 27, which covers the self-update and not these). The versions sit as constants in four classes, so "which runtime versions does release N use" has no single answer. Principle: explicit contracts and reproducibility. The Playwright and Whisper policies are the sound ones.
   - **Improvement:** give `OllamaRuntimeDependency` and `PowerShellRuntimeDependency` a pinned default (version plus the SHA-256 of each platform asset, verified once by a real download, as for Whisper) installed by default, and keep "newer on the same line" as the offered update that the user accepts. Put the pins of all four downloaded dependencies in one internal `RuntimeDependencyPins` class, so a release note can list them and a test can check each pin has a hash for every platform asset.
   - **Effort:** M.
   - **Risk of the change:** a wrong pinned hash refuses the install; `VerifiedDownload` already deletes a mismatch. The `*RuntimeDependencyTest` classes in `iTerminal.Core.Test` fake the feeds and cover the line rule; the end-to-end suite replaces the service with `SandboxServices.Dependencies`, so it does not reach the change.

9. **`Microsoft.FeatureManagement` is referenced and never used** — **Low**, kind: dependency.
   - **Where:** [iConsole/iConsole.csproj:17](../../iConsole/iConsole.csproj), one of only two packages `iConsole` references. The only feature flag is read as plain configuration: `configuration.GetValue<bool>("FeatureManagement:WindowsConsoleDriver")` in [iConsole.Inputs.Windows/Extensions/BuilderExtension.cs:27](../../iConsole.Inputs.Windows/Extensions/BuilderExtension.cs). No source file uses the `Microsoft.FeatureManagement` namespace, `AddFeatureManagement` or `IFeatureManager`.
   - **Problem:** every consumer of `iConsole` restores and ships the package and its dependencies for nothing; it is half of `iConsole`'s package list, against the stated aim of keeping the framework light. A reader assumes the flag goes through the library. Principle: no unused dependencies.
   - **Improvement:** remove the reference; keep the `FeatureManagement` section name in `appSettings.json` so existing settings still apply.
   - **Effort:** S.
   - **Risk of the change:** a transitive package it brought may be needed elsewhere; the build shows a missing type. `BuilderExtensionCoverageTest` covers the flag.

10. **`iTerminal.Core` references the Windows input plug-in and uses none of it** — **Low**, kind: layering.
    - **Where:** [iTerminal.Core/iTerminal.Core.csproj:54](../../iTerminal.Core/iTerminal.Core.csproj). No file in `iTerminal.Core` uses a type or namespace of `iConsole.Inputs.Windows` (checked again on `70cc25fb`); only [iTerminal/Program.cs:4](../../iTerminal/Program.cs) does, and `iTerminal.csproj` already references the plug-in.
    - **Problem:** `CLAUDE.md` places the plug-ins beside `iConsole`, selected at start-up by the composition root; the edge lets `iTerminal.Core` (and its tests) use Windows console internals, and puts the plug-in into every project that references `iTerminal.Core`. Principle: dependency direction.
    - **Improvement:** remove the `ProjectReference`.
    - **Effort:** S.
    - **Risk of the change:** none expected; the build shows any hidden use, and `iTerminal.Test` (which references `iTerminal.Core` only) shows whether the end-to-end host relied on it.

14. **The SDK floats, and a compiler feature is switched on for every project without a note in the architecture pages** — **Low**, kind: dependency, documentation.
    - **Where:** no `global.json`; both build workflows install `11.0.x` ([.github/workflows/build.yml:32](../../.github/workflows/build.yml) and `:116`); [Directory.Build.props:8](../../Directory.Build.props) (`runtime-async=on`, added in #505).
    - **Problem:** a new SDK feature band changes analyzers and the compiler under the same commit, so a red build on `main` can come from the runner image, not the code. `runtime-async` changes how every async method runs (stack traces, profiler output, the `Job` and `TaskManager` behaviour the threading rules rely on), yet no page in `Documentation/architecture/` mentions it. Whether SDK 11.0.100 still needs the switch or has made it the default was not checked: **unverified**.
    - **Improvement:** add a `global.json` with `"version": "11.0.100"` and `"rollForward": "latestPatch"`, and one paragraph in `ui-framework.md` (threading) that says runtime async is on, why, and how to turn it off for a diagnosis.
    - **Effort:** S.
    - **Risk of the change:** a machine without that feature band fails to build until it installs it; the error says which version.

15. **The packaging files and pages disagree on the default version, the licence and arm64** — **Low**, kind: documentation.
    - **Where:** [Directory.Build.props:4](../../Directory.Build.props) (default `0.0.0.0`), [iTerminal.Installer/iTerminal.Installer.wixproj:22](../../iTerminal.Installer/iTerminal.Installer.wixproj) and [iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj:27](../../iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj) (default `1.0.0`), [build-and-packaging.md:10](../architecture/build-and-packaging.md) ("default `1.0.0`"), [Package.wxs:37](../../iTerminal.Installer/Package.wxs) ("the repository has no licence text"; the page itself was corrected at [build-and-packaging.md:12](../architecture/build-and-packaging.md)), [build-and-packaging.md:37](../architecture/build-and-packaging.md) ("`linux-arm64` is mapped but has not been built here", while [.github/workflows/release.yml:82](../../.github/workflows/release.yml) builds it for every release).
    - **Problem:** three defaults for one number. The WiX SDK imports `Directory.Build.props` before the `.wixproj` sets its own default, so the `1.0.0` there is probably dead and a local MSI is `0.0.0.0` (**unverified**, no Windows build was run). Releases always pass `-p:Version`, so only local builds and readers are affected.
    - **Improvement:** keep the default in `Directory.Build.props` only, drop the two others (the Linux `.proj` is not an SDK project and needs its own, so give it the same `0.0.0.0`), correct the `Package.wxs` comment, and correct the two remaining sentences in `build-and-packaging.md`.
    - **Effort:** S.
    - **Risk of the change:** a local `.deb` without `-p:Version` gets version `0.0.0.0`; dpkg accepts it.

24. **`iTerminal.Core` uses SkiaSharp only through `iConsole.Formats`, but repeats `Svg.Skia` itself** — **Low**, kind: dependency, consistency.
    - **Where:** `Svg.Skia` 5.2.3 in [iTerminal.Core/iTerminal.Core.csproj:47](../../iTerminal.Core/iTerminal.Core.csproj) and [iConsole.Formats/iConsole.Formats.csproj:25](../../iConsole.Formats/iConsole.Formats.csproj); one user in `iTerminal.Core`, `Video/SvgVideoDecoder.cs`. `SkiaSharp` and its Linux natives are referenced only by `iConsole.Formats` ([iConsole.Formats.csproj:23](../../iConsole.Formats/iConsole.Formats.csproj) and `:24`), yet nine files in `iTerminal.Core` use `SkiaSharp` (`Video/*`, `Model/Map/MapTile.cs`, `Dialogs/Scanning/ScannerDialog.cs`, `Services/Scanning/ScanImageCodec.cs`). #781 added the same pattern for `Microsoft.Data.Sqlite`, but with a comment that says why ([iConsole.Formats.csproj:33](../../iConsole.Formats/iConsole.Formats.csproj)).
    - **Problem:** the two packages that go together are declared in two different ways, so the next update has to know that one is repeated and the other is not; and if a later move removes `iTerminal.Core`'s need for `iConsole.Formats`, nine files lose SkiaSharp with no reference to say they needed it. Principle: one place per decision.
    - **Improvement:** pick one rule and write it in `iTerminal.Core.csproj`: either remove the `Svg.Skia` line and add a comment that SkiaSharp and Svg.Skia come through `iConsole.Formats`, which owns their natives, or reference both directly in `iTerminal.Core` with the version from `Directory.Packages.props` (finding 5). The first is the smaller change and keeps the natives in one owner.
    - **Effort:** S.
    - **Risk of the change:** none at run time if the version is unchanged; the build shows a missing type, and the image and SVG decoder tests in `iTerminal.Core.Test` cover the use.

25. **`OpenMcdf` is held at 3.3.0 for a known defect that is recorded only in a commit message** — **Low**, kind: dependency, documentation.
    - **Where:** [iConsole.Formats/iConsole.Formats.csproj:21](../../iConsole.Formats/iConsole.Formats.csproj) (no comment, while the three references #781 added beside it each have one); the reason is in the message of `74982a77` (#777): 3.4.0 throws "Commit or revert the changes to a transacted storage before consolidating it" when the `.msg` and legacy Office readers dispose their read-only storage. Users: `Services/OutlookMessageReader.cs` and `Services/LegacyOfficeTextRenderer.cs` in `iConsole.Formats`. `dotnet list --outdated` still reports 3.4.0 as the latest.
    - **Problem:** the next update pass, by hand or by automation (finding 6), sees an outdated package and no reason not to take it, and the defect shows only in the fixture tests or in a user's `.msg` preview. Principle: explicit contracts, a pin says why.
    - **Improvement:** add an XML comment above the reference with the error text, the two readers and the condition for lifting the pin (a release after 3.4.0 that disposes a read-only storage without committing, checked with the `.msg`, `.doc`, `.xls` and `.ppt` fixtures); add the Dependabot `ignore` rule when finding 6 is done.
    - **Effort:** S.
    - **Risk of the change:** none (a comment). The fixture tests in `iConsole.Formats.Test` (`OutlookMessageReader`, legacy Office) are what guard the pin.

26. **`iConsole.Formats.Test` references `ZXing.Net` and uses none of it** — **Low**, kind: dependency.
    - **Where:** [iConsole.Formats.Test/iConsole.Formats.Test.csproj:22](../../iConsole.Formats.Test/iConsole.Formats.Test.csproj). The only user of the `ZXing` namespace is `iConsole.Test/Qr/QrRoundTripTest.cs`, which stayed in `iConsole.Test`.
    - **Problem:** the package list of the new test project was copied from `iConsole.Test`; an unused package is restored on every build and is one more line for finding 5 to carry. Principle: no unused dependencies.
    - **Improvement:** remove the line.
    - **Effort:** S.
    - **Risk of the change:** none; the build of `iConsole.Formats.Test` shows a missing type.

**Package table**

"Used by" is the number of source files that use the package's namespace in non-test projects, then in test projects, as counted in the second pass (the rename of #787 changed namespaces, not package use); "native" means the package carries native files only; "build only" a package used by the build. "Latest" is the latest stable version on nuget.org on 2026-11-21 from `dotnet list --outdated`, re-run at `70cc25fb`, which reported no update for any package other than `OpenMcdf`.

| Package | Project | Current | Latest | Major change | Used by (count) | Action |
| --- | --- | --- | --- | --- | --- | --- |
| Microsoft.Extensions.Hosting | iConsole | 11.0.0 | 11.0.0 | No | 2 / 0 | Keep (the host builder) |
| Microsoft.FeatureManagement | iConsole | 4.8.0 | 4.8.0 | No | 0 / 0 | Remove (finding 9) |
| HtmlAgilityPack | iConsole.Formats | 1.13.1 | 1.13.1 | No | 2 / 0 | Keep |
| Iced | iConsole.Formats | 1.22.0 | 1.22.0 | No | 1 / 0 | Keep |
| JetDatabaseReader | iConsole.Formats | 3.0.0 | 3.0.0 | No | 1 / 0 | Keep |
| DocumentFormat.OpenXml | iConsole.Formats | 3.5.1 | 3.5.1 | No | 2 / 3 | Keep |
| OpenMcdf | iConsole.Formats | 3.3.0 | 3.4.0 | No | 2 / 1 | Hold, write the reason down (finding 25) |
| PdfPig | iConsole.Formats | 0.1.16 | 0.1.16 | No | 1 / 0 | Keep |
| SkiaSharp | iConsole.Formats | 4.157.0 | 4.157.0 | No | 2 + 9 in Core / 9 | Keep; one rule for Core (finding 24) |
| SkiaSharp.NativeAssets.Linux.NoDependencies | iConsole.Formats | 4.157.0 | 4.157.0 | No | native | Keep with SkiaSharp |
| Svg.Skia | iConsole.Formats | 5.2.3 | 5.2.3 | No | 1 / 1 | Keep (owner) |
| System.Security.Cryptography.Pkcs | iConsole.Formats | 11.0.0 | 11.0.0 | No | 1 + 1 in Core / 3 | Keep |
| Tomlyn | iConsole.Formats | 2.11.1 | 2.11.1 | No | 1 / 1 | Keep |
| YamlDotNet | iConsole.Formats | 18.1.0 | 18.1.0 | No | 1 / 1 | Keep |
| PureHDF | iConsole.Formats | 2.2.0 | 2.2.0 (3.0.0-beta.1 out) | No | 1 / 1 | Keep (moved in #781); watch 3.0 |
| Parquet.Net | iConsole.Formats | 6.1.0 | 6.1.0 | No | 1 / 2 | Keep (moved in #781) |
| Microsoft.Data.Sqlite | iTerminal.Core, iConsole.Formats | 11.0.0 | 11.0.0 | No | 6 / 6 in Core, 1 / 3 in Formats | Keep in both (the comment says why); centralize (finding 5) |
| System.CommandLine | iTerminal.Core | 3.0.0 | 3.0.0 | No | 1 / 0 | Keep |
| FluentFTP | iTerminal.Core | 55.1.0 | 55.1.0 | No | 1 / 1 | Keep |
| Microsoft.AspNetCore.DataProtection | iTerminal.Core | 11.0.0 | 11.0.0 | No | 1 / 1 | Optional drop (brought by .Extensions) |
| Microsoft.AspNetCore.DataProtection.Extensions | iTerminal.Core | 11.0.0 | 11.0.0 | No | 1 / 1 | Keep |
| Microsoft.EntityFrameworkCore | iTerminal.Core | 11.0.0 | 11.0.0 | No | 16 / 19 | Keep |
| Microsoft.EntityFrameworkCore.Sqlite | iTerminal.Core | 11.0.0 | 11.0.0 | No | provider of EF | Keep |
| Microsoft.Extensions.AI | iTerminal.Core | 11.0.1 | 11.0.1 | No | 8 / 6 | Keep |
| Microsoft.Extensions.AI.OpenAI | iTerminal.Core | 11.0.1 | 11.0.1 | No | 6 / 2 | Keep |
| imobiledevice-net | iTerminal.Core | 1.3.17 | 1.3.17 (deprecated) | No | 2 / 0 | Decide platform contract (finding 4) |
| Microsoft.Mxc.Sdk | iTerminal.Core | 1.1.0 | 1.1.0 | No | 1 / 1 | Keep; the class comment still calls it "a preview" |
| Microsoft.Playwright | iTerminal.Core | 1.65.1 | 1.65.1 | No | 2 / 3 | Keep |
| MonoTorrent | iTerminal.Core | 3.0.2 | 3.0.2 | No | 1 / 0 | Keep |
| NLayer | iTerminal.Core | 3.0.1 | 3.0.1 | No | 1 / 0 | Keep |
| Serilog | iTerminal.Core | 4.4.0 | 4.4.0 | No | 39 / 22 | Keep |
| Serilog.Sinks.File | iTerminal.Core | 8.0.0-dev-02318 | stable 7.x (unverified) | Yes (to stable) | 1 / 0 | Move to stable (finding 3) |
| SharpZipLib | iTerminal.Core | 1.4.2 | 1.4.2 | No | 3 / 7 | Keep (finding 16) |
| SMBLibrary | iTerminal.Core | 1.5.8.1 | 1.5.8.1 | No | 1 / 1 | Keep; LGPL notice (finding 2) |
| SSH.NET | iTerminal.Core | 2026.0.1 | 2026.0.1 | No | 1 / 0 | Keep |
| Svg.Skia | iTerminal.Core | 5.2.3 | 5.2.3 | No | 1 / 1 | Remove the repeat, or declare SkiaSharp too (finding 24) |
| Whisper.net | iTerminal.Core | 1.9.1 | 1.9.1 | No | 1 / 0 | Keep |
| Whisper.net.Runtime | iTerminal.Core | 1.9.1 | 1.9.1 | No | native | Keep |
| ZstdSharp.Port | iTerminal.Core | 0.8.8 | 0.8.8 | No | 3 / 0 | Keep (the .NET 11.0.0 reference pack has no zstd type) |
| Packaging.Targets | iTerminal (Linux packaging only) | 0.1.232 | unverified | Unknown | build only | Check by hand |
| WixToolset.Sdk, .UI.wixext, .Util.wixext | iTerminal.Installer | 5.0.2 | unverified | Unknown | build only | Check by hand; one version property (finding 5) |
| MSTest.TestFramework | 8 projects (7 test projects and iConsole.Test.Core) | 4.5.3 | 4.5.3 | No | all tests | Keep; centralize (finding 5) |
| MSTest.TestAdapter | 7 test projects | 4.5.3 | 4.5.3 | No | all tests | Keep; centralize (finding 5) |
| Microsoft.NET.Test.Sdk | 7 test projects | 18.12.0 | 18.12.0 | No | all tests | Keep; centralize (finding 5) |
| coverlet.collector | 5 test projects | 10.1.1 | 10.1.1 | No | coverage | Keep |
| Moq | iConsole.Test.Core, iConsole.Test, iConsole.Formats.Test, iTerminal.Core.Test | 4.21.0 | 4.21.0 | No | 0 / 531 and more | Keep |
| ZXing.Net | iConsole.Test | 0.16.11 | 0.16.11 | No | 0 / 1 | Keep |
| ZXing.Net | iConsole.Formats.Test | 0.16.11 | 0.16.11 | No | 0 / 0 | Remove (finding 26) |
| Reqnroll.MSTest | iTerminal.Specifications.Test | 3.3.4 | 3.3.4 | No | 0 / 215 | Keep |
| Microsoft.Playwright | iTerminal.Core.Test | 1.65.1 | 1.65.1 | No | 0 / 3 | Same version as Core (finding 5) |

**What works well**

- The Playwright pair is on one version (1.65.1 in both projects), so the driver the tests use is the one the app installs.
- The Playwright library is shipped and its 130 MB driver is not: `ExcludeAssets="build;buildTransitive"` with the reason next to it ([iTerminal.Core/iTerminal.Core.csproj:27](../../iTerminal.Core/iTerminal.Core.csproj)), and the driver is installed for the exact library version from the NuGet package, checked against the SHA-512 of its catalog entry.
- The Whisper model is pinned by commit URL, size and SHA-256 and written through `VerifiedDownload` (`.partial`, check, rename), and every GitHub download refuses an asset without a published digest ([GitHubReleaseFeed.cs:45](../../iTerminal.Core/Services/RuntimeDependencies/GitHubReleaseFeed.cs)).
- The format packages have one owner that says why in the project file ([iConsole.Formats.csproj:9](../../iConsole.Formats/iConsole.Formats.csproj)), `iConsole` restores none of them, and each reference #781 added carries its own reason.
- The WinGet workflow checks the release before it runs (version form, not a draft, the MSI asset present), writes the manifests without submitting unless `submit` is ticked, and runs only when started by hand.
- Non-free and large tools (rar, unrar, 7-Zip, ffmpeg, git, yt-dlp) are left to the system package manager and never bundled; an existing install is used as it is and never overwritten.
- `Packaging.Targets` is referenced only when `LinuxPackaging` is `true`, so ordinary builds never restore it, and the Debian and RPM dependency lists are owned by the project with the reason written down ([iTerminal/iTerminal.csproj:36](../../iTerminal/iTerminal.csproj) to `:80`).
- Every project targets `net11.0`, and the Microsoft packages that move with the runtime are on 11.0.0 or its first patch.

**Research cross-reference**

- Security overview, item 27 (TUF signed metadata for self-update): confirmed, and finding 7 extends the same weakness to the Ollama and PowerShell downloads. #788 changes which copies install themselves (WinGet copies no longer do), not how the download is checked.
- Security overview, inventory item 4 (CycloneDX SBOM reader) and the writer in `DotnetSolutionService.GenerateSbom`: a possible source for the notice file of finding 2.
- Security overview, "security-tools 3.6" (OSV package check): finding 6 is the same check for iTerminal's own build.
- No research document covers the release workflows; finding 29 is the first item on the WinGet pipeline.

**Suggested order**

1. Quick wins, no dependencies: finding 26 (unused `ZXing.Net`), finding 9 (remove `Microsoft.FeatureManagement`), finding 10 (remove the unused project edge), finding 25 (comment on the `OpenMcdf` pin), finding 29 (pin and hash `wingetcreate`), finding 3 (stable log sink), finding 14 (`global.json`, runtime-async note), finding 15 (version defaults and the remaining sentences).
2. Finding 5 (Central Package Management), then finding 24 (one rule for SkiaSharp and Svg.Skia, one line once versions are central), then finding 6 (audit, lock files, source mapping, Dependabot for NuGet and actions with the `OpenMcdf` ignore).
3. Finding 2 is resolved (see Resolved); nothing to do.
4. Finding 4 (iPhone drive platform contract) and finding 7 (pinned runtime downloads), which need a decision from the maintainer first.

## Resolved

- **Finding 2 (third-party notices in the releases)** — resolved on branch `third-party-notices-in-packages`: `iTerminal/Build/ThirdPartyNotices.targets` runs after `Publish` and calls `iTerminal.ThirdPartyNotices`, which writes `THIRD-PARTY-NOTICES.txt` for the runtime being published (packages from `project.assets.json`, licences from the nuspecs, `Licenses/Overrides.txt` for two packages that state none), and copies the project `LICENSE` beside it. Every installable form publishes first, so the MSI, the deb, the rpm, the tarballs and the portable archives carry both files. Verification: the notice for linux-x64 was generated and read; the tarball, deb and rpm contents were to be checked on the Linux packaging build (see the PR for what was verified).
- **Finding 16 (one "stays inside the folder" check for archives)** — resolved on branch `archive-path-check`: `iTerminal.Core/Helper/ArchivePaths.Inside` is the single check. `ArchiveExtractionService` (the former `SafePath`), `ReleaseArchive` and `UpdateService` (which still throws on a refused entry) all call it; both libraries are kept. The rule is the `ArchiveExtractionService` rule: backslashes are separators, leading slashes are dropped (the entry stays inside), and names with a colon are refused; case is ignored on Windows only. Behaviour changes on the two BCL extractors: a case-only folder difference on Linux now counts as a different folder (the update check was looser), and a colon in a release entry name is refused on Linux too. The escape cases are covered by `ArchiveExtractionServiceTest.OnlyPathsInsideTheTargetAreSafe`, which now calls the helper. Run on Linux: the `iTerminal.Core.Test` tests whose names match `Archive`, `ReleaseArchive` or `UpdateService` (256 passed, 0 failed). Not run: the Windows path (the case-insensitive branch), which the Windows CI job covers.
- **Finding 22 (launch-option contract and mistyped switches)** — resolved in #788: `LaunchOptions.UnknownSwitches` lists the unmatched tokens that start with `-`, `Program.cs` writes one warning line per switch to standard error and still starts the interface, `ElevationService.SelfCommand` drops `--wait-for-parent` together with its value, and the `LaunchOptions` XML comment and `build-and-packaging.md` now give the true reason. Left as it is, and documented on that page: `iConsoleApplication.CreateBuilder(string[] args)` does not read `args`. Not checked at run time: whether the warning stays visible once the interface takes over the screen (**unverified**).
- **Finding 21 (namespaces of `iConsole.Formats`)** — resolved in #787: the format implementations moved from `iConsole.*` to `iConsole.Formats.*` (`Services`, `Services.Disassembly`, `Executables`, `Fonts`, `IWork`, `Pdb`, `Yaml`, `Toml`, `Sqlite`, `Wasm`, `Pdf`, `Photoshop`, `Parquet`, `Hdf`, `Cad`), and their tests to `iConsole.Formats.Test.*`. The contracts that `iConsole` declares (`iConsole.Services`, `iConsole.Executables`, `iConsole.Sqlite`, `iConsole.Parquet`, `iConsole.Hdf`) keep their namespaces, so a `using iConsole.Services;` now names the assembly that holds it.
- **Finding 17 (format implementations in two homes)** — resolved in #781: the Parquet, HDF5 and SQLite readers, the executable analyzer with its PE, ELF and Mach-O parsers, and their packages (`Parquet.Net`, `PureHDF`) moved from `iTerminal.Core` to `iConsole.Formats`; #787 later gave them `iConsole.Formats.*` namespaces. Their tests moved to `iConsole.Formats.Test`. The four registrations moved from `AddiTerminal` into `AddFileFormats`. `Microsoft.Data.Sqlite` is referenced by both projects.
- **Finding 1 (`Svg.Skia` at two major versions)** — resolved by finding 8: `Svg.Skia` is now 5.2.3 everywhere. Correction from the second pass: it is one version in two project files (`iConsole.Formats` and `iTerminal.Core`), not one reference; see finding 24.
- **Finding 8 (`iConsole` carries the file-format packages)** — resolved in #779: the format readers, renderers, converter and their packages (HtmlAgilityPack, Iced, JetDatabaseReader, DocumentFormat.OpenXml, OpenMcdf, PdfPig, SkiaSharp, Svg.Skia, System.Security.Cryptography.Pkcs, Tomlyn, YamlDotNet) moved to `iConsole.Formats`, and their unit tests to `iConsole.Formats.Test`. `iConsole` keeps only the interfaces its dialogs use. Not moved, because the viewer dialogs in `iConsole` read those formats directly: Csv, Dbf, Har, Json, Xml, Markdown and the XPS document model, which have no third-party package.
- **Finding 11 (patch and minor updates waiting)** — resolved in #777: `coverlet.collector` 10.1.1, `Microsoft.Extensions.AI` and `.OpenAI` 11.0.1, `SSH.NET` 2026.0.1, `SkiaSharp` and its Linux natives 4.157.0, `Tomlyn` 2.11.1. `OpenMcdf` stays at 3.3.0 on purpose (finding 25).
- **Finding 12 (Playwright 1.65 makes every user download the driver again)** — resolved in #777: both references are on 1.65.1 together. The re-download at the next start-up offer is the expected cost.
- **Finding 13 (YamlDotNet two major versions behind)** — resolved in #777: 18.1.0, now in `iConsole.Formats`; the YAML reader tests moved with it to `iConsole.Formats.Test`.
