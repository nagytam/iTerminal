# The Linux distributions

iTerminal for Linux comes as a **tar.gz**, a **deb** and an **rpm** per architecture (`x64` and `arm64`), all self-contained: the target machine needs no .NET runtime. There are also two portable archives, described in [portable.md](portable.md). The project internals are in [build-and-packaging.md](../architecture/build-and-packaging.md), under `iTerminal.Packaging.Linux`.

## What a release carries

| File | For | Installs to |
| --- | --- | --- |
| `iterminal_<v>_amd64.deb`, `iterminal_<v>_arm64.deb` | Debian, Ubuntu and derivatives | `/usr/share/iterminal`, link `/usr/local/bin/iTerminal` |
| `iterminal-<v>-0.x86_64.rpm`, `iterminal-<v>-0.aarch64.rpm` | Fedora, openSUSE and other RPM systems | `/usr/share/iterminal`, link `/usr/local/bin/iTerminal` |
| `iterminal-<v>-linux-x64.tar.gz`, `iterminal-<v>-linux-arm64.tar.gz` | anything else | unpacks to `iterminal-<v>/`; run `./iTerminal` from it |

`THIRD-PARTY-NOTICES.txt` and `LICENSE.txt` are beside the executable in every form. The packages are not signed.

## Installing

```bash
sudo apt install ./iterminal_<v>_amd64.deb         # Debian, Ubuntu
sudo dnf install ./iterminal-<v>-0.x86_64.rpm      # Fedora
sudo zypper --no-gpg-checks install ./iterminal-<v>-0.x86_64.rpm   # openSUSE: the rpm is not signed
tar -xzf iterminal-<v>-linux-x64.tar.gz && ./iterminal-<v>/iTerminal
```

Then run `iTerminal`. Removing: `sudo apt remove iterminal`, `sudo dnf remove iterminal`, `sudo zypper remove iterminal`. The app keeps its data in `~/.config/iTerminal`, which a removal leaves alone. `dpkg -r` warns that `/usr/local` is not empty, because the package lists that folder: the link is in `/usr/local/bin`.

## Runtime libraries the packages need

The app is self-contained, but it still uses the system's OpenSSL, ICU and Kerberos libraries:

- **deb**: the dependency list is written in `iTerminal.csproj`, as alternatives so one package fits many releases (`libssl3t64 | libssl3 | libssl1.1`, one `libicuNN` per Debian and Ubuntu release). The tool's own list stops at `libssl1.1` and `libicu70`, which no current system ships, so it is replaced. **A new Debian or Ubuntu release means a new `libicuNN` to add.**
- **rpm**: shared-library names instead of package names (`libssl.so.3()(64bit)` …), which every RPM distribution's packages provide whatever they are called, and ICU as one rich dependency listing the supported `libicuuc.so.NN` versions. Fedora's package names would not resolve on openSUSE.

## How the packages are built

One command builds all of them for the given runtimes, on Windows or on Linux (about 5 minutes per runtime):

```bash
dotnet build iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj -p:Version=1.2.3
dotnet build iTerminal.Packaging.Linux/iTerminal.Packaging.Linux.proj -p:Version=1.2.3 -p:Runtimes="linux-x64;linux-arm64"
```

The result is in `iTerminal.Packaging.Linux/bin/Release`. The formats are written by the Packaging.Targets MSBuild tasks, in managed code, so `dpkg` and `rpmbuild` are not needed. Its targets are referenced from `iTerminal.csproj` only when `LinuxPackaging` is `true`, which only the packaging project sets, so an ordinary build never loads them. `RpmFinisher.cs` then finishes each RPM: it adds the payload digest and a proper signature header (the tool's RPMs are rejected by current Fedora with "does not verify: no digest") and replaces the dependencies as described above. The RPM's uninstall script removes `/usr/share/iterminal` completely; the deb needs none.

In CI, `release.yml`'s "Linux packages (tar.gz, deb, rpm) and portable archives" job builds both runtimes (`linux-x64+linux-arm64`) with the same version as the Windows job, builds the portable archives, and uploads everything; the release job attaches it to the GitHub release `v<version>`.

## Updates

A Linux **package** (deb, rpm, or the non-portable tar.gz) does not update itself. The app checks the GitHub releases of `nagytam/iTerminal` and shows a notification naming the release and its address; the user installs the new package with the package manager. `iTerminal --update` reports the same and exits with code `1` (a release was found, but this copy cannot install it). The portable tar.gz does update itself; see [portable.md](portable.md).

## What has been tested

x86_64 only: **Ubuntu 22.04** (WSL): the tarball unpacked and run; the `.deb` installed with `dpkg -i` with its dependencies resolving by themselves, checked against its `md5sums`, run, removed. **Fedora 44** and **openSUSE Tumbleweed** (Docker): the `.rpm` installed with `dnf` and `zypper`, `rpm -K` "digests OK", `rpm -V` clean, run, removed. The app was started as an ordinary user from `PATH` on a pseudo-terminal, with its data in `~/.config/iTerminal` and nothing written to `/usr/share/iterminal`. `linux-arm64` is built in CI but has not been tested on a device.

## Checking a build

`tar -tzf` (tar.gz), `dpkg -c` (deb) and `rpm -qlp` (rpm) must list `THIRD-PARTY-NOTICES.txt` and `LICENSE.txt` and the executable `iTerminal`; `rpm -K` must say the digests are OK.

## Not set up

- There is no apt or dnf repository in this repository's tooling: the packages are downloaded from the GitHub release.
- The packages and any repository metadata are not signed.
- Windows has WinGet, see [winget.md](winget.md); no Linux package manager or store listing is prepared.
