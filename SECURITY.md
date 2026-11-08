# Security Policy

iTerminal is a file manager that handles credentials (a vault and a password manager), remote
connections (SSH, SMB, FTP, WebDAV, cloud storage), archives and downloaded runtime dependencies.
Security reports are taken seriously.

## Supported versions

iTerminal is in beta. Only the latest release receives security fixes.

| Version | Supported |
| --- | --- |
| Latest release | Yes |
| Older releases | No, please update |

## Reporting a vulnerability

**Please do not report security problems in public issues, discussions or pull requests.**

Report privately, in either of these ways:

1. **GitHub private vulnerability reporting.** On the repository, open the **Security** tab and
   choose **Report a vulnerability**, if that option is shown.
2. **Email** [mail@tamas-nagy.net](mailto:mail@tamas-nagy.net) with the subject `iTerminal security`.

Please include:

- A description of the problem and its impact.
- The iTerminal version, operating system and terminal emulator.
- Steps to reproduce, or a proof of concept (a sample archive or file is ideal).
- Whether you want to be credited, and under what name.

### What to expect

- An acknowledgement within **7 days**.
- A first assessment, and a request for more detail if needed, within **14 days**.
- Regular updates until the problem is fixed or declined. This is a small project run by one
  maintainer, so timelines are best effort.
- Credit in the release notes when the fix ships, unless you prefer to stay anonymous.

Please give us a reasonable time to fix the problem before you disclose it publicly. We aim to
ship a fix within 90 days of a confirmed report and will coordinate the disclosure date with you.

## Scope

In scope:

- Path traversal or overwriting files outside the target while extracting or copying archives.
- Leaking or mishandling secrets from the vault, the password manager or the settings database.
- Credentials appearing in URLs, logs, history or the command line.
- Weak or bypassed host verification (SSH known hosts, FTPS certificate pinning).
- Command injection through file names, URLs, plug-in data or the command line.
- Unsafe handling of downloaded runtime dependencies (Playwright, Ollama, git).
- Crashes or hangs triggered by a crafted file in a viewer or an archive, which an attacker could use against a user who merely browses a folder.
- Actions that destroy data without the confirmation the interface promises.

Out of scope:

- Problems that need an attacker who already controls the user's account or machine.
- Vulnerabilities in third-party tools that iTerminal can call (git, docker, ffmpeg, 7-Zip, Tesseract, Ollama). Report these to their projects. If iTerminal uses them in an unsafe way, that is in scope.
- Issues in unreleased branches or in forks.
- Missing hardening that has no demonstrated impact, and reports from automated scanners without a working scenario.
- Denial of service through very large inputs that the user chose to open on purpose.

## Safe harbour

We will not take legal action against people who research and report problems in good faith under
this policy. Please do not access or change data that is not yours, do not degrade other people's
systems, and do not run tests against services you do not own.

## Hardening tips for users

- Keep iTerminal up to date.
- Prefer SSH keys and the built-in vault to passwords typed into the command line.
- Verify downloaded packages against the checksums shown on the release page.
- Treat archives and documents from unknown sources the way you would in any file manager.
