# Promotion overview

Where iTerminal can be promoted, what each venue requires, and a log of the promotion work done.
The content for each venue is in [Documentation/Promotion](../Promotion/README.md). The copy this
builds on is the [marketing kit](../Promotion/iTerminal-Marketing-Kit.md).

**Basis.** Venue rules come from web searches run on 2026-11-21, across two passes. Where a rule was
read on the venue's own page it is marked **verified**, with the URL where useful. Where it came only
from a search snippet, a mirror or a third-party guide it is marked **unverified**, or **partly
verified** when some of the rule was read and some was not. Nothing has been submitted, posted, sent
or signed up for. Every outward action is the user's to take.

Status values: `prepared` (content written, not sent), `user to submit` (needs the user's account or
decision), `waiting for user` (blocked on a fact or decision only the user has), `skipped` (reason
given below).

## 1. Venues and rules

| # | Venue | Type | How to submit | Rule status | Content file | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | rothgar/awesome-tuis | Awesome-list (TUI apps) | Pull request to the list | Verified from `.github/pull_request_template.md`: repository at least 6 months old (a workflow checks the first-commit date on README PRs); unique application and interface; no wrappers; interactive or redrawing interface. iTerminal is not eligible before 2027-04-15 | [awesome-lists.md](../Promotion/awesome-lists.md) | blocked until 2027-04-15 |
| 2 | awesome-dotnet (quozd/awesome-dotnet) | Awesome-list (.NET) | Pull request, one link per PR, title "Add <project>" | Partly verified: one link per pull request and a title of the form "Add <project>" are stated in the guide as seen in search results (not read in full); the `[Free for OSS]` tag requirement is unverified | awesome-lists.md | prepared |
| 3 | tuxarch/cli-apps (awesome-cli-apps family) | Awesome-list (CLI and TUI apps) | Pull request editing a CSV file under `data/` in the newer forks | Unverified. The older guide (mirror of agarrharr/awesome-cli-apps) asks for apps older than 90 days with more than 20 stars and one PR per app; whether that still holds is unknown | awesome-lists.md | prepared |
| 4 | Console.dev | Developer-tool newsletter | Email hello@console.dev (the About page) | Partly verified: selection criteria read on the site (regular-use tool, fits the development flow). The site says it does not do sponsored reviews | [directories-and-newsletters.md](../Promotion/directories-and-newsletters.md) | user to submit |
| 5 | Terminal Trove | Tool directory | Submit form at terminaltrove.com/submit | Partly verified from the form summary: cross-platform preferred, standalone binaries preferred, image preview required (PNG, GIF or MP4) | directories-and-newsletters.md | user to submit (needs a preview image) |
| 6 | AlternativeTo | Software directory | Free account, "Suggest new application" | Verified from the FAQ: changes are reviewed and may be refused; one listing per app, no separate free and pro versions | directories-and-newsletters.md | user to submit |
| 7 | Hacker News (Show HN) | Forum | Submit with a title beginning "Show HN" | Partly verified: the Show HN page requires something others can try; landing pages, fundraisers, blog posts and newsletters do not qualify. The title rules were not confirmed on the official page | [Marketing kit 4.2](../Promotion/iTerminal-Marketing-Kit.md) | user to submit (after the release is downloadable) |
| 8 | Lobsters | Forum (invite-only) | Account needed; invite required | Unverified: the official rules page was not retrieved. A blog reports that self-promotion above about a quarter of posts and comments is frowned upon and that moderators enforce it | [forums-and-communities.md](../Promotion/forums-and-communities.md) | prepared; a user with an invite posts |
| 9 | r/commandline, r/dotnet, r/linux, r/opensource, r/selfhosted | Reddit | Post under each subreddit's rules | Unverified: Reddit returned HTTP 403 to automated reads (2026-11-21), so no sidebar was retrieved. Mirrors report a rule against largely AI-generated or unreviewed code. Each subreddit sets its own self-promotion rule | forums-and-communities.md | not posted; prepared; read each sidebar first |
| 10 | Dev.to (#showdev) | Blog community | Post with tag `showdev` (for a project) | Partly verified from moderator writing: #showdev is for projects, tutorials are better under #tutorial. The four-tag limit is unverified | [dev-to-article.md](../Promotion/dev-to-article.md) | prepared |
| 11 | This Week in .NET | .NET newsletter | Not found | Not verified: the search returned only archived 2015–2016 issues and no current submission route | [forums-and-communities.md](../Promotion/forums-and-communities.md) | skipped until a route is confirmed |
| 12 | Product Hunt | Launch site | Submit a new product from the product page; launch scheduled up to a month ahead | Verified from the official launch guide: relaunch needs substantive updates; paying a hunter breaks the guidelines; vote rings are penalized | [launch-and-press.md](../Promotion/launch-and-press.md) | user to submit (optional) |
| 13 | heise Download | German software portal | Developer account required; a "Programm vorschlagen" link on category pages | Partly verified: the developer function needs a heise online account. The submission requirements were not found | [launch-and-press.md](../Promotion/launch-and-press.md) | user to check |
| 14 | Phoronix | News site (Linux) | No tip address found; a donation page exists | Not verified: no tip submission route was found. Contact by email to the site is a judgement call for the user | launch-and-press.md | skipped until a route is confirmed |
| 15 | Full Circle Magazine | Free Ubuntu and Linux magazine | Email the article to the articles address; how-to articles are needed | Partly verified from an old page on the magazine's site (about 3,000 days old): articles by email, guidelines to read first. The address may be out of date | [launch-and-press.md](../Promotion/launch-and-press.md) | user to check the current guide |
| 16 | Linux Magazine | Print and web magazine | Send a 1–2 paragraph proposal by email, subject "Proposal" or "Manuscript" | Partly verified from the magazine's "Write for Us" page: pitch first; the sent date matters because print lags | launch-and-press.md | prepared |
| 17 | OMG! Ubuntu | Linux news site | Tip form with a link (<https://omgubuntu.co.uk/tip>) | Verified: name and message required, link requested, no promise of coverage | [linux-news-and-software-sites.md](../Promotion/linux-news-and-software-sites.md) | prepared; user to submit |
| 18 | LinuxLinks | Linux software directory | Suggestion form (<https://linuxlinks.com/suggest-open-source-program/>) | Verified: needs a repository, license, and a public AI-use disclosure URL when generative AI was used; projects developed largely with generative AI are unlikely to be featured. The linked AI-heavy policy page was not read | linux-news-and-software-sites.md | waiting for user (AI-disclosure decision) |
| 19 | Changelog News | Developer link feed | Signed-in submission (<https://changelog.com/news/submit>) | Verified: excludes how-tos and commercial products; submitter must say why it is newsworthy | linux-news-and-software-sites.md | prepared; user to submit |
| 20 | It's FOSS News | Linux news site | Contact page, news only | Unverified: the contact URL returned 404; the rule comes from a search snippet | linux-news-and-software-sites.md | waiting for user (find the current form) |
| 21 | up-for-grabs.net | Contributor listing | Pull request adding a YAML file (<https://github.com/up-for-grabs/up-for-grabs.net/blob/gh-pages/docs/list-a-project.md>) | Verified: needs active maintainers and a list of beginner-friendly issues | [contributor-listings.md](../Promotion/contributor-listings.md) | waiting for user (labelled issues must exist) |
| 22 | Debian archive (and Ubuntu through Debian) | Distribution, official | ITP bug, source package, mentors.debian.net upload, RFS bug, sponsor uploads, NEW queue | Verified on <https://mentors.debian.net/intro-maintainers/>, <https://www.debian.org/devel/wnpp/>, <https://wiki.debian.org/Packaging/Intro>: sponsor needed, source build only, ftpmaster approval of new packages. Turnaround and NEW criteria unverified. | [linux-distribution-packaging.md](../Promotion/linux-distribution-packaging.md) | waiting for user (blocked: no usable .NET build toolchain in Debian, NuGet and native binaries; nothing to file now) |
| 23 | Launchpad PPA (Ubuntu) | Distribution, self-service | Signed source package uploaded with `dput` to the user's own PPA | Partly verified from search summaries of <https://documentation.ubuntu.com/project/contributors/new-package/upload-packages-to-a-ppa/>: source only, signed with the account's GPG key, no reviewer | linux-distribution-packaging.md | waiting for user (needs `debian/` source package, GPG key, Launchpad account) |
| 24 | Fedora official repositories | Distribution, official | Review Request bug in Red Hat Bugzilla, sponsor, review by a packager, `fedpkg request-repo` | Unverified: the Fedora docs returned a bot-protection page; the process comes from search summaries of <https://fedoraproject.org/wiki/PackageReviewProcess> and <https://bugzilla.redhat.com/show_bug.cgi?id=177841>. Offline-build and bundling rules from a .NET SIG list thread and <https://pagure.io/packaging-committee/issue/1193> (unverified) | linux-distribution-packaging.md | waiting for user (blocked: needs a spec that builds offline on Fedora's .NET, and a sponsor) |
| 25 | Fedora COPR (also usable for RHEL clones and EPEL chroots) | Distribution, self-service | FAS account, project, SRPM or git build | Partly verified from search summaries of <https://docs.fedoraproject.org/en-US/quick-docs/publish-rpm-on-copr/> | linux-distribution-packaging.md | waiting for user (needs a spec and SRPM, FAS account) |
| 26 | openSUSE Build Service (home project, then devel project and Factory) | Distribution | OBS account, spec in a home project; submit request to a devel project, then Factory | Unverified: search summaries of <https://en.opensuse.org/openSUSE:How_to_contribute_to_Factory>; one concept page returned HTTP 403. Factory has automatic and manual legal review | linux-distribution-packaging.md | waiting for user (home project possible with a new spec; Factory not realistic now) |
| 27 | Arch User Repository (AUR) | Distribution, self-service | AUR account, SSH key, `PKGBUILD` and `.SRCINFO` pushed to the AUR git repository; `iterminal-bin` over the release tar.gz | Unverified: ArchWiki returned a bot-protection page; rules from search snippets of <https://wiki.archlinux.org/title/AUR_submission_guidelines> | linux-distribution-packaging.md | waiting for user (needs a release and a PKGBUILD; no change to the build) |
| 28 | Alpine aports (`testing`) | Distribution, official | Merge request on gitlab.alpinelinux.org/alpine/aports with an `APKBUILD` | Unverified: the contributing page returned HTTP 404; testing-first workflow from search summaries | linux-distribution-packaging.md | skipped for now (needs a musl build and a source build; narrow audience) |
| 29 | Gentoo GURU overlay | Distribution, community overlay | Pull request or patch to GURU, Bugzilla request for commit access later | Verified on <https://wiki.gentoo.org/wiki/Project:GURU>: `dev` branch, `~arch` only, contributor must maintain it. `dotnet-pkg` eclass details unverified | linux-distribution-packaging.md | waiting for user (needs an ebuild; a `-bin` ebuild over the tar.gz needs no build change) |
| 30 | nixpkgs (NixOS) | Distribution, official | Pull request adding `pkgs/by-name/it/iterminal/package.nix`, `buildDotnetModule`, `deps.json` | Verified on <https://github.com/NixOS/nixpkgs/blob/master/pkgs/README.md>: title format, `meta.maintainers`, source builds preferred. `buildDotnetModule` details partly verified on the .NET manual section | linux-distribution-packaging.md | waiting for user (most realistic official route; needs a Nix expression, lockfile and a named maintainer) |
| 31 | Flathub (Flatpak) | Cross-distribution store | Pull request to `flathub/flathub` branch `new-pr` | Verified on <https://docs.flathub.org/docs/for-app-authors/submission> and <https://docs.flathub.org/docs/for-app-authors/requirements>: console software is not accepted; no AI-generated manifest content | linux-distribution-packaging.md | skipped (stated rule excludes console apps) |
| 32 | Snap Store | Cross-distribution store | Snap Store account, `snapcraft.yaml`, upload; classic confinement via a forum request | Unverified: the classic-confinement page returned only a redirect; rules from search summaries | linux-distribution-packaging.md | skipped for now (strict confinement unworkable for a file manager; classic uncertain) |
| 33 | AppImage | Cross-distribution format | Publish as a release asset; catalogue by pull request | Partly verified: format page read; catalogue rules not read | linux-distribution-packaging.md | skipped (no gain over the portable tar.gz) |
| 34 | Homebrew (homebrew-core or an own tap) on Linux | Package manager | `homebrew-core` pull request; or an own tap, no approval | Partly verified on <https://docs.brew.sh/Acceptable-Formulae>: stable immutable tag, builds on the Linux CI, beta software not eligible. Notability threshold unverified (search summary) | linux-distribution-packaging.md | skipped for homebrew-core (beta); tap optional |
| 35 | Own signed apt and dnf repository | Self-hosted | Project hosts the `.deb` and `.rpm` with signed metadata | Not a venue with rules; tooling is the project's choice | linux-distribution-packaging.md | waiting for user (needs a signing key and a CI step) |

## 2. Venues considered and skipped

- **Linux Journal:** the author guide found belongs to a magazine that no longer publishes. Not pursued.
- **Linux Gazette:** the same; archived guide only.
- **Full Circle Literary (agency):** unrelated name collision. Not pursued.
- **2026-11-21, Reddit status:** the sidebar of all five subreddits was requested and Reddit returned HTTP 403 to every automated request. Nothing was posted. The rules stay unverified and the user reads each sidebar on the day of posting.
- **Reddit as a whole:** the general self-promotion norm (a share of posts) is a rule of thumb from community comments, not a Reddit policy. Each subreddit's sidebar governs.
- **Awesome .NET Newsletter, C# Digest, Dotnet Weekly:** no submission route found in search results. A ".NET News" newsletter takes news through pull requests, but it concerns .NET Foundation projects; iTerminal is not one as far as the repository shows.
- **German press (Linux-Magazin, Golem, ubuntuusers, Linuxnews):** the searches returned no submission pages. Not drafted; heise (venue 13) remains the only German route found. A German-language pitch should follow only after the user confirms the language and a contact address.
- **Hungarian sites (Linuxportal, Prohardver):** no current pages found. Only an old Linuxvilág call for programs came up, and that is archive material. The author's own Hungarian communities are a user decision.
- **LWN, Hackaday, Linuxiac, 9to5Linux:** no tip route found in search.
- **Ubuntu Universe as its own route:** a third-party app arrives there by a Debian sync or an Ubuntu developer's upload, so it is covered by venues 22 and 23. No separate application exists.
- **EPEL, CentOS Stream, RHEL:** EPEL packages are branched from Fedora packages, so they follow venue 24 first; CentOS Stream and RHEL take packages only through their own vendors. Unverified (no page read). Users of those systems can use the project's RPM or COPR (venue 25).
- **Debian derivatives (Linux Mint, Pop!_OS, Kali and others):** they take packages from Debian or Ubuntu; no separate submission route was looked for.
- **Arch official repositories (`extra`):** packages move there only when a Package Maintainer adopts a popular AUR package; no application route. Rule text is from 2011–2024 mailing-list threads and unverified. Use the AUR (venue 27).
- **Alpine `community` and `main`:** new packages start in `testing` (venue 28).
- **Flathub, Snap, AppImage, homebrew-core:** see venues 31–34 for the reasons.

## 3. Facts the content relies on

- Name, license (MIT, checked against `LICENSE`), platforms (Windows and Linux; MSI, tar.gz, deb and rpm
  per `.github/workflows/release.yml`), stack (C# on .NET 11), the beta status, and the six interface
  languages (the six `Strings*.resx` files) come from `README.md` and the marketing kit's fact table.
- The release link and the sponsor link are not yet known; every file uses placeholders.
- Feature claims in the content were checked against the README. Before any send, the release
  notes must confirm the feature list (the kit's own checklist says the README is behind).
- `CONTRIBUTING.md` already states the AI-assisted development publicly. The marketing kit's section
  7.3 says the repository has no `CONTRIBUTING.md`, `SPONSORS.md` or similar; those files now exist,
  so that checklist is out of date. It has not been edited.

## 4. Activity log

- **2026-11-21, first pass:** Created the promoter agent (`.claude/agents/promoter.md`). Ran 18 web
  searches across forums, directories, newsletters, awesome-lists and press. Moved the marketing kit
  from `Documentation/Marketing/` to `Documentation/Promotion/` at the user's request. Wrote the
  content files for venues 1–16 and the index in `Documentation/Promotion/README.md`.
- **2026-11-21, second pass:** Read the existing overview and Promotion files first. Ran six searches
  (Changelog, .NET newsletters, Linux news tip forms, German press, up-for-grabs and LWN/Hackaday,
  Hungarian press) and read five venue pages. Wrote
  `Documentation/Promotion/linux-news-and-software-sites.md` and
  `Documentation/Promotion/contributor-listings.md` for venues 17–21, and added both to the index.
- **2026-11-21, consolidation:** Merged the two passes into this single overview: one venue table,
  one skipped list, one activity log.
- **2026-11-21, Linux distribution packaging pass (venues 22–35):** At the user's request, checked how
  the tar.gz, deb and rpm that `iTerminal.Packaging.Linux` and `release.yml` already build can be
  approved and integrated into distributions. Read `build-and-packaging.md`, `release.yml`,
  `iTerminal.csproj` and `iTerminal.Core.csproj` for what exists (all self-contained; the RPM unsigned;
  `0.0.0.<run>` versions; native NuGet binaries). Ran ten searches and about fifteen page fetches. Pages
  read in full or in part: Debian mentors intro, WNPP, packaging intro and NEW queue; Flathub submission
  and requirements; nixpkgs `pkgs/README.md` and the .NET manual section; Gentoo GURU; Homebrew Acceptable
  Formulae; the AppImage introduction. Pages that returned a bot-protection page, 403 or 404 (Fedora docs,
  ArchWiki, OBS concept page, Alpine contributing, Snap docs): their rules are marked unverified. Wrote
  `Documentation/Promotion/linux-distribution-packaging.md` (per-route how-to, what iTerminal must change,
  what can be done now) and indexed it. Findings in short: nothing can be filed in a main archive (Debian,
  Fedora, openSUSE Factory) now, because of the offline NuGet build and the native NuGet binaries; the
  routes that need no approval and work from today's tar.gz are the AUR `-bin` package, a Gentoo GURU
  `-bin` ebuild and an own signed apt and dnf repository; COPR, OBS home project and a PPA need a spec or
  `debian/` source package first; nixpkgs is the most realistic official route; Flathub excludes console
  apps by its stated rule.
- **2026-11-21, awesome-tuis attempt:** Prepared the PR (fork under `nagytam`, one line under File
  Managers). Stopped before the pull request: the list's template requires a repository at least six
  months old, and iTerminal's first commit is 2026-10-15. The fork was created in error and is to be
  deleted; the token needs the `delete_repo` scope first. Rule recorded in `awesome-lists.md`; venue 1
  blocked until 2027-04-15.
- Nothing was sent, submitted, posted or signed up for in any pass (the awesome-tuis fork is the one
  exception; it is not a submission).

**Waiting for the user:**
- Choose the first wave (see section 5).
- Confirm the release link (`<RELEASE URL>`) and that a downloadable build exists before any Show HN or
  directory submission.
- Provide a preview image for Terminal Trove.
- Obtain a Lobsters invite if Lobsters is wanted.
- Decide whether to submit to LinuxLinks at all, since it asks about generative-AI use and may exclude
  heavily AI-built projects. If yes, give a public disclosure URL (`<AI DISCLOSURE URL>`).
- Confirm that `good first issue` and `help wanted` labels exist with open issues before the
  up-for-grabs.net pull request (`<LABEL ISSUE LIST URL>`).
- Provide a submitter name and email for the forms that need them, and a changelog.com account for
  Changelog News.
- Find the current contact form for It's FOSS News.
- Linux packaging (venues 22–35): decide whether to aim at the main archives or at self-service routes
  first; settle a real version scheme and tag; choose the maintainer name and address that distribution
  tooling may carry; find a packager for Arch, Gentoo or Nix (none is assumed); create a GPG key and the
  accounts each route needs (Launchpad, Fedora FAS, OBS, AUR, GitHub, Gentoo Bugzilla); decide on
  packaging changes for distribution builds (RPM licence field to MIT, `/usr/bin` link, signed RPM,
  optional native NuGet packages). Read the unverified rules in a browser before filing anything.

- **Reddit post, r/VibeCodeDevs (noted at the user's request):** the user linked a post titled "Open sourced my hobby project (iTerminal) a console based file manager for the AI and security era" at <https://www.reddit.com/r/VibeCodeDevs/comments/1xvzlku/open_sourced_my_hobby_project_iterminal_a_console/>. Only the title was supplied. Reddit blocked the fetch from this environment, so the post body, the date, the author and the comments are **unverified** and are not recorded here. It is not a venue row because no submission route or rule was read; whether the user posted it is also unverified. Reddit rules for each subreddit remain as venue 9.

## 5. Suggested order

1. Directory and list entries with no audience risk: the awesome-list pull requests (tuxarch, awesome-tuis, awesome-dotnet), then AlternativeTo and Terminal Trove.
2. Newsletter pitch: Console.dev, by email, once a release link exists.
3. Community posts, one per day, each tailored to its rules after reading its sidebar: Show HN first (Tuesday to Thursday morning US time, the kit's advice, unverified), then r/commandline and r/dotnet, then Dev.to.
4. News tips, once a release link exists: OMG! Ubuntu, Changelog News (only if the release is newsworthy as news, not a how-to), and LinuxLinks if the AI-disclosure decision is made.
5. Contributor listing: up-for-grabs.net, after the labelled issues are confirmed.
6. Press: Linux Magazine proposal; a short tip to Full Circle after its current guide is read.
7. Launch site (Product Hunt) only if the user wants a launch day, and never with paid hunting.
8. Linux packaging, in this order (details in [linux-distribution-packaging.md](../Promotion/linux-distribution-packaging.md)): the release tar.gz, deb and rpm on GitHub Releases first; then the self-service routes that need no approval (AUR `-bin`, Gentoo GURU `-bin`, an own signed apt and dnf repository); then COPR, an OBS home project and a PPA once a spec and `debian/` files exist; nixpkgs as the one official route worth trying; Debian and Fedora archives only after the .NET offline-build problem has an answer. Do the packaging routes before the Show HN and press posts if possible, since "how do I install it" is the first comment those venues get.

## 6. Open questions for the user

- Is there a downloadable release for the Windows MSI and Linux packages yet? Most venues need one.
- Which languages should the press pitch be written in? German tech press and the Hungarian community are natural fits given the six-language UI; drafts are in English only.
- Is an email from the author's own address acceptable for press and Console.dev, or should a project address be used?
- Is AI-assisted development something to state in every announcement? LinuxLinks asks directly, and Changelog judges by audience. `CONTRIBUTING.md` already says so publicly.
