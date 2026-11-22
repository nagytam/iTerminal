# Feature proposals

## How to use this document

Each proposal is one slice that can be delivered as one `/incremental-improvement` pull request. Statuses are Proposed, Planned, Built and Rejected. The planner appends one round per run and does not rewrite older round text; only a proposal's status field and the index may change. Sources are the documents in `Documentation/research/`, `Documentation/QA/qa-overview.md`, and the source tree, which is the authority when they disagree.

## Proposal index

| Id | Title | Kind | Size | Status |
| --- | --- | --- | --- | --- |
| P1 | Give the voice recorder its own chord (Ctrl+Shift+X collision) | Revision | S | Proposed |
| P2 | Remove the 30 s race in `BlockingCompletionModelTest` | Revision | S | Proposed |
| P3 | Parallel test projects and an injectable `aplay` timeout | Revision | S | Proposed |
| P4 | WinGet "already installed" for git and yt-dlp, shared install plan | Revision | S | Proposed |
| P5 | Idle render, key and repaint polling | Revision | M | Proposed |
| P6 | Tell the user when a recorder dies mid-recording | Revision | S | Proposed |
| P7 | Git commit signature status | New | S | Proposed |
| P8 | HTTP security-header grade | New | S | Proposed |
| P9 | Shortcut (.lnk) inspector in F3 | New | M | Proposed |
| P10 | Authentication-Results reader for `.eml` | New | S | Proposed |
| P11 | Redaction helper before text goes to a model | New | S | Proposed |
| P12 | Token counts in the AI bar | New | S | Proposed |
| P13 | Reranker for the meaning search | New | M | Proposed |
| P14 | `NO_COLOR` and colour-depth fallback | New | M | Proposed |
| P15 | Message and wait dialog primitive | New | S | Proposed |

## Round 1: 2026-11-22 everything

Scope: all research overviews (console, security, AI, architecture, design review, profiling, promotion), the QA overview, and the source. Branch searched: `docs/compare-console-survey-6`, commit `b9b81269`. "Not found" lists case-insensitive `*.cs` greps over `iConsole/`, `iConsole.Formats/` and `iTerminal.Core/` that returned no file. The research documents were read for their rankings and item numbers; each proposal below was re-checked against the source where a search term is given. Items marked **unverified** rest on a research or QA document and were not re-read in the source.

### P1. Give the voice recorder its own chord (Ctrl+Shift+X collision)  (Revision)

- **Why**: two dialogs claim Ctrl+Shift+X, so one is unreachable or the wrong one opens, and the footer and palette show a key that does something else.
- **Evidence**: QA-18 (`Documentation/QA/qa-overview.md`). Confirmed in source: `iTerminal.Core/Commands/VoiceRecorderDialogCommand.cs:14` (`Key`) and `iTerminal.Core/Commands/PasswordManagerDialogCommand.cs:19` (`AlternateKey`) are both `new Key(ConsoleKey.X, true, false, true)`. Design review finding 11 was the same bug class.
- **Where**: `iTerminal.Core` commands; `iTerminal.Core.Test/Commands/HotKeyCollisionTest.cs`; `Documentation/architecture/dialogs.md` (needs a sentence fix; not edited here).
- **Slice**: pick a free function-key or Ctrl+Shift chord (no Alt+letter, not Ctrl+M) for the recorder; add a generic test that no two commands share a `Key` or `AlternateKey`; a `DialogsTest` scenario pressing the chord. Leaves the password manager alone.
- **Risks**: keys; localisation of any footer text; which command won today was not traced.
- **Size**: S.

### P2. Remove the 30 s race in `BlockingCompletionModelTest`  (Revision)

