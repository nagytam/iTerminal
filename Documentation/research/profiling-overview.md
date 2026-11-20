# Profiling overview

Measured hot spots of the unit tests and the running application, kept as one document. Each run below is one scope measured on one date. Findings are numbered across the whole document and cited elsewhere as "finding N". The profiler agent appends a new section for every run and never rewrites an older one; a later run records the new number of an earlier finding in "Status of earlier findings". Numbers from different machines are not compared as facts.

| Run | Scope | Machine |
| --- | --- | --- |
| P1 | Unit tests (`iConsole.Test`, `iTerminal.Core.Test`, `iConsole.Inputs.Posix.Test`) and the running app | Linux, 48 CPUs, 499 GB |

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
    - Evidence: 3.91 s median ([RecordingTest.cs:201](../../iTerminal.Core.Test/Speech/RecordingTest.cs) to `:209`): the script runs `sleep 1.2` and the test calls `Thread.Sleep(3500)` before `Stop`.
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
