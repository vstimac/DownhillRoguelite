using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Predator checks (wolves, PredatorTuning) on the test field: a pack notices a carrier, stalks it
/// (circles at a distance and closes in) and bites one wolf at a time after a visible wind-up; a
/// sidestep after the wind-up makes it miss; crouching slowly past a pack goes unnoticed where a
/// sprint doesn't; one solid blow scares a wolf off and half the pack hurt sends it all away; a
/// killed wolf drops hide and raw meat; a fall draws wolves in (a downed carrier is bitten sooner);
/// a carrier at a station's fire is left alone and one bleeding out is circled but never bitten;
/// they live only in the lower circles, more often and bolder at night, one pack at a time; in co-op
/// they pick their prey among all carriers and a guest sees the wind-up.
/// </summary>
public partial class WildlifeTests
{
    private const string WolfName = "Wolf";

    private async Task TestPredators(WildlifeTable table, WildlifeSpecies wolf)
    {
        if (wolf.Temperament != Temperament.Predator)
        {
            Check("a predator species (wolf) is in the table", false, $"found {wolf.DisplayName} ({wolf.Temperament})");
            return;
        }
        TestWolfHabitatAndSpawning(wolf);
        await TestWolfLookAndBody(table, wolf);
        await TestWolfStalkAndBite(table, wolf);
        await TestWolfDodge(table, wolf);
        await TestWolfStealth(table, wolf);
        await TestWolfNightBolder(table, wolf);
        await TestWolfScaredAndPackRetreat(table, wolf);
        await TestWolfFallDraws(table, wolf);
        await TestWolfFire(table, wolf);
        await TestWolfCoopTarget(table, wolf);
    }

    /// <summary>A pack of wolves, facing a point, ticked by the test; bites go to the stand-in co-op peer.</summary>
    private List<WildlifeAnimal> Pack(WildlifeSpecies wolf, WildlifeTable table, Vector3 centre, int size, Vector3 facing, ulong seed)
    {
        List<WildlifeAnimal> pack = new();
        WildlifeHerd? herd = size > 1 ? new WildlifeHerd() : null;
        for (int i = 0; i < size; i++)
        {
            Vector3 offset = i == 0 ? Vector3.Zero : Vector3.Right.Rotated(Vector3.Up, i * Mathf.Tau / Mathf.Max(size - 1, 1)) * 2.5f;
            WildlifeAnimal animal = Standalone(wolf, table, centre + offset, seed + (ulong)i);
            Face(animal, facing);
            animal.TargetPeer = StandInPeer;
            herd?.Join(animal);
            pack.Add(animal);
        }
        return pack;
    }

    /// <summary>A carrier standing (shuffling in place, so it makes a little noise) at a point, by day or at night.</summary>
    private static CarrierSense Standing(Vector3 at, float daylight, bool torch = false)
    {
        return new CarrierSense(at, new Vector3(0.0f, 0.0f, 1.3f), Vector3.Zero, false, false, 0.0f, false, torch, daylight);
    }

    private static int CountHunting(List<WildlifeAnimal> pack)
    {
        return pack.FindAll(a => a.Hunting).Count;
    }

    /// <summary>Wolves live only in the lower circles; they're placed more often at night, never past the pack limit, and not at all when switched off.</summary>
    private void TestWolfHabitatAndSpawning(WildlifeSpecies wolf)
    {
        List<int> circles = new();
        for (int circle = 0; circle <= WildlifeBeyondCircle; circle++)
        {
            if (wolf.LivesIn(circle))
            {
                circles.Add(circle);
            }
        }
        bool lower = circles.Count > 0 && circles.TrueForAll(c => c >= 4 && c < WildlifeBeyondCircle);
        float day = WildlifeSpawner.SpawnWeight(wolf, 0, 1.0f);
        float night = WildlifeSpawner.SpawnWeight(wolf, 0, 0.0f);
        float full = WildlifeSpawner.SpawnWeight(wolf, PredatorTuning.MaxPacks, 0.0f);
        bool roughGround = wolf.LivesOn("Rock", 10.0f) && wolf.LivesOn("Scree", 10.0f) && !wolf.LivesOn("Grass", 10.0f);
        Check("wolves live only in the lower circles on rough ground, more often at night, one pack at a time",
            lower && roughGround && night > day && (!PredatorTuning.Enabled || day > 0.0f) && full == 0.0f && wolf.GroupSize.X >= 2 && wolf.GroupSize.Y <= 4,
            $"circles {string.Join(",", circles.ConvertAll(c => (c + 1).ToString()))} (1-based), rock/scree not grass {roughGround}, weight by day {day:0.0}, at night {night:0.0}, with a pack out {full:0.0}, packs of {wolf.GroupSize.X}-{wolf.GroupSize.Y}, switched {(PredatorTuning.Enabled ? "on" : "off")}");
    }

