---
name: incremental-improvement
description: Deliver one small improvement to iTerminal in four gated phases (plan, implement, test, integrate into main), each ending in a review that must pass before the next phase starts. Use for any incremental feature, fix or refactor that should land as a reviewed PR.
---

# Incremental improvement workflow

One improvement, one branch, one PR. Four phases run in order. **Every phase ends in a review gate.** A gate
either passes, and the next phase starts, or fails, and the phase is redone with the findings. Nothing moves
forward on a failed or skipped gate.

Read `CLAUDE.md` and the relevant `docs/architecture/*.md` before phase 1. The rules there override anything below.

## Working record

Keep the record in the conversation, and in the PR body at the end. For each phase write:

- **Output**: what the phase produced (the plan text, the diff summary, the test results, the PR link).
- **Gate**: the reviewer, the verdict (`PASS` or `FAIL`) and the findings that were raised.
- **Loops**: how many times the phase was redone, and why.

## Reviewers

A gate is reviewed by someone other than the author of the phase. Use a fresh subagent for the review, with
only the artifact and the checklist: it should not inherit the author's reasoning.

- Planning and testing gates: a `general-purpose` agent, given the artifact and the gate checklist.
- Implementation gate: the `code-review` skill at `medium` (or `high` if the change touches key handling, threads
  or persistence). Do not pass `--fix`; the author applies the fixes so the review stays independent.
- Integration gate: the user. Merging is outward-facing and hard to reverse, so it is never self-approved.

If a reviewer cannot be run (no subagent, no network for `gh`), say so and stop at that gate. Do not pass it.

## Phase 1: Plan

**Output.** A short plan written to the working record:

1. The goal, in one sentence, and what is out of scope.
2. The files and subsystems that will change, each with the doc in `docs/architecture/` that governs it.
3. The tests that will prove it: unit tests (`iConsole.Test`, `iTerminal.Core.Test`) and, if the change is a key or
   a panel behaviour, an end-to-end test in `iTerminal.Test`.
4. The risks: threading (the 200 ms budget, `IsCurrentUrl()`), terminal ownership, sandbox services a new
   service would need, and any persisted setting (no `__`-prefixed keys in `Settings`).
5. The choices made without asking, and the reason for each.

**Gate: plan review.** The reviewer checks, and the plan passes only if all hold:

- The scope is one improvement; nothing in it could be split into a separate PR without loss.
- Every file in the plan is one the change needs, and nothing the change does not need is in the plan.
- The governing architecture doc was read, and the plan follows its rules.
- The tests can fail for the reason the change is needed, not only for a symptom.
- Every choice that was made alone is stated, so the user can redirect it.

On `FAIL`, revise the plan and review it again. Do not start the implementation on a failed plan.

## Phase 2: Implement

**Output.** A branch cut from the latest `main` (`git fetch` first), with the change and nothing else. The
commit is made only at the end of the phase, after the gate passes.

Rules while implementing:

- Follow the code style in `CLAUDE.md` (explicit types, file-scoped namespaces, `Serilog` for logging in
  `iTerminal.Core`). Match the line endings of the file being edited.
- Keep to the plan. A change the plan did not foresee goes back to phase 1 as a plan amendment, and is reviewed.
- Build with `dotnet build iTerminal.slnx`. A build that fails is not a finished phase.
- Add the tests from the plan with the change, not after it.

**Gate: implementation review.** Run the `code-review` skill against the working tree at `medium`. The gate passes
when there are no `CONFIRMED` correctness findings and every `PLAUSIBLE` one has been checked and either fixed or
explained. The reviewer also checks:

- The diff matches the plan. Anything extra is removed or justified.
- No new blocking I/O on a key handler or a render path; background writes are guarded by `IsCurrentUrl()`.
- No new service acts on the real machine without a sandbox replacement.
- New user-visible strings are in all six `.resx` files, if the change adds any.

On `FAIL`, fix the findings in the working tree, then run the gate again. Commit once it passes.

Then, **before any test run starts**, push the branch with `git push -u github <branch>` and open a **draft** pull
request with `gh pr create --draft --base main`. The body holds the working record so far (plan, implementation
gate) and an unticked test plan. Bind it with the `ccd_pr` tools. The draft makes the work visible and lets CI start
on the pushed commit while the local runs go. Fixes made in phase 3 are committed and pushed to the same branch.

## Phase 3: Test

Starts only once the draft pull request exists. **Output.** Test results, pasted as summaries, for:

1. `dotnet test` on each test project the change touches, and the full `iConsole.Test` and `iTerminal.Core.Test`
   projects, not only a filter, to catch regressions.
2. `dotnet test iTerminal.Test/iTerminal.Test.csproj` (the end-to-end suite, about 4–5 minutes) whenever the change
   touches keys, panels, dialogs or file operations. Run the filtered class first, then the full suite.
3. Any failure that is already present on `main`. Prove it: stash the change, run the same test on `main`, restore.
   A failure that predates the change is reported as such, not hidden and not fixed silently in this PR.

**Gate: test review.** The reviewer checks, and the phase passes only if all hold:

- Every test named in the plan ran and passed, and the new tests fail when the change is reverted.
- No test was skipped, deleted or weakened to make the run pass.
- Each failure that remains is shown to exist on `main` too, or is fixed.
- The test run did not touch the real machine (`DataDirectory` points at a temp folder; launchers are sandboxed).

On `FAIL`, return to phase 2 for the fix, then re-run the tests from step 1.

## Phase 4: Integrate into main

**Output.** The draft pull request from phase 2, updated: the working record in its body complete, the test plan
ticked and the remaining failures named. Push any last commits, edit the body with `gh pr edit`, then mark it ready
with `gh pr ready`. The commit message and PR body end with the attribution lines the session gives.

**Gate: integration review.** The user reviews the PR and the working record, and decides. The workflow waits here.
It does not merge on its own. Merge only when the user says so, with `gh pr merge --squash` unless they ask
otherwise. Afterwards, update local `main` (`git fetch github main:main`) and delete the branch if the user asks.

On changes requested, return to the phase they concern and run its gate again, then re-open the gate here.

## Stopping rules

- A gate that cannot be run is a stop, not a pass.
- Two failed gates for the same phase: stop and report to the user with both sets of findings.
- A plan that needs a decision only the user can make: stop at phase 1 and ask.
- Never skip a gate because the change looks small. A one-line fix still goes through all four gates.
