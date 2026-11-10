using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// A woman selling yesterday's newspapers on the route early in a run, and begging. E on her starts it:
/// from then on she walks after the carrier (walking, jogging to catch up, turning up behind them if
/// they get far ahead) and keeps asking, until they give her a water bottle or something else she
/// wants (E again: the inventory uses up one carried item through GameEvents.RequestItemUse). Strike
/// her and she screams: two enemies come out for the carrier (GameEvents.RequestAmbush) and she runs off.
///
/// Her model (a Tripo/Mixamo-rigged woman, art/characters/npcs/newspaper_seller) has no clips of its own:
/// a hidden Universal Animation Library mannequin plays idle, talking, walk and jog, and RigFollower
/// copies its pose onto her, as for the humanoid enemies. A new run puts her back on the route.
/// Debug scene: debug/NewspaperSellerLab.tscn; test: debug/tests/NewspaperSellerTests.tscn.
/// </summary>
public partial class NewspaperSeller : Node3D, IInteractable, IDescribed, IMeleeTarget
{
    public enum Mood
    {
        Selling,
        Begging,
        Given,
        Struck,
    }

    [ExportGroup("Model")]
    [Export(PropertyHint.File, "*.glb,*.fbx")] public string ModelPath { get; set; } = "res://art/characters/npcs/newspaper_seller/newspaper_seller.glb";
    [Export(PropertyHint.File, "*.glb")] public string RigPath { get; set; } = "res://art/characters/quaternius_ual/UAL_Mannequin.glb";
    /// <summary>How tall she stands in the world, how tall the model is as imported, and the mannequin's own height (m).</summary>
    [Export] public float Height { get; set; } = 1.62f;
    [Export] public float ModelHeight { get; set; } = 0.98f;
    [Export] public float DriverHeight { get; set; } = 1.83f;
    /// <summary>Extra turn of the model (degrees) if it doesn't face +Z at rest.</summary>
    [Export] public float ModelYaw { get; set; }

    [ExportGroup("Clips")]
    [Export] public string IdleClip { get; set; } = "Idle";
    [Export] public string BegClip { get; set; } = "Idle_Talking";
    [Export] public string WalkClip { get; set; } = "Walk";
    [Export] public string JogClip { get; set; } = "Jog_Fwd";
    [Export] public string HurtClip { get; set; } = "Hit_Chest";
    /// <summary>How fast the mannequin's walk and jog clips move (m/s at its own height), so her feet don't slide.</summary>
    [Export] public float WalkClipSpeed { get; set; } = 1.35f;
    [Export] public float JogClipSpeed { get; set; } = 3.3f;
    [Export] public float ClipBlend { get; set; } = 0.25f;

    [ExportGroup("Following")]
    [Export] public float WalkSpeed { get; set; } = 1.5f;
    [Export] public float JogSpeed { get; set; } = 3.6f;
    /// <summary>m/s² she speeds up and slows down by.</summary>
    [Export] public float Acceleration { get; set; } = 4.0f;
    /// <summary>She stops this close to the carrier (inside their reach, so E finds her), and jogs beyond JogDistance (m).</summary>
    [Export] public float FollowDistance { get; set; } = 1.7f;
    [Export] public float JogDistance { get; set; } = 7.0f;
    /// <summary>Once stopped, she sets off again when the carrier is this much further than FollowDistance (m).</summary>
    [Export] public float FollowSlack { get; set; } = 0.6f;
    /// <summary>Left further behind than this, she turns up CatchUpBehind metres behind the carrier.</summary>
    [Export] public float CatchUpDistance { get; set; } = 45.0f;
    [Export] public float CatchUpBehind { get; set; } = 10.0f;
    /// <summary>How fast she turns to where she walks (per second, as a blend rate).</summary>
    [Export] public float TurnRate { get; set; } = 6.0f;
    /// <summary>While waiting she turns to the carrier when they come this close (m).</summary>
    [Export] public float NoticeDistance { get; set; } = 10.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    [ExportGroup("Placement")]
    /// <summary>Stand on the route at the start of each run (off for debug scenes, which place her).</summary>
    [Export] public bool PlaceOnRoute { get; set; } = true;
    /// <summary>How far down the main route from the start (m along -Z), and how far to its side (m, +X).</summary>
    [Export] public float RouteDistance { get; set; } = 45.0f;
    [Export] public float RouteSide { get; set; } = 3.0f;
    /// <summary>RouteDistance with `-- --seller-near` (rendered checks).</summary>
    [Export] public float NearRouteDistance { get; set; } = 9.0f;