- **Why**: the test sometimes waits a full 30 s gate, 66 % of the `iConsole.Test` span, and it is flaky in CI.
- **Evidence**: profiling finding 14; `iConsole.Test/Controls/BlockingCompletionModelTest.cs:183` (`OpeningGate.Wait(TimeSpan.FromSeconds(30))`, found). Related QA-3, QA-5 on flaky tests.
- **Where**: that test file only.
- **Slice**: block the call by identity, or use a two-call `CountdownEvent`; keep 30 s as a safety timeout. No production change.
- **Risks**: the cause is partly unverified (order of two threads inferred).
- **Size**: S.

### P3. Parallel test projects and an injectable `aplay` timeout  (Revision)

- **Why**: Video, Audio and Speech test projects run serially; one test waits out a real 5 s timeout.
- **Evidence**: profiling findings 15 and 16. Source: no `Parallelize` in `iConsole.Media.Video.Test`, `iConsole.Media.Audio.Test` or `iConsole.Media.Speech.Test` (grep found only two `[DoNotParallelize]` in Audio); `iConsole.Media.Audio/Sinks/AplayAudioSink.cs:286` has `WaitForExit(5000)`.
- **Where**: the three `AssemblyInfo.cs` files (or equivalent), `AplayAudioOutputBackend` constructor, `AplayAudioOutputBackendTest.cs`.
- **Slice**: Video project first with `Parallelize(Workers = 0, Scope = MethodLevel)`; then the optional timeout parameter (default 5 s) used with 200 ms in the test. Audio and Speech parallelism only if the tests use their own temp files.
- **Risks**: test independence unverified; ffmpeg process count on small CI runners.
- **Size**: S.

### P4. WinGet "already installed" for git and yt-dlp, shared install plan  (Revision)

- **Why**: on Windows a git or yt-dlp install is reported failed when WinGet says the package is already installed, while ffmpeg and rar treat it as success.
- **Evidence**: architecture finding 42. Source: `GitRuntimeDependency.cs:116` throws on any non-zero exit code (read above), and its `GitInstallPlan` duplicates `SystemPackageInstallPlan.cs`.
- **Where**: `iTerminal.Core/Services/RuntimeDependencies/` (`GitRuntimeDependency`, `YtDlpRuntimeDependency`, `SystemPackageInstallPlan`); the localized keys for yt-dlp in all six `.resx` files.
- **Slice**: delete `GitInstallPlan`, use `SystemPackageInstallPlan.For`, accept WinGet's two success exit codes in git and yt-dlp, with unit tests on the exit codes. Does not merge the four classes into one base (a later slice).
- **Risks**: sandbox dependency replacement already exists; the failure is unverified at run time.
- **Size**: S.

### P5. Idle render, key and repaint polling  (Revision)

- **Why**: the idle app uses 27 to 31 % of a core, rising to 43 to 46 % with a 10 001-entry folder open, which matters on laptops and over SSH (7 kB/s of output at idle).
- **Evidence**: profiling P1 finding 1 and P2 status ("Open"), finding 17. Source: `Thread.Sleep(1)` at `iConsole/Jobs/ControlRendererJob.cs:36`, `ConsoleBufferRendererJob.cs:155`, `KeyboardEventPublisherJob.cs:45`.
- **Where**: `iConsole/Jobs/*`, `iConsole.Inputs.Posix` input job.
- **Slice**: first measure which loop costs what; then replace the 1 ms sleeps with an event or a longer idle back-off, and stop the once-a-second full repaint when nothing changed. Folder-size gap (finding 17) stays out until re-measured.
- **Risks**: rendering (layers are never cleared; a missed repaint shows stale text), the 200 ms key budget, `ConsoleOwnership` loops, and the end-to-end suite timings. Needs `ITERMINAL_RENDER_DUMPS=1` checks.
- **Size**: M.

### P6. Tell the user when a recorder dies mid-recording  (Revision)

- **Why**: a recorder process that exits while recording loses the take without a message.
- **Evidence**: QA-19 (**unverified** in source; the QA document names the recorder code). Related to P1 in the same dialog.
- **Where**: `iConsole.Media.Speech` recorders and the voice recorder dialog; localisation in six `.resx` files.
- **Slice**: detect the early exit, notify through `INotificationService`, keep the partial audio. Not the voice-activity work of AI item 27.
- **Risks**: threading (notify from the worker, no tree mutation); sandbox replacement for the recorder already needed for tests.
- **Size**: S.

