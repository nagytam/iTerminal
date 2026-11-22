# Profiling overview

Measured hot spots of the unit tests and the running application, kept as one document. Each run below is one scope measured on one date. Findings are numbered across the whole document and cited elsewhere as "finding N". The profiler agent appends a new section for every run and never rewrites an older one; a later run records the new number of an earlier finding in "Status of earlier findings". Numbers from different machines are not compared as facts.

| Run | Scope | Machine |
| --- | --- | --- |
| P1 | Unit tests (`iConsole.Test`, `iTerminal.Core.Test`, `iConsole.Inputs.Posix.Test`) and the running app | Linux, 48 CPUs, 499 GB |
| P2 | Unit tests (all eight projects that build on Linux) and the running app | Linux, 48 CPUs, 499 GB |

## P1 2026-11-20: unit tests and the running application

Scope `tests` and `app`; the end-to-end suite (`iTerminal.Test`) was not run, because it takes 4 to 5 minutes and was not asked for by name. Branch `profiling-overview-refresh`, commit `ff7b63c0` (origin `main`). Machine: Linux 7.3.0-11-generic, 48 CPUs, 499 GB RAM, .NET SDK 11.0.100. Tools: none of `dotnet-trace`, `dotnet-counters` or `dotnet-gcdump` is installed (`dotnet tool list -g` lists only `dotnet-ef`), and nothing was installed. `perf` exists but `perf_event_paranoid` is 4, so it cannot sample. What was used instead: the TRX files of `dotnet test`, the app's own `StartupTrace` (`ITERMINAL_STARTUP_TRACE`), `/proc/<pid>` counters per process and per thread, `strace` on the app process (allowed for a child of the harness), `bwrap --unshare-net` so the app had no network, and a pseudo terminal driven from a Python script. Not measured: GC collection counts, allocation rate, thread-pool queue length (they need `dotnet-counters`); CPU and allocation hot spots by frame (they need `dotnet-trace` and `dotnet-gcdump`); the published ReadyToRun build (`PublishReadyToRun` is set in `iTerminal/iTerminal.csproj:15`, but publishing needs the runtime pack from the network); the end-to-end suite; docker, music, MTP and remote drives. The app was started from the Release build with `dotnet iTerminal.dll`, which is JIT-compiled and not ReadyToRun, so start-up numbers are an upper bound for the packaged app.

### Summary

- The idle app is not idle: 25 % of one core in steady state (35.9 % in the first 30 s) and 6.9 kB of terminal output per second with nothing happening, because both render loops and the key reader poll with `Thread.Sleep(1)` and both render jobs repaint the whole screen once a second (finding 1). A `git rev-parse` process starts every 3 s in a folder that is not a repository (finding 3).
- Start-up to the first frame takes 5.35 s warm and 5.96 s cold (median of 3 each). Only 0.4 s of that is the runtime, and 2.7 to 3.0 s lies between `run` and the start of the database initializer (finding 4).
- Both big test projects spend 10 to 12 s of 24 to 34 s wall-clock before the first test: a project with 7 tests takes 9.6 s wall for 0.4 s of tests (finding 2). Test time itself is 12.2 s and 21.8 s.
- Inside the test run, the long tail is small tests that build a temporary SQLite database through EF (2.5 to 2.8 s each, finding 5) and fixed waits (findings 6 to 10); the longest single test is 7.3 s (finding 7).
- All measured key presses show output within 49 ms and every operation on a 10 001-entry folder is under the 200 ms and 3 000 ms budgets; shutdown takes 0.16 s and the schema stamp survives a second start.

### Measurements

#### Build

| Step | Time (s) | Samples |
| --- | --- | --- |
| `dotnet build iTerminal.slnx -c Release` (incremental, nothing changed) | 25.1 | 1 (**single sample**) |
| `dotnet build iConsole.Inputs.Posix.Test -c Release` (not in the solution build) | 11.0 | 1 (**single sample**) |

#### Unit tests per project

`dotnet test <project> -c Release --no-build`, three runs each. "Test span" is first test start to last test end in the TRX; "sum" is the sum of test durations (tests run in parallel, so it exceeds the span).

| Project | Tests | Wall-clock (s), 3 runs | Median wall (s) | Test span (s), median | Sum of durations (s), median | Over 1 s | Over 5 s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `iConsole.Test` | 5 350 (0 skipped) | 24.3, 25.6, 24.3 | 24.3 | 12.2 | 150 | 22 | 0 |
| `iTerminal.Core.Test` | 6 547 (48 skipped) | 34.9, 31.7, 33.9 | 33.9 | 21.8 | 543 | 116 | 1 |
| `iConsole.Inputs.Posix.Test` | 7 (1 skipped) | 9.7, 9.6, 8.6 | 9.6 | 0.4 | 0.2 | 0 | 0 |

`iConsole.Inputs.Windows.Test` and `iTerminal.Specifications.Test` were not run (Windows only, and a boot of the whole app per scenario).

Fixed cost before the first test, measured without running any test:

| Command | Time (s), 2 to 3 runs | Samples |
| --- | --- | --- |
| `dotnet test iConsole.Test --no-build --list-tests` | 13.2, 12.9, 12.0 | 3 |
| `dotnet test iTerminal.Core.Test --no-build --list-tests` | 11.7, 12.7, 12.3 | 3 |
| `dotnet vstest iConsole.Test.dll -lt` | 9.1, 8.9 | 2 |
| `dotnet vstest iTerminal.Core.Test.dll -lt` | 9.3, 9.6 | 2 |
| `dotnet msbuild iTerminal.Core.Test.csproj -getProperty:TargetPath` (project evaluation) | 1.9 | 1 (**single sample**) |

The 48 CPUs are busy with 24.9 tests on average in `iTerminal.Core.Test` (543 s of test time in a 21.8 s span), so the single test times below include waiting for a CPU and are higher than a one-test run would show.

#### Slowest ten tests, `iTerminal.Core.Test` (median of 3 runs)

| Test | Class | Duration (s) | Samples |
| --- | --- | --- | --- |
| `TheComputerNeverLosesAtTicTacToe` | `RulesInvariantsTest` | 7.32 | 3 |
| `SignIn_PollsThroughPendingAndSlowDown_ThenStoresTheTokens` | `OAuthTokenProviderTest` | 4.06 | 3 |
| `EveryCoreControl_AcceptsNullAndHugeSettings (StatusBar.HotKeys)` | `CoreControlSweepTest` | 3.95 | 3 |
| `ARecorderThatDiesAfterTheStartProbe_IsReportedOnStop_NotSilentlyIgnored` | `RecordingTest` | 3.91 | 3 |
| `InstantiatePublicTypesFromLocalAssemblies` | `InstantiateAllTypesTest` | 3.81 | 3 |
| `EveryCoreControl_SurvivesAwkwardSizes_Keys_AndClicks (StatusBar.HotKeys)` | `CoreControlSweepTest` | 3.75 | 3 |
| `AFreshQuote_IsServed` | `CacheServiceTest` | 2.79 | 3 |
| `AnOldQuoteBesideAFreshOne_IsLeftOut_NotServedAsCurrent` | `CacheServiceTest` | 2.77 | 3 |
| `ASetWeatherReading_IsReadBack` | `CacheServiceTest` | 2.77 | 3 |
| `AQuoteOlderThanTheTtl_IsNotServed` | `CacheServiceTest` | 2.77 | 3 |

Slowest classes by summed duration (mean of 3 runs): `FfmpegAudioFormatsTest` 28.3 s (20 tests), `DialogHintsWholeTest` 16.8 s (110), `TerminalScreenTest` 15.5 s (6), `FfmpegBitmapFormatsTest` 15.4 s (16), `DialogTitleFitsLanguageTest` 14.1 s (6), `LanguageDialogTest` 13.0 s (6).

#### Slowest ten tests, `iConsole.Test` (median of 3 runs)

| Test | Class | Duration (s) | Samples |
| --- | --- | --- | --- |
| `Follow_WaitsForTheEndOfALine_BeforeAddingIt` | `FileViewerFollowTest` | 2.26 | 3 |
| `ChangingASetting_BackAndForth_LeavesTheSamePictureAsSettingItOnce` | `ControlStaleTextSweepTest` | 1.95 | 3 |
| `ReadFileAsync_ForAPdf_NeverCarriesTheSignatureSection` | `FileToTextConverterPdfSignaturesTest` | 1.81 | 3 |
| `ConvertFile_Pdf_ContainsExpectedText` | `FileToTextConverterServiceFixtureTest` | 1.80 | 3 |
| `EveryLanguage_LocalizesBinaryMetadataAndDisassemblyOutput` | `BinaryViewerLanguageTest` | 1.74 | 3 |
| `BeforeItIsStarted_TheTimerDoesNotRun` | `DateTimerTest` | 1.50 | 3 |
| `AfterItIsDisposed_TheTextStopsChanging` | `DateTimerTest` | 1.50 | 3 |
| `ASignedPdf_GivesTheSignatureSection` | `FileToTextConverterPdfSignaturesTest` | 1.33 | 3 |
| `WhileTheTerminalIsLent_TimeStandsStill` | `NotificationJobTest` | 1.27 | 3 |
| `Spreadsheet_TooManyRowsAndColumns_AreTruncatedWithANote` | `OfficeTextRendererCoverageTest` | 1.27 | 3 |

Slowest classes by summed duration (mean of 3 runs): `PdfSignatureReaderTest` 10.7 s (34 tests), `FileToTextConverterServiceFixtureTest` 7.4 s (42), `FileViewerFollowTest` 6.8 s (6), `DisassemblyBuilderTest` 5.1 s (13), `ControlRobustnessSweepTest` 5.0 s (82), `OfficeVariantFixtureTest` 4.9 s (44).