    /// <summary>
    /// The wolf wears its muted coat on every mesh (not the pack's flat colour), and the carrier's real
    /// body runs into it rather than through it.
    /// </summary>
    private async Task TestWolfLookAndBody(WildlifeTable table, WildlifeSpecies wolf)
    {
        PlayerController player = PlayerBody!;
        // A stand-in with the carrier's own body (its shapes, layer and mask) on the flat test field,
        // so the check doesn't depend on where the steps before left the real carrier.
        CharacterBody3D carrier = new() { CollisionLayer = player.CollisionLayer, CollisionMask = player.CollisionMask };
        foreach (Node node in player.FindChildren("*", "CollisionShape3D", false, false))
        {
            CollisionShape3D shape = (CollisionShape3D)node;
            carrier.AddChild(new CollisionShape3D { Shape = shape.Shape, Transform = shape.Transform, Disabled = shape.Disabled });
        }
        AddChild(carrier);
        carrier.GlobalPosition = _fieldTop + new Vector3(20.0f, 0.02f, 20.0f);
        WildlifeAnimal animal = Standalone(wolf, table, carrier.GlobalPosition + Vector3.Forward * 1.6f + Vector3.Up * 0.03f, 501);
        await WaitSeconds(0.1f);
        int meshes = 0;
        int coated = 0;
        Material? coat = wolf.Coat != null ? DownhillRoguelite.Environment.WorldStyle.Coat(wolf.Coat) : null;
        string why = "";
        foreach (Node node in animal.FindChildren("*", "MeshInstance3D", true, false))
        {
            meshes++;
            Material? worn = ((MeshInstance3D)node).MaterialOverride;
            if (coat != null && worn == coat && coat != wolf.Coat && DownhillRoguelite.Environment.WorldStyle.IsInStyle(worn, out why))
            {
                coated++;
            }
        }
        Vector3 toward = animal.GlobalPosition - carrier.GlobalPosition;
        toward.Y = 0.0f;
        KinematicCollision3D hit = new();
        // A little above the ground, so the slope underfoot doesn't stop the move first (at two heights).
        bool byWolf = false;
        foreach (float lift in new[] { 0.25f, 0.6f })
        {
            bool blocked = carrier.TestMove(carrier.GlobalTransform.Translated(Vector3.Up * lift), toward.Normalized() * 2.5f, hit);
            byWolf = blocked && hit.GetCollider() == animal;
            if (byWolf) { break; }
        }
        Check("a wolf wears the palette coat (grey-brown) and the carrier's body collides with it",
            wolf.Coat != null && meshes > 0 && coated == meshes && byWolf && animal.Radius >= 0.4f,
            $"palette coat {DownhillRoguelite.Environment.WorldStyle.NameOf(coat)} from {wolf.Coat?.ResourcePath} {why}, {coated}/{meshes} meshes coated, carrier stopped by the wolf {byWolf} (hit {hit.GetCollider()}), body radius {animal.Radius:0.00} m; wolf {toward.Length():0.00} m off, feet {animal.GlobalPosition.Y - carrier.GlobalPosition.Y:0.00} m from the carrier's (mask {carrier.CollisionMask}, {carrier.GetChildCount()} shape)");
        Free(new[] { animal });
        carrier.QueueFree();
        await WaitSeconds(0.1f);
    }

