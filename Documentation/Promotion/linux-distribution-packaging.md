# Linux distribution packaging: routes, approval and what iTerminal must supply

Per-distribution how-to behind venues 22–35 in [promotion-overview.md](../research/promotion-overview.md).
Nothing here has been filed, uploaded or submitted. Every step below is the user's to take.

Research date: 2026-11-21. Each rule carries a source and a status: **verified** (read on the venue's
own page in this run), **partly verified**, or **unverified** (search snippet or memory of the project's
process, not read). Several official pages (Fedora docs, ArchWiki) answered the fetch tool with a
bot-protection page, so those rules are unverified here and must be read in a browser before acting.

## 1. What iTerminal builds today

Verified against the repository.

| Artifact | Built by | Notes |
| --- | --- | --- |
| `iterminal-<v>-linux-<x64\|arm64>.tar.gz` | `iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj` (Packaging.Targets) | Self-contained, unpacks to `iterminal-<v>/`. |
| `iterminal_<v>_<amd64\|arm64>.deb` | same | Self-contained. Installs to `/usr/share/iterminal`, link in `/usr/local/bin`. Dependencies are hand-listed in `iTerminal/iTerminal.csproj` (libc6, libssl3t64 or libssl3, libicuNN alternatives, and others). |
| `iterminal-<v>-0.<x86_64\|aarch64>.rpm` | same, then `RpmFinisher.cs` | Self-contained, **not signed**; dependencies are shared-library names, so it installs on Fedora and openSUSE. |
| `iTerminal-<v>-linux-<x64\|arm64>-portable.tar.gz` | `.github/workflows/release.yml` (`linux` job) | Self-contained, with the `iTerminal.portable` marker; self-updates from the next tar.gz. |

Facts that decide every route below:

- **The packages are self-contained**: they ship the .NET runtime. No distribution archive accepts that
  as is; each one builds from source against its own .NET (see section 2).
- **Version numbers**: CI builds are `0.0.0.<run>`; releases are tagged `v<version>` on GitHub
  (`release.yml`). Distribution routes need a stable upstream tag and a release tarball. Confirm a real
  version scheme first.
- **Licence**: MIT (`LICENSE`). Suitable for every route. The RPM's licence field currently reads
  "iterminal License" (the packaging tool's default, noted in `build-and-packaging.md`) and must be set
  to `MIT` before an RPM is submitted anywhere.
- **Native binaries pulled in by NuGet**: `imobiledevice-net` (`iTerminal.Core.csproj`) and
  `Whisper.net.Runtime` (the whisper.cpp natives) arrive as prebuilt binaries. Source-only archives
  (Debian, Fedora, openSUSE Factory, Flathub) generally cannot accept prebuilt binaries; this is the main
  obstacle. Dropping or making those packages optional for a distribution build would be an app change.
- **`/usr/local/bin` link**: distribution packages use `/usr/bin`. The packaging tool decides this today.
- **No signed releases**: the RPM and the checksums are unsigned (`zypper` needs `--allow-unsigned-rpm`).
- **Self-update**: a Linux package install shows a toast only and never replaces files, so a distribution
  package does not fight its package manager (`runtime-dependencies.md`, Self-update). Only the portable
  copy updates itself.
- **The Playwright driver, Ollama, git and the Whisper model are downloaded at run time**, not shipped
  (`runtime-dependencies.md`). A distribution that forbids network access at run time should be told.
- **Tested on**: Ubuntu 22.04 (deb), Fedora 44 and openSUSE Tumbleweed (rpm), x86_64 only; arm64 is
  built but untested (`build-and-packaging.md`).
- **The packaging metadata names a maintainer** (`PackageMaintainer` in `iTerminal/iTerminal.csproj`).
  Distribution tooling asks for a maintainer address. Use whatever the user approves; do not copy it into a
  submission without the user's decision.

## 2. How distributions handle the .NET runtime

- **Self-contained (what the project ships)**: the runtime is inside the package. Fine for upstream
  packages, a user repository, AppImage, or a snap. Not accepted by the main archives of Debian, Fedora,
  openSUSE or nixpkgs, which build from source.
- **Framework-dependent against the distribution's runtime**: the app is built with
  `dotnet publish --self-contained false` and depends on the distribution's `dotnet-runtime` package.
  Needs the distribution to ship .NET 11 (the project targets `net11.0`, `CLAUDE.md`). Whether each
  distribution ships .NET 11 preview or final at the time of submission is a fact the user must check
  on the day; it was not verified here.
- **Offline NuGet restore**: build servers of the main archives have no internet access, so all NuGet
  packages must be supplied as sources, packaged individually or locked in a vendor file. iTerminal has
  roughly 40 package references (`iConsole`, `iTerminal.Core`, `iTerminal`), including native ones.
  Fedora's .NET SIG and a Debian debian-cli thread both describe this as the central difficulty
  (search results, unverified; see section 3).

## 3. Per-route detail

Order follows the venue numbers in the overview. "Do now" means: possible with the build as it is today,
without an app change.

### 22. Debian (official archive) and Ubuntu via Debian

- **Route.** File an ITP bug (Intent To Package) against `wnpp`, build a source package that follows
  Debian Policy, upload it to mentors.debian.net, request sponsorship (RFS bug against
  `sponsorship-requests`), and a Debian Developer uploads it. A brand-new source package then waits in
  the NEW queue for ftpmaster approval. Ubuntu then normally syncs from Debian (`syncpackage`).
  Sources: <https://mentors.debian.net/intro-maintainers/> (verified: ITP, source package, signed upload,
  sponsor, RFS, NEW approval), <https://www.debian.org/devel/wnpp/> (verified: ITP and RFP filing),
  <https://wiki.debian.org/Packaging/Intro> (verified: manually built `.deb` files are not accepted,
  policy compliance required), <https://ftp-master.debian.org/new.html> (verified to exist; the page does
  not state its review criteria, see the REJECT-FAQ it links).
