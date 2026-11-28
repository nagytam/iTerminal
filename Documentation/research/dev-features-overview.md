# Development IDE features compared with the Development studio

This document compares Visual Studio, Visual Studio Code and Eclipse with the iTerminal Development studio (`iTerminal.DevelopmentStudio`, Tools > Development studio, F3 on a `.sln` or `.slnx`). The IDEs are the reference; the Development studio is the comparison target. It proposes; it does not decide what gets built. Item numbers are unique across the whole document and are never reused.

## Already at or ahead of the IDEs

- **A Visual Studio layout on a console**: docked editor, Solution Explorer, output and Error List with a layout mode (`StudioPanelLayout`, F8), where the IDEs need a mouse for docking.
- **One tool for four languages**: .NET, Python, PowerShell and Java solutions in one explorer, with build, run, clean and test (`DevelopmentToolchains`).
- **Debugging over the Debug Adapter Protocol** for .NET (netcoredbg) and Python (debugpy) (`DebugSession`, `DapClient`), the same protocol VS Code uses.
- **Coverage in the source gutter, a Python profiler and a CycloneDX SBOM** from the same dialog (`CoberturaParser`, `PythonProfileParser`).

## Missing features

| Feature | Seen in | Status in iTerminal | Fits |
| --- | --- | --- | --- |
| Conditional breakpoints (items 1, 2, 3, 7) | VS, VS Code, Eclipse | Missing: `BreakpointStore` keeps lines only | High: one small data change plus the DAP fields |
| Test rerun, filter and grouping (items 15 to 21) | VS, VS Code, Eclipse | Partly: the Test Explorer lists test items, not tests | High |
| User tasks and launch configurations (items 11, 12) | VS Code | Missing | Medium |
| Attach to a running process (item 8) | VS Code | Missing | Medium |
| Symbol navigation (item 23) | VS, VS Code | Missing, needs a language server | Low: not applicable without one |

## Improvement opportunities

| Area | Existing type or command | What the IDEs do | Expected benefit |
| --- | --- | --- | --- |
| Error List | `StudioPanelKind.Errors`, Enter opens the file | VS Code steps through problems with a key (item 24) | Fix errors without leaving the keyboard rhythm |
| Breakpoints | `BreakpointStore` | Proposal (item 25, unverified) | Breakpoints survive a restart |
| Source view | `DevelopmentStudioDialog` editor panel | Proposal (item 26, unverified) | Edit the line that failed without leaving the studio |

## Suggested order

1. Items 1, 2, 3: conditions, hit counts and logpoints on breakpoints.
2. Items 15, 17, 16: per-test discovery, rerun failed, filter.
3. Item 14: repeat the last run.
4. Item 24: next and previous error.

### Surveys

#### Survey 1: Visual Studio, Visual Studio Code and Eclipse: debugging, tasks and launch, testing, navigation

Basis: the official documentation, not run-time behaviour. Visual Studio (Windows, 2022 and the current release) from learn.microsoft.com; Visual Studio Code from code.visualstudio.com (pages dated 2026-11-25, no product version shown); Eclipse JDT from help.eclipse.org (pages carry no version or date). Surveyed on 2026-11-26 at branch `docs/compare-dev-ide-features`, commit `fdea1d4c` (cut from `main`). "Not found" means a case-insensitive grep of `*.cs` in `iTerminal.DevelopmentStudio/`, `iTerminal.Core/` and `iConsole/` on that commit found nothing for the listed terms; a "partly present" item says what exists.

Sources (every IDE named under "Seen in" appears here):

