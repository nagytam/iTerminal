---
name: fix-main
description: Fix a failing build or test pipeline on main for iTerminal, in four gated phases (triage the CI run, reproduce it, fix on a branch and verify on CI, integrate by PR). Use when the "Build and test" or "Release builds" workflow fails on main, or when the user asks to repair main's build.
---

# Fix main workflow

One failing pipeline, one branch, one PR. The fix never goes straight to `main`, and it is never a
disabled test. Four phases run in order, and **every phase ends in a review gate**. A gate passes and
the next phase starts, or it fails and the phase is redone with the findings. Nothing moves forward on a
failed or skipped gate.

Read `CLAUDE.md` and the `docs/architecture/*.md` that cover the failing area before phase 1. Those rules
override anything below.

## Non-interactive

This workflow asks exactly one question, and only once: right after triage, when it has found an error. It is
asked with `AskUserQuestion`, using this text with the error filled in:

> Would you like to fix {error description}?

Options: **Yes, fix it** and **No, only report**. Nothing else is asked, and nothing waits for a reply after the
answer. If no error is found (main is green, or the failure is a flaky test that passed on rerun), the run
reports that and asks nothing.

After a **Yes**, every decision is made by the rules below. When a rule says to stop, the run ends at once and its
final output is the report: the phase, the gate findings, the evidence, and what would unblock it. After a **No**,
the run ends after triage with the same report, and changes nothing.

The invocation may contain one argument:

- `merge`: after the integration gate passes, squash-merge the pull request. Without it, the run ends with the
  pull request open and green, and merging is left to the user.

## What runs on main

`.github/workflows/build.yml` runs on every push to `main` and on every pull request:

- **linux** (`ubuntu-latest`): restore, build `iConsole.Test`, `iTerminal.Core.Test` and `iTerminal`, then run the
  unit tests of those two test projects with coverage. It does **not** run the end-to-end suite.
- **windows** (`windows-latest`): `dotnet build iTerminal.slnx -c Release -m:1`, then the full test run,
  including `iTerminal.Test` (the end-to-end suite, about 4 minutes).

`release.yml` runs on a merged pull request into `main`. It builds the MSI and the Linux packages, so a broken
packaging step shows there, not in `build.yml`.

Some failures only happen on one OS. A Windows-only panel test that fails on the Windows runner is a real
failure on `main`, even when the Linux job is green.

## Working record

Keep the record in the conversation and in the PR body. For each phase write:

- **Output**: what the phase produced (the classification, the reproduction, the diff summary, the CI run link).
- **Gate**: the reviewer, the verdict (`PASS` or `FAIL`) and the findings.
- **Loops**: how many times the phase was redone, and why.

## Reviewers

A gate is reviewed by a fresh subagent that gets only the artifact and the checklist. The author does not review
their own phase.

- Triage and verification gates: a `general-purpose` agent, given the evidence and the checklist.
- Fix gate: the `code-review` skill at `medium` (`high` if the fix touches key handling, threads or persistence).
  Do not pass `--fix`; the author applies fixes so the review stays independent.
- Integration gate: the pull request's own CI run, read from `gh`, and the `merge` argument. The run never
  approves its own merge without the argument.

If a reviewer cannot run, the run stops at that gate and reports it. It does not pass it.

## Phase 1: Triage

**Output.** A classification of the failure, backed by quotes from the log.

1. Find the first red run on `main` and the last green one:
   `gh run list --branch main --limit 15 --json databaseId,conclusion,headSha,displayTitle`.
   Note the commit that broke it (the first red `headSha`).
2. Read the failing lines: `gh run view <id> --log-failed`. Record the job, the step, the test names and the
   error text. Do not work from the run title.
3. Classify it, with one class and the evidence for it:
   - **Build break**: the compile step fails. The fix is in the code the commit changed.
   - **Test regression**: a test that passed before the commit fails, and reproduces locally or on CI.
   - **Platform-only**: the test fails on one runner only (Windows, or Linux). Name the runner.
   - **Flaky**: the same test passed on the same commit in another run. Check the history:
     `gh run list --workflow build.yml --json conclusion,headSha` for the same test name.
   - **Infrastructure**: a timeout, a runner or download failure, a missing tool. Re-run once with
     `gh run rerun <id> --failed`. A second failure with the same cause is not infrastructure.
4. Name the commit or the test, and who owns the area if the log or `git log` shows it.
5. **Ask the one question**, once the triage gate below has passed: "Would you like to fix {error description}?", with
   the error description written as one line (the failing test or step, its runner, and the class). Wait for the answer. On **No**, end the run with the
   triage report. On **Yes**, continue to phase 2 without asking anything else.

