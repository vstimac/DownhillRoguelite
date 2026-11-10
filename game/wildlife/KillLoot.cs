using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// The reward for a kill: a few items, by id, picked at random from a pool (now and then one from a
/// rarer pool too), tossed out of the body onto the ground around it as WorldItems the carrier picks
/// up with E like any other.
/// </summary>
[GlobalClass]
public partial class KillLoot : Resource
{
    /// <summary>Common finds (item ids from inventory/items); repeats are allowed.</summary>
    [Export] public string[] Common { get; set; } =
    {
        "ration", "dried_fruit", "water_flask", "med_kit", "leather_patch", "cord", "rope",
        "fuel_canister", "mushrooms", "sewing_kit", "wild_berries", "ore_sample", "torch", "torch",
    };
    /// <summary>Better finds: clothes, a seal, a relic.</summary>
    [Export] public string[] Rare { get; set; } =
    {
        "leather_gloves", "wool_hat", "hobnail_boots", "ranger_hood", "ranger_jerkin", "oilskin_coat",
        "seal_of_trust", "relic_counterweight", "carved_stone",
    };
    /// <summary>How many common finds (random in the range, both ends included).</summary>
    [Export] public Vector2I CommonCount { get; set; } = new(2, 4);
    /// <summary>The chance of one rare find besides them.</summary>
    [Export(PropertyHint.Range, "0,1")] public float RareChance { get; set; } = 0.35f;
    /// <summary>They land this far from the body at most, in m, and fly this high on the way.</summary>
    [Export] public float Spread { get; set; } = 1.4f;
    [Export] public float TossHeight { get; set; } = 0.8f;
    [Export] public float TossSeconds { get; set; } = 0.45f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    /// <summary>Throws the loot out of a body at <paramref name="from"/> (its feet); returns the items.</summary>
    public List<WorldItem> Scatter(Node3D body, Vector3 from, string killed)
    {
        List<WorldItem> dropped = new();
        if (!body.IsInsideTree())
        {
            return dropped;
        }
        List<string> ids = new();
        int count = GD.RandRange(CommonCount.X, Mathf.Max(CommonCount.X, CommonCount.Y));
        for (int i = 0; i < count && Common.Length > 0; i++)
        {
            ids.Add(Common[GD.RandRange(0, Common.Length - 1)]);
        }
        if (Rare.Length > 0 && GD.Randf() < RareChance)
        {
            ids.Add(Rare[GD.RandRange(0, Rare.Length - 1)]);
        }
        Node parent = body.GetTree().CurrentScene ?? body.GetTree().Root;
        PhysicsDirectSpaceState3D space = body.GetWorld3D().DirectSpaceState;
        List<string> names = new();
        for (int i = 0; i < ids.Count; i++)
        {
            ItemDefinition? item = ItemText.Find(ids[i]);
            if (item == null)
            {
                continue;
            }
            // Around the body in a ring, each a little further round than the last.
            float angle = Mathf.Tau * (i + GD.Randf() * 0.5f) / Mathf.Max(ids.Count, 1);
            float reach = Spread * (0.5f + GD.Randf() * 0.5f);
            Vector3 at = from + new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * reach;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 2.0f, at + Vector3.Down * 4.0f, GroundMask));
            if (hit.Count > 0)
            {
                at = (Vector3)hit["position"];
            }
            WorldItem loot = new() { Definition = item, Name = $"Loot_{item.ItemId}" };
            parent.AddChild(loot);
            Vector3 start = from + Vector3.Up * 1.0f;
            Vector3 landing = at;
            float height = TossHeight;
            loot.GlobalPosition = start;
            Tween toss = loot.CreateTween();
            toss.TweenMethod(Callable.From<float>(t =>
            {
                if (GodotObject.IsInstanceValid(loot))
                {
                    loot.GlobalPosition = start.Lerp(landing, t) + Vector3.Up * (4.0f * height * t * (1.0f - t));
                }
            }), 0.0f, 1.0f, TossSeconds);
            dropped.Add(loot);
            names.Add(item.DisplayName);
            GameEvents.PublishLootDropped(loot, item.ItemId, landing);
        }
        if (names.Count > 0)
        {
            GameEvents.PublishHint($"The {killed} drops {string.Join(", ", names)}.");
        }
        return dropped;
    }
}