### P7. Git commit signature status  (New)

- **Why**: the Git client shows commits but not whether they are signed and verified; a defensive check costs one format field.
- **Evidence**: security survey 2 item 10 (ranked first in survey 2). Source: `iTerminal.Core/Services/Git/GitService.cs` exists and uses `--format=%gs` for stashes only.
- **Where**: `GitService` log parsing, the commit model, the Git client dialog.
- **Slice**: add `%G?` and `%GS` to the log call and show one status column; no key trust management, no network.
- **Risks**: git output decoding (architecture finding 43, code page); a bad-signature value must render, not throw; six `.resx` keys.
- **Size**: S.
- **Not found**: `show-signature`, `gpgsig`, `%G?` (one `GpgSig` search: no file).

### P8. HTTP security-header grade  (New)

- **Why**: the web fetch and network tools already read response headers but do not tell the user what is missing.
- **Evidence**: security survey 3 item 20 (ranked first). Source: `iTerminal.Core/Services/Web/WebFetcher.cs` exists.
- **Where**: `iTerminal.Core/Services/NetworkTools` or `Web`, a pure function plus a result row in the existing tool dialog.
- **Slice**: grade `Strict-Transport-Security`, `Content-Security-Policy`, `X-Content-Type-Options`, `Referrer-Policy` and frame options from headers already fetched. No new request, no scanning.
- **Risks**: network call stays on a background task; sandbox network replacement exists.
- **Size**: S.
- **Not found**: `Strict-Transport-Security`, `Content-Security-Policy`.

### P9. Shortcut (.lnk) inspector in F3  (New)

- **Why**: a `.lnk` file is a common malware carrier; the user can see its target and arguments without running it.
- **Evidence**: security survey 1 item 5 (ranked first). Not in source.
- **Where**: `iConsole.Formats` (pure parser behind `IFileToTextConverterService` or the F3 viewer registry).
- **Slice**: read target, arguments, working directory, icon path and timestamps from the Shell Link header; read-only text view. Leaves out resolving the target.
- **Risks**: parser on untrusted bytes must bound every length; fuzz-style tests.
- **Size**: M.
- **Not found**: `ShellLink`, `LnkReader`, `LnkParser`, `ShortcutInspector`.

### P10. Authentication-Results reader for `.eml`  (New)

- **Why**: an email's SPF, DKIM and DMARC verdicts are in a header the mail viewer shows raw or not at all.
- **Evidence**: security survey 2 item 13 (second in its ranking). Not in source.
- **Where**: the existing `.eml` reader in `iConsole.Formats` and the Mail dialog.
- **Slice**: a pure parser of the header and one summary line; no DNS lookup.
- **Risks**: spoofed headers should be labelled "claimed by the receiving server".
- **Size**: S.
- **Not found**: `Authentication-Results`.

### P11. Redaction helper before text goes to a model  (New)

