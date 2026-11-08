# player

The carrier: character controller, step planner, foot IK (TwoBoneIK3D) and the gameplay camera.

Ragdoll impact damage uses lost incoming speed along the hit surface's normal. Tangential
sliding changes do not count as blows into the ground, and outward rebound speed is not counted
again. `core/ImpactMath.cs` holds this calculation; the existing forward probe still identifies
the surface and its hardness. `debug/tests/ImpactMathTests.tscn` covers analytical collision cases;
`VitalsTests.tscn` exercises actual drops onto grass and rock.
