#!/usr/bin/env bash
# Downloads NobodyWho (pinned in scripts/versions.sh) and installs its prebuilt libraries into
# addons/nobodywho/. The libraries are ~140 MB each, over GitHub's file limit, so they aren't in git.
# Only the libraries are copied: the committed nobodywho.gdextension and icons stay as they are.
#
#   scripts/fetch_nobodywho.sh            # Linux x86_64 library (default)
#   scripts/fetch_nobodywho.sh windows    # Windows x86_64 library
#   scripts/fetch_nobodywho.sh all        # every library in the release
#   NOBODYWHO_ZIP=/path/to.zip scripts/fetch_nobodywho.sh   # use an already downloaded zip
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/versions.sh"

which_libs="${1:-linux}"
dest="$PROJECT_ROOT/addons/nobodywho"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

zip="${NOBODYWHO_ZIP:-}"
if [ -z "$zip" ]; then
    zip="$work/nobodywho.zip"
    echo "Downloading NobodyWho $NOBODYWHO_VERSION (about 360 MB)..."
    curl -fL --retry 3 -o "$zip" "$NOBODYWHO_URL"
fi

mkdir -p "$work/x"
case "$which_libs" in
    linux)   pattern="*/$NOBODYWHO_LINUX_SO" ;;
    windows) pattern="*/$NOBODYWHO_WINDOWS_DLL" ;;
    all)     pattern="*/libnobodywho-*" ;;
    *) echo "Unknown choice '$which_libs' (linux, windows or all)"; exit 2 ;;
esac
unzip -q -o "$zip" "$pattern" -d "$work/x"

mkdir -p "$dest"
count=0
while IFS= read -r -d '' lib; do
    cp -f "$lib" "$dest/"
    count=$((count + 1))
done < <(find "$work/x" -type f -name 'libnobodywho-*' -print0)
if [ "$count" -eq 0 ]; then
    fail "No NobodyWho library matched '$pattern' in the zip."
    exit 1
fi
ok "Installed $count NobodyWho librar$( [ "$count" -eq 1 ] && echo y || echo ies) into addons/nobodywho/"

# Checksums of the libraries this project was tested with (a mismatch warns, it doesn't stop you).
check() {
    local file="$dest/$1" want="$2"
    [ -f "$file" ] || return 0
    if [ "$(sha256_of "$file")" = "$want" ]; then
        ok "$1 checksum matches $NOBODYWHO_VERSION"
    else
        warn "$1 checksum differs from the tested $NOBODYWHO_VERSION build"
    fi
}
check "$NOBODYWHO_LINUX_SO" "$NOBODYWHO_LINUX_SHA256"
check "$NOBODYWHO_WINDOWS_DLL" "$NOBODYWHO_WINDOWS_SHA256"
