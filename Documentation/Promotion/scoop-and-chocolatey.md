# Scoop and Chocolatey (Windows package managers beyond winget)

**Venues:** the Scoop `extras` bucket, the Chocolatey community repository.

**winget is already in progress and is not repeated here.** The pull request
`microsoft/winget-pkgs#485881` ("New package: TamasNagy.iTerminal version 0.0.0.735") is open (checked
with `gh pr list` on 2026-11-22), and the procedure is in
[Documentation/Distributions/winget.md](../Distributions/winget.md). Read that file before touching
Scoop: it records the problems met with the installer (immutable releases, `MANAGEDBYWINGET=1`).

## Rules that apply

| Venue | Rule | Status |
| --- | --- | --- |
| Scoop `extras` | A new app needs an issue in the bucket, approved by a maintainer, before any pull request ("We are very reluctant to accept random pull requests without a related issue created first"). The manifest needs an SPDX license, `checkver` and `autoupdate`, and a tested install, uninstall and persistence; one manifest per pull request; title `<app-name>: Add version <version>`; a comment starting with `/verify` after opening. Source: <https://github.com/ScoopInstaller/.github/blob/main/.github/CONTRIBUTING.md> | Verified (guide read on 2026-11-22) |
| Scoop `extras`, what is accepted | The bucket README says it holds manifests that do not meet the Main bucket criteria (<https://github.com/ScoopInstaller/Extras>). Any popularity or age threshold is unknown; the criteria page on the Scoop wiki was not read. | **Unverified** |
| Chocolatey community repository | Every version is moderated (automated validator, VirusTotal scan, install test, human sign-off); validator "Requirements" must be fixed before approval. Source: search summary of <https://docs.chocolatey.org/en-us/community-repository/moderation/package-validator/rules> | **Unverified** (page not read) |

## Before sending: what the user must do

- A GitHub account to open the Scoop issue (the agent opens nothing).
- `<VERSION>`: a released version (`https://github.com/nagytam/iTerminal/releases/latest`).
- `<PORTABLE ZIP URL>` and `<PORTABLE ZIP SHA256>`: the exact asset `iTerminal-<VERSION>-x64-portable.zip`
  and its SHA-256, computed from the downloaded file (GitHub also records a digest per asset).
- Decide the immutable-release question. Scoop's `autoupdate` points at release assets; winget's
  documentation chose an immutable second repository because a deleted release breaks a hash
  ([winget.md](../Distributions/winget.md)). `nagytam/iTerminal` releases are not immutable and can be
  deleted, so a Scoop manifest pointing at them has the same risk. Say so to the maintainer or use the same repository.
- Decide the update question. A portable copy updates itself from the next portable archive
  ([portable.md](../Distributions/portable.md)); a Scoop install also updates through Scoop. A
  maintainer may ask which one wins; the answer is the user's, and nothing is promised here.
- The zip has no wrapping folder and carries `iTerminal.portable`; the executable is `iTerminal.exe`
  (both from `portable.md`).

## 1. Scoop: issue text (open this first, wait for a reply)

```text
Title: Request: iTerminal (two-panel terminal file manager, MIT, beta)

Homepage: https://github.com/nagytam/iTerminal
License: MIT
Download: the portable zip, https://github.com/nagytam/iTerminal/releases/latest
(iTerminal-<VERSION>-x64-portable.zip, self-contained, no .NET runtime needed)

iTerminal is a keyboard-driven, two-panel file manager and developer toolbox for the terminal,
written in C# on .NET 11. It is in beta. Releases are frequent and numbered 0.0.0.<build number>.

I would write the manifest with checkver on the GitHub releases and an autoupdate block, and keep
it working. Is it suitable for extras at this stage, or should it wait? I have read the contributing
guide and will not open a pull request before you answer.
```

The pull request follows only after a maintainer agrees. Its fields come from the guide
(`version`, `description`, `homepage`, `license`, `architecture` with `url` and `hash`, `bin`
set to `iTerminal.exe`, `checkver`, `autoupdate`). Do not write the manifest from this file; start
from the repository's template as the guide says.

## 2. Chocolatey

Not drafted: the rules were not read, and each version adds a moderation round. Revisit after
winget is merged and Scoop has an answer. Read the validator rules page above in a browser first.
