# Supervisor review of the agents

## How to use this document

The supervisor agent (`.claude/agents/supervisor.md`) audits the agent definitions in `.claude/agents/` against the commit history, the GitHub Actions results and the model changes, and writes suggestions here. Each suggestion is independent: apply any subset, in any order. Statuses are Proposed, Applied, Rejected and Obsolete. The supervisor never edits the agents, skills, `CLAUDE.md` or any other file; the user decides. Suggestion IDs (`S<n>`) are never reused. Each run replaces the "Current review" section and keeps the index up to date; Applied, Rejected and Obsolete suggestions stay in the index only.

## Suggestion index

| ID | Title | Kind | Effort | Status |
| --- | --- | --- | --- | --- |
| S1 | List `fixer` and `janitor` in `CLAUDE.md` and fix the agent ordinals | Change to CLAUDE.md | S | Proposed |
| S2 | `fixer`: use the `github` remote and ignore cancelled runs | Revise agent | S | Proposed |
| S3 | `fixer`: add a suite-wide slowdown class for mass end-to-end timeouts | Revise agent | S | Proposed |
| S4 | `incremental-improvement`: add a narrow-terminal check to the review gate | Change to a skill | S | Proposed |
| S5 | `janitor`: reconcile the remote note and consider a stronger model | Revise agent | S | Proposed |
| S6 | `supervisor`: change colour (collides with `profiler`) | Revise agent | S | Proposed |

## Current review: 2026-11-20 all agents

Window: the last 100 commits on `main` (no earlier review exists), tip `4ad0edba`. Reviewed on branch `worktree-agent-a2fa1a56c57fd4fa1` at `4ad0edba`. Sources available: git history, `gh` (authenticated, read-only queries), the workflow files. Not run: the app and the test suites. "Not found" means a grep or `git ls-files` on this commit returned nothing. The task said `design-reviewer.md` and `supervisor.md` were just changed; the last commit touching either file in the window is `3fc9b35c` (design-review consolidation), so no newer change is visible in history, and both files were audited as they stand.

### Evidence summary

Commit themes (`git log --name-only --format= -100 main`, first path segment per file touched; `git log --format=%s -100 main | grep`):

