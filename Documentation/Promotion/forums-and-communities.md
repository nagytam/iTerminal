# Forums and communities

**Venues:** r/commandline, r/dotnet, r/linux (and r/opensource, r/selfhosted when relevant), Lobsters,
Hacker News (Show HN is in the [marketing kit, section 4.2](iTerminal-Marketing-Kit.md)), Dev.to
(see [dev-to-article.md](dev-to-article.md)), and the .NET community channels.

## Rules

| Venue | What is known | Status |
| --- | --- | --- |
| Hacker News | Show HN is for something people can try. Landing pages, fundraisers, blog posts, newsletters and sign-up pages do not qualify. Title starts "Show HN". Title style rules were not confirmed on the official page. | Partly verified (news.ycombinator.com/showhn.html, via search) |
| Lobsters | Invite-only, computing-focused. The official rules page was not retrieved. A blog reports that self-promotion above about a quarter of posts and comments is frowned upon and that moderators do remove it. | Unverified. Read the site's rules page before posting |
| r/commandline | The sidebar was not retrieved. Mirrors report a moderator rule against largely AI-generated or unreviewed code and projects. Each subreddit sets its own self-promotion rule. Reddit returned HTTP 403 to automated requests for all five subreddits (2026-11-21), so their sidebars were not read. | Unverified. Read the sidebar and wiki first |
| r/dotnet, r/linux, r/opensource, r/selfhosted | Each subreddit has its own sidebar rules. Assume a self-promotion limit. Reddit returned HTTP 403 to automated requests for all five subreddits (2026-11-21), so their sidebars were not read. | Unverified. Read the sidebar first |
| .NET newsletters ("This Week in .NET") | No current submission route was found; the search returned only archived 2015–2016 issues. | Not verified; do not submit until a route is confirmed |

**Before any post:** read the sidebar and the wiki of that subreddit or site the same day; check the
most recent posts from moderators; post as the author, say you wrote it, and stay to answer comments.
Post at most one venue per day. Do not ask friends to upvote or comment.

## 1. r/commandline

**Title:** I built an open-source two-panel file manager for the terminal (C#/.NET 11) where Git tags, Docker containers and SSH hosts are folders

```text
I wrote iTerminal because I wanted the Midnight Commander / Far workflow on today's systems,
with git, docker, ssh and archives as first-class folders rather than separate tools.

What it does, in short:
- Two panels and the usual F-keys; copy and move run as background tasks with progress.
- Git revisions, tags and branches browse without checkout (git://), and copying from a tag to an SSH host is the same F5 as any other copy.
- Docker containers, SSH and SMB hosts, archives and disk images open as folders.
- Ctrl+O hands the whole terminal to your shell and takes it back.
- Built-in Git client, .NET solution viewer, full-text document search, six UI languages.

It is C# on .NET 11, MIT licensed, for Windows and Linux (self-contained packages). It is beta,
so the things I most want to hear are what breaks in your terminal emulator and which drive or
viewer you would want next.

Repository: https://github.com/nagytam/iTerminal
```

*Attach a short recording (GIF) if the subreddit allows media in the post.*

## 2. r/dotnet

**Title:** A terminal file manager in C# on .NET 11, plus iConsole, the text-UI framework behind it (open source)

```text
iTerminal is an MIT-licensed, two-panel file manager and developer toolbox for the terminal.
The part that may interest .NET developers more is iConsole, the text-UI framework it is built on:
controls, jobs, a diffing renderer that writes only changed cells, notifications and localisation
from .resx files. The code is organised so that a control or a dialog can be developed and tested
without a real terminal, and the end-to-end suite boots the whole app on a virtual terminal.

Some things I would be glad to discuss: how the renderer diffs buffers, why key handlers have a
200 ms budget and how that is enforced in tests, and how the URL-scheme drives (git, docker, ssh,
archives) plug into one panel model.

Repository: https://github.com/nagytam/iTerminal
Architecture notes are in the Documentation folder of the repository.

It is a beta. Feedback and pull requests are welcome.
```

## 3. r/linux (only when the user wants a Linux-focused post)

**Title:** iTerminal: an open-source, keyboard-driven file manager for the terminal, with Git, Docker and SSH as folders (deb, rpm, tar.gz)

```text
iTerminal is a two-panel file manager and developer toolbox for the terminal. It is written in
C# on .NET 11, MIT licensed, and ships as tar.gz, deb and rpm packages (self-contained, so no .NET
install is needed). Audio playback uses PulseAudio or ALSA; ffmpeg and 7-Zip are optional.

It is beta. The most useful feedback is about terminal emulator behaviour, key handling and
packaging on your distribution.

Repository: https://github.com/nagytam/iTerminal
Release: <RELEASE URL>
```

## 4. Lobsters (only after the rules page has been read and an invite is available)

**Title:** iTerminal: a two-panel file manager where Git revisions and Docker containers are folders

Use the GitHub URL as the link and the first paragraph of the r/commandline post as the comment.
Lobsters asks for a considered comment rather than a pitch. Write it as a maintainer's comment:
what the design choice is (every location is a URL-scheme drive), and what you would like feedback on.
Do not post it if you cannot stay to answer comments.

## 5. Show HN

Use the marketing kit's Show HN text ([section 4.2](iTerminal-Marketing-Kit.md)). It is valid only
once the release can be downloaded and run by someone who is not the author. Check the title rules
at news.ycombinator.com/newsguidelines.html before posting.

## Not sent yet

None of these have been posted. Each needs the user's account, the subreddit or site rules read on the
day, and the release link.

**Reddit status (2026-11-21):** nothing has been posted to r/commandline, r/dotnet, r/linux, r/opensource or
r/selfhosted. The subreddit rules are still unverified: Reddit blocked the automated reads with HTTP 403. The
marketing kit's Reddit post (section 4.3) is ready, but each subreddit's sidebar and wiki must be read by the user
on the day of posting, and only one venue is posted per day.
