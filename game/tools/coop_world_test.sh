#!/usr/bin/env bash
# Host + participant + late joiner: actor identities/death, NPC arbitration and pickup history,
# falls and get-ups seen both ways, a med kit, the host's pack set down and shouldered (seen by the
# others, the world running meanwhile), a host's wolf hunting the guest (stalk, crouch and bite seen by
# the guest), and the same set pieces, rocks, grass and signs everywhere.
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
# The world each machine built from the shared seed (the sign text is shown, not checked: it names times).
worlds=$(grep -h '\[WorldProbe\] world_digest' "$LOGS"/*.log | sed 's/ | .*//' | sort -u | wc -l)
world_lines=$(grep -h '\[WorldProbe\] world_digest' "$LOGS"/*.log | wc -l)
empty=$(grep -h '\[WorldProbe\] world_digest' "$LOGS"/*.log | grep -c 'pieces=0:\|ground=0/0/0:\|signs=0:')
[ "$reports" -eq 3 ] || fail=1
[ "$digests" -eq 1 ] || fail=1
[ "$npc_lines" -eq 1 ] || fail=1
[ "$world_lines" -eq 3 ] && [ "$worlds" -eq 1 ] && [ "$empty" -eq 0 ] || fail=1
if grep -qE 'ERROR|Exception|\[WorldProbe\] FAIL' "$LOGS"/*.log; then fail=1; fi
echo "Reports: $reports; distinct actor digests: $digests; distinct worlds near station 1: $worlds of $world_lines ($empty empty); logs: $LOGS"
if [ "$fail" -eq 0 ]; then echo 'COOP WORLD TESTS: PASS'; else echo 'COOP WORLD TESTS: FAIL'; fi
exit "$fail"
