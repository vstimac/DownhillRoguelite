using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The restsite president standing at his podium (docs/restsite-president.md). E talks: his lines
/// play one after another (E skips a line), then choices show on the HUD (number keys). Nothing
/// stops the carrier: walking away closes the conversation. Between talks he now and then says an
/// idle line to a carrier nearby, at most once every IdleCooldown seconds. Built from simple shapes
/// unless Model is set; a model's AnimationPlayer gets IdleClip/TalkClip and an AnimationTree gets
/// TalkParameter (1 while talking), each only if present.
/// </summary>
public partial class PresidentNpc : Node3D, IInteractable, IDescribed
{
    public const string NpcId = "npc_restsite_president";

    [Export] public PackedScene? Model { get; set; }
    /// <summary>The model loaded when Model isn't set: Vučić (a Tripo model on a Mixamo rig). Empty: simple shapes.</summary>
    [Export(PropertyHint.File, "*.glb")] public string ModelPath { get; set; } = "res://art/characters/vucic/vucic.glb";
    [Export] public float ModelScale { get; set; } = 1.0f;
    /// <summary>Turn of the model about Y so it faces the carrier (front is +Z), in degrees.</summary>
    [Export] public float ModelYaw { get; set; } = 0.0f;
    /// <summary>
    /// A model with a rig but no clips of its own follows this hidden mannequin (RigFollower), which
    /// plays IdleClip and TalkClip from the Universal Animation Library.
    /// </summary>
    [Export(PropertyHint.File, "*.glb")] public string DriverPath { get; set; } = "res://art/characters/quaternius_ual/UAL_Mannequin.glb";
    [Export] public string IdleClip { get; set; } = "Idle";
    [Export] public string TalkClip { get; set; } = "Idle_Talking";
    [Export] public string TalkParameter { get; set; } = "parameters/talk/blend_amount";
    /// <summary>The conversation closes when the carrier is further than this, in m.</summary>
    [Export] public float LeaveDistance { get; set; } = 8.0f;
    /// <summary>Idle lines only to a carrier this close, in m, and at least this many seconds apart (random in the range).</summary>
    [Export] public float IdleRange { get; set; } = 14.0f;
    [Export] public Vector2 IdleCooldown { get; set; } = new(45.0f, 90.0f);
    [Export] public float TurnSpeed { get; set; } = 4.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    [Export] public Color SuitColor { get; set; } = new(0.12f, 0.13f, 0.17f);
    [Export] public Color ShirtColor { get; set; } = new(0.92f, 0.92f, 0.9f);
    [Export] public Color TieColor { get; set; } = new(0.55f, 0.08f, 0.1f);
    [Export] public Color SkinColor { get; set; } = new(0.86f, 0.7f, 0.6f);

    public NpcVoice? Voice { get; private set; }
    public PresidentDialogue? Dialogue { get; private set; }
    public TextTable? Text { get; private set; }
    public bool Talking { get; private set; }
    public bool ChoicesShown => _beat != null && _lineIndex >= _beat.Lines.Count && _beat.Options.Count > 0;
    public IReadOnlyList<DialogueOption> Options
    {
        get
        {
            if (_beat != null)
            {
                return _beat.Options;
            }
            return System.Array.Empty<DialogueOption>();
        }
    }
    public bool Grounded { get; private set; }
    public float IdleTimer { get; set; }

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 1.2f;
    public string Title => Text?.Get("npc_name") ?? "";
    public string Description => Text?.Get("npc_description") ?? "";
    public string Details => Text?.Get("npc_title") ?? "";
    public string Prompt
    {
        get
        {
            if (Voice != null && Voice.Speaking && Talking)
            {
                return Text?.Get("prompt_skip") ?? "";
            }
            return Text?.Get("prompt_talk") ?? "";
        }
    }
    public Resource? IconSource => null;

    private DialogueBeat? _beat;
    private int _lineIndex;
    private Node3D? _head;
    private AnimationPlayer? _animation;
    private AnimationTree? _tree;
    private readonly RandomNumberGenerator _random = new();
    private float _clock;

