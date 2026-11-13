# player

The carrier: character controller, step planner, foot IK (TwoBoneIK3D) and the gameplay camera.

Hybrid recovery adds spring-driven hips/free arms and brief physical arm tension during living
falls. Try `debug/TestActiveRagdoll.tscn` with F6; see [setup and limits](../../docs/active-ragdoll.md).

Phase-synchronized gait blending and authored takeoff/landing use the existing Quaternius clips.
Open `debug/TestSmoothLocomotion.tscn` with F6; [assets and tuning guide](../docs/smooth-locomotion.md).

Modular skinned garments, rigid equipment, body visibility and the Blender pipeline are documented
in [modular clothing](../docs/modular-clothing.md). Try `debug/TestClothing.tscn` with F6.

Ragdoll impact damage uses lost incoming speed along the hit surface's normal. Tangential
sliding changes do not count as blows into the ground, and outward rebound speed is not counted
again. `core/ImpactMath.cs` holds this calculation; the existing forward probe still identifies
the surface and its hardness. `debug/tests/ImpactMathTests.tscn` covers analytical collision cases;
`VitalsTests.tscn` exercises actual drops onto grass and rock.
