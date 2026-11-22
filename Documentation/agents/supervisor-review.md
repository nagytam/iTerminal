# Supervisor review of the agents

## How to use this document

The supervisor agent (`.claude/agents/supervisor.md`) audits the agent definitions in `.claude/agents/` against the commit history, the GitHub Actions results and the model changes, and writes suggestions here. Each suggestion is independent: apply any subset, in any order. Statuses are Proposed, Applied, Rejected and Obsolete. The supervisor never edits the agents, skills, `CLAUDE.md` or any other file; the user decides. Suggestion IDs (`S<n>`) are never reused. Each run replaces the "Current review" section and keeps the index up to date; Applied, Rejected and Obsolete suggestions stay in the index only.

## Suggestion index

| ID | Title | Kind | Effort | Status |
| --- | --- | --- | --- | --- |
| S1 | List the six undescribed agents in `CLAUDE.md` and fix the agent ordinals | Change to CLAUDE.md | S | Proposed |
| S2 | `fixer`: use the `github` remote and ignore cancelled runs | Revise agent | S | Proposed |
| S3 | `fixer`: add a suite-wide slowdown class for mass end-to-end timeouts | Revise agent | S | Proposed |
| S4 | `incremental-improvement`: add a narrow-terminal check to the review gate | Change to a skill | S | Proposed |
| S5 | `janitor`: reconcile the remote note and consider a stronger model | Revise agent | S | Proposed |
| S6 | `supervisor` and `promoter`: change colour (three agents are orange) | Revise agent | S | Proposed |
| S7 | `fixer`, `profiler`, `qa`: stop embedding the test project list | Revise agent | S | Proposed |
| S8 | `integrator`: align the merge authority with `CLAUDE.md` | Revise agent | S | Proposed |

## Current review: 2026-11-22 all agents