    public void Setup(VoiceManifest manifest, VoicePack? pack, string variant, TextTable text, PresidentDialogue dialogue)
    {
        Text = text;
        Dialogue = dialogue;
        Name = "RestsitePresident";
        Voice = new NpcVoice { Name = "Voice", NpcId = NpcId, SpeakerName = text.Get("npc_name"), Manifest = manifest, Pack = pack, Variant = variant, Position = Vector3.Up * 1.6f };
        AddChild(Voice);
    }

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        _random.Randomize();
        IdleTimer = _random.RandfRange(IdleCooldown.X * 0.3f, IdleCooldown.X);
        BuildBody();
        GameEvents.VoiceLineCompleted += OnLineCompleted;
        GameEvents.DialogueChoiceRequested += OnChoice;
    }

    public override void _ExitTree()
    {
        GameEvents.VoiceLineCompleted -= OnLineCompleted;
        GameEvents.DialogueChoiceRequested -= OnChoice;
        if (Talking)
        {
            Close();
        }
    }

    public void SetFocused(bool focused)
    {
    }

    public bool Interact(Node3D interactor)
    {
        if (Dialogue == null)
        {
            return false;
        }
        if (Talking)
        {
            if (Voice != null && Voice.Speaking)
            {
                Voice.Skip();
            }
            return true;
        }
        Run(Dialogue.Begin());
        return true;
    }

    /// <summary>Starts a conversation at a beat (the podium starts its own).</summary>
    public void Run(DialogueBeat? beat)
    {
        if (beat == null)
        {
            if (Talking)
            {
                Close();
            }
            return;
        }
        if (!Talking)
        {
            Talking = true;
            GameEvents.PublishDialogue(true);
        }
        _beat = beat;
        _lineIndex = -1;
        NextLine();
    }

    /// <summary>Picks a choice by index (the HUD's number keys do this through GameEvents).</summary>
    public void Choose(int index)
    {
        if (!Talking || _beat == null || !ChoicesShown || index < 0 || index >= _beat.Options.Count)
        {
            return;
        }
        DialogueOption option = _beat.Options[index];
        GameEvents.PublishDialogueChoices(Title, System.Array.Empty<string>());
        GD.Print($"[dialogue] {NpcId} choice {option.Id}");
        DialogueBeat? next = option.Next();
        if (next == null)
        {
            Close();
            return;
        }
        Run(next);
    }

    /// <summary>Ends the conversation (walked away, or nothing left to say). A line already spoken finishes.</summary>
    public void Close()
    {
        _beat = null;
        Talking = false;
        GameEvents.PublishDialogueChoices(Title, System.Array.Empty<string>());
        GameEvents.PublishDialogue(false);
    }

    private void NextLine()
    {
        if (_beat == null)
        {
            return;
        }
        _lineIndex++;
        while (_lineIndex < _beat.Lines.Count)
        {
            string id = _beat.Lines[_lineIndex];
            if (id.Length > 0 && Voice != null && Voice.Say(id))
            {
                return;
            }
            _lineIndex++;
        }
        DialogueBeat done = _beat;
        done.Done?.Invoke();
        if (_beat != done)
        {
            return;
        }
        if (done.Options.Count == 0)
        {
            Close();
            return;
        }
        string[] texts = new string[done.Options.Count];
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i] = done.Options[i].Text;
        }
        GameEvents.PublishDialogueChoices(Title, texts);
    }

    private void OnLineCompleted(string npcId, string lineId, bool skipped)
    {
        if (npcId != NpcId || Voice == null || lineId != Voice.LastFinished || !Talking || _beat == null)
        {
            return;
        }
        if (_lineIndex < _beat.Lines.Count && _beat.Lines[_lineIndex] == lineId)
        {
            NextLine();
        }
    }

    private void OnChoice(int index)
    {
        if (Talking)
        {
            Choose(index);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _clock += dt;
        if (!Grounded)
        {
            Settle();
        }
        Vector3 carrier = GameEvents.CarrierPosition;
        Vector3 flat = carrier - GlobalPosition;
        flat.Y = 0.0f;
        float distance = flat.Length();
        if (Talking && distance > LeaveDistance)
        {
            Close();
        }
        bool speaking = Voice != null && Voice.Speaking;
        if (distance < IdleRange && distance > 0.1f)
        {
            float target = Mathf.Atan2(flat.X, flat.Z);
            Rotation = new Vector3(0.0f, Mathf.LerpAngle(Rotation.Y, target, Mathf.Clamp(TurnSpeed * dt, 0.0f, 1.0f)), 0.0f);
        }
        Animate(speaking);
        IdleTimer -= dt;
        if (!Talking && !speaking && IdleTimer <= 0.0f && distance < IdleRange && !GameEvents.IsNarrating && !GameEvents.IsConversing && Dialogue != null)
        {
            string line = Dialogue.IdleLine();
            if (line.Length > 0)
            {
                Voice?.Say(line);
            }
            IdleTimer = _random.RandfRange(IdleCooldown.X, IdleCooldown.Y);
        }
    }

    private void Animate(bool speaking)
    {
        if (_animation != null)
        {
            string clip = IdleClip;
            if (speaking)
            {
                clip = TalkClip;
            }
            if (_animation.HasAnimation(clip) && _animation.CurrentAnimation != clip)
            {
                _animation.Play(clip, 0.25f);
            }
        }
        if (_tree != null && TalkParameter.Length > 0 && _tree.Get(TalkParameter).VariantType != Variant.Type.Nil)
        {
            float talk = 0.0f;
            if (speaking)
            {
                talk = 1.0f;
            }
            _tree.Set(TalkParameter, talk);
        }
        if (_head != null)
        {
            float nod = 0.0f;
            if (speaking)
            {
                nod = Mathf.Sin(_clock * 9.0f) * 0.06f;
            }
            _head.Rotation = new Vector3(nod, 0.0f, 0.0f);
        }
    }

    private void Settle()
    {
        PhysicsDirectSpaceState3D? space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return;
        }
        PhysicsRayQueryParameters3D ray = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 3.0f, GlobalPosition + Vector3.Down * 30.0f, GroundMask);
        Godot.Collections.Dictionary hit = space.IntersectRay(ray);
        if (hit.Count > 0)
        {
            GlobalPosition = (Vector3)hit["position"];
            Grounded = true;
        }
    }

    private void BuildBody()
    {
        if (Model == null && ModelPath.Length > 0 && ResourceLoader.Exists(ModelPath))
        {
            Model = ResourceLoader.Load<PackedScene>(ModelPath);
        }
        if (Model != null)
        {
            Node3D model = Model.Instantiate<Node3D>();
            model.Scale = Vector3.One * ModelScale;
            model.RotationDegrees = new Vector3(0.0f, ModelYaw, 0.0f);
            AddChild(model);
            Follow(model);
            foreach (Node node in model.FindChildren("*", "AnimationPlayer", true, false))
            {
                _animation = node as AnimationPlayer;
                break;
            }
            foreach (Node node in model.FindChildren("*", "AnimationTree", true, false))
            {
                _tree = node as AnimationTree;
                break;
            }
            return;
        }
        // A suit, a white shirt and a tie: a placeholder until a model exists (front is +Z).
        Node3D body = new() { Name = "Body" };
        AddChild(body);
        body.AddChild(Part(new CapsuleMesh { Radius = 0.26f, Height = 1.45f }, SuitColor, new Vector3(0.0f, 0.73f, 0.0f)));
        body.AddChild(Part(new BoxMesh { Size = new Vector3(0.16f, 0.36f, 0.02f) }, ShirtColor, new Vector3(0.0f, 1.18f, 0.25f)));
        body.AddChild(Part(new BoxMesh { Size = new Vector3(0.05f, 0.3f, 0.02f) }, TieColor, new Vector3(0.0f, 1.15f, 0.265f)));
        _head = new Node3D { Name = "Head", Position = new Vector3(0.0f, 1.55f, 0.0f) };
        body.AddChild(_head);
        _head.AddChild(Part(new SphereMesh { Radius = 0.15f, Height = 0.32f }, SkinColor, new Vector3(0.0f, 0.1f, 0.0f)));
    }

    /// <summary>A rigged model without clips follows the mannequin, whose AnimationPlayer then plays his clips.</summary>
    private void Follow(Node3D model)
    {
        if (model.FindChildren("*", "AnimationPlayer", true, false).Count > 0 || DriverPath.Length == 0)
        {
            return;
        }
        Skeleton3D? skeleton = null;
        foreach (Node node in model.FindChildren("*", "Skeleton3D", true, false))
        {
            skeleton = node as Skeleton3D;
            break;
        }
        if (skeleton == null || ResourceLoader.Load(DriverPath) is not PackedScene driverScene || driverScene.Instantiate() is not Node3D driver)
        {
            return;
        }
        driver.Name = "Driver";
        model.AddChild(driver);
        Skeleton3D? driverSkeleton = null;
        foreach (Node node in driver.FindChildren("*", "Skeleton3D", true, false))
        {
            driverSkeleton = node as Skeleton3D;
            break;
        }
        foreach (Node node in driver.FindChildren("*", "AnimationPlayer", true, false))
        {
            _animation = node as AnimationPlayer;
            break;
        }
        if (driverSkeleton == null)
        {
            return;
        }
        RigFollower follower = new() { Name = "RigFollower", Driver = driverSkeleton };
        follower.UseRigOf(skeleton);
        skeleton.AddChild(follower);
    }

    private static MeshInstance3D Part(Mesh mesh, Color color, Vector3 at)
    {
        return new MeshInstance3D
        {
            Mesh = mesh,
            Position = at,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.8f },
        };
    }
}