- **Who reviews.** A sponsor (a Debian Developer) and ftpmasters. A sponsor is required for a
  non-Developer. Turnaround: not stated on any page read; NEW is a queue and `new.html` lists ages.
  Unverified.
- **Requirements.** DFSG-free licence (MIT is), built from source in a clean build with no network, no
  bundled prebuilt binaries. The project's `.deb` cannot be uploaded; it is a binary built outside
  Debian's buildd.
- **Obstacles.** Debian's .NET toolchain is not in the archive in a usable form: search results show an
  RFP for `dotnet8` (Aug 2024) and an ITP for `dotnet-sdk-6.0` (Dec 2022), with no confirmed outcome, and a
  2022 debian-cli thread calling NuGet dependencies the core difficulty (unverified, search snippets:
  <https://lists.debian.org/debian-wnpp/2024/08/msg00047.html>,
  <https://lists.debian.org/debian-cli/2022/03/msg00000.html>). Plus `imobiledevice-net` and the
  Whisper natives.
- **Verdict.** Not possible now; probably not realistic until Debian ships a .NET SDK that can build a
  `net11.0` app offline. **Do now:** nothing to file. Optionally watch the WNPP bugs for `dotnet`.
- **Ubuntu itself.** There is no separate "Ubuntu Universe" application for a third-party app; it syncs
  from Debian or an Ubuntu developer uploads it. Skip (see the skipped list). The practical Ubuntu route
  is a PPA (venue 23).

### 23. Launchpad PPA (Ubuntu and derivatives)

- **Route.** Create a Launchpad account and a PPA, register a GPG key, upload a **signed source package**
  with `dput ppa:<user>/<ppa>`. Launchpad builds it. Users run `add-apt-repository ppa:...`.
  Sources: <https://documentation.ubuntu.com/project/contributors/new-package/upload-packages-to-a-ppa/>
  and <https://documentation.ubuntu.com/launchpad/user/reference/packaging/ppas/building-a-source-package/>
  (partly verified, from search summaries: source only, pre-built `.deb` files rejected, unsigned uploads
  silently discarded).
- **Who reviews.** Nobody; a PPA is self-service. No sponsor.
- **Requirements.** A Debian source package (`debian/` directory). The Launchpad builders are offline, so
  the same NuGet problem applies, though a PPA may bundle vendored sources or fetch nothing but the
  tarball. A source package that repackages the already-built self-contained binary (a "binary
  repackaging" source package) is technically possible and common for third-party PPAs; whether
  Launchpad accepts it for a given case was not verified.
- **Verdict.** Possible, with new work: write `debian/` files and decide on source versus repackaging.
  Needs the user's Launchpad account and GPG key. **Not doable now by the agent.**

### 24. Fedora official repositories

- **Route.** Get a Fedora Account System (FAS) account, file a **Review Request** bug in Red Hat
  Bugzilla with a spec file and SRPM, add `FE-NEEDSPONSOR` when the submitter is not yet a packager, a
  packager reviews, then request a dist-git repository with `fedpkg request-repo`. Sources:
  <https://fedoraproject.org/wiki/PackageReviewProcess> (from search summary; the docs.fedoraproject.org
  pages returned a bot-protection page, so **unverified**), the sponsor tracker
  <https://bugzilla.redhat.com/show_bug.cgi?id=177841>.
- **Who reviews.** Any packager reviews; a first-time submitter needs a sponsor (a packager who
  approves the package and sponsors the person). Turnaround: no published figure. A forum report cites a
  9 day silence on a new request (unverified, a single anecdote).
- **Requirements.** Built from source on Fedora's offline builders with Fedora's own .NET; bundled
  libraries must not ship; prebuilt binaries only for an approved bootstrap. Source:
  <https://lists.fedoraproject.org/archives/list/dotnet-sig@lists.fedoraproject.org/thread/PXABNW32CCFMDOKSYYJJOCYXUFDO7H5R/>
  and <https://pagure.io/packaging-committee/issue/1193> (unverified; the guidelines page was blocked).
  The issue says the .NET application guidance is unclear and that framework-dependent is preferred by
  default.
- **Verdict.** Not possible now. Needs a spec that builds offline against Fedora's .NET packages and a
  sponsor. The project's RPM is for users and COPR, not for this route.

### 25. Fedora COPR

- **Route.** Log in with a FAS account at copr.fedorainfracloud.org, create a project, build from an SRPM
  or from the GitHub repository (`copr-cli build-scm`), and users run `dnf copr enable <user>/<project>`.
  Source: <https://docs.fedoraproject.org/en-US/quick-docs/publish-rpm-on-copr/> (partly verified from
  search summaries of the translated pages; the English page was not read).
