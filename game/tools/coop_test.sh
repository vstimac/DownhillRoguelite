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
CLIENT_SECONDS=34
GRAB=$(( $(date +%s) + LEAD + CLIENTS + 14 ))
HOST_SECONDS=$((LEAD + CLIENT_SECONDS - 4))
# Client 1 falls just after the grab and comes back RESPAWN seconds later, before anyone reports.
DIE=$((GRAB + 2))
RESPAWN=4
# The host brings an enemy and walks off, leaving it to the clients (it goes for them: its blows reach
# their machines); client 2 strikes it (as a puppet on its machine) a few seconds later.
SPAWN=$((GRAB + 5))
AWAY=$((GRAB + 6))
STRIKE=$((GRAB + 13))
# Client 3 drops something from its pack; everyone else should see it on the ground.
DROPAT=$((GRAB + 3))
# Client 2 marks a spot (B); the host and the other clients should see the mark.
PINGAT=$((GRAB + 4))
# Client 3 tears a spider web; it should come down for everyone else.
WEBAT=$((GRAB + 5))
# Client 1 says a line in the chat (Y); everyone else should read it.
SAYAT=$((GRAB + 8))
# Downed, not dead: client 1 goes down and bleeds out (2 s, nobody helps) before it falls; client 3
# goes down later and client 2 helps it up.
DOWN3=$((GRAB + 9))
HELP2=$((GRAB + 11))

