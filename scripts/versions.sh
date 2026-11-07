# Pinned versions and checksums shared by the setup, build and check scripts (sourced, not run).
# Change them here only, together with the matching notes in docs/BUILDING.md.

GODOT_VERSION="4.7.2"
GODOT_TAG="${GODOT_VERSION}-stable"
GODOT_TEMPLATE_DIR_NAME="${GODOT_VERSION}.stable.mono"
DOTNET_MIN_MAJOR=8

# NobodyWho (local language model GDExtension), installed into addons/nobodywho/.
NOBODYWHO_VERSION="13.0.0"
NOBODYWHO_URL="https://github.com/nobodywho-ooo/nobodywho/releases/download/nobodywho-godot-v${NOBODYWHO_VERSION}/nobodywho-godot-nobodywho-godot-v${NOBODYWHO_VERSION}.zip"
NOBODYWHO_LINUX_SO="libnobodywho-godot-x86_64-unknown-linux-gnu-release.so"
NOBODYWHO_LINUX_SHA256="89634c701322c261a706527f81e4d59b9c345040a5ef979ac2e10ada3e073fb3"
NOBODYWHO_WINDOWS_DLL="libnobodywho-godot-x86_64-pc-windows-msvc-release.dll"
NOBODYWHO_WINDOWS_SHA256="19e569784651288ab2aeacde30d7b976aaedc612de00e1b32027180f1bf2ba6c"

# The companion's language model (optional: without it the companion stays silent).
MODEL_FILE="qwen2.5-3b-instruct-q4_k_m.gguf"
MODEL_URL="https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/${MODEL_FILE}"
MODEL_SHA256="626b4a6678b86442240e33df819e00132d3ba7dddfe1cdc4fbb18e0a9615c62d"
MODEL_SIZE_BYTES=2104932768

# Output helpers: [OK] / [WARN] / [FAIL] lines.
ok()   { printf '[OK]   %s\n' "$*"; }
warn() { printf '[WARN] %s\n' "$*"; }
fail() { printf '[FAIL] %s\n' "$*"; }

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d' ' -f1 | tr -d '*\\'
    else
        shasum -a 256 "$1" | cut -d' ' -f1
    fi
}

# The project root (the folder holding project.godot), whichever folder a script is run from.
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
