# Managed allocation and frame-pause audit

Godot 4.7.2 .NET, `fix/gameplay-math-audit`. This follows the terrain streaming work described in
[terrain-streaming-performance.md](terrain-streaming-performance.md). Main and Claude's worktree
remain separate; this validation does not cover his newer sword/cart feature branch.

## Evidence and attribution

The active workspace previously passed its full suite, but the clean committed candidate at
`b0841589` failed TerrainTests with a 29.8 ms plant step. Shared index topology and a blocked plant
plan reduced allocation further, yet another sample reached 33.63 ms: 32.54 ms was GC pause,
and scene insertion was the largest interrupted slice. Only 17,224 managed bytes were allocated
on the measured main thread during that step. That counter excludes allocations on other threads.

EventPipe then established the distinction that wall timing alone could not: collection 9 had
Depth 1 and took about 30.96 ms; execution-engine suspension took about 0.004 ms. In this capture,
the long pause was actual collection work, not a long wait to suspend a thread. The trace reached
161,068 GC handles and roughly 42,000-46,000 finalization-promoted objects per reported collection.
These observations directed the fix toward transient Godot binding wrappers.

The trace reader reported an EOF/FormatException after recorded events. The collection-9 start,
end and suspension records are complete, but the file must be described as a partial capture.
`GC 1/0` in the earlier lightweight log meant collection-count deltas for generations 0 and 2;
it did not distinguish a generation-1 collection. See the official
[.NET GC event definitions](https://learn.microsoft.com/en-us/dotnet/fundamentals/diagnostics/runtime-garbage-collection-events).

## Ownership changes

- CharacterAnimator keeps 14 fixed StringName parameter keys instead of constructing them for
  every blend, cadence and transition update. Animation graph, rates and calculations are unchanged.
- FootIK reuses its owned ray query and exclusion array, disposes each returned dictionary, and
  uses cached exact-string Variant keys for position and normal. Its output values are scoped.
- PlayerController reuses three explicitly created KinematicCollision3D probes and disposes them
  on exit. Borrowed slide contacts, scene nodes, worlds and direct-space objects remain borrowed.
- PickupInteractor caches its group key and disposes the returned array through its underlying
  untyped Godot Array. The generic wrapper itself does not implement IDisposable.
- WorldClock caches the root NodePath while retaining dynamic RunState lookup. SurfaceLookup
  caches its metadata key. HurtVignette caches the strength shader parameter name.

These are allocation/lifetime changes, not changes to movement, IK, animation or damage math.
The [Godot Variant implementation](https://raw.githubusercontent.com/godotengine/godot/4.7-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/Variant.cs)
creates a finalizable disposer for string values; Vector3 values do not need one. The
[Dictionary getter](https://raw.githubusercontent.com/godotengine/godot/4.7-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/Dictionary.cs)
borrows its key and returns an owned value. Never dispose a cached Variant key or a copied alias:
struct copies share its disposer. Dispose owned results, not borrowed scene objects.

The plant plan now uses 512-record blocks, avoiding large-array growth copies. A 10,000-record
test measured 492,640 allocated bytes versus 480,000 logical payload bytes. Smooth chunks also
reuse immutable shared topology. Geometry, plant density and placement remain unchanged.

## Validation and limits

- Build: zero warnings/errors.
- AnimationSmoothTests: 14 checks pass; the measured 600-update native AnimationTree loop allocates
  zero managed bytes per animator update. This is not a claim that the whole player allocates zero.
- LocomotionTests: 36 checks pass. PlantBufferTests: 12 checks pass.
- ForestTerrainTests: 18 checks pass, including exact geometry, all 2,584 instance matrices,
  shared-topology resizing and unfinished-build cancellation.
- One targeted TerrainTests run recorded a raw 13.45 ms plant peak, including 12.88 ms GC pause.
  An intermediate ownership trace peaked at 73,122 handles, down from 161,068; that capture
  predates the final Variant-key and vignette-key fixes and is not the final handle count.

The 16 ms wall-time assertion is unchanged. No GC pause is subtracted, global GC mode changed,
or no-GC region introduced. Reduced wrapper churn does not guarantee universal freedom from GC,
native-call or scheduling spikes. Android/Galaxy Tab A9 measurements remain finishing work.

The clean full-suite log `main-readiness-owned-bindings-smoke.log` completed with RESULT: PASS:
54 scenes / 730 checks. TerrainTests passed 15 checks, with a 12.5 ms plant peak and 6.5 ms chunk
application peak; ForestTerrainTests passed 18 checks with a 3.9 ms shared plant-frame peak.
Individual logs are in `C:/Users/user/AppData/Local/Temp/tmp.dF3xIfPhcC`.

The native playground Start check also passed four checks and walked 1.70 m. The inspected image
is `clean-ready-playground-view/20261110-094340-046-2148/frame_0003.jpg` under the artifact root.
These results establish this clean candidate; commit identifiers belong in the integration handoff.

## Reproduce and files

```powershell
dotnet build
godot --headless --path . --fixed-fps 60 res://debug/tests/TerrainTests.tscn -- --profile-plants
godot --headless --path . --fixed-fps 60 res://debug/tests/AnimationSmoothTests.tscn
godot --headless --path . --fixed-fps 60 res://debug/tests/PlantBufferTests.tscn
```

For attribution, attach dotnet-trace to the actual Godot CLR process with the low-overhead
`gc-collect` profile; use allocation sampling only when needed. Compare suspension, collection
and restart events. Diagnostic tools are isolated artifacts, not game dependencies. See
[Microsoft's tracing guide](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).

Files: `player/CharacterAnimator.cs`, `FootIK.cs`, `PlayerController.cs`, `PickupInteractor.cs`,
`HurtVignette.cs`; `core/WorldClock.cs`, `SurfaceLookup.cs`; terrain plan/topology implementation
including `terrain/PlantBuffer.cs/.uid`; `debug/tests/AnimationSmoothTests.cs`,
`PlantBufferTests.cs/.uid/.tscn` and `ForestTerrainTests.cs`. Trace/profile logs live under
`D:/Game dev/math-audit-artifacts`, including `terrain-gc-events.jsonl` and
`clean-blocked-plan-profile.log`.