- **Who reviews.** Nobody; a COPR project is self-service. Repositories are signed by COPR. No sponsor.
- **Requirements.** An SRPM or a spec in git. COPR builders may have network access depending on the
  project's settings (unverified), which would make a NuGet restore possible; that was not confirmed.
- **Verdict.** Possible and the cheapest RPM route, but needs a **spec file and SRPM**, which the project
  does not yet produce (it builds the RPM with Packaging.Targets, which has no spec). Needs the user's
  FAS account. Not doable now.

### 26. openSUSE Build Service (OBS), devel project, Factory

- **Route.** Create an OBS account, build in a home project (any user can; OBS builds from a spec).
  Publish the home project as a user repository. For the official distribution: submit request
  from a home project to a devel project (a project that already carries related packages), which in
  turn forwards to `openSUSE:Factory`. Sources:
  <https://en.opensuse.org/openSUSE:How_to_contribute_to_Factory> and
  <https://en.opensuse.org/openSUSE:Factory_submissions> (partly verified, from search summaries; not
  fetched). The Concept page I tried returned HTTP 403.
- **Who reviews.** Factory submissions get an automatic legal check (`legal-auto`) and a manual one for new
  packages, plus a code review (<https://osrt.opensuse.org/docs/processes.html>, search snippet,
  unverified). A devel project maintainer accepts the first request. No named sponsor.
- **Requirements.** Builds offline from sources; spec file; a tarball (`_service` can fetch it).
  .NET apps are harder: no prebuilt binaries in Factory.
- **Verdict.** A **home project** is possible with new work (a spec). Factory is not realistic now. Works
  for Tumbleweed, Leap and SLE users alike through the home repository.

### 27. Arch Linux: AUR (and the official repositories)

- **Route.** Register an AUR account, add an SSH key, create a `PKGBUILD` and a generated `.SRCINFO`,
  push to the AUR git repository (`master` only). Naming: `iterminal-bin` for a prebuilt binary package
  built from the project's GitHub release, `iterminal` for a build from source. Sources: ArchWiki
  <https://wiki.archlinux.org/title/AUR_submission_guidelines> (**unverified**: the page returned a
  bot-protection page; the points above come from search results quoting it). Rule violations can lead to
  deletion without warning (same source, unverified).
