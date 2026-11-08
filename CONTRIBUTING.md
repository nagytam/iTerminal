# Contributing to iTerminal

Thank you for wanting to help. iTerminal is a beta, so bug reports, fixes, new drives, file
viewers, translations and documentation are all welcome. This page tells you how to get a change
from your machine into `main`.

By contributing you agree that your work is released under the project's [MIT License](LICENSE).

## Ways to help

| Contribution | Where to look | Notes |
| --- | --- | --- |
| Report a bug | GitHub Issues | Include OS, terminal emulator, iTerminal version and steps. `ITERMINAL_KEY_TRACE=1` logs every key received, which helps with keyboard problems. |
| Fix a bug | Any project | Add a test that fails without your fix. |
| Translate | `iConsole/Localization/Resources/Strings.*.resx` | A new string needs a key in all six files (neutral English, de, fr, es, it, hu). |
| Add a file reader to the F3 viewer | `iConsole/Services/`, `iTerminal.Core` | Readers take bytes rather than a path, so they are small and easy to unit-test. |
| Add a drive (URL scheme) | `iTerminal.Core` | See "A new URL scheme" in [CLAUDE.md](CLAUDE.md) for the full checklist. |
| Improve terminal support | `iConsole.Inputs.Windows`, `iConsole.Inputs.Posix` | Try WezTerm, Kitty, tmux, Windows Terminal and SSH sessions. |
| Docs and tests | `docs/`, `*.Test` | Always useful. |

Not sure where to start? Look for issues labelled `good first issue` or `help wanted`. For a big
change (a new subsystem, a change to the UI framework), open an issue first so we can agree on the
approach before you spend time on it.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Optional tools (`git`, `docker`,
`ffmpeg`, 7-Zip, Tesseract) are only needed for the features that use them.

```bash
git clone https://github.com/nagytam/iTerminal.git
cd iTerminal
dotnet build iTerminal.slnx
dotnet run --project iTerminal/iTerminal.csproj
```

The app keeps its state (settings, caches, vault) in `%APPDATA%/iTerminal/` on Windows. Set the
`DataDirectory` configuration key to point it at a throwaway folder while you experiment.

## Project layout

```
iConsole  <-  iTerminal.Core  <-  iTerminal
```

- `iConsole`: reusable text-UI framework (controls, commands, jobs, rendering). Reusable primitives belong here.
- `iTerminal.Core`: file-manager behaviour, drives, dialogs, services. App behaviour belongs here.
- `iTerminal`: the executable and composition root. Register new services and controls in the
  `iConsoleApplicationBuilderExtension` / `iTerminalApplicationBuilderExtension` classes, not in `Program.cs`.
- `iConsole.Inputs.Windows`, `iConsole.Inputs.Posix`: platform input plug-ins.

Read the relevant write-up in [docs/architecture/](docs/architecture/) before changing a subsystem.
[CLAUDE.md](CLAUDE.md) has the list of rules that are easy to break. The important ones:

- **Key handlers and `Draw` must return within about 200 ms.** Never run processes, HTTP or other
  blocking I/O there. Use the async services on a background task and write the result back when it lands.
- **A control paints its whole rectangle every time it draws.** Layers are never cleared between frames.
- **Do not touch the control tree from a worker thread.**
- **Global hot keys** are almost always function keys. No Alt+letter hot keys.
- **File operations** act on `FocusedEntry.ActionableUrl(s)`, never on the `..` row or a container row.
  Build child paths with `UrlPath.Child`.
- **Credentials** come from the vault or password manager, never from a URL.
- **Tests must not touch the real machine.** Anything that reaches outside the sandbox needs a
  replacement in `SandboxServices` before an end-to-end test can use it.

## Making a change

1. **Branch from `main`.** One branch and one pull request per improvement. Keep the change focused.
2. **Write the code** following the style below.
3. **Add or update tests** next to the code you changed (test folders mirror source folders).
4. **Update the docs** under `docs/architecture/` when behaviour, keys or settings change, and the
   README feature list when a user-visible feature is added.
5. **Run the checks** (below) and fix what fails.
6. **Open a pull request** against `main` and fill in the template. Explain what changed and why,
   and how you tested it. Screenshots or terminal recordings help for UI changes.

A maintainer reviews every pull request. Expect questions and requests for changes; they are about
the code, not you. Merging into `main` waits for a maintainer's approval.

### Code style

The root `.editorconfig` and `.github/copilot-instructions.md` are the source of truth. In short:

- 4 spaces, CRLF line endings, braces on new lines, accessibility modifiers on non-interface members.
- Explicit types instead of `var` unless the type is obvious from the right-hand side.
- File-scoped namespaces; primary constructors for small command and service classes.
- Constructor injection instead of ad-hoc construction.
- Logging in `iTerminal.Core` goes through `Serilog.ILogger`.
- Async first: do not turn an existing async flow into a blocking call.

### Tests

MSTest with Moq. Assert on what a control drew using `ScreenBuffer`, and draw twice to catch stale
text. Details are in [docs/architecture/testing.md](docs/architecture/testing.md).

```bash
# Everything except the slow end-to-end suite
dotnet test iConsole.Test/iConsole.Test.csproj
dotnet test iTerminal.Core.Test/iTerminal.Core.Test.csproj

# One test by name
dotnet test iTerminal.Core.Test/iTerminal.Core.Test.csproj --filter "FullyQualifiedName~FilePanelRefreshTask"

# The end-to-end suite: the whole app on a virtual terminal (about 4-5 minutes)
dotnet test iTerminal.Test/iTerminal.Test.csproj
```

Before you open a pull request, `dotnet build iTerminal.slnx` must succeed and the tests for the
projects you touched must pass. Run the end-to-end suite if you changed dialogs, key handling or
anything that boots the app. Every public type needs a constructor the container can resolve
(`InstantiateAllTypesTest` checks this).

Performance budgets: a key handler under 200 ms, a content load under 3,000 ms.

## Commit and pull request conventions

- Write commit messages in the imperative mood with a short subject line ("Open WIM images as archives").
- Keep unrelated changes, mass reformatting and generated files out of the pull request.
- Link the issue your change fixes ("Fixes #123").
- Do not commit secrets, personal data, local database files or build output.

## Using AI tools

AI assistance is welcome here; this project is itself developed with it. You are responsible for
every line you submit:

- Read, understand and test the change before you open the pull request.
- Say in the description if an AI tool wrote a significant part of it.
- Do not submit large, unreviewed generated changes or invented references and APIs.

Pull requests that look unreviewed may be closed without a detailed review.

## Reporting security problems

Please do not open a public issue for a vulnerability. Follow [SECURITY.md](SECURITY.md).

## Conduct

Be kind and assume good faith. Harassment, personal attacks and discrimination are not tolerated.
Maintainers may edit, lock or remove comments and contributions that break this rule.

## Supporting the project

You can also help by sponsoring the AI budget and maintainer time behind iTerminal.
See [FUNDING.md](FUNDING.md).