**Gate: triage review.** The reviewer checks, and the phase passes only if all hold:

- The class is supported by quoted log lines, not by a guess.
- The breaking commit is identified, or the reason it cannot be is stated.
- A flaky claim has a second run or history behind it.
- The reviewer can name the runner and the test filter that would reproduce it.

On `FAIL`, return to the evidence and classify again.

## Phase 2: Reproduce

**Output.** The failure reproduced, or the reason it cannot be, with the exact command used.

1. Use the same commands as CI, in `Release`:
   - Build: `dotnet build iTerminal.slnx -c Release -m:1` (Windows job) or the Linux build steps of `build.yml`.
   - Test: `dotnet test <project> -c Release --filter "FullyQualifiedName~<TestName>"` first, then the full project.
2. Platform-only failures that cannot run on this machine: run the failing test on its runner. Push the branch
   from phase 3 and let the pull request's CI run it, or start `build.yml` with `gh workflow run build.yml --ref <branch>`.
   Do not declare the failure fixed on the Linux machine alone.
3. The end-to-end suite runs for about 4 minutes. Run the failing class first, then the whole suite. Do not run
   tests against the real machine: the end-to-end tests use a temp `DataDirectory`, and any test that touches
   the real machine needs its `SandboxServices` replacement first (`CLAUDE.md`).
4. If the failure does not reproduce in three runs, stop at this phase and report the three outcomes. Do not
   guess a fix for a failure nobody can see.

**Gate: reproduction review.** The reviewer checks that the failure appears with the command recorded, or that
the reason it does not is written down.

## Phase 3: Fix and verify

**Output.** A branch cut from the latest `main` (`git fetch` first), named `fix/main-<short-sha>`, with the fix and
its test, and a CI run on the pull request that is green.

Rules while fixing:

- Fix the cause. A failing assertion is not made to pass by weakening it, by a sleep, or by deleting the test.
- A test that failed on `main` must be shown to fail without the fix and to pass with it.
- A flaky test is fixed where it is flaky (the wait condition, the shared state, the timing budget). Quarantining
  a test (`[Ignore]`, a skip filter, removing it from CI) is a stop: the run reports it and does not do it.
- Follow the code style in `CLAUDE.md`. Match the line endings of the file being edited.
- Build with `dotnet build iTerminal.slnx -c Release` before the commit, and run the tests from phase 2.

Then:

1. Commit only after the fix gate passes. Push the branch and open a **draft** pull request against `main`
   (`gh pr create --draft --base main`) **before the local build and test runs start**; fixes made after that go to
   the same branch. CI runs on the pull request, so the platform-only failure is checked on its own runner.
2. Watch the pull request's `Build and test` run to the end. A green Linux job with a red Windows job is red.

**Gate: fix review.** Run the `code-review` skill against the working tree at `medium`. The gate passes when there
are no `CONFIRMED` correctness findings, every `PLAUSIBLE` one has been checked, and the reviewer also confirms:

- The diff is the cause and its test, nothing else.
- No test was skipped, deleted or quarantined in the change.
- The failing test fails without the fix, and passes with it.
- The `Build and test` run on the pull request is green on both runners, or each red job's failure is named
  and is not caused by this change.

On `FAIL`, fix the findings and run the gate again.

## Phase 4: Integrate into main

**Output.** The draft pull request from phase 3, marked ready (`gh pr ready`) once its gates pass, with the working
record in its body. The body names the failing run,
its breaking commit, the classification, the fix and the CI run that proves it.

The commit message and the PR body end with the attribution lines the session gives.

**Gate: integration review.** Read the pull request's `Build and test` run to the end (`gh pr checks <number> --watch`).
The gate passes when both runners are green and the working record is complete. Then:

- With the `merge` argument: `gh pr merge <number> --squash`, then check that the next `build.yml` run on `main` is
  green (`gh run list --branch main --limit 3`). A red run after the merge is reported at once, with the commit.
- Without it: the run ends with the pull request open and its CI green, and the final output says so.

The run does not wait for anyone to press a button.

## Stopping rules

- A gate that cannot be run is a stop, not a pass.
- The failure does not reproduce in three runs: stop at phase 2 and report the outcomes.
- The only fix is to quarantine or delete a test: stop and report it. The run does not quarantine.
- Two failed gates for the same phase: stop and report both sets of findings.
- The failure is in code another open PR is changing: stop and report before fixing, so the two do not collide.
- Never push to `main` directly, and never force-push it.
- A stop is a normal end of the run. The final output is the report described under "Non-interactive".
