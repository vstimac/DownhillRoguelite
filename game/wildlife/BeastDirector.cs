using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// The night's beasts (docs/night-beasts.md). After dark, away from fire and shelter, it brings
/// beasts in out of sight behind the carrier (more of them deeper down), lets one attack at a time
/// (none while the carrier is down), hurries them when the carrier carries food, and sends them
/// away at a fire, in a shelter or at dawn. Says what's happening through hints (GameEvents.PublishHint).
/// A noticed carrier (GameEvents.Detectability above 1: the president's podium, the megaphone's echo)
/// is attacked that much sooner; a megaphone blast (GameEvents.MegaphoneBlasted) throws off the beasts
/// in its radius for a few seconds. Reads only GameEvents, RunState and the WorldClock; Step can be
/// driven directly by tests.
/// </summary>
public partial class BeastDirector : Node3D
{
    [Export] public BeastRoster? Roster { get; set; }
    /// <summary>Beasts hunt while daylight is below this (0 night, 1 day).</summary>
    [Export] public float DarkBelow { get; set; } = 0.3f;
    /// <summary>Most beasts at once in Limbo; one more every BeastsPerCircles circles.</summary>
    [Export] public int BaseBeasts { get; set; } = 1;
    [Export] public int BeastsPerCircles { get; set; } = 3;
    [Export] public int MaxBeasts { get; set; } = 4;
    /// <summary>Seconds of darkness before the first beast comes, then between new ones.</summary>
    [Export] public Vector2 ArrivalDelay { get; set; } = new(15.0f, 40.0f);
    /// <summary>New beasts appear this far away, behind (uphill of) the carrier, in m.</summary>
    [Export] public Vector2 SpawnDistance { get; set; } = new(35.0f, 50.0f);
    /// <summary>A beast stuck this long (detours didn't free it) is moved back into the dark behind the carrier, in s.</summary>
    [Export] public float RelocateAfter { get; set; } = 4.0f;
    /// <summary>A leaving beast melts into the dark after this many seconds, even if it hasn't got far.</summary>
    [Export] public float VanishAfter { get; set; } = 12.0f;
    /// <summary>A spawn spot may lie at most this far above or below the carrier, in m.</summary>
    [Export] public float MaxSpawnDrop { get; set; } = 40.0f;
    [Export] public int SpawnAttempts { get; set; } = 6;
    /// <summary>A beast this far below the carrier fell off the world (a cliff, a gap in the terrain) and is removed, in m.</summary>
    [Export] public float FellBelow { get; set; } = 60.0f;
    /// <summary>Beasts keep away from a station's fire within this distance, in m.</summary>
    [Export] public float FireRadius { get; set; } = 25.0f;
    /// <summary>Leaving beasts are removed beyond this distance, in m.</summary>
    [Export] public float GoneDistance { get; set; } = 70.0f;
    /// <summary>Attacks come this much faster while the carrier carries food (it can be smelled).</summary>
    [Export] public float FoodHurry { get; set; } = 1.6f;
    /// <summary>Said when a megaphone blast throws off a beast.</summary>
    /// <summary>With a torch lit, attacks come this much more slowly (their hurry is multiplied by it).</summary>
    [Export] public float TorchHunger { get; set; } = 0.4f;
    [Export] public string MegaphoneHint { get; set; } = "The noise throws the beasts off for a moment. Then they know exactly where you are.";
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    [Export(PropertyHint.Layers3DPhysics)] public uint BodyLayer { get; set; } = 8;
    [Export(PropertyHint.Layers3DPhysics)] public uint BodyMask { get; set; } = 9;
    /// <summary>Debug: hunt as if it were night (F-key or tests).</summary>
    [Export] public bool ForceNight { get; set; }
    /// <summary>Debug/tests: the circle to hunt in (-1 = the run's).</summary>
    [Export] public int CircleOverride { get; set; } = -1;

    public IReadOnlyList<NightBeast> Beasts => _beasts;
    public bool Hunting { get; private set; }

