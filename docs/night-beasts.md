# Night beasts

Dante meets three beasts at the foot of the hill: a leopard, a lion and a she-wolf. On this mountain they come out after dark. The night should threaten: a reason to reach a station's fire before sunset, and a reason to keep moving when you don't.

## When they hunt

- While daylight (`WorldClock.Daylight`) is below `DarkBelow` (0.3), and the carrier is neither within `FireRadius` (25 m) of a station's fire nor in a shelter (`GameEvents.InShelter`).
- Fire, shelter or dawn sends every beast away: it turns and runs, and is removed past `GoneDistance` (70 m) or after `VanishAfter` (12 s), whichever comes first.
- Beasts walk straight lines, so trees and rocks can hold them up. A held-up beast detours 70° to one side for a second, then the other; a charge that runs into something ends; a beast stuck for `RelocateAfter` (4 s) slips into the dark and comes again from behind.
- With the shorter days (30 / 20 / 14 real minutes per in-game day on easy / normal / hard; `docs/survival-loop.md`) a night comes round often enough to matter.

## How a hunt goes

1. **Arrival.** After `ArrivalDelay` (15–40 s) of darkness a beast appears `SpawnDistance` (35–50 m) behind the carrier, uphill (+Z), out of sight of someone looking down the route. The spot must be within `MaxSpawnDrop` (40 m) of the carrier's height, so none appear on the back of the summit or beyond a ridge; a few directions are tried, widening to the sides. The first time, a hint says "Eyes in the dark".
2. **Stalking.** It circles the carrier inside its `StalkRadius` ring (14–22 m for the she-wolf), walking; left behind, it runs to catch up. Its eyes glow; the body is darkened toward a silhouette.
3. **Wind-up.** When its `AttackInterval` (12–24 s) runs out it stops, faces the carrier and growls for `WindUp` (1.2 s); the eyes swell. The first time, a hint warns to get out of the way. This is the tell.
4. **Charge.** It runs at `ChargeSpeed` along a nearly straight line: the charge bends only `ChargeTurnSpeed` degrees per second, so a late sidestep or a dive makes it miss and overshoot.
5. **Hit.** A hit throws the carrier `ThrowHeight` into the air along the charge (`GameEvents.RequestLaunch`). Space on the way down primes the roll and softens the landing; otherwise a hard landing knocks them down.
6. **Retreat.** Hit or miss, it backs off to the edge of its ring and stalks again.

Only one beast attacks at a time, and none while the carrier is down. Food in the pack (any item of category Food in `GameEvents.CarriedItems`) can be smelled: attacks come `FoodHurry` (1.6×) sooner. A noticed carrier (`GameEvents.Detectability` above 1: the restsite president's podium speech, the megaphone's echo) is attacked that much sooner again.

A megaphone blast (`GameEvents.MegaphoneBlasted`, docs/restsite-president.md) throws off every beast in its radius for a few seconds: a crouch or a charge under way breaks off, and it backs away without attacking. Then the echo makes the carrier easier to notice.

## The three beasts (`wildlife/default_beasts.tres`)

| Beast | From circle | Model (placeholder) | Character |
|---|---|---|---|
| She-wolf | Limbo (0) | Kenney dog | Hunger: with food in the pack her hit tears it out (`GameEvents.RequestDrop`) and only knocks the carrier; the food lies on the ground to take back. Most common. |
| Leopard | Lust (1) | Kenney tiger | Fast and quick: short wind-up (0.9 s), 12 m/s charge that bends more. |
| Lion | Gluttony (2) | Kenney lion | Slow to start (1.5 s wind-up) but its hit throws 5 m. |

How many hunt at once: `BaseBeasts` + circle / `BeastsPerCircles`, at most `MaxBeasts` (1 in Limbo, 2 from circle 3, 3 from circle 6, 4 deepest).

## Code

- `wildlife/BeastSpecies.cs`: one beast (look, eyes, stalking, charge, throw, circle, weight).
- `wildlife/BeastRoster.cs`: the list of beasts.
- `wildlife/NightBeast.cs`: a beast's body and modes (Stalk, WindUp, Charge, Retreat, Leave).
- `wildlife/BeastDirector.cs`: brings them in, lets one attack, sends them away, says what is happening through hints. Reads GameEvents, RunState and the WorldClock only. Removes a beast that falls more than `FellBelow` (60 m) below the carrier.
- The throw uses `GameEvents.RequestLaunch(horizontal, height, cause)`, handled in `PlayerController` (also used by an animal's shove).

## Debug and tests

- `godot --path . -- --hour=23 --beasts-now`: night, and the beasts come within seconds.
- `debug/tests/BeastTests.tscn`: none by day; one arrives behind at night; it stalks at a distance; it growls before it charges and the hit throws; a late sidestep makes it miss; the she-wolf takes food; a noticed carrier is attacked sooner; a megaphone blast breaks off a crouch; fire and dawn send them away; more beasts deeper, no lion in Limbo.

## Later

Real models and growl sounds (the Kenney pets are placeholders); beasts reacting to a lit torch; a beast following the scent of a dropped food item.
