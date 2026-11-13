using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The šalteruša: a counter clerk behind glass in a booth (CounterBooth) beside a station's fire, voiced
/// by the team's own recordings (audio/voice/npc_salterusa, listed in narrative/salterusa/data/npc_salterusa.csv:
/// one id per clip, its event, emotion and subtitle). She stands at FirstStation, and once the carrier
/// has gone on past her, she is waiting again EveryStations further down.
///
/// What she says, by the manifest's events:
///  - bark: when the carrier comes up to the counter (once per approach, with a cooldown);
///  - idle, breath: while they stand around without asking anything, now and then; angry once they've
///    loitered through AngerAfterIdle idle lines;
///  - explain, then documents / quest / gear: E opens the counter (the HUD's dialogue choices, as for the
///    restsite president): permits, what it takes to go further (spoken in order) and your gear. Sometimes
///    she just refuses (refuse), and when a topic has nothing left, it says so on the door (door, refuse);
///  - talk: the choice "Can we just talk?" opens the conversation page with her (the M11 NPC conversation
///    through the local model, persona narrative/npcs/salterusa.tres);
///  - return: the first time the carrier comes up to her in a run after a death or an earlier run;
///  - rare (priority 3): with a small chance in place of a bark or an idle line, each once per run;
///  - dear ("Bože dragi"): when the carrier is thrown or badly hurt near her, or the first time she's struck;
///    angry when struck again.
/// One line at a time (NpcVoice); a new run puts everything back. Debug test: debug/tests/SalterusaTests.tscn.
/// </summary>
public partial class Salterusa : Node3D, IInteractable, IDescribed, IMeleeTarget
{
    [ExportGroup("Who")]
    [Export] public NpcPersona? Persona { get; set; }
    [Export(PropertyHint.File, "*.tres")] public string PersonaPath { get; set; } = "res://narrative/npcs/salterusa.tres";
    [Export(PropertyHint.File, "*.csv")] public string ManifestPath { get; set; } = "res://narrative/salterusa/data/npc_salterusa.csv";

    [ExportGroup("Placement")]
    /// <summary>Stand by a station's fire each run (off for test scenes, which place her).</summary>
    [Export] public bool PlaceAtStation { get; set; } = true;
    [Export] public int FirstStation { get; set; } = 2;
    /// <summary>After the carrier passes her station she waits again this many stations on (0: only once).</summary>
    [Export] public int EveryStations { get; set; } = 4;
    /// <summary>Where the booth stands from the fire (x across the slope, z downhill is -z), in m; it faces the fire.</summary>
    [Export] public Vector3 BoothOffset { get; set; } = new(5.5f, 0.0f, -5.0f);
    /// <summary>`-- --salterusa-near` puts her this far below the start instead (rendered checks).</summary>
    [Export] public float NearRouteDistance { get; set; } = 10.0f;
    [Export] public float NearRouteSide { get; set; } = 4.0f;

    [ExportGroup("Figure")]
    [Export(PropertyHint.File, "*.glb")] public string RigPath { get; set; } = "res://art/characters/quaternius_ual/UAL_Mannequin.glb";
    [Export] public float Height { get; set; } = 1.62f;
    [Export] public float DriverHeight { get; set; } = 1.83f;
    /// <summary>Where she sits in the booth's space: the sitting clip's root, about 0.35 m below the seat at her height (CounterBooth.SeatTop).</summary>
    [Export] public Vector3 SitAt { get; set; } = new(0.0f, 0.65f, -0.02f);
    [Export] public string IdleClip { get; set; } = "Sitting_Idle";
    [Export] public string TalkClip { get; set; } = "Sitting_Talking";
    [Export] public string HurtClip { get; set; } = "Hit_Chest";
    [Export] public float ClipBlend { get; set; } = 0.3f;
    [Export] public Color BodyColor { get; set; } = new(0.36f, 0.42f, 0.55f);
    [Export] public Color HairColor { get; set; } = new(0.42f, 0.2f, 0.16f);
    [Export] public string HeadBone { get; set; } = "DEF-head";
    /// <summary>Her hair (a cap and a bun) and glasses, from the head bone: up and back (m, back is negative).</summary>
    [Export] public Vector2 HairAt { get; set; } = new(0.1f, -0.015f);
    [Export] public float HairRadius { get; set; } = 0.105f;
    [Export] public Vector2 BunAt { get; set; } = new(0.15f, -0.1f);
    [Export] public float BunRadius { get; set; } = 0.055f;
    [Export] public Vector2 GlassesAt { get; set; } = new(0.085f, 0.095f);
    [Export] public float BodyRadius { get; set; } = 0.3f;