    /// <summary>
    /// A pack of three at night notices a carrier with a torch 18 m off, stalks it (circling at a
    /// distance, closing in), then bites one wolf at a time, each after a crouched wind-up. The
    /// stealth eye shows them alerted.
    /// </summary>
    private async Task TestWolfStalkAndBite(WildlifeTable table, WildlifeSpecies wolf)
    {
        Vector3 carrier = Field(50.0f, 40.0f);
        List<WildlifeAnimal> pack = Pack(wolf, table, Field(50.0f, 22.0f), 3, Vector3.Back, 301);
        FootSlipStats feet = new();
        foreach (WildlifeAnimal member in pack)
        {
            member.AddChild(new FootSlipMeter { Stats = feet });
        }
        await Live(pack, 0.3f, _ => CarrierSense.Still(carrier + Vector3.Back * 300.0f));
        int strikes = 0;
        float damage = 0.0f;
        void OnStrike(NetStrike strike)
        {
            if (strike.Peer == StandInPeer && strike.Cause == WolfName)
            {
                strikes++;
                damage += strike.Damage;
            }
        }
        GameEvents.NetCarrierStruck += OnStrike;
        float noticed = -1.0f;
        float firstWindUp = -1.0f;
        float closestBeforeAttack = 99.0f;
        float startRing = -1.0f;
        float ringBeforeAttack = -1.0f;
        float turned = 0.0f;
        Vector3 lastBearing = Vector3.Zero;
        int mostAttacking = 0;
        float windUpStart = -1.0f;
        float shortestWindUp = 99.0f;
        bool crouchedSeen = false;
        WildlifeAnimal.Awareness eye = WildlifeAnimal.Awareness.Unaware;
        WildlifeAnimal watched = pack[0];
        await Live(pack, 24.0f, _ => Standing(carrier, 0.0f, true), (time, sense) =>
        {
            if (noticed < 0.0f && CountHunting(pack) == pack.Count)
            {
                noticed = time;
                startRing = watched.StalkRing;
                eye = StealthEye.Strongest(new List<Node>(pack), carrier, 35.0f);
            }
            int attacking = pack.FindAll(a => a.State is WildlifeAnimal.Mode.WindUp or WildlifeAnimal.Mode.Lunge).Count;
            mostAttacking = Mathf.Max(mostAttacking, attacking);
            bool anyWindUp = pack.Exists(a => a.State == WildlifeAnimal.Mode.WindUp);
            if (anyWindUp && firstWindUp < 0.0f)
            {
                firstWindUp = time;
                ringBeforeAttack = watched.StalkRing;
            }
            if (anyWindUp && windUpStart < 0.0f)
            {
                windUpStart = time;
            }
            if (!anyWindUp && windUpStart >= 0.0f)
            {
                shortestWindUp = Mathf.Min(shortestWindUp, time - windUpStart);
                windUpStart = -1.0f;
            }
            foreach (WildlifeAnimal animal in pack)
            {
                // The crouch shows in the model's pose (streamed to guests as is).
                crouchedSeen |= animal.State == WildlifeAnimal.Mode.WindUp && animal.NetVisual.Origin.Y < -0.05f;
            }
            if (noticed >= 0.0f && firstWindUp < 0.0f)
            {
                bool rushing = pack.Exists(a => a.State == WildlifeAnimal.Mode.Stalk && Flat(a.GlobalPosition, carrier) < PredatorTuning.StalkRingClose - 2.0f);
                if (!rushing)
                {
                    closestBeforeAttack = Mathf.Min(closestBeforeAttack, Flat(watched.GlobalPosition, carrier));
                }
                Vector3 bearing = watched.GlobalPosition - carrier;
                bearing.Y = 0.0f;
                if (lastBearing != Vector3.Zero && bearing.LengthSquared() > 0.01f)
                {
                    turned += Mathf.Abs(lastBearing.SignedAngleTo(bearing, Vector3.Up));
                }
                lastBearing = bearing;
            }
        });
        GameEvents.NetCarrierStruck -= OnStrike;
        int bites = 0;
        foreach (WildlifeAnimal animal in pack)
        {
            bites += animal.Bites;
        }
        Check("a wolf pack notices a carrier and stalks it: circles at a distance and closes in before it attacks",
            noticed >= 0.0f && noticed < 3.0f && Mathf.RadToDeg(turned) > 45.0f && closestBeforeAttack > PredatorTuning.StalkRingClose - 3.0f
                && startRing > ringBeforeAttack && firstWindUp - noticed >= PredatorTuning.StalkSecondsNight.X - 0.5f
                && eye == WildlifeAnimal.Awareness.Alerted,
            $"all stalking after {noticed:0.0} s, circled {Mathf.RadToDeg(turned):0} deg, closest {closestBeforeAttack:0.0} m before attacking, ring {startRing:0.0} -> {ringBeforeAttack:0.0} m, first wind-up {firstWindUp - noticed:0.0} s after noticing, stealth eye {eye}");
        Check("wolves bite one at a time, each after a crouched wind-up you can see, for moderate damage",
            bites >= 2 && strikes == bites && mostAttacking == 1 && shortestWindUp >= PredatorTuning.WindUpSeconds - 0.05f && crouchedSeen
                && Mathf.IsEqualApprox(damage, bites * PredatorTuning.BiteDamage),
            $"{bites} bites in 24 s ({strikes} strikes to the co-op peer, {damage:0} hp), most attacking at once {mostAttacking}, shortest wind-up {shortestWindUp:0.00} s, crouch seen {crouchedSeen}");
        // The walk and gallop follow each wolf's real speed: planted paws stay put instead of gliding.
        Check("hunting wolves' paws stay planted while they circle, rush and spring", feet.Samples >= 60 && feet.Skate < 0.35f, feet.ToString());
        Free(pack);
    }

