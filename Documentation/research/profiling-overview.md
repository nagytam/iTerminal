# Profiling overview

Measured hot spots and ranked improvement opportunities for the unit tests, the end-to-end suite and the running application, kept as one document. Each pass below is one scope measured on one date, headed "P<n> YYYY-MM-DD: <scope>". Findings are numbered across the whole document and cited elsewhere as "finding N". The profiler agent appends a dated section here and re-measures earlier findings in "Status of earlier findings"; it does not rewrite older text. Numbers from different machines are not compared as facts.

| Pass | Scope |
| --- | --- |
| P1 | Unit test projects (`iConsole.Test`, `iTerminal.Core.Test`, `iConsole.Inputs.Windows.Test`) |

## P1 2026-11-20: unit test projects

Scope: `tests`, chosen by the caller (unit tests only). Not in scope and not run: `iTerminal.Test` (end-to-end), `iTerminal.Specifications.Test` (boots the real app per scenario, so it belongs with the end-to-end scope) and `iConsole.Inputs.Posix.Test` (marked non-building on Windows in `iTerminal.slnx`). `iConsole.Test.Core` is a helper library without tests. Branch `feat/directory-completion`, commit `74a41e86`, with uncommitted work in `iConsole` (CSV and grid viewers) that is part of the measured build. Machine "KINCSEM": Windows 11 Pro 10.0.26300, Intel Core i7-1065G7 (4 cores, 8 logical processors), 31.8 GB RAM, .NET SDK 11.0.100. This machine has seven mapped drives (five of them network shares), `git`, `ffmpeg` and `winget` installed. Tools: `dotnet test` with the TRX logger and a parser script kept in the scratch folder; `dotnet-trace`, `dotnet-counters` and `dotnet-gcdump` are not installed (`dotnet tool list -g` shows only `dotnet-ef` and `powershell`), so no CPU sample or allocation view was taken, and nothing was installed. Not measured: CPU frames of the slowest test host (no `dotnet-trace`), and anything about the application or the end-to-end suite. The tool that would have given more detail is `dotnet-trace collect --profile dotnet-sampled` on the `iTerminal.Core.Test` host, which would split the CPU-bound parallel phase by frame instead of by test.

### Summary

- `iTerminal.Core.Test` is the cost: 6 495 tests, median wall-clock 226 s of the three runs (range 214 to 241 s). `iConsole.Test` takes a median 24.6 s for 5 286 tests and `iConsole.Inputs.Windows.Test` 3.8 s for 148 tests, of which 0.26 s is test time.
- The core run has two phases. All eight workers are busy for the first ~125 s, then one thread runs the `[DoNotParallelize]` classes alone for ~78 s (38 % of the run). `GitServiceTest` is 52 s of that tail and carries no documented reason for being serial.
- Run on one worker the same suite takes 359 s with 346 s of test time, so eight workers give 1.6x. Four workers (224 s) were as fast as eight (226 s), because the machine has four cores.
- Five tests wait out real time or burn a fixed search budget: the chess and Morris computer-opponent tests (24 s each, 2 s per computer move), the two executable-analyzer fuzz tests (38 s together run alone) and the device-code sign-in test (a real 1 s poll interval).
- Risks: three tests in `iTerminal.Core.Test` reach the real machine (network drives, `winget`, `ffmpeg`), and one test (`AltF1_IsBoundToNoOtherCommand`) ran into its 60 s hang guard in one of three runs although it takes 0.55 s alone.

### Measurements

Build, Release, `dotnet build iTerminal.slnx -c Release`, one sample each, reported apart from the test runs.

| Step | Wall-clock (s) | Samples | Note |
| --- | --- | --- | --- |
| Build, first Release build of this session | 233 | 1 | **single sample** |
| Build again, nothing changed (`--no-restore`) | 121 | 1 | **single sample**; the cause was not investigated |

Test runs, `dotnet test <project> -c Release --no-build`, three runs each. "Wall" is the whole command; "Test span" is first test start to last test end from the TRX; "Task time" is the sum of test durations.