| Theme | Count | Note |
| --- | --- | --- |
| `iTerminal.Core/` files touched | 399 | feature work, dialogs, drives |
| `iTerminal.Core.Test/`, `iTerminal.Test/`, `iConsole.Test/` files touched | 233 | tests alongside features |
| `Documentation/`, `.claude/`, `docs/`, `CLAUDE.md` files touched | 90 | agent and research work |
| Subjects about narrow or short terminals | 7 | layout fixed by hand, dialog by dialog (for example #650, #651, #656) |
| Subjects about translation or locale | 2 | #660, #662 |
| Subjects starting Fix, Stop, Keep, Never, Repair or Revert | 12 | recurring classes: layout width, stale text, key conflicts |
| Agent additions | 6 | fixer #685, design-reviewer #687, supervisor #688, janitor #690, profiler #699, researchers #678 (planner also added, commit not numbered in the log excerpt) |

Agent outputs used: the research overviews were updated in the window (#678, #701 and earlier), `design-review-overview.md` by #698 and #701, `profiling-overview.md` by #713. `Documentation/plans/` and `Documentation/agents/` did not exist before this run: `planner` was added but has never produced `feature-proposals.md`, so no proposal has shipped from it.

Pipeline results (`gh run list --workflow build.yml --limit 30`, same for `release.yml`, `gh run view <id> --json jobs`, `--log-failed | grep | head`):

| Item | Result |
| --- | --- |
| `build.yml`, last 30 runs | 13 push runs cancelled, 2 pull_request cancelled, 8 pull_request and 1 push in progress, 5 pull_request failed, 1 push on `main` failed (44753840314), 0 successes among the 30 |
| `release.yml`, last 30 runs | 27 success, 3 in progress, 0 failures |
| Failed runs inspected (44756333210, 44753840314, 44752100870, 44755328361) | all four failed in the Windows job step "Test (including end-to-end suite)"; the Linux job was green in one and red with no failing step in three |
| Failing tests seen | `TheDiskManagerSystemSettingsMapAndWebBrowserInGerman_AreGerman` (several cases, 1 m 22 s to 2 m 45 s, "timed out after 120000ms"), `TheGamesStatusLinesAndRulesInGerman_AreGerman`, `TheFileOperationDialogsOpenedAfterALiveSwitchToGerman_AreGerman` |
| Flaky | none shown: no test went red then green on one commit within the sampled runs (too few completed runs) |
| Workflow settings | Linux job 30 min, test step 20 min; Windows job 45 min, test step 38 min; both match `fixer.md` |
| Concurrency | `build.yml` cancels in progress only for pull requests, yet 13 of the 15 push runs listed are cancelled; the cause was not determined |

Model changes:

| Item | Result |
| --- | --- |
| Data model | `git log -100 main -- iTerminal.Core/Data/iTerminalDbContext.cs`: the newest commit is `3df3474f` (colour labels), not in the window's recent history; no entity change was found in the window, so no cached-table drop. No agent writes persistence code, and none needs to mention the stamp |
| Agent models | `haiku`: three researchers, `janitor`. `sonnet`: `fixer`, `planner`, `design-reviewer`, `profiler`, `supervisor`. All use aliases, so none pins an outdated version |
| Other | no new project in `iTerminal.slnx` affects an agent path. Every path under `Documentation/` named by an agent exists except `Documentation/plans/feature-proposals.md` and this file, which their owners create |

### Agent audit

| Agent | Verdict | Reason |
| --- | --- | --- |
| `researcher-console-features` | OK | valid front matter, paths exist, has web tools, treats pages as data |
| `researcher-security-features` | OK | same |
| `researcher-ai-features` | OK | same; reads `Documentation/ai-integration-opportunities.md`, which exists |
| `planner` | OK | consistent with the chain; never run, so effectiveness is unmeasured |
| `design-reviewer` | OK | tools fit, reads the research overviews, output consolidated and used (#698, #701) |
| `profiler` | OK | measures first, handles missing tools; its output landed in #713 |
| `fixer` | Revise | cuts the branch from `origin/main` while the repository workflow uses `github`; treats cancelled runs as failures; no class for suite-wide timeouts (S2, S3) |
| `janitor` | Revise | says the remote is "not `origin`" though `origin` exists here with the same URL; destructive work on `haiku` (S5) |
| `supervisor` | OK | complete; only the colour collides (S6) |

Across the set: `CLAUDE.md` does not describe `fixer` or `janitor` and counts agent ordinals wrongly (S1). Orange is shared by `profiler` and `supervisor`; blue by the researchers and `planner`, which is the intended research-to-plan grouping.

### Suggestions

**S1. List `fixer` and `janitor` in `CLAUDE.md`, fix the ordinals** (Change to CLAUDE.md)
- Why: the agent paragraph names three researchers, `planner`, "a fifth" `design-reviewer`, "a sixth" `profiler` and `supervisor`; `fixer` and `janitor` are absent and the ordinals are off.
- Evidence: `ls .claude/agents` lists nine files; `CLAUDE.md` mentions only the `fix-main` skill for pipeline repair; `fixer` added in #685 and extended in #700, `janitor` in #690.
- Change: in "Workflow for incremental improvements" add: "`fixer` checks the latest `Build and test` runs on `main` (unit and end-to-end) and fixes the causes on a branch with a pull request; `/fix-main` is the stricter gated workflow. `janitor` deletes local branches and worktrees that are merged into `main`." Replace "A fifth agent, `design-reviewer`" and "A sixth agent, `profiler`" with "The `design-reviewer` agent" and "The `profiler` agent".
- Risk: none beyond keeping the text in step with the agents.
- Effort: S.

**S2. `fixer`: use the `github` remote and skip cancelled runs** (Revise agent)
- Why: `fixer.md` cuts the fix branch from `origin/main`, while the skill pushes to `github`; and Phase 1 step 2 acts on "every run that is not green", which includes cancelled runs.
- Evidence: `fixer.md` line 64 (`origin/main`); `incremental-improvement/SKILL.md` line 109 (`git push -u github`); `git remote -v` lists `github`, `origin`, `max`, `nas4`, `nas5`; 13 of 30 `build.yml` runs are push and cancelled.
- Change: line 64: use `git fetch github` and `github/main`, or "the remote whose URL is the GitHub repository". Phase 1 step 1: add "Ignore runs whose conclusion is `cancelled` or `skipped`; take the five latest runs that finished with `success` or `failure` (raise `--limit` to find them)."
- Risk: a clone with different remote names needs the URL rule; included above.
- Effort: S.

**S3. `fixer`: suite-wide slowdown class for mass end-to-end timeouts** (Revise agent)
- Why: in the sampled red runs several unrelated German-locale tests take 82 s to 165 s and time out at 120 s in one Windows step; the **Locale** bullet sends the fixer to `.resx` keys, the wrong first guess for a whole-suite slowdown.
- Evidence: runs 44756333210, 44753840314, 44752100870, 44755328361 (durations from `gh run view <id> --log-failed | grep Failed`); the newest `main` commit is "Fix the end-to-end suite's runaway memory and a Weather flake", so this class has occurred. Four of six failed runs inspected; the share is uncertain.
- Change: in Phase 2 item 3 add before **Locale**: "**Suite-wide slowdown**: three or more unrelated tests time out in one job, or durations grow along the run. Suspect apps, memory or handles leaked by earlier tests (compare TRX start times and per-test durations in order) before the test's own text; fix the leak or the teardown."
- Risk: could delay finding a real locale bug; the duration-trend check is cheap and keeps both.
- Effort: S.

**S4. `incremental-improvement`: narrow-terminal check in the review gate** (Change to a skill)
- Why: layout overflow on narrow or short terminals was fixed by hand, dialog by dialog; a gate item would catch it in the original change.
- Evidence: 7 commit subjects in the window (#650 Settings pages, #651 Passwords dialog, #656 Bluetooth, and four more); `grep -n -i "narrow\|80x24" .claude/skills/incremental-improvement/SKILL.md` finds nothing, while lines 101 and 102 already check sandbox replacements and the six `.resx` files.
- Change: after line 102 add: "- A new or changed screen was checked at 80x24: no line is cut mid-value, hints wrap, and the focused control stays visible (a `ScreenBuffer` test at a small size)."
- Risk: one more gate item; limited to changes that draw.
- Effort: S.

**S5. `janitor`: reconcile the remote note, consider `sonnet`** (Revise agent)
- Why: line 11 says "The remote is named `github` (not `origin`)", but `origin` also exists here with the same URL, and a clone without `github` makes every merge check fail. The agent runs `git branch -D` and `git worktree remove` on `haiku`.
- Evidence: `git remote -v`; `janitor.md` lines 11, 19, 31.
- Change: line 11: "The GitHub remote is usually named `github` (check `git remote -v`; use the remote whose URL is the GitHub repository)." Optionally set `model: sonnet`, since a wrong merge call deletes a branch (recoverable from the SHA it records and the reflog).
- Risk: higher cost per run if the model changes; the existing rules already limit damage, so this part is optional.
- Effort: S.

**S6. `supervisor`: change colour** (Revise agent)
- Why: `supervisor` and `profiler` are both `orange`, for unrelated roles (agent governance and performance).
- Evidence: `grep '^color' .claude/agents/*.md`.
- Change: set `color: cyan` in `supervisor.md` front matter (no other agent uses it).
- Risk: none.
- Effort: S.

### Suggested order

1. S1: the description of the agents in `CLAUDE.md` is wrong today and the fix is a few lines.
2. S2: `fixer` is the agent used against red runs; the wrong remote and cancelled runs both cost work.
3. S3: the Windows end-to-end timeouts recur in the sampled runs.
4. S4: reduces a repeated defect class at the source.
5. S5, S6: optional polish.

### Rejected or skipped

- New agent for localisation or narrow-terminal checking: the evidence (7 narrow-terminal commits, 2 translation commits) is a defect class, but the skill gate (S4) and `design-reviewer`'s "Resilience to size and content" heuristic cover it; a new agent would overlap.
- New agent for dependency or packaging fixes: no commit in the window is about installer, packaging or dependency bumps, and `release.yml` showed 0 failures.
- New agent for end-to-end flake triage: `fixer` was extended to the end-to-end suite in #700; S3 covers the gap seen.
- Data-model guidance in agents: no entity change found in the window and no agent writes persistence code.
- Model change for the researchers (`haiku`): no evidence of poor output.
- Judging `planner`: not possible until it has run once.