    /// <summary>A lone wolf winds up at a carrier standing still; the carrier steps aside once it has crouched, and the spring misses.</summary>
    private async Task TestWolfDodge(WildlifeTable table, WildlifeSpecies wolf)
    {
        Vector3 start = Field(-50.0f, 30.0f);
        WildlifeAnimal hunter = Pack(wolf, table, Field(-50.0f, 18.0f), 1, Vector3.Back, 321)[0];
        hunter.StartStalk(start, false);
        float windUpAt = -1.0f;
        Vector3 side = Vector3.Zero;
        Vector3 at = start;
        int windUps = 0;
        int firstLungeBites = -1;
        await Live(new[] { hunter }, 20.0f, time =>
        {
            if (windUpAt >= 0.0f && side != Vector3.Zero)
            {
                // Steps aside (3.5 m/s) once the wolf has fixed its spring.
                float moving = time - windUpAt - PredatorTuning.WindUpSeconds * PredatorTuning.WindUpAimShare - 0.05f;
                if (moving > 0.0f)
                {
                    at = start + side * Mathf.Min(moving, 1.2f) * 3.5f;
                    return new CarrierSense(at, side * 3.5f, side, false, false, 0.0f, false, true, 0.0f);
                }
            }
            return Standing(at, 0.0f, true);
        }, (time, sense) =>
        {
            if (hunter.State == WildlifeAnimal.Mode.WindUp && windUpAt < 0.0f)
            {
                windUpAt = time;
                Vector3 toward = Flat3(sense.Position - hunter.GlobalPosition).Normalized();
                side = Vector3.Up.Cross(toward).Normalized();
            }
            windUps = hunter.WindUps;
            if (firstLungeBites < 0 && hunter.Lunges >= 1 && hunter.State != WildlifeAnimal.Mode.Lunge)
            {
                firstLungeBites = hunter.Bites;
            }
        });
        Check("a sidestep after the wolf's wind-up makes its spring miss",
            windUps >= 1 && hunter.Lunges >= 1 && firstLungeBites == 0,
            $"wind-ups {windUps}, springs {hunter.Lunges}, bites from the first spring {firstLungeBites}");
        Free(new[] { hunter });
    }

