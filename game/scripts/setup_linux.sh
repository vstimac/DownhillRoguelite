#!/usr/bin/env bash
# Sets up a fresh Linux clone for development:
#   1. checks Godot .NET and the .NET SDK
#   2. installs the NobodyWho library (unless it is already there)
#   3. optionally downloads the companion's model (--with-model, 2.1 GB)
#   4. builds the C# and lets Godot import the project once
#   5. prints the asset check
#
#   scripts/setup_linux.sh [--with-model]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/versions.sh"
cd "$PROJECT_ROOT"

with_model=0
for arg in "$@"; do
    case "$arg" in
        --with-model) with_model=1 ;;
        *) echo "Unknown option $arg (only --with-model)"; exit 2 ;;
    esac
done

godot_bin="${GODOT:-godot}"
command -v "$godot_bin" >/dev/null 2>&1 || { fail "Godot $GODOT_VERSION .NET not found as '$godot_bin' (set GODOT=/path/to/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64)"; exit 1; }
command -v dotnet >/dev/null 2>&1 || { fail ".NET SDK not found (install dotnet-sdk $DOTNET_MIN_MAJOR or newer, e.g. 'sudo pacman -S dotnet-sdk' or 'sudo apt install dotnet-sdk-8.0')"; exit 1; }
command -v unzip >/dev/null 2>&1 || { fail "unzip not found (install unzip)"; exit 1; }
command -v curl >/dev/null 2>&1 || { fail "curl not found (install curl)"; exit 1; }

if [ -f "addons/nobodywho/$NOBODYWHO_LINUX_SO" ]; then
    ok "NobodyWho Linux library already installed"
else
    bash scripts/fetch_nobodywho.sh linux
fi

if [ "$with_model" -eq 1 ]; then
    bash scripts/fetch_model.sh
else
    echo "Skipping the companion model (optional, 2.1 GB): run scripts/fetch_model.sh or pass --with-model."
fi

echo "Building C#..."
dotnet build
echo "Importing the project (first run takes a while)..."
"$godot_bin" --headless --path . --import >/dev/null 2>&1 || "$godot_bin" --headless --path . --quit-after 5 >/dev/null 2>&1 || true

echo
bash scripts/check_assets.sh
