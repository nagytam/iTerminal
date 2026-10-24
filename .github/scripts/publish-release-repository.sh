#!/usr/bin/env bash
# Adds one release to the iTerminalRelease repository (a checkout of it) and rebuilds its APT index.
#
#   publish-release-repository.sh <release-repo-dir> <packages-dir> <version>
#
# The packages directory holds the files the Windows and Linux jobs built. Layout written to the
# release repository:
#
#   releases/<version>/windows-x64/iTerminal-<version>-x64.msi
#   releases/<version>/linux-x64/iterminal-<version>-linux-x64.tar.gz, ...rpm
#   releases/<version>/linux-arm64/iterminal-<version>-linux-arm64.tar.gz, ...rpm
#   apt/pool/main/i/iterminal/iterminal_<version>_<arch>.deb
#   apt/dists/stable/main/binary-<arch>/Packages(.gz)  and  apt/dists/stable/{Release,InRelease,Release.gpg}
#   apt/iterminal-archive-keyring.gpg, apt/iterminal-archive-keyring.asc
#   latest.json, index.html, .nojekyll
#
# The .deb files live only in the APT pool, not under releases/, so each package is stored once.
# Only the newest KEEP_VERSIONS versions are kept; older ones are removed from both places.
#
# Needs apt-ftparchive (apt-utils), gpg, and the signing key in the keyring. SIGNING_KEY_ID names it.
set -euo pipefail

REPO_DIR=${1:?release repository directory}
PACKAGES_DIR=${2:?packages directory}
VERSION=${3:?version}
SIGNING_KEY_ID=${SIGNING_KEY_ID:?SIGNING_KEY_ID is not set}
KEEP_VERSIONS=${KEEP_VERSIONS:-5}
SUITE=stable
COMPONENT=main
ARCHITECTURES=(amd64 arm64)

if [[ ! $VERSION =~ ^[0-9]+(\.[0-9]+)*$ ]]; then
    echo "Not a version: $VERSION" >&2
    exit 1
fi

cd "$REPO_DIR"
release_dir=releases/$VERSION
pool_dir=apt/pool/main/i/iterminal
suite_dir=apt/dists/$SUITE

