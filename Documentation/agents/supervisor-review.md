# Supervisor review of the agents

## How to use this document

The supervisor agent (`.claude/agents/supervisor.md`) audits the agent definitions in `.claude/agents/` against the commit history, the GitHub Actions results and the model changes, and writes suggestions here. Each suggestion is independent: apply any subset, in any order. Statuses are Proposed, Applied, Rejected and Obsolete. The supervisor never edits the agents, skills, `CLAUDE.md` or any other file; the user decides. Suggestion IDs (`S<n>`) are never reused. Each run replaces the "Current review" section and keeps the index up to date; Applied, Rejected and Obsolete suggestions stay in the index only.

## Suggestion index

| ID | Title | Kind | Effort | Status |
| --- | --- | --- | --- | --- |
| S1 | List the six undescribed agents in `CLAUDE.md` and fix the agent ordinals | Change to CLAUDE.md | S | Applied (#814) |
| S2 | `fixer`: use the `github` remote and ignore cancelled runs | Revise agent | S | Proposed |
| S3 | `fixer`: add a suite-wide slowdown class for mass end-to-end timeouts | Revise agent | S | Proposed |
| S4 | `incremental-improvement`: add a narrow-terminal check to the review gate | Change to a skill | S | Proposed |
| S5 | `janitor`: reconcile the remote note and consider a stronger model | Revise agent | S | Applied (#814; model left `haiku`) |
| S6 | `supervisor` and `promoter`: change colour (three agents are orange) | Revise agent | S | Applied (#814) |
| S7 | `fixer`, `profiler`, `qa`: stop embedding the test project list | Revise agent | S | Proposed |
| S8 | `integrator`: align the merge authority with `CLAUDE.md` | Revise agent | S | Proposed |

## Current review: 2026-11-22 all agents, follow-up

Window: the previous review's tip commit (`61b68691`) is no longer in the repository history (squash merges), so this run uses `7486f8e8` (#813) to `b9b81269`, 6 commits, plus `build.yml` runs up to 2026-11-22 03:54 UTC. Reviewed on branch `docs/agents-review-update` cut from `main` at `b9b81269`. Sources available: git history, `gh` (read-only), workflow files. Not run: the app and the test suites. "Not found" means a grep returned nothing. The data model was not diffed; `git log -3 -- iTerminal.Core/Data` shows nothing newer than #792, so no model change in the window.

### Evidence summary

| Theme | Result |
| --- | --- |
| Commits in the window (`git log --oneline`) | 6: #814 agent fixes (supervisor S1, S5, S6), #815 two e2e timeout defects, #816 AI panel tools, #817 architect agent extended with duplication review, #812/#813 QA items |
| Agent outputs used | `qa-overview.md` items fixed in #812, #813, #815; `architecture-overview.md` findings added in #817 |
| Repeated manual work with no agent | none new |

| Pipeline item | Result |
| --- | --- |
| `build.yml`, last 30 runs (`gh run list`) | pull_request: 6 success, 6 failure, 6 cancelled; push: 5 success, 6 failure, 1 cancelled |
| Failed `main` pushes | 44939633048, 44942013183, 44943515522, 44947969087, 44948346711 (before #811, e2e timeouts, German Passwords three times) and 44981366585 (new, after #816: Windows job failed, Linux passed; `--log-failed` returned nothing usable, cause unknown) |
| `release.yml`, last 15 runs | 15 success; `winget.yml`: no runs |
| Timing | run 44981366585: Linux job 8.5 min, Windows job 14.5 min; `fixer.md` line 22 says the Windows suite alone takes about 25 minutes |

| Model item | Result |
| --- | --- |
| Agent models | `opus`: architect; `sonnet`: design-reviewer, fixer, integrator, planner, profiler, promoter, qa, supervisor; `haiku`: three researchers, janitor. No evidence of poor output |
| Data model, other | no change in the window; no new projects or workflows |

### Agent audit

| Agent | Verdict | Reason |
| --- | --- | --- |
| `architect` | OK | extended in #817, output used |
| `design-reviewer`, `researcher-*` (3), `planner` | OK | unchanged; planner never run (`Documentation/plans/feature-proposals.md` absent) |
| `fixer` | Revise | `origin/main`, cancelled runs, no suite-wide class, stale project list and 25-minute claim (S2, S3, S7) |
| `integrator` | Revise | standing merge authority claimed in the file (S8) |
| `janitor` | OK | remote note fixed in #814 |
| `profiler`, `qa` | Revise | stale project lists (S7) |
| `promoter`, `supervisor` | OK | colours fixed in #814 |

Colours are now distinct except green (`integrator`, `qa`) and blue (research and plan group), both acceptable.

### Suggestions

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

1. S8: the one place an agent file overrides a stated project rule.
2. S3, S2: `fixer` is used on red runs; run 44981366585 is another red push to triage, and cancelled runs are common.
3. S7: stale lists rot again unless replaced by a pointer; also settle the 25-minute claim (measured Windows job: 14.5 min).
4. S4: removes a defect class at its source.

### Rejected or skipped

- New agents (WinGet, localisation, flake triage, implement-findings): no new evidence since the last review; `winget.yml` has still never run.
- Model changes for `architect`, researchers or `janitor`: no evidence of poor output.
- Judging `planner`: not possible until it has run once.