Shape of the run: no test is the critical path. The longest test (7.3 s) is a third of the 21.8 s span of `iTerminal.Core.Test`, and 116 tests take more than a second. The two big projects have a flat tail of 1 to 4 second tests spread over 48 workers. `DoNotParallelize` classes (51 classes and 430 tests in `iTerminal.Core.Test`, 25 classes and 152 tests in `iConsole.Test`) take 15.5 s and 3.5 s of test time; in `iTerminal.Core.Test` the last 3.4 s of the span contain only those.

#### Start-up of the app

Process start to marks of `StartupTrace` (`iConsole/Diagnostics/StartupTrace.cs`), milliseconds since the process started, median of 3 runs. Cold is a new empty data folder; warm is the second start on the same folder. Network cut off (`bwrap --unshare-net`), `DataDirectory`, `HOME` and the start folder in a scratch folder, pseudo terminal 120 x 40.

| Mark | Cold (ms) | Warm (ms) | Samples |
| --- | --- | --- | --- |
| `main` (runtime up) | 393 | 391 | 3 + 3 (first set of starts, dialog still open) |
| `run` (host about to start) | 1 487 | 1 466 | 3 + 3 |
| `database-warmed` | 2 970 | 2 664 | 3 + 3 |
| `db-init-start` | 4 457 | 4 151 | 3 + 3 |
| `db-init-done` | 5 230 | 4 548 | 3 + 3 |
| `desktop-start` | 5 315 | 4 637 | 3 + 3 |
| `first-frame` | 5 963 | 5 353 | 3 + 3 |

`DatabaseInitializationHostedService` takes about 770 ms cold (creates the database) and about 400 ms warm. The three slow singletons built by `StartupWarmUp` finish at 3.0 to 3.3 s (`warmed-IAiChatService` 3 274 ms in one cold run). From `desktop-start` to the first frame it takes 650 ms cold and 700 ms warm; `TerminalScreen` base initialisation alone is 0.5 s (4 742 to 5 269 ms in the restart check). The first output byte reaches the terminal 2.7 to 3.1 s after the process was spawned, well before the first frame at 5.3 to 6.0 s.

#### Idle and steady state

Idle = the app at the file panels, first-run dependency dialog dismissed, nothing pressed.

| Measure | Value | Window | Samples |
| --- | --- | --- | --- |
| CPU, first 12 to 27 s after the first frame | 35.9 % of one core (34.2 to 38.4) | 15 s | 6 starts |
| CPU, steady (after 35 s) | 25.0 to 25.7 % of one core | 10 s | 2 windows (**single run**) |
| Thread `iConsole.Jobs.C*` (first of two) | 12.4 to 12.6 % | 10 s | 2 windows |
| Thread `iConsole.Jobs.C*` (second of two) | 4.7 % | 10 s | 2 windows |
| Thread `iConsole.Inputs*` (key reader) | 3.1 to 3.2 % | 10 s | 2 windows |
| Thread `.NET Tiered JIT` | 10.4 to 14 % in the first 30 s, 2.6 to 2.9 % later | 10 to 15 s | 6 + 2 |
| Terminal output at idle | 6.9 kB/s (6 912 to 6 945 B/s) | 15 s | 6 starts |
| Resident set at idle | 228 to 234 MB | at 27 s | 6 starts |
| Threads at idle | 44 to 45 | at 27 s | 6 starts |
| Syscalls, 10 s under `strace -c` | futex 50 089, `clock_gettime` 34 569, `read` 7 273 | 10 s | 1 (**single sample**, strace slows the process) |
| Processes started while idle | `git rev-parse --show-toplevel` every 3.0 s (7 in 20 s) | 20 s | 1 (**single sample**) |
| `/proc/self/mountinfo` opened while idle | 548 times in 20 s | 20 s | 1 (**single sample**) |

The two threads named `iConsole.Jobs.C*` are `ControlRendererJob` and `ConsoleBufferRendererJob`; the thread name is cut at 15 characters, so the table cannot say which is which.

#### Per-operation timings against the budgets

Driven through the pseudo terminal; the screen is rebuilt from the bytes the app wrote. "First output" is the time from sending the key to the first byte written; "visible result" is the time until the expected text is on the emulated screen (it includes the terminal round trip, not only the key handler). The budgets are 200 ms for a key handler and `Draw`, 3 000 ms for a content load. A folder with 10 000 files of 380 bytes (10 001 entries with `..`). Two scenario runs (a second run on a fresh data folder); the values are run 1 / run 2 where both exist.

| Operation | First output (ms) | Visible result (ms) | CPU (s) | Samples |
| --- | --- | --- | --- | --- |
| Enter on the 10 001-entry folder | 11 / 17 | 33 / 44 | 0.08 / 0.10 | 2 |
| Down, Home, End (single key) | 5 to 15 | not separate | 0.06 to 0.35 | 2 |
| 200 x Down sent at once | 6 / 7 | output stops after 664 / 668 | 0.98 / 1.07 | 2 |
| 30 x PageDown sent at once | 5 / 11 | output stops after 511 / 502 | 0.37 / 0.55 | 2 |
| Shift+F11 sort dialog open | 28 / 28 | 36 / 39 | 0.04 / 0.06 | 2 |
| Sort by size, 10 001 entries (`s` in the dialog) | 5 | 121 | 0.17 | 1 (**single sample**) |
| Shift+F1 command palette open | 85 / 90 | 101 / 113 | 0.14 / 0.15 | 2 |
| Mark all (10 000 rows) through the palette | 11 / 12 | 64 / 87 | 0.09 / 0.12 | 2 |
| F3 on a text file (viewer with text) | 49 | 219 | 0.48 | 1 (**single sample**) |
| F7 find dialog open | 19 | 135 | 0.22 | 1 (**single sample**) |
| Ctrl+F8 to the recycle bin drive | 29 | 34 | 0.07 | 1 (**single sample**) |
| Ctrl+F8 back to the 10 001-entry folder, no sort, no marks | 5 to 10 | 349, 357, 390 (re-enter by keys: 374) | 0.87 to 0.96 | 4 |
| Ctrl+F8 back to the 10 001-entry folder, sorted by size, 10 000 marked | 5 | 1 718 | 5.02 | 1 (**single sample**) |

"Output stops after" is the time to the last byte before 0.5 s of quiet; it includes the once-a-second repaint of finding 1, so it is an upper bound. The F3 viewer took 219 ms to show text, but its first output came after 49 ms; the key handler itself is therefore under the 200 ms budget, and the file read is a content load far under 3 000 ms. Every other operation shows first output within 49 ms, so none is over budget. Resident set grew from 242 to 247 MB at the start to 306 to 317 MB after the scenario (opening the big folder, the dialogs, the viewer, the recycle drive); whether that is live data or uncollected garbage was not measured, because no GC counters were available.

#### Database and caches

After the scenario run (fresh data folder):

| Item | Value | Samples |
| --- | --- | --- |
| SQLite file size | 1 970 176 bytes (1.9 MB) | 2 runs |
| Rows: `FolderContentsCache` / `Settings` / `DatabaseMetadata` | 3 / 17 / 1 | 2 runs |
| Rows: `FolderIndexCache`, `MusicLibrary`, `Documents`, `DocumentContents`, `FileLabels` | 0 each (those jobs had nothing to index in the scratch folders) | 2 runs |
| Second start on the same folder: `ModelHash`, file inode, file size, `FolderContentsCache` rows | all four unchanged (3 rows before and after) | 1 (**single sample**) |
| Shutdown (F10 to process exit) | 0.114 to 0.164 s, median 0.164 s | 6 starts |

The cached tables are not dropped on a second start, so the rule in `CLAUDE.md` about the schema stamp holds. Shutdown is far under the five seconds jobs get.

### Findings

1. **Both render loops and the key reader poll every millisecond, and the screen is repainted once a second at idle** - Impact: **High**. Area: idle, rendering.
   - Evidence: a started app with nothing pressed uses 25.0 to 25.7 % of one core in steady state and 34.2 to 38.4 % in the first 30 s (6 starts, `/proc/<pid>/stat`), and writes 6.9 kB/s to the terminal; one full frame of 120 x 40 cells is about that size. Per thread: 12.4 to 12.6 %, 4.7 % and 3.1 to 3.2 % for the two render threads and the key reader. `strace -c` over 10 s: 50 089 `futex` and 34 569 `clock_gettime` calls. The loops are [iConsole/Jobs/ControlRendererJob.cs:36](../../iConsole/Jobs/ControlRendererJob.cs) (`Thread.Sleep(1)` when idle), [iConsole/Jobs/ConsoleBufferRendererJob.cs:155](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) and [iConsole/Jobs/KeyboardEventPublisherJob.cs:45](../../iConsole/Jobs/KeyboardEventPublisherJob.cs); the repaint fallbacks are `FullRedrawFallbackInterval` of one second in [ControlRendererJob.cs:10](../../iConsole/Jobs/ControlRendererJob.cs) and [ConsoleBufferRendererJob.cs:19](../../iConsole/Jobs/ConsoleBufferRendererJob.cs), and `ForceRedraw` at [ConsoleBufferRendererJob.cs:190](../../iConsole/Jobs/ConsoleBufferRendererJob.cs) clears the last buffer so the next pass rewrites every cell.
   - Cause: each loop wakes about 1 000 times a second to ask whether anything changed, and the fallback marks the whole tree changed once a second and makes the diff renderer write the whole screen again, although only the clock in the menu bar changes.
   - Improvement: let the three loops wait on a signal (a `SemaphoreSlim` or `ManualResetEventSlim` that `Control` state changes, `Refresh`, a key event and the clock tick set) with a timeout of one second instead of `Sleep(1)`; keep the fallback repaint but at a long interval (for example 30 s), since the diff renderer already redraws changed cells and the tree-version repaint covers added and removed controls. Keep the `Buffer.SyncLock` and `ConsoleOwnership` brackets as they are.
   - Expected gain: idle CPU from about 25 % to under 5 % of a core and idle output from 6.9 kB/s to the few bytes of a clock tick (**estimate**: the three polling threads account for 20 of the 25 points; the rest is the job threads and the clock). On a laptop this is battery and fan; over SSH it is 7 kB/s of traffic for nothing.
   - Effort: M. The wake-up source for "something changed" has to be found for every control path, so partly **unverified**: the split of the 25 % between the two render threads and the cost of one empty pass were read from thread times, not from a sampled trace.

