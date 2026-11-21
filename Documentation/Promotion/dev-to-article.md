# Dev.to article: "Every place I work is a folder"

**Venue:** Dev.to, tagged `showdev` (the project tag; moderators say tutorials belong under
`tutorial`, so keep the post about the project and the design). Also suitable as a blog post on the
author's own site, cross-posted with a canonical link.

**Rules**
- #showdev is for projects. A moderator's note says tutorial-style posts get moved; this article is a project write-up with design notes, so keep it that way.
- The tag limit (four tags) is *unverified*. Check the tag guidelines on the tag page on the day.
- Do not post the same text on several platforms on the same day; set the canonical URL if the post is cross-published.

**Suggested tags:** showdev, dotnet, csharp, opensource (choose at most four, after checking the limit)

**Suggested cover image:** a screenshot of the two-panel view with a `git://` tag on the left and a
local folder on the right. Make it yourself from the app; do not use a stock image.

---

## Every place I work is a folder

I wanted the Midnight Commander workflow on the systems I use now. My day already has a Git repo
on one screen, a container's logs on another, an SSH host in a third, and an archive somewhere
in between. Each of them has its own tool and its own set of keys. So I built iTerminal: a
two-panel, keyboard-driven file manager for the terminal where those places are all folders.

It is in beta, it is MIT licensed, and it runs on Windows and Linux. It is written in C# on .NET 11.

### The idea: a drive is a URL scheme

Each location is a drive with a URL scheme, and a panel shows any of them:

- `file://` for the local disk
- `ssh://` and `smb://` for remote hosts and shares
- `git://` for any revision, branch, tag or commit, browsed without checking anything out
- `docker://` for container file systems, with logs and start, stop and restart
- `archive://`, `zip://` and `targz://` for archives, and disk images with 7-Zip
- `recycle://` for the recycle bin, and `search://` for search results

Because they are all folders, one command works across them. Copying from a Git tag to a running
container is the same F5 as copying between two local directories. The operation runs as a background
task with progress, so the interface stays responsive.

### Why a URL, not a special case

The panel does not know what a Git revision is. It knows a URL, asks the drive for a listing, and
shows the entries. Each drive is a small class with a clear contract. That makes it possible to add
a new location without touching the panels. It also means the things that go wrong are the same across
drives: a path that does not exist, a listing that is slow, a network that drops.

Slow drives are the hard part. Key handlers must return within about 200 ms, so a listing that might
take a second to arrive runs on a background task and lands in the panel when it is ready. A stale
result, from a panel that has since moved to a different folder, is discarded. That check is what
keeps the panel from showing the wrong folder after a slow SSH response.

### A toolbox, not only a file manager

The same window holds a Git client (stage, commit, branches, stashes, push and pull), a .NET
solution viewer with build and test, a viewer for many file types, full-text search over documents,
and a music player. Ctrl+O hands the whole terminal to your shell, and takes it back when the shell
exits, so the tool does not get in the way when you need a full prompt.

### What it is not yet

It is beta. The feature list is broad and some parts are newer than the documentation. The best
feedback at this stage is: what breaks in your terminal emulator, which drive or file format you
would want next, and where a key is hard to reach on your keyboard layout.

### Try it

- Repository and downloads: https://github.com/nagytam/iTerminal
- Release: <RELEASE URL>
- Architecture notes for each subsystem are in the Documentation folder of the repository.

If you want to contribute, the codebase is organised so that a change can be small: a new drive, a
reader for a file format, a translation, or a bug fix. Issues and pull requests are welcome.

---

*Author note before publishing: check every feature named here against the release you link, and
replace the release placeholder. Do not add a sponsor call to this article; the marketing kit's
sponsor posts are separate (sections 4.7 and 8).*