- **Who reviews.** Nobody before publishing; the community flags or requests deletion. No sponsor.
- **Requirements.** A PKGBUILD whose `source` is the GitHub release asset with a `sha256sum`; the licence
  `MIT`; the `-bin` suffix for a package that unpacks prebuilt files; the dependency list (`icu`,
  `openssl`, `krb5`, `zlib`). A `-bin` package can reuse the project's **tar.gz** directly, so this
  route needs no change to the build, only a stable release asset URL and a checksum.
- **Verdict. Do now (once a release exists).** The user (or an Arch maintainer the user trusts) can
  write the PKGBUILD from the tar.gz. The project's tar.gz folder name is `iterminal-<v>`; the `PKGBUILD`
  installs it into `/usr/share/iterminal` and links `/usr/bin/iTerminal`.
- **Official repositories.** Packages move from the AUR into `extra` when a Package Maintainer (formerly
  Trusted User) takes them on; older mailing-list threads say a move is considered for popular packages
  (votes and pkgstats usage), and a self-contained binary is unlikely to be accepted. Everything on this
  is unverified and from 2011–2024 threads
  (<https://lists.archlinux.org/archives/list/aur-general@lists.archlinux.org/thread/CMW2BGKYXCVTHY6TOQLH7XQL2YV7DCRH>).
  Not a route to pursue; the AUR is the channel.

### 28. Alpine Linux (aports, `testing`)

- **Route.** Merge request on gitlab.alpinelinux.org/alpine/aports adding an `APKBUILD` under `testing/`.
  New packages go to `testing` first, then move to `community` (search result summarising Alpine's
  workflow; the CONTRIBUTING page returned HTTP 404, so **unverified**).
- **Who reviews.** Alpine developers review the merge request. A maintainer is named in the APKBUILD.
  Turnaround unknown.
- **Requirements.** Alpine uses musl and builds from source. The project's packages are glibc builds
  (`linux-x64`, not `linux-musl-x64`), so they do not run on Alpine. .NET is packaged in `testing`
  (dotnet6 was bootstrapped from Microsoft binaries, per merge requests 28195 to 29253; whether current
  versions, including .NET 11, exist was not checked).
- **Verdict.** Not possible now. Would need a `linux-musl-x64` build (a new runtime in `KnownRuntime`),
  and a source build on Alpine's .NET. Low priority; the audience (Alpine users, mostly containers) is
  narrow.

### 29. Gentoo: GURU overlay and own overlay

- **Route.** Submit an ebuild by pull request or patch to the GURU overlay
  (<https://wiki.gentoo.org/wiki/Project:GURU>), or to the `gentoo-guru` mailing list; contributors then
  request access via Bugzilla after a few merged contributions. Alternatively publish an own overlay on
  GitHub (listed on <https://overlays.gentoo.org/>, unverified). Verified from the GURU page: commits
  go to the `dev` branch; GURU has no Gentoo-developer review before publishing and a contributor
  must be able to maintain what they add; `~arch` keywords only.
- **Who reviews.** Regular GURU contributors and Trusted Contributors; no sponsor needed for the first
  pull request.
- **Requirements.** An ebuild. Gentoo builds from source with a `dotnet-pkg` eclass that takes a NuGet
  list (`NUGETS=`); the page read did not cover this (**unverified**). A `-bin` ebuild installing the
  project's tar.gz is the cheap alternative and normal for GURU.
- **Verdict.** Possible. Needs an ebuild; the `-bin` version needs no build change, only a release
  asset. Good fit for an early, self-service route. Needs the user's GitHub or Codeberg account and
  someone who knows Gentoo packaging.

### 30. NixOS: nixpkgs

- **Route.** Pull request to <https://github.com/NixOS/nixpkgs> adding `pkgs/by-name/it/iterminal/package.nix`
  with `buildDotnetModule`, a `deps.json` NuGet lockfile generated by `nix-build -A iterminal.fetch-deps`,
  commit title `iterminal: init at <version>`. Anyone can open it; reviewers (the committers) merge it.
  Sources: <https://github.com/NixOS/nixpkgs/blob/master/pkgs/README.md> (verified: commit title format,
  `meta.maintainers` required, the submitter should be willing to maintain the package for one release
  cycle, source builds preferred, binary repackaging needs `meta.sourceProvenance`), and the `.NET`
  section of the nixpkgs manual (partly verified from
  <https://raw.githubusercontent.com/NixOS/nixpkgs/master/doc/languages-frameworks/dotnet.section.md>:
  `buildDotnetModule`, `nugetDeps`, `selfContainedBuild` default false, `fetch-deps`).
- **Who reviews.** Nixpkgs committers; no formal sponsor, but a committer must merge. The contributor
  adds themselves to the maintainers list in a separate commit.
- **Requirements.** The .NET SDK in nixpkgs must be able to build `net11.0` (not verified for .NET 11).
  The NuGet native packages (imobiledevice-net, Whisper runtimes) must be patched with `autoPatchelf`
  or left out.
- **Verdict.** The most realistic **official** route for a .NET app. Possible with new work (a Nix
  expression and a lockfile) by someone with Nix. Turnaround unknown. The user would need a GitHub
  account and agree to be a named maintainer.

### 31. Flathub (Flatpak)

- **Route.** Fork `flathub/flathub`, add a manifest, MetaInfo (AppStream) file, icon and desktop file,
  and open a pull request against the `new-pr` branch with the title "Add <app id>". Volunteer reviewers
  comment; test builds run by `bot, build`; merges happen in batches with no fixed timeline. Source:
  <https://docs.flathub.org/docs/for-app-authors/submission> (**verified**).
- **Hard rule.** From <https://docs.flathub.org/docs/for-app-authors/requirements> (**verified**):
  "Console software is not accepted", with exceptions for Flatpak tooling, and terminals, file managers
  and broad IDEs are "generally unsuitable ... and are usually rejected unless upstream officially submits
  or supports Flatpak." iTerminal is a console file manager. Also: built entirely from source with no
  network during the build; no precompiled binaries in the pull request; the app ID must come from a
  controlled domain or `io.github.<user>.<repo>`; Flathub manifests must not contain AI-generated or
  AI-assisted content, even if disclosed, and AI tools must not open or automate submission pull
  requests. `CONTRIBUTING.md` states AI-assisted development publicly, so the manifest would need to be
  written by hand.
- **Verdict. Rejected by the stated rule.** Skipped. Revisit only if Flathub's console-app rule changes.

### 32. Snap Store

- **Route.** Register an account at snapcraft.io, register the snap name, build with `snapcraft`
  (`snapcraft.yaml`, a `dotnet` plugin or by repackaging the tar.gz), upload to the store. Strict-confined
  snaps are checked automatically; classic confinement needs a manual request in the forum's
  store-requests category. Sources: search results about
  <https://snapcraft.io/docs/reviewing-classic-confinement-snaps> (the page itself returned only a
  redirect notice when fetched, so the category list is **unverified**); search summaries say requests
  are rejected unless the snap fits a supported category and that strict confinement must be tried first.
- **Who reviews.** Automated checks; for classic confinement, the store reviewers (a manual request,
  possibly rejected). No sponsor for strict.
- **Requirements.** A file manager that opens any file, starts shells, and mounts network drives cannot work in
  strict confinement without many interfaces; classic is probably needed, and whether a terminal file
  manager fits a supported classic category was not confirmed. The Ctrl+O child shell also needs the
  host's tools.
- **Verdict.** Possible to publish, but the confinement problem makes it a poor fit. Low priority; a
  strict snap would be a different, restricted product. Unverified.

### 33. AppImage

- **Route.** Build an AppImage with the project's tar.gz as the content and publish it as a release asset.
  A catalogue listing (appimage.github.io) is a pull request to that repository; its rules were not read
  (**unverified**; the AppImage documentation page fetched does not cover publishing).
  Source for the format: <https://docs.appimage.org/introduction/index.html> (verified: one app per file,
  bundles what the target may lack).