2. **Every `dotnet test` of a project pays about 9 to 12 s before the first test** - Impact: **High** (35 to 50 % of a project run). Area: test.
   - Evidence: `iConsole.Inputs.Posix.Test` has 7 tests that take 0.2 to 0.4 s and its run takes 9.6 s wall (3 runs). `iConsole.Test` takes 24.3 s wall for 12.2 s of tests, `iTerminal.Core.Test` 33.9 s for 21.8 s. Listing the tests of the big projects without running them takes 9.0 to 9.6 s with `dotnet vstest -lt` and 11.7 to 13.2 s with `dotnet test --list-tests`.
   - Cause: both big projects use the VSTest adapter (`MSTest.TestAdapter` in [iConsole.Test/iConsole.Test.csproj:22](../../iConsole.Test/iConsole.Test.csproj)), so each run starts the VSTest console and a test host and discovers all tests again; MSBuild project evaluation adds about 2 s (`dotnet msbuild` took 1.9 s).
   - Improvement: move the test projects to the MSTest runner (Microsoft.Testing.Platform: the test project becomes an executable and runs without VSTest, `EnableMSTestRunner` in `Directory.Build.props` or per project). It is part of MSTest, not a new dependency. Keep the same TRX and coverage reports used by the workflows, and check `ci.runsettings` and `coverage.runsettings` still apply.
   - Expected gain: 8 to 10 s per project run, so `iConsole.Test` from about 24 s to about 15 s wall (**estimate** from the 7-test project, 9.6 s wall for 0.4 s of tests). `dotnet test iTerminal.slnx` runs the projects side by side, so the saving on the whole solution is smaller; that was not measured.
   - Effort: M. **Unverified** on this repository: no runner switch was tried.

3. **A `git rev-parse` process starts every 3 seconds while the app is idle** - Impact: **Medium**. Area: idle.
   - Evidence: `strace -f -e trace=execve` for 20 s at the file panels in a folder that is not a repository: `git rev-parse --show-toplevel` at 15:53:32.098, :35.102, :38.105, :41.102, :44.102, :47.103 and so on (every 3.0 s); `wait4` and `vfork` counted 4 in a 12 s window under `strace -c`.
   - Cause: `GitLens.ScheduleRefreshIfNeeded` is called from the draw path and starts a refresh whenever `RefreshInterval` (2 s, [iTerminal.Core/Controls/Lenses/GitLens.cs:18](../../iTerminal.Core/Controls/Lenses/GitLens.cs)) has passed, also when the last answer was "not a repository" ([GitLens.cs:109](../../iTerminal.Core/Controls/Lenses/GitLens.cs), `RefreshStateAsync` at [GitLens.cs:156](../../iTerminal.Core/Controls/Lenses/GitLens.cs) runs `GetRepositoryRootAsync` first). The draw cadence of finding 1 probably makes the effective period 3 s (**unverified**).
   - Improvement: remember a negative answer per directory and ask again only when the directory changes or after a long interval (for example 60 s); for a repository, refresh on the panel's own refresh and file-system event rather than on a clock. The run stays on a background task, so no key handler is touched.
   - Expected gain: 0.33 process starts per second removed at idle, about 20 per minute (**estimate**: the cost of one start was not timed; a spawn of `git` takes some milliseconds of CPU plus the fork of a 230 MB process).
   - Effort: S.

4. **Start-up takes 5.4 to 6.0 s to the first frame, and 2.7 to 3.0 s of it lie between `run` and the database initializer** - Impact: **Medium**. Area: start-up.
   - Evidence: median `first-frame` 5 963 ms cold and 5 353 ms warm (3 runs each; range 5 282 to 6 095 ms). `main` at 391 to 397 ms, `run` at 1 466 to 1 487 ms, `db-init-start` at 4 151 to 4 457 ms, `desktop-start` at 4 637 to 5 315 ms. The three singletons in `StartupWarmUp` are done by 3.0 to 3.3 s, so about 1.1 to 1.4 s pass between the last warm-up and `db-init-start`. Marks are placed in [iTerminal/Program.cs:27](../../iTerminal/Program.cs) to `:75`, [DatabaseInitializationHostedService.cs:31](../../iTerminal.Core/Application/DatabaseInitializationHostedService.cs) and `:52`, and [StartupWarmUp.cs:37](../../iTerminal.Core/Application/StartupWarmUp.cs).
   - Cause: before the host calls the first `StartAsync`, it builds all six hosted services ([iTerminalApplicationBuilderExtension.cs:151](../../iTerminal.Core/Application/iTerminalApplicationBuilderExtension.cs) to `:590`) and their dependencies on one thread; what remains in that chain after the three warmed services is not marked. This is a reading of the order of the marks, so the cause is **unverified**. The build measured is JIT-compiled and not ReadyToRun, so part of the time is JIT (the `.NET Tiered JIT` thread used 10 to 14 % of a core in the first 30 s).
   - Improvement: first add `StartupTrace.Mark` calls around each hosted service construction and `StartAsync` and around the biggest singleton constructors, then add the slowest ones found to `StartupWarmUp.SlowServices` or make them lazy. The same trace on a published ReadyToRun build will show what JIT costs in the shipped app.
   - Expected gain: up to 1 s of the 5.4 to 6.0 s (**estimate**, the size of the unexplained gap); cold start also pays 0.37 s more in the database initializer (770 ms against 400 ms).
   - Effort: S for the trace marks, M for the fix.

5. **Tests that make a temporary SQLite database through EF take 2.5 to 2.8 s each** - Impact: **Medium**. Area: test.
   - Evidence: `CacheServiceTest` has four tests of 2.77 to 2.79 s ([CacheServiceTest.cs:27](../../iTerminal.Core.Test/Services/CacheServiceTest.cs) calls `EnsureCreated` in `[TestInitialize]`); `TerminalScreenTest` has six tests of about 2.5 s whose body is a key press, and each builds `new SettingsService()` ([TerminalScreenTest.cs:28](../../iTerminal.Core.Test/Screens/TerminalScreenTest.cs)), which creates a temporary database ([SettingsService.cs:31](../../iTerminal.Core/Services/SettingsService.cs) and `:148`). 46 test files call `new SettingsService()` (123 call sites) and 8 call `EnsureCreated`; the 73 classes in those files hold 494 tests and 141 s of the 533 s test time.
   - Cause: every test builds a new database file and runs the EF schema creation for the whole model, under 48 parallel workers.
   - Improvement: create one template database in `TestAssemblySetup` (`[AssemblyInitialize]`) and give `TemporaryDbContextFactory` and these fixtures a `File.Copy` of it, or share a read-only one per class with `[ClassInitialize]` where the tests do not write.
   - Expected gain: 2 s or more per affected test; of the 141 s of test time in these classes perhaps 50 to 100 s is set-up, which at 24.9 busy workers is 2 to 4 s of the 21.8 s span (**estimate**; more on a runner with fewer cores). Not every test in those classes pays the full 2.5 s: the split was not measured per test.
   - Effort: M. **Unverified**: that the schema creation, and not the first EF model build, is the cost was not separated.

6. **`DriveInfoJob` reads `/proc/self/mountinfo` about 27 times a second on this machine** - Impact: **Low**. Area: idle.
   - Evidence: 548 opens of `/proc/self/mountinfo` and 380 `readlink("/snap")` calls in a 20 s `strace`, from a pass every 5 s ([DriveInfoJob.cs:97](../../iTerminal.Core/Jobs/DriveInfoJob.cs), `WaitOne(5000)`) that calls `DriveInfo.GetDrives()` ([DriveInfoJob.cs:46](../../iTerminal.Core/Jobs/DriveInfoJob.cs)) and then asks each drive for readiness, size and label.
   - Cause: on Linux every `DriveInfo` property read parses the mount table again, and this machine has many mounts (snap images), so one pass is about 137 opens.
   - Improvement: read the property values once per drive per pass and skip a pass when the mount table has not changed (compare the size and mtime of `/proc/self/mountinfo`, or poll it for changes), or lengthen the interval when nothing changed.
   - Expected gain: the burst of file opens every 5 s goes away; the CPU saving was not measured (**estimate**: small on a machine with few mounts).
   - Effort: S.

7. **`TheComputerNeverLosesAtTicTacToe` is the longest test, 7.3 s** - Impact: **Low**. Area: test.
   - Evidence: 7.32 s median, 6.6 to 7.8 s in 3 runs ([RulesInvariantsTest.cs:149](../../iTerminal.Core.Test/Services/Games/RulesInvariantsTest.cs)): 200 games, each move a full negamax search of the board ([TicTacToeGame.cs:194](../../iTerminal.Core/Services/Games/TicTacToeGame.cs)).
   - Cause: the search has no memo, and all 200 games run in one test on one worker.
   - Improvement: memoize `BestMove` positions in the game (a 3^9 table) or split the seeds into `[DynamicData]` cases of 25 games so the workers share them.
   - Expected gain: the longest test from 7.3 s to under 1 s; the 21.8 s span shrinks little because other tests fill the workers (**estimate** 0 to 1 s).
   - Effort: S.