    /// <summary>A carrier crouching slowly past a pack 20 m off (its crouched figure is seen at about 19 m by day) goes unnoticed; one sprinting past at the same distance is hunted.</summary>
    private async Task TestWolfStealth(WildlifeTable table, WildlifeSpecies wolf)
    {
        List<WildlifeAnimal> quiet = Pack(wolf, table, Field(-60.0f, -40.0f), 3, Vector3.Forward, 341);
        Vector3 from = Field(-75.0f, -20.0f);
        Vector3 sneak = new(0.9f, 0.0f, 0.0f);
        await Live(quiet, 0.3f, _ => CarrierSense.Still(from + Vector3.Back * 300.0f));
        await Live(quiet, 30.0f, time => new CarrierSense(from + sneak * time, sneak, sneak.Normalized(), false, false, 20.0f, true, false, 1.0f));
        int quietHunts = 0;
        foreach (WildlifeAnimal animal in quiet)
        {
            quietHunts += animal.Stalks;
        }
        Free(quiet);

        List<WildlifeAnimal> loud = Pack(wolf, table, Field(-60.0f, -40.0f), 3, Vector3.Forward, 351);
        Vector3 run = new(6.0f, 0.0f, 0.0f);
        await Live(loud, 0.3f, _ => CarrierSense.Still(from + Vector3.Back * 300.0f));
        await Live(loud, 5.0f, time => new CarrierSense(from + run * time, run, run.Normalized(), true, false, 20.0f, false, false, 1.0f));
        int loudHunts = CountHunting(loud);
        Check("crouching slowly past a wolf pack 20 m off slips by; sprinting past at the same distance gets it hunting",
            quietHunts == 0 && loudHunts >= 2,
            $"crouching walker at 0.9 m/s 20 m off: {quietHunts} hunts started; sprinter: {loudHunts}/3 hunting");
        Free(loud);
    }

    /// <summary>By night wolves start closer and bite sooner than by day.</summary>
    private async Task TestWolfNightBolder(WildlifeTable table, WildlifeSpecies wolf)
    {
        float[] firstWindUp = { -1.0f, -1.0f };
        float[] ring = { 0.0f, 0.0f };
        for (int pass = 0; pass < 2; pass++)
        {
            float daylight = pass == 0 ? 1.0f : 0.0f;
            Vector3 carrier = Field(70.0f, -60.0f);
            WildlifeAnimal hunter = Pack(wolf, table, Field(70.0f, -84.0f + 0.0f), 1, Vector3.Back, 361)[0];
            // It learns the hour from its first look round, then hears the carrier.
            await Live(new[] { hunter }, 0.2f, _ => CarrierSense.Still(carrier + Vector3.Back * 300.0f) with { Daylight = daylight });
            hunter.StartStalk(carrier, false);
            ring[pass] = hunter.StalkRing;
            int index = pass;
            await Live(new[] { hunter }, 20.0f, _ => Standing(carrier, daylight, true), (time, sense) =>
            {
                if (firstWindUp[index] < 0.0f && hunter.State == WildlifeAnimal.Mode.WindUp)
                {
                    firstWindUp[index] = time;
                }
            });
            Free(new[] { hunter });
            await WaitSeconds(0.05f);
        }
        Check("wolves are bolder at night: they circle closer and bite sooner than by day",
            firstWindUp[1] > 0.0f && firstWindUp[0] > firstWindUp[1] && ring[1] < ring[0],
            $"first wind-up {firstWindUp[0]:0.0} s by day, {firstWindUp[1]:0.0} s at night; starting ring {ring[0]:0.0} m by day, {ring[1]:0.0} m at night");
    }