| IDE | Feature | Area | Source | Version or date | Description |
| --- | --- | --- | --- | --- | --- |
| VS Code | Conditional, hit count, logpoint, data, function, inline and triggered breakpoints | Debugging | https://code.visualstudio.com/docs/debugtest/debugging | 2026-11-25 | Breakpoint kinds the debugger extension may support. |
| VS Code | Call stack frame select, Set Value, Debug Console REPL | Debugging | https://code.visualstudio.com/docs/debugtest/debugging | 2026-11-25 | Inspect any frame, edit a variable, evaluate expressions in the frame. |
| VS Code | Launch, attach, preLaunchTask, postDebugTask, compounds | Launch | https://code.visualstudio.com/docs/debugtest/debugging-configuration | 2026-11-25 | A launch configuration runs a task first, can attach, and can start several sessions together. |
| VS Code | tasks.json, default build task, dependsOn, problem matchers, Rerun Last Task | Build and tasks | https://code.visualstudio.com/docs/debugtest/tasks | 2026-11-25 | User-defined commands with dependencies and output parsing. |
| VS Code | Test Explorer: filter by status, run and debug from the gutter, coverage | Testing | https://code.visualstudio.com/docs/debugtest/testing | 2026-11-25 | A test tree with filters and coverage views. |
| VS Code | Go to Symbol, Open Symbol by Name, Peek, Rename Symbol, Problems stepping | Navigation | https://code.visualstudio.com/docs/editing/editingevolved | 2026-11-25 | Symbol-level navigation from language support. |
| VS | Test Explorer: group by state and duration, search filter, playlists, run after build, repeat last run, run in context | Testing | https://learn.microsoft.com/en-us/visualstudio/test/run-unit-tests-with-test-explorer | ms.date 2026-03-06 | Test list organised and run by group, filter or cursor position. |
| VS | Tracepoints, hit-count and "only enable when another breakpoint is hit" | Debugging | https://learn.microsoft.com/en-us/visualstudio/debugger/using-tracepoints | ms.date 2025-09-03 | A breakpoint that logs a message and continues. |
| VS | Go To All, Go to line (Ctrl+G), type, member, symbol filters | Navigation | https://learn.microsoft.com/en-us/visualstudio/ide/go-to | ms.date 2025-04-03 | One search box for lines, files, types and members. |
| Eclipse | Conditional breakpoint: suspend on true or on value change | Debugging | https://help.eclipse.org/latest/topic/org.eclipse.jdt.doc.user/tasks/task-manage_conditional_breakpoint.htm | no date shown | Java condition on a line breakpoint, which may have side effects. |
| Eclipse | JUnit view: rerun failures first, failures only, next and previous failed test, run history | Testing | https://help.eclipse.org/latest/topic/org.eclipse.jdt.doc.user/reference/views/ref-view-junit.htm | no date shown | A view of one run with failure navigation and history. |
| Eclipse | Hit count breakpoints (line, exception, watchpoint, method) | Debugging | https://help.eclipse.org/latest/topic/org.eclipse.jdt.doc.user/reference/breakpoints/ref-hitcount_option.htm | not fetched | Known from a search snippet only: unverified. |

Features considered and skipped: data breakpoints (VS Code; adapter-dependent, and netcoredbg and debugpy support is not documented in the sources read), inline breakpoints (columns on a line; low value in a line-based source view), Test Explorer audio cues (VS; Windows sound scheme, not applicable), Test Explorer columns and drag reordering (VS; mouse driven), Go to Implementation, Peek and Call Hierarchy (need a language server, see item 23), Debug with Copilot suggestions (VS; out of scope here, see `Documentation/research/ai-features-overview.md`).

**Debugging**