8. **PDF signature tests make new 2 048-bit RSA keys for every test** - Impact: **Low**. Area: test.
   - Evidence: `PdfSignatureReaderTest` 34 tests, 10.7 s; `FileToTextConverterPdfSignaturesTest` 7 tests, 4.5 s; keys made with `RSA.Create(2048)` at [PdfSignatureReaderTest.cs:18](../../iConsole.Test/Pdf/PdfSignatureReaderTest.cs), `:27` and `:33`.
   - Cause: key generation costs hundreds of milliseconds under load, and each test builds its own certificate chain.
   - Improvement: build the key and the chain once in `[ClassInitialize]` where tests only read them and sign with the cached key.
   - Expected gain: up to 10 s of the 150 s summed test time of `iConsole.Test`, about 0.5 s of its span (**estimate**).
   - Effort: S.

9. **Fixed waits of 1.1 to 1.5 s in `FileViewerFollowTest`, `DateTimerTest` and `NotificationJobTest`** - Impact: **Low**. Area: test.
   - Evidence: `FileViewerFollowTest` has 6 tests of 1.1 to 2.3 s, each with `await Task.Delay(1100)` per check ([FileViewerFollowTest.cs:74](../../iConsole.Test/Dialogs/FileViewerFollowTest.cs)); `DateTimerTest` two tests of 1.5 s with `await Task.Delay(1500)` to prove that nothing happens ([DateTimerTest.cs:62](../../iConsole.Test/Controls/StatusBar/DateTimerTest.cs) and `:75`); `NotificationJobTest.WhileTheTerminalIsLent_TimeStandsStill` 1.27 s.
   - Cause: the tests wait out a real interval of the code under test instead of advancing an injected clock; `DateTimerTest` already has a `Clock` it advances.
   - Improvement: give the follow check an injected interval or time source (as `Date` has a clock), and replace the "nothing happens" delay with a few timer ticks on a fake clock.
   - Expected gain: about 12 s of the 150 s summed time of `iConsole.Test`; the span is not affected unless the tests are on the tail (**estimate** 0 to 1 s).
   - Effort: S to M.

10. **The OAuth sign-in test waits out real poll intervals, 4.1 s** - Impact: **Low**. Area: test.
    - Evidence: [OAuthTokenProviderTest.cs:128](../../iTerminal.Core.Test/Services/Cloud/OAuthTokenProviderTest.cs) answers `"interval":1`, and [OAuthTokenProvider.cs:295](../../iTerminal.Core/Services/Cloud/OAuthTokenProvider.cs) clamps the interval to at least one second and sleeps it at `:306` before each of the four polls. The test already injects `slowDownStep: TimeSpan.Zero` for the slow-down part.
    - Cause: the base poll interval has a one second floor and no injection point.
    - Improvement: add a constructor parameter for the minimum poll interval, next to `slowDownStep`, and pass a few milliseconds from the test.
    - Expected gain: 4.1 s to about 0.1 s for this test (**estimate**).
    - Effort: S.

11. **`RecordingTest` sleeps 3.5 s for a process that is gone after 1.2 s** - Impact: **Low**. Area: test.
    - Evidence: 3.91 s median ([RecordingTest.cs:201](../../iConsole.Media.Speech.Test/RecordingTest.cs) to `:209`): the script runs `sleep 1.2` and the test calls `Thread.Sleep(3500)` before `Stop`.
    - Cause: a fixed sleep stands in for "the recorder has exited".
    - Improvement: wait until the capture reports the exit (poll the process state with a short step and a deadline), or shorten the script to the probe time plus a small margin.
    - Expected gain: about 2.3 s for this test (**estimate**).
    - Effort: S.

12. **Returning to a 10 001-entry folder that is sorted by size and has 10 000 marked rows takes 1.7 s** - Impact: **Low** (under the 3 000 ms budget). Area: rendering, allocation.
    - Evidence: Ctrl+F8 away and back took 1 718 ms to show the folder again, with 5.02 s of CPU in that window (**single sample**). Without sort and marks the same step took 349, 357 and 390 ms with 0.87 to 0.96 s of CPU.
    - Cause: not found. The extra 1.4 s and 4 CPU seconds come with the sort or with restoring 10 000 marks; the two were not separated.
    - Improvement: repeat with the sort alone and the marks alone; then look at where the sort (`ContentModelBase.FilteredEntries`, [file-operations.md](../architecture/file-operations.md)) or the mark restore is done per entry rather than once per swap.
    - Expected gain: unknown until the cause is found; the budget is not at risk now, but 100 000 entries would scale this badly (**estimate**).
    - Effort: S to find, then S to M. **Unverified**.

13. **The serial test classes stretch the end of `iTerminal.Core.Test`** - Impact: **Low**. Area: test.
    - Evidence: 51 `DoNotParallelize` classes with 430 tests take 15.5 s of test time (the largest: `ContentModelMusicCatalogTest` 2.4 s, `AiChatClientFactoryTest` 2.3 s, `SystemSettingsServiceTest` 1.9 s, `ContentModelMusicPlaylistTest` 1.9 s); the parallel tests are done at 18.0 s of a 21.4 s span.
    - Cause: classes that change process-wide state must run alone, as `Documentation/architecture/testing.md` says; several also pay the temporary database of finding 5.
    - Improvement: after finding 5, check each of the four largest classes for a documented reason; a class that only needs its own database can lose `[DoNotParallelize]`. Do not remove it from a class that swaps `Console.In` or flips a static test switch.
    - Expected gain: up to 3 s of the 21.8 s span (**estimate**, the idle tail after the parallel tests end).
    - Effort: M.

### What is already fast

- Opening a 10 001-entry folder shows the list in 33 to 44 ms; single key presses on it show output in 5 to 15 ms; sorting it takes 121 ms and marking all rows 64 to 87 ms (table above).
- Shutdown takes 0.11 to 0.16 s (6 starts), against the five seconds jobs get.
- The cached tables survive a second start; the schema stamp, file inode and rows were unchanged (single sample).
- Start-up already starts the EF warm-up and three slow singletons in parallel (`DatabaseWarmUp`, `StartupWarmUp`); `database-warmed` is at 2.7 to 3.0 s, before the first frame, not after it.
- The unit tests are already parallel (48 workers, 24.9 tests on average at once) and only 1 test is over 5 s in 6 547.
- Polling intervals of the other jobs are long: `OllamaServerJob` 5 s, `MusicSessionJob` 5 s, `MusicLibraryIndexJob` 5 s, `DocumentLibraryIndexJob` 10 s, `ToDoReminderJob` 5 s, `CalendarIdentityJob` 5 s, `InternetConnectivityJob` 60 s online and 15 s offline, `SettingsSynchronizationJob` 1 s, `WeatherJob` 15 min, `RuntimeDependencyCheckJob` 12 h. In the idle thread times none of these shows above 0.7 % (`iTerminal.Core.*` threads 0.4 to 0.7 %, thread-pool workers 0.3 to 0.6 %).

### Suggested order

1. Quick wins (S): finding 3 (git probe), finding 11 (`RecordingTest` sleep), finding 10 (OAuth poll interval), finding 7 (TicTacToe), finding 8 (PDF keys), finding 6 (mount table).
2. High: finding 1 (idle render and key loops) first, because it is the cost of every minute the app is open; then finding 2 (test host floor), which saves 9 to 10 s on every test run.
3. The rest: finding 4 (add the start-up marks first, then fix), finding 5 (template database), finding 9 and 13 (waits and serial classes), finding 12 (after a second measurement).

### How to reproduce

All commands run from the repository root. Put traces, TRX files and the scripts in a scratch folder outside the repository; nothing generated is committed.

1. Build once: `dotnet build iTerminal.slnx -c Release`, and `dotnet build iConsole.Inputs.Posix.Test/iConsole.Inputs.Posix.Test.csproj -c Release` (it is not built by the solution).
2. Tests, three runs per project: `dotnet test <project> -c Release --no-build --logger "trx;LogFileName=<name>_<n>.trx" --results-directory <scratch>/trx`, timed with `/usr/bin/time -f %e`. A short Python script (run with `python3 -I`) reads each TRX: for every `UnitTestResult` it takes `testName`, `duration`, `outcome`, `startTime`, `endTime`, maps `testId` to the class through `UnitTest/TestMethod@className`, and prints the count, the sum, the span from the first start to the last end, the numbers over 1 s and 5 s, the slowest tests and the classes by summed duration. The medians over the three runs are taken per (class, test name). Fixed cost: the same `dotnet test` with `--list-tests`, `dotnet vstest <dll> -lt`, and the 7-test `iConsole.Inputs.Posix.Test`.
3. The app: a Python harness opens a pseudo terminal (`pty.openpty`, window size 120 x 40 through `TIOCSWINSZ`), starts `bwrap --bind / / --dev-bind /dev /dev --unshare-net --die-with-parent dotnet iTerminal/bin/Release/net11.0/iTerminal.dll` in a new session with `cwd` set to a scratch folder and the environment `DataDirectory=<scratch>/data`, `HOME=<scratch>/home`, `XDG_CONFIG_HOME=<scratch>/home/.config`, `ITERMINAL_STARTUP_TRACE=<scratch>/trace.txt`, `TERM=xterm-256color`. A reader thread keeps every byte and feeds a small screen emulator (cursor positioning, erase, text; about 60 lines) so the harness can wait for text on screen. `bwrap` is used because there is no other way to cut the network (`unshare -rn` is refused here); without it the app probes `1.1.1.1:443` after 5 s.
4. A new data folder shows the runtime-dependency dialog on top of the panels, which swallows F10. The harness sends `s` (skip the ticked offers and close) 9 s after the process starts, so nothing is installed, and waits 3 s. Never press Enter there: it moves to the next page of an install offer.
5. Start-up: read `trace.txt` (written at the first frame); cold = a new data folder, warm = the same folder again, three pairs. Idle: 12 s after the dialog is closed, read `utime + stime` and `Threads` and `VmRSS` from `/proc/<pid>/stat` and `status` and the per-thread CPU from `/proc/<pid>/task/*/stat` over 15 s, and divide the bytes the harness read by the window. Find the .NET pid with `pgrep -P <bwrap pid> dotnet`. Shutdown: send F10 (`ESC [ 2 1 ~`) and wait for the process to exit.
6. Syscalls and processes: `strace -f -c -p <pid> -o <file>` for 10 s, and `strace -f -tt -e trace=execve,openat,readlink -p <pid>` for 20 s, stopped with SIGINT.
7. Operations: a folder `parent/big` with 10 000 files (`file_00000.txt` to `file_09999.txt`, 20 lines each) and `parent/notes.txt`, the app started in `parent`. Keys are sent as terminal bytes (Down `ESC [ B`, Enter `CR`, Shift+F11 `ESC [ 2 3 ; 2 ~`, Shift+F1 `ESC [ 1 ; 2 P`, F3 `ESC O R`, F7 `ESC [ 1 8 ~`, Ctrl+F8 `ESC [ 1 9 ; 5 ~`). For each key the harness records the time to the first byte written and the time until a regular expression matches the emulated screen (`1000[01] entries` for the folder, `Sort: size`, `10000 marked`, and so on), plus the CPU time of the process in that window. Mark all goes through the palette (type `mark all`, Enter), because Num* has no distinct code on a Linux terminal.
8. Database: after F10, open `<scratch>/data/iTerminal.sqlite` read-only with Python's `sqlite3`, count rows per table, then start the app again on the same folder and compare `DatabaseMetadata`, the inode and the size.
9. Afterwards `git status --short` must show only this document (`scratchpad/` in the repository root was there before this run and is not part of it), and no `iTerminal.dll` or `bwrap` process may be left.