    [ExportGroup("Giving and striking")]
    /// <summary>What she takes, the first carried one in this order: water first, then food.</summary>
    [Export] public string[] AcceptedItems { get; set; } =
    {
        "water_bottle", "water_flask", "water_skin", "ration", "strategic_sandwich", "dried_fruit", "food_bag", "wild_berries",
    };
    /// <summary>How many enemies come when she is struck.</summary>
    [Export] public int AmbushCount { get; set; } = 2;
    [Export] public float FleeSpeed { get; set; } = 4.2f;
    /// <summary>She runs off this long after being struck, then is gone for the run.</summary>
    [Export] public float FleeSeconds { get; set; } = 6.0f;
    /// <summary>Her body for the sword (a standing capsule).</summary>
    [Export] public float BodyRadius { get; set; } = 0.3f;

    [ExportGroup("Newspapers")]
    /// <summary>The bundle she carries: the hand bone (Mixamo name, prefix optional), its size and place in the hand's space (m), turn and colour.</summary>
    [Export] public string NewspaperBone { get; set; } = "mixamorig1_LeftHand";
    [Export] public Vector3 NewspaperSize { get; set; } = new(0.06f, 0.30f, 0.22f);
    [Export] public Vector3 NewspaperOffset { get; set; } = new(0.0f, 0.10f, 0.04f);
    [Export] public Vector3 NewspaperTurn { get; set; } = Vector3.Zero;
    [Export] public Color NewspaperColor { get; set; } = new(0.82f, 0.8f, 0.72f);

    [ExportGroup("Words")]
    [Export] public string SellerName { get; set; } = "Newspaper seller";
    [Export(PropertyHint.MultilineText)] public string Blurb { get; set; } = "A thin woman with a bundle of damp newspapers under her arm. The headlines are a week old.";
    [Export] public string GreetLine { get; set; } = "Paper, love? Last week's news, still true. No coin? Then water. I'll walk with you till you remember you have some.";
    [Export] public string[] BegLines { get; set; } =
    {
        "Just a sip. You've got plenty, I can hear it slosh.",
        "I'm not following you. We're going the same way.",
        "Bread, water, a button. Anything worth something.",
        "The mountain takes from everyone. Give to me first.",
    };
    [Export] public string NothingLine { get; set; } = "Nothing? Then I'll keep walking.";
    [Export] public string ThanksLine { get; set; } = "Bless you. Here, take a paper. Read it slowly, it's all bad.";
    [Export] public string StruckLine { get; set; } = "Help! Somebody help me!";
    /// <summary>Seconds between her begging lines while she follows, and how long a line stays up.</summary>
    [Export] public float BegInterval { get; set; } = 22.0f;
    [Export] public float LineSeconds { get; set; } = 4.5f;

    public Mood State { get; private set; } = Mood.Selling;
    public bool Following => State == Mood.Begging;
    /// <summary>How fast she walks now (m/s, on the ground plane).</summary>
    public float Speed { get; private set; }
    public string Clip { get; private set; } = "";
    public AnimationPlayer? Animations { get; private set; }
    public RigFollower? Follower { get; private set; }
    /// <summary>The item she was last given (its id), or empty.</summary>
    public string Received { get; private set; } = "";
    public bool Placed { get; private set; }

    // IDescribed
    public string Title => SellerName;
    public string Description => Blurb;
    public string Details
    {
        get
        {
            if (State == Mood.Begging)
            {
                return "Follows you until you give her water or something else of value.";
            }
            if (State == Mood.Given)
            {
                return "You gave her " + ItemName(Received) + ".";
            }
            return "Sells newspapers. Begs, too.";
        }
    }
    public string Prompt
    {
        get
        {
            if (State == Mood.Selling)
            {
                return "E  Buy a paper";
            }
            if (State == Mood.Begging)
            {
                string offer = Offer();
                if (offer.Length > 0)
                {
                    return "E  Give her your " + ItemName(offer).ToLowerInvariant();
                }
                return "She wants water or food. You have none";
            }
            if (State == Mood.Given)
            {
                return "E  Talk";
            }
            return "";
        }
    }
    public Resource? IconSource => null;

    // IInteractable
    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * Height * 0.6f;

    // IMeleeTarget
    public Vector3 HitBase => GlobalPosition;
    public float HitHeight => Height;
    public float HitRadius => BodyRadius;
    public bool CanBeHit => State != Mood.Struck && Visible;
    public bool Lockable => false;

