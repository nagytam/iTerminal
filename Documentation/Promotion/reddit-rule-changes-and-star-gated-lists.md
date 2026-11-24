# Reddit rule changes, r/csharp, and star-gated lists

Survey 6 (2026-11-24). This file corrects the Reddit plan in
[forums-and-communities.md](forums-and-communities.md) and records two lists iTerminal cannot enter yet.
Nothing here has been posted or sent.

## Venues and rules

| Venue | Rule | Source | Status |
| --- | --- | --- | --- |
| r/opensource | Since 2026-10-24 the moderators reject projects that obviously use AI, require advertised projects to be at least one year old and actively maintained, with roughly three or more contributors, and exclude common student-style projects such as command-line music players. Spam and excessive self-promotion remain a standing rule. | <https://www.linux-magazine.com/Online/News/r-opensource-Bans-AI-Assisted-and-AI-Related-Projects?category=13443> (news report of the moderator post; the post itself was not read). Also <https://www.theregister.com/a/5305392>, <https://ppc.land/r-opensource-bans-ai-built-projects-for-its-98-000-weekly-visitors/> (search summaries only) | Report verified from one article; the subreddit's own rules page is **unverified** (Reddit blocks automated reads) |
| r/csharp | The monthly side-projects thread is where mirrors show project sharing; the current rules and whether a standalone post is allowed are not known. | <https://redlib.hbubli.cc/r/csharp/comments/45h4ny/rules_update_and_regarding_recent_spambots> (2016 post only) | **Unverified** |
| alebcay/awesome-shell | GitHub projects need at least 50 stars, own projects allowed, no age rule; PR process refers to the sindresorhus/awesome guide. | <https://raw.githubusercontent.com/alebcay/awesome-shell/master/CONTRIBUTING.md> | Verified. iTerminal has 1 star (2026-11-21): **blocked** |
| Terminal Trove weekly newsletter | Showcases tools sent in through the site's submit form; no separate route. | <https://terminaltrove.com/tool-of-the-week> via search summary (the page returned 403 to a direct fetch) | **Unverified**; same route as the Terminal Trove submission in [directories-and-newsletters.md](directories-and-newsletters.md) |

## What follows for iTerminal

- Do **not** post in r/opensource. The project is about six weeks old, has one maintainer, and
  `CONTRIBUTING.md` states that it is developed with AI assistance. It fails the stated rules on three
  counts. Posting would likely be removed and could get the account banned.
- Other subreddits set their own rules and may follow the same direction. Read each sidebar and say the
  AI-assisted development in the post, as `CONTRIBUTING.md` already does. Hiding it is worse than the
  rule.
- Prefer venues that judge a tool on what it does (Show HN, Lemmy, Dev.to, directories).

## Before sending (r/csharp only)

- Read <https://www.reddit.com/r/csharp/about/rules> and the pinned threads in a browser. If a monthly
  side-projects thread exists, use it instead of a standalone post.
- Placeholders: none beyond the release link, which is `https://github.com/nagytam/iTerminal/releases/latest`.
- You need a Reddit account with normal history. No votes or comments from friends.

## Draft: r/csharp side-projects thread comment

> **iTerminal, a two-panel file manager for the terminal, written in C# on .NET 11 (beta, MIT)**
>
> I have been building a keyboard-driven file manager in the style of Midnight Commander and Far
> Manager. It runs on Windows and Linux, ships as MSI, tar.gz, deb and rpm, and the interface is
> available in English, German, French, Spanish, Italian and Hungarian. Archives, git revisions, docker
> containers and SSH hosts open as folders in a panel.
>
> It sits on a reusable text-UI framework (`iConsole`) in the same repository, and the code is
> developed with AI assistance, which `CONTRIBUTING.md` states. It is beta software: expect rough edges.
>
> Repository and releases: <https://github.com/nagytam/iTerminal>
>
> What I would like: feedback on the framework's design and on anything that breaks in your terminal.

## Considered and skipped in this file

- **ComputerWoche "Tools fuer Profis":** the call is from 2007 (page header date); stale.
  <https://www.computerwoche.de/article/2660653/die-computerwoche-honoriert-ihre-erfahrungen-2.html>
- **Fastlancer:** paid listing (39 EUR) aimed at freelancers; no free route, poor fit.
  <https://www.fastlancer.org/tool-vorschlagen/>
- **LWN:** only a 2005 submission guide found; no current route.
- **LibHunt, SourceForge, Softpedia, MajorGeeks:** no submission rules found.
- **hup.hu:** rules page returned 404; no route found. Hungarian users remain a user decision.