## P2 2026-11-22: unit tests (all projects that build on Linux) and the running application

Scope `tests` and `app`, chosen because the request named neither; the end-to-end suite (`iTerminal.Test`) was not run, as asked. Branch `docs/profiling-tests-app` (cut from `main`), commit `b9b81269`. Machine: Linux 7.3.0-11-generic, 48 CPUs, 499 GB RAM, .NET SDK 11.0.100, the same machine class as P1, so the numbers can be compared with it. Tools: none of `dotnet-trace`, `dotnet-counters` or `dotnet-gcdump` is installed (`dotnet tool list -g` lists only `dotnet-ef`) and nothing was installed; `dotnet-trace` would have given the exclusive frames behind findings 1 and 17. What was used: the TRX files of `dotnet test`, the app's own `StartupTrace` (`ITERMINAL_STARTUP_TRACE`), `/proc/<pid>` counters per process and per thread, `strace -e trace=execve` on the app process, `bwrap --unshare-net` so the app had no network, and a pseudo terminal driven from a Python script. Not measured: GC collection counts, allocation rate and thread-pool queue length (need `dotnet-counters`); CPU and allocation hot spots by frame (need `dotnet-trace` and `dotnet-gcdump`); the published ReadyToRun build; the end-to-end suite; `iConsole.Inputs.Windows.Test` and `iTerminal.Specifications.Test` (Windows only, and a boot of the whole app per scenario); `iConsole.Test.Core` (a helper library, it holds no tests); docker, music, MTP and remote drives. The 10 001-entry folder timings were taken in two runs, and the "visible result" column is only filled where the screen pattern matched. Everything ran under a scratch folder with `DataDirectory`, `HOME` and the start folder there; the user's own data folder was not touched.

### Summary

- `BlockingCompletionModelTest.AnOpeningUpdateThatFinishesAfterAKeystrokeDoesNotCloseTheList` took 32.2 s in 2 of 3 runs and 0.2 s in the third, and stretched `iConsole.Test` from a 13.2 s test span to 39.0 s (wall 48.8 s against 25.3 s). It is a race in the test's own model, which waits out a 30 s gate (finding 14). In P1 this class took no such time.
- `iConsole.Media.Video.Test`, `iConsole.Media.Audio.Test` and `iConsole.Media.Speech.Test` run their tests one after another (no `Parallelize` attribute): the sum of test durations equals the span (11 s against 11.7 s for Video, 12 s against 12.3 s for Audio). One test in the Audio project waits out a real 5 s process timeout and takes 5.85 s of its 12.3 s span (findings 15 and 16).
- The idle app still uses 29.3 % of one core warm (27.4 to 31.3 %) and writes 7.3 kB/s with nothing happening, and 43 to 46 % of one core once a 10 001-entry folder is shown (findings 1 and 17). `git rev-parse` still starts every 3 s (finding 3).
- Start-up to the first frame takes 5.67 s cold and 5.06 s warm (medians of 3), 0.3 s less than in P1; about 3 s of it lies between `run` and `db-init-start`. Shutdown takes 0.22 s.
- Every project still pays 8 to 11 s of fixed cost before the first test: 7 tests take 9.8 s wall for 0.4 s of tests (finding 2). All 12 373 tests that ran passed; 46 of 6 421 in `iTerminal.Core.Test` and 3 in other projects did not execute.
- All measured key presses show first output within 96 ms, and the 10 001-entry folder is on screen within 57 to 80 ms of Enter, so no budget is at risk.

### Measurements

#### Build

| Step | Time (s) | Samples |
| --- | --- | --- |
| `dotnet build iTerminal.slnx -c Release` (first build in this checkout, 1 808 warnings, 0 errors) | 95.3 | 1 (**single sample**) |
| `dotnet build iConsole.Inputs.Posix.Test -c Release` (not in the solution build) | 5.5 | 1 (**single sample**) |

The P1 build number (25.1 s) was an incremental build and is not comparable.

#### Unit tests per project

`dotnet test <project> -c Release --no-build`, three runs each, projects run one after another so none shared the CPUs with another. "Fixed" is median wall-clock minus median test span (host start-up, discovery, report). "Sum" is the sum of test durations.

| Project | Tests | Wall-clock (s), 3 runs | Median wall (s) | Test span (s), median | Sum (s), median | Fixed (s) | Over 1 s / over 5 s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `iConsole.Test` | 5 073 (0 skipped) | 48.8, 49.9, 25.3 | 48.8 | 39.0 | 143 | 9.8 | 19 / 1 |
| `iTerminal.Core.Test` | 6 421 (46 not executed) | 26.2, 28.7, 27.1 | 27.1 | 16.7 | 414 | 10.4 | 62 / 0 |
| `iConsole.Formats.Test` | 576 (0 skipped) | 12.4, 12.5, 11.9 | 12.4 | 3.0 | 87 | 9.4 | 8 / 0 |
| `iConsole.Media.Audio.Test` | 40 (0 skipped) | 20.5, 20.0, 20.1 | 20.1 | 12.3 | 12 | 7.7 | 1 / 1 |
| `iConsole.Media.Speech.Test` | 27 (1 skipped) | 17.4, 27.3, 15.1 | 17.4 | 6.5 | 6 | 10.9 | 2 / 0 |
| `iConsole.Media.Video.Test` | 114 (1 skipped) | 20.3, 21.0, 20.8 | 20.8 | 11.7 | 11 | 9.1 | 0 / 0 |
| `iConsole.Inputs.Posix.Test` | 7 (1 skipped) | 9.9, 9.8, 9.8 | 9.8 | 0.4 | 0 | 9.4 | 0 / 0 |
| `iTerminal.Core.Games.Test` | 164 (0 skipped) | 12.3, 12.3, 12.3 | 12.3 | 3.2 | 24 | 9.1 | 7 / 0 |