    /// <summary>
    /// One solid sword blow scares a wolf off while the rest hunt on; a kick on a second (half the
    /// pack of four hurt) sends the whole pack away, and it stays away. A wolf killed drops hide and raw meat.
    /// </summary>
    private async Task TestWolfScaredAndPackRetreat(WildlifeTable table, WildlifeSpecies wolf)
    {
        Vector3 carrier = Field(-20.0f, 70.0f);
        List<WildlifeAnimal> pack = Pack(wolf, table, Field(-20.0f, 55.0f), 4, Vector3.Back, 381);
        foreach (WildlifeAnimal animal in pack)
        {
            animal.StartStalk(carrier, false);
        }
        await Live(pack, 2.0f, _ => Standing(carrier, 1.0f));
        pack[0].TakeHit(26.0f, Vector3.Forward * 2.0f, "sword");
        bool oneFled = pack[0].State == WildlifeAnimal.Mode.Flee && !pack[0].IsDead;
        await Live(pack, 1.0f, _ => Standing(carrier, 1.0f));
        int stillHunting = CountHunting(pack);
        pack[1].TakeHit(8.0f, Vector3.Forward * 2.0f, "sword");
        bool allFled = pack.TrueForAll(a => a.State == WildlifeAnimal.Mode.Flee);
        int retreats = pack.FindAll(a => a.PackRetreats > 0).Count;
        await Live(pack, 14.0f, _ => Standing(carrier, 1.0f));
        float nearest = 999.0f;
        foreach (WildlifeAnimal animal in pack)
        {
            nearest = Mathf.Min(nearest, Flat(animal.GlobalPosition, carrier));
        }
        int huntedAgain = 0;
        await Live(pack, 10.0f, _ => Standing(carrier, 1.0f), (time, sense) => huntedAgain = Mathf.Max(huntedAgain, CountHunting(pack)));
        Check("one solid blow scares a wolf off; with half the pack hurt the whole pack retreats and stays away",
            oneFled && stillHunting == 3 && allFled && retreats == 4 && nearest > 25.0f && huntedAgain == 0,
            $"struck one fled {oneFled}, the others hunting on {stillHunting}/3, after a kick on a second all fled {allFled} ({retreats} in the retreat), nearest {nearest:0.0} m after 14 s, hunting again {huntedAgain}");
        Free(pack);

        WildlifeAnimal prey = Standalone(wolf, table, Field(-40.0f, 70.0f), 391);
        await Live(new[] { prey }, 0.3f, _ => CarrierSense.Still(carrier + Vector3.Back * 300.0f));
        int hideBefore = CountLoot("leather_patch");
        int meatBefore = CountLoot("raw_meat");
        int blows = 0;
        while (!prey.IsDead && blows < 10)
        {
            prey.TakeHit(30.0f, Vector3.Right * 2.0f, "sword");
            blows++;
        }
        await Live(new[] { prey }, 1.0f, _ => CarrierSense.Still(carrier));
        int hide = CountLoot("leather_patch") - hideBefore;
        int meat = CountLoot("raw_meat") - meatBefore;
        Check("a killed wolf drops hide and raw meat",
            prey.IsDead && hide >= 1 && meat >= 1,
            $"dead after {blows} blows, {hide} hide and {meat} raw meat");
        Free(new[] { prey });
        foreach (Node node in GetTree().CurrentScene.GetChildren())
        {
            if (node.Name.ToString().StartsWith("Loot_", System.StringComparison.Ordinal))
            {
                node.QueueFree();
            }
        }
    }

