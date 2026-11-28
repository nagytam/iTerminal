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

#### Survey 3: PyCharm: Python environments, run configurations, packages, tests, inspections, tools

Basis: the official documentation, not run-time behaviour. PyCharm from www.jetbrains.com/help/pycharm (the pages show "PyCharm 2026.3 Help" with page dates between 2026-02-13 and 2026-11-17; the Professional edition is named where a page says so). Surveyed on 2026-11-28 at branch `feature/devstudio-menu-bar`, commit `f40c66ec`, edited in the working tree (no branch switch, no commit). "Not found" means a case-insensitive grep of `*.cs` in `iTerminal.DevelopmentStudio/`, `iTerminal.Core/` and `iConsole/` on that commit found nothing for the listed terms; a "partly present" item says what exists. Items 1 to 42 are not repeated: where PyCharm offers what an earlier item already covers, the item number is given instead.

Sources (every IDE named under "Seen in" appears here; rows marked unverified come from a search result only):

| IDE | Feature | Area | Source | Version or date | Description |
| --- | --- | --- | --- | --- | --- |
| PyCharm | Interpreter kinds (system, venv, conda, uv, Poetry, pipenv, Hatch; remote over SSH, Docker, WSL), per-project and per-subproject choice, status bar switcher | Solution and projects | https://www.jetbrains.com/help/pycharm/configuring-python-interpreter.html | 2026.3, updated 2026-11-17 | A project names the interpreter whose packages it uses, and the choice can be changed from the status bar. |
| PyCharm | Python run configuration: script or module, parameters, environment variables, working directory, interpreter options, run with Python console, emulated terminal, redirect input from a file | Build and tasks | https://www.jetbrains.com/help/pycharm/run-debug-configuration-python.html | 2026.3, 2026-11-17 | Saved settings for how one script or module runs. |
| PyCharm | Rerun failed tests, rerun automatically on source change, drop into the debugger on failed tests (also setup and teardown errors) | Testing | https://www.jetbrains.com/help/pycharm/rerunning-tests.html | 2026.3, 2026-02-13 | The test runner repeats failures and can stop the debugger at a failing assertion. |
| PyCharm | requirements.txt: detection, unsatisfied-requirements notification with Install, version specifier policy, creating a virtual environment from the file | Packages | https://www.jetbrains.com/help/pycharm/managing-dependencies.html | 2026.3, 2026-08-17 | The editor checks the interpreter against the requirements file and offers to install what is missing. |
| PyCharm | Python Packages tool window listing installed packages and PyPI and conda repositories, with a search field | Packages | https://jetbrains.com/help/pycharm/available-packages.html | not fetched | Known from a search snippet only: unverified. |
| PyCharm | Refactoring list (Ctrl+Alt+Shift+T), Preview in the Find window, conflict list, undo; Change Signature | Refactoring | https://www.jetbrains.com/help/pycharm/refactoring-source-code.html | 2026.3, 2026-08-17 | Refactorings are previewed, conflicts are listed first, and Ctrl+Z reverts them. The fetched page did not name rename or extract method for Python; those come from a search snippet (Shift+F6, Ctrl+Alt+M): unverified. |
| PyCharm | Code inspections: severities, profiles, scopes (whole project, production code, modified files), Qodana and command-line runs | Diagnostics | https://www.jetbrains.com/help/pycharm/code-inspection.html | 2026.3, 2026-10-05 | Static checks with a severity each, grouped in profiles and run over a chosen scope. The page did not cover quick fixes or suppression. |
| PyCharm | Django: manage.py tasks, run server configuration, debug templates, test runner choice | Frameworks | https://www.jetbrains.com/help/pycharm/django-support7.html | 2026.3, 2026-10-16 | Framework commands run from the IDE with their own run configurations. |
| PyCharm | HTTP Client: `.http` files, requests separated by `###`, environments, response history, response handler scripts, cURL and Postman import | Tools | https://www.jetbrains.com/help/pycharm/http-client-in-product-code-editor.html | 2026.3, 2026-09-25 | Requests written and run from a text file, with variables per environment. |
| PyCharm | Python console: project interpreter, environment variables, starting script, working directory | Tools | https://www.jetbrains.com/help/pycharm/python-console.html | 2026.3, 2026-10-24 | A REPL on the project's interpreter. The page covers its settings only; sending a selection to it and the variables view were not read: unverified. |
| PyCharm | Built-in terminal: tabs, sessions saved between runs, Open in Terminal | Tools | https://www.jetbrains.com/help/pycharm/terminal-emulator.html | 2026.3, 2026-10-23 | Shell sessions inside the IDE. The page did not say the virtual environment is activated, so that is unverified. |

