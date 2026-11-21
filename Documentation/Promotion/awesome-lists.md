# Awesome-list entries

**Venues:** rothgar/awesome-tuis (TUI applications), awesome-dotnet (quozd/awesome-dotnet), and the
awesome-cli-apps family (tuxarch/cli-apps, whose newer forks keep the data in CSV files under `data/`).

**Rules to check before opening a pull request**
- **rothgar/awesome-tuis (verified from its `.github/pull_request_template.md`, 2026-11-21):** the repository must be at least 6 months old; the application must be unique; the interface must be unique (no wrappers such as `fzf`); the interface must be interactive or redraw its output (output formatters do not count as TUIs). A workflow, `readme_pr_repo_first_commit.yml`, checks the repository's first-commit date on every PR that changes `README.md`. iTerminal's first commit is 2026-10-15, so it is **not eligible before 2027-04-15**.
- One link per pull request, with the title `Add iTerminal` (awesome-dotnet asks for this; the other lists use the same norm). *Partly verified* from the repository guide as seen in search results; check each `CONTRIBUTING.md` before sending.
- awesome-dotnet may ask for a pricing tag. iTerminal is free and MIT licensed, so use the free-for-open-source tag if the list has one. *Unverified.*
- awesome-cli-apps (older guide): the app should be easy to install, documented, older than 90 days, and have more than 20 stars if it is on GitHub. *Unverified for the current repository.* Check the current rules and the CSV format in `tuxarch/cli-apps` before opening the pull request; the forks differ.
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
- Release: <RELEASE URL>
- Status: beta (stated in the README)
- Terminal UI built on iConsole, a reusable text-UI library in the same repository

Thanks for maintaining the list.
```

## 2. awesome-dotnet (quozd/awesome-dotnet)

Category: Console / Terminal (or the closest category: Tools, or Command-line).

```markdown
- [iTerminal](https://github.com/nagytam/iTerminal) - Two-panel terminal file manager and developer toolbox (Git, Docker, SSH and archives as folders), with the reusable TUI framework iConsole. MIT. [Free for OSS]
```

Remove the `[Free for OSS]` tag if the list has no such tag in its current format.

**Pull request title:** `Add iTerminal`

**Pull request body**

```markdown
iTerminal is an MIT-licensed, C# / .NET 11 terminal file manager for Windows and Linux. It is
built on iConsole, a text-UI framework in the same repository that can be used on its own.

- Repository: https://github.com/nagytam/iTerminal
- Release: <RELEASE URL>
- Beta; the README states it.

Please tell me if the category or the description format should change.
```

## 3. tuxarch/cli-apps (and the awesome-cli-apps CSV forks)

Check the repository first for the file to edit (a CSV under `data/` in the newer forks, the
README in the older ones) and for the required columns. Then add one row for the terminal file
managers category.

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
- Release: <RELEASE URL>

Note: the project is in beta, and the README says so. Happy to adjust the entry.
```

**Before sending, the user must:** confirm the release link works; confirm the repository is at least
as old as the list requires (if the list's age rule applies, check the repository creation date);
confirm the star threshold if the list has one, and wait if iTerminal is below it.
