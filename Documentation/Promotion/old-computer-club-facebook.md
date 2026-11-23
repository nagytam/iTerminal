# Old Computer Club (Facebook group): post

**Venue.** The Facebook group "Old Computer Club" (audience: retro-computing hobbyists who like old machines, DOS, Norton Commander and Midnight Commander style tools, and terminal UIs).

**Rules.** **Unverified.** A search on 2026-11-23 found no page for this group; Facebook groups are not readable by the fetch tool. Group rules live in the group's "Rules" tab (on mobile: "About", then "Rules"). Many retro groups ban or limit self-promotion and links. Conservative option: read the Rules tab, and if self-promotion is not allowed, message an admin first or post as a question ("who still uses a two-panel file manager?"). Check whether the group is the same as other clubs of that name (a TechPowerUp forum group of that name exists; it is not this venue).

**Before posting (user).**
- Read the group rules and decide whether to post or ask an admin.
- Fill `<RELEASE URL>` (use `https://github.com/nagytam/iTerminal/releases/latest`; the latest release when this was written was v0.0.0.775, assets: MSI, portable zip, deb, rpm, tar.gz for x64 and arm64).
- Attach `Documentation/Promotion/screenshots/two-panels-selection.png` and optionally `f3-viewer.png` (see Screenshots below).
- Post only as yourself, stay in the thread to answer, and do not ask friends to like or comment.

## Post (English, primary)

> **Remember Norton Commander? It never really left.**
>
> I am building iTerminal, an open-source two-panel file manager for the terminal, in the spirit of Norton Commander and Midnight Commander. Left panel, right panel, a row of F-key buttons at the bottom: F3 view, F4 edit, F5 copy, F6 rename, Shift+F6 move, F7 search, F8 delete, F10 quit. It is all keyboard, in a text UI, no mouse needed.
>
> It runs on Windows and Linux (written in C# on .NET). Beyond the classic file operations, git commits, Docker containers, SSH hosts and zip/tar archives open as ordinary folders. The interface speaks English, German, French, Spanish, Italian and Hungarian.
>
> It is a beta, so expect rough edges. MIT licensed, free.
>
> Repo and downloads: https://github.com/nagytam/iTerminal  (releases: <RELEASE URL>)
>
> What would make a modern Commander clone feel right to you? I would like to hear what you miss from the old days.

## Post (Hungarian, optional short version)

> **Emlékszel a Norton Commanderre? Nem tűnt el.**
>
> Az iTerminal egy nyílt forráskódú, kétpaneles fájlkezelő a terminálban, a Norton Commander és a Midnight Commander szellemében: bal és jobb panel, alul az F-billentyűk sora (F3 nézet, F4 szerkesztés, F5 másolás, F6 átnevezés, Shift+F6 áthelyezés, F7 keresés, F8 törlés, F10 kilépés). Csak billentyűzettel használható.
>
> Windowson és Linuxon fut (C#, .NET). A git-tárolók, Docker-konténerek, SSH-gépek és a zip/tar archívumok sima mappaként nyílnak meg. A felület magyarul is elérhető. Béta, MIT licenc.
>
> https://github.com/nagytam/iTerminal
>
> Mit hiányolsz a régi Commanderekből?

## Fact check (each claim against the repository)

| Claim | Source |
| --- | --- |
| Two-panel, keyboard-driven file manager for the terminal, C# on .NET, Windows and Linux, beta | `README.md` lines 1-5 |
| F3 view, F4 edit, F5 copy, F7 search, F8 delete, F10 quit | `README.md` (F3 viewer, F4 editor, F7 find); `Documentation/architecture/key-patterns.md` (F5, F6, F8 act on the focused entry; F10 quit via `QuitCommand`) |
| F6 rename, Shift+F6 move | `iTerminal.Core/Commands/MoveDialogCommand.cs` (Shift+F6), `FocusedEntry.cs` doc comment (F6 rename) |
| git, Docker, SSH, archives as folders | `README.md` "Drives" table (`git://`, `docker://`, `ssh://`, `archive://`) |
| Six languages | `iConsole/Localization/Resources/Strings*.resx` (neutral English, de, fr, es, it, hu) |
| MIT | `LICENSE` |
| Releases for Windows and Linux | `gh release list` / assets of v0.0.0.775 |

Not claimed: any DOS look, retro colour theme, or mouse behaviour (not checked); the Norton Commander and Midnight Commander comparison is the author's framing, as in the kit.

## Screenshots

No interface screenshots existed in the repository, so three were rendered on 2026-11-23 from a Release build of the app (`iTerminal/bin/Release/net10.0/linux-x64/iTerminal.dll`) run in a pseudo-terminal at 120 x 34, with the output drawn to PNG by `pyte` and Pillow. The app ran with a scratch `HOME` and a demo folder with sample files, so no personal data is shown. The rendering is a close stand-in, not a real terminal capture; glyphs such as arrows and box-drawing characters render through DejaVu Sans Mono and some show as boxes.

Files in `Documentation/Promotion/screenshots/`:

1. **`two-panels-selection.png`** (use first): two panels with a file selected, the status line and the F-key bar. This is the hook for this audience.
2. **`f3-viewer.png`**: the F3 text viewer with its footer. Its bottom line renders as a row of boxes in the DejaVu font; crop it or skip it if that looks poor.
3. **`music-player.png`**: the music player (Ctrl+Shift+M, or Tools > Music Player) with the demo library in the catalog and three tracks in the queue. The library is demo content (two invented artists, tagged MP3 tones).
4. **`web-browser.png`**: the built-in Web Browser (Tools > Web Browser) showing a local demo page served from localhost.
5. **`splash-screen.png`**: the start-up splash (cropped to its box): the welcome line with .NET 11, copyright, CPU and memory, the colour logo and the terminal protocol list. The version in the title is the development placeholder (0.0.0.0) of this build. The "user @ machine" line is redacted to a demo name (`guest @ pc`) in the render.
6. **`startup-with-dependency-prompt.png`**: the first-run "Runtime dependencies" prompt and the update banner. **Do not post it**; it is only kept as a record.

**Online radio: no screenshot.** The Online radio dialog (Ctrl+Shift+Y) could not be opened in this build: it is absent from the Tools menu and from the command palette, and the Ctrl+Shift+Y chord never reached the app. The YouTube dialog, which registers in the same "net" group, is also missing from the menu. This needs a look in the code before a radio shot can be taken.

Still missing, to capture on a real terminal if wanted: a `git://` browsing shot (one panel inside a git tag or commit). Check that no personal path or name is visible before posting.

Facebook posts show one to four images well; attach 1 to 2 images.

## Notes

- Keep the post short; the first two lines carry the hook.
- Nothing in the post asks for likes, shares or stars. The closing question asks for opinions only.
- Do not add personal details beyond the repository URL.