Features considered and skipped: remote interpreters over SSH, Docker, Docker Compose and WSL (PyCharm Professional; the studio runs processes on the local machine, and remote drives belong to `iTerminal.Core`), Qodana and CI quality gates (a hosted service, not a terminal feature), Django template debugging (needs a template debug protocol the studio does not reach), HTTP/2, WebSocket and GraphQL requests of the HTTP Client (only the `.http` file runner is proposed), Python scientific mode and Jupyter notebooks (a GUI notebook view; no PyCharm page for them was read), the Services tool window (a GUI dock), inspection profiles in a settings UI (see item 53 for the cheaper part). Rerun failed tests and auto rerun are covered by items 17 and 13; Python rename is covered by item 23.

**Solution and projects**

43. **Choose the Python interpreter and virtual environment.** A project names the interpreter (system, venv, conda, uv, Poetry, pipenv, Hatch) and the build, run, test and debug commands use it. Seen in PyCharm. Partly present: `PythonToolchain.Interpreter` is one `init`-only property, `python` on Windows and `python3` elsewhere, shared by every Python project. Not found: `VIRTUAL_ENV`, `pyvenv.cfg`, `.python-version`, `conda`, `poetry`, `Scripts\python`, `bin/python` (the strings `venv` and `.venv` occur only in folder skip lists in `DevelopmentWorkspaceScanner`, `DevelopmentStudioDialog` and `PythonToolchain.SafeSubfolders`). A cheap start: detect `.venv` or `venv` next to the project and use its interpreter.
44. **Create a virtual environment.** A new venv from a base interpreter, or from a requirements file. Seen in PyCharm. Not found: `-m venv`, `ensurepip`, `virtualenv`, `uv venv`. Depends on item 43.
45. **Switch the interpreter from the status line.** Seen in PyCharm (status bar widget). Not found: `Interpreter` in `StudioSettings`, `SelectInterpreter`. Depends on item 43; `StudioBuildSettingsTabPage` already holds the build configuration (item 27, built).

**Packages**

46. **Install, list and update Python packages.** A packages view for the project's interpreter with search. Seen in PyCharm (the tool window is unverified, search snippet). Partly present: `NuGetManagerDialog` and `INuGetPackageService` do this for .NET projects. Not found: `pip install`, `pip list`, `pip freeze`, `pip-audit`, `PyPI`, `pypi.org`. `PackageManagerDialog` in `iTerminal.Core` manages system packages, not Python ones.
47. **Requirements file check.** Opening a project with `requirements.txt` warns when the interpreter lacks a listed package and offers Install requirements. Seen in PyCharm (verified). Not found: `requirements.txt`, `Install requirements`, `pyproject`. Depends on items 43 and 46.

**Build and tasks**

