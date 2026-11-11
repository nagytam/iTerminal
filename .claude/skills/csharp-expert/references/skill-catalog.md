# Vendored .NET Skill Catalog

The skills in this catalog are vendored in `.claude/skills` from
[`dotnet/skills`](https://github.com/dotnet/skills). Only the C#-relevant subset is present; MAUI,
WinForms, Blazor, ASP.NET Core, template-engine, upgrade and test-migration skills are deliberately
excluded. Use this file to find the exact installed skill name for a request.

## Language, interop and scripting

| Skill | Owns |
|---|---|
| `csharp-expert` | Routing C# and .NET requests, C# language fallback |
| `csharp-refactoring` | Behavior-preserving refactors: rename, extract, inline, merge, split |
| `dotnet-breaking-changes` | Public API, ApiCompat, `InternalsVisibleTo`, multi-target contracts |
| `csharp-scripts` | File-based C# apps run with the .NET CLI |
| `dotnet-pinvoke` | P/Invoke and `LibraryImport`, marshalling, native memory lifetime |
| `vectorization` | SIMD, `TensorPrimitives`, hardware intrinsics |

## Build and packaging

| Skill | Owns |
|---|---|
| `msbuild` | General MSBuild authoring, imports, incremental generation, `.binlog` review |
| `binlog-generation` | Capturing `/bl:` binary logs |
| `binlog-failure-analysis` | Diagnosing failures from an existing binlog |
| `build-parallelism` | Scheduling across projects, `-m` scaling, graph builds |
| `build-perf-baseline` | Cold/warm/incremental build measurements |
| `build-perf-diagnostics` | Bottlenecks from binlog timelines |
| `check-bin-obj-clash` | `OutputPath`/`IntermediateOutputPath` collisions |
| `copy-to-output-directory` | `CopyToOutputDirectory`/`CopyToPublishDirectory` modes |
| `directory-build-organization` | `Directory.Build.*` hierarchy and shared policy |
| `eliminate-redundant-restores` | Repeated NuGet restores in multi-step builds |
| `eval-performance` | Evaluation cost before targets run |
| `extension-points` | Imports and hooks, `CustomBefore`/`CustomAfter`, NuGet auto-imports |
| `find-repeated-work-within-one-build` | Duplicate tasks and batching inside one build |
| `including-generated-files` | Generated artifacts missing from compilation or output |
| `incremental-build` | Up-to-date checks, `Inputs`/`Outputs`, `FileWrites` |
| `item-management` | `Include`/`Remove`/`Update`, metadata, batching, duplicates |
| `msbuild-antipatterns` | Review and audit of project/build files |
| `msbuild-modernization` | Legacy to SDK-style project conversion |
| `property-patterns` | Property conditions, defaults, append versus overwrite |
| `resolve-project-references` | Interpreting `ResolveProjectReferences` timing |
| `target-authoring` | Custom target patterns and dependency chains |
| `troubleshoot-binlog-mcp` | Binlog MCP startup failures |
| `convert-to-cpm` | Central Package Management via `Directory.Packages.props` |
| `nuget-trusted-publishing` | NuGet OIDC trusted publishing on GitHub Actions |
| `setup-local-sdk` | Local, non-system .NET SDK installs and `global.json` |

## Testing

| Skill | Owns |
|---|---|
| `code-testing` | Writing, adding, repairing, or strengthening tests |
| `code-testing-agent` | Legacy alias that forwards to `code-testing` |
| `code-testing-extensions` | Language-specific test authoring guidance |
| `assertion-quality` | Assertion depth, variety, and false confidence |
| `coverage-analysis` | Interpreting and collecting .NET coverage |
| `crap-score` | CRAP score for a named method, class, or file |
| `detect-static-dependencies` | Finding ambient/static C# dependencies |
| `filter-syntax` | Test filter syntax |
| `find-untested-sources` | Pairing sources with tests |
| `generate-testability-wrappers` | New seams for untestable static dependencies |
| `grade-tests` | Grading individual tests A-F |
| `migrate-static-to-wrapper` | Migrating static calls to an injected abstraction |
| `mtp-hot-reload` | Microsoft Testing Platform hot reload |
| `platform-detection` | Test platform, framework, and runner detection |
| `run-tests` | Running .NET tests, filters, TRX, coverage flags |
| `scaffold-dotnet-test-project` | Creating or repairing test projects and solution wiring |
| `test-analysis-extensions` | Language-specific test analysis guidance |
| `test-anti-patterns` | Severity-ranked test audits |
| `test-gap-analysis` | Behavioral blind spots and pseudo-mutation analysis |
| `test-smell-detection` | Academic test-smell taxonomy |
| `test-tagging` | Classifying tests by traits and distribution |
| `test-value-evidence` | Evidence that a change is tested |
| `testability-obstacle` | Smallest production seam for deterministic tests |
| `writing-mstest-tests` | Fixing existing MSTest tests and configuration |

## Diagnostics and performance

| Skill | Owns |
|---|---|
| `analyzing-dotnet-performance` | Static scan for performance anti-patterns |
| `dotnet-trace-collect` | Choosing and collecting traces from running processes |
| `dump-collect` | Crash dump configuration and collection |
| `microbenchmarking` | BenchmarkDotNet design, setup, and comparison |
| `clr-activation-debugging` | .NET Framework CLR activation logs |
| `android-tombstone-symbolication` | Symbolicating .NET Android tombstones |
| `apple-crash-symbolication` | Symbolicating .NET Apple `.ips` crash logs |

## .NET 11

| Skill | Owns |
|---|---|
| `process-api-net11` | `Process.Run`, `RunAndCaptureText`, `StartAndForget`, `KillOnParentExit` |
| `system-text-json-net11` | `JsonNamingPolicy.PascalCase`, generic `GetTypeInfo` accessors |