`iTerminal.Core.Games.Test` is new since P1 (the game engines and their tests moved out of `iTerminal.Core.Test`, which is why that project's sum fell from 543 s to 414 s and its span from 21.8 s to 16.7 s). `iConsole.Formats.Test` is new too (the format readers moved out of `iConsole.Test`). The `iConsole.Test` span without the race of finding 14 is 13.2 s (run 3). The Speech wall of 27.3 s in run 2 is a spread I cannot explain (the test span of that run was 6.4 s); it is one of three runs.

#### Slowest ten tests, `iConsole.Test` (median of 3 runs)

| Test | Class | Duration (s) | Samples |
| --- | --- | --- | --- |
| `AnOpeningUpdateThatFinishesAfterAKeystrokeDoesNotCloseTheList` | `BlockingCompletionModelTest` | 32.22 | 3 (32.2, 32.2, 0.2) |
| `ChangingASetting_BackAndForth_LeavesTheSamePictureAsSettingItOnce` | `ControlStaleTextSweepTest` | 2.59 | 3 |
| `Follow_WaitsForTheEndOfALine_BeforeAddingIt` | `FileViewerFollowTest` | 2.25 | 3 |
| `ProcessorUseShowsWorkDoneBetweenSamples` | `ProcessServiceTest` | 1.78 | 3 |
| `EveryLanguage_LocalizesBinaryMetadataAndDisassemblyOutput` | `BinaryViewerLanguageTest` | 1.54 | 3 |
| `BeforeItIsStarted_TheTimerDoesNotRun` | `DateTimerTest` | 1.50 | 3 |
| `AfterItIsDisposed_TheTextStopsChanging` | `DateTimerTest` | 1.50 | 3 |
| `ProcessorUseIsMeasuredBetweenSamplesAndStaysWithinRange` | `ProcessServiceTest` | 1.48 | 3 |
| `EveryControl_SurvivesDrawKeysAndClicks_AtAwkwardSizes` | `ControlRobustnessSweepTest` | 1.36 | 3 |
| `WhileTheTerminalIsLent_TimeStandsStill` | `NotificationJobTest` | 1.29 | 3 |

Slowest classes by summed duration (run 1): `BlockingCompletionModelTest` 32.4 s (5 tests), `FileViewerFollowTest` 6.9 s (6), `ControlRobustnessSweepTest` 6.4 s (83), `ProcessServiceTest` 5.8 s (5), `ControlPropertySweepTest` 5.1 s (105).

#### Slowest ten tests, `iTerminal.Core.Test` (median of 3 runs)

| Test | Class | Duration (s) | Samples |
| --- | --- | --- | --- |
| `Cancellation_EndsALongRun (Bash)` | `LocalShellSessionTest` | 4.23 | 3 |
| `SignIn_PollsThroughPendingAndSlowDown_ThenStoresTheTokens` | `OAuthTokenProviderTest` | 4.12 | 3 |
| `Cancellation_EndsALongRun (Sh)` | `LocalShellSessionTest` | 4.08 | 3 |
| `WhilePlayingTheWidgetTakesRoomFromTheQuotesFirst` | `TerminalMenuBarTest` | 3.69 | 3 |
| `Switch_IsRefusedWhileACommandRuns` | `LocalShellSessionServiceTest` | 3.57 | 3 |
| `BubblewrapSession_CanWriteItsFolder_ButNotTheRestOfTheSystem` | `SessionSandboxesTest` | 3.46 | 3 |
| `AFreshQuote_IsServed` | `CacheServiceTest` | 3.07 | 3 |
| `AQuoteOlderThanTheTtl_IsNotServed` | `CacheServiceTest` | 3.06 | 3 |
| `AnOldQuoteBesideAFreshOne_IsLeftOut_NotServedAsCurrent` | `CacheServiceTest` | 3.06 | 3 |
| `ASetWeatherReading_IsReadBack` | `CacheServiceTest` | 3.02 | 3 |

Slowest classes (run 1): `LocalShellSessionTest` 40.1 s (16 tests), `ProcessManagerDialogTest` 18.8 s (13), `TerminalScreenTest` 17.4 s (6), `LanguageDialogTest` 13.2 s (6), `CacheServiceTest` 12.2 s (4 slow of 27). The 42 tests over 2 s (116 s of the 414 s) nearly all start between 1.2 and 2.1 s after the first test and finish 2.8 to 3.1 s later, so they are one wave that waits for the same one-time start-up, not 42 separate costs (see finding 5 in the status list).

#### Slowest tests of the other projects (median of 3 runs)

| Test | Project | Duration (s) | Samples |
| --- | --- | --- | --- |
| `GetDevices_GivesUpOnAHungAplayInsteadOfWaitingForIt` | `iConsole.Media.Audio.Test` | 5.85 | 3 |
| `ARecorderThatDiesAfterTheStartProbe_IsReportedOnStop_NotSilentlyIgnored` | `iConsole.Media.Speech.Test` | 3.91 | 3 |
| `TheComputerNeverLosesAtTicTacToe` | `iTerminal.Core.Games.Test` | 2.51 | 3 |
| `UndoTakesBackBothMovesAgainstTheComputer` | `iTerminal.Core.Games.Test` | 2.08 | 3 |
| `TheComputerAnswersTheOpeningWithinItsTimeBudget` | `iTerminal.Core.Games.Test` | 2.08 | 3 |
| `ReadFileAsync_ForAPdf_NeverCarriesTheSignatureSection` | `iConsole.Formats.Test` | 1.92 | 3 |
| `ConvertFile_Pdf_ContainsExpectedText` | `iConsole.Formats.Test` | 1.91 | 3 |
| `EachFfmpegBitmap_DecodesThroughFfmpeg (".ppm")` | `iConsole.Media.Video.Test` | 0.98 | 3 |

#### Start-up of the app

Process start to marks of `StartupTrace` ([iConsole/Diagnostics/StartupTrace.cs](../../iConsole/Diagnostics/StartupTrace.cs)), milliseconds since the process started, median of 3. Cold is a new empty data folder; warm is the second start on the same folder. Network cut off (`bwrap --unshare-net`), pseudo terminal 120 x 40.

| Mark | Cold (ms) | Warm (ms) | Samples |
| --- | --- | --- | --- |
| `main` (runtime up) | 506 | 493 | 3 + 3 |
| `run` (host about to start) | 1 547 | 1 495 | 3 + 3 |
| `database-warmed` | 3 104 | 2 753 | 3 + 3 |
| `db-init-start` | 4 226 | 3 838 | 3 + 3 |
| `db-init-done` | 4 930 | 4 218 | 3 + 3 |
| `desktop-start` | 5 022 | 4 301 | 3 + 3 |
| `terminal-screen-base-init` | 5 513 | 4 902 | 3 + 3 |
| `first-frame` | 5 667 (5 511 to 5 833) | 5 059 (4 928 to 5 091) | 3 + 3 |
| First byte on the terminal | 3 196 (3 103 to 3 337) | 2 860 (2 806 to 2 987) | 3 + 3 |

`DatabaseInitializationHostedService` takes about 700 ms cold and about 380 ms warm (`db-init-start` to `db-init-done`). The three slow singletons of `StartupWarmUp` finish at 3.4 to 3.7 s (`warmed-IAiChatService` last); between that and `db-init-start` still pass about 0.6 to 1.0 s. From `desktop-start` to the first frame it takes 650 ms cold and 760 ms warm, of which `TerminalScreen` base initialisation alone is 490 to 600 ms. In P1 the cold and warm first frames were 5 963 and 5 353 ms, so both are about 300 ms faster here; the marks for each hosted service that P1 asked for in finding 4 are still not in the code.

#### Idle and steady state

Idle = the app at the file panels, the first-run dependency dialog closed with `s`, nothing pressed. The window starts about 19 s after the dialog is closed and lasts 20 s; "warm" runs have no dialog and start the window at about the same age.

| Measure | Value | Window | Samples |
| --- | --- | --- | --- |
| CPU, warm start, small folder | 29.3 % of one core (27.4, 31.3, 29.3) | 20 s | 3 |
| CPU, cold start, small folder | 42.7 % of one core (43.5, 42.7, 30.7; the first minute still has JIT and indexing) | 20 s | 3 |
| CPU, a 10 001-entry folder shown | 43.2 and 46.0 % of one core | 10 s | 2 |
| Thread `iConsole.Jobs.C*` (first of two), warm | 13.5 to 14.6 % | 20 s | 3 |
| Thread `iConsole.Jobs.C*` (second of two), warm | 5.0 to 5.4 % | 20 s | 3 |
| Thread `iConsole.Inputs*` (key reader), warm | 3.3 to 3.6 % | 20 s | 3 |
| Thread `.NET Tiered JIT`, warm | 1.4 to 5.1 % | 20 s | 3 |
| Terminal output at idle | 7.28 to 7.31 kB/s | 20 s | 6 starts |
| Resident set at idle | 225 to 227 MB | at about 40 s | 6 starts |
| Threads at idle | 45 to 47 | at about 40 s | 6 starts |
| `git rev-parse --show-toplevel` started while idle | at 16.28, 19.28, 22.28 and 25.29 s (every 3.0 s) | 12 s | 1 (**single sample**) |

The two threads named `iConsole.Jobs.C*` are `ControlRendererJob` and `ConsoleBufferRendererJob`; the thread name is cut at 15 characters, so the table cannot say which is which. Resident set after the 10 001-entry scenario grew from 236 to 276 MB.

#### Per-operation timings against the budgets

Driven through the pseudo terminal in a folder `big` with 10 000 files (10 001 entries with `..`), fresh data folder, two scenario runs. "First output" is the time from sending the key to the first byte written, which is an upper bound for the key handler. "Visible result" is the time until a screen pattern matched; it is only given where the pattern matched, and it includes the terminal round trip. The budgets are 200 ms (key handler, `Draw`) and 3 000 ms (content load).

| Operation | First output (ms) | Visible result (ms) | Samples |
| --- | --- | --- | --- |
| Down onto the folder row | 31, 45 | not matched | 2 |
| Enter on the 10 001-entry folder | 16, 20 | 80, 57 | 2 |
| Down, End, Home (single key) | 3 to 7 | not matched | 2 |
| 200 x Down sent at once | 3, 6 | not matched | 2 |
| 30 x PageDown sent at once | 6, 9 | not matched | 2 |
| Shift+F11 sort dialog open | 31, 36 | 47, 47 | 2 |
| Sort by size (`s` in the dialog) | 1, 25 | 1 001, 1 028 (see below) | 2 |
| Shift+F1 command palette open | 92, 96 | not matched | 2 |
| F7 find dialog open | 8, 8 | 88, 73 | 2 |
| Ctrl+F8 to the drive list and back to the 10 001-entry folder | 5 | 85, 76 | 2 |

The sort result matched at 1.00 and 1.03 s, which is the once-a-second full repaint of finding 1, not the sort itself (the first output is 1 to 25 ms and P1 measured 121 ms to the visible result); I did not separate the two, so the sort time is **not measured** here. All first outputs are under 200 ms, and the folder is shown within 80 ms, so no operation is over budget. P1 recorded a 1 718 ms return to a sorted 10 001-entry folder with 10 000 marked rows (finding 12); that case was not repeated.

#### Database and caches

| Item | Value | Samples |
| --- | --- | --- |
| SQLite file size after 3 cold and 3 warm starts of the small folder | 139 264 bytes | 1 file |
| Rows: `Settings` / `FolderContentsCache` / `DatabaseMetadata` / `DocumentSearch_data` | 14 / 2 / 1 / 2 | 1 file |
| Rows: `MusicLibrary`, `Documents`, `DocumentContents`, `FolderIndexCache`, `FileLabels`, `VaultEntries` | 0 each (nothing to index in the scratch folder) | 1 file |
| Another start on the same folder: `ModelHash`, file inode, file size, `FolderContentsCache` rows | all unchanged (inode 92929, 139 264 bytes, hash `bda8f8e0...`, 2 rows) | 1 (**single sample**) |
| Shutdown (F10 to process exit) | 0.158 to 0.246 s, median 0.22 s | 6 starts |

The cached tables are not dropped on a start with the same data folder, so the rule in `CLAUDE.md` about the schema stamp holds. Shutdown is far under the five seconds jobs get.

### Findings

Numbers continue from P1 (the last was 13).

14. **A race in the test's own model makes one test wait out a 30 s gate** - Impact: **High** (in 2 of 3 runs it is 66 % of the project's test span and about half of its wall-clock). Area: test.
    - Evidence: `AnOpeningUpdateThatFinishesAfterAKeystrokeDoesNotCloseTheList` took 32.22, 32.22 and 0.22 s in the three TRX files of `iConsole.Test`; the project's test span was 39.3, 39.0 and 13.2 s and its wall-clock 48.8, 49.9 and 25.3 s. In P1 the class took no such time. [iConsole.Test/Controls/BlockingCompletionModelTest.cs:183](../../iConsole.Test/Controls/BlockingCompletionModelTest.cs) is `OpeningGate.Wait(TimeSpan.FromSeconds(30))`; 32.2 s is that wait plus the 200 ms and the 2 s of set-up.
    - Cause: the model blocks the first call of `UpdateMatchingValues` ([BlockingCompletionModelTest.cs:181](../../iConsole.Test/Controls/BlockingCompletionModelTest.cs) counts calls with `Interlocked.Increment`), assuming the opening update is call 1. The opening update and the one the keystroke starts run on different threads ([BlockingCompletionModelTest.cs:201](../../iConsole.Test/Controls/BlockingCompletionModelTest.cs) to `:203`), so when the keystroke's call gets there first it is the one that blocks, nothing sets the gate, and `await model.UpdateTask` at `:204` waits for the full 30 s. The order of the two calls was inferred from the time and the code, not traced, so the cause is partly **unverified**.
    - Improvement: block by identity, not by order: let the model block the call whose argument is the opening one (the update started by `OnInitializeAsync`), or make both calls wait on a gate the test opens after it has seen both calls arrive (a `CountdownEvent` of 2). Keep the 30 s as a safety timeout only.
    - Expected gain: `iConsole.Test` span from 39 s to about 13 s and wall-clock from 49 s to about 25 s in the runs that hit it (**estimate**, from run 3); a flaky 32 s wait is removed from CI as well.
    - Effort: S.