"$GODOT" --headless --path . -- --host="$PORT" --net-name=Host --net-probe="$HOST_SECONDS" --net-linger=20 --net-grab-at="$GRAB" --net-respawn="$RESPAWN" --net-spawn-at="$SPAWN" --net-step-away-at="$AWAY" --net-advance=8 > "$LOGS/host.log" 2>&1 &
PIDS="$!"
sleep "$LEAD"
for i in $(seq 1 "$CLIENTS"); do
    DIES=""
    if [ "$i" = "1" ]; then DIES="--net-die-at=$DIE --net-say-at=$SAYAT --net-bleed=2"; fi
    if [ "$i" = "2" ]; then DIES="--net-strike-at=$STRIKE --net-ping-at=$PINGAT --net-help-at=$HELP2"; fi
    if [ "$i" = "3" ]; then DIES="--net-drop-at=$DROPAT --net-die-at=$DOWN3 --net-bleed=8 --net-web-at=$WEBAT"; fi
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
# Where two reached for the same one, the slower went without: a client is refused by the host, and
# the host (which takes its own claims) simply finds it gone. If nobody's targets met, there's nothing to refuse.
refused=$(grep -ho "refused=[1-9]" "$LOGS"/*.log | wc -l)
missed=$(grep -h "\[NetProbe\] role" "$LOGS"/*.log | grep -v "target=none" | grep -c "got=none")
contested=$(grep -h "\[NetProbe\] role" "$LOGS"/*.log | grep -o "target=[^ ]*" | grep -v "target=none" | sort | uniq -d | wc -l)
check "players reaching for a taken supply went without ($refused refused, $missed missed, $contested contested)" "$([ "$contested" -eq 0 ] || [ $((refused + missed)) -ge 1 ]; echo $?)"
looks=$(grep -h "\[NetProbe\] role" "$LOGS"/*.log | grep -c "looks=.*:.*:[0-9a-f]\{6\}")
check "everyone sees the others' nickname, character and colours ($looks of $players)" "$([ "$looks" -eq "$players" ]; echo $?)"
fell=$(grep -h "name=Client1 " "$LOGS"/client1.log | grep -c "fell=True watched=[A-Za-z]")
check "a fallen player watches a living teammate" "$([ "$fell" -eq 1 ]; echo $?)"
back=$(grep -h "name=Client1 " "$LOGS"/client1.log | grep -c "back=True")
check "and comes back after the respawn time" "$([ "$back" -eq 1 ]; echo $?)"
saw=$(grep -h "\[NetProbe\] role" "$LOGS"/host.log | grep -c "saw_fallen=True")
check "the host saw them fallen" "$([ "$saw" -eq 1 ]; echo $?)"
report() { grep -h "\[NetProbe\] role" "$1"; }
num() { report "$1" | grep -o "$2=[-0-9.]*" | head -1 | cut -d= -f2; }
spread=$(grep -ho "clock_at_grab=[-0-9.]*" "$LOGS"/*.log | cut -d= -f2 | sort -n | awk 'NR==1{lo=$1} {hi=$1} END{printf "%.3f", hi-lo}')
hostclock=$(num "$LOGS/host.log" clock_at_grab)
check "everyone's clock follows the host's (host moved it on to $hostclock h; spread $spread h)" "$(awk -v s="$spread" -v h="$hostclock" 'BEGIN{exit !(s < 0.1 && h >= 7.9)}'; echo $?)"
puppets=0
for i in $(seq 1 "$CLIENTS"); do
    if [ "$(num "$LOGS/client$i.log" saw_puppets)" -ge 1 ] 2>/dev/null; then puppets=$((puppets + 1)); fi
done
check "every client saw the host's enemy ($puppets of $CLIENTS)" "$([ "$puppets" -eq "$CLIENTS" ]; echo $?)"
if [ "$CLIENTS" -ge 2 ]; then
    sent=$(num "$LOGS/client2.log" hits_sent)
    applied=$(num "$LOGS/host.log" hits_applied)
    check "a client's blow on it reached the host ($sent sent, $applied applied)" "$([ "${sent:-0}" -ge 1 ] && [ "${applied:-0}" -ge 1 ]; echo $?)"
fi
dealt=$(num "$LOGS/host.log" strikes_sent)
taken=0
for i in $(seq 1 "$CLIENTS"); do
    t=$(num "$LOGS/client$i.log" strikes_taken)
    taken=$((taken + ${t:-0}))
done
check "the host's enemy went for the clients and its blows landed on them ($dealt dealt, $taken taken)" "$([ "${dealt:-0}" -ge 1 ] && [ "$taken" -ge 1 ]; echo $?)"
if [ "$CLIENTS" -ge 3 ]; then
    hostgot=$(num "$LOGS/host.log" loot_received)
    dropped=$(grep -c "dropped=True" "$LOGS/client3.log")
    check "a dropped item appears for the others (client 3 dropped one: $dropped; the host got $hostgot)" "$([ "$dropped" -ge 1 ] && [ "${hostgot:-0}" -ge 1 ]; echo $?)"
fi
if [ "$CLIENTS" -ge 2 ]; then
    marked=$(num "$LOGS/client2.log" pings_sent)
    saw=0
    others=0
    for f in host client1 client3; do
        if [ -f "$LOGS/$f.log" ]; then
            others=$((others + 1))
            if [ "$(num "$LOGS/$f.log" pings_seen)" -ge 1 ] 2>/dev/null; then saw=$((saw + 1)); fi
        fi
    done
    check "a marked spot (B) appears for the others (client 2 marked $marked; $saw of $others saw it)" "$([ "${marked:-0}" -ge 1 ] && [ "$saw" -eq "$others" ]; echo $?)"
fi
if [ "$CLIENTS" -ge 3 ]; then
    torn=$(num "$LOGS/client3.log" webs_sent)
    down=0
    others=0
    for f in host client1 client2; do
        others=$((others + 1))
        if [ "$(num "$LOGS/$f.log" webs_received)" -ge 1 ] 2>/dev/null; then down=$((down + 1)); fi
    done
    check "a spider web one player tears comes down for everyone (client 3 tore $torn; $down of $others took it down)" "$([ "${torn:-0}" -ge 1 ] && [ "$down" -eq "$others" ]; echo $?)"
fi
downs1=$(num "$LOGS/client1.log" downs)
check "a downed player nobody helps bleeds out, then falls (client 1 went down $downs1 time(s))" "$([ "${downs1:-0}" -ge 1 ] && [ "$fell" -eq 1 ]; echo $?)"
if [ "$CLIENTS" -ge 3 ]; then
    downs3=$(num "$LOGS/client3.log" downs)
    up3=$(num "$LOGS/client3.log" revived)
    helped2=$(num "$LOGS/client2.log" helped_up)
    fell3=$(grep -h "name=Client3 " "$LOGS/client3.log" | grep -c "fell=False")
    check "a friend helps a downed player up, and they don't fall (client 3 down $downs3, up $up3; client 2 helped $helped2)" "$([ "${downs3:-0}" -ge 1 ] && [ "${up3:-0}" -ge 1 ] && [ "${helped2:-0}" -ge 1 ] && [ "$fell3" -eq 1 ]; echo $?)"
fi
said=$(num "$LOGS/client1.log" chat_sent)
heard=0
listeners=0
for f in host client2 client3; do
    if [ -f "$LOGS/$f.log" ]; then
        listeners=$((listeners + 1))
        if [ "$(num "$LOGS/$f.log" chat_seen)" -ge 1 ] 2>/dev/null; then heard=$((heard + 1)); fi
    fi
done
check "a chat line (Y) reaches everyone (client 1 said $said; $heard of $listeners read it)" "$([ "${said:-0}" -ge 1 ] && [ "$heard" -eq "$listeners" ]; echo $?)"
shared=$(num "$LOGS/host.log" loot_shared)
same=0
for i in $(seq 1 "$CLIENTS"); do
    if [ "$(num "$LOGS/client$i.log" loot_received)" -ge "${shared:-0}" ] 2>/dev/null; then same=$((same + 1)); fi
done
check "its loot appeared for everyone ($shared items; $same of $CLIENTS clients got all)" "$([ "${shared:-0}" -ge 1 ] && [ "$same" -eq "$CLIENTS" ]; echo $?)"
errors=$(grep -h "ERROR\|Unhandled exception" "$LOGS"/*.log | grep -v "ObjectDB\|leaked\|resources still in use" | sort -u | head -5)
if [ -n "$errors" ]; then echo "Errors in the logs:"; echo "$errors"; fi
echo "logs: $LOGS"
# Not a check, a clue: a carrier thrown at impossible speed (the hard limit caught it) names the last throw.
grep -h "sideways (\|broken velocity" "$LOGS"/*.log | sed 's/^/NOTE  /' | head -5
if [ "$fail" = "0" ]; then echo "COOP TESTS: PASS"; else echo "COOP TESTS: FAIL"; fi
exit "$fail"
