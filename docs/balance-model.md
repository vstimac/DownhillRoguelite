# Balance model (M2)

How `balance/BalanceController` works, and why, so it can be tuned without re-deriving it. All numbers live in `balance/default_balance.tres` (`BalanceSettings`).

## Model

The upper body and load form an inverted pendulum about the feet, on two axes: **roll** (positive toward the carrier's right) and **pitch** (positive toward its forward). State is a tilt angle and tilt velocity per axis.

**Only the load destabilizes.** The carrier's own weight is folded into `Strength`, and every disturbance uses the load's mass × height. A combined centre of mass would let 70 kg of body dilute the difference between a high and a low pack (1.11 m vs 1.00 m), while load-only moments differ by 42% (29.9 vs 21.0 kg·m for the test loads). See also `docs/inventory-design.md`.

Torques on the tilt:

| Term | Formula | Notes |
| --- | --- | --- |
| Gravity | m·g·(h·sin θ + load offset) | Load offset sideways/forward from the feet |
| Hold | −m·g·(h·sin θ_target + compensated offset) | Feedforward: the carrier holds its target posture exactly |
| Correction | −Strength·(θ − θ_target) − damping·θ' | Muscles; damping from `DampingRatio` |
| Inertial | −m·h·acceleration × `InertialFactor` | Braking pitches forward, turning swings outward |
| Slope | m·g·h·(normal · axis) × `Slope*Factor` | Cross-slope rolls, descents pitch |
| Footfall | impulse ∝ speed × m·h × (1 + looseness) | Sideways toward the landing foot, forward on descents |
| Stumble push | `StumblePushFactor` × m·g·h along the stumble | Only while stumbling: the body keeps tipping |
| Recovery | −`RecoveryTorque` × effort | Only while stumbling: leaning against it, or bracing |

**Target posture.** Forward/back load offset is leaned into fully (`AutoPitchCompensation` 1). Sideways offset only partly (`AutoRollCompensation` 0.25); the rest pushes the tilt off target, amplified by how little `Strength` exceeds the load's m·g·h. That residual is the player's job: lean input shifts the roll target by up to `MaxLeanAngle`. Without the hold feedforward, gravity would amplify every target by Strength / (Strength − m·g·h), about 4× for load A, which made lean input wildly overshoot.

## States

Imbalance = the combined centre of mass's horizontal offset from the feet ÷ the support half size on that axis (from how far apart the planted feet are). 1 means the centre of mass is at the edge of the support.

- **Stable** below `WobbleThreshold` (0.5)
- **Wobbling** up to `StumbleThreshold` (1.0)
- **Stumbling**: lasts at most `StumbleWindow` (0.9 s). Recovers below `RecoverThreshold` (0.75); falls at `FallThreshold` (2.5) or when the window runs out. Locomotion slows and lurches toward the tilt.
- **Fallen**: input off for `FallDuration` (2.5 s), then upright and stable.

## Tuning targets (checked by `debug/tests/BalanceTests`)

- Empty and load B stay stable at rest; load A rests wobbling toward its heavy side (~0.5) and is steadied by holding the counter-lean (~0.15).
- Stopping from a stride pitches A forward about twice as much as B.
- **A vs B:** striding then braking down the 30° lane with no lean input, A falls and B stays stable.
- Bracing cuts the sway from a knock; an ignored stumble becomes a fall, a countered one recovers.

If a change breaks one of these, the test output prints the measured values.
