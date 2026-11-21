# Contributor listings: up-for-grabs.net

**Venue:** up-for-grabs.net (list of projects with beginner-friendly issues), a pull request to
`up-for-grabs/up-for-grabs.net`.

## Rules that apply

Source: <https://github.com/up-for-grabs/up-for-grabs.net/blob/gh-pages/docs/list-a-project.md>
(**verified**, page read on 2026-11-21).

- The project should be active, with maintainers who want to guide new contributors, a curated list
  of small beginner-friendly tasks, and maintainers willing to review contributions. Projects that
  miss these may be declined.
- Add a `.yml` file named after the project in `_data/projects` with `name`, `desc`, `site`, `tags`
  and `upforgrabs` (a `name` for the label and a `link` to the issue list).
- Tags are lowercase, no spaces, using `a-z`, `0-9`, `+`, `#`, `.`, `-`. Leave out the `stats`
  fields; tooling generates them.

## Before sending

- `CONTRIBUTING.md` points to the labels `good first issue` and `help wanted`. Confirm on GitHub
  that those labels exist and that open issues carry them (the marketing kit suggests 10 to 15).
  Without a populated list the pull request will be declined. Fill `<LABEL ISSUE LIST URL>`.
- The user makes the pull request from their GitHub account.
- Do not promise review times in the entry that the maintainer will not keep.

## File: `_data/projects/iterminal.yml`

```yaml
name: iTerminal
desc: Two-panel terminal file manager and developer toolbox in C# on .NET 11 (MIT, beta)
site: https://github.com/nagytam/iTerminal
tags:
  - c#
  - .net
  - terminal
  - tui
  - file-manager
upforgrabs:
  name: good first issue
  link: <LABEL ISSUE LIST URL>
```

Tags must be checked against the tag list in the repository before the pull request; `c#` and
`.net` are the intended forms, other tags are suggestions.

## Pull request text

**Title:** Add iTerminal

```text
Adds iTerminal, an MIT-licensed terminal file manager written in C# on .NET 11 (beta).

- Issues labelled `good first issue`: <LABEL ISSUE LIST URL>
- Contribution guide: https://github.com/nagytam/iTerminal/blob/main/CONTRIBUTING.md
- The guide says that AI assistance is welcome and that significant AI-written parts must be
  mentioned in the pull request description.
```

(Verify the guide link and wording against the pushed repository before sending.)
