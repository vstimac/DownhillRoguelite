#!/usr/bin/env bash
# Optional: downloads the companion's language model (Qwen2.5 3B Instruct, Q4_K_M GGUF, 2.1 GB) and
# checks its SHA-256. Without it the game runs normally; the companion just doesn't talk back.
#
#   scripts/fetch_model.sh                 # into ../../Models/ next to the repository (where the editor looks)
#   scripts/fetch_model.sh /path/to/Game   # into /path/to/Game/Models/ (next to an exported game)
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/versions.sh"

if [ $# -ge 1 ]; then
    dir="$1/Models"
else
    dir="$PROJECT_ROOT/../../Models"
fi
mkdir -p "$dir"
file="$dir/$MODEL_FILE"

if [ -f "$file" ] && [ "$(sha256_of "$file")" = "$MODEL_SHA256" ]; then
    ok "Model already present and verified: $file"
    exit 0
fi

echo "The companion's model is optional and large: $MODEL_FILE, $((MODEL_SIZE_BYTES / 1000000)) MB."
echo "Source: $MODEL_URL"
echo "Licence: Qwen2.5-3B-Instruct is released by Alibaba Cloud under the Qwen Research License."
echo "Saving to: $file"
curl -fL --retry 3 -C - -o "$file.part" "$MODEL_URL"

got="$(sha256_of "$file.part")"
if [ "$got" != "$MODEL_SHA256" ]; then
    fail "Checksum mismatch (got $got). The partial download is kept as $file.part; delete it and try again."
    exit 1
fi
mv -f "$file.part" "$file"
ok "Model downloaded and verified: $file"