1. **Conditional breakpoints.** A breakpoint stops only when an expression is true. Seen in VS, VS Code, Eclipse. Not found: `hitCondition`, `condition`, `SourceBreakpoint`, `BreakpointCondition` (`BreakpointStore` stores line numbers only, and `DebugSession.SetBreakpointsAsync` sends `{ line }` only).
2. **Hit-count breakpoints.** Stop on the Nth, or every Nth, hit. Seen in VS, VS Code; Eclipse is unverified (search snippet). Not found: `hitCondition`, `HitCount`, `hit count`.
3. **Logpoints (tracepoints).** Print a message with expressions and continue, without editing the code. Seen in VS (tracepoints), VS Code (logpoints); Eclipse reaches it with a condition that prints and returns false. Not found: `logMessage`, `logpoint`, `tracepoint`.
4. **Exception breakpoints.** Stop when an exception is thrown. **Unverified**: no page for it was read for any IDE (the VS Code debugging page says it does not cover it). Not found: `setExceptionBreakpoints`, `ExceptionBreakpoint`, `exceptionOptions`.
5. **Function breakpoints.** Stop on entering a named function without opening its source. Seen in VS Code. Not found: `setFunctionBreakpoints`, `FunctionBreakpoint`.
6. **Edit a variable while stopped.** Seen in VS Code (Set Value). Not found: `setVariable`, `Set Value`, `SetVariableAsync` (only `EvaluateAsync` for watches exists).
7. **Triggered breakpoints.** A breakpoint armed only after another one is hit. Seen in VS Code, VS ("only enable when the following breakpoint is hit"). Not found: `triggered`, `only enable`, `EnableWhen`.
8. **Attach to a running process.** Seen in VS Code (`request: attach`). Not found: `"attach"`, `AttachAsync`, `attach to process`; `DebugSession` only launches.
9. **Choose a stack frame.** Partly present: `DebugSession.StackTraceAsync` and the Debug view list the stack, but locals and watches are read for the top frame only (`frameId` comes from `frames.FirstOrDefault()`). Seen in VS Code. Missing: selecting another frame to see its source, locals and watch values. Not found: `SelectFrame`, `StackFrameSelected`.
10. **Debug console.** Partly present: `DebugSession.EvaluateAsync` evaluates watch expressions. Seen in VS Code (REPL in the selected frame). Missing: a free-form expression line.

**Build and tasks**

11. **User-defined tasks.** A named command with a default build task and `dependsOn`, plus output parsing. Seen in VS Code. Partly present: `BuildDiagnosticParser` parses the built-in commands' output. Not found: `tasks.json`, `dependsOn`, `problemMatcher`, `DefaultBuildTask`.
12. **Pre-launch and post-debug tasks, and compound launch configurations.** Seen in VS Code. Not found: `preLaunchTask`, `postDebugTask`, `launch.json`, `compounds`, `launchSettings`. .NET debugging already builds first, which is the one fixed case.
13. **Run tests after every build.** Seen in VS (Test Explorer setting). Not found: `RunTestsAfterBuild`, `after every build`, `continuous test`, `FileSystemWatcher` in the studio.
14. **Repeat the last run.** Seen in VS (`TestExplorer.RepeatLastRun`, Ctrl+R, L), VS Code (Rerun Last Task). Not found: `RepeatLastRun`, `RerunLast`, `LastCommand`. `b`, `c`, `t`, `r` must be pressed again on the right item.

**Testing**

15. **Per-test discovery and results.** Partly present: the Test Explorer lists test items (projects and files, `DevelopmentProject.IsTest`) and `TestOutputParser` sums pass, fail and skip counts. Seen in VS, VS Code, Eclipse (a tree of individual tests with a result each). Missing: individual test rows, their duration, message and stack trace. Not found: `TestCase`, `TestResult`, `DiscoverTests`, `trx`.
16. **Filter and search the test list.** By name or by state. Seen in VS, VS Code. Not found: `TestFilter`, `FilterTests`, `filter` in the studio.
17. **Rerun failed tests.** Seen in Eclipse ("Rerun Test - Failures First"), VS (run the Failed Tests group). Not found: `RerunFailed`, `FailedTests`, `run failed`. Depends on item 15.
18. **Group tests by state or duration.** Seen in VS. Not found: `GroupBy`, `Group By`, `TestState`. Depends on item 15.
19. **Test playlists.** A saved list of tests run as a group. Seen in VS. Not found in the studio: `Playlist` (all matches in `iTerminal.Core` are the music queue). Depends on item 15.
20. **Next and previous failed test.** Seen in Eclipse ("Next Failed Test"). Not found: `NextFailed`, `PreviousFailed`. Depends on item 15.
21. **Run the test under the source cursor.** Seen in VS (the cursor in a method, class or namespace picks the scope), VS Code (the gutter control). Not found: `RunAllTestsInContext`, `RunTestAtCursor`, `TestInContext`. Depends on item 15.