| Project | Tests (run / skipped / failed) | Wall median (s) | Wall range (s) | Test span median (s) | Task time median (s) |
| --- | --- | --- | --- | --- | --- |
| `iConsole.Test` | 5 286 / 0 / 0 | 24.6 | 24.0 to 25.2 | 20.0 | 105 |
| `iTerminal.Core.Test` | 6 463 / 32 / 1 to 3 | 226 | 214 to 241 | 217 | 1 091 |
| `iConsole.Inputs.Windows.Test` | 148 / 0 / 0 | 3.8 | 3.5 to 7.9 | 0.26 | 0.26 |

Start-up and shape of each run.

| Project | Wall minus span (s) | Smallest filtered run wall (s) | One worker: wall (s) | One worker: task time (s) | Tests over 1 s (one worker) | Tests over 5 s (one worker) |
| --- | --- | --- | --- | --- | --- | --- |
| `iConsole.Test` | 4.6 | 5.0 to 6.1 (2 runs) | 52.8 | 47.8 | 13 | 0 |
| `iTerminal.Core.Test` | 9.0 | 4.8 to 7.6 (3 runs, median 5.5) | 359 | 346 | 56 | 8 |
| `iConsole.Inputs.Windows.Test` | 3.3 | not run | not run | not run | not run | not run |

"One worker" is a run with a scratch `.runsettings` that sets `MSTest` `Parallelize` to one worker at method level; it is a **single sample** and gives each test's time without contention. Under the normal run the same tests are slower (task time 1 091 s against 346 s for the core suite, 105 s against 48 s for the console suite), so durations in a parallel TRX are inflated by sharing the machine and must not be added up.

Parallelism experiments on `iTerminal.Core.Test`, **single sample** each, compared with the default (server GC, one worker per logical processor: 214, 226 and 241 s).

| Setting | Wall (s) | Test span (s) | Task time (s) | Samples |
| --- | --- | --- | --- | --- |
| Default (server GC, 8 workers) | 226 (median) | 206 to 234 | 1 033 to 1 096 | 3 |
| Workstation GC (`DOTNET_gcServer=0`) | 197 | 192 | 1 018 | 1 |
| 4 workers (`runsettings`) | 224 | 216 | 639 | 1 |
| 1 worker (`runsettings`) | 359 | 352 | 346 | 1 |

Timeline of the third default run (average number of tests in flight per 10 s): 7.6 to 8.0 from second 0 to 100, a burst of 26 to 84 around second 110 to 120 (tests waiting on timers and processes), then 1.0 from second 130 to 206. The classes that started after second 128, by summed duration, are below.

| Class (all in `iTerminal.Core.Test`) | Tests | Time in the serial tail (s) | Why serial |
| --- | --- | --- | --- |
| `Services.Git.GitServiceTest` | 13 | 52.3 | `[DoNotParallelize]`, no comment |
| `Tasks.FilePanelRefreshTaskTimingTest` | 7 | 8.7 | wall-clock budgets (documented) |
| `Application.MusicPanelsRegistrationTest` | 7 | 6.2 | not marked; it simply started late |
| `Dialogs.Map.MapDialogTest` | 24 | 2.2 | `[DoNotParallelize]` |
| `AI.AiChatClientFactoryTest` | 7 | 2.1 | `[DoNotParallelize]` |
| `Services.Shell.ShellLaunchTest` | 8 | 1.9 | swaps `Console.In` (documented) |
| 19 other classes | 185 | 10.0 | `[DoNotParallelize]` |

Slowest ten tests of `iTerminal.Core.Test` with one worker (no contention, one sample), then the same tests' range in the three default runs.

