# Copilot instructions for iTerminal

## Architecture
- Treat the solution as three layers: `iConsole` provides reusable terminal UI infrastructure, `iTerminal.Core` adds file-manager and terminal behavior on top of it, and `iTerminal` is the composition root.
- Keep changes inside the correct layer. Reusable console primitives belong in `iConsole`; app-specific behavior belongs in `iTerminal.Core`; startup wiring belongs in `iTerminal/Program.cs` and DI extension methods.
- Prefer extending the existing dependency-injection setup in `iConsole.Application.iConsoleApplicationBuilderExtension` and `iTerminal.Core.Application.iTerminalApplicationBuilderExtension` instead of adding ad-hoc construction logic.

## C# and project conventions
- Target modern .NET (`net10.0`) and keep nullable reference types enabled.
- Follow the root `.editorconfig`: 4 spaces, CRLF line endings, braces on new lines, accessibility modifiers on non-interface members, and PascalCase for types and members.
- Prefer explicit types over `var` unless the type is obvious from the right-hand side.
- Match the existing style in the touched file. The codebase commonly uses file-scoped namespaces, top-level statements for the app entry point, and primary constructors for small command classes.
- Keep changes minimal and focused. Reuse existing helpers, services, models, and controls before introducing new abstractions.

## Implementation patterns
- Favor constructor injection for collaborators and register new services through the existing DI extension methods.
- Many user actions are implemented as classes deriving from `iConsole.Commands.Command`; when adding similar behavior, override `Key`, `CanExecute()` when needed, and `ExecuteAsync(object? args)`.
- Preserve the async-first APIs already used across commands, jobs, and tasks. Do not replace asynchronous flows with blocking calls.
- Use existing domain types such as `Url`, content models, dialogs, jobs, and task abstractions instead of duplicating file-system or UI logic.
- Logging in `iTerminal.Core` uses `Serilog.ILogger`; prefer the existing logging abstractions where logging is needed.

## Testing
- Use MSTest for new tests (`[TestClass]`, `[TestMethod]`).
- Use `Moq` for collaborators and keep tests close to the existing style in `iConsole.Test` and `iTerminal.Core.Test`.
- Cover the observable behavior of commands, services, and models with focused unit tests rather than broad integration-only coverage.
 - Target all assemblies (iConsole, iTerminal.Core, iConsole.Inputs.* and iTerminal) when generating tests.
 - Add full per-class unit tests where possible; if a class depends on services, prefer small unit tests with Moq fakes and isolated assertions.

## Change guidance
- Do not introduce unrelated cleanup while making a requested change.
- Avoid new dependencies unless the repository already lacks the required capability.
- Prefer small, composable additions that fit the existing naming and folder structure.
