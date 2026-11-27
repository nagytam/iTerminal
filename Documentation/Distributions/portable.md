# The portable distributions

A portable copy is iTerminal without setup: unpack it anywhere and start the executable. Nothing is written to the registry, no shortcut or `PATH` entry is made, and removing it is deleting the folder. There is one archive per platform:

| File | Platform | Start with |
| --- | --- | --- |
| `iTerminal-<v>-x64-portable.zip` | Windows x64 | `iTerminal.exe` |
| `iTerminal-<v>-linux-x64-portable.tar.gz` | Linux x64 | `./iTerminal` |
| `iTerminal-<v>-linux-arm64-portable.tar.gz` | Linux arm64 | `./iTerminal` |
| `iTerminal-<v>-osx-arm64-portable.tar.gz` | macOS on Apple silicon | `./iTerminal` |
| `iTerminal-<v>-osx-x64-portable.tar.gz` | macOS on Intel | `./iTerminal` |

All of them are self-contained (no .NET runtime needed) and are attached to every GitHub release `v<version>` next to the MSI and the Linux packages. The installers are described in [windows-msi.md](windows-msi.md) and [linux.md](linux.md).

## The marker

A portable copy is the same self-contained `dotnet publish` as the installer for that platform, plus an empty file `iTerminal.portable` beside the executable. The marker is what makes a copy portable:

- the app reads it to choose the archive for its updates instead of the MSI or a package, and
- About shows the release type "Portable" instead of "Package".

The marker is added by `release.yml` after the publish and never by the project, so an ordinary build is never portable. A copy that loses the file becomes an ordinary unpackaged build, and one that is given the file is treated as portable.

`THIRD-PARTY-NOTICES.txt` and `LICENSE.txt` are in the folder too.

## Using it

**Windows**: unzip (the files are in the root of the archive, no wrapping folder) to a folder you can write to, and run `iTerminal.exe`. For `PATH` access add the folder to your own `PATH`. The `.zip` carries no shortcut; make one if you want it.

**Linux**: `tar -xzf iTerminal-<v>-linux-x64-portable.tar.gz` unpacks to a folder named after the archive; run `./iTerminal` from it. Unlike the deb and rpm it installs nothing, needs no root, and the folder can be anywhere.

**macOS**: `tar -xzf iTerminal-<v>-osx-arm64-portable.tar.gz` (or `osx-x64` on Intel Macs) unpacks to a folder named after the archive; run `./iTerminal` from it in Terminal. The archives are not signed or notarized, and a file downloaded through a browser carries the quarantine flag, so macOS may refuse to start `iTerminal` until the flag is removed with `xattr -dr com.apple.quarantine <folder>`. Only the portable archive is published for macOS; there is no `.app` bundle, `.dmg` or `.pkg` yet.

The app keeps its settings, database and vault in its data directory (Windows `%APPDATA%\iTerminal`, Linux `~/.config/iTerminal`) and not in the portable folder, so a portable copy shares them with any other copy of the same user. The `DataDirectory` configuration key moves that folder if a copy must be fully self-contained.

## Updates

A portable copy updates itself from the portable archive of the newest release, on both systems (the update dialog, or `iTerminal --update`):

1. The archive of the right kind is downloaded (`…-x64-portable.zip`, or the tar.gz of the machine's architecture) and checked against the `sha256` digest GitHub records. An asset without a digest is refused.
2. Before the app closes, it checks that the install folder and every folder of the new release inside it can take a file; if not, nothing is started and the log says so.
3. The archive is unpacked next to the download. An entry that would land outside the target folder stops the unpacking; a Windows zip without the marker and `iTerminal.exe` is refused, and the `iTerminal.exe` in it gets the same Authenticode check as the MSI. A Linux archive refuses links and keeps the executable bit.
4. A small script does the replacement after the app has exited: `apply-update.cmd` (Windows) or `apply-update.sh` (Linux) waits for the process, backs up the install folder, copies the new files over it (copies only, so the user's own files in the folder stay), and restores the backup if the copy fails. Only a finished update deletes the staged files and the backup. Progress goes to `apply-update.log` in the update folder.
5. Windows starts the new `iTerminal.exe` from the install folder; on Linux nothing restarts (a terminal program has no window for a script to reopen it in), so the dialog says to start it again.

The download folder is the per-user temp folder on Windows and `~/.local/share/iTerminal/update` on Linux (the shared `/tmp` could be pre-created by another user). Linux archives are checked by their SHA-256 digest only, because Linux releases are not Authenticode-signed.

## How the archives are built

All in `release.yml`, no project:

- **Windows**: the Windows job runs `dotnet publish iTerminal/iTerminal.csproj -c Release -r win-x64 --self-contained -p:Version=$VERSION -o portable`, creates `portable/iTerminal.portable`, and zips the folder with `ZipFile.CreateFromDirectory` (forward slashes, no wrapping folder) into `iTerminal.Installer/bin/Release` so the job uploads it with the MSI.
- **Linux**: for each of `linux-x64` and `linux-arm64` the Linux job publishes into a folder named `iTerminal-<v>-<rid>-portable`, adds the marker, and runs `tar -czf`. The archive therefore has one top folder; the updater finds it by its marker.

## Checking an archive

List it (`unzip -l`, `tar -tzf`): `iTerminal` or `iTerminal.exe`, `iTerminal.portable`, `THIRD-PARTY-NOTICES.txt` and `LICENSE.txt` must be there. Run it from a folder that is not the checkout, and open About: the release row must say "Portable".

## What portable copies are not

- They are not in WinGet or any package manager, and nothing checks them for a package manager's updates; they update themselves as above.
- The Windows zip is not code-signed, like the MSI.