| Test | One worker (s) | Default runs (s) | Cause (see finding) |
| --- | --- | --- | --- |
| `ComputerOpponentTest.TheComputerAlwaysHasAMoveInMorris` | 24.1 | 24.1 to 24.2 | fixed 2 s search per move (finding 2) |
| `ComputerOpponentTest.TheComputerAlwaysHasAMoveInChess` | 21.8 | 24.1 | fixed 2 s search per move (finding 2) |
| `ExecutableAnalyzerTest.EveryTruncation_OfEveryFormat_IsRefusedCleanly` | 10.6 | 53 to 81 | many temp files (finding 3) |
| `WinGetPackageManagerServiceTest.GetInstalledPackagesAsync_ParsesAtLeastOneInstalledPackage` | 7.4 | 17.6 | real `winget` (finding 5) |
| `ExecutableAnalyzerTest.SeededCorruption_OfEveryFormat_NeverEscapesAsAnotherException` | 6.6 | 31 to 69 | many temp files (finding 3) |
| `FilePanelRefreshTaskTimingTest.ExecuteAsync_LargeDirectory_EnumerationStaysWithinBudget` | 6.5 | 7.2 | 2 050 real entries, serial by design |
| `GitServiceTest.GetCommitDiffAsync_ContainsSubjectAndChangedFile` | 5.6 | 2 to 3 | real `git` processes (finding 1) |
| `GitServiceTest.GetBranchesAsync_SeparatesRemoteTrackingBranchesAndCheckingOneOutTracksIt` | 5.0 | 6.8 to 12 | real `git` processes (finding 1) |
| `WinGetPackageManagerServiceTest.InstallAsync_UnknownPackageId_StreamsOutputAndReturnsNonZeroExitCode` | 4.4 | 10.9 to 12.5 | real `winget` (finding 5) |
| `BenchmarkProbesTest.TheInternetProbesReportUnavailableWhenTheServerCannotBeReached` | 4.1 | not in top 20 | loopback connect failure, 4 s (not investigated) |

Slowest classes of `iTerminal.Core.Test` by summed duration with one worker.

| Class | Tests | Sum (s) | Sum in default run 3 (s) | Cause (see finding) |
| --- | --- | --- | --- | --- |
| `Services.Games.ComputerOpponentTest` | 5 | 46.8 | 51.0 | finding 2 |
| `Services.Git.GitServiceTest` | 13 | 34.5 | 52.3 | finding 1 |
| `Services.Executables.ExecutableAnalyzerTest` | 36 | 18.0 | 86.7 | finding 3 |
| `Services.PackageManager.WinGetPackageManagerServiceTest` | 5 | 12.4 | 31.7 | finding 5 |
| `Video.FfmpegBitmapFormatsTest` | 15 | 11.7 | 34.5 | finding 5 |
| `Tasks.FilePanelRefreshTaskTimingTest` | 7 | 8.6 | 8.7 | serial by design |
| `Services.Cloud.OAuthTokenProviderTest` | 12 | 7.4 | not in top 12 | finding 6 |
| `Services.Git.GitDriveServiceTest` | 12 | 7.0 | 26.0 | real `git` processes |
| `Dialogs.AiModelDialogTest` | 5 | 6.1 | not in top 12 | not investigated |
| `Services.Games.ChessTest` | 22 | 6.1 | not in top 12 | not investigated |

Two classes are much slower under contention than alone and so mostly measure the machine: `Jobs.Test.DriveInfoJobTest` (3 tests, 9.7 s each alone, 17 to 24 s each in the default runs; finding 5) and `Services.PasswordManagerTest` (13 tests, 0.65 s each alone, 32.8 s summed in run 3; finding 7).

Slowest ten tests of `iConsole.Test`, default run 2 (3 runs gave the same order).

