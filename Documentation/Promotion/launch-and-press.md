# Press, magazines, launch site and talks

**Venues:** Linux Magazine (proposal), Full Circle Magazine (article), Phoronix (news tip, route not
confirmed), heise Download and German tech press (listing and news), Product Hunt (launch), and a
five-minute lightning talk for a meetup.

## Rules

| Venue | What is known | Status |
| --- | --- | --- |
| Linux Magazine | Send a one- or two-paragraph proposal by email; subject line includes "Proposal" or "Manuscript". Print lead time means dates in an announcement can be old when the issue appears. | Partly verified ("Write for Us" page, via search) |
| Full Circle Magazine | Articles by email to the articles address; how-to articles are needed; read the guidelines first. The page found is about 3,000 days old. | Partly verified, may be outdated |
| Phoronix | No tip address or submission form found. A donation page exists. | Not verified. Use only if the user finds a contact |
| heise Download | Developer account needed (free). A "Programm vorschlagen" link exists on category pages. Submission requirements not found. | Partly verified |
| Product Hunt | Official guide: launches up to a month ahead or immediately; up to three tags; relaunch needs substantive updates; paying a hunter, or vote rings, break the guidelines. | Verified from the official launch guide |

## 1. Linux Magazine: proposal

**To:** the editorial address on linux-magazine.com "Write for Us" (check the current address)
**Subject:** Proposal: a two-panel file manager that treats Git, Docker and SSH as folders

```text
Dear editors,

I would like to propose an article on iTerminal, an open-source (MIT), two-panel file manager for
Linux and Windows, written in C# on .NET 11.

The article would explain how the tool models every location as a drive with a URL scheme, so that
a Git revision, a Docker container, an SSH host and an archive can be used as a folder with the same
keys. It would walk through three practical tasks: browsing a Git tag without checkout and copying
files out of it; copying a directory into a running container; and synchronising a local folder with
an SSH host. It would close with how to package and try the tool on Debian, Fedora or a tarball.

I am the author of the project. The article would include screenshots and would follow the length
the magazine's guidelines give. Happy to adapt the outline.

Repository: https://github.com/nagytam/iTerminal
Release: <RELEASE URL>

Best regards,
<YOUR NAME>
```

*Before sending: confirm the length and payment terms on the current author guidelines (not found in
the search), and confirm the article's three tasks are what the release actually does.*

## 2. Full Circle Magazine: article

**To:** the articles address from the magazine's current writers' guide (the one found is old)
**Subject:** Article proposal: iTerminal, a file manager with Git, Docker and SSH as folders

```text
Hello,

I would like to write a how-to article for Full Circle about iTerminal, an open-source, MIT-licensed
file manager for the terminal that runs on Linux and Windows.

The article would install the Linux package (tar.gz, deb or rpm), open a Git repository's history
as a folder, and show how to move files between a local folder and an SSH host with the same keys.
It would be aimed at Ubuntu users, with screenshots prepared to the image limits in the current guide.

Repository: https://github.com/nagytam/iTerminal
Release: <RELEASE URL>

Thank you,
<YOUR NAME>
```

## 3. Phoronix: news tip (only if a contact is confirmed)

No tip route was found. If the user finds an address on the site, a tip should be short and factual,
and should not ask for coverage.

```text
Subject: Tip: iTerminal, an open-source terminal file manager (C#/.NET 11, MIT)

iTerminal is a new beta of an open-source two-panel terminal file manager for Linux and Windows.
It treats Git revisions, Docker containers, SSH hosts and archives as folders. It is MIT licensed.

Repository: https://github.com/nagytam/iTerminal
Release: <RELEASE URL>
```

## 4. German tech press and heise Download

Use German for German-language outlets. The app ships with German, and a German-language note is the
natural fit. This draft is in English; a German version is for the user to request.

**Pitch (short, for an editor's inbox)**

```text
Subject: Open-source Dateimanager für das Terminal: Git, Docker und SSH als Ordner

iTerminal ist ein Zwei-Fenster-Dateimanager für das Terminal im Stil von Midnight Commander,
geschrieben in C# auf .NET 11, MIT-lizenziert, für Windows und Linux. Git-Revisionen, Docker-Container,
SSH-Hosts und Archive werden als Ordner geöffnet, mit denselben Tasten. Die Oberfläche gibt es in
sechs Sprachen, darunter Deutsch.

Das Projekt ist im Beta-Stadium; Rückmeldungen sind willkommen.

Repository: https://github.com/nagytam/iTerminal
Download: <RELEASE URL>
```

For heise Download: create the developer account, then use the program suggestion link on the Linux
or Windows category page. Use the short description above.

## 5. Product Hunt (optional)

Only when the user wants a launch day. Choose a date a few weeks ahead. Do not pay anyone to hunt or
send traffic; the guide treats that as a breach.

- **Name:** iTerminal
- **Tagline (60 characters or less):** A two-panel terminal file manager with Git, Docker and SSH folders
- **Description:** Use the short description from the directory listing, plus one sentence on beta status.
- **Link:** https://github.com/nagytam/iTerminal
- **Gallery:** a short recording of the two-panel view and a `git://` browse; a screenshot of the Git client.
- **First comment (author):** Explain why it exists in three sentences and ask for the one thing you want: "tell me what breaks in your terminal emulator".
- **Tags:** up to three from the launch tags list (for example developer tools, open source, Linux).

Reply to every comment on launch day. Do not ask friends to upvote.

## 6. Lightning talk (five minutes)

For meetups and conferences, the marketing kit's 15–20 minute outline is the long version. This is a
short one.

| Time | Slide | Content |
| --- | --- | --- |
| 0:00 | Title | iTerminal: every place I work is a folder |
| 0:30 | The problem | Four tools, four key sets: Git, Docker, SSH, archives |
| 1:00 | One screen | Live demo of two panels: a Git tag on the left, a local folder on the right |
| 2:00 | Drives are URLs | `git://`, `docker://`, `ssh://`, `archive://`: one panel model |
| 3:00 | Responsiveness | Key handlers within about 200 ms; slow work on background tasks |
| 4:00 | Get it, help | MIT, Windows and Linux packages, beta; the contribution entry points |
| 4:40 | Ask | One thing: try it and tell me what breaks |

Demo fallback: record the demo beforehand in case the terminal or the network fails.

## Not sent yet

Nothing here has been sent, submitted or published. Each item needs the user's decision, the current
contact address (checked the same day), and the release link.