    [ExportGroup("Voice")]
    /// <summary>She notices the carrier this close (m), forgets them beyond LeaveDistance, closes the counter beyond TalkDistance.</summary>
    [Export] public float GreetDistance { get; set; } = 9.0f;
    [Export] public float LeaveDistance { get; set; } = 15.0f;
    [Export] public float TalkDistance { get; set; } = 5.0f;
    [Export] public float BarkCooldown { get; set; } = 20.0f;
    /// <summary>Seconds between idle lines while the carrier stands around (random between x and y).</summary>
    [Export] public Vector2 IdleInterval { get; set; } = new(14.0f, 26.0f);
    [Export] public int AngerAfterIdle { get; set; } = 5;
    [Export] public float BreathChance { get; set; } = 0.3f;
    /// <summary>Chance a bark or idle line is one of the rare ones instead.</summary>
    [Export] public float RareChance { get; set; } = 0.08f;
    /// <summary>Chance she refuses a question at the counter outright.</summary>
    [Export] public float RefuseChance { get; set; } = 0.25f;
    /// <summary>"Bože dragi" when the carrier is thrown or takes at least StartleDamage this close (m), at most every StartleCooldown s.</summary>
    [Export] public float StartleDistance { get; set; } = 14.0f;
    [Export] public float StartleDamage { get; set; } = 12.0f;
    [Export] public float StartleCooldown { get; set; } = 8.0f;

    [ExportGroup("Counter")]
    [Export] public string[] ChoiceTexts { get; set; } =
    {
        "I need a permit to go further down.",
        "What do I need to get past here?",
        "Could you look over my gear?",
        "Can we just talk?",
        "Goodbye.",
    };
    /// <summary>What each choice asks for: a manifest event, "talk" (the conversation page) or "bye".</summary>
    [Export] public string[] ChoiceEvents { get; set; } = { "documents", "quest", "gear", "talk", "bye" };
    /// <summary>Events whose lines are said in manifest order (a thread), not shuffled.</summary>
    [Export] public string[] OrderedEvents { get; set; } = { "quest" };
    /// <summary>Events whose lines are each said at most once per run.</summary>
    [Export] public string[] OncePerRunEvents { get; set; } = { "rare", "documents", "quest", "gear" };

    public const string TalkChoice = "talk";
    public const string ByeChoice = "bye";

    public NpcVoice? Voice { get; private set; }
    public VoiceManifest Manifest { get; private set; } = new();
    public CounterBooth? Booth { get; private set; }
    public AnimationPlayer? Animations { get; private set; }
    public bool Placed { get; private set; }
    /// <summary>The station whose fire she sits by now (0 while unplaced or in a test scene).</summary>
    public int Station { get; private set; }
    /// <summary>True while the counter is open (the dialogue choices).</summary>
    public bool Talking { get; private set; }
    public bool ChoicesShown { get; private set; }
    public string Clip { get; private set; } = "";
    /// <summary>Every line she started this run, in order (tests, debug).</summary>
    public IReadOnlyList<string> Said => _said;
    /// <summary>A death (or an earlier run) she'd remember: the first greeting of a run is a return line.</summary>
    public bool Returning
    {
        get
        {
            if (s_deathSeen)
            {
                return true;
            }
            return _run != null && _run.Records.Runs > 0;
        }
    }

    // IDescribed
    public string Title => Persona?.DisplayName ?? "Šalteruša";
    public string Description => Persona?.Description ?? "";
    public string Details => Persona?.Title ?? "";
    public string Prompt
    {
        get
        {
            if (Talking)
            {
                return "E  Hurry her along";
            }
            return "E  Step up to the counter";
        }
    }
    public Resource? IconSource => null;

    // IInteractable
    public Vector3 InteractionPoint
    {
        get
        {
            if (Booth != null)
            {
                return Booth.ToGlobal(Booth.WindowPoint);
            }
            return GlobalPosition + Vector3.Up * 1.2f;
        }
    }

    // IMeleeTarget
    public Vector3 HitBase => ToGlobal(SitAt);
    public float HitHeight => Height * 0.8f;
    public float HitRadius => BodyRadius;
    public bool CanBeHit => Visible;
    public bool Lockable => false;

    private static bool s_deathSeen;