**Navigation**

22. **Go to line in the editor panel.** Partly present: `GoToLineDialog` (`iConsole/Dialogs/GoToLineDialog.cs`) serves the text viewers and editor; the studio's source panel does not use it. Seen in VS (Ctrl+G, `:` prefix). Not found in the studio: `GoToLine`, `GoToLineDialog`, `Go to line`.
23. **Symbol navigation (Go To All, Open Symbol, Find All References, Rename).** Seen in VS, VS Code. **Not applicable until a language server client exists**: the studio ships none. Not found: `LanguageServer`, `omnisharp`, `GoToSymbol`, `WorkspaceSymbol`, `FindAllReferences`, `RenameSymbol`. A cheaper partial step is a name search over the solution's files (`FindFilesDialog` already supports a Containing text).

**Improvement opportunities**

24. **Step through the Error List.** Existing: the Error List panel (`StudioPanelKind.Errors`; Enter opens the file at the line). VS Code steps to the next or previous problem with a key (Alt+F8) without moving focus to the list. Benefit: a fix-build loop with two keys. Not a defect: the current behaviour works; this is a proposal. Filtering by severity is not found (`filter`, `Severity` apply to the status line only).
25. **Keep breakpoints between sessions.** Existing: `BreakpointStore`, an in-memory singleton that survives closing the dialog but not the app. **Proposal, unverified**: the IDEs' behaviour was not read in this survey. Benefit: no re-setting of breakpoints after a restart. Search terms for the store's persistence: `ISettingsService`, `File.`, `Persist` found nothing in `BreakpointStore.cs`.
26. **Open the cursor line in the text editor.** Existing: the editor panel is a read-only list of source lines with a gutter; `TextFileEditorDialog` (F4) edits a file. **Proposal, unverified**: the IDEs edit in place, and no page on editing was read in this survey. Benefit: fix the failing line, then build again. Not found: `TextFileEditorDialog` in the studio project.

**Suggested order** (new items only, by value to iTerminal)

1. Items 1, 2, 3 and 7 together: extend `BreakpointStore` with a condition, a hit count and a log message, and send them in `SetBreakpointsAsync`.
2. Item 15, then 17, 16, 20: per-test results are the base for the other test items.
3. Item 14: repeat the last run.
4. Items 24 and 22: next and previous error; go to line.
5. Items 9, 10, 6: inspect another frame, a debug console, edit a value.
6. Items 11, 12, 13: user tasks, pre-launch tasks, run tests after build.
7. Items 8, 5, 4, 18, 19, 21, 25, 26.
8. Item 23 last: it needs a language server client.

**Candidates already covered**

- Breakpoints, step over, into and out, watches, locals: present (F9, F10, F11, Shift+F11, `w`, `DebugSession`).
- Code coverage with a source gutter: present (`o`, `CoberturaParser`, F7 Coverage view); VS Code's coverage overlay and VS's coverage window are matched for .NET and Python.
- Running a test, a project or a script, and cancelling: present (`t`, `r`, `x`).
- Text search inside files across a folder: present in the file manager (`FindFilesDialog`, F7), outside the studio.
- Solution, test and file trees with a layout of dockable panels: present.

#### Status after the pull of 2026-11-28

Re-checked against branch `main`, commit `4b2323ea`, by a case-insensitive grep of `*.cs` in `iTerminal.DevelopmentStudio/`, `iTerminal.Core/` and `iConsole/`. Survey 1 is not edited; these lines supersede its statements.