- **Who reviews.** Self-published; no gatekeeper for the file itself.
- **Requirements.** An AppImage is normally started from a desktop launcher; a console-only app without
  a `.desktop` file or icon is a poor match. The tar.gz already offers the same "unpack and run" use
  with self-update.
- **Verdict.** Not worth the work; the portable tar.gz serves the same need. Skipped.

### 34. Homebrew on Linux (Linuxbrew): `homebrew-core` or an own tap

- **Route.** `homebrew-core` takes a formula by pull request. An own **tap** (`<user>/homebrew-iterminal`
  on GitHub) needs no approval at all: users run `brew tap` and `brew install`.
- **Rules (partly verified)** from <https://docs.brew.sh/Acceptable-Formulae> (verified: needs a stable
  release with an immutable tag, builds and passes tests on the Linux CI matrix, beta software is not
  eligible) and a search summary of the package acceptance policy (unverified: a self-submitted project
  needs 90 forks, 90 watchers or 225 stars, DFSG licence, binary-only formulae belong in casks).
- **Who reviews.** Homebrew maintainers, for `homebrew-core`. Tap: nobody.
- **Verdict.** `homebrew-core` is out until the project is no longer beta and meets the notability
  threshold. An own tap with a formula that installs the Linux tar.gz is possible with a small new
  file, no build change. Low value on Linux; useful if the macOS audience matters (the project does not
  build for macOS, so skip unless that changes).