| Test | Duration (s) | Cause |
| --- | --- | --- |
| `ControlStaleTextSweepTest.ChangingASetting_BackAndForth_LeavesTheSamePictureAsSettingItOnce` | 2.50 | one test sweeps every control type |
| `FileViewerFollowTest.Follow_WaitsForTheEndOfALine_BeforeAddingIt` | 2.28 | `Task.Delay(1100)` per check (finding 6) |
| `DateTimerTest.AfterItIsDisposed_TheTextStopsChanging` | 1.50 | `Task.Delay(1500)` (finding 6) |
| `DateTimerTest.BeforeItIsStarted_TheTimerDoesNotRun` | 1.50 | `Task.Delay(1500)` (finding 6) |
| `NotificationJobTest.WhileTheTerminalIsLent_TimeStandsStill` | 1.34 | real wait |
| `ConsoleBufferRendererJobRunTest.Run_WhenTheResizeHandlerFailsOnce_TellsItAgainSoTheLayoutDoesNotStayStale` | 1.26 | real wait |
| `FileViewerFollowTest.Follow_AddsTheNewLinesAndMovesTheCursorToTheLast` | 1.15 | `Task.Delay(1100)` |
| `FileViewerFollowTest.AFileThatGetsShorter_StopsTheFollow_AndKeepsTheText` | 1.14 | `Task.Delay(1100)` |
| `BinaryViewerLanguageTest.EveryLanguage_LocalizesBinaryMetadataAndDisassemblyOutput` | 1.14 | sweep over languages |
| `FileViewerFollowTest.F6_StartsAndStopsTheFollow` | 1.13 | `Task.Delay(1100)` |

`iConsole.Test` has 16 tests over 1 s and none over 5 s in the default run; the slowest class is `Dialogs.FileViewerFollowTest` (6 tests, 6.9 s), then `Services.FileToTextConverterServiceFixtureTest` (42 tests, 6.1 s). The run is a long tail of small tests, not a few big ones: the test span is 20 s for 105 s of task time.

`iConsole.Inputs.Windows.Test`: 148 tests, 0.22 to 0.29 s of test time, 3.5 to 7.9 s of wall-clock. 93 % of the wall-clock is test host start-up and discovery.

Failures seen. Run 1 of the core suite failed three tests, runs 2 and 3 one each. `RarToolRoundTripTest.APasswordIsNeededToReadAndTheRightOneOpensIt` failed in all three runs (4 to 5 s each: "secret.rar: the password is wrong", `iTerminal.Core/Services/Archives/UnrarArchiveReader.cs:92`), so it depends on the tools installed here. `FfmpegBitmapFormatsTest.EachFfmpegBitmap_DecodesThroughFfmpeg (".pam")` failed once (the temp folder could not be deleted: `picture.pam` still in use) and `AiPromptBarVoiceTest.AltF1_IsBoundToNoOtherCommand` failed once on its 60 s timeout (alone it takes 0.55 s).

### Findings

1. **Run `GitServiceTest` in parallel with the other classes.** Impact **High**. Area: test.
   - Evidence: in default run 3 the single-threaded tail starts at second ~128 and lasts ~78 s of a 206 s span; `GitServiceTest` is 52.3 s of its 83.4 s of task time (command `python trx.py ... 128`, see How to reproduce). With one worker the class takes 34.5 s, alone 27 s. `iTerminal.Core.Test/Services/Git/GitServiceTest.cs:6` has `[DoNotParallelize]` without a comment. `GitDriveServiceTest` also runs real `git` processes and is already parallel.
   - Cause: the class was marked serial in an unrelated commit (`48ef8b26`, a dialog-focus change) and nothing in it shares state: each test copies its own template repository into a new temp folder (`GitServiceTest.cs:33`, `:53`). Its 13 tests then run alone while seven cores idle.
   - Improvement: remove the attribute, run the class five times in the suite to see whether it stays stable under load, and keep `[ClassCleanup]` as is. If a test turns out to share state, mark only that class.
   - Expected gain: 35 to 45 s of the 226 s median (16 to 20 %), **estimate**: the 52 s of serial work moves into the parallel phase, where it adds roughly 52 s divided by the effective 4 to 5 cores. Also drop the `GitAvailable()` probe that starts a `git --version` process in each test (`GitServiceTest.cs:9`) by checking once in a static `Lazy<bool>`.
   - Effort: S.