- **Why**: terminal output and logs sent to an AI provider can carry tokens and keys; redaction is a precondition of every other AI feature that sends output.
- **Evidence**: AI survey 6 item 30. Not in source.
- **Where**: `iTerminal.Core` AI services (one class with a pattern list).
- **Slice**: a function over a string that masks key-like and password-like patterns, wired into one caller (the AI bar's terminal-output attachment); a count shown to the user. Leaves the other callers.
- **Risks**: false negatives; the user must be told the filter is best effort.
- **Size**: S.
- **Not found**: `Redact`.

### P12. Token counts in the AI bar  (New)

- **Why**: the bulk approvals and long histories give the user no size to judge before sending.
- **Evidence**: AI survey 2 item 13. Not in source.
- **Where**: the AI bar and its chat service.
- **Slice**: show an approximate count (from the provider's usage field where returned, else a length estimate); no limit enforcement.
- **Risks**: an estimate labelled as such; localisation.
- **Size**: S.
- **Not found**: `TokenCount`, `CountTokens`, `EstimateTokens`.

### P13. Reranker for the meaning search  (New)

- **Why**: the document index's meaning search returns first-pass hits only.
- **Evidence**: AI survey 7 item 32 (ranked first of survey 7). Not in source.
- **Where**: `iTerminal.Core/Services/Documents`; a runtime dependency per `runtime-dependencies.md`.
- **Slice**: a local reranker step over the top results, behind a setting, off by default. Leaves chunking (item 31).
- **Risks**: download and licence; the 200 ms budget (run on a background task and land through the stale-result check).
- **Size**: M.
- **Not found**: `Rerank`.

### P14. `NO_COLOR` and colour-depth fallback  (New)

- **Why**: users on 16-colour or monochrome terminals and users who set `NO_COLOR` get no adaptation.
- **Evidence**: console survey items 2, 3 and 34 (status re-check 2026-11-22: no `NO_COLOR`, `38;5`, `38;2` found).
- **Where**: `iConsole` drivers and `AnsiRenderer`; the `Color` mapping.
- **Slice**: honour `NO_COLOR` and `COLORTERM` detection with a fallback table; no new palette work and no true-colour theme.
- **Risks**: rendering of every control; `ScreenBuffer` tests; the end-to-end virtual terminal needs a case.
- **Size**: M.
- **Not found**: `NO_COLOR`, `38;5`, `38;2`.

### P15. Message and wait dialog primitive  (New)

- **Why**: message and wait popups are rebuilt per dialog; `Spinner` and `ProgressGroup` exist with no dialog around them.
- **Evidence**: console survey item 94 (re-confirmed still missing, survey 6).
- **Where**: `iConsole` dialog primitive; localised captions.
- **Slice**: one `MessageDialog` (OK) and one `WaitDialog` (Esc cancels) with `ScreenBuffer` tests; convert one existing ad-hoc caller.
- **Risks**: modal removal from the tree, Esc layering; `InstantiateAllTypesTest`.
- **Size**: S.

### Suggested order

1. P1: a real bug on a default chord, with a test that guards every future chord.
2. P4: a likely failure on Windows, small and independent.
3. P2, then P3: remove the CI flake first, then CI time; test-only.
4. P6: data loss without a message, in the same area as P1.
5. P7, P8: defensive items with no new dependency.
6. P11, then P12: redaction before anything else sends terminal output.
7. P5: biggest user-visible cost, but needs measurement first and touches rendering.
8. P10, P9, P15, P14, P13: independent, ordered by size and by what they depend on (P13 needs a runtime decision).

### Skipped

- BLAKE3 (security item 1), ML-DSA, Argon2id: need a package or algorithm decision; not a small slice yet.
- C2PA, RFC 3161, Sigma, TUF, Folder vault, Passkey, Landlock: network, native bridge or a trust decision; larger than one slice.
- Cursor save and restore (console item 157): only useful with inline output (item 28), which is not planned.
- AI items on A2A, Microsoft Agent Framework, Docling, Phonon 2: network-facing, Python runtime or not recommended by the survey.
- Architecture duplication findings 44 to 46 (hand-copied dialogs, decoding drift): no user-visible change yet; propose after P4 and P5.
- Promotion material: not a product feature.
- CycloneDX reader (security item 4): the source already has SBOM types (`SbomResult`, `SolutionViewerDialog`), so the research status needs a re-check before it is proposed.

#### Status after the pull of 2026-11-22

- Security item 4 (CycloneDX reader) is not confirmed missing: CycloneDX terms appear in `iTerminal.Core/Dialogs/Dotnet/SolutionViewerDialog.cs`; the security overview should be checked for a "partly" status.
- Nothing in the older rounds exists; this is round 1.