    private Node3D? _visual;
    private RunState? _run;
    private float _begTimer;
    private int _begLine;
    private float _fleeTimer;
    private Vector3 _fleeFrom;
    private float _hurtTimer;
    private bool _walking;
    private PhysicsRayQueryParameters3D? _ray;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        // Rendered checks: `-- --seller-near` puts her just below the start, in the first view.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--seller-near") >= 0)
        {
            RouteDistance = NearRouteDistance;
        }
        AddToGroup(IInteractable.Group);
        AddToGroup(IMeleeTarget.Group);
        BuildVisual();
        Play(IdleClip, 0.0f, 1.0f);
        GameEvents.RespawnRequested += OnNewRun;
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= OnNewRun;
    }

    /// <summary>The hidden mannequin that plays the clips, and her model following it bone for bone.</summary>
    private void BuildVisual()
    {
        _visual = new Node3D { Name = "Visual", RotationDegrees = new Vector3(0.0f, 180.0f + ModelYaw, 0.0f) };
        AddChild(_visual);
        Skeleton3D? driver = null;
        if (ResourceLoader.Load(RigPath) is PackedScene rigScene && rigScene.Instantiate() is Node3D rig)
        {
            rig.Name = "Rig";
            rig.Scale = Vector3.One * (Height / Mathf.Max(DriverHeight, 0.1f));
            _visual.AddChild(rig);
            driver = FindFirst<Skeleton3D>(rig);
            Animations = FindFirst<AnimationPlayer>(rig);
        }
        if (ResourceLoader.Load(ModelPath) is not PackedScene modelScene || modelScene.Instantiate() is not Node3D model)
        {
            GD.PushWarning($"NewspaperSeller: can't load {ModelPath}.");
            return;
        }
        model.Name = "Model";
        model.Scale = Vector3.One * (Height / Mathf.Max(ModelHeight, 0.01f));
        _visual.AddChild(model);
        Skeleton3D? body = FindFirst<Skeleton3D>(model);
        if (body == null || driver == null)
        {
            GD.PushWarning($"NewspaperSeller: {ModelPath} or the rig has no skeleton; she stands still.");
            return;
        }
        Follower = new RigFollower { Name = "RigFollower", Driver = driver };
        Follower.UseRigOf(body);
        body.AddChild(Follower);
        AddNewspapers(body);
    }

    /// <summary>A bundle of folded newspapers held in her hand (follows the hand bone).</summary>
    private void AddNewspapers(Skeleton3D body)
    {
        int bone = RigFollower.FindBone(body, NewspaperBone);
        if (bone < 0 || NewspaperSize == Vector3.Zero)
        {
            return;
        }
        BoneAttachment3D hand = new() { Name = "NewspaperHand", BoneName = body.GetBoneName(bone) };
        body.AddChild(hand);
        // The model (and its armature inside) is scaled; the bundle keeps its size in metres.
        float scale = Mathf.Max(body.GlobalBasis.Scale.X / Mathf.Max(GlobalBasis.Scale.X, 0.0001f), 0.0001f);
        StandardMaterial3D paper = new() { AlbedoColor = NewspaperColor, Roughness = 1.0f };
        MeshInstance3D bundle = new()
        {
            Name = "Newspapers",
            Mesh = new BoxMesh { Size = NewspaperSize / scale },
            Position = NewspaperOffset / scale,
            RotationDegrees = NewspaperTurn,
            MaterialOverride = paper,
        };
        hand.AddChild(bundle);
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        foreach (Node node in root.FindChildren("*", typeof(T).Name, true, false))
        {
            if (node is T found)
            {
                return found;
            }
        }
        return null;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!Placed && PlaceOnRoute)
        {
            PlaceOnTheRoute();
        }
        if (!Visible)
        {
            return;
        }
        if (_hurtTimer > 0.0f)
        {
            _hurtTimer -= dt;
        }
        Vector3 carrier = GameEvents.CarrierPosition;
        Vector3 toCarrier = new(carrier.X - GlobalPosition.X, 0.0f, carrier.Z - GlobalPosition.Z);
        float distance = toCarrier.Length();
        float wanted = 0.0f;
        Vector3 heading = Vector3.Zero;
        if (State == Mood.Begging)
        {
            if (distance > CatchUpDistance)
            {
                CatchUp(carrier, toCarrier);
                return;
            }
            // She sets off once the carrier is FollowSlack beyond where she stops, and stops there.
            if (distance > FollowDistance + FollowSlack)
            {
                _walking = true;
            }
            else if (distance <= FollowDistance + 0.02f)
            {
                _walking = false;
            }
            if (distance > JogDistance)
            {
                wanted = JogSpeed;
            }
            else if (_walking)
            {
                wanted = WalkSpeed;
            }
            else
            {
                Speed = 0.0f;
            }
            if (distance > 0.01f)
            {
                heading = toCarrier / distance;
            }
            _begTimer -= dt;
            if (_begTimer <= 0.0f && BegLines.Length > 0)
            {
                _begTimer = BegInterval;
                Say(BegLines[_begLine % BegLines.Length]);
                _begLine++;
            }
        }
        else if (State == Mood.Struck)
        {
            _fleeTimer -= dt;
            Vector3 away = new(GlobalPosition.X - _fleeFrom.X, 0.0f, GlobalPosition.Z - _fleeFrom.Z);
            if (away.LengthSquared() > 0.0001f)
            {
                heading = away.Normalized();
            }
            else
            {
                heading = Vector3.Back;
            }
            if (_hurtTimer <= 0.0f)
            {
                wanted = FleeSpeed;
            }
            if (_fleeTimer <= 0.0f)
            {
                Gone();
                return;
            }
        }
        Speed = Mathf.MoveToward(Speed, wanted, Acceleration * dt);
        if (Speed > 0.01f && heading != Vector3.Zero)
        {
            Vector3 step = heading * Speed * dt;
            if (State == Mood.Begging)
            {
                // Never step past the spot where she stops.
                step = step.LimitLength(Mathf.Max(distance - FollowDistance, 0.0f));
            }
            GlobalPosition += step;
            Settle(3.0f, 8.0f);
            Face(heading, dt);
        }
        else if (State != Mood.Struck && distance < NoticeDistance && distance > 0.01f)
        {
            Face(toCarrier / distance, dt);
        }
        Animate();
    }

    /// <summary>Left far behind: she turns up behind the carrier, on the side she was on.</summary>
    private void CatchUp(Vector3 carrier, Vector3 toCarrier)
    {
        Vector3 back = -toCarrier.Normalized();
        GlobalPosition = new Vector3(carrier.X, carrier.Y, carrier.Z) + back * CatchUpBehind;
        Settle(60.0f, 120.0f);
        Speed = WalkSpeed;
        Rotation = new Vector3(0.0f, Mathf.Atan2(back.X, back.Z), 0.0f);
    }

    /// <summary>Turns her front (-Z) toward a flat direction.</summary>
    private void Face(Vector3 direction, float dt)
    {
        float target = Mathf.Atan2(-direction.X, -direction.Z);
        float yaw = Mathf.LerpAngle(Rotation.Y, target, Mathf.Clamp(TurnRate * dt, 0.0f, 1.0f));
        Rotation = new Vector3(0.0f, yaw, 0.0f);
    }

    /// <summary>Puts her feet on the ground under her (from <paramref name="above"/> m above to <paramref name="below"/> m below).</summary>
    private bool Settle(float above, float below)
    {
        if (!IsInsideTree())
        {
            return false;
        }
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = GroundMask };
        _ray.From = GlobalPosition + Vector3.Up * above;
        _ray.To = GlobalPosition + Vector3.Down * below;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        if (hit.Count == 0)
        {
            return false;
        }
        GlobalPosition = (Vector3)hit["position"];
        return true;
    }

    /// <summary>On the main route a little below the start, beside it, facing up toward the carrier coming down.</summary>
    private void PlaceOnTheRoute()
    {
        IRouteQuery? route = _run?.Route;
        if (route == null)
        {
            return;
        }
        Vector3 at = route.RoutePoint(route.StartZ - RouteDistance) + Vector3.Right * RouteSide;
        at.Y = route.GroundHeight(at.X, at.Z);
        GlobalPosition = at;
        Settle(20.0f, 40.0f);
        Rotation = new Vector3(0.0f, Mathf.Pi, 0.0f);
        Placed = true;
    }

    /// <summary>Idle (or begging) standing, walk or jog by how fast she really moves, sped up to match.</summary>
    private void Animate()
    {
        if (_hurtTimer > 0.0f)
        {
            return;
        }
        float stride = Height / Mathf.Max(DriverHeight, 0.1f);
        if (Speed < 0.1f)
        {
            if (State == Mood.Begging)
            {
                Play(BegClip, ClipBlend, 1.0f);
            }
            else
            {
                Play(IdleClip, ClipBlend, 1.0f);
            }
            return;
        }
        string clip = WalkClip;
        float clipSpeed = WalkClipSpeed * stride;
        if (Speed > (WalkClipSpeed + JogClipSpeed) * 0.5f * stride)
        {
            clip = JogClip;
            clipSpeed = JogClipSpeed * stride;
        }
        Play(clip, ClipBlend, Mathf.Clamp(Speed / Mathf.Max(clipSpeed, 0.01f), 0.5f, 2.0f));
    }

    /// <summary>Plays a looping clip on the mannequin (the "_Loop" suffix is optional).</summary>
    private void Play(string clip, float blend, float speed, bool once = false)
    {
        if (Animations == null)
        {
            return;
        }
        string name = clip;
        if (!Animations.HasAnimation(name))
        {
            name = clip + "_Loop";
        }
        if (!Animations.HasAnimation(name))
        {
            return;
        }
        Animations.SpeedScale = speed;
        if (Clip == name && Animations.IsPlaying())
        {
            return;
        }
        if (!once)
        {
            Animations.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        }
        Clip = name;
        Animations.Play(name, blend);
    }

    // --- E: begging and giving ---

    public void SetFocused(bool focused)
    {
    }

    public bool Interact(Node3D interactor)
    {
        if (State == Mood.Selling)
        {
            State = Mood.Begging;
            _begTimer = BegInterval;
            Say(GreetLine);
            GameEvents.PublishBeggarChanged(SellerName, "begging");
            return true;
        }
        if (State == Mood.Begging)
        {
            return Give();
        }
        if (State == Mood.Given)
        {
            Say(ThanksLine);
            return true;
        }
        return false;
    }

    /// <summary>Gives her the first carried item she takes; she stops following. False (and a sigh) with nothing to give.</summary>
    public bool Give()
    {
        string offer = Offer();
        if (offer.Length == 0)
        {
            Say(NothingLine);
            return false;
        }
        int before = Count(offer);
        GameEvents.RequestItemUse(offer);
        if (Count(offer) >= before)
        {
            // Nothing took it out of the pack (no inventory listening): she keeps asking.
            Say(NothingLine);
            return false;
        }
        Received = offer;
        State = Mood.Given;
        Speed = 0.0f;
        Say(ThanksLine);
        GameEvents.PublishHint("You gave the newspaper seller your " + ItemName(offer).ToLowerInvariant() + ". She stops following you.");
        GameEvents.PublishBeggarChanged(SellerName, "given");
        return true;
    }

    /// <summary>The first item the carrier has that she takes (AcceptedItems order), or empty.</summary>
    public string Offer()
    {
        foreach (string id in AcceptedItems)
        {
            if (Count(id) > 0)
            {
                return id;
            }
        }
        return "";
    }

    private static int Count(string id)
    {
        int count = 0;
        foreach (string carried in GameEvents.CarriedItems)
        {
            if (carried == id)
            {
                count++;
            }
        }
        return count;
    }

    private static string ItemName(string id)
    {
        if (id.Length == 0)
        {
            return "nothing";
        }
        return ItemText.Find(id)?.DisplayName ?? id;
    }

    // --- The sword ---

    public void TakeHit(float damage, Vector3 push, string source)
    {
        if (State == Mood.Struck)
        {
            return;
        }
        State = Mood.Struck;
        _fleeFrom = GameEvents.CarrierPosition;
        _fleeTimer = FleeSeconds;
        Speed = 0.0f;
        Say(StruckLine);
        if (Animations != null && Animations.HasAnimation(HurtClip))
        {
            Play(HurtClip, ClipBlend * 0.4f, 1.0f, true);
            _hurtTimer = (float)Animations.GetAnimation(HurtClip).Length;
        }
        GlobalPosition += new Vector3(push.X, 0.0f, push.Z) * 0.15f;
        RemoveFromGroup(IInteractable.Group);
        GameEvents.RequestAmbush(GlobalPosition, AmbushCount, "newspaper_seller");
        GameEvents.PublishBeggarChanged(SellerName, "struck");
    }

    /// <summary>Ran off: gone for the rest of the run.</summary>
    private void Gone()
    {
        Visible = false;
        Speed = 0.0f;
        RemoveFromGroup(IMeleeTarget.Group);
    }

    private void Say(string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        GameEvents.PublishSpeech(new SpeechLine(SellerName, text, GlobalPosition + Vector3.Up * Height, LineSeconds));
    }

    /// <summary>A new run: back to selling, on the new mountain's route.</summary>
    private void OnNewRun()
    {
        State = Mood.Selling;
        Received = "";
        Speed = 0.0f;
        _fleeTimer = 0.0f;
        _hurtTimer = 0.0f;
        Visible = true;
        if (!IsInGroup(IInteractable.Group))
        {
            AddToGroup(IInteractable.Group);
        }
        if (!IsInGroup(IMeleeTarget.Group))
        {
            AddToGroup(IMeleeTarget.Group);
        }
        if (PlaceOnRoute)
        {
            Placed = false;
        }
        Play(IdleClip, 0.0f, 1.0f);
    }
}
