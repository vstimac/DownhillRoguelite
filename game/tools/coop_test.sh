#!/usr/bin/env bash
# Co-op test with local processes: one host and up to three clients, all headless on this machine.
# Each runs the game with a NetProbe that prints what it sees and quits; then the lines are checked:
# everyone joined on the same seed, everyone sees everyone, states flow, and the one supply they all
# reach for at the same moment goes to exactly one player.
# Usage (from game/): bash tools/coop_test.sh [clients=3] [port=7790] [log dir]
set -u
CLIENTS="${1:-3}"
PORT="${2:-7790}"
LOGS="${3:-$(mktemp -d)}"
GODOT="${GODOT:-godot}"
mkdir -p "$LOGS"
# Clients start LEAD seconds after the host, a second apart; all reach for the supply at the same
# moment (GRAB, by the computer's clock) and the host reports before the clients leave.
LEAD=8
CLIENT_SECONDS=30
GRAB=$(( $(date +%s) + LEAD + CLIENTS + 14 ))
HOST_SECONDS=$((LEAD + CLIENT_SECONDS - 4))

"$GODOT" --headless --path . -- --host="$PORT" --net-name=Host --net-probe="$HOST_SECONDS" --net-linger=20 --net-grab-at="$GRAB" > "$LOGS/host.log" 2>&1 &
PIDS="$!"
sleep "$LEAD"
for i in $(seq 1 "$CLIENTS"); do
    "$GODOT" --headless --path . -- --join="127.0.0.1:$PORT" --net-name="Client$i" --net-probe="$CLIENT_SECONDS" --net-linger=6 --net-grab-at="$GRAB" > "$LOGS/client$i.log" 2>&1 &
    PIDS="$PIDS $!"
    sleep 1
done
for pid in $PIDS; do
    wait "$pid"
done

fail=0
check() {
    if [ "$2" = "0" ]; then echo "PASS  $1"; else echo "FAIL  $1"; fail=1; fi
}
players=$((CLIENTS + 1))
others=$CLIENTS
grep -h "\[NetProbe\] role" "$LOGS"/*.log
lines=$(grep -h "\[NetProbe\] role" "$LOGS"/*.log | wc -l)
check "every process reported ($lines of $players)" "$([ "$lines" -eq "$players" ]; echo $?)"
joined=$(grep -h "\[NetProbe\] role" "$LOGS"/*.log | grep -c "joined=True")
check "everyone is in the session ($joined of $players)" "$([ "$joined" -eq "$players" ]; echo $?)"
seeds=$(grep -ho "\[NetProbe\] role.* seed=[0-9]*" "$LOGS"/*.log | grep -o "seed=[0-9]*$" | sort -u | wc -l)
check "everyone descends the same mountain (seeds: $seeds distinct)" "$([ "$seeds" -eq 1 ]; echo $?)"
seeing=$(grep -h "\[NetProbe\] role" "$LOGS"/*.log | grep -c "peers=$others remotes=$others")
check "everyone sees the other $others players ($seeing of $players)" "$([ "$seeing" -eq "$players" ]; echo $?)"
silent=$(grep -h "\[NetProbe\] role" "$LOGS"/*.log | grep -c "min_states=0 ")
check "states arrive from every remote carrier ($silent silent)" "$([ "$silent" -eq 0 ]; echo $?)"
got=$(grep -h "\[NetProbe\] role" "$LOGS"/*.log | grep -vc "got=none")
check "the supply everyone reached for went to exactly one player ($got got it)" "$([ "$got" -eq 1 ]; echo $?)"
errors=$(grep -h "ERROR\|Unhandled exception" "$LOGS"/*.log | grep -v "ObjectDB\|leaked\|resources still in use" | sort -u | head -5)
if [ -n "$errors" ]; then echo "Errors in the logs:"; echo "$errors"; fi
echo "logs: $LOGS"
if [ "$fail" = "0" ]; then echo "COOP TESTS: PASS"; else echo "COOP TESTS: FAIL"; fi
exit "$fail"