    private readonly List<string> _said = new();
    private readonly Dictionary<string, List<string>> _decks = new();
    private readonly HashSet<string> _spent = new();
    private readonly RandomNumberGenerator _random = new();
    private RunState? _run;
    private Node3D? _figure;
    private BoneAttachment3D? _dress;
    private Skeleton3D? _skeleton;
    private Transform3D _head = Transform3D.Identity;
    private bool _dressed;
    private bool _near;
    private bool _metThisRun;
    private bool _greetedThisRun;
    private bool _explained;
    private bool _doorSaid;
    private bool _choicesAfterLine;
    private int _idleCount;
    private int _struck;
    private float _idleTimer;
    private float _barkTimer;
    private float _startleTimer;
    private float _hurtTimer;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _random.Randomize();
        if (Persona == null && ResourceLoader.Exists(PersonaPath))
        {
            Persona = ResourceLoader.Load<NpcPersona>(PersonaPath);
        }
        Manifest = VoiceManifest.Load(ManifestPath);
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--salterusa-near") >= 0)
        {
            PlaceAtStation = true;
            FirstStation = 0;
        }
        AddToGroup(IInteractable.Group);
        AddToGroup(IMeleeTarget.Group);
        Booth = new CounterBooth { Name = "Booth" };
        AddChild(Booth);
        BuildFigure();
        Voice = new NpcVoice
        {
            Name = "Voice",
            NpcId = NpcIdOrDefault(),
            SpeakerName = Title,
            Manifest = Manifest,
            Position = SitAt + Vector3.Up * Height * 0.55f,
        };
        AddChild(Voice);
        ResetIdleTimer();
        Play(IdleClip);
        GameEvents.VoiceLineCompleted += OnLineCompleted;
        GameEvents.DialogueChoiceRequested += OnChoice;
        GameEvents.RespawnRequested += OnNewRun;
        GameEvents.StationReached += OnStationReached;
        GameEvents.CarrierDied += OnCarrierDied;
        GameEvents.Launched += OnLaunched;
        GameEvents.Damaged += OnDamaged;
    }

    public override void _ExitTree()
    {
        GameEvents.VoiceLineCompleted -= OnLineCompleted;
        GameEvents.DialogueChoiceRequested -= OnChoice;
        GameEvents.RespawnRequested -= OnNewRun;
        GameEvents.StationReached -= OnStationReached;
        GameEvents.CarrierDied -= OnCarrierDied;
        GameEvents.Launched -= OnLaunched;
        GameEvents.Damaged -= OnDamaged;
        if (Talking)
        {
            Close();
        }
    }

    private string NpcIdOrDefault()
    {
        if (Persona != null && Persona.Id.Length > 0)
        {
            return Persona.Id;
        }
        return "salterusa";
    }

    /// <summary>The mannequin sitting at her desk, in a blouse colour, with hair, a bun and glasses.</summary>
    private void BuildFigure()
    {
        if (ResourceLoader.Load(RigPath) is not PackedScene rigScene || rigScene.Instantiate() is not Node3D rig)
        {
            GD.PushWarning($"Salterusa: can't load {RigPath}; the counter stays empty.");
            return;
        }
        _figure = rig;
        rig.Name = "Figure";
        rig.Position = SitAt;
        rig.Scale = Vector3.One * (Height / Mathf.Max(DriverHeight, 0.1f));
        AddChild(rig);
        StandardMaterial3D cloth = new() { AlbedoColor = BodyColor, Roughness = 0.85f };
        foreach (Node node in rig.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh)
            {
                mesh.MaterialOverride = cloth;
            }
        }
        foreach (Node node in rig.FindChildren("*", "AnimationPlayer", true, false))
        {
            Animations = node as AnimationPlayer;
            break;
        }
        foreach (Node node in rig.FindChildren("*", "Skeleton3D", true, false))
        {
            if (node is Skeleton3D skeleton && skeleton.FindBone(HeadBone) >= 0)
            {
                BoneAttachment3D head = new() { Name = "Head", BoneName = HeadBone };
                skeleton.AddChild(head);
                _dress = head;
                _skeleton = skeleton;
            }
            break;
        }
    }

    /// <summary>
    /// Hair, bun and glasses go on the head bone once it has its first pose, placed by her own up and
    /// forward (the bone's axes are the rig's business), so they then move with her head.
    /// </summary>
    private void Dress()
    {
        _dressed = true;
        if (_dress == null || _skeleton == null)
        {
            return;
        }
        _head = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_skeleton.FindBone(HeadBone));
        Basis facing = GlobalBasis.Orthonormalized();
        Vector3 up = facing.Y;
        Vector3 forward = facing.Z;
        Vector3 head = _head.Origin;
        StandardMaterial3D hair = new() { AlbedoColor = HairColor, Roughness = 0.95f };
        StandardMaterial3D frames = new() { AlbedoColor = new Color(0.08f, 0.08f, 0.09f), Roughness = 0.4f };
        Piece("Hair", new SphereMesh { Radius = HairRadius, Height = HairRadius, IsHemisphere = true, RadialSegments = 14, Rings = 6 },
            head + up * HairAt.X + forward * HairAt.Y, facing.Rotated(facing.X, Mathf.DegToRad(-20.0f)), hair);
        Piece("Bun", new SphereMesh { Radius = BunRadius, Height = BunRadius * 2.0f, RadialSegments = 10, Rings = 6 },
            head + up * BunAt.X + forward * BunAt.Y, facing, hair);
        Piece("Glasses", new BoxMesh { Size = new Vector3(0.15f, 0.035f, 0.012f) }, head + up * GlassesAt.X + forward * GlassesAt.Y, facing, frames);
    }

    private void Piece(string name, Mesh mesh, Vector3 at, Basis basis, Material material)
    {
        if (_dress == null)
        {
            return;
        }
        // Kept relative to the head bone's pose (what the attachment follows), wherever the attachment is this frame.
        Transform3D world = new(basis, at);
        MeshInstance3D piece = new() { Name = name, Mesh = mesh, MaterialOverride = material, Transform = _head.AffineInverse() * world };
        _dress.AddChild(piece);
    }


    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (PlaceAtStation && !Placed)
        {
            TryPlace();
        }
        if (!_dressed && _figure != null)
        {
            Dress();
        }
        if (_barkTimer > 0.0f)
        {
            _barkTimer -= dt;
        }
        if (_startleTimer > 0.0f)
        {
            _startleTimer -= dt;
        }
        if (_hurtTimer > 0.0f)
        {
            _hurtTimer -= dt;
        }
        Vector3 carrier = GameEvents.CarrierPosition;
        Vector3 window = InteractionPoint;
        float distance = new Vector2(carrier.X - window.X, carrier.Z - window.Z).Length();
        if (Talking && distance > TalkDistance)
        {
            Close();
        }
        if (distance > LeaveDistance)
        {
            _near = false;
            _idleCount = 0;
        }
        else if (distance < GreetDistance)
        {
            if (!_near)
            {
                _near = true;
                Greet();
            }
            else if (!Talking && !Busy())
            {
                _idleTimer -= dt;
                if (_idleTimer <= 0.0f)
                {
                    Idle();
                }
            }
        }
        Animate();
    }

    private bool Busy() => Voice != null && Voice.Speaking;

    /// <summary>The carrier came up to the counter.</summary>
    private void Greet()
    {
        ResetIdleTimer();
        if (!_metThisRun)
        {
            _metThisRun = true;
            GameEvents.PublishVisitorMet(Title, Details);
        }
        if (!_greetedThisRun)
        {
            _greetedThisRun = true;
            if (Returning && Say(Pick("return")))
            {
                _barkTimer = BarkCooldown;
                return;
            }
        }
        if (_barkTimer > 0.0f || Busy())
        {
            return;
        }
        _barkTimer = BarkCooldown;
        if (_random.Randf() < RareChance && Say(Pick("rare")))
        {
            return;
        }
        Say(Pick("bark"));
    }

    /// <summary>The carrier stands around without asking anything.</summary>
    private void Idle()
    {
        ResetIdleTimer();
        _idleCount++;
        if (AngerAfterIdle > 0 && _idleCount % AngerAfterIdle == 0 && Say(Pick("angry")))
        {
            return;
        }
        float roll = _random.Randf();
        if (roll < RareChance && Say(Pick("rare")))
        {
            return;
        }
        if (roll < RareChance + BreathChance && Say(Pick("breath")))
        {
            return;
        }
        Say(Pick("idle"));
    }

    public void SetFocused(bool focused)
    {
    }

    public bool Interact(Node3D interactor)
    {
        if (Busy())
        {
            Voice!.Skip();
            return true;
        }
        if (Talking)
        {
            if (!ChoicesShown)
            {
                ShowChoices();
            }
            return true;
        }
        Open();
        return true;
    }

    /// <summary>Opens the counter: she explains (the first time in a run), then the choices.</summary>
    public void Open()
    {
        Talking = true;
        GameEvents.PublishDialogue(true);
        ResetIdleTimer();
        _idleCount = 0;
        if (!_explained)
        {
            _explained = true;
            if (SayThenChoices(Pick("explain")))
            {
                return;
            }
        }
        ShowChoices();
    }

    /// <summary>Picks a choice by index (the HUD's number keys do this through GameEvents).</summary>
    public void Choose(int index)
    {
        if (!Talking || !ChoicesShown || index < 0 || index >= ChoiceEvents.Length)
        {
            return;
        }
        HideChoices();
        string asked = ChoiceEvents[index];
        GD.Print($"[dialogue] {NpcIdOrDefault()} choice {asked}");
        if (asked == ByeChoice)
        {
            Close();
            return;
        }
        if (asked == TalkChoice)
        {
            Close();
            if (Persona != null)
            {
                GameEvents.RequestTalkTo(Persona.Speaker(Voice?.GlobalPosition ?? InteractionPoint));
            }
            return;
        }
        if (!SayThenChoices(Answer(asked)))
        {
            ShowChoices();
        }
    }

    /// <summary>
    /// Her answer to a question at the counter: now and then a flat refusal; otherwise the topic's next
    /// line, and once a topic has nothing left, the door (once), then refusals.
    /// </summary>
    public string Answer(string topic)
    {
        if (_random.Randf() < RefuseChance)
        {
            return Pick("refuse");
        }
        string line = Pick(topic);
        if (line.Length > 0)
        {
            return line;
        }
        if (!_doorSaid)
        {
            _doorSaid = true;
            string door = Pick("door");
            if (door.Length > 0)
            {
                return door;
            }
        }
        return Pick("refuse");
    }

    /// <summary>Closes the counter (walked off, or said goodbye). A line already spoken finishes.</summary>
    public void Close()
    {
        Talking = false;
        _choicesAfterLine = false;
        HideChoices();
        GameEvents.PublishDialogue(false);
        ResetIdleTimer();
    }

    private void ShowChoices()
    {
        ChoicesShown = true;
        GameEvents.PublishDialogueChoices(Title, ChoiceTexts);
    }

    private void HideChoices()
    {
        ChoicesShown = false;
        GameEvents.PublishDialogueChoices(Title, System.Array.Empty<string>());
    }

    private bool SayThenChoices(string id)
    {
        if (!Say(id))
        {
            return false;
        }
        _choicesAfterLine = true;
        return true;
    }

    /// <summary>Says a manifest line (false for none, or one the manifest doesn't have).</summary>
    public bool Say(string id)
    {
        if (id.Length == 0 || Voice == null || !Manifest.Has(id))
        {
            return false;
        }
        _choicesAfterLine = false;
        if (!Voice.Say(id))
        {
            return false;
        }
        _said.Add(id);
        return true;
    }

    /// <summary>
    /// The next line of an event: shuffled (or in manifest order for OrderedEvents), no repeat until all
    /// have been said; OncePerRunEvents don't come round again in the run (empty once they're used up).
    /// </summary>
    public string Pick(string eventName)
    {
        if (!_decks.TryGetValue(eventName, out List<string>? deck))
        {
            deck = new List<string>();
            _decks[eventName] = deck;
        }
        if (deck.Count == 0)
        {
            if (System.Array.IndexOf(OncePerRunEvents, eventName) >= 0 && _spent.Contains(eventName))
            {
                return "";
            }
            _spent.Add(eventName);
            foreach (VoiceLine line in Manifest.ForEvent(eventName))
            {
                deck.Add(line.Id);
            }
            if (System.Array.IndexOf(OrderedEvents, eventName) < 0)
            {
                for (int i = deck.Count - 1; i > 0; i--)
                {
                    int j = _random.RandiRange(0, i);
                    (deck[i], deck[j]) = (deck[j], deck[i]);
                }
            }
        }
        if (deck.Count == 0)
        {
            return "";
        }
        string id = deck[0];
        deck.RemoveAt(0);
        return id;
    }

    private void ResetIdleTimer()
    {
        _idleTimer = _random.RandfRange(IdleInterval.X, Mathf.Max(IdleInterval.X, IdleInterval.Y));
    }

    private void OnLineCompleted(string npcId, string lineId, bool skipped)
    {
        if (npcId != NpcIdOrDefault())
        {
            return;
        }
        ResetIdleTimer();
        if (Talking && _choicesAfterLine && !Busy())
        {
            _choicesAfterLine = false;
            ShowChoices();
        }
    }

    private void OnChoice(int index)
    {
        if (Talking)
        {
            Choose(index);
        }
    }

    public void TakeHit(float damage, Vector3 push, string source)
    {
        _struck++;
        if (Talking)
        {
            Close();
        }
        if (Animations != null && PlayClip(HurtClip, ClipBlend * 0.4f, false))
        {
            _hurtTimer = (float)Animations.GetAnimation(Clip).Length;
        }
        if (_struck == 1 && Say(Pick("dear")))
        {
            return;
        }
        Say(Pick("angry"));
    }

    public bool Bleed(float damage)
    {
        return Visible;
    }

    private bool NearCarrier()
    {
        return GlobalPosition.DistanceTo(GameEvents.CarrierPosition) < StartleDistance;
    }

    private void Startled()
    {
        if (_startleTimer > 0.0f || Busy() || !NearCarrier())
        {
            return;
        }
        _startleTimer = StartleCooldown;
        Say(Pick("dear"));
    }

    private void OnLaunched(string cause) => Startled();

    private void OnDamaged(float amount)
    {
        if (amount >= StartleDamage)
        {
            Startled();
        }
    }

    private void OnCarrierDied(string cause)
    {
        s_deathSeen = true;
    }

    /// <summary>Puts her booth by her station's fire, facing it, once the mountain's route exists.</summary>
    private void TryPlace()
    {
        IRouteQuery? route = _run?.Route;
        if (route == null)
        {
            return;
        }
        if (Station < FirstStation)
        {
            Station = FirstStation;
        }
        Vector3 fire;
        Vector3 at;
        if (Station <= 0)
        {
            // Rendered checks: just below the start, beside the route.
            fire = route.RoutePoint(route.StartZ - NearRouteDistance);
            at = fire + Vector3.Right * NearRouteSide;
        }
        else
        {
            fire = route.Station(Station);
            at = fire + BoothOffset;
        }
        at.Y = route.GroundHeight(at.X, at.Z);
        GlobalPosition = at;
        Vector3 look = new(fire.X, at.Y, fire.Z);
        if (!look.IsEqualApprox(at))
        {
            LookAt(look, Vector3.Up, true);
        }
        Placed = true;
    }

    /// <summary>Once the carrier is past her station, she moves on to wait further down.</summary>
    private void OnStationReached(int station)
    {
        if (!PlaceAtStation || EveryStations <= 0 || Station <= 0 || station <= Station)
        {
            return;
        }
        Close();
        Station += EveryStations;
        Placed = false;
        _near = false;
    }

    /// <summary>A new run: everything back, at the first station of the new mountain.</summary>
    private void OnNewRun()
    {
        if (Talking)
        {
            Close();
        }
        Voice?.Skip();
        _said.Clear();
        _decks.Clear();
        _spent.Clear();
        _near = false;
        _metThisRun = false;
        _greetedThisRun = false;
        _explained = false;
        _doorSaid = false;
        _idleCount = 0;
        _struck = 0;
        _barkTimer = 0.0f;
        _hurtTimer = 0.0f;
        ResetIdleTimer();
        if (PlaceAtStation)
        {
            Station = FirstStation;
            Placed = false;
        }
    }

    /// <summary>Sitting idle, talking while a line plays, flinching when struck.</summary>
    private void Animate()
    {
        if (_hurtTimer > 0.0f)
        {
            return;
        }
        if (Busy())
        {
            Play(TalkClip);
        }
        else
        {
            Play(IdleClip);
        }
    }

    private void Play(string clip)
    {
        PlayClip(clip, ClipBlend, true);
    }

    /// <summary>Plays a clip on the rig (Godot drops the "_Loop" suffix on import; either name works).</summary>
    private bool PlayClip(string clip, float blend, bool loop)
    {
        if (Animations == null)
        {
            return false;
        }
        string name = clip;
        if (!Animations.HasAnimation(name))
        {
            name = clip + "_Loop";
        }
        if (!Animations.HasAnimation(name))
        {
            return false;
        }
        if (name == Clip && Animations.IsPlaying())
        {
            return true;
        }
        if (loop)
        {
            Animations.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        }
        Animations.Play(name, blend);
        Clip = name;
        return true;
    }
}
