# Directories and newsletters

**Venues:** Console.dev (developer-tool newsletter), Terminal Trove (terminal tool directory),
AlternativeTo (software directory).

## Rules

| Venue | Rule | Status |
| --- | --- | --- |
| Console.dev | Suggest a tool by email to hello@console.dev (the site's About page). Selection criteria: a tool a developer would use regularly and that fits the development flow. The site says it does not do sponsored reviews. | Partly verified (site pages read in search results) |
| Terminal Trove | Submit form at terminaltrove.com/submit. Cross-platform preferred; standalone binaries preferred but not required; an image preview (PNG, GIF or MP4) is required; the tool must not already be listed. | Partly verified (form summary) |
| AlternativeTo | Free account; "Suggest new application"; fill in platforms, license, description and tags. One listing per application, no separate free and pro versions. Submissions are reviewed and can be refused. | Verified from the FAQ; approval times vary |

## 1. Console.dev: email suggestion

**To:** hello@console.dev
**Subject:** Tool suggestion: iTerminal (terminal file manager, MIT)

```text
Hello,

I would like to suggest iTerminal for the newsletter.

iTerminal is an open-source (MIT), two-panel, keyboard-driven file manager and developer toolbox
for the terminal. It runs on Windows and Linux and is written in C# on .NET 11. Git revisions,
Docker containers, SSH hosts, archives and cloud storage open as ordinary folders, so one set of
keys copies and compares between them. It also includes a Git client, a .NET solution viewer and
a background task queue.

It is in beta, so feedback from developers who use it is especially useful.

Repository: https://github.com/nagytam/iTerminal
Release: <RELEASE URL>
Documentation: https://github.com/nagytam/iTerminal/tree/main/Documentation

Thank you for considering it.

<YOUR NAME>
```

Send only once a release link exists. Use the project address or your own address as the user chooses
(see the open questions in the overview).

## 2. Terminal Trove: submission checklist

Use the form at terminaltrove.com/submit. Its form reads the project URL and fills in releases
and activity from the repository, so enter the repository first.

- **Project URL:** https://github.com/nagytam/iTerminal
- **Image preview (required):** create a 60–90 second terminal recording (GIF or MP4) of the
  two-panel view, `git://` browsing and a copy from a Git tag to a local folder, as the kit's demo
  script describes. Do not use a screenshot of a different app. *Not yet made.*
- **Short description (one line):** Two-panel, keyboard-driven file manager and developer toolbox; Git, Docker, SSH and archives as folders.
- **Long description (paste and trim to the form's limits):**

```text
iTerminal is a two-panel file manager for the terminal, in the tradition of Midnight Commander and
Far Manager, with the developer tools built in. Every place you work is a drive: local disks, SSH
and SMB hosts, FTP and WebDAV, Docker containers, any Git revision (browsed without checkout),
ZIP, 7z, TAR and disk-image archives, cloud storage, the recycle bin and search results.

It includes a Git client, a .NET solution viewer with build and test, background copy and move
with progress, folder comparison and sync, a viewer for many file types, full-text document search,
and a music player. It runs on Windows and Linux, is written in C# on .NET 11, and is MIT licensed.
It is in beta.
```

- **Platforms:** Windows, Linux. **License:** MIT. **Language:** C#.

## 3. AlternativeTo: suggestion

Create the entry under "Suggest new application". Fill in:

- **Name:** iTerminal
- **Platforms:** Windows, Linux
- **License:** Open source (MIT)
- **Website:** https://github.com/nagytam/iTerminal
- **Description:** Use the long description above, shortened.
- **Tags:** file manager, terminal, TUI, Midnight Commander, Total Commander, Git client, Docker, SSH, C#, .NET
- **Alternatives to:** Midnight Commander, Total Commander, Far Manager, Double Commander (only the ones the user is willing to be compared with; the entry is a directory note, not a comparison)

Do not claim to be "better than" another program. AlternativeTo lists the alternatives; it does not
need a sales argument.

## Not sent yet

Nothing in this file has been submitted. Every item above needs the user's account, the release link,
and for Terminal Trove a preview recording.
