#!/usr/bin/env bash
# Host + participant + late joiner: actor identities/death, NPC arbitration and pickup history.
set -u
PORT="${1:-7940}"
LOGS="${2:-$(mktemp -d)}"
GODOT="${GODOT:-godot}"
mkdir -p "$LOGS"
START=$(( $(date +%s) + 14 ))
"$GODOT" --headless --path . -- --host="$PORT" --net-name=WorldHost --net-world-probe="$START" > "$LOGS/host.log" 2>&1 &
HPID=$!
sleep 7
"$GODOT" --headless --path . -- --join="127.0.0.1:$PORT" --net-name=WorldClient --net-world-probe="$START" > "$LOGS/client.log" 2>&1 &
CPID=$!
sleep 28
"$GODOT" --headless --path . -- --join="127.0.0.1:$PORT" --net-name=WorldLate --net-world-probe="$START" > "$LOGS/late.log" 2>&1 &
LPID=$!
fail=0
for pid in "$HPID" "$CPID" "$LPID"; do wait "$pid" || fail=1; done
grep -h '\[WorldProbe\]' "$LOGS"/*.log
reports=$(grep -h '\[WorldProbe\] result' "$LOGS"/*.log | wc -l)
digests=$(grep -h '\[WorldProbe\] digest=' "$LOGS"/*.log | sort -u | wc -l)
npc_lines=$(grep -h '\[WorldProbe\] npc_line=' "$LOGS"/*.log | sort -u | wc -l)
[ "$reports" -eq 3 ] || fail=1
[ "$digests" -eq 1 ] || fail=1
[ "$npc_lines" -eq 1 ] || fail=1
if grep -qE 'ERROR|Exception|\[WorldProbe\] FAIL' "$LOGS"/*.log; then fail=1; fi
echo "Reports: $reports; distinct world digests: $digests; logs: $LOGS"
if [ "$fail" -eq 0 ]; then echo 'COOP WORLD TESTS: PASS'; else echo 'COOP WORLD TESTS: FAIL'; fi
exit "$fail"
