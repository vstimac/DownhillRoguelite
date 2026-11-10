using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The humanoid enemies (the bog ogre and the pack fiend) on a flat floor with a stand-in carrier:
/// their models follow the animated rig; they wander until the carrier is in sight, then run in; their
/// blows hurt and shove the carrier, unless the shield takes them; they guard or roll aside from a
/// swing they see coming; they reel when hit (their health bar drops) and die at 0 health; the director
/// brings them; the jade urn, used, gives stamina or wakes the jade glutton beside the nearest tree.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/EnemyTests.tscn
/// </summary>
public partial class EnemyTests : Node3D
{
    private readonly List<string> _failures = new();
    private float _damage;
    private int _damageEvents;
    private int _shoves;

    public override async void _Ready()
    {
        StaticBody3D floor = new() { CollisionLayer = 1 };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400, 1, 400) }, Position = Vector3.Down * 0.5f });
        AddChild(floor);
        GameEvents.DamageRequested += OnDamage;
        GameEvents.LaunchRequested += OnLaunch;
        GameEvents.KnockdownRequested += OnKnockdown;

        foreach (string id in new[] { "ogre", "packbeast", "jade_glutton" })
        {
            await Species(GD.Load<EnemySpecies>($"res://wildlife/enemies/{id}.tres"));
        }
        await Director();
        await StuckEnemy();
        await Wake();

        GameEvents.DamageRequested -= OnDamage;
        GameEvents.LaunchRequested -= OnLaunch;
        GameEvents.KnockdownRequested -= OnKnockdown;
        GameEvents.Blocker = null;
        if (_failures.Count == 0)
        {
            GD.Print("ENEMY TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("ENEMY TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    private void OnDamage(float amount, string cause)
    {
        _damage += amount;
        _damageEvents++;
    }

    private void OnLaunch(Vector3 horizontal, float height, string cause) => _shoves++;

    private void OnKnockdown(Vector3 velocity, string cause) => _shoves++;

    private async Task Species(EnemySpecies species)
    {
        string name = species.Id;

        // Far off: it wanders, unaware; its model follows the animated mannequin.
        HumanoidEnemy enemy = Spawn(species, new Vector3(0, 0, -60));
        Vector3 carrier = Vector3.Zero;
        await Track(enemy, carrier, 60);
        Check($"{name}: the model follows the animated rig", enemy.Follower != null && enemy.Follower.Followed >= 40 && enemy.Animations != null && enemy.Animations.HasAnimation("Punch_Jab"),
            $"{enemy.Follower?.Followed} bones followed");
        Check($"{name}: out of sight it wanders, unaware", !enemy.Aware && enemy.State == HumanoidEnemy.Phase.Wander, $"{enemy.State}, aware {enemy.Aware}");

        // In sight: it runs in.
        enemy.GlobalPosition = new Vector3(0, 0.1f, -16);
        float before = enemy.GlobalPosition.DistanceTo(carrier);
        float fastest = 0.0f;
        for (int i = 0; i < 60; i++)
        {
            enemy.Track(carrier);
            await Frames(1);
            Vector3 v = enemy.GetRealVelocity();
            fastest = Mathf.Max(fastest, new Vector2(v.X, v.Z).Length());
        }
        float after = enemy.GlobalPosition.DistanceTo(carrier);
        Check($"{name}: once it sees the carrier it runs in", enemy.Aware && after < before - 2.0f && fastest > species.WalkSpeed + 0.5f && (enemy.Clip == "Jog_Fwd" || enemy.Clip == "Sprint"),
            $"{before:0.0} m → {after:0.0} m, fastest {fastest:0.0} m/s, clip {enemy.Clip}");

        // Its blows land: health and a shove.
        _damage = 0.0f;
        _damageEvents = 0;
        _shoves = 0;
        GameEvents.Blocker = null;
        await Track(enemy, carrier, 360, () => enemy.HitsLanded > 0);
        Check($"{name}: its blow hurts and shoves the carrier", enemy.HitsLanded > 0 && _damage > 0.0f && _shoves > 0,
            $"{enemy.HitsLanded} blows, {_damage:0} damage in {_damageEvents} events, {_shoves} shoves");

        // The shield stops a blow: no damage, it reels back.
        GameEvents.Blocker = (_, _) => true;
        _damageEvents = 0;
        int blocked = enemy.HitsBlocked;
        await Track(enemy, carrier, 420, () => enemy.HitsBlocked > blocked);
        Check($"{name}: the carrier's shield stops its blow", enemy.HitsBlocked > blocked && _damageEvents == 0 && enemy.State == HumanoidEnemy.Phase.Stagger,
            $"blocked {enemy.HitsBlocked - blocked}, damage events {_damageEvents}, now {enemy.State}");
        GameEvents.Blocker = null;
        enemy.QueueFree();

        // It sees a swing coming and guards: a blow from the front does almost nothing.
        EnemySpecies guarding = (EnemySpecies)species.Duplicate();
        guarding.GuardChance = 1.0f;
        guarding.DodgeChance = 0.0f;
        guarding.Attacks = System.Array.Empty<string>();
        HumanoidEnemy guard = Spawn(guarding, new Vector3(0, 0, -1.4f));
        await Track(guard, carrier, 30);
        GameEvents.PublishMeleeSwung(carrier, Vector3.Forward, "swing");
        await Track(guard, carrier, 2);
        float health = guard.Health;
        guard.TakeHit(20.0f, Vector3.Forward * 3.0f, "sword");
        Check($"{name}: it raises its guard against a swing and takes little", guard.State == HumanoidEnemy.Phase.Guard && guard.Guarded == 1 && health - guard.Health <= 20.0f * guarding.GuardedDamage + 0.01f,
            $"{guard.State}, guarded {guard.Guarded}, lost {health - guard.Health:0.0}");
        // Unguarded, a blow wounds it and it reels.
        await Track(guard, carrier, 120, () => guard.State != HumanoidEnemy.Phase.Guard);
        health = guard.Health;
        guard.TakeHit(20.0f, Vector3.Forward * 3.0f, "sword");
        Check($"{name}: an unguarded blow wounds it and it reels", guard.State == HumanoidEnemy.Phase.Stagger && Mathf.IsEqualApprox(health - guard.Health, 20.0f),
            $"{guard.State}, lost {health - guard.Health:0.0}");
        Check($"{name}: its health bar shows what's left", guard.Bar != null && Mathf.IsEqualApprox(guard.Bar.Fraction, guard.Health / guard.MaxHealth) && guard.Bar.Fraction < 1.0f,
            $"bar {guard.Bar?.Fraction:0.00}, health {guard.Health:0}/{guard.MaxHealth:0}");
        guard.QueueFree();

        // Or it rolls aside, out of the blow's way.
        EnemySpecies rolling = (EnemySpecies)species.Duplicate();
        rolling.GuardChance = 0.0f;
        rolling.DodgeChance = 1.0f;
        rolling.Attacks = System.Array.Empty<string>();
        HumanoidEnemy roller = Spawn(rolling, new Vector3(0, 0, -1.4f));
        await Track(roller, carrier, 30);
        Vector3 start = roller.GlobalPosition;
        GameEvents.PublishMeleeSwung(carrier, Vector3.Forward, "swing");
        await Track(roller, carrier, 2);
        bool untouchable = !roller.CanBeHit;
        await Track(roller, carrier, 60);
        Check($"{name}: it rolls aside from a swing", roller.Dodges == 1 && untouchable && roller.GlobalPosition.DistanceTo(start) > 0.8f,
            $"dodges {roller.Dodges}, untouchable while rolling {untouchable}, moved {roller.GlobalPosition.DistanceTo(start):0.0} m");
        roller.QueueFree();

        // At 0 health it dies, can't be hit, and is gone after a while.
        EnemySpecies dying = (EnemySpecies)species.Duplicate();
        dying.CorpseSeconds = 0.5f;
        HumanoidEnemy corpse = Spawn(dying, new Vector3(0, 0, -3));
        await Track(corpse, carrier, 10);
        corpse.TakeHit(10000.0f, Vector3.Forward * 3.0f, "sword");
        bool dead = corpse.State == HumanoidEnemy.Phase.Dead && !corpse.CanBeHit && !corpse.IsInGroup(IMeleeTarget.Group) && corpse.Clip == "Death01";
        await Track(corpse, carrier, 200);
        Check($"{name}: at 0 health it dies and is later gone", dead && !IsInstanceValid(corpse), $"dead {dead}, gone {!IsInstanceValid(corpse)}");

        // It falls as a ragdoll: its parts drop under the chest and are carried the way the blow went.
        EnemySpecies falling = (EnemySpecies)species.Duplicate();
        falling.CorpseSeconds = 30.0f;
        HumanoidEnemy body = Spawn(falling, new Vector3(2.5f, 0, -3));
        await Track(body, carrier, 10);
        float standingTop = body.GlobalPosition.Y + falling.Height * 0.6f;
        body.TakeHit(10000.0f, Vector3.Forward * 4.0f, "sword");
        int parts = body.Ragdoll?.Parts ?? 0;
        Vector3 corePosition = body.Ragdoll?.Core?.GlobalPosition ?? Vector3.Zero;
        await Track(body, carrier, 150);
        Vector3 coreNow = body.Ragdoll?.Core?.GlobalPosition ?? corePosition;
        bool finite = coreNow.IsFinite();
        Check($"{name}: it falls as a ragdoll, carried by the blow", parts >= 5 && finite && coreNow.Y < standingTop && (coreNow - corePosition).Dot(Vector3.Forward) > 0.1f,
            $"{parts} parts, core from y {corePosition.Y:0.00} to {coreNow.Y:0.00} (standing chest {standingTop:0.00}), {(coreNow - corePosition).Dot(Vector3.Forward):0.00} m along the blow");
        // The kill pays: two to five things land on the ground around the body, ready to pick up.
        int landed = 0;
        List<string> loot = new();
        foreach (Inventory.WorldItem item in body.Dropped)
        {
            if (IsInstanceValid(item) && item.GlobalPosition.DistanceTo(body.GlobalPosition) < 3.0f && item.Definition != null)
            {
                landed++;
                loot.Add(item.Definition.ItemId);
            }
        }
        Check($"{name}: the kill leaves loot on the ground", landed >= 2 && landed <= 5, $"{landed} items: [{string.Join(", ", loot)}]");
        foreach (Inventory.WorldItem item in body.Dropped)
        {
            if (IsInstanceValid(item))
            {
                item.QueueFree();
            }
        }
        body.QueueFree();
        await Frames(2);
    }

    private async Task Director()
    {
        EnemyDirector director = new()
        {
            Species = new[] { GD.Load<EnemySpecies>("res://wildlife/enemies/ogre.tres") },
            Arrivals = false,
            CircleOverride = 4,
        };
        AddChild(director);
        await Frames(2);
        HumanoidEnemy brought = director.Bring(director.Species[0], new Vector3(5, 0, -10));
        await Frames(10);
        Check("the director brings enemies, more and tougher deeper down", director.Enemies.Count == 1 && director.MostEnemies() == 3 && Mathf.IsEqualApprox(brought.Health, 160.0f * 1.4f),
            $"{director.Enemies.Count} brought, at most {director.MostEnemies()} in circle 4, health {brought.Health:0}");
        director.QueueFree();
        await Frames(2);
    }

    /// <summary>An enemy wedged against a wall (wanting to run at the carrier but going nowhere) is put somewhere open.</summary>
    private async Task StuckEnemy()
    {
        StaticBody3D wall = new() { Name = "Wall", CollisionLayer = 1 };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(30.0f, 4.0f, 1.0f) }, Position = new Vector3(0.0f, 2.0f, -6.0f) });
        AddChild(wall);
        EnemyDirector director = new()
        {
            Species = new[] { GD.Load<EnemySpecies>("res://wildlife/enemies/ogre.tres") },
            Arrivals = false,
            CircleOverride = 0,
        };
        AddChild(director);
        await Frames(2);
        GameEvents.PublishLocomotion(default(LocomotionSnapshot));
        HumanoidEnemy enemy = director.Bring(director.Species[0], new Vector3(0.0f, 0.0f, -7.0f));
        await Track(enemy, Vector3.Zero, 60 * 8, () => enemy.Unstuck > 0);
        float behindWall = enemy.GlobalPosition.Z;
        Check("an enemy stuck on a wall comes back from somewhere open", IsInstanceValid(enemy) && enemy.Unstuck > 0 && enemy.Health > 0.0f,
            $"moved {enemy.Unstuck} times, now at z {behindWall:0.0}");
        director.QueueFree();
        wall.QueueFree();
        await Frames(2);
    }

    /// <summary>The jade urn: used, it gives stamina or (by chance) wakes the glutton by the nearest tree, which comes at once.</summary>
    private async Task Wake()
    {
        Inventory.ItemDefinition urn = GD.Load<Inventory.ItemDefinition>("res://inventory/items/jade_urn.tres");
        Check("the jade urn gives stamina, or one time in five wakes the glutton", urn.UsableAlone && urn.UseStamina > 0.0f
            && urn.Wakes(0.1f) && !urn.Wakes(0.5f) && Mathf.IsEqualApprox(urn.UseWakeChance, 0.2f) && urn.UseWakes == "jade_glutton", "");

        Vector3 near = new(6.0f, 0.0f, -6.0f);
        StaticBody3D trees = new() { Name = "TreeTrunks" };
        trees.AddToGroup("tree_trunks");
        trees.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.3f, Height = 3.0f }, Position = near + Vector3.Up * 1.5f });
        trees.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.3f, Height = 3.0f }, Position = new Vector3(-20.0f, 1.5f, -20.0f) });
        AddChild(trees);
        EnemyDirector director = new()
        {
            Wakeable = new[] { GD.Load<EnemySpecies>("res://wildlife/enemies/jade_glutton.tres") },
            Arrivals = false,
            CircleOverride = 0,
        };
        AddChild(director);
        await Frames(2);
        GameEvents.PublishLocomotion(default(LocomotionSnapshot));
        GameEvents.RequestWake("jade_glutton", "Grinning jade urn");
        await Frames(2);
        HumanoidEnemy? woken = null;
        if (director.Enemies.Count == 1)
        {
            woken = director.Enemies[0];
        }
        float fromTree = 99.0f;
        float fromCarrier = 0.0f;
        if (woken != null)
        {
            fromTree = new Vector2(woken.GlobalPosition.X - near.X, woken.GlobalPosition.Z - near.Z).Length();
            fromCarrier = new Vector2(woken.GlobalPosition.X, woken.GlobalPosition.Z).Length();
        }
        Check("used, it wakes the glutton beside the nearest tree, and it comes at once", woken != null && fromTree < 1.5f && fromCarrier > near.Length()
            && woken.Aware && woken.State == HumanoidEnemy.Phase.Chase,
            $"{director.Enemies.Count} woken, {fromTree:0.0} m from the tree, {fromCarrier:0.0} m from the carrier, aware {woken?.Aware}, {woken?.State}");
        GameEvents.RequestWake("ogre", "nothing");
        await Frames(2);
        Check("only the kinds that can be woken wake", director.Enemies.Count == 1, $"{director.Enemies.Count}");
        director.QueueFree();
        trees.QueueFree();
        await Frames(2);
    }

    private HumanoidEnemy Spawn(EnemySpecies species, Vector3 at)
    {
        HumanoidEnemy enemy = HumanoidEnemy.Create(species);
        AddChild(enemy);
        enemy.GlobalPosition = at + Vector3.Up * 0.1f;
        Vector3 look = -at;
        enemy.Rotation = new Vector3(0.0f, Mathf.Atan2(-look.X, -look.Z), 0.0f);
        return enemy;
    }

    /// <summary>Runs frames telling the enemy where the carrier is, until <paramref name="until"/> or the count.</summary>
    private async Task Track(HumanoidEnemy enemy, Vector3 carrier, int frames, System.Func<bool>? until = null)
    {
        for (int i = 0; i < frames; i++)
        {
            if (!IsInstanceValid(enemy))
            {
                return;
            }
            enemy.Track(carrier);
            await Frames(1);
            if (until != null && IsInstanceValid(enemy) && until())
            {
                return;
            }
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
