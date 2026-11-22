# Lemmy c/commandline, Linux Today, It's FOSS (corrected route), awesome-dotnet by issue

**Venues:** the Lemmy community `commandline@lemmy.ml`; the Linux Today contribution form; the It's
FOSS news form (the working page found this run); `aloneguid/awesome-dotnet` (issue-based list).

## Rules that apply

| Venue | Rule | Status |
| --- | --- | --- |
| Lemmy `commandline@lemmy.ml` | The community description says it covers "anything regarding the command line, in any operating system" and that "tips, and interesting programs/console applications you've found or made yourself are welcome". No written self-promotion limit in the sidebar. Source: <https://lemmy.ml/c/commandline> | Verified (page read 2026-11-22). Instance-wide rules of lemmy.ml were not read; recommend reading them and keeping to a post a community member would not call spam. |
| Linux Today | A form with five fields (headline, link to the article, description, submitter name, submitter email); every submission is reviewed by a moderator before publication. The page says nothing about what is accepted or about self-promotion. Source: <https://www.linuxtoday.com/contribute-to-linuxtoday/> | Verified (page read). It asks for a link to an *article*; a release page may not qualify. Unclear, so the conservative choice is to link a write-up (the Dev.to article, once posted) rather than the repository. |
| It's FOSS News | Developers of open-source software who release a new version can tell the site and it "may cover it"; coverage depends on how important and fresh the news is and on writer availability; the news portal covers news only, reviews and tutorials belong elsewhere. A "Submit a form" link is on the page. Source: <https://itsfoss.com/contact-us-2> | Verified (page read). Replaces the 404 URL recorded in venue 20. The form fields were not visible, so they are unverified. |
| `aloneguid/awesome-dotnet` | A new link is added by opening an issue with the project details; it appears on the list "automatically if it gets at least 5 likes" or a like from the maintainer. Source: search summary quoting the README, as mirrored on <https://awesome.ecosyste.ms/lists/aloneguid%2Fawesome-dotnet> | **Unverified**: the README itself returned 404 from the paths tried (the repository page <https://github.com/aloneguid/awesome-dotnet> also returned 404 through the fetch tool), so the list may have moved or been renamed. Check before using. |

## Before sending: what the user must do

- Placeholders: `<SUBMITTER NAME>` and `<SUBMITTER EMAIL>` (Linux Today asks for both; give only what you want public), `<ARTICLE URL>` (the published Dev.to article, if Linux Today is used).
- A lemmy.ml account (or an account on an instance that federates with it) for the Lemmy post. The agent creates none.
- A GitHub account for the awesome-dotnet issue.
- Do not ask anyone to like the awesome-dotnet issue, to upvote the Lemmy post or to comment on either. The five-like rule invites that; the sensible course is to let the issue stand on its own, and it is allowed to stay unlisted.
- Check each feature line against the release notes (the kit warns that the README lags).

## 1. Lemmy post (c/commandline@lemmy.ml)

**Title:** iTerminal: a two-panel, keyboard-driven file manager for the terminal (C#, MIT, beta)

**Body:**

```text
I wrote a commander-style file manager for the terminal and would like feedback from people who
live in one. It is called iTerminal, it is MIT licensed, and it is beta.

What is different: every place is a URL, so Git revisions, Docker containers, SSH and SMB hosts,
archives and disk images open as ordinary panel folders. Copying from a Git tag to an SSH host is
the same keystroke as any other copy. Long copies and moves run as background tasks with progress.
It also has a Git client, a file viewer (F3) for many formats and a document search, and Ctrl+O
lends the whole terminal to your shell and takes it back.

It runs on Windows and Linux (MSI; tar.gz, deb and rpm), the interface is in English, German,
French, Spanish, Italian and Hungarian, and it is written in C# on .NET 11. Parts of it were
written with AI assistance; the contributing guide says how.

https://github.com/nagytam/iTerminal
Downloads: https://github.com/nagytam/iTerminal/releases/latest

What I would like to know: which keys or panels feel wrong coming from mc, ranger or Far, and what
breaks on your terminal.
```

The AI sentence is true per `CONTRIBUTING.md`; keep it, because this audience asks.

## 2. Linux Today: form entries

| Field | Entry |
| --- | --- |
| Headline | iTerminal, a two-panel file manager for the terminal, enters public beta |
| Link to article | `<ARTICLE URL>` |
| Description | iTerminal is an open-source (MIT) two-panel file manager and developer toolbox for the terminal. Git revisions, Docker containers, SSH hosts, archives and disk images open as folders. It ships as an MSI, tar.gz, deb and rpm. It is in beta. |
| Name, email | `<SUBMITTER NAME>`, `<SUBMITTER EMAIL>` |

Skip this form if there is no article to link; the page asks for one.

## 3. It's FOSS News: message

Use the OMG! Ubuntu text from [linux-news-and-software-sites.md](linux-news-and-software-sites.md)
and add the release link. Send news, not a review request; the portal says it does not take reviews.
The form is reached from <https://itsfoss.com/contact-us-2>.

## 4. awesome-dotnet (aloneguid) issue

Only after the list is found at its current address. Issue title and body:

```text
Title: Add iTerminal

Name: iTerminal
Link: https://github.com/nagytam/iTerminal
Category: command-line and console applications (or the nearest category the list has)
Description: A two-panel, keyboard-driven file manager and developer toolbox for the terminal,
written in C# on .NET 11. Open source (MIT), beta. Its text-UI framework, iConsole, is in the same repository.
```

The field names are a guess from the list's description; follow the issue template if there is one.
The quozd/awesome-dotnet pull request (#1606) is a different list and is already open.