48. **Per-script run settings: arguments, environment variables, interpreter options, working directory.** Saved with the project and used by run and debug. Seen in PyCharm (VS Code's launch configuration is item 12). Partly present: `ProcessCommand` carries `Environment` and `WorkingDirectory` (the Java toolchain sets `JAVA_HOME` through it), and `DevelopmentProcessRunner` honours both; `PythonToolchain.Run` passes the script path only and `PlanDebug` sends `args = []`. Not found: `ScriptArguments`, `Parameters`, `PYTHONUNBUFFERED`, `interpreter options`, `Run configuration`. Related to items 11 and 12.
49. **Run a module with `-m`.** The run configuration can name a module instead of a file. Seen in PyCharm. Not found: `ModuleName`, `module name`; only `py_compile`, `pytest`, `coverage` and `cProfile` are fixed module calls in `PythonToolchain`.
50. **Feed standard input from a file.** A script that reads input runs without typing. Seen in PyCharm ("Redirect input from"). Not found in `DevelopmentProcessRunner`: `RedirectStandardInput`, `Redirect input`. Only the debug adapter and language server processes redirect input.
51. **Framework run tasks (Django manage.py and run server; Flask and FastAPI are not covered by any page read).** Seen in PyCharm (Django, verified). Not found: `manage.py`, `django`, `flask`, `fastapi`, `uvicorn`. **Fit: low.** Items 48 and 49 give the same result for a user who types the arguments.

**Testing**

52. **Stop the debugger when a test fails.** "Drop into debugger on failed tests": failures and setup and teardown errors stop at the cause with variables visible. Seen in PyCharm (verified). Partly present: `PythonToolchain.PlanDebug` with `debugTests` starts `pytest` under debugpy. Not found: `--pdb`, `breakOnFailure`, `exceptionBreakpoint`, `setExceptionBreakpoints`. Related to item 4.

**Diagnostics**

53. **Static analysis for Python.** A check of the whole project or a scope, with a severity per problem, shown in the Errors panel. Seen in PyCharm (inspections, verified: scope and severity). Partly present: `IDevelopmentToolchain.Analyze` returns a command for .NET only (`DotNetToolchain.Analyze`, the `n` key); `PythonToolchain` does not override it, so `n` has no command for Python, and `PythonToolchain.Build` is `py_compile` of one file, which finds syntax errors only. Not found: `ruff`, `flake8`, `pylint`, `mypy`, `pyright`, `pycodestyle`. A cheap start: `Analyze` returns a ruff or flake8 command when installed, parsed by `BuildDiagnosticParser`.
54. **A Python language server for the live gutter marks.** Seen in PyCharm (live inspections; the page did not cover quick fixes). Not found: `pylsp`, `pyright`, `python-lsp-server`; `LanguageServerLauncher` and `CSharpLanguageServer` serve C# only. Depends on item 41 and relates to items 23 and 32.
55. **Refactoring with preview, conflict list and undo (Python).** Preview, conflicts and undo are verified for PyCharm; rename and extract method are unverified (search snippet). Needs requests on the language server client (items 23, 32, 54). Not found: `Refactor`, `RefactorPreview`, `Conflicts` in the studio.

**Tools**

56. **Python console (REPL) on the project interpreter.** Seen in PyCharm; the settings page is verified, sending a selection to it is unverified. Not found: `python -i`, `PythonConsole`, `Repl` in the studio. Partly present: Ctrl+O lends the terminal to a shell (`ConsoleOwnership`), where `python` can be typed, but not in the project's environment. Related to item 10.
57. **HTTP request files (`.http`).** Run the requests of a text file, choose an environment, keep the response history. Seen in PyCharm (verified). Not found: `.http`, `http-client.private.env.json`, `HttpRequestFile`. `HarViewerDialog` in `iConsole` reads recorded traffic and sends nothing. **Fit: medium** for a console tool; it needs no language support.

**Improvement opportunities**

58. **Start a shell from the studio in the project's folder and environment.** Existing: `ConsoleOwnership` (Ctrl+O) and the terminal panel in `iTerminal.Core/Controls/Terminals`. PyCharm keeps terminal sessions and opens one at a file's folder (verified); that it activates the virtual environment is unverified. **Proposal, unverified**: no code was read that starts elsewhere. Benefit: a shell opened from the studio starts in the project folder with the project's interpreter first on `PATH`. Search terms: `TerminalPanel`, `WorkingDirectory` found no hand-over from `DevelopmentStudioDialog`.
59. **Recognise virtual environments by content, not by name.** Existing: `DevelopmentWorkspaceScanner` skips `.venv`, `venv` and `env` by name, `PythonToolchain.SafeSubfolders` skips `venv` and `env`, and `DevelopmentStudioDialog` holds a third list. PyCharm treats an environment as a project interpreter, not as a folder name (verified, interpreter page). **Proposal, unverified**: a venv with another name would be scanned for projects and cache folders; no such case was run. Benefit: one rule (`pyvenv.cfg` present) in place of three lists, and the same detection feeds item 43.
60. **Python Build with a useful result.** Existing: `PythonToolchain.Build` runs `py_compile` on the project's file. PyCharm has no compile step and reports problems through inspections (verified, item 53). Benefit: `b` on a Python item would list lint problems in the Errors tab. Proposal; the current command works as built.

**Suggested order** (new items only, by value to iTerminal)

1. Items 43, then 47 and 44: the interpreter and virtual environment choice is the base; every Python command already goes through the one `Interpreter` property.
2. Items 53 and 60: Python static analysis through `Analyze`, shown in the Errors tab.
3. Items 48, 49, 50: run settings (arguments, environment, module, input file); shares work with items 11 and 12.
4. Item 46: pip package view, modelled on `NuGetManagerDialog`.
5. Item 52, with item 4: debug a failing test.
6. Items 56, 58, 59, 45: console and terminal in the project environment.
7. Item 57: `.http` files.
8. Items 54 and 55 last: a Python language server and refactorings; item 51 is low.

**Candidates already covered**

- Rerun failed tests, auto rerun: items 17 and 13 (PyCharm's page matches both).
- Rename and symbol navigation for Python: item 23.
- Test results as a list of tests: item 15 (PyCharm shows a tree with a result each).
- Run configurations saved with the project and shared by VCS: items 11, 12, 28.
- Running a script, pytest, coverage and a profiler: present (`r`, `t`, `o`, `PythonToolchain.Coverage` and `Profile`).
- Debugging a Python script or its tests under debugpy: present (`PythonToolchain.PlanDebug`).