Window: `4ad0edba` (the previous review's tip) to `61b68691`, 92 commits. Reviewed on branch `fix/qa-16-wipe-tick-source` at `61b68691` (not `main`; the branch has an unrelated uncommitted change in `iTerminal.Core/Dialogs/SecureWipeDialog.cs`, left untouched). Sources available: git history, `gh` (authenticated, read-only queries), the workflow files. Not run: the app and the test suites. "Not found" means a grep or `git ls-files` returned nothing. S1 to S6 were re-checked against the agent files and `CLAUDE.md`: none is applied yet.

### Evidence summary

Commit themes (`git log --oneline 4ad0edba..HEAD`, `git log --name-only`, subject greps):

| Theme | Count | Note |
| --- | --- | --- |
| Commits in the window | 92 | |
| Project splits ("Move ... into ...") | 8 | `iConsole.Formats`, `iConsole.Media.Audio`, `.Speech`, `.Video`, `iTerminal.Core.Games`; `iTerminal.slnx` gained 10 projects (5 source, 5 test) |
| WinGet manifests and publishing | 7 | #782, #783, #784, #786, #788, #791, #793: three hand-made manifest sets (0.0.0.727, .731, .735), then a manual `winget.yml` |
| Subjects with Fix, Repair, Revert, Stop, Keep or Never | 14 | recurring class: German or narrow layout in e2e tests (#811) |
| Agent additions | 4 | `integrator` #747 (then 7 follow-ups #749 to #758), `promoter` #767, `architect` #776, `qa` #810 |
| Agent outputs used | yes | architect findings shipped (#805, #807, #808 name findings 24, 26, 10, 55, 68); promotion overview updated in #770, #794, #795, #806; `Documentation/QA/qa-overview.md` used by `61b68691` (QA-16) and #812 |

`Documentation/plans/feature-proposals.md` still does not exist: `planner` has never run, so its effectiveness is unmeasured.

Pipeline results (`gh run list --workflow build.yml --limit 40`, `gh run view <id> --json jobs`, `--log-failed | grep`):

| Item | Result |
| --- | --- |
| `build.yml`, last 40 runs (2026-11-21 17:37 to 21:20) | pull_request: 8 success, 7 failure, 9 cancelled; push on `main`: 5 success, 6 failure, 5 cancelled |
| `release.yml`, last 30 runs | 30 success (pull_request events); `winget.yml`: 0 runs |
| Six failed `main` pushes (44936425002, 44939633048, 44942013183, 44943515522, 44947969087, 44948346711) | all fail in the "Test (including end-to-end suite)" step; Windows only in 3, both jobs in 3 |
| Cause 1: German Passwords e2e test | `ThePaletteFooterAndSixMoreDialogsInGerman_AreGerman ("Passwords", ...)` failed after 30 s on 3 consecutive pushes (44942013183, 44943515522, 44947969087), fixed by #811 (the empty-list hint wrapped). Same class as the German locale failures of the previous review |
| Cause 2: other e2e timeouts, one each | `CtrlShiftDShowsTheFocusedFolderInTheOtherPanel` (44936425002), `ASlowListingIsNotCachedAsEmpty_...` (44939633048), `CtrlShiftF8_AnyOtherKeyAfterTheFirstEnter_WithdrawsIt` (44942013183), `EnterOpensATarGzAndF5CopiesOutOfIt` (44948346711); all in Windows jobs. No test went red then green on one commit in the sample, so flakiness is suspected, not proven |
| Workflow settings | Linux job 30 min, test step 20 min; Windows job 45 min, test step 38 min (`build.yml` lines 26, 58, 134, 152); the Linux step runs nine test projects (lines 60 to 99) |
| Cancelled runs | 14 of 40 are cancelled; `fixer` still treats every non-green run as a failure |

Model changes:

| Item | Result |
| --- | --- |
| Data model | `git log 4ad0edba..HEAD -- iTerminal.Core/Data` lists `a3773607` (disk usage), `df8c67d4` (audio move) and `53c58bff` (music player buttons). I did not diff the entities, so a cached-table drop is not excluded. No agent writes persistence code |
| Agent models | `opus`: `architect`. `sonnet`: `design-reviewer`, `fixer`, `integrator`, `planner`, `profiler`, `promoter`, `qa`, `supervisor`. `haiku`: three researchers, `janitor`. All aliases, none pinned |
| Other | `iTerminal.slnx` gained 10 projects and `build.yml` runs all of them, but `fixer.md` lines 21 and 22, `profiler.md` line 32 and `qa.md` line 48 name only the older set (S7). New workflow `winget.yml` (manual) has no agent |

### Agent audit

| Agent | Verdict | Reason |
| --- | --- | --- |
| `architect` | OK | opus fits a deep review; writes one document; output used (#776, #805, #807, #808) |
| `design-reviewer` | OK | unchanged, output used |
| `fixer` | Revise | `origin/main`, cancelled runs, no suite-wide class, stale project list (S2, S3, S7) |
| `integrator` | Revise | claims a standing authority to merge while `CLAUDE.md` says merging waits for the user's approval (S8); worktree and one-shot wait script are sound |
| `janitor` | Revise | remote note (S5) |
| `planner` | OK | never run |
| `profiler` | Revise | stale test project list (S7), colour (S6) |
| `promoter` | Revise | colour only (S6); output logged and used (#770, #794, #795, #806) |
| `qa` | Revise | unit-test pairing names only two source projects (S7); scope `pipeline` overlaps `fixer` by design (qa documents, fixer fixes) |
| `researcher-console-features`, `researcher-security-features`, `researcher-ai-features` | OK | unchanged |
| `supervisor` | Revise | colour (S6) |

Across the set: `CLAUDE.md` describes none of `fixer`, `janitor`, `integrator`, `architect`, `promoter`, `qa` (S1). Colours: orange is used by `profiler`, `promoter` and `supervisor`; green by `integrator` and `qa`; blue by the three researchers, `planner` and `architect` (acceptable as the research and plan group).

### Suggestions

**S1. List the six undescribed agents in `CLAUDE.md`, fix the ordinals** (Change to CLAUDE.md)
- Why: the agent paragraph names the three researchers, `planner`, "a fifth" `design-reviewer`, "a sixth" `profiler` and `supervisor`; `fixer`, `janitor`, `integrator`, `architect`, `promoter` and `qa` are absent.
- Evidence: `ls .claude/agents` lists 12 agents and `wait-for-change.sh`; `grep -n -i 'fixer\|janitor\|integrator\|promoter' CLAUDE.md` finds nothing; four were added in this window (#747, #767, #776, #810).
- Change: replace "A fifth agent, `design-reviewer`" and "A sixth agent, `profiler`" with "The `design-reviewer` agent" and "The `profiler` agent", and add: "`architect` writes `Documentation/research/architecture-overview.md`, `qa` writes `Documentation/QA/qa-overview.md`, `promoter` writes `Documentation/Promotion/` and `Documentation/research/promotion-overview.md`. `fixer` fixes red `Build and test` runs on `main` on a branch with a pull request (`/fix-main` is the gated version). `integrator` lands green pull requests when the user starts it for that. `janitor` deletes local branches and worktrees already merged into `main`."
- Risk: none beyond keeping the text in step.
- Effort: S.

**S2. `fixer`: use the `github` remote and skip cancelled runs** (Revise agent)
- Why: `fixer.md` line 64 cuts the branch from `origin/main`, while the skill and `integrator.md` use `github`; Phase 1 acts on every non-green run, and cancelled runs are common.
- Evidence: `fixer.md` line 64; `incremental-improvement/SKILL.md` (`git push -u github`); `integrator.md` line 19; 14 of 40 `build.yml` runs are cancelled (5 of 16 push runs).
- Change: line 64: use `git fetch github` and `github/main`, or "the remote whose URL is the GitHub repository". Phase 1 step 1: add "Ignore runs whose conclusion is `cancelled` or `skipped`; take the five latest that ended `success` or `failure` (raise `--limit` to find them)."
- Risk: a clone with other remote names needs the URL rule, which is included.
- Effort: S.

**S3. `fixer`: classes for suite-wide timeouts and German layout** (Revise agent)
- Why: the last window showed mass 120 s timeouts; this one shows one test failing on three consecutive pushes (German Passwords, 30 s) and four other 30 s Windows timeouts. The Locale bullet covers keys but not a hint that wraps in German.
- Evidence: runs 44942013183, 44943515522, 44947969087 (same test); 44936425002, 44939633048, 44942013183, 44948346711 (four others); fix #811. `grep -n -i 'suite-wide\|wrap' .claude/agents/fixer.md` finds nothing.
- Change: in Phase 2 item 3 add before **Locale**: "**Suite-wide slowdown**: three or more unrelated tests time out in one job, or durations grow along the run. Suspect apps, memory or handles leaked by earlier tests (compare per-test durations in order) before the test's own text." And to **Locale**: "A test that waits for German text and times out may be waiting for a line that wraps or is cut: check the width of the German string at the test's terminal size."
- Risk: could delay finding a real key bug; both checks are cheap.
- Effort: S.

**S4. `incremental-improvement`: narrow-terminal and German-width check in the review gate** (Change to a skill)
- Why: layout overflow was fixed by hand, dialog by dialog, and #811 shows it still reaches `main` and turned it red for about 1.5 hours.
- Evidence: `grep -n -i 'narrow\|80x24\|German' .claude/skills/incremental-improvement/SKILL.md` finds nothing; three consecutive red `main` pushes from one German Passwords hint (44942013183 to 44947969087, 19:38 to 20:45); 7 narrow-terminal commits in the previous window.
- Change: next to the sandbox and `.resx` gate items add: "- A new or changed screen was checked at 80x24 and with the German strings: no line is cut mid-value, hints wrap, and the focused control stays visible (a `ScreenBuffer` test at a small size)."
- Risk: one more gate item, limited to changes that draw.
- Effort: S.

**S5. `janitor`: reconcile the remote note, consider `sonnet`** (Revise agent)
- Why: line 11 says "The remote is named `github` (not `origin`)"; a clone without `github` makes every merge check fail. The agent runs `git branch -D` and `git worktree remove` on `haiku`.
- Evidence: `janitor.md` lines 11, 19, 31; `git remote -v` lists `github`, `max` and others.
- Change: line 11: "The GitHub remote is usually named `github` (check `git remote -v`; use the remote whose URL is the GitHub repository)." Optionally `model: sonnet`.
- Risk: higher cost per run if the model changes; optional.
- Effort: S.

**S6. `supervisor` and `promoter`: change colour** (Revise agent)
- Why: `profiler`, `promoter` and `supervisor` are all `orange` for unrelated roles.
- Evidence: `grep '^color' .claude/agents/*.md`.
- Change: `supervisor.md`: `color: cyan`; `promoter.md`: `color: pink`. The green pair (`integrator`, `qa`) is left: both touch the pipeline.
- Risk: none.
- Effort: S.

**S7. `fixer`, `profiler`, `qa`: stop embedding the test project list** (Revise agent)
- Why: the embedded lists miss five new test projects, so the fixer can name a failing job's projects wrongly and `qa` pairs only two source projects with tests.
- Evidence: `fixer.md` lines 21 and 22 list four Linux test projects; `build.yml` lines 60 to 99 run nine (`iConsole.Formats.Test`, `iConsole.Media.Audio.Test`, `iConsole.Media.Speech.Test`, `iConsole.Media.Video.Test`, `iTerminal.Core.Games.Test` are missing); `profiler.md` line 32 and `qa.md` line 48 are equally partial; `git diff 4ad0edba..HEAD -- iTerminal.slnx` adds 10 projects.
- Change: replace each list with "the test projects are the `*.Test` entries of `iTerminal.slnx` and the `dotnet test` lines of `.github/workflows/build.yml`; read them at the start". In `qa.md` item 1, pair every source project `X` with `X.Test`, not only `iConsole` and `iTerminal.Core`. In `fixer.md` line 22, "the suite takes about 25 minutes" disagrees with `CLAUDE.md` (4 to 5 minutes for the end-to-end suite alone); check which is right before editing.
- Risk: slightly more reading per run.
- Effort: S.

**S8. `integrator`: align the merge authority with `CLAUDE.md`** (Revise agent)
- Why: `CLAUDE.md` and the skill say merging waits for the user's approval; `integrator.md` line 13 states that the user "has explicitly asked" it to merge, a claim held in a file, not given in chat.
- Evidence: `integrator.md` line 13; `CLAUDE.md` "Merging into `main` waits for the user's approval"; `incremental-improvement/SKILL.md` lines 30 and 160. Also line 114 stops merging while `main` is red, yet 6 of 16 finished push runs on `main` were red, 3 in a row for one test; whether the integrator was running then is not known.
- Change: `integrator.md` line 13: "Merge only when the message that started you asks for merging; otherwise report the green pull requests and stop." In `CLAUDE.md`, one sentence naming the exception: "`integrator` merges only when the user starts it for that purpose."
- Risk: the loop does less when started loosely; that is the point.
- Effort: S.

### Suggested order

1. S1: six agents are missing from `CLAUDE.md`; a few lines.
2. S8: the one place an agent file overrides a stated project rule.
3. S3, S2: `fixer` is the agent used on red runs, and both classes recurred this window.
4. S7: stale lists cause wrong guesses and rot again unless replaced by a pointer.
5. S4: removes a defect class at its source.
6. S5, S6: polish.

### Rejected or skipped

- New agent for WinGet or release publishing: 7 commits, but three manifest sets were hand-made and `winget.yml` (manual, 0 runs) now automates it; judge it after its first run.
- New agent for localisation or layout checking: one German failure class (3 red pushes) is covered by S3, S4 and the `design-reviewer` resilience heuristic.
- New agent to implement architect findings or project splits: findings already ship through `/incremental-improvement` (#805, #807, #808); the splits are one-off.
- New agent for end-to-end flake triage: four different tests timed out once each and none went red then green on one commit; `fixer` covers it (S3).
- Model change for `architect` (`opus`) or the researchers (`haiku`): no evidence of poor output.
- Judging `planner`: not possible until it has run once.
