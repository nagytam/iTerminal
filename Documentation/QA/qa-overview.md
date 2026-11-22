# QA overview

This document is the running record of the QA agent (`.claude/agents/qa.md`). Each run appends a section. Solved issues (QA-1, QA-2, QA-14, QA-16) have been removed from the document, and so have QA-3, QA-19, QA-20, QA-21, QA-22, QA-23, QA-25 and QA-26, fixed in #827. Issue IDs (`QA-n`) are unique across the whole document, and older sections are not edited. A later run records the new state of an earlier issue in its own "Status of earlier issues" list.

## Q1 2026-11-21: all (pipeline, coverage, code review)

Scope `all`, chosen because the request named none. Branch `add-sonar-analyzer-qa-agent`, commit `744f5990` (the run started on `52abb3f1`; the next commit only adds SonarAnalyzer and the QA agent), Linux 7.3.0-11, `dotnet 11.0.100`, `gh 2.46.0`. Pipeline history is the last 50 runs of "Build and test" on `main` (2026-11-21 08:37 to 19:55 UTC). Failure patterns also use the 21 older runs back to 2026-11-20 20:41 UTC, which is every run `main` has in GitHub Actions (71 in total; 200 were requested). Coverage was measured with the `XPlat Code Coverage` collector (`coverlet.collector` 10.1.1, `coverage.runsettings`) on six unit test projects on Linux. Not measured: `iTerminal.Test` and `iTerminal.Specifications.Test` coverage (the end-to-end suite was not run; another process on this machine was already running it), `iConsole.Inputs.Windows.Test` and `iConsole.Inputs.Posix.Test` (Windows-only and non-building in the `.slnx`), and the Windows-only code paths. The three newest `main` commits seen by CI (`2c75c4fe`, `df0fc527`, `25ba25f5`) are not in the local clone, so for those the evidence is the CI log and `gh api` commit patches.

### Summary

- Pipeline: of the last 50 "Build and test" runs on `main`, 15 passed, 19 failed and 16 were cancelled (a failure rate of 38% of all runs, 56% of completed runs). The longest red streak is 12 failed runs with no success in between. The 3 newest completed runs are red. "Release builds" has no run on `main` (it runs on closed pull requests; its last 50 runs all passed).
- Largest coverage gap: 153 of 908 `iTerminal.Core` source files have no test that mentions their type (11 987 lines), and `iConsole.Media.Audio` is covered at 31.2% lines. 605 of 627 BDD scenarios in `iTerminal.Specifications.Test` are skipped as `@ignore` stubs.
- Most serious open defect at the time: directory completion failed intermittently in the end-to-end suite. Removed from this document with the fix in #827. A 12-run red streak on one Windows test showed that failing tests were merged repeatedly (the test is fixed; the process point is now written down in `Documentation/architecture/testing.md`).

### Measurements

Pipeline, workflow "Build and test", branch `main`, 50 runs:

| Workflow | Runs | Pass | Fail | Cancelled | Failure rate (completed) | Median duration (successful runs) |
| --- | --- | --- | --- | --- | --- | --- |
| Build and test | 50 | 15 | 19 | 16 | 55.9% (19 of 34) | 16.7 min, the newest 9 of 18 successes, against 16.9 min for the 9 before (flat) |
| Release builds | 0 on `main` | n/a | n/a | n/a | n/a | not measured (no run on `main`; 50 of 50 pull-request runs passed) |

Fewer than 40 successful runs exist, so the 20-against-20 comparison was done with 9 against 9. The end-to-end step takes 3.5 to 4.5 min on the Linux job and 9 to 10 min on the Windows job (runs 44943515522, 44939633048, 44915127385).

Failure patterns over all 71 runs (24 failed runs; each failed run counted once under its first failing test):

| Pattern | Failed runs | First run | Last run | Class |
| --- | --- | --- | --- | --- |
| `ShowsMemoryAndProcessorUseForEachProcess` (Windows, Core.Test) | 12 | 44891874408 (11-21 09:55) | 44907848614 (13:14) | Test failure, fixed |
| `TabCompletesAFolderTypedInTheCopyDialog` / `...MoveDialog` / `CtrlPageUpGoesToTheParentFolder` (end-to-end, 30 s timeout) | 4 | 44813675241 (11-20 20:41) | 44879680992 (11-21 07:19) | Flaky (timeout) |
| `Draw_LoadsThePictureAndShowsItAsAscii` (NullReferenceException) | 2 | 44915127385 (14:40) | 44925547090 (16:36) | Flaky |
| `ThePaletteFooterAndSixMoreDialogsInGerman_AreGerman("Passwords", ...)` (both OS) | 2 | 44942013183 (19:38) | 44943515522 (19:55) | Test failure, open |
| `F2InTheCsvViewerAsksForAFileNameAndWritesTheRowsOnScreen` | 1 only as first failure (2 runs in all) | 44882901966 (08:01, Linux) | 44905768469 (12:49, Windows) | Flaky |
| `Identity_IsReadOnce_UntilItAnswers` (2 runs, both also in the first pattern) | 0 as first failure (2 runs in all) | 44897329529 (11:04) | 44900935517 (11:49) | Flaky |
| `CtrlShiftDShowsTheFocusedFolderInTheOtherPanel`, `ShrinkingTheTerminalLeavesNothingOffScreen`, `CtrlShiftF8_AnyOtherKeyAfterTheFirstEnter_WithdrawsIt` (end-to-end, Windows, 30 s or 16 s timeout) | 3 (one run each) | 44924437491 (16:24) | 44942013183 (19:38) | Timeout, possibly load |
| `ASlowListingIsNotCachedAsEmpty_ItFillsTheCacheWhenItEnds_AndIsNotStartedTwice` (Windows, Core.Test) | 1 | 44939633048 (19:11) | 44939633048 | Test failure |

No build errors, no infrastructure failures and no job timeouts were found. In every failed run the failing step was the test step. The log text of run 44936425002 and the other runs was read with `gh api repos/{owner}/{repo}/actions/jobs/<id>/logs`, because `gh run view --log-failed` returned no text for 18 of the 24 failed runs (the reason was not determined).

Coverage per project, line and branch rates of the assembly under test, from the Cobertura files (Linux, Release):

| Test project | Assembly measured | Line rate | Branch rate | Tests passed / skipped |
| --- | --- | --- | --- | --- |
| `iConsole.Test` | `iConsole` | 91.5% | 83.5% | 5073 / 0 |
| `iConsole.Formats.Test` | `iConsole.Formats` | 87.2% | 75.3% | 576 / 0 |
| `iTerminal.Core.Test` | `iTerminal.Core` | 82.5% | 68.7% | 6346 / 54 (2 tests failed under the collector, 0 in a rerun without it; the names were not captured) |
| `iTerminal.Core.Games.Test` | `iTerminal.Core.Games` | 89.7% | 83.3% | 164 / 0 |
| `iConsole.Media.Video.Test` | `iConsole.Media.Video` | 89.5% | 76.7% | 113 / 1 |
| `iConsole.Media.Speech.Test` | `iConsole.Media.Speech` | 48.7% | 37.1% | 26 / 1 |
| `iConsole.Media.Audio.Test` | `iConsole.Media.Audio` | 31.2% | 27.0% | 38 / 0 |
| `iTerminal.Test` (end-to-end) | all | not measured | not measured | CI: 481 to 483 passed, 1 to 2 skipped of 484 |
| `iTerminal.Specifications.Test` | all | not measured | not measured | CI: 22 passed, 605 skipped of 627 |

