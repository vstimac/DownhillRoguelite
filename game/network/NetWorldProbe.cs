using System;
using System.Collections.Generic;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Map;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Opt-in, isolated-process consistency scenario used by tools/coop_world_test.sh. Besides actors,
/// NPCs and pickups it checks what each player sees of the others and of the world: the host and
/// the guest each fall and get up (backwards and forwards) and the other sees the fall and the
/// get-up; everyone early uses a med kit; the host sets its pack down to repack it (the world keeps
/// going, and the guest and a late joiner see the pack standing on the ground, then shouldered again);
/// the guest crouches with a loaded pack and swings its sword, and the host's animals learn of all three;
/// and every machine prints one digest of the set pieces, rocks, bushes, grass carpet and route signs
/// near station 1, which must be the same everywhere.
/// </summary>
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
    /// <summary>What this machine saw of each other player's falls: fallen, back up, and how high their hips came after.</summary>
    private sealed class FallWatch { public bool Fell; public bool Up; public float Tallest; }
    private readonly Dictionary<long, FallWatch> _falls = new();
    private bool _selfFell;
    private bool _selfUp;
    private float _healBefore = -1.0f;
    private float _healAfter = -1.0f;
    private bool _kitGone;
    private bool _packOpened;
    private bool _runningWhilePacking;
    private bool _ownPackOff;
    private bool _sawHostPackOff;
    private bool _hostPackOnGround;
    private bool _otherPackMoved;
    private int _hostPosesBefore = -1;
    private int _hostPosesDuring = -1;
    private double _joinedAt = -1.0;
    private double _posesFrom;
    private bool _guestCrouchSeen;
    private float _guestLoadSeen = -1.0f;
    private int _swingsHeard;
    private float _ownLoad;
    private bool _swung;
    public override void _Ready()
    {
        GameEvents.SupplyPickedUp += Picked; GameEvents.SharedCarrierSwung += HeardSwing; GameEvents.LoadChanged += OwnLoad;
    }
    public override void _ExitTree()
    {
        GameEvents.SupplyPickedUp -= Picked; GameEvents.SharedCarrierSwung -= HeardSwing; GameEvents.LoadChanged -= OwnLoad;
    }
    private void HeardSwing(Vector3 at) { _swingsHeard++; }
    private void OwnLoad(LoadSnapshot load) { _ownLoad = load.Mass; }
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
        if (_joinedAt < 0.0) { _joinedAt = time; }
        Watch(host);
        PlayerRagdoll? ownRagdoll = player.GetNodeOrNull<PlayerRagdoll>("Ragdoll");
        if (ownRagdoll != null) { _selfFell |= ownRagdoll.Downed; _selfUp |= _selfFell && !ownRagdoll.Downed; }
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
        if (host && Once(11, 12, time))
        {
            // The host falls backwards (the guest falls forwards at 16), so both get-ups are seen.
            PlayerRagdoll fall = player.GetNode<PlayerRagdoll>("Ragdoll");
            fall.SafeImpactSpeed = 100; fall.GoDown(Vector3.Back * 2, Vector3.Zero);
        }
        if (early && Once(12, 20, time))
        {
            // Medicine: hurt, with a med kit in the pack...
            ItemDefinition kit = ItemText.AtPath("res://inventory/items/med_kit.tres")!;
            CarrierInventory? inventory = player.GetNodeOrNull<CarrierInventory>("Inventory");
            if (player.Health != null && inventory != null && inventory.TryAdd(kit)) { player.Health.TakeDamage(50.0f, "probe"); }
        }
        if (early && Once(18, 21, time))
        {
            // ...a moment later (once the hurt is known, as a player would press V) it gives its health back.
            ItemDefinition kit = ItemText.AtPath("res://inventory/items/med_kit.tres")!;
            CarrierInventory? inventory = player.GetNodeOrNull<CarrierInventory>("Inventory");
            if (player.Health != null && inventory != null && inventory.AllItems.Contains(kit))
            {
                _healBefore = player.Health.Current;
                GameEvents.RequestUseItem(kit.ItemId);
                _healAfter = player.Health.Current;
                _kitGone = !inventory.AllItems.Contains(kit);
            }
        }
        if (host && Once(13, 24, time))
        {
            // The real packing flow (as holding the pack key would): the pack comes off and stands on the ground.
            if (Find<PackingMode>() is PackingMode packing) { packing.AllowAnywhere = true; _packOpened = packing.TryOpen(); }
        }
        if (host && Once(14, 27, time))
        {
            _runningWhilePacking = !GetTree().Paused && Find<PackingMode>()?.Phase == PackingPhase.Open;
            _ownPackOff = player.GetNodeOrNull<CarrierModel>("CarrierModel")?.PackSetDown?.Off == true;
        }
        // Shouldered at 36, so a late joiner (in by about 30 s) also finds it standing on the ground.
        if (host && Once(15, 36, time)) { Find<PackingMode>()?.BeginPutOn(); }
        if (!host && Session.Remotes.TryGetValue(1, out RemoteCarrier? hostCarrier))
        {
            // Three seconds of the host's body while its pack is open (from 26 s, or from just after a late join).
            if (time >= Math.Max(26.0, _joinedAt + 0.5) && Once(16, 0, time)) { _hostPosesBefore = hostCarrier.Pose?.Received ?? 0; _posesFrom = time; }
            if (_hostPosesBefore >= 0 && time >= _posesFrom + 3.0 && time < 36.0 && Once(17, 0, time))
            {
                _hostPosesDuring = (hostCarrier.Pose?.Received ?? 0) - _hostPosesBefore;
            }
        }
        if (!host && early && Once(19, 22, time))
        {
            // Sneaking for the host's animals: crouch (the brake key, standing still) with a loaded pack, and swing.
            CarrierInventory? inventory = player.GetNodeOrNull<CarrierInventory>("Inventory");
            inventory?.TryAdd(ItemText.AtPath("res://inventory/items/relic_counterweight.tres")!);
            inventory?.TryAdd(ItemText.AtPath("res://inventory/items/historic_banner.tres")!);
            Input.ActionPress(WildlifeSpawner.CrouchAction);
        }
        if (!host && early && Once(20, 23, time))
        {
            MeleeCombat? melee = player.GetNodeOrNull<MeleeCombat>("MeleeCombat");
            melee?.SetDrawnNow(true);
            _swung = melee?.Attack() == true;
            GD.Print($"[WorldProbe] guest_sneak load={_ownLoad:0.0} swung={_swung} sent={Session.Poses?.SwingsSent}");
        }
        if (host && time >= 23.5 && time < 27.0)
        {
            foreach (NetCarrier other in GameEvents.OtherCarriers)
            {
                _guestCrouchSeen |= other.Crouching;
                _guestLoadSeen = Mathf.Max(_guestLoadSeen, other.LoadMass);
            }
        }
        if (!host && early && Once(21, 27, time))
        {
            Input.ActionRelease(WildlifeSpawner.CrouchAction);
            player.GetNodeOrNull<MeleeCombat>("MeleeCombat")?.SetDrawnNow(false);
        }
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
        if (!_reported && time >= 40)
        {
            _reported = true;
            Report(host, early, player);
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
            if (!host && early) { Check("this guest swung and told the host", _swung && Session.Poses?.SwingsSent >= 1); }
            string taken = string.Join(",", Session.TakenSupplies.OrderBy(x => x, StringComparer.Ordinal));
            GD.Print($"[WorldProbe] digest={Session.Mobs?.StateDigest()} taken={taken}");
            GD.Print($"[WorldProbe] result name={Session.LocalName} failures={_errors} got={string.Join(',', _got)}");
        }
        if (_reported && time >= 43) { GetTree().Quit(Math.Min(_errors, 1)); }
    }

    /// <summary>Follows every other player's body and pack as this machine shows them.</summary>
    private void Watch(bool host)
    {
        foreach (RemoteCarrier remote in Session!.Remotes.Values)
        {
            if (!IsInstanceValid(remote)) { continue; }
            if (!_falls.TryGetValue(remote.PeerId, out FallWatch? watch)) { watch = new FallWatch(); _falls[remote.PeerId] = watch; }
            bool falling = remote.Pose?.PhysicalFall == true;
            watch.Fell |= falling;
            if (watch.Fell && !falling)
            {
                float hips = HipsHeight(remote);
                watch.Tallest = Mathf.Max(watch.Tallest, hips);
                watch.Up |= hips > 0.6f;
            }
            PackSetDown? pack = remote.Look?.PackSetDown;
            bool off = pack?.Off == true;
            if (remote.PeerId == 1 && !host)
            {
                if (off && pack!.Way >= 0.99f && pack.Copy != null)
                {
                    _sawHostPackOff = true;
                    _hostPackOnGround |= Mathf.Abs(pack.Copy.GlobalPosition.Y - pack.GroundHeight) < 0.6f;
                }
            }
            else if (off)
            {
                // Nobody else sets a pack down here: a pack coming off would be this machine's own packing leaking onto them.
                _otherPackMoved = true;
            }
        }
    }

    /// <summary>How high the hips of their shown body are over their feet, in m (about 0.9 standing, low lying).</summary>
    private static float HipsHeight(RemoteCarrier remote)
    {
        Skeleton3D? skeleton = remote.GetNodeOrNull<Skeleton3D>("Model/Rig/Skeleton3D");
        int hips = skeleton?.FindBone("DEF-hips") ?? -1;
        if (skeleton == null || hips < 0) { return 0.0f; }
        return (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(hips).Origin).Y - remote.GlobalPosition.Y;
    }

    private void Report(bool host, bool early, PlayerController player)
    {
        foreach ((long peer, FallWatch watch) in _falls)
        {
            GD.Print($"[WorldProbe] fall_seen peer={peer} fell={watch.Fell} up={watch.Up} hips={watch.Tallest:0.00}");
        }
        if (host)
        {
            Check("the guest's fall and get-up are seen", _falls.Values.Any(w => w.Fell) && _falls.Values.Where(w => w.Fell).All(w => w.Up));
            GD.Print($"[WorldProbe] guest_seen crouching={_guestCrouchSeen} load={_guestLoadSeen:0} swings={_swingsHeard}");
            Check("the guest's crouch and load reach the host's animals", _guestCrouchSeen && _guestLoadSeen >= NetPoseCodec.LoadStep);
            Check("the guest's sword swing reaches the host's animals", _swingsHeard >= 1);
            Check("the pack comes off to be repacked (real packing flow)", _packOpened && _ownPackOff);
            Check("co-op keeps the world running while the pack is open", _runningWhilePacking);
            Check("the pack is back on after shouldering", player.GetNodeOrNull<CarrierModel>("CarrierModel")?.PackSetDown?.Off == false);
        }
        else
        {
            if (early) { Check("the host's fall and get-up are seen", _falls.TryGetValue(1, out FallWatch? seen) && seen.Fell && seen.Up); }
            if (_joinedAt < 32.0)
            {
                Check("the host's pack is seen standing on the ground while it repacks", _sawHostPackOff && _hostPackOnGround);
                Check("the host's body keeps arriving while it repacks", _hostPosesDuring > 20);
            }
            else
            {
                GD.Print($"[WorldProbe] SKIP the host's pack set down (joined at {_joinedAt:0} s, after it)");
            }
            bool hostPackBack = Session!.Remotes.TryGetValue(1, out RemoteCarrier? hostCarrier) && hostCarrier.Look?.PackSetDown?.Off == false;
            Check("the host's pack is seen back on its back", hostPackBack);
        }
        Check("nobody else's pack moves with this machine's own packing", !_otherPackMoved);
        if (early)
        {
            ItemDefinition kit = ItemText.AtPath("res://inventory/items/med_kit.tres")!;
            Check("a med kit heals its user", _kitGone && Mathf.Abs(_healAfter - _healBefore - kit.UseHealth) < 0.01f);
            GD.Print($"[WorldProbe] medkit health {_healBefore:0.0} -> {_healAfter:0.0}");
        }
        if (_selfFell) { Check("own fall ends standing", _selfUp); }
        CheckStationsAndSigns();
        GD.Print($"[WorldProbe] joined_at={_joinedAt:0.0} host_poses_while_packing={_hostPosesDuring}");
        GD.Print($"[WorldProbe] {WorldDigest()}");
    }

    /// <summary>
    /// The stations and route signs standing near the carrier are where this machine's mountain puts
    /// them (a guest builds the host's mountain after its own: nothing of its own may be left standing).
    /// </summary>
    private void CheckStationsAndSigns()
    {
        if (Find<StationManager>() is not StationManager stations) { return; }
        IRouteQuery route = GetNode<RunState>("/root/RunState").Route!;
        float worst = 0.0f;
        List<string> seen = new();
        foreach ((int index, StationSite site) in stations.Stations)
        {
            if (!IsInstanceValid(site)) { continue; }
            worst = Mathf.Max(worst, site.GlobalPosition.DistanceTo(route.Station(index)));
            seen.Add(FormattableString.Invariant($"station{index}@{site.GlobalPosition.X:0.0},{site.GlobalPosition.Z:0.0}"));
        }
        foreach ((int stage, RouteSign sign) in stations.Signs)
        {
            if (!IsInstanceValid(sign)) { continue; }
            float top = stage > 0 ? route.Station(stage).Z : route.StartZ;
            float z = top - stations.SignDistance;
            float x = route.RoutePoint(z).X - stations.SignSideOffset;
            Vector3 expected = new(x, route.GroundHeight(x, z) - stations.GroundSink, z);
            worst = Mathf.Max(worst, sign.GlobalPosition.DistanceTo(expected));
            seen.Add(FormattableString.Invariant($"sign{stage}@{sign.GlobalPosition.X:0.0},{sign.GlobalPosition.Z:0.0}"));
        }
        GD.Print($"[WorldProbe] stations_and_signs {string.Join(' ', seen)} worst={worst:0.00}");
        Check("stations and route signs stand on this mountain (none left from another)", seen.Count > 0 && worst < 0.05f);
    }

    /// <summary>
    /// One line describing the world near station 1 that every machine builds from the shared seed:
    /// the set pieces planned for the first stages, the rocks, trunks, bushes, plants and grass carpet
    /// of the chunks round the station, and the route signs standing there.
    /// </summary>
    private string WorldDigest()
    {
        RunState run = GetNode<RunState>("/root/RunState");
        IRouteQuery route = run.Route!;
        System.Text.StringBuilder pieces = new();
        int pieceCount = 0;
        if (Find<SetPiecePlacer>() is SetPiecePlacer placer)
        {
            for (int stage = 0; stage < 4; stage++)
            {
                foreach (SetPiecePlacer.Placement p in placer.PlanFor(route, stage))
                {
                    pieceCount++;
                    pieces.Append(FormattableString.Invariant($"{p.Piece.Id}@{p.Centre.X:0.00},{p.Centre.Y:0.00},{p.Centre.Z:0.00}/{p.Yaw:0.000};"));
                }
            }
        }
        System.Text.StringBuilder ground = new();
        int obstacles = 0;
        int plants = 0;
        int carpet = 0;
        if (Find<TerrainStreamer>()?.Generator is TerrainGenerator generator)
        {
            Color[] palette = new Color[Enum.GetValues<TerrainSurface>().Length + 1];
            Array.Fill(palette, Colors.Gray);
            Vector3 station = route.Station(1);
            Vector2I centre = generator.ChunkAt(station.X, station.Z);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    TerrainChunkData chunk = generator.BuildChunk(centre + new Vector2I(dx, dz), palette, 0);
                    foreach (ObstacleData o in chunk.Obstacles)
                    {
                        obstacles++;
                        ground.Append(FormattableString.Invariant($"o{(int)o.Kind}@{o.Position.X:0.00},{o.Position.Y:0.00},{o.Position.Z:0.00}/{o.Size.X:0.00};"));
                    }
                    foreach (PlantData plant in chunk.Plants)
                    {
                        plants++;
                        ground.Append(FormattableString.Invariant($"p{(int)plant.Kind}{plant.Model}@{plant.Position.X:0.00},{plant.Position.Y:0.00},{plant.Position.Z:0.00}/{plant.Yaw:0.00};"));
                    }
                    foreach (byte cell in chunk.CarpetMask) { if (cell != 0) { carpet++; } }
                    ground.Append(Convert.ToBase64String(chunk.CarpetMask));
                }
            }
        }
        System.Text.StringBuilder signs = new();
        System.Text.StringBuilder signText = new();
        int signCount = 0;
        if (Find<StationManager>() is StationManager stations)
        {
            foreach ((int stage, RouteSign sign) in stations.Signs.OrderBy(pair => pair.Key))
            {
                if (stage > 3 || !IsInstanceValid(sign)) { continue; }
                signCount++;
                signs.Append(FormattableString.Invariant($"{stage}@{sign.GlobalPosition.X:0.00},{sign.GlobalPosition.Y:0.00},{sign.GlobalPosition.Z:0.00};"));
                signText.Append(sign.Text).Append('|');
            }
        }
        return $"world_digest pieces={pieceCount}:{Hash(pieces.ToString())} ground={obstacles}/{plants}/{carpet}:{Hash(ground.ToString())} signs={signCount}:{Hash(signs.ToString())}"
            + $" | sign_text={Hash(signText.ToString())}";
    }

    private static string Hash(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(text)) { hash = (hash ^ b) * 1099511628211UL; }
        return hash.ToString("x16");
    }
}