2. **Give the computer-opponent search an injectable time budget.** Impact **High** (saves about 44 s of CPU-bound work). Area: test.
   - Evidence: `ComputerOpponentTest.TheComputerAlwaysHasAMoveInChess` 21.8 s and `...Morris` 24.1 s with one worker (24.1 s in all default runs); the class sums to 46.8 s. `iTerminal.Core/Services/Games/ChessGame.cs:315` builds `new ChessSearch(new Random(seed), token, 2000, 6)` and `iTerminal.Core/Services/Games/NineMensMorrisGame.cs:871` builds `new MorrisSearch(new Random(seed), token, 10, 2000)`; the test plays 2 seeds x 6 computer moves (`ComputerOpponentTest.cs:14`, `:33`, `:72`).
   - Cause: a search runs for its whole 2 000 ms budget (12 moves x 2 s = 24 s per game) and spins a core while it does, in the CPU-bound phase of the run.
   - Improvement: add an init-only `ThinkMilliseconds` (default 2000) to `ChessGame` and `NineMensMorrisGame`, read at the two lines above, and set it to 50 in the test. The assertions (a computer has a legal move, the game is not over) do not depend on the search depth. A separate test keeps one real 2 s search so the default is still exercised.
   - Expected gain: 44 s less CPU and two 24 s long poles gone; 5 to 10 s of wall-clock, **estimate**, because the poles overlap the parallel phase.
   - Effort: S.

