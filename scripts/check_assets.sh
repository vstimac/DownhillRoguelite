#!/usr/bin/env bash
# Checks what a build of Downhill Roguelite needs and says what's missing, one line each:
#   [OK]   present and right
#   [WARN] missing or different, but the game still builds and runs (with a fallback)
#   [FAIL] missing and needed to build
# Exit code: 1 if anything failed, else 0.
set -uo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/versions.sh"
cd "$PROJECT_ROOT"
failed=0

# --- Tools ---
godot_bin="${GODOT:-godot}"
if command -v "$godot_bin" >/dev/null 2>&1; then
    v="$("$godot_bin" --version 2>/dev/null | head -1)"
    case "$v" in
        "$GODOT_VERSION".stable.mono*) ok "Godot $v" ;;
        *mono*) warn "Godot $v (this project is tested with $GODOT_VERSION .NET)" ;;
        *) fail "Godot $v is not the .NET (mono) build; download Godot $GODOT_VERSION .NET"; failed=1 ;;
    esac
else
    fail "Godot not found (put the Godot $GODOT_VERSION .NET binary on PATH as 'godot', or set GODOT=/path/to/it)"
    failed=1
fi

if command -v dotnet >/dev/null 2>&1; then
    sdk="$(dotnet --version 2>/dev/null)"
    major="${sdk%%.*}"
    if [ -n "$major" ] && [ "$major" -ge "$DOTNET_MIN_MAJOR" ] 2>/dev/null; then
        ok ".NET SDK $sdk"
    else
        fail ".NET SDK $sdk is too old (need $DOTNET_MIN_MAJOR or newer)"
        failed=1
    fi
else
    fail ".NET SDK not found (install dotnet-sdk $DOTNET_MIN_MAJOR or newer)"
    failed=1
fi

if [ -n "${APPDATA:-}" ]; then
    templates="$(cygpath -u "$APPDATA" 2>/dev/null || echo "$APPDATA")/Godot/export_templates/$GODOT_TEMPLATE_DIR_NAME"
else
    templates="${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/$GODOT_TEMPLATE_DIR_NAME"
fi
if [ -f "$templates/linux_release.x86_64" ]; then
    ok "Linux export templates ($templates)"
else
    warn "Linux export templates not installed in $templates (needed only to export; Editor > Manage Export Templates)"
fi

# --- NobodyWho (local language model addon) ---
so="addons/nobodywho/$NOBODYWHO_LINUX_SO"
if [ -f "$so" ]; then
    if [ "$(sha256_of "$so")" = "$NOBODYWHO_LINUX_SHA256" ]; then
        ok "NobodyWho $NOBODYWHO_VERSION Linux library"
    else
        warn "NobodyWho Linux library present but not the tested $NOBODYWHO_VERSION build"
    fi
else
    warn "NobodyWho Linux library missing: the companion can't talk (run scripts/fetch_nobodywho.sh)"
fi
[ -f addons/nobodywho/nobodywho.gdextension ] && ok "nobodywho.gdextension" || { fail "addons/nobodywho/nobodywho.gdextension missing (it is in git; restore it)"; failed=1; }

# --- Companion model (optional) ---
model="../Models/$MODEL_FILE"
if [ -f "$model" ]; then
    size="$(wc -c < "$model" | tr -d ' ')"
    if [ "$size" = "$MODEL_SIZE_BYTES" ]; then
        ok "Companion model $model"
    else
        warn "Companion model $model has an unexpected size; re-run scripts/fetch_model.sh"
    fi
else
    warn "Companion model not found at $model: optional, the companion stays silent (scripts/fetch_model.sh)"
fi

# --- Mixamo character (not redistributable, so not in git) ---
if [ -f art/characters/mixamo/carrier.fbx ]; then
    ok "Mixamo carrier character"
else
    warn "art/characters/mixamo/carrier.fbx missing: the carrier uses the mannequin (see art/characters/mixamo/README.md)"
fi

# --- Linux speech (optional) ---
if [ "$(uname -s)" = "Linux" ]; then
    found=""
    for p in /usr/lib/libspeechd.so.2 /usr/lib64/libspeechd.so.2 /usr/lib/x86_64-linux-gnu/libspeechd.so.2 /lib/x86_64-linux-gnu/libspeechd.so.2 /usr/local/lib/libspeechd.so.2; do
        [ -f "$p" ] && found="$p" && break
    done
    if [ -n "$found" ]; then
        ok "speech-dispatcher ($found): companion replies are read aloud"
    else
        warn "speech-dispatcher not installed: companion replies are subtitled only (install speech-dispatcher for a voice)"
    fi
fi

exit $failed
