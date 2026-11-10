#!/usr/bin/env bash
# Co-op test with local processes: one host and up to three clients, all headless on this machine.
# Each runs the game with a NetProbe that prints what it sees and quits; then the lines are checked:
# everyone joined on the same seed, everyone sees everyone, states flow, no supply is picked up
# twice, and a fallen player watches a teammate, is seen fallen, and comes back.
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
# Client 1 falls just after the grab and comes back RESPAWN seconds later, before anyone reports.
DIE=$((GRAB + 2))
RESPAWN=4

"$GODOT" --headless --path . -- --host="$PORT" --net-name=Host --net-probe="$HOST_SECONDS" --net-linger=20 --net-grab-at="$GRAB" --net-respawn="$RESPAWN" > "$LOGS/host.log" 2>&1 &
PIDS="$!"
sleep "$LEAD"
for i in $(seq 1 "$CLIENTS"); do
    DIES=""
    if [ "$i" = "1" ]; then DIES="--net-die-at=$DIE"; fi
    "$GODOT" --headless --path . -- --join="127.0.0.1:$PORT" --net-name="Client$i" --net-probe="$CLIENT_SECONDS" --net-linger=6 --net-grab-at="$GRAB" --net-respawn="$RESPAWN" $DIES > "$LOGS/client$i.log" 2>&1 &
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
# Each machine reaches for the first supply it has loaded (by key); where several reach for the same
# one, only one may get it: no key is ever got twice, and someone was refused.
dupes=$(grep -ho "got=[^ ]*" "$LOGS"/*.log | grep -v "got=none" | sort | uniq -d | wc -l)
check "no supply was picked up by two players ($dupes duplicates)" "$([ "$dupes" -eq 0 ]; echo $?)"
refused=$(grep -ho "refused=[1-9]" "$LOGS"/*.log | wc -l)
check "players reaching for a taken supply were refused ($refused refused)" "$([ "$refused" -ge 1 ]; echo $?)"
fell=$(grep -h "name=Client1 " "$LOGS"/client1.log | grep -c "fell=True watched=[A-Za-z]")
check "a fallen player watches a living teammate" "$([ "$fell" -eq 1 ]; echo $?)"
back=$(grep -h "name=Client1 " "$LOGS"/client1.log | grep -c "back=True")
check "and comes back after the respawn time" "$([ "$back" -eq 1 ]; echo $?)"
saw=$(grep -h "\[NetProbe\] role" "$LOGS"/host.log | grep -c "saw_fallen=True")
check "the host saw them fallen" "$([ "$saw" -eq 1 ]; echo $?)"
errors=$(grep -h "ERROR\|Unhandled exception" "$LOGS"/*.log | grep -v "ObjectDB\|leaked\|resources still in use" | sort -u | head -5)
if [ -n "$errors" ]; then echo "Errors in the logs:"; echo "$errors"; fi
echo "logs: $LOGS"
if [ "$fail" = "0" ]; then echo "COOP TESTS: PASS"; else echo "COOP TESTS: FAIL"; fi
exit "$fail"
