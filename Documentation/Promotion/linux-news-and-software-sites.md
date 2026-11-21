# Linux news tips, software directories and Changelog News

**Venues:** OMG! Ubuntu (news tip form), LinuxLinks (software suggestion form), Changelog News
(link submission), It's FOSS News (tip form, rules unverified).

## Rules that apply

| Venue | Rule | Status |
| --- | --- | --- |
| OMG! Ubuntu | A tip form (name and message required, email optional). Asks for a link to the project or download, and says it does not answer every tip or cover everything. Source: <https://omgubuntu.co.uk/tip> | Verified (page read) |
| LinuxLinks | Suggestion form: application name, what it does, repository (GitHub, GitLab or Codeberg), generative-AI use (yes, no, don't know), a public AI-usage disclosure URL if yes, an AI declaration checkbox, license, name and email. Prefers software that runs natively on Linux, is actively maintained and ready for everyday use. Projects developed largely with generative AI are unlikely to be featured; substantial use must be disclosed publicly, and a submission without the disclosure "will not be considered". Source: <https://linuxlinks.com/suggest-open-source-program/> | Verified (page read). The separate "AI-heavy projects policy" it links to was not read |
| Changelog News | Sign-in needed. Anyone may submit their own or others' work; the test is whether fellow developers will find it interesting; the submitter should say why it is newsworthy. Excluded: how-tos and tutorials, commercial products or services, reader-hostile sites. Source: <https://changelog.com/news/submit> | Verified (page read) |
| It's FOSS News | A contact page for news; the news portal covers news only, not reviews or tutorials, and does not publish every topic (from a search snippet). The page itself returned 404 on 2026-11-21 (<https://itsfoss.com/news/contact-us>). | Unverified |

## Before sending

- Replace `https://github.com/nagytam/iTerminal/releases/latest` with a published release. A beta without a download is not newsworthy to any of these.
- **AI disclosure.** iTerminal is developed with AI assistance (`CONTRIBUTING.md` and `FUNDING.md` say so). LinuxLinks asks the question directly and wants a public disclosure URL. Answer "Yes" and give `<AI DISCLOSURE URL>`, for example the "Using AI tools" section of `CONTRIBUTING.md` on the repository once it is pushed. Do not answer "No" or "Don't know". Decide whether LinuxLinks's policy would exclude the project; if so, do not submit.
- Changelog News excludes commercial products. The project is MIT licensed and free, but a sponsor appeal in the same submission could be read as commercial. Leave the sponsor ask out.
- `<SUBMITTER NAME>` and `<SUBMITTER EMAIL>`: only the forms that need them (LinuxLinks). The user chooses which address to give.
- Linux-only venues: say plainly that iTerminal also runs on Windows, but name the Linux packages first (tar.gz, deb, rpm).

## 1. OMG! Ubuntu: news tip

Message (the form is short; send a link, not a pitch):

```text
iTerminal is a free, open-source (MIT) two-panel file manager and developer toolbox for the
terminal. Git revisions, Docker containers, SSH hosts, archives and disk images open as ordinary
folders, so one set of keys copies between any two of them. It ships as a self-contained deb,
rpm and tar.gz for Linux (and an MSI for Windows). It is in beta.

Project and downloads: https://github.com/nagytam/iTerminal/releases/latest
Source: https://github.com/nagytam/iTerminal
```

Credit line: ask for none, or give `<SUBMITTER NAME>`. Leave the email empty unless the user wants a
reply.

## 2. LinuxLinks: software suggestion

| Field | Entry |
| --- | --- |
| Name of application | iTerminal |
| What does the application do? | A two-panel, keyboard-driven file manager and developer toolbox for the terminal. Local disks, SSH, SMB, FTP and WebDAV hosts, Docker containers, Git revisions, archives and disk images, and cloud storage open as folders. Built in: a Git client, a viewer for many file formats (F3), full-text document search, background copy and move with progress. Linux and Windows, written in C# on .NET 11, beta. |
| Code repository web address | https://github.com/nagytam/iTerminal |
| Use of generative AI in development | Yes |
| Public AI usage disclosure URL | `<AI DISCLOSURE URL>` |
| AI usage declaration | Tick only after reading the policy the form links to |
| License | MIT |
| Name, email | `<SUBMITTER NAME>`, `<SUBMITTER EMAIL>` |

## 3. Changelog News: link submission

Needs a changelog.com account (the user's). Title and note:

**Title:** iTerminal: a two-panel terminal file manager where Git tags, Docker and SSH are folders

**Why it is newsworthy (a few sentences):**

```text
A commander-style file manager (think Midnight Commander or Far) written in C# on .NET 11 and
MIT licensed, now in public beta. The distinctive part is that every location is a URL scheme:
git://, docker://, ssh://, archive://, recycle://. They all behave like directories, so copying
from a Git tag into a container is the same keystroke as any copy. The UI layer (iConsole) is a
separate reusable library in the same repository. Long operations run as background tasks, and
key handlers have a 200 ms budget enforced in tests.
```

Link: `https://github.com/nagytam/iTerminal/releases/latest` or the repository, whichever Changelog prefers; the repository is the safer
choice for a "project" item.

## 4. It's FOSS News

Only after the contact page is found again: send the OMG! Ubuntu text with the release link. Do not
send reviews or tutorial requests to the news portal.