# Map each file to where it goes: the MSI and the Linux archives under releases/, the .deb to the pool.
mkdir -p "$release_dir/windows-x64" "$release_dir/linux-x64" "$release_dir/linux-arm64" "$pool_dir"
shopt -s nullglob
msi_files=("$PACKAGES_DIR"/*.msi)
deb_files=("$PACKAGES_DIR"/*.deb)
linux_files=("$PACKAGES_DIR"/*.tar.gz "$PACKAGES_DIR"/*.rpm)
shopt -u nullglob

if [[ ${#msi_files[@]} -eq 0 || ${#deb_files[@]} -eq 0 || ${#linux_files[@]} -eq 0 ]]; then
    echo "Expected an MSI, a .deb and Linux archives in $PACKAGES_DIR" >&2
    exit 1
fi

cp "${msi_files[@]}" "$release_dir/windows-x64/"
cp "${deb_files[@]}" "$pool_dir/"
for file in "${linux_files[@]}"; do
    case $(basename "$file") in
        *-linux-x64.tar.gz|*.x86_64.rpm)  cp "$file" "$release_dir/linux-x64/" ;;
        *-linux-arm64.tar.gz|*.aarch64.rpm) cp "$file" "$release_dir/linux-arm64/" ;;
        *) echo "Unknown Linux package: $file" >&2; exit 1 ;;
    esac
done

# Keep the newest versions only. Version order is the numeric sort of the build numbers.
mapfile -t versions < <(find releases -mindepth 1 -maxdepth 1 -type d -printf '%f\n' | sort -V)
remove_count=$(( ${#versions[@]} - KEEP_VERSIONS ))
for (( i = 0; i < remove_count; i++ )); do
    old=${versions[i]}
    echo "Removing version $old"
    rm -rf "releases/$old"
    rm -f "$pool_dir"/iterminal_"$old"_*.deb
done

# The index. Packages paths are relative to apt/, so the commands run from there.
rm -rf "$suite_dir"
for arch in "${ARCHITECTURES[@]}"; do
    mkdir -p "$suite_dir/$COMPONENT/binary-$arch"
done
(
    cd apt
    for arch in "${ARCHITECTURES[@]}"; do
        binary_dir=dists/$SUITE/$COMPONENT/binary-$arch
        apt-ftparchive --arch "$arch" packages pool > "$binary_dir/Packages"
        gzip -9 -k -n -f "$binary_dir/Packages"
    done
    cd "dists/$SUITE"
    apt-ftparchive \
        -o APT::FTPArchive::Release::Origin=iTerminal \
        -o APT::FTPArchive::Release::Label=iTerminal \
        -o APT::FTPArchive::Release::Suite="$SUITE" \
        -o APT::FTPArchive::Release::Codename="$SUITE" \
        -o APT::FTPArchive::Release::Architectures="${ARCHITECTURES[*]}" \
        -o APT::FTPArchive::Release::Components="$COMPONENT" \
        -o APT::FTPArchive::Release::Description="iTerminal packages for Debian and Ubuntu" \
        release . > Release
    gpg --batch --yes --pinentry-mode loopback --passphrase '' --local-user "$SIGNING_KEY_ID" \
        --clearsign --output InRelease Release
    gpg --batch --yes --pinentry-mode loopback --passphrase '' --local-user "$SIGNING_KEY_ID" \
        --detach-sign --output Release.gpg Release
)

# The public key, for users' signed-by= line: binary for the keyring, ASCII for reading.
gpg --batch --yes --export "$SIGNING_KEY_ID" > apt/iterminal-archive-keyring.gpg
gpg --batch --yes --armor --export "$SIGNING_KEY_ID" > apt/iterminal-archive-keyring.asc

# latest.json and index.html are rebuilt from what is in the repository now.
mapfile -t versions < <(find releases -mindepth 1 -maxdepth 1 -type d -printf '%f\n' | sort -V)
latest=${versions[-1]}
{
    printf '{\n  "latest": "%s",\n  "versions": [' "$latest"
    sep=''
    for (( i = ${#versions[@]} - 1; i >= 0; i-- )); do
        printf '%s"%s"' "$sep" "${versions[i]}"
        sep=', '
    done
    printf ']\n}\n'
} > latest.json

{
    cat <<'HTML'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>iTerminal downloads</title>
<style>
body { font-family: system-ui, sans-serif; margin: 2rem auto; max-width: 52rem; padding: 0 1rem; line-height: 1.5; }
code, pre { background: #f2f2f2; border-radius: 4px; }
pre { padding: .75rem; overflow-x: auto; }
table { border-collapse: collapse; width: 100%; }
td, th { border-bottom: 1px solid #ddd; padding: .35rem .5rem; text-align: left; vertical-align: top; }
@media (prefers-color-scheme: dark) {
  body { background: #161616; color: #e8e8e8; }
  code, pre { background: #262626; }
  td, th { border-color: #444; }
}
</style>
</head>
<body>
<h1>iTerminal downloads</h1>
<p>Latest version: <strong>LATEST</strong></p>
<h2>Ubuntu and Debian</h2>
<pre>curl -fsSL https://nagytam.github.io/iTerminalRelease/apt/iterminal-archive-keyring.gpg | sudo tee /usr/share/keyrings/iterminal-archive-keyring.gpg &gt;/dev/null
echo "deb [signed-by=/usr/share/keyrings/iterminal-archive-keyring.gpg] https://nagytam.github.io/iTerminalRelease/apt stable main" | sudo tee /etc/apt/sources.list.d/iterminal.list
sudo apt update &amp;&amp; sudo apt install iterminal</pre>
<h2>Windows</h2>
<p>Download the MSI from the table below and run it.</p>
<h2>All versions</h2>
<table>
<tr><th>Version</th><th>Windows x64</th><th>Linux x64</th><th>Linux arm64</th><th>Debian / Ubuntu</th></tr>
HTML
    for (( i = ${#versions[@]} - 1; i >= 0; i-- )); do
        v=${versions[i]}
        win=$(find "releases/$v/windows-x64" -type f -printf '%f\n' | head -1)
        x64=$(find "releases/$v/linux-x64" -type f -printf '%f\n' | sort | paste -sd' ' -)
        arm=$(find "releases/$v/linux-arm64" -type f -printf '%f\n' | sort | paste -sd' ' -)
        printf '<tr><td>%s</td><td><a href="releases/%s/windows-x64/%s">%s</a></td>' "$v" "$v" "$win" "$win"
        printf '<td>'
        for f in $x64; do printf '<a href="releases/%s/linux-x64/%s">%s</a><br>' "$v" "$f" "$f"; done
        printf '</td><td>'
        for f in $arm; do printf '<a href="releases/%s/linux-arm64/%s">%s</a><br>' "$v" "$f" "$f"; done
        printf '</td><td>'
        for f in "$pool_dir"/iterminal_"$v"_*.deb; do
            name=$(basename "$f")
            printf '<a href="apt/pool/main/i/iterminal/%s">%s</a><br>' "$name" "$name"
        done
        printf '</td></tr>\n'
    done
    cat <<'HTML'
</table>
</body>
</html>
HTML
} > index.html
sed -i "s/<strong>LATEST<\/strong>/<strong>$latest<\/strong>/" index.html

touch .nojekyll
echo "Published $VERSION to $REPO_DIR (latest: $latest)"