- Item 25 (keep breakpoints between sessions): **built**. `BreakpointStore` now takes an `IDbContextFactory<iTerminalDbContext>` and loads its rows in the background (`Loaded`, `PendingWrites`); the survey 1 text calling it an in-memory singleton is out of date.
- Item 26 (edit the line in the studio): **built**. The source panel is `StudioSourceEditor`, a `TextEditorControl` subclass; `SaveSource` (Ctrl+S) writes the file.
- Item 23 (symbol navigation): **partly changed**. The "studio ships no language server" premise no longer holds: `LspClient`, `CSharpLanguageServer` and `LanguageServerLauncher` exist, but only `textDocument/didOpen`, `didChange` and `publishDiagnostics` are used. Still not found: `definition`, `references`, `documentSymbol`, `rename`, `hover`, `completion`, `codeAction`. The item stays missing, and its "not applicable" label should read "needs requests on the existing client".
- Item 22 (go to line in the studio): unchanged. The editor control's hint list names F5 for Go to line (`TextEditorControl`), but F5 is the studio's debug key and `IsEditorKey` excludes it, so the studio does not reach it. Not found in the studio: `GoToLine`.
- Item 27 (build configurations): **built** on branch `feature/devstudio-build-configuration`. `D` switches Debug and Release; every .NET command passes `-c`; Release refuses to debug (`DotNetToolchain.Configuration`, `Model/BuildConfiguration.cs`). Still missing: a custom configuration name, and a Configuration Manager that maps configurations to projects.
- Items 1 to 21, 24: unchanged; the terms listed in survey 1 still find nothing in `iTerminal.DevelopmentStudio/`.

#### Survey 2: Visual Studio, Visual Studio Code and Eclipse: configurations and startup, editor actions, tasks, version control

Basis: the official documentation, not run-time behaviour. Visual Studio (Windows, 2022 and the current release) from learn.microsoft.com; Visual Studio Code from code.visualstudio.com (pages dated 2026-11-25, no product version shown); Eclipse JDT from help.eclipse.org (pages carry no version or date). Surveyed on 2026-11-28 at branch `main`, commit `4b2323ea`, edited in the working tree because the user asked for no branch switch. "Not found" means a case-insensitive grep of `*.cs` in `iTerminal.DevelopmentStudio/`, `iTerminal.Core/` and `iConsole/` on that commit found nothing for the listed terms; a "partly present" item says what exists.

Sources (every IDE named under "Seen in" appears here; rows marked unverified come from a search result only):

| IDE | Feature | Area | Source | Version or date | Description |
| --- | --- | --- | --- | --- | --- |
| VS | Solution and project configurations, Configuration Manager, Batch Build | Solution and projects | https://learn.microsoft.com/en-us/visualstudio/ide/understanding-build-configurations | ms.date 2025-08-07 | Named sets of build settings, one active, with a project mapping per solution configuration; Batch Build builds several at once. |
| VS | Multiple startup projects and multi-project launch profiles (`.slnLaunch`) | Solution and projects | https://learn.microsoft.com/visualstudio/ide/how-to-set-multiple-startup-projects | ms.date 2026-02-25 | Per project Start, Start without debugging or None, ordered, saved as a shareable profile. |
| VS | Task List: TODO, HACK, UNDONE and custom comment tokens, task shortcuts, next and previous task | Diagnostics | https://learn.microsoft.com/en-us/visualstudio/ide/using-the-task-list | ms.date 2025-11-17 | A list of tagged code comments that opens the file at the line. |
| VS | Format Document, Code Cleanup profiles, cleanup on save, EditorConfig precedence, Quick Actions | Editor | https://learn.microsoft.com/en-us/visualstudio/ide/code-styles-and-code-cleanup | ms.date 2026-01-06 | Style rules from an EditorConfig file win over user options; Cleanup applies chosen fixes, optionally on save. |
| VS Code | Source Control view, diff editor, blame, graph, stage selected lines, merge editor, stash | Version control | https://code.visualstudio.com/docs/sourcecontrol/overview | 2026-11-25 | Git work inside the editor, down to single lines in a diff. |
| VS Code | Code Actions, Quick Fix, Refactor Preview, `editor.codeActionsOnSave` | Editor | https://code.visualstudio.com/docs/editing/refactoring | 2026-11-25 | Fixes and refactorings at the cursor, previewed by change, and source actions on save. |
| Eclipse | Source menu: Format, Organize Imports, Clean Up, Surround With, Toggle Comment, Generate and Override methods | Editor | https://help.eclipse.org/latest/topic/org.eclipse.jdt.doc.user/reference/ref-menu-source.htm | no date shown | Whole-file and selection edit commands with a key each. |
| Eclipse | Quick Fix (Ctrl+1), Java templates | Editor | https://help.eclipse.org/latest/topic/org.eclipse.jdt.doc.user/reference/ref-java-editor-quickfix.htm | not fetched | Known from a search snippet only: unverified. |
| VS Code | Multi-cursor editing, snippets, Format Document | Editor | https://code.visualstudio.com/docs/editing/codebasics | not fetched | Known from a search snippet only: unverified. |

