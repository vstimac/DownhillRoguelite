# Shared co-op presentation and world authority

Solo and co-op now share `main` and the same public build. The old `feature/multiplayer-beta`
branch remains as integration history; continue development on `main`. The earlier standalone
active-ragdoll commits remain independently usable.

Public Windows and Linux presets support both modes and use the public updater. Older separately
exported co-op tests can retain the `coop` feature and their matching-package update guard.

- Each player owns their movement and fall simulation. At 15 Hz the host relays the final
  position/rotation of 22 key bones, model placement and sword/shield placement. Remote drivers
  interpolate these poses with a 120 ms delay, then existing rig followers drive characters and
  clothes. Remote players have no local input, ragdoll solver or gameplay collision authority.
- The host alone simulates humanoid enemies, night beasts, wildlife and Tuntun. Ten snapshots
  per second share identity, position, animation, health, death and procedural model placement.
  Client blows go to the host. Host blows go to the targeted player; a host fall no longer
  stops an enemy attacking a standing teammate. Wildlife uses the selected player's movement
  input and sends a shove to that player. Enemy corpses use authored animation in co-op;
  their sinking and removal are controlled by the host too.
- Terrain retention and creature population consider all players on the host. Population caps
  stay global. This enables separated players without multiplying the nearby population.
- Boons, relics and contract boards use host pickup reservations. A station offers one shared
  boon choice; its effect belongs to the player who takes it. Accepted jobs still belong to their
  carrier. Other players see the delivery table and shared job outcome without receiving duplicate
  cargo. Unclaimed dropped loot and taken-object history are replayed for late joiners.
- Station visitors are selected deterministically in station order. NPC presence, facing,
  animation and recorded speech are shared. The host grants one participant at a time control
  of an NPC conversation, and releases ownership on timeout/disconnect. Dialogue choices and
  personal progression UI stay with that participant. Public restsite bridge/board presentation
  is shared without replacing personal saves. Existing quest drops join the shared loot stream.
- Protocol `downhill-coop-6` rejects older builds. Pose packets have fixed size, finite-value,
  sequence and rate checks. Corpse removal tombstones prevent delayed state packets recreating
  an actor. Shared drops are deduplicated by world identity.

The shared world has network delay; cameras, HUDs and personal inventory are individual.
This is co-op replication, not deterministic lockstep or a PvP hitbox agreement. Cosmetic
particles and sound randomization are not a synchronized simulation.

## Validation

- `NetPoseTests`: 13 checks with actual final skeletons, a real combat swing, weapon mounts,
  physical fall/recovery and malformed/stale/flooded packets. Samples sharing a millisecond
  select the latest sequence instead of an older pose.
- `tools/coop_test.sh`: host plus three clients, 25 checks including LAN discovery, final poses,
  single host simulation, combat delivery, shared pickups, revival, loot, clock, chat and webs.
- `tools/coop_world_test.sh`: host, participant and a late joiner. All report the same actor
  identities/death states and taken keys. The scenario checks four actor types, actual remote
  falls, NPC arbitration/speech, shared delivery tables, deduplicated loot and pickup persistence.
- Test peers use isolated save directories. These are local-process/LAN tests; WAN latency,
  packet-loss stress and long-running population/loot pressure need separate playtesting.

Unrelated `CLAUDE.md`, `PR-multiplayer-beta.md` and character image import metadata are excluded.