### 35. Own signed apt and dnf repository (not a distribution, but the route that works today)

- **Route.** Host the `.deb` and `.rpm` in a repository the project controls (GitHub Pages or a release
  host), sign the repository metadata with a project key, and publish a two-line setup instruction.
  This is what most third-party Linux applications do while they wait for a distribution.
- **Who reviews.** Nobody.
- **Requirements.** A signing key kept by the user; `apt` needs `Release`/`InRelease` files made by
  `apt-ftparchive` or `reprepro`; `dnf` needs `createrepo_c`. Signing the RPM (`rpm --addsign`) would
  remove the `--allow-unsigned-rpm` need on openSUSE. Neither tool is part of the current build, which uses
  Packaging.Targets and needs no `dpkg` or `rpmbuild`.
- **Verdict.** Possible with new work (a CI step and a key). The decision on keys and hosting is the
  user's. Not verified against any venue page (the tooling is the project's own choice).

## 4. Summary by effort

| Route | Reviewer or sponsor | Can start with today's artifacts | Notes |
| --- | --- | --- | --- |
| Own apt/dnf repository (35) | none | yes, `.deb` and `.rpm` | needs a signing key |
| AUR `-bin` (27) | none | yes, the tar.gz | community maintained |
| Gentoo GURU `-bin` (29) | GURU contributors | yes, the tar.gz | self-service, then Bugzilla for commit access |
| Homebrew tap (34) | none | yes, the tar.gz | little Linux value |
| COPR (25) | none | needs a spec and SRPM | cheapest RPM route |
| OBS home project (26) | none | needs a spec | reaches openSUSE and others |
| Launchpad PPA (23) | none | needs `debian/` source package | needs GPG key |
| nixpkgs (30) | committer merge | needs Nix expression, lockfile | official, source build |
| Debian archive (22) | sponsor and ftpmasters | no | blocked by .NET toolchain |
| Fedora archive (24) | sponsor and reviewer | no | blocked by offline NuGet |
| Alpine (28) | Alpine developers | no | musl build needed |
| Snap Store (32) | store reviewers for classic | partly | confinement problem |
| Flathub (31) | volunteer reviewers | no | console apps not accepted |
| AppImage (33) | none | partly | skipped, no gain |

## 5. What the user must decide

- Whether the project wants to be in the main archives at all, given the NuGet and native-binary
  obstacles, or to rely on its own repository, the AUR and a GURU `-bin` package.
- A stable version scheme and a release tag per version (the CI builds are `0.0.0.<run>`).
- Who is the named maintainer, and which contact address distribution tooling may carry
  (`PackageMaintainer` in `iTerminal/iTerminal.csproj` already holds one).
- Whether to set the RPM licence field to `MIT` and move the link from `/usr/local/bin` to `/usr/bin`
  for distribution-bound builds (app and packaging changes, not made in this run).
- A GPG key (PPA, signed repository), and accounts: Launchpad, FAS (Fedora), OBS, AUR, GitHub (nixpkgs),
  Bugzilla (Gentoo).
- Whether a volunteer packager exists for Arch, Gentoo or Nix; none is assumed here.

## 6. Placeholders

`<RELEASE URL>`, `<VERSION>`, `<TARBALL SHA256>`, `<MAINTAINER NAME>`, `<MAINTAINER EMAIL>`. Nothing
in this file is ready to paste; each route needs a package file (PKGBUILD, spec, ebuild, `debian/`,
Nix expression) that does not exist yet and that the user, or a packager the user asks, must write.
