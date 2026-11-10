using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The newspaper seller on a flat floor with a stand-in carrier: her model follows the animated rig;
/// she waits until E, then walks (and jogs) after the carrier and stops near them; with nothing to give
/// she keeps following, and a water bottle from the real inventory stops her; struck by the sword, she
/// calls two enemies onto the carrier through the enemy director and runs off.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/NewspaperSellerTests.tscn
/// </summary>
public partial class NewspaperSellerTests : Node3D
{
    private readonly List<string> _failures = new();
    private readonly List<string> _moods = new();
    private Vector3 _carrier;

    public override async void _Ready()
    {
        StaticBody3D floor = new() { CollisionLayer = 1 };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400, 1, 400) }, Position = Vector3.Down * 0.5f });
        AddChild(floor);
        GameEvents.BeggarChanged += OnMood;
        CarrierInventory inventory = new()
        {
            Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"),
            StartingItemPaths = System.Array.Empty<string>(),
            AutoPackOnStart = false,
        };
        AddChild(inventory);
        await Frames(2);

        await FollowAndGive(inventory);
        await Struck();

        GameEvents.BeggarChanged -= OnMood;
        if (_failures.Count == 0)
        {
            GD.Print("NEWSPAPER SELLER TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("NEWSPAPER SELLER TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    private void OnMood(string who, string mood) => _moods.Add(mood);

    private NewspaperSeller Spawn(Vector3 at)
    {
        NewspaperSeller seller = new() { PlaceOnRoute = false, BegInterval = 1000.0f };
        AddChild(seller);
        seller.GlobalPosition = at;
        return seller;
    }

    private async Task FollowAndGive(CarrierInventory inventory)
    {
        _carrier = new Vector3(0, 0, 6);
        NewspaperSeller seller = Spawn(Vector3.Zero);
        await Hold(30);
        Check("her model follows the animated rig", seller.Follower != null && seller.Follower.Followed >= 40 && seller.Animations != null && seller.Animations.HasAnimation("Walk"),
            $"{seller.Follower?.Followed} bones followed");
        Node3D? papers = seller.FindChild("Newspapers", true, false) as Node3D;
        Vector3 held = Vector3.Zero;
        if (papers != null)
        {
            held = papers.GlobalPosition - seller.GlobalPosition;
        }
        Check("she holds a bundle of newspapers at hand height", papers != null && held.Y > 0.5f && held.Y < 1.3f && new Vector2(held.X, held.Z).Length() < 0.6f, $"bundle at {held}");
        Check("before E she stays where she stands", seller.State == NewspaperSeller.Mood.Selling && seller.GlobalPosition.Length() < 0.05f && seller.Prompt.StartsWith("E  Buy"),
            $"{seller.State} at {seller.GlobalPosition}, prompt \"{seller.Prompt}\"");

        bool started = seller.Interact(this);
        Check("E starts her begging and following", started && seller.Following && _moods.Contains("begging"), $"{seller.State}");

        // The carrier walks off down the slope: she comes after them.
        _carrier = new Vector3(0, 0, 20);
        string walking = "";
        for (int i = 0; i < 60 * 8; i++)
        {
            await Hold(1);
            if (seller.Speed > 0.5f && walking.Length == 0)
            {
                walking = seller.Clip;
            }
        }
        float gap = Flat(seller.GlobalPosition, _carrier);
        Check("she walks after the carrier", walking.StartsWith("Walk") || walking.StartsWith("Jog"), $"clip {walking}");
        Check("she stops close to them, begging", gap < seller.FollowDistance + 0.3f && gap > seller.FollowDistance - 0.3f && seller.Clip.StartsWith("Idle_Talking"),
            $"{gap:0.00} m away, clip {seller.Clip}");

        // Left far behind, she turns up behind the carrier.
        _carrier = new Vector3(0, 0, 120);
        await Hold(3);
        gap = Flat(seller.GlobalPosition, _carrier);
        Check("left far behind, she catches up", gap <= seller.CatchUpBehind + 0.5f && seller.GlobalPosition.Z < _carrier.Z, $"{gap:0.0} m behind");

        // Nothing she wants: she keeps following.
        bool gaveNothing = seller.Give();
        Check("with nothing to give she keeps following", !gaveNothing && seller.Following && seller.Prompt.StartsWith("She wants"), $"prompt \"{seller.Prompt}\"");

        // A water bottle (and a pencil she doesn't want) in the pack: E gives her the bottle.
        inventory.TryAdd(GD.Load<ItemDefinition>("res://inventory/items/pencil.tres"), -1.0f, false);
        inventory.TryAdd(GD.Load<ItemDefinition>("res://inventory/items/water_bottle.tres"), -1.0f, false);
        await Hold(2);
        string prompt = seller.Prompt;
        bool gave = seller.Interact(this);
        await Hold(2);
        Check("E with a water bottle gives it to her", gave && prompt == "E  Give her your water bottle" && seller.Received == "water_bottle" && !GameEvents.CarriedItems.Contains("water_bottle") && GameEvents.CarriedItems.Contains("pencil"),
            $"prompt \"{prompt}\", received {seller.Received}, carried [{string.Join(", ", GameEvents.CarriedItems)}]");

        Vector3 stopped = seller.GlobalPosition;
        _carrier = new Vector3(0, 0, 150);
        await Hold(120);
        Check("given water, she stops following", seller.State == NewspaperSeller.Mood.Given && !seller.Following && Flat(seller.GlobalPosition, stopped) < 0.05f && _moods.Contains("given"),
            $"{seller.State}, moved {Flat(seller.GlobalPosition, stopped):0.00} m");
        seller.QueueFree();
        await Hold(2);
    }

    private async Task Struck()
    {
        EnemyDirector director = new()
        {
            Species = new[] { GD.Load<EnemySpecies>("res://wildlife/enemies/ogre.tres"), GD.Load<EnemySpecies>("res://wildlife/enemies/packbeast.tres") },
            Arrivals = false,
            CircleOverride = 0,
        };
        AddChild(director);
        _carrier = new Vector3(0, 0, 3);
        NewspaperSeller seller = Spawn(Vector3.Zero);
        seller.FleeSeconds = 2.0f;
        await Hold(10);
        seller.Interact(this);
        await Hold(30);
        Check("she can be struck (a sword target)", seller.IsInGroup(IMeleeTarget.Group) && seller.CanBeHit, $"{seller.State}");

        seller.TakeHit(20.0f, new Vector3(0, 0, -3), "sword");
        await Hold(5);
        int aware = 0;
        foreach (HumanoidEnemy enemy in director.Enemies)
        {
            if (IsInstanceValid(enemy) && enemy.Aware && enemy.GlobalPosition.DistanceTo(seller.GlobalPosition) < director.AmbushRadius + 2.0f)
            {
                aware++;
            }
        }
        Check("striking her brings two enemies, set on the carrier", director.Enemies.Count == 2 && aware == 2 && _moods.Contains("struck"),
            $"{director.Enemies.Count} brought, {aware} near her and aware");
        seller.TakeHit(20.0f, Vector3.Zero, "sword");
        await Hold(2);
        Check("a second blow brings no more", director.Enemies.Count == 2 && !seller.CanBeHit && !seller.IsInGroup(IInteractable.Group), $"{director.Enemies.Count} enemies");

        Vector3 hit = seller.GlobalPosition;
        await Hold(60 * 3);
        Check("she runs off and is gone", !seller.Visible && Flat(seller.GlobalPosition, _carrier) > Flat(hit, _carrier) + 1.0f, $"visible {seller.Visible}");
        director.QueueFree();
        seller.QueueFree();
        await Hold(2);
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Waits physics frames, telling everyone where the stand-in carrier stands each frame.</summary>
    private async Task Hold(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            GameEvents.PublishLocomotion(default(LocomotionSnapshot) with { Position = _carrier, Forward = Vector3.Forward });
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
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
