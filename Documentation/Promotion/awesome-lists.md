# Awesome-list entries

**Venues:** rothgar/awesome-tuis (TUI applications), awesome-dotnet (quozd/awesome-dotnet), and the
awesome-cli-apps family (tuxarch/cli-apps, whose newer forks keep the data in CSV files under `data/`).

**Rules to check before opening a pull request**
- **rothgar/awesome-tuis (verified from its `.github/pull_request_template.md`, 2026-11-21):** the repository must be at least 6 months old; the application must be unique; the interface must be unique (no wrappers such as `fzf`); the interface must be interactive or redraw its output (output formatters do not count as TUIs). A workflow, `readme_pr_repo_first_commit.yml`, checks the repository's first-commit date on every PR that changes `README.md`. iTerminal's first commit is 2026-10-15, so it is **not eligible before 2027-04-15**.
- One link per pull request, with the title `Add iTerminal` (awesome-dotnet asks for this; the other lists use the same norm). *Partly verified* from the repository guide as seen in search results; check each `CONTRIBUTING.md` before sending.
- agarrharr/awesome-cli-apps (**verified 2026-11-21** from <https://raw.githubusercontent.com/agarrharr/awesome-cli-apps/master/contributing.md>): the app must be more than three months old and, on GitHub, have more than 20 stars; one pull request per app titled `Add APP_NAME`; use the PR template or the PR is closed unread; **AI-generated PRs are not welcome** (explain in your own words why the app is awesome). iTerminal has 1 star and its first commit is 2026-10-15, so it is **not eligible** now (age rule met from 2027-01-15; stars not met). Do not open this PR yet.
- toolleeo/awesome-cli-apps-in-a-csv (**verified 2026-11-21** from <https://raw.githubusercontent.com/toolleeo/awesome-cli-apps-in-a-csv/master/CONTRIBUTING.md>): no age or star rule; edit `data/apps.csv` only, not the README; fields `name`, `homepage`, `git` (clonable URL), `description`; check for duplicates first. Whether `tuxarch/cli-apps` is a fork with the same rules is **unverified**.
- awesome-dotnet (**verified 2026-11-21** from <https://raw.githubusercontent.com/quozd/awesome-dotnet/master/CONTRIBUTING.md>): entry format `[NAME](LINK) - DESCRIPTION`; one link per PR; the PR description says what the project is and why it belongs; projects must be actively maintained, stable, documented and tested; `**[Free for OSS]**` is for a commercial project that has a free OSS edition, so it does **not** apply to iTerminal and is dropped below. No PR title format or ordering rule is stated there.
- Entries go into the matching category, in alphabetical order where the list asks for it.
- Do not open the pull request until the release link exists, because a list entry should point to something people can install.

## 1. rothgar/awesome-tuis

**Status: blocked until 2027-04-15** by the six-month age rule above. Do not open the pull request before then.

Category: File managers (or the closest category in the list's current table of contents).

```markdown
- [iTerminal](https://github.com/nagytam/iTerminal) - Two-panel, keyboard-driven file manager and developer toolbox; Git revisions, Docker containers, SSH hosts and archives open as folders. C# / .NET 11, MIT.
```

**Pull request title:** `Add iTerminal`

**Pull request body**

```markdown
Adds iTerminal, an MIT-licensed, two-panel file manager and developer toolbox for the terminal
(Windows and Linux, C# on .NET 11).

- Project: https://github.com/nagytam/iTerminal
- Release: https://github.com/nagytam/iTerminal/releases/latest
- Status: beta (stated in the README)
- Terminal UI built on iConsole, a reusable text-UI library in the same repository

Thanks for maintaining the list.
```

## 2. awesome-dotnet (quozd/awesome-dotnet)

**Status: submitted** as [quozd/awesome-dotnet#1606](https://github.com/quozd/awesome-dotnet/pull/1606) (2026-11-21), from the branch `add-iterminal-cli` in the `nagytam/awesome-dotnet` fork. The `awesome-bot` link check had not reported when the pull request was opened. Watch for the maintainer's decision on the beta status.

**Rules checked (2026-11-21)**
- `CONTRIBUTING.md` (verified, <https://raw.githubusercontent.com/quozd/awesome-dotnet/master/CONTRIBUTING.md>): format `[LIBRARY](LINK) - DESCRIPTION`; the link is the project; descriptions concise, clear and simple; one link per PR; the PR description must have a meaningful project description, why it should be added, and a link to the project.
- Quality standard in the same file: generally useful, actively maintained, **stable**, documented, **tests**. The **[Research]** tag is for academic projects; **[$]**, **[Proprietary]**, **[Free]** and **[Free for OSS]** are for proprietary or commercial software. iTerminal is open source (MIT), so **no tag applies** (re-verified: Free for OSS does not apply).
- README (verified, <https://raw.githubusercontent.com/quozd/awesome-dotnet/master/README.md>): no PR title convention and no PR template (`.github` holds only the workflow). Recent merged-style titles are of the form `Add <NAME> to <Category>`, for example `Add RepoMedic to CLI section`, `Add TeaPie to Testing`.
- Check: workflow `awesome-bot` (verified, <https://github.com/quozd/awesome-dotnet/blob/master/.github/workflows/awesome-bot.yml>) runs `awesome_bot` link checking on README.md for every PR. Every link in the entry must answer, with no redirect other than 302 or 429 tolerated. Beta or stable status is not checked by a machine, only by the maintainer's reading of the quality standard.
- Duplicates: no `iTerminal` or `iConsole` entry exists in the README (searched 2026-11-21).
- Activity: the repository was last pushed 2026-03-26, with recent "Add ..." PRs; review speed is unverified.

**Category:** CLI (section `## CLI`; it holds Gui.cs and spectre.console, the terminal UI entries). Fallback: Tools. The CLI list is not strictly alphabetical in the whole file, but this section is, so the entry goes after `Gui.cs` and before `Power Args`.

**Entry line**

```markdown
* [iTerminal](https://github.com/nagytam/iTerminal) - Two-panel, keyboard-driven terminal file manager and developer toolbox in C# on .NET 11, built on iConsole, a reusable text-UI framework in the same repository. Beta, MIT.
```

(The list uses `*` bullets, not `-`.)

**Pull request title:** `Add iTerminal to CLI`

**Pull request body**

```markdown
Adds iTerminal to the CLI section, after Gui.cs.

iTerminal is a two-panel, keyboard-driven file manager and developer toolbox for the terminal,
written in C# on .NET 11 for Windows and Linux. It is built on iConsole, a reusable text-UI
framework in the same repository, so it is of interest both as an application and as a TUI
framework example for .NET developers. It is MIT licensed and has no paid edition, so no tag is used.

- Project: https://github.com/nagytam/iTerminal
- Latest release: https://github.com/nagytam/iTerminal/releases/latest (packages for Windows: MSI and portable zip; Linux: tar.gz, deb, rpm)
- Documentation: the README, plus architecture notes in the repository's Documentation folder
- Tests: the solution has unit test projects and an end-to-end suite that drives the whole app on a virtual terminal (README, "Build, test and run")

Status: the README marks the project as beta. I understand the list asks for stable projects; please
tell me if you prefer to wait, or if the category or wording should change.
```

**Facts behind the text (verified against the repository and the release, 2026-11-21):** README opening paragraph and beta banner; MIT in `LICENSE`; `net11.0` in CLAUDE.md; release `v0.0.0.751` (the latest, not 750; not a prerelease, published 2026-11-21) with MSI, portable zip, tar.gz, deb and rpm assets. Re-read the release number before sending.

**Possible blockers:** the guide's "Stable" criterion against the beta status; the project is about six weeks old (first commit 2026-10-15) with 1 star, and the README of the list says it aims at "very well-known resources" (both unverified as grounds for rejection). "Actively maintained" is met by the commit history.

## 3. awesome-cli-apps CSV list (toolleeo; tuxarch/cli-apps if it shares the rules)

Edit `data/apps.csv` only, with the columns `name`, `homepage`, `git`, `description` (verified above).
Not agarrharr/awesome-cli-apps: that list is closed to iTerminal for now (stars and age).

Description text to paste into the description column:

```text
Two-panel, keyboard-driven file manager and developer toolbox: Git revisions, Docker containers, SSH hosts, archives and cloud storage as folders. C#/.NET 11, Windows and Linux, MIT.
```

Markdown fallback for a README-style list:

```markdown
- [iTerminal](https://github.com/nagytam/iTerminal) - Two-panel keyboard-driven file manager and developer toolbox; Git, Docker, SSH and archives as folders. C#/.NET 11, Windows and Linux.
```

**Pull request title:** `Add iTerminal`

**Pull request body**

```markdown
Adds iTerminal (MIT, Windows and Linux), a two-panel terminal file manager that browses Git
revisions, Docker containers, SSH hosts and archives as folders.

- Repository: https://github.com/nagytam/iTerminal
- Release: https://github.com/nagytam/iTerminal/releases/latest

Note: the project is in beta, and the README says so. Happy to adjust the entry.
```

**Before sending, the user must:** confirm the release link works and check for a duplicate entry in the CSV.
The awesome-dotnet PR has been opened (see section 2). The CSV list has no age or star rule.