Untested source types (no test file with the type's name, and no mention of the name in any test file of the paired project; a proxy, since a type can be covered indirectly):

| Source project | Files | With no test | Lines in them | Folders (count) |
| --- | --- | --- | --- | --- |
| `iConsole` | 512 | 53 | 3 472 | Controls 10, Services 7, Diagnostics 5, Interfaces 5 |
| `iConsole.Formats` | 66 | 23 | 4 199 | Executables 6, Services 5, IWork 3, Fonts 3 |
| `iTerminal.Core` | 908 | 153 | 11 987 | Commands 67, Services 42, Dialogs 13, Model 11, Tasks 5, Jobs 3 |
| `iTerminal.Core.Games` | 12 | 1 | 683 | `ChessEngine.cs` |
| `iConsole.Media.Audio` | 13 | 9 | 1 521 | Sinks 4 (`WinMmAudioSink`, `PulseAudioSink`, `AplayAudioSink`, `AudioOutputBackends`) |
| `iConsole.Media.Speech` | 11 | 3 | 37 | interfaces only |
| `iConsole.Media.Video` | 18 | 3 | 95 | small types |
| `iConsole.Inputs.Posix` | 3 | 1 | 49 | `Interop/LinuxStdin.cs` |
| `iConsole.Inputs.Windows` | 12 | 3 | 740 | `Data/VKey.cs` (730 lines of constants) |

The biggest `iTerminal.Core` files with no test that names them: `Services/Scanning/Twain/TwainScannerBackend.cs` 660 lines, `Commands/Evidence/EvidenceCommands.cs` 558, `Services/Cloud/GoogleDriveFileSystemService.cs` 531, `AI/AiPanelTools.ArchivesAndFolders.cs` 520, `Services/Recycle/RecycleBinStores.cs` 452, `Services/Cloud/OneDriveFileSystemService.cs` 447, `Tasks/ContentModelFolderTransferTask.cs` 242, `Tasks/FolderCopyPipeline.cs` 191, `Jobs/CalendarIdentityJob.cs` 41, `Jobs/ToDoReminderJob.cs` 40.

Test quality counts (grep): `[Ignore]` 6 (all in `iTerminal.Core.Test`; the reasons are a live Swissquote page and Playwright, a running Ollama, an SSH server, and a 360 MB Playwright download); `Thread.Sleep` in tests 28 (12 in `iConsole.Test`, 12 in `iTerminal.Core.Test`, 2 in `iTerminal.Test`, 2 in `iConsole.Media.Speech.Test`); `Task.Delay` of 1 s or more 13; `Assert.Inconclusive` 120 (101 in `iTerminal.Core.Test`, 8 in `iTerminal.Test`), which CI reports as skipped. Localization: 3 860 keys in each of the six `AppStrings*.resx` files and 727 in each of the six `Strings*.resx`, all sorted, no missing key, no duplicate.

### Quality issues


1. **QA-4 `Draw_LoadsThePictureAndShowsItAsAscii` throws a NullReferenceException on Windows from time to time**
   - **Severity**: Medium
   - **Category**: flaky-test
   - **Area**: `iTerminal.Core.Test/Dialogs/Video/ImageFileViewerDialogTest.cs`
   - **Evidence**: runs 44915127385 and 44925547090, both Windows, `NullReferenceException` at `DrawUntilLoadedAsync` (`ImageFileViewerDialogTest.cs:41`). The test already carries diagnostic output ("intermittent NullReferenceException seen only on the Windows CI runner", lines 41-52 region); the second failure came after commit `df1be23b` (14:35, "Fix two Windows-only test failures"), so the fix did not stick. The full exception text is in the log of job 136911314693. The test draws on a `MockBuffer`, which the testing rules say to avoid in favour of `ScreenBuffer`.
   - **Impact**: a random red Windows run; the cause may be a real race in the dialog (`dialog.Picture` or `Parent` read before the load ends).
   - **Suggested fix**: read the stack in the job log, fix the null source, and switch the test to `ScreenBuffer`.
   - **Test that would catch it**: the same test, run in a loop.

2. **QA-5 `Identity_IsReadOnce_UntilItAnswers` asserts on a timing result**
   - **Severity**: Medium
   - **Category**: flaky-test
   - **Area**: `iTerminal.Core.Test/Services/SystemInfo/SystemSamplerTest.cs`
   - **Evidence**: failed in runs 44897329529 and 44900935517 (Windows): "the first pass does not wait for identity, actual: false" at `Assert.IsTrue(first.CpuModel.State != ProbeState.Ok)` (`SystemSamplerTest.cs:167-181`). The assertion holds only if the identity probe has not answered by the time of the first snapshot.
   - **Impact**: random red Windows runs.
   - **Suggested fix**: hold the fake identity probe with a gate until after the first snapshot.
   - **Test that would catch it**: the same test, after the gate.

3. **QA-6 `F2InTheCsvViewerAsksForAFileNameAndWritesTheRowsOnScreen` wrote an empty file**
   - **Severity**: Medium
   - **Category**: flaky-test
   - **Area**: `iTerminal.Test/EditorViewerAndPermissionsTest.cs:152`, CSV viewer export
   - **Evidence**: run 44882901966 (Linux, 08:01): "expected: `tûrõ,név\r\nalma,3\r\n`, actual: `` (length 0)" when reading the target file; run 44905768469 (Windows, 12:49) failed the same test. The test reads the file right after Enter, so the write may not have finished (a missing wait) or the export may report success before it writes.
   - **Impact**: either a flaky test or a user who presses Enter and gets an empty export.
   - **Suggested fix**: wait for the "saved" footer text before reading the file; if the file is still empty after that, the export has a defect.
   - **Test that would catch it**: the same test with the wait.
   - **unverified** as to the cause.

4. **QA-7 Windows end-to-end tests time out singly under load**
   - **Severity**: Medium
   - **Category**: flaky-test
   - **Area**: `iTerminal.Test`
   - **Evidence**: `CtrlShiftDShowsTheFocusedFolderInTheOtherPanel` (44936425002, 30 s), `ShrinkingTheTerminalLeavesNothingOffScreen` (44924437491, 16 s), `CtrlShiftF8_AnyOtherKeyAfterTheFirstEnter_WithdrawsIt` ("Timed out waiting for 'Enter:continue'", 44942013183, `iTerminal.Test/SecureWipeAndSignaturesTest.cs:53`). Each failed once and passed on the next run. The Windows end-to-end step takes 9 to 10 min against 3.5 to 4.5 min on Linux, and the step limit is 20 min (`.github/workflows/build.yml`, step "Tests (including end-to-end suite)").
   - **Update (Q3)**: `CtrlShiftF8_AnyOtherKeyAfterTheFirstEnter_WithdrawsIt` was not a load timeout but a real race in `SecureWipeDialog`: `UpdateLines` ran on both the key thread and the idle thread, so the idle thread could store stale "armed" lines after a key had withdrawn the arming (the failure screen in job 136961068636 shows the Choosing footer over the Armed body). Fixed by serializing `UpdateLines` and reading `Phase` once. `CtrlShiftDShowsTheFocusedFolderInTheOtherPanel` (job 136951812852) was also a real defect, not load: the right panel showed the left folder's path with only the `..` row. `ContentModelFileSystem.Refresh(forceRefresh: true)`, called by the file-watcher debounce, cancelled every refresh task for the URL, whichever panel owned it. With both panels on one folder, the left panel's watcher cancelled the right panel's listing, and nothing started it again. Fixed by cancelling only the tasks of the panel's own name; `AForcedRefresh_DoesNotCancelTheOtherPanelsListingOfTheSameFolder` fails without the fix. `ShrinkingTheTerminalLeavesNothingOffScreen` (job 136907314107, a test of the sample app in `iConsole.Test`, 60x12 screen with the hint row never drawn) is not analysed and stays open.
   - **Impact**: one random red Windows run per few merges; the Windows suite is 2 to 3 times slower than the Linux one.
   - **Suggested fix**: profile the Windows suite (the `profiler` agent) before raising any timeout.
   - **Test that would catch it**: none; these tests are the detectors.

5. **QA-8 153 `iTerminal.Core` files have no test that names them**
   - **Severity**: Medium
   - **Category**: test-gap
   - **Area**: `iTerminal.Core/Commands` (67 files), `Services` (42), `Dialogs` (13), `Model` (11), `Tasks` (5), `Jobs` (3)
   - **Evidence**: pairing script output (see How to reproduce); `grep -rl` for `RecycleBinStores`, `FolderCopyPipeline`, `ContentModelFolderTransferTask`, `EvidenceCommands`, `SevenZipStreamWriter`, `CalendarIdentityJob`, `ToDoReminderJob`, `ChessEngine` finds no test file. These act on files or time: the recycle bin stores (`Services/Recycle/RecycleBinStores.cs`, 452 lines), the folder copy pipeline (`Tasks/FolderCopyPipeline.cs`, `Tasks/ContentModelFolderTransferTask.cs`), the evidence commands (558 lines), the Google Drive and OneDrive services (531 and 447 lines). The overall rate is 82.5% lines, so some are covered through other types; the per-file figure was not read.
   - **Impact**: a regression in a destructive path (recycle, copy, wipe) is found only if an end-to-end scenario happens to touch it.
   - **Suggested fix**: first read the Cobertura rate for the files above and add a unit test for the ones under 50% (recycle bin stores and the folder copy pipeline first).
   - **Test that would catch it**: none; add one per file.

6. **QA-9 `iConsole.Media.Audio` is 31.2% covered and its sinks have no tests**
   - **Severity**: Medium
   - **Category**: test-gap
   - **Area**: `iConsole.Media.Audio/Sinks`
   - **Evidence**: coverage 31.2% lines, 27.0% branches; `WinMmAudioSink.cs` (497 lines), `PulseAudioSink.cs` (397), `AplayAudioSink.cs` (303), `Decoders/Mp3AudioDecoder.cs` (127) have no test naming them; on the Windows runner 17 to 19 of the 38 to 40 tests in this project are skipped (run 44943515522: 21 passed, 19 skipped). Commit `25ba25f5` ("Probe audio devices on a background task") changed `AplayAudioSink.cs` and `PulseAudioSink.cs` (`gh api` file list), the two sinks with no test.
   - **Impact**: audio playback regressions are found by users.
   - **Suggested fix**: add sink tests against a fake process runner (the `AplayAudioOutputBackendTest` already does this for the backend) and un-skip the tests that need only a decoder.
   - **Test that would catch it**: none.

7. **QA-10 605 of 627 BDD scenarios are skipped**
    - **Severity**: Medium
    - **Category**: test-gap
    - **Area**: `iTerminal.Specifications.Test`
    - **Evidence**: run 44939633048: "Passed: 22, Skipped: 605, Total: 627". 75 `@ignore` tags in `Features/**/*.feature`; the generated files call `testRunner.SkipScenarioAsync()` when the tag is set (for example `Features/Compare/Compare.CompareFolders.feature.cs:149-152`). `Documentation/architecture/testing.md` describes the tag as the stub marker, so this is by design, but the skipped scenarios show nothing.
    - **Impact**: 96% of the documented behaviour has no executable check; the pass count of the project is not a measure of anything.
    - **Suggested fix**: bind the steps for one feature folder per improvement, starting with `Files` and `Archives`.
    - **Test that would catch it**: the scenarios themselves.

8. **QA-11 `ReleaseArchive` silently skips archive entries that contain a colon, and has no test**
    - **Severity**: Medium
    - **Category**: defect
    - **Area**: `iTerminal.Core/Services/RuntimeDependencies/ReleaseArchive.cs`, `iTerminal.Core/Helper/ArchivePaths.cs`
    - **Evidence**: commit `8cedca9a` (#803) merged three checks into `ArchivePaths.Inside`. It refuses any name with `:` on every OS (`ArchivePaths.cs:18`), and strips leading slashes, so `/etc/x` now lands inside the folder instead of being refused. `ReleaseArchive` calls it at `ReleaseArchive.cs:34` and `:54` and does `continue` on null, with no message, so a file with a colon in a Playwright, Ollama or ffmpeg package is left out and the install reports success. `UpdateService` (`UpdateService.cs:625`) throws instead, which is correct. `iTerminal.Core.Test` has no test that names `ReleaseArchive`; `ArchiveExtractionServiceTest` covers `Inside` through a single entry list, and `UpdateServiceTest.cs:496-503` covers `../escape.txt` only.
    - **Impact**: a downloaded runtime dependency may be incomplete on Linux or macOS without any message; an archive that escaped before is now extracted.
    - **Suggested fix**: allow `:` on non-Windows systems, or make `ReleaseArchive` log and fail when `Inside` returns null.
    - **Test that would catch it**: none; add a `ReleaseArchive` test with an entry `a:b.txt` and an entry `../x`.
    - **unverified** (read, not reproduced).

9. **QA-12 Fix that did not stick: the picture dialog and the Tab completion**
    - **Severity**: Low
    - **Category**: pipeline
    - **Area**: process
    - **Evidence**: `Draw_LoadsThePictureAndShowsItAsAscii` failed again 2 h after commit `df1be23b`, which claimed to fix two Windows-only failures (QA-4); the Tab completion commit `693956d2` adds diagnostics only (QA-3). Recorded here so the next run checks whether each stays green for 10 runs.
    - **Impact**: a fix that only adds diagnostics is counted as a fix.
    - **Suggested fix**: none; track in the next run.
    - **Test that would catch it**: not applicable.

10. **QA-13 No end-to-end scenario for the screen saver**
    - **Severity**: Low
    - **Category**: test-gap
    - **Area**: `iTerminal.Test`, `iTerminal.Core/ScreenSavers`
    - **Evidence**: `grep -rli ScreenSaver iTerminal.Test` finds 0 files; the settings page `iTerminal.Core/Dialogs/SettingsScreenSaverTabPage.cs` (111 lines) has no test that names it.
    - **Impact**: a screen saver that never starts or never stops is not found before release.
    - **Suggested fix**: one end-to-end scenario that sets a one-second timeout, waits for the saver, and presses a key.
    - **Test that would catch it**: none.

### What is in good shape

- `iConsole` is covered at 91.5% lines and 83.5% branches by 5 073 tests that run in about 45 s; `iTerminal.Core.Games` 89.7%, `iConsole.Media.Video` 89.5%, `iConsole.Formats` 87.2%.
- Localization: 3 860 `AppStrings` keys and 727 `Strings` keys are identical and sorted in all six languages.
- No `[Ignore]` without a reason (6 of 6 have one), no `__` key written to `Settings` by the code in `iTerminal.Core` and `iConsole` (grep for a `Set`/`Save` call with a `"__` literal found none).
- The 18 one-line empty `catch { }` blocks in `iTerminal.Core` are all best-effort cleanup (`process.Kill`, `Disconnect`, `Dispose`, deleting a pid file); none hides a failure from the user.
- The recent `Program.cs` change (#804) disposes the host, removes the `CancelKeyPress` handler in a `finally`, and has a unit test for the cancelled path (`UpdateCommandLineTest`).
- "Release builds": 50 of 50 pull-request runs passed.

### Suggested order

1. QA-6 and QA-7 (flaky tests in the end-to-end suite erode trust in every run), then QA-4 and QA-5.
2. QA-11 (defect a user can reach).
3. Process: do not merge while the Windows job is red.
4. Coverage gaps: QA-8, QA-9, QA-10, QA-13.
5. QA-12 on the next run.

### Status of earlier issues

None (first run).

### How to reproduce

```bash
# Pipeline
gh run list --branch main --workflow "Build and test" --limit 80 --json databaseId,conclusion,createdAt,startedAt,updatedAt,headSha,displayTitle
gh run view <id> --json jobs
gh api repos/{owner}/{repo}/actions/jobs/<job id>/logs      # when `gh run view <id> --log-failed` is empty
gh run list --workflow "Release builds" --limit 50 --json conclusion,event
gh api repos/{owner}/{repo}/commits/<sha> --jq '.files[].filename'

# Coverage (one project at a time; Linux)
dotnet test <project>/<project>.csproj -c Release --collect:"XPlat Code Coverage" \
  --settings coverage.runsettings --results-directory <scratch>/cov/<project>
# then read <scratch>/cov/<project>/*/coverage.cobertura.xml, per <package>: lines with hits > 0 over all lines

# Test quality counts
grep -rE '^\s*\[Ignore' --include=*.cs <test project>
grep -rE 'Thread\.Sleep' --include=*.cs <test project>
grep -rE 'Assert\.Inconclusive' --include=*.cs <test project>
```

The untested-type pairing was a script: for each `<source>` and `<source>.Test` pair it lists the `.cs` files of the source whose file name has no `<name>Test.cs` or `<name>.cs` in the test project and whose type name is not mentioned in any test file there. The scratch files of this run are not committed.

## Q2 2026-11-21: warnings

Scope `warnings`. Branch `docs/qa-warnings`, from commit `fd0c0d13` on `main`. The build was `dotnet build iTerminal.slnx -c Release --no-incremental` on Linux 7.3.0-11, with the log in the scratchpad (`build.log` and `warnings-unique.txt` in the same folder). This is the first warnings run, so the SonarAnalyzer total of this run is the baseline for later runs. Not measured: the Windows build (warnings from Windows-only code paths in `iConsole.Inputs.Windows` and the Windows branches of the other projects are not covered), the `#pragma` and `SuppressMessage` count (Phase 3 step 6), and the `.editorconfig` severity changes. The build's only error is the known `MSB4057` in `iTerminal.Installer.wixproj` (no `Rebuild` target), not counted as a warning.

### Summary

- The Release build has 1,838 unique warnings. 1,824 (99.3%) come from SonarAnalyzer (`S####`), 5 from the compiler (`CS`), 7 from the .NET and MSTest analyzers (`CA`, `MSTEST`), and 2 have no rule code.
- The 1,827 warnings in the non-test projects are almost all Sonar. The 9 in the test projects are `iTerminal.Core.Test` (8) and `iConsole.Media.Audio.Test` (1); none of them is from Sonar.
- Most warnings are style rules: `S3358` (nested ternary), `S108` (empty block), `S8949` (cancellation token not passed on), `S927` (parameter name differs from base) and `S1905` (redundant cast). These five codes make up 904 of the 1,824 Sonar warnings.
- The warnings that read as defects are the swallowed-exception rule `S2486` (68) and the compiler warning `CS8604` (1). The disposal rule `S2930` (34) and `CS0108` (1) have since been fixed. They are listed as issues below.
- No `CS8602` (possible null dereference) warning is in the build. The `S2245` (random number) and `S2068` (`password`) hits were read as false positives: the random numbers are in game code, and the `password` hits are property names.

### Measurements

Build warnings by project, unique warnings per project, with the split by analyzer. "Other" is the two entries with no rule code (one NuGet `NU1503`, one entry for the solution and the specification project).

| Project | Unique warnings | Sonar (`S####`) | Compiler (`CS####`) | .NET and MSTest analyzers (`CA`, `MSTEST`) | Other |
| --- | --- | --- | --- | --- | --- |
| `iTerminal.Core` | 1,050 | 1,048 | 2 | 0 | 0 |
| `iConsole` | 579 | 579 | 0 | 0 | 0 |
| `iConsole.Formats` | 67 | 66 | 1 | 0 | 0 |
| `iTerminal.Core.Games` | 45 | 45 | 0 | 0 | 0 |
| `iConsole.Media.Audio` | 38 | 38 | 0 | 0 | 0 |
| `iConsole.Media.Video` | 16 | 16 | 0 | 0 | 0 |
| `iConsole.Inputs.Windows` | 15 | 15 | 0 | 0 | 0 |
| `iConsole.Inputs.Posix` | 9 | 9 | 0 | 0 | 0 |
| `iConsole.Media.Speech` | 7 | 7 | 0 | 0 | 0 |
| `iTerminal` | 1 | 1 | 0 | 0 | 0 |
| `iTerminal.Core.Test` | 8 | 0 | 2 | 6 | 0 |
| `iConsole.Media.Audio.Test` | 1 | 0 | 0 | 1 | 0 |
| Solution and `iTerminal.Specifications.Test` | 2 | 0 | 0 | 0 | 2 |
| Total | 1,838 | 1,824 | 5 | 7 | 2 |

Top warning codes (Sonar unless marked):

| Code | Analyzer | Count | Projects | Class |
| --- | --- | --- | --- | --- |
| `S3358` | SonarAnalyzer.CSharp | 347 | not split by project | Low (nested ternary) |
| `S108` | SonarAnalyzer.CSharp | 163 | not split by project | Low (empty block) |
| `S8949` | SonarAnalyzer.CSharp | 153 | not split by project | Low (cancellation token not passed on) |
| `S927` | SonarAnalyzer.CSharp | 127 | not split by project | Low (parameter name differs from base) |
| `S1905` | SonarAnalyzer.CSharp | 114 | not split by project | Low (redundant cast) |
| `S3267` | SonarAnalyzer.CSharp | 70 | not split by project | Low (loop could be LINQ) |
| `S2486` | SonarAnalyzer.CSharp | 68 | `iConsole` (sampled) | High (exception swallowed) |
| `S1075` | SonarAnalyzer.CSharp | 50 | not split by project | Low (hard-coded URI or path) |
| `S2325` | SonarAnalyzer.CSharp | 49 | not split by project | Low (could be static) |
| `S1144` | SonarAnalyzer.CSharp | 45 | not split by project | Low (unused private member) |
| `S2930` | SonarAnalyzer.CSharp | 34 | `iConsole`, `iTerminal.Core` (sampled) | High (not disposed) |
| `S5332` | SonarAnalyzer.CSharp | 28 | not split by project | Medium (plain `http` or `ftp`) |
| `S1172` | SonarAnalyzer.CSharp | 13 | `iConsole` (sampled) | Medium (unused parameter) |
| `S2068`, `S2245` | SonarAnalyzer.CSharp | 5 and 7 | `iTerminal.Core`, `iTerminal.Core.Games` | Low (read: no real secret, game randomness) |
| `CS0108` | compiler | 1 | `iTerminal.Core` | High (member hides base member) |
| `CS8604` | compiler | 1 | `iConsole.Formats` | High (possible null argument) |
| `CA1416` | .NET analyzer | 2 | test projects | Medium (Unix-only API in tests) |

The 1,824 Sonar warnings are 99.3% of the total. The five largest codes (S3358, S108, S8949, S927, S1905) make up 904 of them.

### Quality issues

1. **QA-15 Exceptions swallowed in the idle loop and elsewhere (S2486, 68 warnings)**
   - **Severity**: Medium
   - **Category**: error-handling
   - **Area**: `iConsole/Controls/Control.cs:949`, `iConsole/Desktops/Desktop.cs:101`, `iConsole/Controls/StatusBar/Date.cs:43`
   - **Evidence**: `Control.cs:949` is `try { control.OnIdle(); } catch { }` with no comment, so any exception thrown by any control's idle handler is discarded. The same pattern appears in `Desktop.cs:101` and `Date.cs:43`. The analyzer reports each as "Handle the exception or explain in a comment why it can be ignored".
   - **Impact**: a control that throws during idle stops working silently, and nothing is logged, so a developer finds it only when the screen does not update.
   - **Suggested fix**: record the exception through the framework's existing diagnostics and keep the loop running; add a comment where swallowing is intended.
   - **Test that would catch it**: none; a unit test with a control whose `OnIdle` throws, asserting that the failure is recorded.
   - **unverified** as to which of the 68 places hide a real failure; only `Control.cs:949` was read, and `Desktop.cs:101` and `Date.cs:43` were taken from the analyzer output.

2. **QA-17 Possible null argument in the PDF signature reader (CS8604)**
   - **Severity**: Low
   - **Category**: defect
   - **Area**: `iConsole.Formats/Pdf/PdfSignatureReader.cs:119`
   - **Evidence**: the compiler reports a possible null for argument `range` of `Check(...)`, called at `iConsole.Formats/Pdf/PdfSignatureReader.cs:119` (`found.Add(Check(pdf, keyStart, range, number, configureChain, cancellationToken));`). The loop above the call has a `continue` path (line 116), so the code before the call was read, but the source of `range` was not traced.
   - **Impact**: if a path reaches the call with `range` null, the reader may throw a `NullReferenceException` on a malformed PDF instead of reporting it as unreadable.
   - **Suggested fix**: check `range` for null before the call, or make `Check` reject a null range explicitly; add a test with a malformed signature range.
   - **Test that would catch it**: none; the reader's test set was not read in this run.
   - **unverified** as to whether the null path is reachable.

### What is in good shape

- No `CS8602` (possible dereference of null) warning in the build.
- The test projects have no Sonar warning: the analyzer is not referenced there, as intended.
- The only build error is the known WiX `MSB4057`.
- Of the security-tagged Sonar rules read in this run, the open question is the plain `http` and `ftp` URLs (`S5332`, 28 warnings, for example `iTerminal.Core/Jobs/WeatherJob.cs:81`); the `S2068` (`password`) and `S2245` (game randomness) hits are false positives.

### Suggested order

1. QA-15 (exceptions swallowed in the idle loop; the rest are the same pattern).
2. QA-17 (compiler warning, one-line fix).
3. The style codes (`S3358`, `S108`, `S8949`) only when a file is touched for another reason; they are not worth a dedicated change.
4. The `S5332` URLs need a decision: each `http` URL is either fixed or documented as intentional.

### Status of earlier issues

Not applicable: Q1 did not measure warnings, so none of its issues is re-checked here.

### How to reproduce

```bash
# Build (about 5 minutes); the WiX project is expected to fail with MSB4057
dotnet build iTerminal.slnx -c Release --no-incremental -nologo -v:n > <scratch>/build.log 2>&1

# Unique warnings, grouped by code
grep -E ": warning " <scratch>/build.log | sed -E 's/^ *[0-9]+>//' | sed -E 's/^\s+//' | sort -u > <scratch>/warnings-unique.txt
grep -oE "warning [A-Z]+[0-9]+" <scratch>/warnings-unique.txt | sort | uniq -c | sort -rn

# Per project
grep -oE "\[[^]]*\.(csproj|slnx|props)\]" <scratch>/warnings-unique.txt | sort | uniq -c | sort -rn
```


## Q3 2026-11-22: all (pipeline, coverage, warnings, code review)

Scope `all`, chosen because the request named none. Branch `docs/qa-all-q3`, cut from `main` at commit `b9b81269`; Linux 7.3.0-11, `dotnet 11.0.100`, `gh 2.46.0`. Pipeline history is the last 50 runs of "Build and test" on `main` (2026-11-21 09:55 to 2026-11-22 04:01 UTC); "Release builds" has no run on `main`. Coverage is the `XPlat Code Coverage` collector (`coverlet.collector` is referenced by the test projects, with `coverage.runsettings`) on the seven unit test projects that build on Linux. The Release build ran on a clean `git archive HEAD` copy in the scratchpad, because a first build in the working tree failed with `MSB4018` ("iConsole.dll is being used by another process": another process was building in the same tree). Code review covered the changes since Q1: the voice recorder (#809), the AI panel tools (#816), the wipe dialog and panel refresh fix (#815) and the Sonar fixes (#812). Not measured: the Windows build and Windows coverage; `iTerminal.Test` and `iTerminal.Specifications.Test` coverage (the end-to-end suite was not run; CI logs were read instead). The working tree held uncommitted edits to other documents made by another process; they are not part of this run.

### Summary

- Pipeline: of the last 50 "Build and test" runs on `main`, 16 passed, 22 failed and 12 were cancelled (failure rate 57.9% of the 38 completed runs, 44% of all). The longest red streak is 12 runs. The 8 runs since Q1 have 3 failures; the head of `main` (`b9b81269`, run 44983162486) is green. Median duration of successful runs rose from 15.8 min (older 8) to 20.7 min (newest 8).
- Largest coverage gap: `iTerminal.Core` is at 82.4% lines and 68.6% branches, but `EvidenceCommands.cs` is at 11% (276 lines) and `TwainScannerBackend.cs` at 0% (235 lines); `iConsole.Media.Audio` is at 36.1% lines. The Q1 name-pairing count of untested `iTerminal.Core` files grew from 153 to 173, but coverage shows the pairing overstates the gap (see the status of QA-8).
- Build warnings: 1,805 unique (Q2: 1,838), 1,793 of them Sonar. The 34 `S2930` and the `CS0108` of Q2 are gone.
- Most serious defect found: the new voice recorder shares Ctrl+Shift+X with the password manager (QA-18). A recorder that dies while recording is lost without a message (QA-19) is fixed in #827 and no longer listed.

### Measurements

Pipeline, branch `main`, last 50 runs per workflow:

| Workflow | Runs | Pass | Fail | Cancelled | Failure rate (completed) | Median duration of successful runs |
| --- | --- | --- | --- | --- | --- | --- |
| Build and test | 50 | 16 | 22 | 12 | 57.9% (22 of 38) | 20.7 min (newest 8 of 16) against 15.8 min (older 8): +31% |
| Release builds | 0 on `main` | n/a | n/a | n/a | n/a | not measured; the 10 newest runs (all `pull_request`) passed |

Fewer than 40 successful runs exist, so 8 against 8 was compared. The window is one day, so the trend is noisy; the slowest successes (26.0, 26.4, 28.2 min) are all on days with several merges in a row.

Failure patterns (each failed run counted once, under its first failing test; no build error, infrastructure failure or job timeout was found):

| Pattern | Failed runs | First run | Last run | Class |
| --- | --- | --- | --- | --- |
| `ShowsMemoryAndProcessorUseForEachProcess` (Windows, Core.Test), 12 runs in a row | 12 | 44891874408 (11-21 09:55) | 44907848614 (13:14) | Test failure, fixed |
| `Draw_LoadsThePictureAndShowsItAsAscii` (Windows) | 2 | 44915127385 | 44925547090 | Flaky |
| Windows end-to-end timeouts (`CtrlShiftDShowsTheFocusedFolderInTheOtherPanel`, `ShrinkingTheTerminalLeavesNothingOffScreen`) | 2 | 44924437491 | 44936425002 | Timeout, QA-7 |
| `ASlowListingIsNotCachedAsEmpty_...` (Windows, Core.Test) | 1 | 44939633048 | 44939633048 | Test failure |
| `CtrlShiftF8_AnyOtherKeyAfterTheFirstEnter_WithdrawsIt` (Windows) | 1 | 44942013183 | 44942013183 | Test failure, fixed in #815 |
| `ThePaletteFooterAndSixMoreDialogsInGerman_AreGerman("Passwords", ...)`, both OS | 2 | 44943515522 | 44947969087 | Test failure, fixed in #811 (no red run for it since) |
| `EnterOpensATarGzAndF5CopiesOutOfIt` (Windows, 34 s) | 1 | 44948346711 | 44948346711 | Flaky (timeout) |
| `OneHungDisk_DoesNotBlankTheOthers` (Windows, Core.Test) | 1 | 44981366585 (11-22 03:35) | 44981366585 | Flaky |

Counts add to 22. Run 44942013183 is also counted for the German test in Q1; here it is placed under its first failure. The German test was read in the logs of runs 44943515522 and 44947969087 (jobs 136979370778 and 136979371056: "Timed out waiting for the German text.").

Coverage per project, line and branch rate of the assembly under test (Cobertura package, Linux, Release):

| Test project | Assembly | Line rate | Branch rate | Tests passed / skipped | Q1 line rate |
| --- | --- | --- | --- | --- | --- |
| `iConsole.Test` | `iConsole` | 91.4% | 83.5% | 5073 / 0 | 91.5% |
| `iConsole.Formats.Test` | `iConsole.Formats` | 87.2% | 75.3% | 576 / 0 | 87.2% |
| `iTerminal.Core.Test` | `iTerminal.Core` | 82.4% | 68.6% | 6374 / 54, 1 failed under the collector (fixed in #827) | 82.5% |
| `iTerminal.Core.Games.Test` | `iTerminal.Core.Games` | 89.7% | 83.3% | 164 / 0 | 89.7% |
| `iConsole.Media.Video.Test` | `iConsole.Media.Video` | 89.5% | 76.7% | 113 / 1 | 89.5% |
| `iConsole.Media.Speech.Test` | `iConsole.Media.Speech` | 48.7% | 37.1% | 26 / 1 | 48.7% |
| `iConsole.Media.Audio.Test` | `iConsole.Media.Audio` | 36.1% | 32.5% | 40 / 0 | 31.2% |
| `iTerminal.Test` (end-to-end) | all | not measured | not measured | CI run 44983162486 (head of `main`): green | not measured |
| `iTerminal.Specifications.Test` | all | not measured | not measured | not re-read | not measured |

`iTerminal.Core` by folder, lowest line rate first (lines in the measured assembly): `Screens` 47% (77), `Jobs` 65% (852), `AI` 66% (1,538), `Commands` 67% (2,842), `Services` 77% (22,472), `Model` 85%, `Documents` 86%, `Tasks` 87%, `Dialogs` 88% (26,602). Selected files: `Commands/Evidence/EvidenceCommands.cs` 11% (276 lines), `Services/Scanning/Twain/TwainScannerBackend.cs` 0% (235), `AI/AiPanelTools.MorePanelOperations.cs` 58% (277), `Dialogs/Voice/VoiceRecorderDialog.cs` 85% (305), `Services/Voice/VoiceRecorderService.cs` 91% (70), `Services/Recycle/RecycleBinStores.cs` 80% (172), `Tasks/ContentModelFolderTransferTask.cs` 98% (113), `Tasks/FolderCopyPipeline.cs` 89% (28), `Services/RuntimeDependencies/ReleaseArchive.cs` 95% (39).

Untested source types by name pairing (no `<Name>Test.cs` and no mention of the type name in any test file of the paired project; a proxy):

| Source project | Files | With no test | Lines in them | Largest folders (count) |
| --- | --- | --- | --- | --- |
| `iConsole` | 512 | 57 | 3,985 | Services 8, Diagnostics 5, Interfaces 5, Controls 4 |
| `iConsole.Formats` | 66 | 24 | 4,210 | Executables 6, Services 5, IWork 3, Fonts 3 |
| `iTerminal.Core` | 912 | 173 | 13,764 | Commands 73, Services 52, Dialogs 13, Model 12, Tasks 5 |
| `iTerminal.Core.Games` | 12 | 1 | 683 | `ChessEngine.cs` |
| `iConsole.Media.Audio` | 13 | 8 | 1,532 | Sinks 4 (`WinMmAudioSink` 497 lines, `PulseAudioSink` 403, `AplayAudioSink` 311) |
| `iConsole.Media.Speech` | 11 | 2 | 34 | interfaces |
| `iConsole.Media.Video` | 18 | 2 | 92 | small types |
| `iConsole.Inputs.Posix` | 3 | 2 | 76 | `LinuxStdin.cs` |
| `iConsole.Inputs.Windows` | 12 | 2 | 736 | `VKey.cs` (730 lines of constants) |

Test quality counts (grep): `[Ignore]` 6, all in `iTerminal.Core.Test`, each with a reason (live Swissquote page, Ollama, SSH server, a 360 MB Playwright download); `Thread.Sleep` 30 (12 in `iConsole.Test`, 14 in `iTerminal.Core.Test`, 2 in `iTerminal.Test`, 2 in `iConsole.Media.Speech.Test`); `Assert.Inconclusive` 119 (101 in `iTerminal.Core.Test`). Skips are by platform: on the Windows runner 19 of 40 `iConsole.Media.Audio.Test` tests are skipped (job 137083178496).

Build warnings, Release, `dotnet build iTerminal.slnx -c Release --no-incremental`, unique lines (the only errors are the known WiX `MSB4057`):

| Project | Unique warnings | Sonar (`S####`) | Compiler (`CS####`) | .NET and MSTest analyzers | Other |
| --- | --- | --- | --- | --- | --- |
| `iTerminal.Core` | 1,020 | 1,019 | 1 | 0 | 0 |
| `iConsole` | 577 | 577 | 0 | 0 | 0 |
| `iConsole.Formats` | 67 | 66 | 1 | 0 | 0 |
| `iTerminal.Core.Games` | 45 | 45 | 0 | 0 | 0 |
| `iConsole.Media.Audio` | 38 | 38 | 0 | 0 | 0 |
| `iConsole.Media.Video` | 16 | 16 | 0 | 0 | 0 |
| `iConsole.Inputs.Windows` | 15 | 15 | 0 | 0 | 0 |
| `iConsole.Inputs.Posix` | 9 | 9 | 0 | 0 | 0 |
| `iConsole.Media.Speech` | 7 | 7 | 0 | 0 | 0 |
| `iTerminal` | 1 | 1 | 0 | 0 | 0 |
| `iTerminal.Core.Test` | 8 | 0 | 2 | 6 | 0 |
| `iConsole.Media.Audio.Test` | 1 | 0 | 0 | 1 | 0 |
| Installer (`NU1503`) | 1 | 0 | 0 | 0 | 1 |
| Total | 1,805 | 1,793 | 4 | 7 | 1 |

Test projects have no `S####` warning, as intended. Against Q2 (1,838 total, 1,824 Sonar): minus 34 `S2930` and the `CS0108` (fixed in #812), plus a few new Sonar hits in the new code; the net is minus 33 (Sonar minus 31).

Top warning codes:

| Code | Analyzer | Count | Projects | Class |
| --- | --- | --- | --- | --- |
| `S3358` | Sonar | 348 | not split | Low (nested ternary) |
| `S108` | Sonar | 163 | not split | Low (empty block) |
| `S8949` | Sonar | 153 | not split | Low (cancellation token not passed on) |
| `S927` | Sonar | 127 | not split | Low (parameter name differs from base) |
| `S1905` | Sonar | 114 | not split | Low (redundant cast) |
| `S3267` | Sonar | 70 | not split | Low |
| `S2486` | Sonar | 68 | 6 | High (exception swallowed; QA-15) |
| `S2325` | Sonar | 51 | not split | Low (could be static) |
| `S1075` | Sonar | 50 | not split | Low (hard-coded URI or path) |
| `S9422`, `S6966` | Sonar | 40 each | 5 and 4 | Low |
| `S5332` | Sonar | 28 | 3 | Medium (plain `http` or `ftp`) |
| `S1172` | Sonar | 13 | 4 | Medium (unused parameter) |
| `CS8604` | compiler | 1 | `iConsole.Formats` | High (possible null argument; QA-17) |
| `CS9107` | compiler | 1 | `iTerminal.Core` | Low (`EditTextFileDialogCommand.cs:21`, a captured parameter also passed to the base) |
| `CS0067` | compiler | 2 | `iTerminal.Core.Test` | Low (`SystemSamplerFakes.cs:137,139`, unused events in a fake) |
| `MSTEST0044` | MSTest analyzers | 5 | `iTerminal.Core.Test` | Low (`DataTestMethod` is obsolete) |
| `CA1416` | .NET analyzers | 2 | 2 test projects | Medium (Unix-only API in tests) |

Suppressions: 7 `#pragma warning disable` (all in test code; 5 carry a reason, 2 have none: `iTerminal.Core.Test/Dialogs/NetworkBrowserDialogTest.cs:351` for `CS0162` and `iTerminal.Core.Test/Helper/MarkableTestModel.cs:30` for `CS0067`); 0 `[SuppressMessage]`; one `NoWarn` (`NETSDK1206` in `Directory.Build.props:6`); `.editorconfig` sets 4 diagnostic severities (naming rules to suggestion, `IDE1006` to none). The two reasonless pragmas are Low (an unreachable `yield break` that makes the method an iterator, and an interface event a fake never raises); they are counted under QA-27.

### Quality issues

1. **QA-18 Ctrl+Shift+X is bound to the voice recorder and to the password manager**
   - **Severity**: High
   - **Category**: input
   - **Area**: `iTerminal.Core/Commands/VoiceRecorderDialogCommand.cs`, `iTerminal.Core/Commands/PasswordManagerDialogCommand.cs`
   - **Evidence**: `VoiceRecorderDialogCommand.cs:13-14` sets `Key` to `new Key(ConsoleKey.X, true, false, true)` (Ctrl+Shift+X). `PasswordManagerDialogCommand.cs:19` sets `AlternateKey` to the same chord and handles it at `:25-28`; its comment says this is "the chord that always reaches it" because Ctrl+X cuts in a file panel. `HotKeyCollisionTest.cs:83` pins the password manager's alternate key to Ctrl+Shift+X, and the file's other tests exist because earlier collisions made a feature unreachable (design review finding 11, `Documentation/research/design-review-overview.md:729`). The test for the recorder (`VoiceRecorderDialogTest.cs:164`, `TheCommandIsCtrlShiftX`) asserts only its own key. `Documentation/architecture/dialogs.md:308` still says Ctrl+Shift+X is the password manager's, and `:739` gives it to the recorder. No end-to-end test presses Ctrl+Shift+X (`grep -rn ConsoleKey.X iTerminal.Test` finds nothing).
   - **Impact**: one of the two dialogs does not open on its default chord, or the chord opens the wrong one; the footer and the command palette show a key that does something else. Which command wins depends on the order the key router offers the commands, which was not traced.
   - **Suggested fix**: give the recorder a free chord (not Alt+letter, not Ctrl+M) and add a collision test next to `AddEvidenceAndThePasswordManagerHaveDifferentChords`; fix the sentence in `dialogs.md`.
   - **Test that would catch it**: none; a `HotKeyCollisionTest` case comparing every command's `Key` and `AlternateKey` over the container (a generic duplicate-chord test would have caught finding 11 as well), plus a `DialogsTest` scenario that presses the recorder's chord.
   - **unverified** as to which command wins; the duplicate itself was read in both files.






2. **QA-24 Median run time of "Build and test" grew by a third**
   - **Severity**: Low
   - **Category**: pipeline
   - **Area**: `.github/workflows` (Build and test)
   - **Evidence**: median of the successful runs, newest 8 against the 8 before: 20.7 min against 15.8 min; the successes of 26.0, 26.4 and 28.2 min are runs 44979642086, 44950987713 and 44931138996. The Windows end-to-end step is the largest part (9 min 16 s in run 44947969087).
   - **Impact**: slower feedback; only 16 successful runs exist, so the trend may be noise.
   - **Suggested fix**: none until the next run confirms it; then compare the Windows job times step by step (`gh run view <id> --json jobs`).
   - **Test that would catch it**: not applicable.



3. **QA-27 Two suppressions without a reason, and obsolete `DataTestMethod` use**
    - **Severity**: Low
    - **Category**: test-quality
    - **Area**: `iTerminal.Core.Test`
    - **Evidence**: `NetworkBrowserDialogTest.cs:351` (`#pragma warning disable CS0162`) and `MarkableTestModel.cs:30` (`#pragma warning disable CS0067`) carry no reason; five `MSTEST0044` warnings (`DataTestMethod`) in `WatchAlarmTest.cs:30`, `WatchStopwatchTest.cs:49`, `WatchTimerTest.cs:10,24` and `NetworkManagerDialogTest.cs:67`; two `CS0067` in `SystemSamplerFakes.cs:137,139` that have no pragma.
    - **Impact**: noise in the test build; a reader cannot tell whether the suppression is still needed.
    - **Suggested fix**: add one-line reasons, replace `DataTestMethod` with `TestMethod`, and declare the fake's events with `#pragma ... // reason` or an empty accessor.
    - **Test that would catch it**: not applicable.

### Code review notes (no issue raised)

- `VoiceRecorderService` releases the microphone under a lock on every path (`Start` after `Dispose`, a second `Start`, `Discard`), and the dialog reads a snapshot under a lock so no key handler waits for the device; the dialog is removed from the tree by `Close()` (`VoiceRecorderDialog.cs`). Coverage of both is 85% and 91%.
- `SecureWipeDialog` (#815): `UpdateLines` is serialized with `_linesLock` and reads the phase once; `ContentModelFileSystem.Refresh` now cancels only the panel's own tasks (`ContentModelFileSystem.cs:221`), with a test in `ContentModelFileSystemTest`.
- `GoToPath` in the AI tools cannot carry a credential: `Url` parses only `user@host` (`iConsole/Data/Url.cs:30-42`), no password.
- `Control.cs:949` and `Desktop.cs:101` still hold the empty `catch` (QA-15).

### What is in good shape

- `iConsole` 91.4% lines and 83.5% branches over 5,073 tests in about 40 s; `iTerminal.Core.Games` 89.7%, `iConsole.Media.Video` 89.5%, `iConsole.Formats` 87.2%.
- Files that Q1 listed as untested are in fact well covered: `ContentModelFolderTransferTask` 98%, `ReleaseArchive` 95%, `FolderCopyPipeline` 89%, `RecycleBinStores` 80%.
- Build warnings fell by 33 since Q2: the disposal rule `S2930` (34) and `CS0108` are at zero. No `CS8602` and no `S####` in a test project.
- Head of `main` is green; no build error, infrastructure failure or job timeout in 50 runs. No `[Ignore]` without a reason.
- The new voice recorder service and dialog have unit tests that cover the start, stop, discard, transcription and missing-model paths.

### Suggested order

1. QA-18 (duplicate chord, one-line fix plus a collision test).
2. QA-24 (re-check on the next run).
3. Coverage: QA-8, QA-9, QA-10, QA-13 from Q1.
4. QA-11, QA-15, QA-17 from Q1/Q2, then QA-27.

### Status of earlier issues

- **QA-4** (`Draw_LoadsThePictureAndShowsItAsAscii`): last failure run 44925547090; no failure in the 30 runs since. Open, watch.
- **QA-5** (`Identity_IsReadOnce_UntilItAnswers`): not seen again, but its sibling in the same fixture failed in run 44981366585. The fixture is changed in #827 (identity read gated, probe budgets held); open until CI and later runs confirm it.
- **QA-6** (CSV export empty file): no failure since 44905768469. Open, watch.
- **QA-7**: `CtrlShiftF8_...` was fixed in #815; `CtrlShiftDShowsTheFocusedFolderInTheOtherPanel` was also fixed there (panel refresh cancelling the other panel's listing); `ShrinkingTheTerminalLeavesNothingOffScreen` not seen since 44924437491. The archive-listing timeout seen on Windows is fixed in #827. Partly fixed, open.
- **QA-8** (untested `iTerminal.Core` files): name pairing now gives 173 (Q1: 153); coverage shows `RecycleBinStores` 80%, `ContentModelFolderTransferTask` 98% and `FolderCopyPipeline` 89%, so the original examples are covered. The remaining gaps (evidence and AI-tool error paths) are covered in #827. Open, narrowed.
- **QA-9** (audio coverage): 36.1% lines (Q1: 31.2%), 40 passed and 0 skipped on Linux; on Windows 19 of 40 are skipped. Open.
- **QA-10** (605 of 627 BDD scenarios skipped): not re-measured.
- **QA-11** (`ReleaseArchive` and a colon in an entry name): code unchanged (`ArchivePaths.cs:18` refuses `:` on every OS; `ReleaseArchive.cs:34` continues on null). The file is 95% covered, but no test with a colon entry was found by name. Open, unverified.
- **QA-12** (fixes that did not stick): the Tab completion and the picture dialog did not fail again in the window. Open, watch.
- **QA-13** (screen saver, no end-to-end scenario): still no match in `iTerminal.Test`; unit tests exist (`ScreenSaverSettingsTest.cs`, `ScreenSaverEffectsTest.cs`, `ScreenSaverOverlayTest.cs`). Open.
- **QA-15** (S2486): 68 warnings, unchanged; `Control.cs:949` and `Desktop.cs:101` unchanged. Open.
- **QA-17** (`CS8604` in `PdfSignatureReader.cs:119`): still reported. Open.
- Q2 items `S2930` (34) and `CS0108`: fixed in #812, zero in this build.

### How to reproduce

```bash
# Pipeline
gh run list --branch main --workflow "Build and test" --limit 50 --json databaseId,conclusion,createdAt,startedAt,updatedAt,headSha,displayTitle,event
gh run list --branch main --workflow "Release builds" --limit 50 --json databaseId,conclusion,createdAt,event
gh run view <id> --json jobs --jq '.jobs[]|{name,conclusion,id:.databaseId,steps:[.steps[]|select(.conclusion=="failure")|.name]}'
gh api repos/{owner}/{repo}/actions/jobs/<job id>/logs | grep -E "Failed |Error Message|Timed out"

# Build warnings, on a clean copy so no other build locks the obj folders (about 1 minute with a warm server)
git archive HEAD | tar -x -C <scratch>/src && cd <scratch>/src
dotnet build iTerminal.slnx -c Release --no-incremental -nologo -v:n > <scratch>/build.log 2>&1
grep -E ": warning " <scratch>/build.log | sed -E 's/^ *[0-9]+>//; s/^\s+//' | sort -u > <scratch>/warnings-unique.txt

# Coverage, one test project at a time (Linux), then read the package with the assembly's name in coverage.cobertura.xml
dotnet test <project>/<project>.csproj -c Release --no-build --collect:"XPlat Code Coverage" \
  --settings coverage.runsettings --results-directory <scratch>/cov/<project>

# Quality counts
grep -rE '^\s*\[Ignore' --include=*.cs <test project>
grep -r 'Thread\.Sleep' --include=*.cs <test project>
grep -rn 'pragma warning disable' --include=*.cs .
```