15. **Three test projects run their tests one after another** - Impact: **High** for `iConsole.Media.Video.Test` (9 s of 20.8 s), **Medium** for the other two. Area: test.
    - Evidence: no `[assembly: Parallelize]` in `iConsole.Media.Video.Test`, `iConsole.Media.Audio.Test` or `iConsole.Media.Speech.Test` (the other five test projects have it in `AssemblyInfo.cs`, for example [iConsole.Test/AssemblyInfo.cs:5](../../iConsole.Test/AssemblyInfo.cs)). The sum of test durations equals the span: Video 11 s against 11.7 s, Audio 12 s against 12.3 s, Speech 6 s against 6.5 s (3 runs). Video has 114 tests, most of them 0.35 to 1 s each (an ffmpeg process per file, for example `FfmpegBitmapFormatsTest`), and no `[DoNotParallelize]` in the project.
    - Cause: MSTest runs methods serially unless the assembly asks for parallel execution.
    - Improvement: add the same `Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)` attribute that `iConsole.Test` has to the Video project, and check that its tests use their own temporary files; in the Audio project the two `[DoNotParallelize]` classes ([AplayAudioOutputBackendTest.cs:12](../../iConsole.Media.Audio.Test/AplayAudioOutputBackendTest.cs), [AudioDecoderFactoryTest.cs:8](../../iConsole.Media.Audio.Test/AudioDecoderFactoryTest.cs)) stay serial and the rest parallel. These are unit tests that each start their own ffmpeg process, not the end-to-end suite.
    - Expected gain: Video span from 11.7 s to about 1 to 2 s on 48 CPUs, so wall-clock from 20.8 s to about 11 s (**estimate**: 114 tests of at most 1 s on up to 48 workers); Audio and Speech 2 to 6 s each. On a CI runner with 4 CPUs the gain is a quarter of that.
    - Effort: S. **Unverified**: that the tests are independent was not checked beyond the `[DoNotParallelize]` search.
    - Status (fix branch `fix/parallelize-media-tests`, 2026-11-22): **Fixed.** An `AssemblyInfo.cs` with the same `Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)` attribute was added to the Video, Audio and Speech test projects. All tests still pass: Video 113 passed and 1 skipped, Audio 40 passed, Speech 26 passed and 1 skipped. Single runs, Release, `--no-build`: Video wall-clock 20.8 s to 9.1 s; Audio 20.1 s to 14.6 s (span 12.3 s to 6 s); Speech 17.4 s to 13.4 s. The independence check was done by reading the temp-file and process-wide state in these projects: all temp paths use GUID names, and the only process-wide change (`PATH` in `AplayAudioOutputBackendTest`) is in a `[DoNotParallelize]` class. Video's 9.1 s is under the 11 s estimate, but it is one run; the gain on a 4-CPU CI runner was not measured.

16. **`GetDevices_GivesUpOnAHungAplayInsteadOfWaitingForIt` waits out the real 5 s timeout** - Impact: **High** for its project (5.85 s of the 12.3 s span, 29 % of the 20.1 s wall). Area: test.
    - Evidence: 5.85 s median in 3 runs ([AplayAudioOutputBackendTest.cs:62](../../iConsole.Media.Audio.Test/AplayAudioOutputBackendTest.cs) to `:80`): a fake `aplay` runs `sleep 60`, and the call returns after the 5 s the production code allows ([AplayAudioSink.cs:286](../../iConsole.Media.Audio/Sinks/AplayAudioSink.cs), `process.WaitForExit(5000)`). The project is serial (finding 15), so the wait is on the critical path.
    - Cause: the timeout is a constant, so the test can only wait it out.
    - Improvement: give `AplayAudioOutputBackend` an optional timeout in its constructor (default 5 s) and pass 200 ms from the test, which already builds the backend itself at `:73`. The `GetDevices` call in the same file needs the same parameter.
    - Expected gain: 5.6 s of the 12.3 s span of this project (**estimate**: the 5 s becomes about 0.2 s).
    - Effort: S.

17. **The idle app costs 43 to 46 % of one core when a 10 001-entry folder is on screen** - Impact: **Medium**. Area: idle, rendering.
    - Evidence: 43.2 and 46.0 % of one core over 10 s in two scenario runs with the folder shown and nothing pressed, against 27.4 to 31.3 % in the small folder (3 warm runs). The runs differ in uptime (the scenario run is a minute older) and in data folder, so the 15 points are a lead, not a measured cost of the folder.
    - Cause: not found. The once-a-second full repaint of finding 1 touches the whole tree; with 10 001 rows the list may do per-entry work in `Draw` (or in a lens such as the git lens of finding 3) for rows that are not on screen. This is a reading of the numbers and the code path, so it is **unverified**.
    - Improvement: after finding 1 is done, measure again; if the gap stays, sample it with `dotnet-trace` and look at the list controls' `Draw` for work done for off-screen rows (the project's `PagedDataSource` only reads the visible page).
    - Expected gain: up to 15 points of one core while a large folder is open (**estimate**, the size of the gap).
    - Effort: S to find, then S to M.

18. **`LocalShellSessionTest.Cancellation_EndsALongRun` and two neighbours take 3.5 to 4.2 s for a 0.5 s cancel** - Impact: **Low**. Area: test.
    - Evidence: 4.23 s (Bash) and 4.08 s (Sh) median in 3 runs ([LocalShellSessionTest.cs:147](../../iTerminal.Core.Test/Services/Shell/LocalShellSessionTest.cs) to `:155`: cancel after 500 ms of `sleep 30`); `LocalShellSessionServiceTest.Switch_IsRefusedWhileACommandRuns` 3.57 s; the class sums to 40.1 s over 16 tests.
    - Cause: not traced. `Dispose` kills the shell and waits up to 2 s for it to exit ([LocalShellSession.cs:173](../../iTerminal.Core/Services/Shell/LocalShellSession.cs) to `:176`), and a `sleep 30` that outlives the shell keeps the output streams open; with 48 tests starting processes at once the start of a shell also queues. Both are readings, so **unverified**.
    - Improvement: kill the whole process group on `Interrupt` (so the child cannot hold the pipe), or have the test wait for `HasEnded` with a short deadline; first measure the three phases (start, cancel, dispose) in one test.
    - Expected gain: about 2 to 3 s for each of three tests, none of them on the critical path of the 16.7 s span (**estimate** 0 to 1 s of the span).
    - Effort: S to M.

19. **Game tests wait out the computer's real two-second search budget** - Impact: **Low**. Area: test.
    - Evidence: five tests of 2.0 to 2.1 s in `iTerminal.Core.Games.Test` (`ChessTest.UndoTakesBackBothMovesAgainstTheComputer`, `TheComputerAnswersTheOpeningWithinItsTimeBudget`, `AMoveChosenForAnOldPositionIsDropped`, and two in `NineMensMorrisTest`); the games search for `SearchMilliseconds = 2000` by default ([ChessGame.cs:306](../../iTerminal.Core.Games/ChessGame.cs), [NineMensMorrisGame.cs:860](../../iTerminal.Core.Games/NineMensMorrisGame.cs)). The project span is 3.2 s of 12.3 s wall.
    - Cause: the property is settable, but these tests keep the default, so each waits for the full search time.
    - Improvement: set `SearchMilliseconds` to 100 in the tests that check undo and stale moves, and keep the default only in the one test that checks the default budget.
    - Expected gain: the span from 3.2 s to about 1.3 s (**estimate**: the 2 s tests are the tail; the longest remaining is the 1.2 s `ComputerOpponentTest`).
    - Effort: S.

### Status of earlier findings

Same machine class as P1. "Open" means the measurement and the source still show the same cause.