3. **Cut the executable-analyzer fuzz tests down in file work and split them per format.** Impact **Medium**. Area: test.
   - Evidence: alone, `EveryTruncation_OfEveryFormat_IsRefusedCleanly` 22.5 s and `SeededCorruption_OfEveryFormat_NeverEscapesAsAnotherException` 15.9 s; in the default runs 53 to 81 s and 31 to 69 s (each runs on one worker, loop over five formats); with one worker 10.6 s and 6.6 s. `iTerminal.Core.Test/Services/Executables/ExecutableAnalyzerTest.cs:415` and `:433`; every case writes and deletes a temp file in `AssertSurvives` (`ExecutableAnalyzerTest.cs:384`).
   - Cause: two methods each loop over `Fixtures()` (ELF, PE64, PE32, Mach-O, Fat) with thousands of cases, so each is a single long task, and every case does a file write, a read by `BoundedFileReader`, and a delete (Windows scans each).
   - Improvement: make each of the two a `[DynamicData]` test with one case per format (the project's own rule for sweeps, `Documentation/architecture/testing.md`), so five tasks of a fifth of the length run on five workers. Reuse one temp file per case (overwrite with `FileStream` and `FileMode.Create`) instead of a new GUID name. A path-free overload of `ExecutableAnalyzer.Analyze` would remove the file work, but it changes production code and is a separate step.
   - Expected gain: the longest single task falls from 53 to 81 s to about 15 s, and file churn drops; 3 to 8 s wall-clock and 10 to 20 s CPU, **estimate**. The share of time spent in file I/O against analysis was not measured (**unverified**; a `dotnet-trace` sample would show it).
   - Effort: S.

4. **Measure the GC mode and worker count on the machines that matter before keeping server GC.** Impact **Medium**. Area: test.
   - Evidence: workstation GC (`DOTNET_gcServer=0`, **single sample**) took 197 s against 214, 226 and 241 s with server GC (`iTerminal.Core.Test/iTerminal.Core.Test.csproj:13`); 4 workers took 224 s (**single sample**). `Documentation/architecture/testing.md` records that server GC took the run "from about 52 s to about 34 s on a 48-core machine".
   - Cause: server GC creates one heap and thread per logical processor; on 4 cores with hyper-threading the heaps compete with the 8 test workers. The effect is inside the spread of the three default runs, so it is not proven.
   - Improvement: repeat both experiments three times on this machine and on a CI-sized agent; if workstation GC wins on 8 or fewer logical processors, set `GCHeapAffinitizeMask` or `GCHeapCount` in the `.csproj` (for example `ServerGarbageCollection` with `GCHeapCount` 4) rather than switching off. Do not change the setting without that data.
   - Expected gain: 10 to 30 s (4 to 13 %), **estimate** from one sample.
   - Effort: S.

5. **Take the real machine out of three unit tests.** Impact **Medium**. Area: test.
   - Evidence: `DriveInfoJobTest` (3 tests) takes 9.7 s each alone and 17 to 24 s in the default runs because `iTerminal.Core/Jobs/DriveInfoJob.cs:46` calls `DriveInfo.GetDrives()` and probes `IsReady` (`:61`, `:67`) on this machine's five network shares (`\\nas4\backup`, `\\fritz.box\FRITZ.NAS`, `\\Fritz\fritz`, `\\luna\eSHAB`, `\\nas5\NAS5`). `WinGetPackageManagerServiceTest` runs the real `winget` (7.4 and 4.4 s with one worker, 17.6 and 10.9 s in run 3; `WinGetPackageManagerServiceTest.cs:83` starts a real install of a package that does not exist). `FfmpegBitmapFormatsTest` (15 tests, 11.7 s with one worker) and `FfmpegAudioFormatsTest` (20 tests, about 1.8 s each alone) start `ffmpeg` and `ffprobe` per case.
   - Cause: the tests use the host's drives, package manager and ffmpeg. Their time and result depend on the machine, an offline share can make a test take its whole 50 s deadline (`DriveInfoJobTest.cs:21`), and it breaks the rule that tests must not reach the real machine (`CLAUDE.md`).
   - Improvement: give `DriveInfoJob` an injectable drive source (an `IDriveSource` with a fake that returns two drives) and run the first pass once in a `[ClassInitialize]` for the three tests; for `winget`, use the `FakeProcessRunner` helper the other package-manager tests use. Keep one ffmpeg test per decoder kind and keep the skip when ffmpeg is absent, which they already have.
   - Expected gain: 29 s of task time for the drive tests (3 x 9.7 s becomes one 0 s pass) and 12 s for `winget`; wall-clock 2 to 5 s because none of them is a long pole, **estimate**. The main gain is that run time no longer depends on the network.
   - Effort: M.

6. **Replace fixed real-time waits with an injected clock or interval.** Impact **Low**. Area: test.
   - Evidence: `iConsole.Test/Dialogs/FileViewerFollowTest.cs:74` waits 1 100 ms before each of 6 follow checks (6.9 s for the class); `iConsole.Test/Controls/StatusBar/DateTimerTest.cs:62` and `:75` wait 1 500 ms each to prove that nothing happens (4.1 s); `iTerminal.Core.Test/Speech/RecordingTest.cs:209` sleeps 3 500 ms and spawns `ping -n 3` (3.95 s with one worker); `iTerminal.Core.Test/Services/Cloud/OAuthTokenProviderTest.cs:128` uses a 1 s poll interval for four polls, 4 s of the class's 7.4 s (`iTerminal.Core/Services/Cloud/OAuthTokenProvider.cs:288`, `:295`, `:306` clamp the interval to at least 1 s and there is no way to inject a shorter one, only `slowDownStep`); `iTerminal.Core.Test/Model/ContentModels/ContentModelFileSystemTest.cs:94` has `Thread.Sleep(1_000)`.
   - Cause: the code under test reads the wall clock or `Task.Delay`, and the test waits it out.
   - Improvement: pass a minimum poll interval into `OAuthTokenProvider` like `slowDownStep`; give the follow check in `FileViewerDialog` a clock (the test already uses a fake `Clock` for `DateTimer`); replace the `ContentModelFileSystemTest` sleep with `WaitUntilAsync` on the entries.
   - Expected gain: about 20 s of idle waiting across the two projects, but 0 to 2 s of wall-clock because these tests wait in parallel with others, **estimate**.
   - Effort: M.

7. **Build the SQLite file for the database tests once and copy it.** Impact **Low**. Area: test. **unverified** (cause read, not traced).
   - Evidence: `DatabaseInitializationHostedServiceTest` takes 1.9 to 2.0 s per test alone and 31 s summed in run 3 (5 tests); `PasswordManagerTest` takes 0.65 s per test alone (13 tests, 32.8 s summed in run 3); `MusicLibraryServiceTest` 28 tests, 21.6 s summed in run 3. Each test creates its own `.sqlite` file (`DatabaseInitializationHostedServiceTest.cs:25` onward).
   - Cause: every test creates the database schema from the EF model. A `dotnet-trace` sample would show whether the time is EF model building, SQLite file creation or antivirus scanning of the new file.
   - Improvement: create the initialized database once in a `[ClassInitialize]` and copy the file per test (the same pattern `GitServiceTest.RepoTemplate` already uses for a repository).
   - Expected gain: 1 to 2 s per test in the three classes, about 50 s of task time under contention, 3 to 6 s wall-clock, **estimate**.
   - Effort: M.

8. **Make the tests that failed or hit their hang guard reliable.** Impact **Medium**. Area: test.
   - Evidence: `AiPromptBarVoiceTest.AltF1_IsBoundToNoOtherCommand` timed out after 60 s in run 1 and takes 0.55 s alone (`iTerminal.Core.Test/Controls/Terminals/AiPromptBarVoiceTest.cs:235`); it reads all 1 519 `.cs` files (9.2 MB) of `iTerminal.Core` and `iConsole` with an interpreted `Regex`, and its walk also enumerates the `bin` and `obj` folders. Run 1 (241 s) was the slowest of the three. `RarToolRoundTripTest.APasswordIsNeededToReadAndTheRightOneOpensIt` failed 3 of 3 times here. The `.pam` case failed once because the temp folder was deleted while `ffmpeg` held the file (`FfmpegBitmapFormatsTest.cs:45`).
   - Cause: the first test scans the source tree on every run under full load; the cause of the 60 s stall was not found (**unverified**; it may be file-system scanning by antivirus while eight workers read files). The other two depend on external tools.
   - Improvement: scan the source once per assembly in a static `Lazy`, skip `bin` and `obj` directories by `EnumerateDirectories`, and compile the regex. Retry the temp folder delete in the ffmpeg test, or end the child process before it. Make the Rar test report `Assert.Inconclusive` when the installed `rar` cannot create a password-protected archive.
   - Expected gain: a failed run costs a full rerun of 3.5 to 4 minutes; the 60 s stall also lengthens a run when it lands on the critical path. **estimate**: 0 to 15 s per affected run.
   - Effort: S.

9. **Find out why a build with no changes takes two minutes.** Impact **Medium**. Area: test (build loop).
   - Evidence: `dotnet build iTerminal.slnx -c Release --no-restore` with nothing changed took 121 s (**single sample**), against 19 s to run all of `iConsole.Test`. The first Release build took 233 s.
   - Cause: not known. The solution holds 14 projects including the MSI and Linux packaging projects; any project that does not skip its work when its inputs are unchanged (for example a code generator, a resource step or a packaging target) would explain it.
   - Improvement: run `dotnet build iTerminal.slnx -c Release -bl:<scratch>/build.binlog` and list targets that run when nothing changed; add `Inputs` and `Outputs` to those targets.
   - Expected gain: up to 100 s per edit-and-test loop on a build with few changes, **estimate** from one sample.
   - Effort: S for the investigation, size of the fix unknown.

10. **Start-up of the test host is 5 s per project and 3.5 s for a project with 0.26 s of tests.** Impact **Low**. Area: test.
    - Evidence: `iConsole.Inputs.Windows.Test`: wall 3.5 to 7.9 s for 148 tests that take 0.22 to 0.29 s. A filtered `iTerminal.Core.Test` run of four tiny tests took 4.8, 5.5 and 7.6 s of wall-clock; `iConsole.Test` with one test took 5.0 and 6.1 s. The full runs add 4.6 s (console) and 9.0 s (core) over their test span.
    - Cause: `dotnet test` starts the VSTest runner, loads the adapter, discovers tests by reflection (6 495 in the core assembly) and starts a host process; this is cost per project, not per test.
    - Improvement: nothing to change in the tests. Run the projects through `dotnet test iTerminal.slnx`, which starts the hosts side by side (already the documented command), and do not split the large assemblies further.
    - Expected gain: none to claim; this is the floor the other findings work above.
    - Effort: S.

### What is already fast

- `iConsole.Test` runs 5 286 tests in a 20 s test span (median wall-clock 24.6 s), with no test over 5 s and 16 over 1 s.
- Most core tests are small: of 6 463 executed tests, 1 226 to 1 265 (about 19 %) take over 0.1 s in the default runs, and 397 (6 %) with one worker.
- `GitServiceTest` already copies a template repository instead of running four `git` processes per test (`GitServiceTest.cs:31`).
- The tests that need a clock use a fake one for most cases (`DateTimerTest`, `GamesDialogTest`), and `OAuthTokenProviderTest` already removes the slow-down step.
- `TestAssemblySetup` raises the thread-pool floor; the only timeout failure in the three default core runs is the one in finding 8.
- `iConsole.Inputs.Windows.Test` is as fast as a project can be: 0.26 s of tests.

### Suggested order

1. Quick wins that need no production change: finding 1 (remove `[DoNotParallelize]` from `GitServiceTest`), finding 3 (split the fuzz tests per format), finding 8 (scan the source once; make the Rar test inconclusive).
2. High, one small production change: finding 2 (an injectable think time for chess and Morris).
3. Measure before changing: finding 4 (GC mode and worker count, three runs each, on this machine and on CI) and finding 9 (a binlog of the no-op build).
4. The rest: finding 5 (real drives, `winget`, `ffmpeg`), finding 7 (shared database template), finding 6 (fixed waits), finding 10 (nothing to change).

Together, findings 1 to 3 should take the core run from a median 226 s to about 170 to 185 s (**estimate**), and finding 2 also removes a 24 s long pole.

### How to reproduce

Use a scratch folder outside the repository. Nothing here writes into the repository.

1. Build once: `dotnet build iTerminal.slnx -c Release` (timed on its own), and again with `--no-restore` for the no-op time.
2. For each of `iConsole.Test`, `iTerminal.Core.Test`, `iConsole.Inputs.Windows.Test` and three times: `dotnet test <project>/<project>.csproj -c Release --no-build --logger "trx;LogFileName=<name>.trx" --results-directory <scratch>/trx`, with the wall-clock taken from `date +%s%N` before and after the command in a Git Bash script. The projects run one after another, never at the same time.
3. One-worker run: write `serial.runsettings` with `<RunSettings><MSTest><Parallelize><Workers>1</Workers><Scope>MethodLevel</Scope></Parallelize></MSTest></RunSettings>` and add `--settings <file>`. Four workers: the same with 4. Workstation GC: set `DOTNET_gcServer=0` in the environment of the command.
4. Isolated runs: `--filter "FullyQualifiedName~<ClassName>"` for one class at a time (`GitServiceTest`, `ExecutableAnalyzerTest`, `DriveInfoJobTest`, `ComputerOpponentTest` and so on). These filters were used only to time a class apart, never to hide a test from the full runs.
5. The parser script (`trx.py`, Python run with `-I`): read each `UnitTestResult` for `testId`, `testName`, `duration`, `startTime`, `endTime` and `outcome`; map `testId` to the class through the `UnitTest` elements (the test name is not unique across classes); print the count, the sum of durations, the span, the number over 0.1, 1 and 5 s, the 20 slowest tests and the 12 slowest classes by summed duration, and with a second argument the same by class for tests started after that many seconds from the first start. A second script divides the span into 10 s buckets and adds up how much of each bucket every test was running, which gives the average number of tests in flight.
6. Machine facts: `dotnet --version`, `dotnet tool list -g`, `git rev-parse --short HEAD`, and for the CPU name and RAM a `Get-CimInstance Win32_Processor` and `Win32_ComputerSystem` query.
7. Taking a CPU sample, which was not done here: install `dotnet-trace` with the user's approval, then `dotnet trace collect --profile dotnet-sampled -- dotnet test iTerminal.Core.Test/iTerminal.Core.Test.csproj -c Release --no-build`, and list the top inclusive frames that are not test code.