    /// <summary>A fall near wolves doesn't scatter them like prey: it draws them in, and a downed carrier is bitten sooner.</summary>
    private async Task TestWolfFallDraws(WildlifeTable table, WildlifeSpecies wolf)
    {
        Vector3 carrier = Field(20.0f, -60.0f);
        List<WildlifeAnimal> pack = Pack(wolf, table, Field(20.0f, -75.0f), 3, Vector3.Forward, 401);
        await Live(pack, 0.3f, _ => CarrierSense.Still(carrier + Vector3.Back * 300.0f));
        // What a fall (FleeFrom) and a hard landing (Startle) nearby do to them.
        pack[0].FleeFrom(carrier);
        pack[1].Startle(carrier);
        bool drawn = CountHunting(pack) == pack.Count && pack.TrueForAll(a => a.Flees == 0);
        float firstWindUp = -1.0f;
        await Live(pack, 12.0f, _ => Standing(carrier, 1.0f) with { Downed = true }, (time, sense) =>
        {
            if (firstWindUp < 0.0f && pack.Exists(a => a.State == WildlifeAnimal.Mode.WindUp))
            {
                firstWindUp = time;
            }
        });
        int bites = 0;
        foreach (WildlifeAnimal animal in pack)
        {
            bites += animal.Bites;
        }
        Check("a fall draws wolves in instead of scaring them, and they close in on the downed carrier sooner",
            drawn && firstWindUp > 0.0f && firstWindUp < PredatorTuning.StalkSecondsDay.X && bites >= 1,
            $"all hunting, none fled {drawn}; first wind-up {firstWindUp:0.0} s after the fall (by day a standing carrier waits {PredatorTuning.StalkSecondsDay.X:0}-{PredatorTuning.StalkSecondsDay.Y:0} s), {bites} bites");
        Free(pack);
    }

    /// <summary>
    /// Stalking wolves leave a carrier who reaches a station's fire and keep their distance; a carrier
    /// bleeding out (co-op, waiting for a friend) is circled close but never bitten.
    /// </summary>
    private async Task TestWolfFire(WildlifeTable table, WildlifeSpecies wolf)
    {
        Vector3 carrier = Field(-70.0f, 0.0f);
        List<WildlifeAnimal> pack = Pack(wolf, table, Field(-70.0f, -16.0f), 3, Vector3.Back, 421);
        foreach (WildlifeAnimal animal in pack)
        {
            animal.StartStalk(carrier, false);
        }
        await Live(pack, 3.0f, _ => Standing(carrier, 0.0f, true));
        int bitesBefore = 0;
        int windUpsBefore = 0;
        foreach (WildlifeAnimal animal in pack)
        {
            bitesBefore += animal.Bites;
            windUpsBefore += animal.WindUps;
        }
        float nearestLate = 999.0f;
        await Live(pack, 20.0f, _ => Standing(carrier, 0.0f, true) with { AtFire = true }, (time, sense) =>
        {
            if (time > 12.0f)
            {
                foreach (WildlifeAnimal animal in pack)
                {
                    nearestLate = Mathf.Min(nearestLate, Flat(animal.GlobalPosition, carrier));
                }
            }
        });
        int bites = -bitesBefore;
        int windUps = -windUpsBefore;
        foreach (WildlifeAnimal animal in pack)
        {
            bites += animal.Bites;
            windUps += animal.WindUps;
        }
        Check("wolves leave a carrier at a station's fire and keep their distance",
            bites == 0 && windUps == 0 && CountHunting(pack) == 0 && nearestLate >= PredatorTuning.FireKeepOff - 5.0f,
            $"bites at the fire {bites}, wind-ups {windUps}, hunting {CountHunting(pack)}, nearest {nearestLate:0.0} m from 12 s on (keep off {PredatorTuning.FireKeepOff:0} m)");
        Free(pack);

        List<WildlifeAnimal> circling = Pack(wolf, table, Field(-70.0f, -40.0f), 2, Vector3.Back, 431);
        Vector3 bleeding = Field(-70.0f, -26.0f);
        foreach (WildlifeAnimal animal in circling)
        {
            animal.StartStalk(bleeding, false);
        }
        float closest = 999.0f;
        await Live(circling, 15.0f, _ => Standing(bleeding, 0.0f, true) with { Bleeding = true }, (time, sense) =>
        {
            foreach (WildlifeAnimal animal in circling)
            {
                closest = Mathf.Min(closest, Flat(animal.GlobalPosition, bleeding));
            }
        });
        int bleedBites = circling[0].Bites + circling[1].Bites;
        Check("a carrier bleeding out is circled close but never bitten (room for a friend's help)",
            bleedBites == 0 && closest < PredatorTuning.StalkRingClose && CountHunting(circling) == 2,
            $"bites {bleedBites}, closest {closest:0.0} m, still circling {CountHunting(circling)}/2");
        Free(circling);
    }