    private readonly List<NightBeast> _beasts = new();
    private RandomNumberGenerator _random = new();
    /// <summary>The beast (its DisplayName) a web torn in the dark calls, if it lives in this circle.</summary>
    [Export] public string WebCaller { get; set; } = "giant spider";
    /// <summary>Seconds until the web's owner arrives after a web is torn in the dark.</summary>
    [Export] public float WebCallDelay { get; set; } = 5.0f;
    [Export] public string WebCallHint { get; set; } = "Something large stirs in the trees. The web's owner felt it tear.";
    /// <summary>Strings the giant spider's webs between trees in the deeper circles (SpiderWebs).</summary>
    [Export] public bool WebsBetweenTrees { get; set; } = true;
    private RunState? _run;
    private WorldClock? _clock;
    private Vector3 _carrier;
    private bool _hasCarrier;
    private bool _downed;
    private float _arrival;
    private BeastSpecies? _called;
    private bool _toldEyes;
    private bool _toldGrowl;
    private PhysicsRayQueryParameters3D? _ray;
    private IReadOnlyList<string>? _carried;
    private string _food = "";

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _random.Randomize();
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            // Debug: `-- --beasts-now` hunts at once (pair it with `--hour=23` for the dark).
            if (argument == "--beasts-now")
            {
                ForceNight = true;
                ArrivalDelay = new Vector2(1.0f, 3.0f);
            }
        }
        _arrival = _random.RandfRange(ArrivalDelay.X, ArrivalDelay.Y);
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.DownedChanged += OnDowned;
        GameEvents.RespawnRequested += Clear;
        GameEvents.MegaphoneBlasted += OnMegaphone;
        GameEvents.Snared += OnSnared;
        // The giant spider's webs between the trees of the deeper circles.
        if (WebsBetweenTrees)
        {
            AddChild(new SpiderWebs { Name = "SpiderWebs" });
        }
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.DownedChanged -= OnDowned;
        GameEvents.RespawnRequested -= Clear;
        GameEvents.MegaphoneBlasted -= OnMegaphone;
        GameEvents.Snared -= OnSnared;
    }

    /// <summary>Throws off every beast within the blast's radius. Returns how many.</summary>
    public int Blast(Vector3 at, float radius, float confuseSeconds)
    {
        int count = 0;
        foreach (NightBeast beast in _beasts)
        {
            if (beast.GlobalPosition.DistanceTo(at) <= radius && beast.State != NightBeast.Mode.Leave)
            {
                beast.Confuse(confuseSeconds);
                count++;
            }
        }
        if (count > 0)
        {
            GameEvents.PublishHint(MegaphoneHint);
        }
        return count;
    }

    private void OnMegaphone(Vector3 at, float radius, float confuseSeconds, float noticeSeconds)
    {
        Blast(at, radius, confuseSeconds);
    }

    /// <summary>A web torn while they hunt: its spider comes for the carrier, and soon.</summary>
    private void OnSnared(float seconds, float speed)
    {
        if (!Hunting || Roster == null || _called != null)
        {
            return;
        }
        foreach (BeastSpecies species in Roster.Beasts)
        {
            if (species.DisplayName == WebCaller && species.MinCircle <= Circle())
            {
                _called = species;
                _arrival = Mathf.Min(_arrival, WebCallDelay);
                if (WebCallHint.Length > 0)
                {
                    GameEvents.PublishHint(WebCallHint);
                }
                return;
            }
        }
    }

    /// <summary>The beast a torn web called, coming next (null: none).</summary>
    public BeastSpecies? Called => _called;

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _carrier = snapshot.Position;
        _hasCarrier = true;
    }

    private void OnDowned(bool downed) => _downed = downed;

    public override void _PhysicsProcess(double delta)
    {
        if (!_hasCarrier)
        {
            return;
        }
        float daylight = 1.0f;
        if (_clock != null)
        {
            daylight = _clock.Daylight;
        }
        if (ForceNight)
        {
            daylight = 0.0f;
        }
        Step((float)delta, _carrier, daylight, GameEvents.InShelter || NearFire(_carrier), _downed || GameEvents.CarrierBleeding);
    }

    /// <summary>One update: brings beasts in, drives them, removes the ones gone, says what's happening.</summary>
    public void Step(float dt, Vector3 carrier, float daylight, bool safe, bool downed)
    {
        if (!GameEvents.MaySimulateWorld) { if (_beasts.Count > 0) { Clear(); } Hunting = false; return; }
        bool anyoneExposed = !safe;
        Vector3 spawnNear = carrier;
        foreach (NetCarrier other in GameEvents.OtherCarriers)
        {
            if (!other.Down && !NearFire(other.Feet)) { anyoneExposed = true; spawnNear = other.Feet; }
        }
        Hunting = daylight < DarkBelow && anyoneExposed;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        string food = FoodCarried();
        float hunger = 1.0f;
        if (food.Length > 0)
        {
            hunger = FoodHurry;
        }
        // Noticed (the podium, the megaphone's echo): attacks come that much sooner.
        hunger *= Mathf.Max(GameEvents.Detectability, 0.1f);
        // A burning torch keeps them back: they circle longer before they dare to charge.
        if (GameEvents.CarrierTorchLit)
        {
            hunger *= TorchHunger;
        }
        // Co-op: the other players the beasts may go for (standing, and not by a station's fire).
        System.Collections.Generic.List<NetCarrier> prey = new();
        foreach (NetCarrier other in GameEvents.OtherCarriers)
        {
            if (!other.Down && !NearFire(other.Feet))
            {
                prey.Add(other);
            }
        }
        NetCarrier[] others = prey.ToArray();
        bool someoneAttacking = false;
        foreach (NightBeast beast in _beasts)
        {
            if (beast.State == NightBeast.Mode.WindUp || beast.State == NightBeast.Mode.Charge)
            {
                someoneAttacking = true;
            }
        }
        for (int i = _beasts.Count - 1; i >= 0; i--)
        {
            NightBeast beast = _beasts[i];
            NightBeast.Mode before = beast.State;
            beast.FoodInPack = food;
            beast.Others = others;
            bool mayAttack = !someoneAttacking;
            float targetHunger = hunger;
            if (beast.TargetPeer != 0)
            {
                targetHunger = 1;
                foreach (NetCarrier other in others) { if (other.Peer == beast.TargetPeer && other.TorchLit) { targetHunger *= TorchHunger; } }
            }
            beast.Tick(dt, carrier, mayAttack, !Hunting, targetHunger, space, downed || safe);
            if (beast.State == NightBeast.Mode.WindUp && before != NightBeast.Mode.WindUp)
            {
                someoneAttacking = true;
                if (!_toldGrowl && beast.TargetPeer == 0)
                {
                    _toldGrowl = true;
                    GameEvents.PublishHint($"A growl behind you: the {beast.Species?.DisplayName} is about to charge. Get out of its way!");
                }
            }
            if (beast.LastHit.Length > 0)
            {
                Told(beast);
            }
            if (beast.StuckFor > RelocateAfter && beast.State != NightBeast.Mode.Leave && Spot(WorldInterests.Nearest(beast.GlobalPosition, carrier), space, out Vector3 elsewhere))
            {
                // Stuck behind something for long: it slips away into the dark and comes again from behind.
                beast.Relocate(elsewhere + Vector3.Up * 0.05f);
            }
            Vector3 offset = beast.GlobalPosition - WorldInterests.Nearest(beast.GlobalPosition, carrier);
            bool fellOff = offset.Y < -FellBelow;
            bool gone = beast.State == NightBeast.Mode.Leave
                && (new Vector2(offset.X, offset.Z).Length() > GoneDistance || beast.LeavingFor > VanishAfter);
            if (fellOff || gone)
            {
                beast.QueueFree();
                _beasts.RemoveAt(i);
            }
        }
        if (!Hunting)
        {
            // Dawn or a fire: whatever a torn web called gives up.
            _called = null;
            return;
        }
        _arrival -= dt;
        if (_arrival <= 0.0f && _beasts.Count < MostBeasts())
        {
            _arrival = _random.RandfRange(ArrivalDelay.X, ArrivalDelay.Y);
            if (Bring(spawnNear, space) != null && !_toldEyes)
            {
                _toldEyes = true;
                GameEvents.PublishHint("Eyes in the dark. Something is following you. Fire keeps it away.");
            }
        }
    }

    /// <summary>Brings a beast in behind the carrier, on the ground. Returns it, or null when there's no ground or no beast for this circle.</summary>
    public NightBeast? Bring(Vector3 carrier, PhysicsDirectSpaceState3D space)
    {
        BeastSpecies? species = _called ?? Pick(Circle());
        if (species == null)
        {
            return null;
        }
        if (!Spot(carrier, space, out Vector3 ground))
        {
            return null;
        }
        _called = null;
        NightBeast beast = new();
        beast.Setup(species, _random.Randi(), BodyLayer, BodyMask);
        AddChild(beast);
        beast.GlobalPosition = ground + Vector3.Up * 0.05f;
        _beasts.Add(beast);
        return beast;
    }

    /// <summary>
    /// A spot on the ground behind (uphill, +Z, out of sight of a carrier looking down the route) at
    /// spawn distance, no further above or below the carrier than MaxSpawnDrop (not over a ridge
    /// or the back of the summit). Tries a few directions, widening to the sides.
    /// </summary>
    private bool Spot(Vector3 carrier, PhysicsDirectSpaceState3D space, out Vector3 ground)
    {
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = GroundMask };
        for (int attempt = 0; attempt < SpawnAttempts; attempt++)
        {
            float spread = 1.2f + 0.4f * attempt;
            float angle = _random.RandfRange(-spread, spread);
            float distance = _random.RandfRange(SpawnDistance.X, SpawnDistance.Y);
            Vector3 at = carrier + new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle)) * distance;
            _ray.From = at + Vector3.Up * 120.0f;
            _ray.To = at + Vector3.Down * 250.0f;
            Godot.Collections.Dictionary hit = space.IntersectRay(_ray);
            if (hit.Count == 0)
            {
                continue;
            }
            ground = (Vector3)hit["position"];
            if (Mathf.Abs(ground.Y - carrier.Y) <= MaxSpawnDrop)
            {
                return true;
            }
        }
        ground = carrier;
        return false;
    }

    /// <summary>Removes every beast (a new run).</summary>
    public void Clear()
    {
        foreach (NightBeast beast in _beasts)
        {
            beast.QueueFree();
        }
        _beasts.Clear();
        _toldEyes = false;
        _toldGrowl = false;
        _called = null;
        _arrival = _random.RandfRange(ArrivalDelay.X, ArrivalDelay.Y);
    }

    public int MostBeasts()
    {
        int extra = Circle() / Mathf.Max(BeastsPerCircles, 1);
        return Mathf.Min(BaseBeasts + extra, MaxBeasts);
    }

    private int Circle()
    {
        if (CircleOverride >= 0)
        {
            return CircleOverride;
        }
        if (_run != null)
        {
            return _run.Circle;
        }
        return 0;
    }

    private BeastSpecies? Pick(int circle)
    {
        if (Roster == null)
        {
            return null;
        }
        float total = 0.0f;
        foreach (BeastSpecies species in Roster.Beasts)
        {
            if (species.MinCircle <= circle)
            {
                total += species.Weight;
            }
        }
        if (total <= 0.0f)
        {
            return null;
        }
        float roll = _random.RandfRange(0.0f, total);
        foreach (BeastSpecies species in Roster.Beasts)
        {
            if (species.MinCircle > circle)
            {
                continue;
            }
            roll -= species.Weight;
            if (roll <= 0.0f)
            {
                return species;
            }
        }
        return null;
    }

    /// <summary>Near a station's fire: the stations around the carrier's stage.</summary>
    private bool NearFire(Vector3 carrier)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null)
        {
            return false;
        }
        int next = route.NextStationIndex(carrier.Z);
        for (int index = Mathf.Max(1, next - 1); index <= next; index++)
        {
            Vector3 fire = route.Station(index);
            if (new Vector2(fire.X - carrier.X, fire.Z - carrier.Z).Length() < FireRadius)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A food item the carrier has (the she-wolf's target, and what the beasts smell), or empty. Looked up again only when the carried list changes.</summary>
    private string FoodCarried()
    {
        IReadOnlyList<string> carried = GameEvents.CarriedItems;
        if (ReferenceEquals(carried, _carried))
        {
            return _food;
        }
        _carried = carried;
        _food = "";
        foreach (string id in carried)
        {
            if (ItemText.Find(id) is ItemDefinition item && item.Category == ItemCategory.Food)
            {
                _food = id;
                break;
            }
        }
        return _food;
    }

    private static void Told(NightBeast beast)
    {
        string name = beast.Species?.DisplayName ?? "beast";
        if (beast.LastHit == "threw")
        {
            GameEvents.PublishHint($"The {name} hits you. Space as you come down: roll the landing!");
        }
        else
        {
            string what = beast.LastHit.Replace('_', ' ');
            GameEvents.PublishHint($"The {name} tears the {what} out of your pack and drops it. Take it back if you dare.");
        }
        beast.LastHit = "";
    }
}
