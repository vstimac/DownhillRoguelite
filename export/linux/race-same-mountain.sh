#!/bin/sh
# Everyone plays the same mountain (seed 1). Your first life counts: deepest wins.
cd "$(dirname "$0")"
exec ./DownhillRoguelite.x86_64 -- --seed=1 "$@"
