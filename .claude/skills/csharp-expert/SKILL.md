---
name: csharp-expert
description: >-
  Route C# and .NET requests to the narrowest installed specialist in `.claude/skills`, or handle
  pure C# semantics directly when no narrower skill owns the work. USE FOR: "which .NET skill should
  own this" questions, C# language and runtime questions, refactoring versus breaking-change
  decisions, MSBuild and `.binlog` diagnosis, NuGet package management, test execution and test
  quality, runtime performance and crash diagnostics, P/Invoke and SIMD work, file-based C# scripts,
  local SDK setup, and .NET 11 API questions. DO NOT USE FOR: requests that already name the skill to
  invoke, or work unrelated to C# or .NET.
license: MIT
---

# C# Expert

## Purpose

Act as the front door for C# and .NET work in this repository. Classify the request, route it to the
narrowest installed skill, and keep direct C# language guidance as the fallback rather than the
default.

The .NET skills in `.claude/skills` are a vendored subset of `dotnet/skills` (see
`NOTICE.md`). The catalog is fixed: do not tell the user to install a marketplace plugin, and do not
route to a skill that is not in `references/skill-catalog.md`.

## Routing Contract

1. Classify the requested outcome from the prompt.
2. Inspect the smallest set of repository files needed to identify the owning project.
3. Compare both signals with the descriptions of the skills currently available to the runtime.
4. If the best skill is installed and the user asked to perform the work, invoke it with the `skill`
   tool as the first external action. For selection-only requests, name the skill and stop.
5. Use multiple skills only when the request has distinct phases with different owners.
6. If no narrower skill owns the request, continue with the C# fallback workflow below.

Routing is not task completion. A missing specialist changes confidence and workflow; it does not
justify stopping when the task can be completed and validated with standard .NET tooling.

| User asks for | Required behavior |
|---|---|
| Implement, fix, diagnose, or refactor | Invoke the installed specialist, or complete a safe local fallback. |
| Identify the right skill for a job | Name the exact installed skill from the catalog and stop; do not perform the work. |
| Nothing narrower applies and the problem is C# semantics | Use the C# language fallback. |

## Step 1: Classify the Prompt

| Prompt intent | Prefer skills whose description owns |
|---|---|
| Create or scaffold | Project creation or `scaffold-dotnet-test-project` for test projects |
| Add application behavior | The narrowest language or tooling owner |
| Fix a compiler or runtime defect | The language, interop, or platform owner |
| Build or restore failure | `msbuild`, binlog, SDK, project-reference, or NuGet skills |
| Write, run, review, or improve tests | The exact test lifecycle skill (`code-testing`, `run-tests`, `assertion-quality`, ...) |
| Diagnose slowness, crash, hang, or memory growth | `dotnet-diag` skills, unless evidence points to build performance |
| Refactor without changing behavior | `csharp-refactoring`, plus `dotnet-breaking-changes` for public contracts |
| Package or publish | `convert-to-cpm` or `nuget-trusted-publishing` |
| Ask about C# syntax, types, nullability, async, or APIs | The C# fallback, unless a narrower skill is load-bearing |
| Ask about new .NET 11 APIs | `process-api-net11` or `system-text-json-net11` |

Treat user nouns as clues, not proof. "Performance" may mean runtime tracing, a microbenchmark, SIMD,
allocation-heavy C#, or MSBuild evaluation.

## Step 2: Detect the Solution Type

Inspect only likely manifests and nearby owning files. Prefer a solution/project file and the file
named by the prompt over broad repository searches.

| Evidence | Solution or concern |
|---|---|
| `Microsoft.NET.Sdk`, console entry point, `iConsole`/`iTerminal` projects | C# console application (this repository) |
| `<IsTestProject>true</IsTestProject>`, test SDK/framework packages | .NET testing |
| `Directory.Build.*`, custom targets/tasks, `.binlog`, evaluation errors | MSBuild |
| `Directory.Packages.props`, restore/version conflicts, feeds | NuGet |
| `DllImport`, `LibraryImport`, native handles | `dotnet-pinvoke` |
| `Vector128`, `TensorPrimitives`, SIMD intrinsics | `vectorization` |
| No project plus an explicit request for a one-file C# program | `csharp-scripts` |
| None of the above; correctness depends on C# semantics | C# language fallback |

