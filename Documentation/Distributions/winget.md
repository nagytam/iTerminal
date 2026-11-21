# Releasing iTerminal on WinGet

How a version of iTerminal gets onto the Windows Package Manager (`winget install TamasNagy.iTerminal`), what each step is for, and the problems already met. The MSI itself is built by `iTerminal.Installer` and published by `release.yml` (see [build-and-packaging.md](../architecture/build-and-packaging.md)); this page starts from a published release.

## The pieces

| Piece | Where | Role |
| --- | --- | --- |
| Builds and releases | `nagytam/iTerminal`, `release.yml` | Every merge into `main` builds all packages and publishes a release `v0.0.0.<run number>`. These releases are **not** immutable and can be deleted. |
| Installer releases for WinGet | `nagytam/iTerminal-WinGet` (public, immutable releases on) | Holds only the MSIs that are submitted to WinGet. GitHub makes releases immutable per repository, never per release, hence the second repository. |
| Manifests | `iTerminal.Packaging.WinGet/manifests/t/TamasNagy/iTerminal/<version>/` | Laid out like `microsoft/winget-pkgs`. A version, an installer and a default-locale file, schema 1.28.0. Not a project, not in the solution. |
| Community repository | `microsoft/winget-pkgs`, from the fork `nagytam/winget-pkgs` | A pull request per version. Microsoft's pipeline validates it, a moderator reviews it, and WinGet serves it after the merge. |
| Manual workflow | `.github/workflows/winget.yml` ("Publish to WinGet") | Generates the manifests for a release with `wingetcreate update` and can open the pull request. Starts only by hand. |

Nothing publishes to WinGet by itself. Releases appear every few minutes, and only the versions you choose go to WinGet.

## Why the installer lives in iTerminal-WinGet

A manifest's `InstallerSha256` must stay true for as long as the version exists. If the release behind `InstallerUrl` is deleted, or its file replaced, every install fails with a hash mismatch. A published immutable release can never lose or change its assets or its tag; a mistake is fixed by shipping a new version, never by editing the old one. Keeping that guarantee on the whole `nagytam/iTerminal` repository would lock every build forever, so only the WinGet installers go to `iTerminal-WinGet`.

The file there is the same MSI, byte for byte, as in `nagytam/iTerminal`: the same SHA-256 and the same ProductCode. Only the download URL in the manifest differs. `PackageUrl`, `LicenseUrl` and `ReleaseNotesUrl` keep pointing at `nagytam/iTerminal`.

## Releasing a version

Pick a release of `nagytam/iTerminal` that you want users to get; call its version `V` (for example `0.0.0.735`).

### 1. Take the facts from the MSI

```powershell
gh release download v$V --repo nagytam/iTerminal -p "iTerminal-$V-x64.msi" -D $env:TEMP\msi$V
(Get-FileHash $env:TEMP\msi$V\iTerminal-$V-x64.msi -Algorithm SHA256).Hash
```

The hash must equal the `digest` of the asset on the release (`gh release view v$V --json assets`). The ProductCode comes from the MSI, because WiX writes a new one in every build, while the UpgradeCode stays `{0E27BCAD-3940-4A25-B391-E316A54EACF9}`:

```powershell
$i = New-Object -ComObject WindowsInstaller.Installer
$db = $i.OpenDatabase("$env:TEMP\msi$V\iTerminal-$V-x64.msi", 0)
$v = $db.OpenView("SELECT Value FROM Property WHERE Property='ProductCode'"); $v.Execute(); $v.Fetch().StringData(1)
```

Call the method directly, as above. `InvokeMember` fails with a type mismatch in PowerShell 7.

### 2. Publish the MSI to iTerminal-WinGet

This step cannot be undone, so check before it. Create the release as a draft, compare the digest of the uploaded file with the original, and publish last: an immutable release takes no more files once it is published.

```powershell
gh release create v$V "$env:TEMP\msi$V\iTerminal-$V-x64.msi" --repo nagytam/iTerminal-WinGet --draft --title "iTerminal $V" --notes "The x64 installer of iTerminal $V, identical (same SHA-256) to https://github.com/nagytam/iTerminal/releases/tag/v$V."
gh release view v$V --repo nagytam/iTerminal-WinGet --json assets --jq ".assets[].digest"
gh release edit v$V --repo nagytam/iTerminal-WinGet --draft=false
gh release view v$V --repo nagytam/iTerminal-WinGet --json isImmutable --jq .isImmutable
```

The last command must print `true`.

### 3. Write the manifests

Copy the folder of the latest version to `iTerminal.Packaging.WinGet/manifests/t/TamasNagy/iTerminal/<V>/` and change, in all three files, the version, and in the installer file:

- `InstallerUrl`: `https://github.com/nagytam/iTerminal-WinGet/releases/download/v<V>/iTerminal-<V>-x64.msi`
- `InstallerSha256` and both `ProductCode` lines (the installer entry and `AppsAndFeaturesEntries`): from step 1.
- `ReleaseNotesUrl` in the locale file: `https://github.com/nagytam/iTerminal/releases/tag/v<V>`

Rules the manifest has to keep:

- **No `ElevationRequirement`.** For an MSI WinGet would start the file with the `runas` verb, which `Msi.Package` does not have, and the install fails with "This file does not have an app associated". `msiexec` raises its own UAC prompt for a machine-scope MSI.
- **`UpgradeCode` only inside `AppsAndFeaturesEntries`.** At the installer level the schema does not know the field and `winget validate` warns.
- **Schema 1.28.0** in `ManifestVersion` and in the `$schema` header of each file: the version the `winget-pkgs` pull request template asks for.
- The `DisplayName` in `AppsAndFeaturesEntries` (`iTerminal BETA`), the publisher (`Tamas Nagy`) and the ProductCode must match what the MSI registers, or WinGet cannot tell that the package is installed.

### 4. Validate and test-install

A local manifest has to be allowed once, in an **elevated** shell: `winget settings --enable LocalManifestFiles`.

```powershell
winget validate --manifest <folder>
winget install --manifest <folder>
```

`winget validate` must print "Manifest validation succeeded." with no warnings. The install downloads the file from `InstallerUrl` and prints "Successfully verified installer hash"; check the registry entry (`HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\<ProductCode>`) for the name, publisher and version. Then remove the test install:

```powershell
winget uninstall --product-code '{<ProductCode>}'
```

Notes from the first runs:

- Run the install from an elevated shell. A non-elevated shell asks for elevation and a dismissed UAC prompt ends with `0x800704c7`.
- `winget list --id TamasNagy.iTerminal` does **not** find a package installed from a local manifest, because it belongs to no source. Uninstall by `--product-code`.
- A hung earlier `winget install` keeps the downloaded MSI in `%TEMP%\WinGet\TamasNagy.iTerminal.<V>` locked ("being used by another process"). Close it first.
- An older iTerminal already installed is replaced by the MSI (same UpgradeCode, a major upgrade); uninstalling the test version then removes iTerminal entirely.

### 5. Submit to microsoft/winget-pkgs

**First submission of the package** (done once, for 0.0.0.735, as `microsoft/winget-pkgs#485881`): the repository is several GB, so clone only what is needed.

```powershell
gh repo fork microsoft/winget-pkgs --clone=false
git clone --filter=blob:none --no-checkout --depth 1 https://github.com/nagytam/winget-pkgs.git
cd winget-pkgs
git sparse-checkout init --cone
git sparse-checkout set manifests/t/TamasNagy
git checkout master
git switch -c TamasNagy.iTerminal-<V>
```

Copy the version folder to `manifests/t/TamasNagy/iTerminal/<V>/`, commit it as `New package: TamasNagy.iTerminal version <V>`, push to the fork and open the pull request against `microsoft/winget-pkgs` (base `master`) with the same title. Fill in the template: tick only what was actually done. The Microsoft CLA is signed once by the author, by commenting `@microsoft-github-policy-service agree` on the pull request when the bot asks; the check `license/cla` turns green and the `Needs-CLA` label goes. It is never signed on someone else's behalf.

**Later versions**: the package exists, so the title is `New version: TamasNagy.iTerminal version <V>`, and `wingetcreate update TamasNagy.iTerminal --version <V> --urls https://github.com/nagytam/iTerminal-WinGet/releases/download/v<V>/iTerminal-<V>-x64.msi|x64 --submit` can do steps 3 and 5 in one go. The "Publish to WinGet" workflow wraps that command (see below).

Changing a submitted version: add a commit to the pull request branch that replaces the folder (a rename in one commit needs no force-push), and change the pull request title and description. The validation pipeline starts again on every push.

### 6. After the submission

The pipeline labels the pull request (`Azure-Pipeline-Passed`, `Validation-Completed`, `New-Package`), then a moderator reviews it; the first version of a package takes longer, days rather than hours. Answer review comments with new commits. After the merge the package is available through `winget install TamasNagy.iTerminal` once the WinGet source has refreshed.

## The manual workflow (winget.yml)

`Actions` → "Publish to WinGet" → *Run workflow*, or `gh workflow run winget.yml -f version=<V> -f submit=false`. It checks that release `v<V>` of `nagytam/iTerminal` is published and has the MSI, runs `wingetcreate update`, runs `winget validate`, and attaches the manifests to the run. With `submit` ticked it opens the pull request on `microsoft/winget-pkgs`, using the repository secret `WINGET_PAT` (a token with the `public_repo` scope, of an account that forks `winget-pkgs`).

Not done yet, so do the manual steps above until it is:

- It still reads the MSI from the release in `nagytam/iTerminal`, not from `iTerminal-WinGet`. It should copy the file into `iTerminal-WinGet` (draft, compare digests, publish) and run `wingetcreate` against that URL. That needs a second secret: a fine-grained token with `contents: write` on `nagytam/iTerminal-WinGet` only.
- It can only update a package that already exists in `winget-pkgs`.

## Open points

- The MSI is **not code-signed**, so SmartScreen warns on a download and Defender or SmartScreen flags can slow the review. Signing is listed in [build-and-packaging.md](../architecture/build-and-packaging.md); it does not block a submission.
- Versions are `0.0.0.<run number>` and the package says BETA.

## Log

| Version | Installer release | winget-pkgs pull request | State |
| --- | --- | --- | --- |
| 0.0.0.727 | `nagytam/iTerminal` | none (manifests only, test installs) | not submitted |
| 0.0.0.731 | `nagytam/iTerminal` | `microsoft/winget-pkgs#485881` (first title) | replaced by 735 |
| 0.0.0.735 | `nagytam/iTerminal-WinGet` | [`microsoft/winget-pkgs#485881`](https://github.com/microsoft/winget-pkgs/pull/485881) | submitted, under validation and review |
