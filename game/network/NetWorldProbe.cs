using System;
using System.Collections.Generic;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>Opt-in, isolated-process consistency scenario used by tools/coop_world_test.sh.</summary>
public partial class NetWorldProbe : Node
{
    public NetSession? Session { get; set; }
    public double StartAtUnix { get; set; }
    private readonly HashSet<int> _done = new();
    private readonly List<string> _got = new();
    private HumanoidEnemy? _enemy;
    private bool _npcDenied;
    private bool _sawFall;
    private bool _reported;
    private int _errors;
    public override void _Ready() { GameEvents.SupplyPickedUp += Picked; }
    public override void _ExitTree() { GameEvents.SupplyPickedUp -= Picked; }
    private void Picked(string key) { _got.Add(key); }
    private T? Find<T>() where T : Node
    { return GetTree().CurrentScene.FindChildren("*", "", true, false).OfType<T>().FirstOrDefault(); }
    private bool Once(int step, double time, double now) => now >= time && _done.Add(step);
    private void Check(string name, bool ok)
    {
        string result = "PASS";
        if (!ok) { _errors++; result = "FAIL"; }
        GD.Print($"[WorldProbe] {result} {name}");
    }
    public override void _Process(double delta)
    {
        if (Session?.Joined != true) { return; }
        double time = Time.GetUnixTimeFromSystem() - StartAtUnix;
        PlayerController? player = Session.LocalPlayer();
        RunState run = GetNode<RunState>("/root/RunState");
        if (player == null || run.Route == null || time < 0) { return; }
        bool host = Session.Mode == NetSession.Role.Host;
        bool early = Session.LocalName != "WorldLate";
        foreach (RemoteCarrier remote in Session.Remotes.Values) { _sawFall |= remote.Pose?.PhysicalFall == true; }
        if (Once(0, 0, time))
        {
            player.DirectionOverride = Vector3.Zero; player.Health?.Refill();
            player.GetNodeOrNull<CarrierInventory>("Inventory")?.Clear();
            player.Teleport(run.Route.Station(1) + new Vector3(-13, 0.1f, 1.3f), 0);
            if (Find<EnemyDirector>() is EnemyDirector enemies) { enemies.Arrivals = false; }
            if (Find<BeastDirector>() is BeastDirector beasts) { beasts.SetPhysicsProcess(false); beasts.Clear(); }
            if (Find<WildlifeSpawner>() is WildlifeSpawner animals) { animals.SetPhysicsProcess(false); animals.Clear(); }
            Find<StationVisitors>()?.SeatAhead(1);
            Find<PresidentDirector>()?.Seat(1);
            if (Find<BoonOfferings>() is BoonOfferings boons) { boons.RequireCompletedWork = false; boons.Offer(1); }
            Find<ContractDirector>()?.Offer(1);
        }
        if (host && Once(1, 4, time))
        {
            Vector3 at = player.GlobalPosition;
            EnemyDirector enemies = Find<EnemyDirector>()!;
            _enemy = enemies.Bring(enemies.Species[0], at + Vector3.Right * 5);
            _enemy.SetPhysicsProcess(false);
            NightBeast beast = new();
            BeastRoster roster = GD.Load<BeastRoster>("res://wildlife/default_beasts.tres");
            beast.Setup(roster.Beasts[0], 1, 0, 0); GetTree().CurrentScene.AddChild(beast); beast.GlobalPosition = at + Vector3.Right * 7;
            WildlifeTable table = GD.Load<WildlifeTable>("res://wildlife/default_wildlife.tres");
            WildlifeAnimal animal = new(); animal.Setup(table.Species[0], table, 1);
            GetTree().CurrentScene.AddChild(animal); animal.GlobalPosition = at + Vector3.Right * 9;
            TuntunSahur tuntун = new() { LifeSeconds = 100 }; GetTree().CurrentScene.AddChild(tuntун);
            tuntун.GlobalPosition = at + Vector3.Right * 11; tuntун.SetPhysicsProcess(false);
            ItemDefinition definition = ItemText.AtPath("res://inventory/items/ore_sample.tres")!;
            WorldItem loot = new() { Definition = definition }; GetTree().CurrentScene.AddChild(loot); loot.GlobalPosition = at + Vector3.Right;
            GameEvents.PublishLootDropped(loot, definition.ItemId, loot.GlobalPosition);
        }
        if (early && Once(2, 7, time))
        {
            BoonStone? stone = Find<BoonOfferings>()?.Stones.FirstOrDefault(s => s.Station == 1);
            stone?.Interact(player);
        }
        if (!host && early && Once(3, 9, time))
        {
            Find<PresidentDirector>()?.Npcs[1].Interact(player);
        }
        if (host && Once(4, 10, time))
        {
            PresidentNpc npc = Find<PresidentDirector>()!.Npcs[1];
            _npcDenied = !npc.Interact(player);
        }
        if (early && Once(5, 12, time))
        {
            // This is a real keyed pickup, with the same world identity on every peer.
            ItemDefinition definition = ItemText.AtPath("res://inventory/items/ore_sample.tres")!;
            WorldItem relic = new() { Definition = definition, SlotKey = "relic:coop-proof" };
            GetTree().CurrentScene.AddChild(relic); relic.GlobalPosition = player.GlobalPosition;
            relic.Interact(player);
        }
        if (early && Once(6, 14, time)) { Find<ContractDirector>()?.Boards[1].Interact(player); }
        if (!host && early && Once(7, 16, time))
        {
            PlayerRagdoll fall = player.GetNode<PlayerRagdoll>("Ragdoll");
            fall.SafeImpactSpeed = 100; fall.GoDown(Vector3.Forward * 2, Vector3.Zero);
        }
        if (host && Once(8, 18, time)) { _enemy?.TakeHit(10000, Vector3.Zero, "sword"); }
        if (Once(9, 28, time))
        {
            // Host reservation history must also prevent streamed/re-created pickups.
            WorldItem resurrected = new() { Definition = ItemText.AtPath("res://inventory/items/ore_sample.tres"), SlotKey = "relic:coop-proof" };
            GetTree().CurrentScene.AddChild(resurrected);
        }
        if (Once(10, 25, time))
        {
            string line = Find<PresidentDirector>()?.Npcs[1].Voice?.CurrentLine ?? "";
            GD.Print($"[WorldProbe] npc_line={line}");
        }
        if (!_reported && time >= 32)
        {
            _reported = true;
            Check("joined", Session.Joined && Session.Peers.Count == 2);
            Check("all remote poses arrive", Session.Remotes.Values.All(r => r.Pose?.Received > 0));
            Check("one simulation authority", host || Session.Mobs?.OwnedCount == 0);
            Check("four shared actor kinds", Session.Mobs?.StateDigest().Split('|').Length == 4);
            Check("boon reservation persists", Session.TakenSupplies.Contains("boon:1") && Find<BoonOfferings>()!.Stones.Count == 0);
            Check("relic reservation persists", Session.TakenSupplies.Contains("relic:coop-proof") && !GetTree().GetNodesInGroup(IInteractable.Group).OfType<WorldItem>().Any(i => i.SlotKey == "relic:coop-proof"));
            Check("contract reservation persists", Session.TakenSupplies.Contains("contract:1"));
            Check("delivery table shared with everyone", GetTree().CurrentScene.FindChildren("*", "", true, false).OfType<DeliveryPost>().Count() == 1);
            WorldItem[] loot = GetTree().GetNodesInGroup(IInteractable.Group).OfType<WorldItem>().Where(i => i.SlotKey.StartsWith("L", StringComparison.Ordinal)).ToArray();
            Check("unclaimed loot survives late joining", loot.Count(i => i.SlotKey == "L1.1") == 1 && loot.Select(i => i.SlotKey).Distinct().Count() == loot.Length);
            if (host) { Check("second NPC participant refused", _npcDenied); Check("actual remote fall observed", _sawFall); }
            else { Check("NPC presentation arrives", Session.WorldActors?.Received > 0); }
            string taken = string.Join(",", Session.TakenSupplies.OrderBy(x => x, StringComparer.Ordinal));
            GD.Print($"[WorldProbe] digest={Session.Mobs?.StateDigest()} taken={taken}");
            GD.Print($"[WorldProbe] result name={Session.LocalName} failures={_errors} got={string.Join(',', _got)}");
        }
        if (_reported && time >= 35) { GetTree().Quit(Math.Min(_errors, 1)); }
    }
}