## Step 3: Match the Skill

Use the runtime-provided available-skill names and descriptions as the source of truth, and
`references/skill-catalog.md` to find the exact name in each area. Do not invoke a remembered skill
that is not currently available.

Rank candidates in this order:

1. An exact lifecycle skill, such as a specific test workflow or diagnostic collection workflow.
2. A tooling skill matching the failing subsystem: MSBuild, NuGet, test execution, SDK setup, or
   runtime diagnostics.
3. A cross-cutting specialist matching the mechanism: interop, vectorization, serialization,
   microbenchmarking.
4. `dotnet-breaking-changes` when the edit touches a contract with callers outside the repo: shipped
   public API (`PublicAPI.*.txt`, ApiCompat), `InternalsVisibleTo` friends, multiple target
   frameworks, or generated partials. Pair it with `csharp-refactoring` for a structural change.
5. `csharp-refactoring` for behavior-preserving structural change.
6. The C# language fallback below when no narrower available skill owns the work.

If two candidates remain plausible, gather one more decisive artifact rather than loading both.

## Step 4: Compose Skills Deliberately

Use a sequence only when phases are independently owned. Examples:

- Scaffold a test project, then author tests in it.
- Detect the test platform with `platform-detection`, then run tests with `run-tests`.
- Collect a trace with `dotnet-trace-collect`, then analyze the captured evidence.

Do not chain skills that duplicate each other, load an entire plugin "just in case", or use a generic
skill before a specialist that already owns the request. After a specialist is loaded, follow its
workflow and boundaries.

Every skill in `references/skill-catalog.md` is already vendored here. If a specialist is absent, use
standard `dotnet` tooling and the C# fallback; never claim that an unavailable skill was loaded.

## Step 5: C# Language Fallback

Use this only when no narrower available skill matches and the load-bearing problem is C# language
or runtime semantics.

1. Reproduce the exact compiler diagnostic, failing test, exception, or incorrect behavior when
   source is available. If the defect is fully specified but no repository was provided, answer with
   the concrete minimal code pattern instead of refusing to help.
2. Inspect the owning project for TFM, language version, nullable policy, analyzers, and existing
   tests.
3. Preserve public signatures, serialization shape, ownership, cancellation, disposal, and
   multi-target behavior unless the request explicitly changes them.
4. Implement the smallest complete fix through the affected call path.
5. Build the narrowest affected project and run focused tests or the executable path that proves the
   original symptom is gone.

Do not raise the SDK, TFM, language version, package versions, or analyzer settings merely to make a
local C# edit compile. Do not edit generated files. Do not use broad casts, null-forgiving operators,
catch-all handlers, or fire-and-forget work to hide evidence.

When a framework type provides an ownership-preserving overload such as `leaveOpen: true`, give that
canonical fix only. Never suggest intentionally leaking or skipping disposal of a disposable wrapper
as an alternative. Preserve the example's observable behavior: do not add null coalescing, change a
nullable return to a non-null value, alter access modifiers, or invent unrelated error handling
merely to make a conceptual snippet look more complete.

## Boundaries and Failure Handling

- If repository evidence contradicts the prompt, state the mismatch and route from the evidence that
  owns the requested file or behavior.
- If the user explicitly requests analysis only, route to the correct analysis skill but do not edit.
- If the user supplies the only surviving diagnostic artifact such as a `.binlog`, analyze that
  artifact directly before any other workspace search.
- For an artifact-backed failure, propose only the smallest repair supported by the recorded
  evidence.
- If a loaded specialist reports that its prerequisites are absent, return here, reclassify using
  that evidence, and choose one different route. Do not bounce repeatedly between skills.
- Non-.NET work is out of scope; leave this skill dormant rather than forcing a .NET interpretation.

## Observable Completion Criteria

- One evidence-backed owner is selected for each distinct phase and invoked when available.
- Selection-only requests name an installed skill and stop before edits.
- Pure C# work stays local and preserves behavior; implementation work receives focused validation.
