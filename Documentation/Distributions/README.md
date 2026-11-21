# Distributions

How iTerminal is packaged and distributed. The project internals (what builds each package) are in [../architecture/build-and-packaging.md](../architecture/build-and-packaging.md); how the app updates itself is in [../architecture/runtime-dependencies.md](../architecture/runtime-dependencies.md) (*Self-update*).

| Document | Covers |
| --- | --- |
| [windows-msi.md](windows-msi.md) | The Windows installer: what it installs, silent install switches, upgrades, the codes, building, signing |
| [portable.md](portable.md) | The portable zip and tar.gz archives, the `iTerminal.portable` marker, self-update of portable copies |
| [linux.md](linux.md) | The tar.gz, deb and rpm packages: install, dependencies, building, what was tested |
| [winget.md](winget.md) | Releasing a version on WinGet: the immutable `iTerminal-WinGet` repository, manifests, test install, the `microsoft/winget-pkgs` pull request |

Every GitHub release `v0.0.0.<run number>` of `nagytam/iTerminal` carries: the MSI, the Windows portable zip, the deb, rpm and tar.gz of both Linux architectures, and the two Linux portable archives. Only the versions chosen for WinGet are also published to `nagytam/iTerminal-WinGet`.