Features considered and skipped: Docker Compose launch profiles (VS; needs a container tool the studio does not drive), the Toolbox filtering by configuration (VS; GUI designer), Project Rank column in the Task List (VS; only meaningful with a project dependency graph, which the explorer does not build), the Source Control Graph and worktrees (VS Code; the Git client dialog in `iTerminal.Core` is the place for them, not the studio), the 3-way merge editor (VS Code; a file manager feature, see `iTerminal.Core`).

**Solution and projects**

27. **Build configurations (Debug, Release, custom).** A named configuration chosen once and used by build, run, test and debug. Seen in VS. Not found: `--configuration`, `BuildConfiguration`, `Configuration Manager`, `-c Release` (the .NET commands in `DevelopmentToolchains` pass no configuration, so every build is the SDK's default).
28. **Multiple startup projects and saved launch profiles.** Start several projects together, each with or without the debugger, in an order, saved per solution. Seen in VS. Not found: `slnLaunch`, `StartupProject`, `launchSettings`, `Multiple startup`. Partly present: `MainProgramStore` holds one Main program. Related to item 12.
29. **Batch build of several configurations.** Seen in VS. Not found: `BatchBuild`, `Batch build`. Depends on item 27.

**Editor**

30. **Format document and clean up code.** One command that applies white space and style fixes, and Organize Imports that removes unused usings or imports. Seen in VS (Format Document, Code Cleanup), Eclipse (Format, Organize Imports, Clean Up); VS Code is unverified for Format Document (search snippet) and verified for the organize-imports source action. Partly present: `TextEditorControl` has line tools (trim trailing spaces, join, remove blank lines). Not found: `dotnet format`, `FormatDocument`, `CodeCleanup`, `OrganizeImports`. A cheap start for .NET is the `dotnet format` command through `DotNetToolchain`.
31. **Run format or fixes on save.** Seen in VS (Run Code Cleanup profile on save), VS Code (`editor.codeActionsOnSave`). Not found: `codeActionsOnSave`, `FormatOnSave`, `OnSave` as a setting (`SaveSource` only writes the file). Depends on item 30.
32. **Code actions and quick fixes at the cursor.** A key lists the fixes for the problem under the cursor, with a preview for refactorings. Seen in VS (Quick Actions), VS Code (Quick Fix, Refactor Preview); Eclipse is unverified (Ctrl+1, search snippet). Not found: `codeAction`, `QuickFix`, `LightBulb`, `Refactor`. Needs a `textDocument/codeAction` request on the existing `LspClient` (see the status note on item 23).
33. **EditorConfig applied to the editor.** Indent style, tab width, line ending and charset taken from the file's `.editorconfig`, which wins over the user's options. Seen in VS. Not found in the studio or `iConsole/Controls/Editors`: `editorconfig` (the only matches are the file-type lists in `SyntaxHighlighter` and `EditTextFileDialogCommand`). Build-time enforcement is already present: `DotNetToolchain.Analyze` passes `EnforceCodeStyleInBuild`.
34. **Snippets, templates and Surround With.** Insert a named template, or wrap the selection in one. Seen in Eclipse (Surround With, verified; templates unverified), VS Code (snippets, unverified). Not found: `snippet`, `Surround`, `Template` in the editor.
35. **Multiple cursors.** **Unverified**: known from a search snippet of the VS Code editing page. Not found: `multicursor`, `MultiCaret`, `AddCursor`.

**Diagnostics**

36. **Task List of TODO comments.** A panel (here, an output tab) listing comments tagged `TODO`, `HACK`, `UNDONE` or custom tokens, with Enter opening the file at the line and next and previous task keys. Seen in VS. Not found: `TODO`, `HACK`, `UNDONE`, `TaskList`, `comment token`. A cheap start: a scan of the solution's source files, in the style of the Errors tab (`BuildDiagnostic`, `OpenDiagnostic`).

**Version control**

37. **Changed-line markers in the editor gutter.** **Unverified**: the fetched VS Code overview page did not describe them; a search snippet of it did. Partly present: `StudioSourceEditor` has a mark column (`GutterMark`) used for the breakpoint, stop, diagnostic and coverage marks. Not found: `GitStatus`, `GitDiff`, `changed line` in the studio.
38. **Blame for the cursor line.** Seen in VS Code. Not found: `blame`, `git blame`, `Annotate`.
39. **Stage selected lines of a diff.** Seen in VS Code. Partly present: `GitClientDialog` (`iTerminal.Core/Dialogs/Git/`) stages whole files (`Unstaged` and `Staged` lists) and `DiffFilesDialogCommand` compares two files; neither is wired to the studio's editor. Not found: `hunk`, `StageLines`. Also not found in the studio: `GitClientDialog`.

**Improvement opportunities**

40. **Keep the Main program between restarts.** Existing: `MainProgramStore`, an in-memory singleton ("survives closing the studio", per its summary) with no persistence, where `BreakpointStore` now persists. **Proposal, unverified**: no page on where the IDEs keep the startup project was read in this survey. Benefit: F5 starts the same program after a restart. Search terms: `ISettingsService`, `DbContext` found nothing in `MainProgramStore.cs`; `StudioSettings` has no key for it.
41. **Send each edit to the language server.** Existing: `CSharpLanguageServer.OpenFile`, `SaveFile` and the diagnostics marks (`!`, `~`); the architecture document lists "the server hears of a save, not of each keystroke" as a known limit. **Proposal, unverified**: the IDEs' behaviour here was not read in this survey. Benefit: the gutter marks follow typing, not only Ctrl+S. `DidChangeAsync` exists, so the work is calling it from the editor's change event, rate limited.
42. **Open a diff or the Git client from the studio.** Existing: `GitClientDialog` and `DiffFilesDialogCommand` in `iTerminal.Core`. VS Code keeps the Source Control view in the same window as the editor (verified, overview page). Benefit: review the change to the open file without leaving the studio. Not found in the studio: `GitClientDialog`, `DiffFiles`. This is a proposal; the dialogs work as built.

**Suggested order** (new items only, by value to iTerminal)

1. Item 27, then 29: build configurations are one argument on the .NET commands and apply to build, run, test and debug.
2. Item 30, then 31: `dotnet format` as a command, then on save.
3. Item 36: the Task List, built on the Errors tab's machinery.
4. Items 28 and 40: launch profiles and a persisted Main program.
5. Items 42, 37, 38: Git in the studio, starting with opening the existing Git client.
6. Items 41 and 32: language server follow-ups on the existing client.
7. Items 33, 34, 39, 35.

**Candidates already covered**

- Enforce code style at build: present (`DotNetToolchain.Analyze`, the `n` key and the Analysis tab).
- Cleaning, building, testing from one explorer: present (`b`, `c`, `t`, `r`).
- Saving the edited file: present (`SaveSource`, Ctrl+S); item 26 is built.
- Persisting breakpoints: present; item 25 is built.
- Git operations and file diff in the file manager: present outside the studio (`GitClientDialog`, `DiffFilesDialogCommand`); items 39 and 42 only concern the studio's editor.
- Toggle comment and line tools in the editor: present (`TextEditorControl`).
