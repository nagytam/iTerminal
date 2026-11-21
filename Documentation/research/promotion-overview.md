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
| 1 | rothgar/awesome-tuis | Awesome-list (TUI apps) | Pull request to the list | Unverified: the list is the largest TUI catalogue, the contribution guide was not read | [awesome-lists.md](../Promotion/awesome-lists.md) | prepared |
| 2 | awesome-dotnet (quozd/awesome-dotnet) | Awesome-list (.NET) | Pull request, one link per PR, title "Add <project>" | Partly verified: one link per pull request and a title of the form "Add <project>" are stated in the guide as seen in search results (not read in full); the `[Free for OSS]` tag requirement is unverified | awesome-lists.md | prepared |
| 3 | tuxarch/cli-apps (awesome-cli-apps family) | Awesome-list (CLI and TUI apps) | Pull request editing a CSV file under `data/` in the newer forks | Unverified. The older guide (mirror of agarrharr/awesome-cli-apps) asks for apps older than 90 days with more than 20 stars and one PR per app; whether that still holds is unknown | awesome-lists.md | prepared |
| 4 | Console.dev | Developer-tool newsletter | Email hello@console.dev (the About page) | Partly verified: selection criteria read on the site (regular-use tool, fits the development flow). The site says it does not do sponsored reviews | [directories-and-newsletters.md](../Promotion/directories-and-newsletters.md) | user to submit |
| 5 | Terminal Trove | Tool directory | Submit form at terminaltrove.com/submit | Partly verified from the form summary: cross-platform preferred, standalone binaries preferred, image preview required (PNG, GIF or MP4) | directories-and-newsletters.md | user to submit (needs a preview image) |
| 6 | AlternativeTo | Software directory | Free account, "Suggest new application" | Verified from the FAQ: changes are reviewed and may be refused; one listing per app, no separate free and pro versions | directories-and-newsletters.md | user to submit |
| 7 | Hacker News (Show HN) | Forum | Submit with a title beginning "Show HN" | Partly verified: the Show HN page requires something others can try; landing pages, fundraisers, blog posts and newsletters do not qualify. The title rules were not confirmed on the official page | [Marketing kit 4.2](../Promotion/iTerminal-Marketing-Kit.md) | user to submit (after the release is downloadable) |
| 8 | Lobsters | Forum (invite-only) | Account needed; invite required | Unverified: the official rules page was not retrieved. A blog reports that self-promotion above about a quarter of posts and comments is frowned upon and that moderators enforce it | [forums-and-communities.md](../Promotion/forums-and-communities.md) | prepared; a user with an invite posts |
| 9 | r/commandline, r/dotnet, r/linux, r/opensource, r/selfhosted | Reddit | Post under each subreddit's rules | Unverified: the r/commandline sidebar was not retrieved. Mirrors report a rule against largely AI-generated or unreviewed code. Each subreddit sets its own self-promotion rule | forums-and-communities.md | prepared; read each sidebar first |
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

## 2. Venues considered and skipped

- **Linux Journal:** the author guide found belongs to a magazine that no longer publishes. Not pursued.
- **Linux Gazette:** the same; archived guide only.
- **Full Circle Literary (agency):** unrelated name collision. Not pursued.
- **Reddit as a whole:** the general self-promotion norm (a share of posts) is a rule of thumb from community comments, not a Reddit policy. Each subreddit's sidebar governs.
- **Awesome .NET Newsletter, C# Digest, Dotnet Weekly:** no submission route found in search results. A ".NET News" newsletter takes news through pull requests, but it concerns .NET Foundation projects; iTerminal is not one as far as the repository shows.
- **German press (Linux-Magazin, Golem, ubuntuusers, Linuxnews):** the searches returned no submission pages. Not drafted; heise (venue 13) remains the only German route found. A German-language pitch should follow only after the user confirms the language and a contact address.
- **Hungarian sites (Linuxportal, Prohardver):** no current pages found. Only an old Linuxvilág call for programs came up, and that is archive material. The author's own Hungarian communities are a user decision.
- **LWN, Hackaday, Linuxiac, 9to5Linux:** no tip route found in search.

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
- Nothing was sent, submitted, posted or signed up for in any pass.

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

## 5. Suggested order

1. Directory and list entries with no audience risk: the awesome-list pull requests (tuxarch, awesome-tuis, awesome-dotnet), then AlternativeTo and Terminal Trove.
2. Newsletter pitch: Console.dev, by email, once a release link exists.
3. Community posts, one per day, each tailored to its rules after reading its sidebar: Show HN first (Tuesday to Thursday morning US time, the kit's advice, unverified), then r/commandline and r/dotnet, then Dev.to.
4. News tips, once a release link exists: OMG! Ubuntu, Changelog News (only if the release is newsworthy as news, not a how-to), and LinuxLinks if the AI-disclosure decision is made.
5. Contributor listing: up-for-grabs.net, after the labelled issues are confirmed.
6. Press: Linux Magazine proposal; a short tip to Full Circle after its current guide is read.
7. Launch site (Product Hunt) only if the user wants a launch day, and never with paid hunting.

## 6. Open questions for the user

- Is there a downloadable release for the Windows MSI and Linux packages yet? Most venues need one.
- Which languages should the press pitch be written in? German tech press and the Hungarian community are natural fits given the six-language UI; drafts are in English only.
- Is an email from the author's own address acceptable for press and Console.dev, or should a project address be used?
- Is AI-assisted development something to state in every announcement? LinuxLinks asks directly, and Changelog judges by audience. `CONTRIBUTING.md` already says so publicly.