    /// <summary>
    /// Co-op: a wolf picks its prey among all carriers (the nearest, a fallen one first, never one at a
    /// fire, sticking with its prey); a guest's puppet shows the wind-up (and the stealth eye reads it).
    /// </summary>
    private async Task TestWolfCoopTarget(WildlifeTable table, WildlifeSpecies wolf)
    {
        Vector3 at = Vector3.Zero;
        WildlifeSpawner.Prey Host(float distance, bool downed = false, bool fire = false) => new(0, new Vector3(distance, 0.0f, 0.0f), downed, false, fire);
        WildlifeSpawner.Prey Guest(float distance, bool downed = false, bool fire = false) => new(StandInPeer, new Vector3(0.0f, 0.0f, distance), downed, false, fire);
        long Pick(long current, params WildlifeSpawner.Prey[] prey) => prey[WildlifeSpawner.PickPrey(at, prey, current)].Peer;
        long nearer = Pick(-1, Host(20.0f), Guest(12.0f));
        long fireGuest = Pick(-1, Host(20.0f), Guest(12.0f, fire: true));
        long fallen = Pick(-1, Host(18.0f, downed: true), Guest(14.0f));
        long loyal = Pick(0, Host(14.0f), Guest(12.0f));
        long switches = Pick(0, Host(20.0f), Guest(10.0f));
        long allAtFire = Pick(-1, Host(20.0f, fire: true), Guest(12.0f, fire: true));
        Check("in co-op a wolf picks its prey among all carriers: the nearest, a fallen one first, never one at a fire, and it sticks with its prey",
            nearer == StandInPeer && fireGuest == 0 && fallen == 0 && loyal == 0 && switches == StandInPeer && allAtFire == StandInPeer,
            $"nearer guest {nearer}, guest at a fire {fireGuest}, fallen host {fallen}, current prey kept {loyal}, clearly closer guest {switches}, all at fires (watches the nearest) {allAtFire}");

        WildlifeAnimal puppet = new() { Puppet = true };
        AddChild(puppet);
        puppet.Setup(wolf, new WildlifeTable(), 1);
        puppet.GlobalPosition = Field(0.0f, 60.0f);
        await WaitSeconds(0.1f);
        List<Node> near = new() { puppet };
        Vector3 carrier = Field(0.0f, 64.0f);
        puppet.NetShow(wolf.IdleClip, 0.0f, 1.0f, 1.0f, false);
        WildlifeAnimal.Awareness calm = StealthEye.Strongest(near, carrier, 35.0f);
        bool calmWindUp = puppet.WindingUp;
        // What a host streams for a stalking wolf: at the stalking pace it lopes in its gallop.
        puppet.NetShow(wolf.FleeClip, 0.0f, PredatorTuning.StalkSpeed / wolf.FleeGroundSpeed, 1.0f, false);
        WildlifeAnimal.Awareness stalking = StealthEye.Strongest(near, carrier, 35.0f);
        puppet.NetShow(wolf.IdleClip, 0.0f, PredatorTuning.WindUpClipSpeed, 1.0f, false);
        WildlifeAnimal.Awareness crouched = StealthEye.Strongest(near, carrier, 35.0f);
        bool windingUp = puppet.WindingUp;
        Check("a guest sees a wolf stalk and wind up (the stealth eye shows it like other animals)",
            calm == WildlifeAnimal.Awareness.Unaware && !calmWindUp && stalking == WildlifeAnimal.Awareness.Alerted
                && crouched == WildlifeAnimal.Awareness.Alerted && windingUp && puppet.PuppetWindUps == 1,
            $"idle {calm} (winding up {calmWindUp}), stalking {stalking}, crouched {crouched} (winding up {windingUp}, {puppet.PuppetWindUps} growl)");
        Free(new[] { puppet });
    }

    private static Vector3 Flat3(Vector3 v)
    {
        return new Vector3(v.X, 0.0f, v.Z);
    }
}