| Finding | P1 | P2 |
| --- | --- | --- |
| 1 render loops and key reader poll, full repaint once a second | 25.0 to 25.7 % steady, 6.9 kB/s | **Open.** `Thread.Sleep(1)` is still at [ControlRendererJob.cs:36](../../iConsole/Jobs/ControlRendererJob.cs), [ConsoleBufferRendererJob.cs:155](../../iConsole/Jobs/ConsoleBufferRendererJob.cs), [KeyboardEventPublisherJob.cs:45](../../iConsole/Jobs/KeyboardEventPublisherJob.cs) and [PosixTerminalInputPublisherJob.cs:215](../../iConsole.Inputs.Posix/Jobs/PosixTerminalInputPublisherJob.cs), and both fallbacks are still 1 s. 29.3 % warm (27.4 to 31.3), 7.3 kB/s. The window differs (P1 after 35 s, here 19 s after the dialog), so the 4 points are not a regression. |
| 2 fixed cost before the first test | 9 to 12 s | **Open.** 7.7 to 10.9 s in all eight projects, 9.4 s for the 7-test project (9.6 s in P1). |
| 3 `git rev-parse` every 3 s | every 3.0 s | **Open.** Four starts at 3.0 s spacing under `strace -f -e trace=execve`; the `RefreshInterval` of 2 s and the rule at [GitLens.cs:126](../../iTerminal.Core/Controls/Lenses/GitLens.cs) are unchanged. |
| 4 start-up 5.4 to 6.0 s | 5 963 cold, 5 353 warm | **Open, slightly better.** 5 667 cold, 5 059 warm; the per-service marks were not added. |
| 5 temporary SQLite databases take 2.5 to 2.8 s per test | per-test cost | **Cause revised, still open.** The slow tests (3.0 s `CacheServiceTest`, 2.9 s `TerminalScreenTest`) all start 1.2 to 2.1 s after the first test and end together; the other 23 tests of `CacheServiceTest` take under 0.2 s ([CacheServiceTest.cs:27](../../iTerminal.Core.Test/Services/CacheServiceTest.cs) `EnsureCreated`). So it is a one-time cost of the first EF model build in the process that the first wave of workers waits for, not a cost per test. A shared template database would save less than P1 estimated; warming EF on a background task in `AssemblyInitialize` ([TestAssemblySetup.cs:19](../../iTerminal.Core.Test/TestAssemblySetup.cs)) while other tests start would shorten the wave (**estimate** 0 to 1 s of the 16.7 s span). `new SettingsService()` (a temporary database, [SettingsService.cs:29](../../iTerminal.Core/Services/SettingsService.cs)) is used 127 times in 47 test files. |
| 6 `DriveInfoJob` reads the mount table | 548 opens in 20 s | **Open, not re-measured.** The pass is unchanged at [DriveInfoJob.cs:97](../../iTerminal.Core/Jobs/DriveInfoJob.cs) (`WaitOne(5000)`) and now reads one more value per drive (`UsedFraction`). |
| 7 TicTacToe test 7.3 s | 7.32 s | **Moved, smaller.** The test is now in `iTerminal.Core.Games.Test` and takes 2.51 s; the search is still unmemoized (not changed by a fix that I can see in the log). |
| 8 PDF signature tests make RSA keys | 10.7 s + 4.5 s | **Open.** `PdfSignatureReaderTest` now lives in `iConsole.Formats.Test` (34 tests, 11.5 s); the two `FileToTextConverter` PDF tests are the slowest there at 1.9 s. |
| 9 fixed waits in follow, `DateTimer`, `NotificationJob` tests | 1.1 to 2.3 s each | **Open.** `FileViewerFollowTest` 6 tests 6.9 s, `DateTimerTest` 1.50 s twice, `NotificationJobTest` 1.29 s. |
| 10 OAuth poll interval | 4.06 s | **Open.** 4.12 s. |
| 11 `RecordingTest` sleeps 3.5 s | 3.91 s | **Open.** 3.91 s; it is now in `iConsole.Media.Speech.Test` ([RecordingTest.cs:209](../../iConsole.Media.Speech.Test/RecordingTest.cs), `Thread.Sleep(3500)`). |
| 12 return to a sorted, fully marked 10 001-entry folder 1.7 s | 1 718 ms, single sample | **Not re-measured.** |
| 13 serial test classes stretch the end | 51 classes | **Not re-measured.** |

### What is already fast

- Opening a 10 001-entry folder shows it within 57 to 80 ms of Enter, and single key presses answer with output in 3 to 7 ms (table above).
- Shutdown takes 0.16 to 0.25 s (6 starts), against the five seconds jobs get.
- The cached tables survive a start on the same folder: schema stamp, file inode, file size and rows were unchanged.
- Start-up already starts the EF warm-up and three slow singletons in parallel; `database-warmed` is at 2.8 to 3.1 s, before the first frame.
- The big test projects are parallel and their tests are short: 6 421 tests in `iTerminal.Core.Test` have no test over 5 s and a 16.7 s span; `iConsole.Formats.Test` runs 576 tests in a 3.0 s span.
- Resident set at idle is flat between runs (225 to 227 MB) and threads stay at 45 to 47.

### Suggested order

1. Quick wins (S): finding 14 (the 30 s race, up to 25 s of `iConsole.Test` in CI), finding 15 (`Parallelize` for the Video project first), finding 16 (inject the aplay timeout), then P1's findings 3, 11, 10, 7 and 8, and finding 19.
2. High: P1 finding 1 (idle render and key loops), then P1 finding 2 (the 8 to 11 s test-host floor, the same on every project).
3. The rest: finding 17 (after finding 1, measure again), P1 findings 4 and 5 (the cause of 5 is revised above), finding 18, P1 findings 9, 12 and 13.

### How to reproduce

All commands run from the repository root; put traces, TRX files and scripts in a scratch folder outside the repository. Nothing generated is committed.

1. Build once: `dotnet build iTerminal.slnx -c Release` and `dotnet build iConsole.Inputs.Posix.Test/iConsole.Inputs.Posix.Test.csproj -c Release` (it is not in the solution build).
2. Tests: for each of `iConsole.Test`, `iTerminal.Core.Test`, `iConsole.Formats.Test`, `iConsole.Media.Audio.Test`, `iConsole.Media.Speech.Test`, `iConsole.Media.Video.Test`, `iConsole.Inputs.Posix.Test` and `iTerminal.Core.Games.Test`, three times one after another: `dotnet test <project>/<project>.csproj -c Release --no-build --logger "trx;LogFileName=<project>_<n>.trx" --results-directory <scratch>/trx`, timed with `date` before and after. A short Python script (run with `python3 -I`) reads each TRX: for every `UnitTestResult` it takes `testName`, `duration`, `outcome`, `startTime`, `endTime`, maps `testId` to the class through `UnitTest/TestMethod@className`, and prints the count, the sum, the span from the first start to the last end, the number over 1 s and 5 s, the ten slowest tests by median across the three runs and the five slowest classes. "Fixed" is the median wall-clock minus the median span. To see whether slow tests are one wave, print each slow test's start offset from the first start.
3. The app: a Python harness opens a pseudo terminal (`pty.openpty`, window 120 x 40 through `TIOCSWINSZ`) and starts `bwrap --bind / / --dev-bind /dev /dev --unshare-net --die-with-parent dotnet iTerminal/bin/Release/net11.0/iTerminal.dll` in a new session, with `DataDirectory=<scratch>/data`, `HOME=<scratch>/home`, `XDG_CONFIG_HOME`, `ITERMINAL_STARTUP_TRACE=<scratch>/trace.txt` and `TERM=xterm-256color`. A reader thread counts the bytes read and stamps the first byte. On a cold start the dependency dialog is closed by sending `s` as soon as the text `skip version` appears in the output (it appears 10.6 to 10.9 s after the spawn; a fixed 9 s wait is too early and leaves the dialog up, which then swallows F10).
4. Start-up: read the trace file (name;milliseconds per line, written at the first frame); cold = a new data folder, warm = the same folder again, three pairs. Idle: 4 s after the dialog closes and another 15 s later, read `utime + stime` from `/proc/<pid>/stat` and the per-thread times from `/proc/<pid>/task/*/stat` over 20 s, and divide the bytes read by the window; `VmRSS` and `Threads` from `/proc/<pid>/status`. Find the .NET pid with `pgrep -P <bwrap pid> dotnet`. Shutdown: send F10 (`ESC [ 2 1 ~`) and poll for the process exit every 5 ms.
5. Child processes: start the app again on the same folder, wait 12 s, and run `strace -f -tt -e trace=execve -p <dotnet pid>` for up to 20 s (use `pgrep -x dotnet`; a pid taken from `pgrep -f` can be the wrapper).
6. Operations: a folder `p2` with `big/` (10 000 files `file_00000.txt` to `file_09999.txt`, 20 lines each) and `notes.txt`, the app started in `p2` with a fresh data folder (a data folder from an earlier run restores its last folder and ignores the start folder). Keys are sent as terminal bytes (Down `ESC [ B`, Enter `CR`, End `ESC [ F`, Home `ESC [ H`, PageDown `ESC [ 6 ~`, Shift+F11 `ESC [ 2 3 ; 2 ~`, Shift+F1 `ESC [ 1 ; 2 P`, F7 `ESC [ 1 8 ~`, Ctrl+F8 `ESC [ 1 9 ; 5 ~`). For each key the harness records the time to the first byte written and the time until a regular expression matches the bytes read (`10[^\d]?001 entries` for the folder).
7. Database: after F10, open `<scratch>/data/iTerminal.sqlite` read-only with Python's `sqlite3`, count rows per table, read `DatabaseMetadata`, and compare the inode and the size before and after another start.
8. Afterwards `git status --short` must show only this document, and no `iTerminal.dll` or `bwrap` process may be left.
