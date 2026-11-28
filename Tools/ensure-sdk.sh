#!/usr/bin/env bash
# Checks that the .NET SDK that global.json asks for is available, and offers to install it.
#
# Usage: Tools/ensure-sdk.sh            (run from anywhere; it works on the repository root)
#
# Exit codes: 0 = a compatible SDK is available (now or after the install),
#             1 = no compatible SDK, and the user declined or the session cannot prompt.
# The prompt needs an interactive terminal. In CI (or when stdin is not a terminal) the
# script never prompts: it prints the install command and exits 1.

set -u

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
global_json="$root/global.json"

requested="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$global_json" 2>/dev/null | head -n 1)"
requested="${requested:-any}"

sdk_ok() {
    # `dotnet --version` resolves the SDK through global.json (rollForward included), so its exit
    # code is the authority: non-zero means no compatible SDK is installed.
    (cd "$root" && dotnet --version >/dev/null 2>&1)
}

if sdk_ok; then
    echo "The .NET SDK $(cd "$root" && dotnet --version) is available (global.json asks for $requested)."
    exit 0
fi

echo "No .NET SDK compatible with global.json (requested $requested) is installed."
if command -v dotnet >/dev/null 2>&1; then
    echo "Installed SDKs:"
    dotnet --list-sdks | sed 's/^/  /'
fi

if [ ! -t 0 ] || [ -n "${CI:-}" ]; then
    echo "Install it with: curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --jsonfile \"$global_json\""
    exit 1
fi

read -r -p "Download and install the .NET SDK $requested now (into ~/.dotnet)? [y/N] " answer
case "$answer" in
    y|Y|yes|YES) ;;
    *)
        echo "Not installed. Install the SDK, or change global.json to an installed version."
        exit 1
        ;;
esac

installer_dir="$(mktemp -d)"
trap 'rm -rf "$installer_dir"' EXIT
installer="$installer_dir/dotnet-install.sh"

if ! curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"; then
    echo "Could not download the installer from https://dot.net/v1/dotnet-install.sh"
    exit 1
fi

if ! bash "$installer" --jsonfile "$global_json"; then
    echo "The installer failed."
    exit 1
fi

echo "Add ~/.dotnet to PATH if it is not there yet (export PATH=\"\$HOME/.dotnet:\$PATH\"), then rerun the build."
if [ -x "$HOME/.dotnet/dotnet" ] && (cd "$root" && "$HOME/.dotnet/dotnet" --version >/dev/null 2>&1); then
    echo "The .NET SDK $("$HOME/.dotnet/dotnet" --version) is ready."
    exit 0
fi
exit 1
