# iTerminal Marketing Kit

Ready-to-use copy for announcing iTerminal: taglines, pitches of several lengths, a feature
catalogue, announcement texts for mailing lists and forums, social posts, a slide outline, a demo
script and an FAQ. Copy what you need and trim to the channel.

**Facts to keep consistent everywhere**

| | |
| --- | --- |
| Name | iTerminal (its UI framework is called iConsole) |
| What it is | A two-panel, keyboard-driven file manager and developer toolbox for the terminal |
| Stack | C# on .NET 11 |
| Platforms | Windows and Linux (self-contained packages: Windows MSI; Linux tar.gz, deb, rpm) |
| Status | **Beta**. Say so; it builds trust. |
| License | MIT |
| Author | Tamás Nagy, <mail@tamas-nagy.net>, [github.com/nagytam](https://github.com/nagytam) |
| Repository | <https://github.com/nagytam/iTerminal> |
| Languages | English, German, French, Spanish, Italian, Hungarian (switchable at run time) |
| Contribute | Pull requests and issues welcome; see section 7 |
| Sponsor | Donations and AI-budget sponsorship; see section 8 (`<SPONSOR URL>` placeholder) |

> **Before sending anything:** check each feature claim against the current build (this kit was
> written from the repository's documentation, not from a fresh run of every feature), and replace
> the `<RELEASE URL>` and `<SPONSOR URL>` placeholders with the real links. Do not promise
> sponsor perks or funding uses you do not intend to keep (section 8).

## 0. Goals

| Goal | Audience | Call to action |
| --- | --- | --- |
| **Awareness and users** | Terminal-centric developers, sysadmins, commander-style file manager fans | Download, try, star |
| **Contributors** | C#/.NET developers, TUI enthusiasts, people who want their own file-format or drive supported, translators | Fix a bug, pick a "good first issue", add a drive or viewer, translate |
| **Sponsors** | Individuals who use it daily, companies whose staff live in terminals, AI/dev-tool vendors | Donate, or fund the AI budget that speeds up development |

Every announcement should carry one primary ask and, below it, the secondary asks. Typical
order: *try it* → *tell us what breaks* → *send a PR* → *sponsor if it saves you time*.

---

## 1. Positioning

**One line:** The classic two-panel file manager, rebuilt for today's terminal, with the whole
developer toolbox built in.

**Taglines (pick one)**
- *Your files, your servers, your repos, one keyboard.*
- *The commander for the terminal generation.*
- *Two panels. Every drive. No mouse.*
- *Midnight Commander's spirit, a modern toolbox's reach.*

**Who it is for**
- Developers and sysadmins who live in a terminal and SSH sessions.
- People who loved Norton/Midnight/Total Commander/Far and want that workflow on current systems.
- .NET developers who want a serious, reusable text-UI framework (`iConsole`) as open source.

**Why it is different**
1. **Everything is a folder.** Git revisions, Docker containers, SSH hosts, archives, disk images, cloud storage, a phone, the recycle bin and search results all open as ordinary panel drives. Copy from a git tag to an SSH host with F5.
2. **A toolbox, not just a file manager.** Git client, Docker, solution viewer, package manager, AI assistant, translator, music player, document search and more, without leaving the terminal.
3. **Works where a GUI can't.** It runs over SSH on a headless box, and can lend the whole terminal to your shell (Ctrl+O) and take it back.
4. **Responsive by design.** Key handlers must finish within about 200 ms; slow work runs on background tasks with progress and notifications.
5. **Open and tested.** MIT licensed, with unit tests and an end-to-end suite that boots the whole app on a virtual terminal.

---

## 2. Pitches

### 25 words
iTerminal is an open-source, two-panel file manager and developer toolbox for the terminal. It treats Git, Docker, SSH, archives and cloud storage as folders. Windows and Linux, C# on .NET 11.

### 60 words
iTerminal brings the two-panel commander workflow to modern systems. Browse local disks, SSH hosts, Docker containers, Git revisions, archives, disk images and cloud storage with the same keys, then copy between any two of them. Built in: a Git client, a full-text document index, a music player, a local AI assistant and a dozen utilities. Open source (MIT), written in C# on .NET 11, runs on Windows and Linux.

### 150 words
If you spend your day in a terminal, you probably juggle `ls`, `scp`, `git`, `docker`, `tar`, `find` and a file browser for the odd job. iTerminal puts them side by side in one keyboard-driven, two-panel interface.

Every location is a drive with a URL: `file://`, `ssh://`, `smb://`, `docker://`, `git://`, `archive://`, `recycle://` and more. Because they all behave like folders, a copy from a Git tag to a container, or from a ZIP to an SSH host, is the same two keystrokes as anything else. Long operations run as background tasks with progress windows and notifications, so the interface never freezes.

Around the file manager sits a developer toolbox: a Git client, a .NET solution viewer, a package-manager front end, a shell hand-over (Ctrl+O) and an optional local AI assistant through Ollama. It is open source under the MIT licence, runs on Windows and Linux, and is in beta, so feedback is very welcome.

### Contributor and sponsor add-ons (append to any pitch)

**Contributors, 30 words**
> iTerminal is built in the open and has plenty of small, well-bounded jobs: a new drive, a new file viewer, a translation, a bug fix. Pull requests are welcome, and review is fast.

**Sponsors, 30 words**
> iTerminal is free and MIT licensed. If it saves you time, sponsoring covers the AI budget and the hours behind new features and fixes. Every contribution, large or small, is published openly.

---

## 3. Feature catalogue

Group these to suit the audience. Key bindings are those documented in the repository.

### 3.1 File management
- **Two panels** with per-panel sorting (name, extension, size, date; Shift+F11), hidden-file toggle and natural number sorting.
- **Copy, move, delete** as background tasks with progress windows and completion notifications; a task manager can pause and reorder the queue.
- **Marking:** Insert, Shift+arrows or by pattern, to act on many entries at once.
- **Multi-rename** with patterns and a preview.
- **Links:** create symbolic links, junctions and hard links.
- **Compare and synchronise folders** (Shift+F2), compare files (Ctrl+F10), with plan-then-confirm sync.
- **Find** by name and content (F7); results appear as a panel drive you can act on.
- **File clipboard** (Ctrl+C / Ctrl+X / Ctrl+V) and **Open with / Edit with** file associations.
- **Duplicate finder, disk usage, colour labels and tags** with filtering.
- **Command palette** (Shift+F1), **folder selector** with favourites, history and tree (Ctrl+F1), **workspaces** that save named layouts (Ctrl+Shift+F2), themes and a settings dialog.
- **A user menu** (Shift+F10) and a command line under the panels, as in the classic commanders.

### 3.2 Every place is a drive

| Scheme | What you get |
| --- | --- |
| `file://` | Local disks |
| `ssh://`, `smb://`, `ftp`/FTPS, `dav://`, `davs://` | Remote hosts, shares and WebDAV |
| `docker://` | Container file systems and logs; start, stop, restart |
| `git://` | Any revision, branch, tag or commit, without checking it out |
| `archive://` | ZIP, TAR, TAR.GZ, TAR.BZ2, 7z, RAR, CAB and ISO, IMG, VMDK, WIM, DMG disk images (7-Zip) |
| `s3://`, `onedrive://`, `gdrive://` | Cloud storage |
| `mtp://`, `iphone://` | Phones and cameras |
| `recycle://` | Recycle bin / Trash with restore |
| `search://`, `preview://` | Search results and quick preview |
| `memdrive://` | In-memory scratch drive |
| `musiccatalog://`, `musicplaylist://` | Music library and queue |

Credentials come from the built-in vault or password manager, never from a URL. SSH and FTPS host identities are pinned.

### 3.3 Safety first
- Destructive actions ask for confirmation in the footer.
- Recycle-bin and freedesktop-Trash browsing with restore.
- **Secure wipe** (Ctrl+Shift+F8) and **signatures** (Ctrl+Shift+F7).
- **Checksums** (MD5, SHA-1, SHA-256, SHA-512) with automatic verification against `.sha256` / `SHA256SUMS` files.
- Free-space check before a copy; safe path handling on extraction.

### 3.4 Archives
- Compress (Alt+F5): ZIP (AES-256 or ZipCrypto), 7z, TAR.GZ, TAR.BZ2, TAR; add to existing archives.
- Extract (Shift+F5) with password support.
- Or just open any archive or disk image as a folder.

### 3.5 A viewer for almost everything (F3)
- Text with selectable **code pages**, hex view, syntax colouring in the editor (F4).
- **Office documents:** Word, Excel and PowerPoint (OpenXML and the legacy 97-2003 formats), OpenDocument, RTF, Pages / Numbers / Keynote.
- **Books and mail:** EPUB, FB2, Kindle (MOBI/AZW), `.eml`, Outlook `.msg`, mbox, vCard, iCalendar, XPS.
- **Data viewers:** JSON, XML, CSV/TSV grid, SQLite, Access, HDF5.
- **Media:** images with metadata, video, and a music tag viewer/editor.
- **Solutions:** `.sln` / `.slnx` open in a solution viewer with build, test and run.
- Because readers work on bytes, a document inside an archive or on an SSH host reads exactly like a local one.

### 3.6 Developer toolbox
- **Git client** (Ctrl+G): stage, commit, branches, stashes, push/pull, diffs, plus `git://` browsing.
- **Docker** as a drive, **.NET solution viewer**, **package manager** front end (WinGet on Windows, apt on Linux).
- **Shell hand-over** (Ctrl+O): the whole terminal goes to a child shell; Ctrl+C, Ctrl+\ and Ctrl+Z behave normally there.
- **Remote connection manager** (Alt+F2), command log terminal, **virtual machine manager**, **Bluetooth manager**, process / task / disk / network / system managers.
- **Local AI assistant bar** through Ollama: runs on your machine, downloaded on demand from Tools > Runtime Dependencies.

### 3.7 Documents, knowledge and media
- **Document manager** (Alt+F3): full-text search (SQLite FTS5) across text, markup, Office, OpenDocument, PDF and image metadata, with optional OCR via Tesseract.
- **Music player:** its own audio pipeline (WAV, managed MP3; FLAC/OGG/Opus/M4A/WMA via ffmpeg), WinMM / PulseAudio / ALSA output, an indexed library, a persistent queue and M3U8 playlists, all as panel drives.
- **Encyclopedia** (Ctrl+Shift+W), **translator** (Ctrl+Shift+F4), **currency converter** (Ctrl+Shift+F5), **BitTorrent manager** (Ctrl+Shift+F1), **contacts** (Ctrl+Shift+K), web browser, map, mail, stock browser, calendar, to-do, calculator, unit converter.
- **Printing** (Ctrl+P) and a print queue.

### 3.8 And for the breaks
Tic-Tac-Toe, Chess (full rules, with a computer opponent), Go (9x9 to 19x19), Nine Men's Morris, Snake and Tetris, all in the Tools menu.

### 3.9 For .NET developers: iConsole
`iConsole` is a reusable terminal UI framework that lives in the same repository: controls, commands, background jobs, a layered rendering pipeline with diffed output, a notification and toast system, localisation, and platform input plug-ins for Windows and POSIX. If you want to build your own text UI in C#, it is open source and documented under `docs/architecture/`.

---

## 4. Announcement texts

### 4.1 Mailing list / newsletter

**Subject options**
- Introducing iTerminal: a two-panel file manager and developer toolbox for your terminal
- iTerminal beta: browse Git tags, Docker containers and SSH hosts like folders
- Open source: the commander-style file manager rebuilt in C# for .NET 11

**Body**

> Hello everyone,
>
> I am happy to announce the first public beta of **iTerminal**, an open-source, two-panel,
> keyboard-driven file manager and developer toolbox for the terminal. It runs on Windows and
> Linux, it is written in C# on .NET 11, and it is MIT licensed.
>
> **The idea:** every place you work is a drive. Local disks, SSH and SMB hosts, FTP and WebDAV,
> Docker containers, any Git revision, ZIP/7z/RAR archives and ISO/VMDK/WIM/DMG disk images, cloud
> storage, an iPhone, the recycle bin and even search results all open as panels, so one set of
> keys copies, compares and synchronises between any two of them.
>
> **What is in the box**
> - Background copy/move/delete with progress, a pausable task queue, marking, multi-rename, folder comparison and sync
> - A viewer (F3) for text, hex, Office and OpenDocument files, e-books, mail, JSON/XML/CSV, SQLite and HDF5, images, video and audio tags
> - A Git client, a .NET solution viewer, a package manager front end, a remote connection manager and a shell hand-over (Ctrl+O)
> - Full-text search across your documents, a music player, an optional local AI assistant (Ollama), a translator and more
> - Six interface languages: English, German, French, Spanish, Italian and Hungarian
>
> **Status:** beta. Expect rough edges and keep backups of anything you cannot afford to lose.
> That is exactly why I would love your feedback: bug reports, ideas and pull requests are welcome.
>
> **Help shape it.** The code is organised so that a contribution can be small: a new drive, a
> file-format reader for the viewer, a translation, a bug fix. The architecture notes in `docs/`
> explain each subsystem, and the test suite tells you quickly whether a change is safe. Pull
> requests are welcome.
>
> **Support it.** iTerminal is free and will stay MIT licensed. If it earns a place in your daily
> workflow, or your team's, please consider sponsoring: `<SPONSOR URL>`. Funds go to development
> time and to the AI budget used to build and review features faster.
>
> Download: `<RELEASE URL>`
> Source and docs: <https://github.com/nagytam/iTerminal>
>
> Thank you,
> Tamás Nagy

### 4.2 Show HN / Hacker News

**Title:** Show HN: iTerminal – a two-panel terminal file manager where Git tags, Docker and SSH are folders

**First comment**

> Hi HN, I built iTerminal because I wanted the Midnight Commander / Far workflow on current
> systems, with the things I actually touch every day (git, docker, ssh, archives) as first-class
> folders instead of separate tools.
>
> Every location is a URL scheme (`git://`, `docker://`, `ssh://`, `archive://`, `recycle://`...)
> and behaves like a directory, so copying from a Git tag into a container is the same operation as
> any other copy. Long operations run as background tasks; key handlers and drawing have a ~200 ms
> budget, which is enforced in tests.
>
> It is C# / .NET 11, MIT licensed, for Windows and Linux (self-contained MSI, tar.gz, deb, rpm).
> The UI layer is a separate reusable library (iConsole) in the same repo. Optional pieces
> (git, docker, ffmpeg, 7-Zip, Tesseract, Ollama) are used when present or downloaded on demand.
>
> It is a beta, so I am especially interested in what breaks on your terminal emulator and in which
> drives or viewers you would want next. PRs are very welcome (there are architecture notes per
> subsystem under docs/, and a new drive or file reader is a good self-contained first change).
> Repo: https://github.com/nagytam/iTerminal

### 4.3 Reddit (r/commandline, r/dotnet, r/linux, r/opensource, r/selfhosted)

**Title:** I built an open-source two-panel file manager for the terminal (C#/.NET 11) where Git revisions, Docker containers and SSH hosts are just folders

> Midnight Commander fans: this is my take on the idea with modern plumbing. Highlights: `git://`
> browsing without checkout, `docker://` file systems and logs, archives and disk images as
> folders, background tasks, a built-in Git client, a viewer for Office/EPUB/SQLite/HDF5 files,
> full-text document search, an optional local AI bar via Ollama, and six UI languages. MIT licence,
> Windows and Linux, beta. Feedback and bug reports very welcome: https://github.com/nagytam/iTerminal

*Tailor the first sentence per subreddit and follow each one's self-promotion rules.*

### 4.4 Social posts

**Short (X / Mastodon / Bluesky)**
> Meet iTerminal: an open-source, two-panel file manager for your terminal. Git tags, Docker containers, SSH hosts, archives and cloud storage all open as folders. C# / .NET 11, Windows + Linux, MIT. Beta, feedback welcome. https://github.com/nagytam/iTerminal

**Variants**
- *Git angle:* Browse any Git commit or tag without checking it out, then copy files out with F5. That is `git://` in iTerminal. https://github.com/nagytam/iTerminal
- *Sysadmin angle:* One screen: your laptop on the left, an SSH host on the right, a Docker container's logs one keystroke away. iTerminal, open source, MIT.
- *Dev angle:* The text-UI framework behind iTerminal (iConsole) is open source too: controls, jobs, a diffing renderer, localisation. All C#.
- *Nostalgia angle:* F5 copy, F6 move, F7 find, F8 delete, still muscle memory, now with Git, Docker and SSH.

**LinkedIn**
> Today I am releasing the first public beta of iTerminal, an open-source file manager and
> developer toolbox for the terminal. I built it to bring the two-panel commander workflow to
> modern systems and to make everyday infrastructure (Git, Docker, SSH, archives, cloud storage)
> browsable like folders. It is written in C# on .NET 11, MIT licensed, and runs on Windows and
> Linux. If you work in terminals, I would value your feedback: https://github.com/nagytam/iTerminal

### 4.5 Blog post outline ("Why I built iTerminal")
1. The itch: five tools for one task. The case for a unified, keyboard-first workspace.
2. Everything is a URL: how drives work and why a Git tag can be a source folder.
3. A responsive UI in a terminal: the 200 ms budget, background tasks and rendering by diff.
4. Tested like a product: unit tests plus an end-to-end suite on a virtual terminal.
5. Lending the terminal (Ctrl+O): handing the screen to a shell and getting it back.
6. Beta, roadmap, how to contribute and how to sponsor.

### 4.6 Call for contributors (standalone post)

**Title:** iTerminal needs you: good first issues for C#/.NET developers

> iTerminal is an open-source terminal file manager written in C# on .NET 11, and it is now open
> for contributions. You do not need to understand all of it: the code is split into a reusable UI
> framework (`iConsole`), the application behaviour (`iTerminal.Core`) and a thin executable, and
> each subsystem has a write-up in `docs/architecture/`.
>
> **Ways to help, from small to large**
> - Report a bug with the terminal emulator and OS you use (the single most useful thing in a beta).
> - Fix a bug labelled `good first issue`.
> - Translate: a new string needs a key in six `.resx` files; a new language is a new file.
> - Add a file-format reader to the F3 viewer. Readers take bytes, so they are small, isolated and easy to unit-test.
> - Add a drive (a URL scheme), a game, a dialog or a command-palette action.
> - Improve the docs, write tests, try it on a terminal we have not (WezTerm, Kitty, Windows Terminal, tmux, SSH over a slow link).
>
> **What we promise:** a quick first response, honest review feedback, and credit in the release
> notes. **What we ask:** one focused change per PR, tests with your change, and `dotnet build`
> and `dotnet test` green before you open it. The repository's `CLAUDE.md` lists the rules that
> are easy to break (threading budget, rendering, key handling).
>
> Start here: https://github.com/nagytam/iTerminal

### 4.7 Sponsor appeal (standalone post)

**Title:** Help fund iTerminal's AI budget and development time

> iTerminal is free, MIT-licensed and built mostly in spare time. A growing part of the work is
> done with AI assistance (design, implementation, test writing and review), and that has a real
> monthly cost in tokens and tooling. Sponsorship pays for that budget and for focused time on bugs
> and features.
>
> **What your money buys**
> - AI budget for implementing and reviewing features, writing tests and keeping docs current
> - Maintainer time to review community pull requests quickly
> - CI and release infrastructure, code signing and packaging (WinGet, deb/rpm repositories)
> - Priority on your bug reports and a vote on the roadmap, for sponsors who want it
>
> **How to help:** `<SPONSOR URL>`. One-off or monthly, every amount is welcome. Companies that
> put iTerminal in front of their engineers can sponsor at a team level (see tiers on the page).
>
> **Transparency:** a short monthly note will say what was received and what it was spent on.

### 4.8 Email to companies and dev-tool vendors

**Subject:** Sponsoring iTerminal, an open-source terminal file manager (MIT)

> Hello `<name>`,
>
> I maintain iTerminal, an open-source (MIT) two-panel file manager and developer toolbox for the
> terminal, built in C# on .NET 11 for Windows and Linux. It lets engineers browse and copy between
> SSH hosts, Docker containers, Git revisions, archives and cloud storage with one set of keys.
>
> I am looking for sponsors to fund AI-assisted development and maintainer time. In return:
> `<logo/name on README and website, release-note thanks, priority issue triage, and a say in the
> roadmap; adjust to what you will really deliver>`.
>
> If an in-kind sponsorship (API credits, a code-signing certificate, CI minutes) suits you better
> than money, that helps just as much. Repository: https://github.com/nagytam/iTerminal
>
> Best regards,
> Tamás Nagy

---

## 5. Presentation outline (15-20 minutes)

| # | Slide | Content |
| --- | --- | --- |
| 1 | Title | iTerminal, "Two panels. Every drive. No mouse." |
| 2 | The problem | `ls`, `scp`, `git`, `docker`, `tar`, `find`, a browser for the rest: context switching |
| 3 | The idea | Every place is a folder with a URL |
| 4 | Live demo 1 | Open an SSH host on the right, copy a file from the left (F5) |
| 5 | Drives at a glance | The scheme table from section 3.2 |
| 6 | Live demo 2 | `git://` a tag, open an archive and a disk image as folders |
| 7 | Never freeze | Background tasks, task manager, 200 ms rule |
| 8 | Safety | Confirmations, recycle bin, checksums, wipe, host pinning |
| 9 | Viewer (F3) | Office, EPUB, SQLite, HDF5, CSV, JSON, hex, images |
| 10 | Developer toolbox | Git client, Docker, solution viewer, package manager, Ctrl+O shell |
| 11 | Search and knowledge | Full-text document index, OCR, encyclopedia, translator |
| 12 | Local AI | Ollama assistant bar, runs on your machine |
| 13 | Under the hood | C#/.NET 11, iConsole, layered rendering, plug-in input drivers |
| 14 | Quality | Unit tests and end-to-end suite on a virtual terminal |
| 15 | Build it with us | Contribution paths: bug fix, drive, viewer, translation; docs per subsystem; tests as a safety net |
| 16 | Fuel it | Sponsorship: what it funds (AI budget, review time, CI and signing), tiers, transparency report |
| 17 | Get it | Packages, license, repo, beta status, `<SPONSOR URL>` |

**Speaker notes**
- Lead with the demo; the "aha" is copying between two unlike places with the same keys.
- Say "beta" early and without apology.
- End with a concrete ask: try it, file one issue, star the repo, pick one good first issue, sponsor if it helps you.
- For slide 15, show the repository tree in one picture (`iConsole` → `iTerminal.Core` → `iTerminal`) so people see where their change would go.
- For slide 16, show real numbers only if you are willing to publish them.

---

## 6. Five-minute demo script

1. **Start** (`iTerminal`). Show the two panels and the footer's F-key bar. Open the command palette (Shift+F1) to show everything is discoverable.
2. **Copy across hosts.** Right panel: connect to an SSH host from the remote connection manager (Alt+F2). Mark two files with Insert, copy with F5. Watch the progress window and the completion toast.
3. **Git without checkout.** Navigate to a `git://` revision of a repository, browse an old tag, copy a file out.
4. **Archive and disk image as folders.** Enter a ZIP and a disk image like directories; F3 on a file inside.
5. **Viewer tour.** F3 on a `.docx`, a `.csv`, a SQLite file.
6. **Search.** F7 find by content; results appear as a drive.
7. **Shell hand-over.** Ctrl+O, run something, exit, and the UI returns.
8. **Show where a contribution goes.** Open the Tools menu, point at one small feature (a game or a viewer), and say which folder holds it and which test covers it.
9. **Close** with the repository URL, the "good first issue" label and the sponsor link.

*Rehearse on the same terminal emulator you will present with, and keep a pre-built sandbox folder of sample files.*

---

## 7. Attracting contributors

### 7.1 Why a developer would contribute
- **Small, bounded entry points.** Drives, viewers, games, dialogs and translations are separate, self-contained pieces.
- **Documented architecture.** One write-up per subsystem in `docs/architecture/`, plus a list of easy-to-break rules in `CLAUDE.md`.
- **A real safety net.** Unit tests per project and an end-to-end suite that boots the whole app on a virtual terminal.
- **Modern C#.** .NET 11, nullable enabled, async-first, constructor injection, an `.editorconfig`.
- **Visible results.** A change appears in a running app within minutes, and the project is small enough for a maintainer to notice it.
- **A reusable framework.** Work on `iConsole` benefits anyone who builds a text UI in C#.

### 7.2 Entry points to advertise

| Contribution | Where | Why it is a good first change |
| --- | --- | --- |
| Bug fix | Any, with a failing test | Small and verifiable |
| Translation | `iConsole/Localization/Resources/Strings.*.resx` | No deep code knowledge; a new string needs a key in all six files |
| File viewer / reader | F3 readers in `iConsole`, `iTerminal.Core` | Takes bytes, returns lines; easy to unit-test |
| New drive (URL scheme) | `iTerminal.Core`, see the checklist in `CLAUDE.md` | Clear recipe, big user-visible value |
| Game or small dialog | Tools menu, `Services/Games/` | Fun, isolated, low risk |
| Terminal compatibility | `iConsole.Inputs.Windows` / `.Posix` | Test on WezTerm, Kitty, tmux, Windows Terminal, SSH |
| Docs and tests | `docs/`, `*.Test` | Always useful |

### 7.3 Make contributing easy (do these before the launch)
The repository currently has no `CONTRIBUTING.md`, issue templates or code of conduct. Add:
- [ ] `CONTRIBUTING.md`: build and test commands, branch and PR rules, the 200 ms budget, code style, how to run a single test, what "done" means (tests green, docs touched, strings in all six files).
- [ ] Issue templates: bug report (OS, terminal emulator, version, steps, `ITERMINAL_KEY_TRACE` output) and feature request.
- [ ] A PR template with a short checklist.
- [ ] A `CODE_OF_CONDUCT.md` (for example the Contributor Covenant) and a `SECURITY.md` with a contact address.
- [ ] 10-15 issues labelled `good first issue` and `help wanted`, each with a pointer to the relevant file and test.
- [ ] GitHub Discussions (or a chat room) for questions.
- [ ] A short "Your first PR in 30 minutes" walkthrough: clone, `dotnet build`, `dotnet run`, change a string, run one test.
- [ ] CI that runs on every PR and reports clearly, so first-timers get quick feedback.

### 7.4 Maintainer promises (only publish what you will keep)
- First response within a few days; review feedback that explains the *why*.
- Contributors credited in release notes; a "Contributors" section in the README.
- A clear stance on AI-assisted pull requests (allowed? disclosed? tested?). Many developers look for this.
- Consistent labels and a public roadmap issue, so people can claim work.

### 7.5 Contributor-focused README block
```markdown
## Contributing
iTerminal welcomes bug fixes, new drives, file viewers, translations and docs.
Start with [CONTRIBUTING.md](CONTRIBUTING.md) and the issues labelled
[`good first issue`](https://github.com/nagytam/iTerminal/labels/good%20first%20issue).
Architecture notes per subsystem are in [docs/architecture](docs/architecture).
```

### 7.6 Where to find contributors
- "Good first issue" aggregators (up-for-grabs.net, goodfirstissue.dev, firsttimersonly), after the labels exist.
- Hacktoberfest (October): label issues `hacktoberfest` and announce in September.
- .NET and TUI communities: r/dotnet, r/commandline, the Terminal.Gui and Spectre.Console communities, .NET community Discords.
- Localisation: ask native speakers in the language communities of any language you want to add.
- Meetups and conferences: a lightning talk ends well with slide 15.

---

## 8. Attracting sponsors

### 8.1 Pitch in one paragraph
iTerminal is free, MIT-licensed software that saves engineers time every day. Maintaining it
takes maintainer hours plus a recurring AI budget (tokens and tooling used to implement, test and
review features). Sponsorship turns that cost into predictable funding, and sponsors get public
credit and a voice in what is built next.

### 8.2 What funds are used for (state this exactly, and report on it)
1. **AI budget:** model usage and tooling for implementation, test writing, code review and docs.
2. **Maintainer time:** faster pull-request review, triage and releases.
3. **Infrastructure:** CI minutes, code-signing certificates, package repositories and hosting.
4. **Bounties (optional, once funds allow):** pay contributors for tagged issues.

### 8.3 Suggested tiers (adjust amounts and perks to what you will deliver)

| Tier | Who | Suggested perks |
| --- | --- | --- |
| **Supporter** | Individuals | Name in `SPONSORS.md`, our thanks, a sponsor badge on your profile |
| **Backer** | Power users | The above plus a vote in roadmap polls |
| **Team** | Small companies | Logo in README, release-note mention, priority triage of your issues |
| **Partner** | Companies and vendors | Larger logo, a short case study or blog post, a quarterly call with the maintainer |
| **In-kind** | AI vendors, CI and signing providers | Credits or services in exchange for a mention, treated like money in reports |

One-off donations should be possible at any level. Offering a fixed *AI-credit* goal ("help us
reach the monthly AI budget") gives donors a concrete, understandable target.

### 8.4 Set up the plumbing (before announcing)
- [ ] Turn on **GitHub Sponsors** and add `.github/FUNDING.yml`. Optionally add Open Collective, Ko-fi, Liberapay or Buy Me a Coffee; one or two channels are enough.
- [ ] Show a **Sponsor** badge and a "Support this project" section in the README.
- [ ] Create `SPONSORS.md` (or a README table) and keep it current.
- [ ] Publish a **monthly transparency note**: income, spend by category (AI budget, infrastructure, time) and what shipped.
- [ ] Clarify the legal/tax side where you live (individual vs. company, invoices for businesses). Many companies can only pay against an invoice.
- [ ] Decide what an AI-vendor sponsorship looks like (credits vs. money) and how you will credit them.

README snippet:
```markdown
## Support iTerminal
iTerminal is free and MIT licensed. If it saves you time, please consider
[sponsoring](<SPONSOR URL>). Funds go to the AI budget, review time and release
infrastructure. Thank you to all [sponsors](SPONSORS.md).
```

### 8.5 Short sponsor copy for different places
- **README / website:** "Fuel iTerminal. Your sponsorship pays for the AI budget and the review time behind every release."
- **Release notes footer:** "Thanks to our sponsors for making this release possible. Join them: `<SPONSOR URL>`."
- **Social:** "iTerminal is free and open source. If it saves you time, help fund the AI budget behind it: `<SPONSOR URL>`."
- **Conference slide:** "Sponsor the AI budget. 1 sponsor = N more fixes per month." (use a real number)

### 8.6 Principles
- Ask after the user has seen value: a sponsor link in the README, the About dialog and release notes, not in popups that interrupt work.
- Be transparent. Publish what comes in and what it buys.
- Never sell product decisions that harm the open-source users; keep the licence MIT.
- Thank people publicly and promptly.
- Do not promise delivery dates or features in exchange for money unless you can keep the promise.

---

## 9. FAQ

**Is it free?** Yes. MIT licence, free for personal and commercial use.

**Which platforms?** Windows and Linux. Released packages are self-contained, so no .NET install is needed. Building from source needs the .NET 11 SDK.

**Is it stable?** It is in beta. Keep backups of anything you cannot afford to lose.

**Does it need extra software?** Not to start. Optional tools unlock features when present: `git`, `docker`, `ffmpeg`, 7-Zip and Tesseract. The AI bar's Ollama server and model are downloaded by iTerminal on demand unless one is already running.

**Does the AI assistant send my files to the cloud?** It talks to a local Ollama server, so processing stays on your machine.

**Does it work over SSH?** Yes, it is a terminal application, so it works wherever your terminal does.

**How does it compare with Midnight Commander, Far, Total Commander or Double Commander?** It follows the same two-panel, function-key workflow and adds drives for Git, Docker, cloud and phones, a built-in Git client and a wide-ranging viewer. The repository's `docs/mc-parity.md` keeps an honest feature-by-feature comparison, including the gaps.

**Can I use the UI framework in my own app?** Yes. `iConsole` is a separate project in the same repository under the same licence.

**How do I report a bug or contribute?** Open an issue or pull request at <https://github.com/nagytam/iTerminal>. See section 7 for good first changes (translations, viewers, drives, bug fixes).

**Can I send a pull request written with AI help?** `<State your policy here, for example: yes, if you understand and have tested the change, and say so in the PR description.>`

**Why sponsor if it is free?** Sponsorship pays for the AI budget, review time and release infrastructure, so fixes and features arrive faster. The software stays free and MIT licensed.

**What happens to the money?** It is spent on the categories in section 8.2 and reported in a monthly note.

**Can my company sponsor with an invoice, or give credits instead of money?** Yes; ask via the contact address in the repository (`<contact>`).

---

## 10. Launch checklist and tips

**Before launch**
- [ ] Add `CONTRIBUTING.md`, issue and PR templates, a code of conduct and 10-15 `good first issue` items (section 7.3).
- [ ] Enable GitHub Sponsors, add `.github/FUNDING.yml`, a README sponsor block and `SPONSORS.md` (section 8.4).
- [ ] Publish a GitHub release with the Windows MSI and the Linux tar.gz / deb / rpm; paste the links into the texts above.
- [ ] Record a 60-90 second terminal GIF or video of the demo script; place it at the top of the README.
- [ ] Add 3-4 screenshots (two panels, `git://` browsing, viewer, Git client) and a social preview image.
- [ ] Tag a few "good first issue" items and enable GitHub Discussions.
- [ ] Update the README feature list so it matches this kit (several areas listed here are newer than the README).

**Where to announce (in this order)**
1. Your own mailing list / newsletter and personal networks (warm audience, early feedback).
2. Show HN (Tuesday-Thursday morning US time works well; be present to answer comments).
3. Reddit communities listed in 4.3, one or two per day, respecting self-promotion rules.
4. .NET community: .NET Blog community links, Dev.to, Hashnode, a "This Week in .NET" submission, Awesome-lists (awesome-dotnet, awesome-tuis, awesome-cli-apps).
5. Package channels once available: WinGet, Scoop, Chocolatey, an AUR package, Homebrew-on-Linux.
6. Conference and meetup lightning-talk submissions using the slide outline.
7. After the first wave: the contributor call (4.6) and the sponsor appeal (4.7) as separate posts, a week or two apart, so each gets its own attention. Send the company email (4.8) personally, never in bulk.

**After launch (keep the momentum)**
- Reply to every issue and PR within a few days; merge a first community PR quickly and celebrate it publicly.
- Post a monthly update: what shipped, who contributed, what sponsors funded.
- Re-announce at milestones (first release candidate, a new drive, a new language, 100 stars).

**Message discipline**
- Lead with the benefit ("Git tags and Docker containers are folders"), not the implementation.
- Mention beta honestly and invite feedback; ask for one specific thing per post.
- Keep the sponsor ask short and secondary in general announcements; give it its own post (4.7) for people who want to help.
- Do not claim to be "better than" other managers; say what is different.
- Answer every early comment and issue quickly; the first week sets the tone.

**Hashtags and keywords**
`#opensource #dotnet #csharp #terminal #cli #tui #filemanager #devtools #linux #windows #git #docker #sysadmin`
