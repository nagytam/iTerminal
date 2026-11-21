# Architecture overview

This document holds the architect reviews of iTerminal, one section per scope. A later review of the same scope replaces its section. Finding numbers are unique across the whole document and are never reused; a finding that is fixed moves to the **Resolved** list at the end with its number.

## Layering and dependencies

Reviewed on 2026-11-21, scope "Layering and dependencies", branch `docs/architecture-review-layering` cut from `main` at commit `de1026af`, with the focus on the four latest commits: `de1026af` (#779, `iConsole.Formats`), `b464c8d3` (#778, dialog tables bound to live data), `74982a77` (#777, `--update`, `--help` and package updates) and `fe868605` (#776, the architect agent and the first review). Read: `CLAUDE.md`, `iTerminal.slnx`, every `*.csproj`, `Directory.Build.props`, the diffs of the four commits, `iConsole/Application/`, `iConsole.Formats/` (layout, namespaces, registration), the dialogs and tests the move changed, `iTerminal/Program.cs`, `LaunchOptions`, `UpdateCommandLine`, `SettingsService`, `DatabaseInitializationHostedService`, `ElevationService`, the five converted dialogs (the hand-off code only), both `Instantiate*TypesTest` classes, and the architecture pages `ui-framework.md`, `build-and-packaging.md`, `testing.md`, `media-and-documents.md`, `dialogs.md` and `runtime-dependencies.md`, plus the design-review and profiling overviews. The `ProjectReference` and `using` edges were checked with `grep`, not from memory. Not done: no build, no test run, no run-time check.

**Summary**

- The dependency direction holds: no file in `iConsole`, `iConsole.Formats` or the input plug-ins uses an `iTerminal` namespace, `iConsole` does not reference `iConsole.Formats`, and `iConsole.Formats` references only `iConsole` and uses none of its UI namespaces. The move in #779 is placed correctly.
- The move is half done as a rule: four readers that implement `iConsole` interfaces (Parquet, HDF5, SQLite, the executable analyzer) and their packages are still in `iTerminal.Core`, so `CLAUDE.md`'s "`iConsole.Formats` holds the file-format readers and their third-party packages" is not yet true (finding 17).
- The container itself is never validated by a test, and the `InstantiateAllTypesTest` contract that `CLAUDE.md` describes is not what the test checks; #779 removed a registration from `AddiConsoleCore`, which is the kind of change only the end-to-end suite would catch (finding 18).
- #778 repeats the "worker parks a result, the UI thread takes it" hand-off by hand in five more dialogs, with two different synchronisation styles (finding 19).
- Strong parts: the format seams are small interfaces owned by `iConsole`; registration has one entry point (`AddFileFormats`, called from `AddiTerminal`, so the app and the end-to-end host get the same set); `--help` returns before any service is built; and the dialogs that #778 converted no longer touch the control tree from a worker.

**Findings**

17. **Format implementations have two homes: `iConsole.Formats` and `iTerminal.Core`** — **Medium**, kind: layering, consistency.
    - **Where:** [iTerminal.Core/Services/ParquetFiles/ParquetFileReader.cs:21](../../iTerminal.Core/Services/ParquetFiles/ParquetFileReader.cs), [iTerminal.Core/Services/Hdf/Hdf5FileReader.cs:20](../../iTerminal.Core/Services/Hdf/Hdf5FileReader.cs), [iTerminal.Core/Services/Sqlite/SqliteDatabaseReader.cs:12](../../iTerminal.Core/Services/Sqlite/SqliteDatabaseReader.cs), [iTerminal.Core/Services/Executables/ExecutableAnalyzer.cs:10](../../iTerminal.Core/Services/Executables/ExecutableAnalyzer.cs), registered at [iTerminalApplicationBuilderExtension.cs:278](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs), `:281`, `:286` and `:294`, beside `AddFileFormats()` at `:54`; the packages `Parquet.Net` and `PureHDF` in [iTerminal.Core/iTerminal.Core.csproj:35](../../iTerminal.Core/iTerminal.Core.csproj) and `:37`; the rule in [CLAUDE.md:46](../../CLAUDE.md). The `ExecutableAnalysisDialog` takes its analyzer from `iTerminal.Core` and its disassembler from `iConsole.Formats` ([iConsole/Dialogs/ExecutableAnalysisDialog.cs:45](../../iConsole/Dialogs/ExecutableAnalysisDialog.cs)).
    - **Problem:** the four readers implement `iConsole` interfaces (`IParquetFileReader`, `IHdfFileReader`, `ISqliteDatabaseReader`, `IExecutableAnalyzer`) and use no `iTerminal.Core` type (their only `using` lines are `System.*`, the `iConsole` interface namespace and the package), so they are format readers by every measure #779 used. A developer adding a format now has two precedents, and a second host of `iConsole` that wants the Parquet or HDF5 viewer must reference all of `iTerminal.Core`. The layering page and `CLAUDE.md` describe a rule the code does not follow yet. Principle: one home per responsibility, consistency with the framework.
    - **Improvement:** move the four classes to `iConsole.Formats` (`Services/ParquetFiles`, `Services/Hdf`, `Services/Sqlite`, `Services/Executables`, namespaces as they are or as finding 21 decides), move `Parquet.Net` and `PureHDF` to `iConsole.Formats.csproj`, and register the four in `FileFormatsServiceCollectionExtension.AddFileFormats` instead of `AddiTerminal`. `Microsoft.Data.Sqlite` stays referenced by `iTerminal.Core` too, because six other files there use it; with finding 5 that is one version line. Move their tests from `iTerminal.Core.Test` to `iConsole.Formats.Test`.
    - **Effort:** M.
    - **Risk of the change:** a missed registration shows only when a viewer opens (finding 18 closes that gap, so do it first). The Parquet, HDF5 and SQLite viewer tests and `ExecutableAnalysisDialogTest` catch a behaviour change; the end-to-end viewer tests catch a wiring gap.

18. **No test builds the real container, and `InstantiateAllTypesTest` does not check what `CLAUDE.md` says it checks** — **Medium**, kind: testability, documentation.
    - **Where:** [iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs:140](../../iTerminal.Core.Test/Generated/InstantiateAllTypesTest.cs) (any type no constructor can build is allocated with `GetUninitializedObject` and counts as a success), [iConsole.Test/Generated/InstantiateIConsoleTypesTest.cs:41](../../iConsole.Test/Generated/InstantiateIConsoleTypesTest.cs) (never runs a constructor), [iTerminal.Core.Test/Application/iTerminalApplicationBuilderExtensionTest.cs:22](../../iTerminal.Core.Test/Application/iTerminalApplicationBuilderExtensionTest.cs) (checks seven registrations by type, builds nothing); the contract in [CLAUDE.md:114](../../CLAUDE.md) and [testing.md:9](../architecture/testing.md). No test passes `ValidateOnBuild`; the host builder in [iConsoleApplicationBuilder.cs:19](../../iConsole/Application/iConsoleApplicationBuilder.cs) does not either.
    - **Problem:** neither test uses the container, and the iTerminal one falls back to an uninitialised object, so a public type whose dependencies are not registered passes. #779 removed `IFileToTextConverterService` from `AddiConsoleCore` and added three registrations elsewhere; the dialogs that need them are transient and resolved only when opened, so a host that misses `AddFileFormats` builds and starts and fails on the first F3. Only the end-to-end suite (4 to 5 minutes) reaches that path. Principle: testability seams, explicit contracts.
    - **Improvement:** add `iTerminal.Core.Test/Application/CompositionRootTest`: register what `Program.cs` registers (`AddDefaultConsoleDriver`, `AddiConsoleCore`, `AddiTerminal` with a `TemporaryDataDirectory`, the sandbox replacements the end-to-end host uses), build with `new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }`, and resolve every registered non-factory service type once. Then correct `CLAUDE.md:114` and `testing.md:9` to say what `InstantiateAllTypesTest` checks (that each public type can be allocated and, where possible, constructed), and point to the new test for the "the container can resolve it" contract.
    - **Effort:** S to M (the sandbox list exists in `iTerminal.Test/TerminalApp.cs:200`; some factories may need a stand-in).
    - **Risk of the change:** the first run may fail on real gaps or on services whose constructor touches the machine; each is either a fix or a sandbox registration. No production change.

19. **Five dialogs hand-write the same worker-to-UI hand-off, in two styles** — **Medium**, kind: concurrency, consistency.
    - **Where:** [PrintDialog.cs:60](../../iTerminal.Core/Dialogs/PrintDialog.cs) and `:281` (lock, generation counter, in-flight count, `PendingOperation`), [StreamsDialog.cs:51](../../iTerminal.Core/Dialogs/StreamsDialog.cs) and `:267` (the same), [TorrentManagerDialog.cs:45](../../iTerminal.Core/Dialogs/Torrents/TorrentManagerDialog.cs) and `:355` (the same), [BluetoothDeviceManagerDialog.cs:31](../../iTerminal.Core/Dialogs/Bluetooth/BluetoothDeviceManagerDialog.cs) and `:278` (`volatile` fields instead of a lock), [ToDoListDialog.cs:42](../../iTerminal.Core/Dialogs/ToDo/ToDoListDialog.cs) and `:93`. Across `iTerminal.Core/Dialogs` and `iConsole/Dialogs`, 21 files keep a generation or operation counter and 12 expose a `PendingOperation`. Design review, finding 68.
    - **Problem:** each dialog re-implements the threading rule "don't mutate the control tree from a worker" with its own fields, lock and stale-result check, and each conversion in #778 added another copy (the Print dialog alone has 19 `lock (_sync)` blocks). A slip in one copy (a missed generation check, a write outside the lock) is a race that only shows under load, and the tests poke `ListCountForTest`-style internals rather than a shared contract. Principle: consistency with the framework, explicit contracts for background work.
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

20. **`IFileToTextConverterService` now mixes extension routing with conversion, so every fake forwards to the real converter, and `iConsole.Test` depends on `iConsole.Formats`** — **Low**, kind: interface, testability.
    - **Where:** [iConsole/Services/IFileToTextConverterService.cs:31](../../iConsole/Services/IFileToTextConverterService.cs) to `:41` (four predicates that were `static` before #779); five call sites in [FileViewerDialog.cs:305](../../iConsole/Dialogs/FileViewerDialog.cs), `:453`, `:473`, `:553` and [TextFileEditorDialog.cs:641](../../iConsole/Dialogs/TextFileEditorDialog.cs); the two fakes that forward them to the real converter, [FileViewerDialogDisassemblyTest.cs:16](../../iConsole.Test/Dialogs/FileViewerDialogDisassemblyTest.cs) and [FileViewerDialogPdfSignaturesTest.cs:16](../../iConsole.Test/Dialogs/FileViewerDialogPdfSignaturesTest.cs); [TextFileEditorDialogTest.cs:44](../../iConsole.Test/Dialogs/TextFileEditorDialogTest.cs) (uses the real converter because a mock answers `false`); [iConsole.Test/iConsole.Test.csproj:30](../../iConsole.Test/iConsole.Test.csproj) (the new reference) and `:40` (fixtures linked from the other test project).
    - **Problem:** the predicates are lookups in extension tables and need no format package, but on the interface they must be implemented by every fake, and the only way to get them right is to call the production class, so the `iConsole` tests now need `iConsole.Formats` and its packages, which the seam was meant to avoid. The interface has eight members; the editor uses two. Adding a format means keeping the predicates and the `ReadFileAsync` branch in step in one 1 163-line class. Principle: interface segregation.
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

      The converter keeps using the same tables. Then the two fakes lose their forwarding lines, `TextFileEditorDialogTest` can use a mock again, and the example-file tests of `DataTreeViewerDialogTest` and `ExecutableAnalysisDialogTest` that need the real parsers move to `iConsole.Formats.Test`, so the `iConsole.Formats` reference can leave `iConsole.Test.csproj`. Callers: 5 in two dialogs, 3 implementations (one real, two fakes). It removes interface members, so any implementer outside this solution breaks; #779 already broke them by adding the members, so doing it before a release that publishes `iConsole` costs nothing extra (no external caller is known in this repository).
    - **Effort:** S.
    - **Risk of the change:** a table that differs between the static class and the converter would show a wrong code-page switch or a missing disassembly; `FileViewerDialogDisassemblyTest`, `FileViewerDialogPdfSignaturesTest`, `TextFileEditorDialogTest` and `FileToTextConverterServiceFixtureTest` cover each predicate.

21. **The namespaces of `iConsole.Formats` do not say which assembly a type is in** — **Low**, kind: layering, documentation.
    - **Where:** 44 of the 46 namespace declarations in `iConsole.Formats` are `iConsole.*` namespaces outside `iConsole.Formats` (17 files in `iConsole.Services`, 6 in `iConsole.Services.Disassembly`, 6 in `iConsole.Fonts`, the rest in `iConsole.Toml`, `.Yaml`, `.Pdb`, `.Pdf`, `.Wasm`, `.Cad`, `.IWork`, `.Photoshop`), while [iConsole.Formats/StructuredDocumentParser.cs:6](../../iConsole.Formats/StructuredDocumentParser.cs) and the registration are in `iConsole.Formats`. `iConsole.Services` now holds the interface `IFileToTextConverterService` in one assembly and its implementation in another. The choice is stated in [iConsole.Formats/iConsole.Formats.csproj:13](../../iConsole.Formats/iConsole.Formats.csproj).
    - **Problem:** keeping the namespaces made the move cheap and source-compatible, but a reader cannot tell from a `using` whether a file needs the `iConsole.Formats` reference, and a layering check cannot be written against namespaces (an architecture test of "`iConsole` never uses `iConsole.Formats`" has to look at assembly references instead). The next moves (finding 17, and the readers still in `iConsole`) will widen the overlap. Principle: dependency direction made visible.
    - **Improvement:** decide it once and write it down. Either keep the namespaces and add one sentence to `ui-framework.md` and `CLAUDE.md` ("`iConsole.Formats` keeps the `iConsole.*` namespaces of the code it took over; the assembly, not the namespace, is the layer"), or move the types to `iConsole.Formats.*` namespaces in one breaking release, with `StructuredDocumentParser` already there as the pattern. The first is S and enough unless `iConsole` is published as a package.
    - **Effort:** S (document) or M (rename, about 46 files and their `using` lines in `iTerminal.Core` and the tests).
    - **Risk of the change:** the rename breaks every consumer's `using` lines; the build shows each.

22. **The launch-option contract rests on a reason that is not true, and a mistyped switch starts the full interface** — **Low**, kind: interface, documentation.
    - **Where:** [iTerminal.Core/Application/LaunchOptions.cs:9](../../iTerminal.Core/Application/LaunchOptions.cs) ("Unknown tokens are not an error: the console framework and the elevation relaunch pass their own arguments through the same list"), [build-and-packaging.md:2](../architecture/build-and-packaging.md) (the same reason); [iConsole/Application/iConsoleApplication.cs:12](../../iConsole/Application/iConsoleApplication.cs) (`CreateBuilder(string[] args)` never reads `args`; the configuration comes from `appSettings.json` and environment variables only, [iConsoleApplicationBuilder.cs:13](../../iConsole/Application/iConsoleApplicationBuilder.cs)); [ElevationService.cs:124](../../iTerminal.Core/Services/Elevation/ElevationService.cs) (removes `--wait-for-parent` from the relaunch arguments but keeps the process id after it).
    - **Problem:** the framework takes no arguments, so the only foreign token is the stray process id the relaunch leaves behind. Ignoring every unknown token means `iTerminal --udpate` silently opens the interface instead of saying the switch is unknown, and `CreateBuilder(args)` advertises a parameter that does nothing. Whether System.CommandLine 3.0 reports unmatched tokens when `--help` is combined with one was not tried: **unverified**. Principle: explicit contracts.
    - **Improvement:** in `ElevationService.SelfCommand`, drop the value with the switch; in `LaunchOptions`, write unknown `--` switches to standard error (keep starting, so an old shortcut still works); and either feed `args` into the configuration (`AddCommandLine(args)`, which would let `--DataDirectory=` work as documented keys do) or mark the parameter as unused in its XML comment. Correct the two sentences.
    - **Effort:** S.
    - **Risk of the change:** a script that passes extra words sees a warning line; `UpdateCommandLineTest` covers the parsing and needs one case for the warning, and the elevation tests cover the relaunch list.

23. **`iConsoleApplication` owns the host but cannot dispose it, and `--update` returns without stopping it or listening for Ctrl+C** — **Low**, kind: lifetime.
    - **Where:** [iConsole/Application/iConsoleApplication.cs:6](../../iConsole/Application/iConsoleApplication.cs) (holds an `IHost`, implements neither `IDisposable` nor `IAsyncDisposable`), [iTerminal/Program.cs:54](../../iTerminal/Program.cs) (`var app = builder.Build()` with no `using`), `:58` to `:60` (`--update` with `CancellationToken.None`, after `DatabaseWarmUp.Start()` at `:35` and the full registration). 25 files in `iTerminal.Core/Services` implement `IDisposable`.
    - **Problem:** the singletons' `Dispose` never runs on either path, so any service that would flush or release something there (a file handle, a pooled SQLite connection, a temporary folder) relies on process exit. On the `--update` path the host is not even stopped, and Ctrl+C during the download ends the process mid-step; `VerifiedDownload`'s `.partial` file keeps that safe today, but nothing in the type says so. The cost of building every registration to reach one service was not measured: **unverified**. Principle: ownership and lifetime.
    - **Improvement:** make `iConsoleApplication` `IAsyncDisposable` (dispose the host) and write `await using var app = builder.Build();` in `Program.cs`; for `--update`, pass a `CancellationTokenSource` cancelled from `Console.CancelKeyPress` and return exit code 1 on cancellation.
    - **Effort:** S.
    - **Risk of the change:** a `Dispose` that was never called may now throw or block at exit, which the shutdown budget (five seconds) and the end-to-end suite, which stops the app after every test, would show. `iConsoleApplicationBuilderTest` covers the builder.

**What works well**

- The new edges are the right ones: `iConsole.Formats` → `iConsole` only ([iConsole.Formats.csproj:32](../../iConsole.Formats/iConsole.Formats.csproj)), `iTerminal.Core` → `iConsole.Formats` ([iTerminal.Core.csproj:56](../../iTerminal.Core/iTerminal.Core.csproj)), and `iTerminal` reaches it through `iTerminal.Core`. No file in `iConsole.Formats` uses `iConsole.Controls`, `.Dialogs`, `.Screens`, `.Desktops` or `.Jobs`, so the format layer has no UI in it.
- The seams are small and owned by the consumer: `IStructuredDocumentParser` (two members) and `IExecutableDisassembler` (one member, with "blocking file I/O: the caller runs it on a worker thread" in the type), and `ExecutableAnalysisDialog` keeps its `Func` constructor as the test seam.
- One registration point: `AddFileFormats()` is called from `AddiTerminal`, so `Program.cs` and the end-to-end host (`iTerminal.Test/TerminalApp.cs:200`) cannot drift apart.
- `--help` returns before any service, database or terminal is touched, and `--update` reuses the dialog's `IUpdateService` path (signature check, MSI, portable scripts) instead of a second installer.
- `LaunchOptions` and `UpdateCommandLine` live in `iTerminal.Core`, so they are unit-tested (`UpdateCommandLineTest`) without starting the app.
- The dialogs converted in #778 apply background data on the UI thread through `ConcurrentObservableCollection.ReplaceAll` and bind a `Table<T>`; `Table<T>.RowForeground` and the wrapped `EmptyText` are reusable additions in the right layer.
- `TestAssemblySetup` now initialises `TextEncodings` explicitly, which removes a hidden order dependency between tests that the move exposed.

**Research cross-reference**

- Design review, finding 68 (hand-painted tables): confirmed as fixed for the five dialogs; finding 19 here is the architecture follow-up, so the remaining conversions do not add more copies of the hand-off.
- Design review, finding 41 (`Table<T>` header, keyboard sort): unchanged by #778; `RowForeground` is a per-row colour, not the cell colour that research item asks for.
- Profiling overview, finding 4 (start-up 5.4 to 6.0 s): does not measure the `--update` path or assembly loading after the split; a `StartupTrace` mark before `UpdateCommandLine.RunAsync` would show the cost finding 23 marks unverified.
- AI overview, item 2 (in-process model, large native packages): the decision the previous review asked for is made; native-heavy format packages go to `iConsole.Formats`. Finding 17 completes the rule for the packages still in `iTerminal.Core`.

**Suggested order**

1. Quick wins: finding 22 (relaunch argument and unknown switches), finding 23 (dispose the host, cancel `--update`), finding 21 (write the namespace decision down).
2. Finding 18 (a validated container test), before finding 17, so the move of four registrations is checked.
3. Finding 17 (move the remaining four readers and their packages), then finding 20 (routing out of the converter interface, `iConsole.Test` without `iConsole.Formats`).
4. Finding 19 (one hand-off primitive), on its own branch; convert the five #778 dialogs in the same pull request.

## Dependencies and packages

Reviewed on 2026-11-21 (second pass, replacing the first one of the same day), scope "Dependencies and packages", branch `docs/architecture-review-layering` at commit `de1026af`, after #777 updated the packages and #779 moved the format packages to `iConsole.Formats`. Read: every `*.csproj`, the `.wixproj` and the Linux `.proj`, `Directory.Build.props`, both workflows, `runtime-dependencies.md`, `build-and-packaging.md`, and the source files that use each changed package (found with `grep` on the namespace). Run: `dotnet list iTerminal.slnx package` with `--outdated`, `--vulnerable --include-transitive` and `--deprecated`, and `--include-transitive` for `iTerminal.Core`, against nuget.org on SDK 11.0.100; the restore found everything up to date and wrote nothing outside `obj/`. Not done: no build, no test run, no run-time check. Not covered by those commands: the WiX packages (not a PackageReference project) and `Packaging.Targets` (referenced only when `LinuxPackaging` is `true`), so their latest versions stay **unverified**; the latest stable `Serilog.Sinks.File` was not listed, because the referenced pre-release is above the stable line. There is no `global.json`, no `Directory.Packages.props`, no `Directory.Build.targets`, no `nuget.config` and no `.github/dependabot.yml`.

**Summary**

- No package has a known vulnerability, direct or transitive; every project targets `net11.0`; `--outdated` reports one update only, `OpenMcdf` 3.3.0 to 3.4.0, which #777 held back on purpose (finding 25). #777 resolved findings 11, 12 and 13.
- `iConsole` itself now references two packages (`Microsoft.Extensions.Hosting` and the unused `Microsoft.FeatureManagement`, finding 9); the format packages are in `iConsole.Formats`.
- The structural risks of the first pass remain: no Central Package Management (finding 5, now eight copies of the MSTest version), no audit gate or lock files (finding 6), no third-party notices (finding 2), a pre-release log sink (finding 3), and floating runtime downloads (finding 7).
- New: `iTerminal.Core` uses SkiaSharp only through `iConsole.Formats` while it repeats `Svg.Skia` itself (finding 24), and a test project carries a package it does not use (finding 26).
- Strong parts, unchanged: the Playwright driver is kept out of the packages and installed for the exact library version with a NuGet SHA-512 check, the Whisper model is pinned by URL, size and hash, and the new `System.CommandLine` 3.0.0 is the current stable release, used by one file.

**Findings**

2. **The releases ship third-party code under notice-requiring licences without any notice file** — **Medium**, kind: dependency, documentation.
   - **Where:** the licence fields of the cached packages (`nuspec`): `SMBLibrary` 1.5.8.1 LGPL-3.0-or-later, `OpenMcdf` 3.3.0 MPL-2.0, `PdfPig` 0.1.16 and `Serilog.Sinks.File` Apache-2.0, `Tomlyn` BSD-2-Clause, `imobiledevice-net` 1.3.17 a licence URL only (its native `libimobiledevice` files are LGPL), most others MIT. Release artifacts: [iTerminal.Installer/Package.wxs:37](../../iTerminal.Installer/Package.wxs) (licence page skipped because "the repository has no licence text"), [build-and-packaging.md:12](../architecture/build-and-packaging.md) and `:34` (RPM licence field "iterminal License"), while [LICENSE](../../LICENSE) is an MIT licence. No `THIRD-PARTY-NOTICES` or similar file exists in the repository or the publish output.
   - **Problem:** MIT, BSD and Apache-2.0 ask for the copyright and licence text to travel with a binary distribution, MPL-2.0 and LGPL ask for more (where the source is, and for LGPL that the library can be replaced). The MSI, the deb, the rpm and the portable archives carry none of it, and the installer and the package metadata state the project's own licence wrongly. This is a release obligation that grows with every package added; it is a reading of the licence fields, not legal advice.
   - **Improvement:** generate a `THIRD-PARTY-NOTICES.txt` at publish time from the restored packages (the `project.assets.json` of `iTerminal` lists every package and path; a small MSBuild target or a step in `release.yml` copies each package's licence file), place it next to `iTerminal.exe`, and show the project licence in the installer (`License.rtf`) and the RPM licence field (`MIT`). The SBOM writer in `DotnetSolutionService.GenerateSbom` (security overview, inventory item 4) already walks a solution's packages and could be the source of the list.
   - **Effort:** M.
   - **Risk of the change:** none for the app; a packaging step can fail a release build, which the "Release builds" pipeline shows. Check the MSI with `msiexec /a` and the deb with `dpkg -c` for the new file.

3. **The log sink is a development pre-release** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:39](../../iTerminal.Core/iTerminal.Core.csproj) (`Serilog.Sinks.File` `8.0.0-dev-02318`, since the first public commit `6a1069a2`). The only use is `.WriteTo.File(path)` in [iTerminal.Core/Logger/Logger.cs:33](../../iTerminal.Core/Logger/Logger.cs).
   - **Problem:** every release ships a CI build of the sink that was never published as stable; nuget.org lists a different pre-release on the same number, so "8.0.0" is a moving name, and `dotnet list --outdated` cannot report a stable update for it because the stable line is below it. #777 updated the packages around it and left this one. The one call used exists in every stable version. Principle: shipped dependencies are stable unless a feature needs otherwise.
   - **Improvement:** reference the newest stable `Serilog.Sinks.File` (7.x; the exact number was not listed by the commands run, so it is **unverified**) and keep `Serilog` 4.4.0.
   - **Effort:** S.
   - **Risk of the change:** a downgrade of a pre-release; the build and `iTerminal.Core.Test` (the `Logger` tests) show any API gap. NuGet reports NU1605 if another package needs the newer one.

4. **`imobiledevice-net` is deprecated and its Linux native files are never shipped; the warning that says so is silenced** — **Medium**, kind: dependency.
   - **Where:** [iTerminal.Core/iTerminal.Core.csproj:25](../../iTerminal.Core/iTerminal.Core.csproj), [Directory.Build.props:6](../../Directory.Build.props) (`NETSDK1206` suppressed for the whole solution), [iTerminal.Core/Services/IPhoneDeviceService.cs:37](../../iTerminal.Core/Services/IPhoneDeviceService.cs) (a failure to load shows no devices), [panels-and-drives.md:26](../architecture/panels-and-drives.md) (`iphone://`).
   - **Problem:** `dotnet list --deprecated` still reports the package as "Legacy" with no alternative. Its native files are for `win-x86`, `win-x64`, `osx-x64` and `ubuntu.16.04-x64` only; .NET 8 and later ignore the Ubuntu identifier, so the `linux-x64` and `linux-arm64` packages carry no `libimobiledevice`, and on Linux the `iphone://` drive can only work with a system library of a matching name. The suppressed warning is the build's only signal of that, and the catch-all in the service turns it into an empty device list. Whether a system library is picked up was not tried: **unverified**.
   - **Improvement:** decide the platform contract of the iPhone drive first. If it is Windows only (and macOS x64), say so in `panels-and-drives.md` and hide the drive elsewhere through `OperatingSystem` checks, and scope the `NoWarn` to `iTerminal.Core.csproj` with a comment that names the effect. If Linux matters, the replacement needs a maintained binding, which is a new dependency decision for the maintainer.
   - **Effort:** M.
   - **Risk of the change:** hiding the drive changes the drive list on Linux; the drive-selector tests in `iTerminal.Core.Test` and the Places page tests in `iTerminal.Test` show it.

5. **No Central Package Management: each version is written in up to eight project files** — **Medium**, kind: dependency, consistency.
   - **Where:** no `Directory.Packages.props`. `MSTest.TestFramework` 4.5.3 in 8 project files, `MSTest.TestAdapter` 4.5.3 and `Microsoft.NET.Test.Sdk` 18.12.0 in 7, `coverlet.collector` 10.1.1 in 5, `Moq` 4.21.0 in 4, `ZXing.Net` 0.16.11, `Svg.Skia` 5.2.3 and `Microsoft.Playwright` 1.65.1 in 2 each ([iTerminal.Core/iTerminal.Core.csproj:32](../../iTerminal.Core/iTerminal.Core.csproj), [iTerminal.Core.Test/iTerminal.Core.Test.csproj:26](../../iTerminal.Core.Test/iTerminal.Core.Test.csproj)), seven `Version="11.0.0"` lines for the Microsoft packages that move with the runtime, and the WiX version 5.0.2 four times in [iTerminal.Installer/iTerminal.Installer.wixproj:20](../../iTerminal.Installer/iTerminal.Installer.wixproj), `:31`, `:32` and `:57`.
   - **Problem:** an update means editing every copy by hand, and a missed copy is silent: finding 1 was that failure, and #777 had to touch six project files for nine package updates. #779 added two project files that copied the test package lines again. The Playwright pair matters more than the rest, because `PlaywrightRuntimeDependency.LibraryVersion()` ([PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs)) installs the driver for the library version, so a test project on another version tests a driver the app never asks for. Principle: one place per decision.
   - **Improvement:** add `Directory.Packages.props` with `ManagePackageVersionsCentrally` and one `PackageVersion` per package, remove the `Version` attributes from the `PackageReference` items, and keep the WiX SDK version in one MSBuild property used by both `Import` elements and the two extensions. `Packaging.Targets` can stay conditional; CPM allows a `PackageVersion` that a build does not use.
   - **Effort:** M (15 project files, mechanical).
   - **Risk of the change:** restore resolves exactly as before if the versions are copied as they are; `dotnet list package --include-transitive` before and after must match. Build and run all test projects; the Windows job checks the installer.

6. **Restore has no guard rails: no lock files, no audit gate, no update automation, no source mapping** — **Medium**, kind: dependency.
   - **Where:** no `packages.lock.json`, no `NuGetAuditMode`, `NuGetAuditLevel` or `WarningsAsErrors` for `NU1901`–`NU1904` in any project file, no `nuget.config`, no `.github/dependabot.yml`; both workflows restore from the default feed ([.github/workflows/build.yml:32](../../.github/workflows/build.yml)).
   - **Problem:** today `--vulnerable --include-transitive` is clean, but nothing makes a future advisory fail a pull request: the SDK's audit only warns, and a warning in a 40-package restore is easy to miss. Without lock files a release build can resolve a different transitive set from the one tested, and without package source mapping any feed added later is searched for every package. The research proposals for an OSV package check (security overview, "security-tools 3.6") look at users' projects; this is the same question for iTerminal's own build. #777 was a manual update pass; automation would have proposed the same updates as pull requests.
   - **Improvement:** in `Directory.Build.props` set `NuGetAuditMode` to `all` and turn `NU1903` and `NU1904` (high and critical) into errors; add `RestorePackagesWithLockFile` with `--locked-mode` in the two workflows; add a `nuget.config` with one source and `packageSourceMapping`; add a Dependabot `nuget` entry (with an `ignore` rule for `OpenMcdf` 3.4.x, finding 25) or a weekly `dotnet list package --outdated --vulnerable` job.
   - **Effort:** S for audit and source mapping, M with lock files (one per project and runtime identifier for the self-contained publishes).
   - **Risk of the change:** lock files fail a restore when a version changes without the file; the build workflow shows it at once. An audit error can block a release on an advisory without a fix, so the level is the maintainer's call.

7. **Runtime downloads follow four different version policies, and two of them float** — **Medium**, kind: dependency, consistency.
   - **Where:** [OllamaRuntimeDependency.cs:27](../../iTerminal.Core/Services/RuntimeDependencies/OllamaRuntimeDependency.cs) and `:75` (`Built` 0.40.4, any 0.40.x accepted, the newest offered), [PowerShellRuntimeDependency.cs:24](../../iTerminal.Core/Services/RuntimeDependencies/PowerShellRuntimeDependency.cs) and `:84` (`Built` 7.7.0, any 7.x), [WhisperRuntimeDependency.cs:24](../../iTerminal.Core/Services/RuntimeDependencies/WhisperRuntimeDependency.cs) (pinned URL, size and SHA-256), [PlaywrightRuntimeDependency.cs:70](../../iTerminal.Core/Services/RuntimeDependencies/PlaywrightRuntimeDependency.cs) (the library's own version, NuGet SHA-512), and the package-manager tools (git, ffmpeg, yt-dlp, rar), [runtime-dependencies.md:5](../architecture/runtime-dependencies.md) to `:21`.
   - **Problem:** for Ollama and PowerShell what a user installs depends on the day: the newest release of the line, checked only against the digest GitHub records next to the file, so a release cannot say which version it was tested with, and one compromised account changes the file and its digest together (security overview, item 27, which covers the self-update and not these). The versions sit as constants in four classes, so "which runtime versions does release N use" has no single answer. Principle: explicit contracts and reproducibility. The Playwright and Whisper policies are the sound ones.
   - **Improvement:** give `OllamaRuntimeDependency` and `PowerShellRuntimeDependency` a pinned default (version plus the SHA-256 of each platform asset, verified once by a real download, as for Whisper) installed by default, and keep "newer on the same line" as the offered update that the user accepts. Put the pins of all four downloaded dependencies in one internal `RuntimeDependencyPins` class, so a release note can list them and a test can check each pin has a hash for every platform asset.
   - **Effort:** M.
   - **Risk of the change:** a wrong pinned hash refuses the install; `VerifiedDownload` already deletes a mismatch. The `*RuntimeDependencyTest` classes in `iTerminal.Core.Test` fake the feeds and cover the line rule; the end-to-end suite replaces the service with `SandboxServices.Dependencies`, so it does not reach the change.

9. **`Microsoft.FeatureManagement` is referenced and never used** — **Low**, kind: dependency.
   - **Where:** [iConsole/iConsole.csproj:17](../../iConsole/iConsole.csproj), now one of only two packages `iConsole` references. The only feature flag is read as plain configuration: `configuration.GetValue<bool>("FeatureManagement:WindowsConsoleDriver")` in [iConsole.Inputs.Windows/Extensions/BuilderExtension.cs:27](../../iConsole.Inputs.Windows/Extensions/BuilderExtension.cs). No source file uses the `Microsoft.FeatureManagement` namespace, `AddFeatureManagement` or `IFeatureManager`.
   - **Problem:** every consumer of `iConsole` restores and ships the package and its dependencies for nothing; after #779 it is half of `iConsole`'s package list, against the stated aim of keeping the framework light. A reader assumes the flag goes through the library. Principle: no unused dependencies.
   - **Improvement:** remove the reference; keep the `FeatureManagement` section name in `appSettings.json` so existing settings still apply.
   - **Effort:** S.
   - **Risk of the change:** a transitive package it brought may be needed elsewhere; the build shows a missing type. `BuilderExtensionCoverageTest` covers the flag.

10. **`iTerminal.Core` references the Windows input plug-in and uses none of it** — **Low**, kind: layering.
    - **Where:** [iTerminal.Core/iTerminal.Core.csproj:57](../../iTerminal.Core/iTerminal.Core.csproj). No file in `iTerminal.Core` uses a type or namespace of `iConsole.Inputs.Windows` (checked again on `de1026af`); only [iTerminal/Program.cs:4](../../iTerminal/Program.cs) does, and `iTerminal.csproj` already references the plug-in.
    - **Problem:** `CLAUDE.md` places the plug-ins beside `iConsole`, selected at start-up by the composition root; the edge lets `iTerminal.Core` (and its tests) use Windows console internals, and puts the plug-in into every project that references `iTerminal.Core`. #779 edited the same `ItemGroup` and left it. Principle: dependency direction.
    - **Improvement:** remove the `ProjectReference`.
    - **Effort:** S.
    - **Risk of the change:** none expected; the build shows any hidden use, and `iTerminal.Test` (which references `iTerminal.Core` only) shows whether the end-to-end host relied on it.

14. **The SDK floats, and a compiler feature is switched on for every project without a note in the architecture pages** — **Low**, kind: dependency, documentation.
    - **Where:** no `global.json`; both workflows install `11.0.x` ([.github/workflows/build.yml:32](../../.github/workflows/build.yml) and `:116`); [Directory.Build.props:8](../../Directory.Build.props) (`runtime-async=on`, added in #505).
    - **Problem:** a new SDK feature band changes analyzers and the compiler under the same commit, so a red build on `main` can come from the runner image, not the code. `runtime-async` changes how every async method runs (stack traces, profiler output, the `Job` and `TaskManager` behaviour the threading rules rely on), yet no page in `Documentation/architecture/` mentions it. Whether SDK 11.0.100 still needs the switch or has made it the default was not checked: **unverified**.
    - **Improvement:** add a `global.json` with `"version": "11.0.100"` and `"rollForward": "latestPatch"`, and one paragraph in `ui-framework.md` (threading) that says runtime async is on, why, and how to turn it off for a diagnosis.
    - **Effort:** S.
    - **Risk of the change:** a machine without that feature band fails to build until it installs it; the error says which version.

15. **The packaging files and pages disagree on the default version, the licence and arm64** — **Low**, kind: documentation.
    - **Where:** [Directory.Build.props:4](../../Directory.Build.props) (default `0.0.0.0`), [iTerminal.Installer/iTerminal.Installer.wixproj:22](../../iTerminal.Installer/iTerminal.Installer.wixproj) and [iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj:27](../../iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj) (default `1.0.0`), [build-and-packaging.md:10](../architecture/build-and-packaging.md) ("default `1.0.0`"), `:12` ("the repository has no licence text", see finding 2), `:35` ("`linux-arm64` is mapped but has not been built here", while [.github/workflows/release.yml:82](../../.github/workflows/release.yml) builds it for every release).
    - **Problem:** three defaults for one number. The WiX SDK imports `Directory.Build.props` before the `.wixproj` sets its own default, so the `1.0.0` there is probably dead and a local MSI is `0.0.0.0` (**unverified**, no Windows build was run). Releases always pass `-p:Version`, so only local builds and readers are affected.
    - **Improvement:** keep the default in `Directory.Build.props` only, drop the two others (the Linux `.proj` is not an SDK project and needs its own, so give it the same `0.0.0.0`), and correct the three sentences in `build-and-packaging.md`.
    - **Effort:** S.
    - **Risk of the change:** a local `.deb` without `-p:Version` gets version `0.0.0.0`; dpkg accepts it.

16. **Tar and zip are read by two libraries, with three different "stays inside the folder" checks** — **Low**, kind: consistency.
    - **Where:** SharpZipLib (`Tar`, `Zip`, `GZip`, `BZip2`) in `Services/Archives/ArchiveExtractionService.cs`, `ArchiveDriveService.cs` and `ManagedArchiveWriter.cs`; the BCL (`System.Formats.Tar`, `ZipArchive`) in `Services/Updates/UpdateService.cs`, `Services/RuntimeDependencies/ReleaseArchive.cs` and `Services/Docker/DockerDriveService.cs`. The checks: [ArchiveExtractionService.cs:156](../../iTerminal.Core/Services/Archives/ArchiveExtractionService.cs) (case-insensitive on Windows only), [ReleaseArchive.cs:100](../../iTerminal.Core/Services/RuntimeDependencies/ReleaseArchive.cs) (always ordinal), [UpdateService.cs:604](../../iTerminal.Core/Services/Updates/UpdateService.cs) (always case-insensitive, also on Linux).
    - **Problem:** SharpZipLib has a stated reason (writing encrypted ZIP, [file-operations.md:210](../architecture/file-operations.md)); the split for reading is not explained. The path check that guards against escaping entries is written three times with three case rules; the update one is looser than needed on Linux, where a folder differing only in case is a different folder. No exploit was found (the roots end in a separator), but a fourth extractor would copy one of the three at random. Principle: one solution per problem.
    - **Improvement:** one `internal static string? ArchivePaths.Inside(string root, string entryName)` in `iTerminal.Core/Helper` with the `ArchiveExtractionService` rule (rooted and drive-letter names refused, case-insensitive only on Windows), used by the three; keep both libraries.
    - **Effort:** S.
    - **Risk of the change:** the extraction tests in `iTerminal.Core.Test` (archive, release-archive and update tests) cover the escape cases and would catch a changed rule.

24. **`iTerminal.Core` uses SkiaSharp only through `iConsole.Formats`, but repeats `Svg.Skia` itself** — **Low**, kind: dependency, consistency.
    - **Where:** `Svg.Skia` 5.2.3 in [iTerminal.Core/iTerminal.Core.csproj:50](../../iTerminal.Core/iTerminal.Core.csproj) and [iConsole.Formats/iConsole.Formats.csproj:25](../../iConsole.Formats/iConsole.Formats.csproj); one user in `iTerminal.Core`, `Video/SvgVideoDecoder.cs`. `SkiaSharp` and its Linux natives are referenced only by `iConsole.Formats` ([iConsole.Formats.csproj:23](../../iConsole.Formats/iConsole.Formats.csproj) and `:24`), yet nine files in `iTerminal.Core` use `SkiaSharp` (`Video/*`, `Model/Map/MapTile.cs`, `Dialogs/Scanning/ScannerDialog.cs`, `Services/Scanning/ScanImageCodec.cs`). The Resolved entry of finding 1 said the reference became one; it is one version in two places.
    - **Problem:** the two packages that go together are declared in two different ways, so the next update has to know that one is repeated and the other is not; and if finding 17 or a later move removes `iTerminal.Core`'s need for `iConsole.Formats`, nine files lose SkiaSharp with no reference to say they needed it. Principle: one place per decision.
    - **Improvement:** pick one rule and write it in `iTerminal.Core.csproj`: either remove the `Svg.Skia` line and add a comment that SkiaSharp and Svg.Skia come through `iConsole.Formats`, which owns their natives, or reference both directly in `iTerminal.Core` with the version from `Directory.Packages.props` (finding 5). The first is the smaller change and keeps the natives in one owner.
    - **Effort:** S.
    - **Risk of the change:** none at run time if the version is unchanged; the build shows a missing type, and the image and SVG decoder tests in `iTerminal.Core.Test` cover the use.

25. **`OpenMcdf` is held at 3.3.0 for a known defect that is recorded only in a commit message** — **Low**, kind: dependency, documentation.
    - **Where:** [iConsole.Formats/iConsole.Formats.csproj:21](../../iConsole.Formats/iConsole.Formats.csproj) (no comment); the reason is in the message of `74982a77` (#777): 3.4.0 throws "Commit or revert the changes to a transacted storage before consolidating it" when the `.msg` and legacy Office readers dispose their read-only storage. Users: `Services/OutlookMessageReader.cs` and `Services/LegacyOfficeTextRenderer.cs` in `iConsole.Formats`. `dotnet list --outdated` reports 3.4.0 as the latest.
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

"Used by" is the number of source files that use the package's namespace in non-test projects, then in test projects; "native" means the package carries native files only; "build only" a package used by the build. "Latest" is the latest stable version on nuget.org on 2026-11-21 from `dotnet list --outdated`, which reported no update for any package other than `OpenMcdf`.

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
| Tomlyn | iConsole.Formats | 2.11.1 | 2.11.1 | No | 1 / 1 | Keep (updated in #777) |
| YamlDotNet | iConsole.Formats | 18.1.0 | 18.1.0 | No | 1 / 1 | Keep (updated in #777) |
| System.CommandLine | iTerminal.Core | 3.0.0 | 3.0.0 | No | 1 / 0 | Keep (new in #777) |
| FluentFTP | iTerminal.Core | 55.1.0 | 55.1.0 | No | 1 / 1 | Keep |
| Microsoft.AspNetCore.DataProtection | iTerminal.Core | 11.0.0 | 11.0.0 | No | 1 / 1 | Optional drop (brought by .Extensions) |
| Microsoft.AspNetCore.DataProtection.Extensions | iTerminal.Core | 11.0.0 | 11.0.0 | No | 1 / 1 | Keep |
| Microsoft.EntityFrameworkCore | iTerminal.Core | 11.0.0 | 11.0.0 | No | 16 / 19 | Keep |
| Microsoft.EntityFrameworkCore.Sqlite | iTerminal.Core | 11.0.0 | 11.0.0 | No | provider of EF | Keep |
| Microsoft.Data.Sqlite | iTerminal.Core | 11.0.0 | 11.0.0 | No | 6 / 6 | Keep (used directly; also the SQLite viewer, finding 17) |
| Microsoft.Extensions.AI | iTerminal.Core | 11.0.1 | 11.0.1 | No | 8 / 6 | Keep (updated in #777) |
| Microsoft.Extensions.AI.OpenAI | iTerminal.Core | 11.0.1 | 11.0.1 | No | 6 / 2 | Keep (updated in #777) |
| imobiledevice-net | iTerminal.Core | 1.3.17 | 1.3.17 (deprecated) | No | 2 / 0 | Decide platform contract (finding 4) |
| Microsoft.Mxc.Sdk | iTerminal.Core | 1.1.0 | 1.1.0 | No | 1 / 1 | Keep; the class comment still calls it "a preview" |
| Microsoft.Playwright | iTerminal.Core | 1.65.1 | 1.65.1 | No | 2 / 3 | Keep (updated in #777) |
| MonoTorrent | iTerminal.Core | 3.0.2 | 3.0.2 | No | 1 / 0 | Keep |
| NLayer | iTerminal.Core | 3.0.1 | 3.0.1 | No | 1 / 0 | Keep |
| PureHDF | iTerminal.Core | 2.2.0 | 2.2.0 (3.0.0-beta.1 out) | No | 1 / 1 | Move to iConsole.Formats (finding 17); watch 3.0 |
| Parquet.Net | iTerminal.Core | 6.1.0 | 6.1.0 | No | 1 / 2 | Move to iConsole.Formats (finding 17) |
| Serilog | iTerminal.Core | 4.4.0 | 4.4.0 | No | 39 / 22 | Keep |
| Serilog.Sinks.File | iTerminal.Core | 8.0.0-dev-02318 | stable 7.x (unverified) | Yes (to stable) | 1 / 0 | Move to stable (finding 3) |
| SharpZipLib | iTerminal.Core | 1.4.2 | 1.4.2 | No | 3 / 7 | Keep (finding 16) |
| SMBLibrary | iTerminal.Core | 1.5.8.1 | 1.5.8.1 | No | 1 / 1 | Keep; LGPL notice (finding 2) |
| SSH.NET | iTerminal.Core | 2026.0.1 | 2026.0.1 | No | 1 / 0 | Keep (updated in #777) |
| Svg.Skia | iTerminal.Core | 5.2.3 | 5.2.3 | No | 1 / 1 | Remove the repeat, or declare SkiaSharp too (finding 24) |
| Whisper.net | iTerminal.Core | 1.9.1 | 1.9.1 | No | 1 / 0 | Keep |
| Whisper.net.Runtime | iTerminal.Core | 1.9.1 | 1.9.1 | No | native | Keep |
| ZstdSharp.Port | iTerminal.Core | 0.8.8 | 0.8.8 | No | 3 / 0 | Keep (the .NET 11.0.0 reference pack has no zstd type) |
| Packaging.Targets | iTerminal (Linux packaging only) | 0.1.232 | unverified | Unknown | build only | Check by hand |
| WixToolset.Sdk, .UI.wixext, .Util.wixext | iTerminal.Installer | 5.0.2 | unverified | Unknown | build only | Check by hand; one version property (finding 5) |
| MSTest.TestFramework | 8 projects (7 test projects and iConsole.Test.Core) | 4.5.3 | 4.5.3 | No | all tests | Keep; centralize (finding 5) |
| MSTest.TestAdapter | 7 test projects | 4.5.3 | 4.5.3 | No | all tests | Keep; centralize (finding 5) |
| Microsoft.NET.Test.Sdk | 7 test projects | 18.12.0 | 18.12.0 | No | all tests | Keep; centralize (finding 5) |
| coverlet.collector | 5 test projects | 10.1.1 | 10.1.1 | No | coverage | Keep (updated in #777) |
| Moq | iConsole.Test.Core, iConsole.Test, iConsole.Formats.Test, iTerminal.Core.Test | 4.21.0 | 4.21.0 | No | 0 / 531 and more | Keep |
| ZXing.Net | iConsole.Test | 0.16.11 | 0.16.11 | No | 0 / 1 | Keep |
| ZXing.Net | iConsole.Formats.Test | 0.16.11 | 0.16.11 | No | 0 / 0 | Remove (finding 26) |
| Reqnroll.MSTest | iTerminal.Specifications.Test | 3.3.4 | 3.3.4 | No | 0 / 215 | Keep |
| Microsoft.Playwright | iTerminal.Core.Test | 1.65.1 | 1.65.1 | No | 0 / 3 | Same version as Core (finding 5) |

**What works well**

- #777 kept the Playwright pair on one version (1.65.1 in both projects), so the driver the tests use is the one the app installs.
- The Playwright library is shipped and its 130 MB driver is not: `ExcludeAssets="build;buildTransitive"` with the reason next to it ([iTerminal.Core/iTerminal.Core.csproj:27](../../iTerminal.Core/iTerminal.Core.csproj)), and the driver is installed for the exact library version from the NuGet package, checked against the SHA-512 of its catalog entry.
- The Whisper model is pinned by commit URL, size and SHA-256 and written through `VerifiedDownload` (`.partial`, check, rename), and every GitHub download refuses an asset without a published digest ([GitHubReleaseFeed.cs:45](../../iTerminal.Core/Services/RuntimeDependencies/GitHubReleaseFeed.cs)).
- The format packages now have an owner that says why in the project file ([iConsole.Formats.csproj:9](../../iConsole.Formats/iConsole.Formats.csproj)), and `iConsole` restores none of them.
- Non-free and large tools (rar, unrar, 7-Zip, ffmpeg, git, yt-dlp) are left to the system package manager and never bundled; an existing install is used as it is and never overwritten.
- `Packaging.Targets` is referenced only when `LinuxPackaging` is `true`, so ordinary builds never restore it, and the Debian and RPM dependency lists are owned by the project with the reason written down ([iTerminal/iTerminal.csproj:36](../../iTerminal/iTerminal.csproj) to `:80`).
- Every project targets `net11.0`, and the Microsoft packages that move with the runtime are on 11.0.0 or its first patch.

**Research cross-reference**

- Security overview, item 27 (TUF signed metadata for self-update): confirmed, and finding 7 extends the same weakness to the Ollama and PowerShell downloads. The new `--update` switch (#777) uses the same install path, so it neither adds to nor reduces that risk.
- Security overview, inventory item 4 (CycloneDX SBOM reader) and the writer in `DotnetSolutionService.GenerateSbom`: a possible source for the notice file of finding 2.
- Security overview, "security-tools 3.6" (OSV package check): finding 6 is the same check for iTerminal's own build.
- AI overview, item 2 (in-process model, large native packages): the place for native-heavy packages is decided (`iConsole.Formats` for format code); finding 17 in "Layering and dependencies" applies it to the two packages still outside.

**Suggested order**

1. Quick wins, no dependencies: finding 26 (unused `ZXing.Net`), finding 9 (remove `Microsoft.FeatureManagement`), finding 10 (remove the unused project edge), finding 25 (comment on the `OpenMcdf` pin), finding 3 (stable log sink), finding 14 (`global.json`, runtime-async note), finding 15 (version defaults and page corrections).
2. Finding 5 (Central Package Management), then finding 24 (one rule for SkiaSharp and Svg.Skia, one line once versions are central), then finding 6 (audit, lock files, source mapping, Dependabot with the `OpenMcdf` ignore).
3. Finding 2 (third-party notices and the project licence in the packages), before the next public release.
4. Finding 4 (iPhone drive platform contract) and finding 7 (pinned runtime downloads), which need a decision from the maintainer first.
5. Finding 16 (one archive path check).

## Resolved

- **Finding 1 (`Svg.Skia` at two major versions)** — resolved by finding 8: `Svg.Skia` is now 5.2.3 everywhere. Correction from the second pass: it is one version in two project files (`iConsole.Formats` and `iTerminal.Core`), not one reference; see finding 24.
- **Finding 8 (`iConsole` carries the file-format packages)** — resolved in #779: the format readers, renderers, converter and their packages (HtmlAgilityPack, Iced, JetDatabaseReader, DocumentFormat.OpenXml, OpenMcdf, PdfPig, SkiaSharp, Svg.Skia, System.Security.Cryptography.Pkcs, Tomlyn, YamlDotNet) moved to `iConsole.Formats`, and their unit tests to `iConsole.Formats.Test`. `iConsole` keeps only the interfaces its dialogs use (`IFileToTextConverterService`, `IStructuredDocumentParser`, `IExecutableDisassembler`). The move kept the namespaces (finding 21). Not moved, because the viewer dialogs in `iConsole` read those formats directly: Csv, Dbf, Har, Json, Xml, Markdown and the XPS document model, which have no third-party package; and not moved although they are format readers behind `iConsole` interfaces: the Parquet, HDF5, SQLite and executable readers in `iTerminal.Core` (finding 17).
- **Finding 11 (patch and minor updates waiting)** — resolved in #777: `coverlet.collector` 10.1.1, `Microsoft.Extensions.AI` and `.OpenAI` 11.0.1, `SSH.NET` 2026.0.1, `SkiaSharp` and its Linux natives 4.157.0, `Tomlyn` 2.11.1. `OpenMcdf` stays at 3.3.0 on purpose (finding 25).
- **Finding 12 (Playwright 1.65 makes every user download the driver again)** — resolved in #777: both references are on 1.65.1 together. The re-download at the next start-up offer is the expected cost.
- **Finding 13 (YamlDotNet two major versions behind)** — resolved in #777: 18.1.0, now in `iConsole.Formats`; the YAML reader tests moved with it to `iConsole.Formats.Test`.
