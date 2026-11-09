#!/usr/bin/env bash
# Exports the Linux build with the "Linux" preset from export_presets.cfg (the Windows preset is
# untouched) and packs it as a .tar.gz with the player guide.
#
#   scripts/build_linux.sh [output-dir]        # default: build/linux
#   GODOT=/path/to/godot scripts/build_linux.sh
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/versions.sh"
cd "$PROJECT_ROOT"

godot_bin="${GODOT:-godot}"
out="$(mkdir -p "${1:-build/linux}" && cd "${1:-build/linux}" && pwd)"
name="DownhillRoguelite"
# Keep Godot from importing the build output if it lies inside the project.
touch "$out/.gdignore"

bash scripts/check_assets.sh || { fail "Fix the [FAIL] lines above first."; exit 1; }

echo "Building C#..."
dotnet build
echo "Exporting to $out ..."
rm -rf "$out/stage"
mkdir -p "$out/stage"
"$godot_bin" --headless --path . --export-release "Linux" "$out/stage/$name.x86_64"
[ -f "$out/stage/$name.x86_64" ] || { fail "Export produced no binary (are the Linux export templates installed?)"; exit 1; }
chmod +x "$out/stage/$name.x86_64"
cp docs/LINUX.md "$out/stage/README-LINUX.md"
cp -r export/playtest/Wallpaper "$out/stage/Wallpaper"   # thank-you wallpapers for beta testers
mkdir -p "$out/stage/Models"
cat > "$out/stage/Models/PUT-THE-MODEL-HERE.txt" <<EOF
Optional: the companion's language model ($MODEL_FILE, 2.1 GB) goes in this folder.
Download: $MODEL_URL
SHA-256: $MODEL_SHA256
Without it the game runs normally; the companion just doesn't talk back.
EOF

tarball="$out/$name-linux-x86_64.tar.gz"
# The game binary is marked executable inside the archive itself, so it runs after unpacking even
# when the archive is made on Windows (where the file system has no execute bit).
rm -f "$tarball" "${tarball%.gz}"
tar -cf "${tarball%.gz}" --owner=0 --group=0 --exclude="./$name.x86_64" -C "$out/stage" .
tar -rf "${tarball%.gz}" --owner=0 --group=0 --mode=755 -C "$out/stage" "./$name.x86_64"
gzip -f "${tarball%.gz}"
ok "Linux build: $out/stage/$name.x86_64"
ok "Archive:     $tarball"
