# The Windows installer (MSI)

`iTerminal-<version>-x64.msi` is the installer for Windows: a WiX Toolset 5 package built by `iTerminal.Installer`. It is the file the GitHub release carries as the Windows package, the file `iTerminal --update` installs on an MSI copy, and the file WinGet installs. For the other forms see [portable.md](portable.md), [linux.md](linux.md) and [winget.md](winget.md); the project internals are in [build-and-packaging.md](../architecture/build-and-packaging.md).

## What it installs

- The app, self-contained (`dotnet publish -r win-x64 --self-contained`), in `C:\Program Files\iTerminal`. No .NET runtime is needed on the machine.
- A Start menu shortcut, `iTerminal`, which starts the app in the user's Documents folder (the app writes `iTerminal.log` into its working directory, and `Program Files` is not writable for the user).
- The install folder on the machine `PATH`, so `iTerminal` starts from any terminal. This is a feature (`PathEntry`), on by default.
- An entry in Apps & features: `iTerminal BETA`, publisher `Tamas Nagy`, with the product icon.
- `THIRD-PARTY-NOTICES.txt` and `LICENSE.txt` beside the executable.

The installer is per machine, so Windows asks for administrator rights itself (a UAC prompt). The wizard has a welcome page, an install-folder page and progress; the licence page is skipped. The last page has a "Start iTerminal BETA now" checkbox, ticked by default.

## Install, upgrade, uninstall

| Task | Command |
| --- | --- |
| Interactive install | double-click the MSI |
| Silent install | `msiexec /i iTerminal-<v>-x64.msi /qn` |
| Silent install, and start the app afterwards | `msiexec /i iTerminal-<v>-x64.msi /qn LAUNCHAFTERINSTALL=1` |
| Without the `PATH` entry | `msiexec /i iTerminal-<v>-x64.msi ADDLOCAL=Application` |
| Uninstall | Apps & features, or `msiexec /x iTerminal-<v>-x64.msi`, or `msiexec /x {ProductCode}` |
| Look inside without installing | `msiexec /a iTerminal-<v>-x64.msi /qn TARGETDIR=<short path>` |

- `LAUNCHAFTERINSTALL` is off unless passed: silent installs usually come from a deployment tool running as SYSTEM, where an interactive terminal app would only leave a stray process. A failed launch never fails the install.
- Use a **short** `TARGETDIR` for the extraction: Windows Installer refuses paths over 260 characters and some bundled files are deep.
- Uninstall removes the files, the shortcut and the `PATH` entry.

## Upgrades and the codes

- **`UpgradeCode` is `{0E27BCAD-3940-4A25-B391-E316A54EACF9}` and must never change.** It is what lets a newer MSI find and replace an older install (`MajorUpgrade`). An older MSI refuses to run over a newer install.
- **`ProductCode` is new in every build.** WiX writes it; it identifies one MSI. Anything that names one specific build (a WinGet manifest, `msiexec /x {…}`) needs the code of that MSI. Read it from the file:

  ```powershell
  $i = New-Object -ComObject WindowsInstaller.Installer
  $db = $i.OpenDatabase("C:\path\iTerminal-<v>-x64.msi", 0)
  $v = $db.OpenView("SELECT Value FROM Property WHERE Property='ProductCode'"); $v.Execute(); $v.Fetch().StringData(1)
  ```

- **Release builds are all "the same version" to Windows Installer.** The version is `0.0.0.<run number>` and Windows Installer compares only the first three parts, so `AllowSameVersionUpgrades` is on: every build replaces the last instead of being refused.
- The version comes from `-p:Version=` (default `1.0.0`) and is also stamped on the published `iTerminal.exe`.

## Building it

```powershell
dotnet build iTerminal.Installer/iTerminal.Installer.wixproj -c Release -p:Version=1.2.3
```

Windows only: elsewhere the project is skipped, not failed, so `dotnet build iTerminal.slnx` still succeeds on Linux. The build publishes the app first (about 5 minutes, ~240 MB compressed) and `Package.wxs` packs whatever was published, minus `*.pdb` and `*.log`, so a new file in the app's output needs nothing here. The output is `iTerminal.Installer/bin/Release/iTerminal-<version>-x64.msi`. In CI, `release.yml` builds it in the "Windows installer (MSI) and portable archive" job and uploads it with the portable zip; the release job attaches it to the GitHub release `v<version>`.

The ICE60 warnings in the build are about four font-like files without a language and are harmless.

## Updates

- **An MSI copy** updates from the GitHub release: the Install button in the update dialog, or `iTerminal --update`, downloads the release's `.msi`, checks it against the `sha256` digest GitHub records, checks its Authenticode signature, runs `msiexec /i … /passive /norestart`, and closes the app so the installer can replace the files.
- **A WinGet-managed copy** is also an MSI, but WinGet's manifest passes `MANAGEDBYWINGET=1`, which makes the installer write `HKLM\Software\iTerminal\ManagedBy = WinGet`. The app then installs nothing itself and tells the user to run `winget upgrade TamasNagy.iTerminal`. A direct install, or an update the app runs, does not pass the property, so the value is removed again and the copy is no longer WinGet's. See [winget.md](winget.md).
- Nothing forces an update; background checks only announce.

## Signing

The MSI is **not code-signed**. SmartScreen warns on a download of an unsigned file, and the updater logs an unsigned MSI as a warning and still runs it (release signing is not configured yet; a signature that is present but invalid, or an untrusted or revoked chain, stops the install). Signing needs a certificate and `signtool` after the build; see [build-and-packaging.md](../architecture/build-and-packaging.md).

## Checking a build

- `msiexec /a` into a short folder: `iTerminal.exe`, `THIRD-PARTY-NOTICES.txt` and `LICENSE.txt` must be there.
- Read the `Property`, `CustomAction`, `ControlEvent` and `InstallExecuteSequence` tables (Orca, or the `WindowsInstaller.Installer` COM object) to check the launch wiring without installing.
- Install it, check Apps & features (`iTerminal BETA`, `Tamas Nagy`, the version) and the registry key `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{ProductCode}`, start `iTerminal` from a new terminal, uninstall.
