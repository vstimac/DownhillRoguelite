using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// A found text standing in the world: a broken sign, a page on a crate, a ledger, words cut into a
/// stone. From a distance its heading shows; up close (focused) its words do. E reads it: the
/// journal copies it and the lore book remembers it across runs. Reading changes nothing else.
/// Models come from the Kenney graveyard and survival kits; pages and books are primitives.
/// </summary>
public partial class LoreMark : Node3D, IInteractable, IDescribed
{
    [Export(PropertyHint.File, "*.glb")] public string SignModel { get; set; } = "res://art/items/kenney_survival_kit/GLB format/signpost-single.glb";
    [Export(PropertyHint.File, "*.glb")] public string NoteModel { get; set; } = "res://art/items/kenney_survival_kit/GLB format/box.glb";
    [Export(PropertyHint.File, "*.glb")] public string LedgerModel { get; set; } = "res://art/props/kenney_graveyard_kit/GLB format/altar-wood.glb";
    [Export(PropertyHint.File, "*.glb")] public string StoneModel { get; set; } = "res://art/props/kenney_graveyard_kit/GLB format/gravestone-wide.glb";
    [Export(PropertyHint.File, "*.glb")] public string LetterModel { get; set; } = "res://art/items/kenney_survival_kit/GLB format/bedroll-packed.glb";
    [Export(PropertyHint.File, "*.glb")] public string CairnModel { get; set; } = "res://art/props/kenney_graveyard_kit/GLB format/rocks-tall.glb";
    [Export(PropertyHint.File, "*.glb")] public string CandleModel { get; set; } = "res://art/props/kenney_graveyard_kit/GLB format/candle.glb";
    [Export] public Color PaperColor { get; set; } = new(0.9f, 0.84f, 0.68f);
    [Export] public Color ReadColor { get; set; } = new(0.7f, 0.66f, 0.56f);
    [Export] public Color CandleColor { get; set; } = new(1.0f, 0.75f, 0.4f);
    [Export] public float HeadingRange { get; set; } = 22.0f;
    [Export] public float TextRange { get; set; } = 8.0f;
    /// <summary>A sign leans this far (it has been knocked about), in degrees.</summary>
    [Export] public float SignLean { get; set; } = 11.0f;

    public LoreEntry? Entry { get; private set; }
    public bool HasBeenRead { get; private set; }

    private RunState? _run;
    private Label3D? _heading;
    private Label3D? _text;
    private float _top = 1.2f;

    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * Mathf.Min(_top, 1.2f);

    public string Title
    {
        get
        {
            if (Entry == null)
            {
                return "";
            }
            return Entry.Heading;
        }
    }

    public string Description
    {
        get
        {
            if (Entry == null)
            {
                return "";
            }
            return "\"" + Entry.Text + "\"";
        }
    }

    public string Details
    {
        get
        {
            if (Entry == null || _run == null)
            {
                return "";
            }
            if (_run.Lore.HasEverRead(Entry.Id))
            {
                return "You have read this before. It's in your journal.";
            }
            return "Read it to copy it into your journal. What you read stays with you across runs.";
        }
    }

    public string Prompt
    {
        get
        {
            if (HasBeenRead)
            {
                return "";
            }
            return "E  Read";
        }
    }

    public Resource? IconSource => null;

    public void Setup(LoreEntry entry)
    {
        Entry = entry;
        Name = "Lore_" + entry.Id;
    }

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        AddToGroup(IInteractable.Group);
        if (Entry == null)
        {
            return;
        }
        BuildModel(Entry.Form);
        _heading = Label(34, _top + 0.45f, HeadingRange);
        _heading.Text = Entry.Heading;
        _text = Label(26, _top + 0.8f, TextRange);
        _text.Text = Entry.Text;
        _text.Visible = false;
        if (_run != null && _run.Lore.ReadThisRun(Entry.Id))
        {
            MarkRead();
        }
    }

    private void BuildModel(LoreForm form)
    {
        if (form == LoreForm.Sign)
        {
            _top = AddModel(SignModel, 1.7f, new Vector3(0.0f, 0.0f, SignLean), Vector3.Zero);
            AddPage(new Vector3(0.0f, 1.2f, 0.07f), new Vector3(0.0f, 0.0f, -6.0f));
        }
        else if (form == LoreForm.Ledger)
        {
            _top = AddModel(LedgerModel, 0.9f, Vector3.Zero, Vector3.Zero);
            AddBook(new Vector3(0.0f, _top + 0.03f, 0.0f));
            AddCandle(new Vector3(0.35f, _top, 0.15f));
        }
        else if (form == LoreForm.Stone)
        {
            _top = AddModel(StoneModel, 1.25f, new Vector3(0.0f, 8.0f, 3.0f), Vector3.Zero);
            AddCandle(new Vector3(0.45f, 0.0f, 0.45f));
        }
        else if (form == LoreForm.Letter)
        {
            _top = AddModel(LetterModel, 0.4f, new Vector3(0.0f, 25.0f, 0.0f), Vector3.Zero);
            AddPage(new Vector3(0.05f, _top + 0.01f, 0.0f), new Vector3(-90.0f, 20.0f, 0.0f));
        }
        else if (form == LoreForm.Cairn)
        {
            _top = AddModel(CairnModel, 1.1f, Vector3.Zero, Vector3.Zero);
            AddPage(new Vector3(0.0f, _top * 0.6f, 0.32f), new Vector3(-10.0f, 0.0f, 5.0f));
        }
        else
        {
            _top = AddModel(NoteModel, 0.5f, new Vector3(0.0f, 12.0f, 0.0f), Vector3.Zero);
            AddPage(new Vector3(0.0f, _top + 0.01f, 0.0f), new Vector3(-90.0f, -14.0f, 0.0f));
        }
        if (_top <= 0.05f)
        {
            // No model (a missing file): a plain post, so the text still stands somewhere.
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 1.2f, 0.12f) }, Position = Vector3.Up * 0.6f });
            AddPage(new Vector3(0.0f, 1.0f, 0.07f), Vector3.Zero);
            _top = 1.2f;
        }
    }

    /// <summary>Places a model scaled to <paramref name="height"/> on the ground here; returns its height (0 when missing).</summary>
    private float AddModel(string path, float height, Vector3 rotation, Vector3 at)
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene)
        {
            return 0.0f;
        }
        Node3D holder = new() { Name = "Model" };
        holder.AddChild(scene.Instantiate<Node3D>());
        Aabb bounds = ModelFit.Bounds(holder);
        float scale = height / Mathf.Max(bounds.Size.Y, 0.01f);
        holder.Transform = ModelFit.FitOnGround(bounds, bounds.Size * scale, true);
        Node3D place = new() { Position = at, RotationDegrees = rotation };
        place.AddChild(holder);
        AddChild(place);
        return height;
    }

    private void AddPage(Vector3 at, Vector3 rotation)
    {
        AddChild(new MeshInstance3D
        {
            Name = "Page",
            Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.3f, 0.006f) },
            Position = at,
            RotationDegrees = rotation,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = PaperColor, Roughness = 1.0f },
        });
    }

    private void AddBook(Vector3 at)
    {
        StandardMaterial3D cover = new() { AlbedoColor = new Color(0.35f, 0.18f, 0.12f), Roughness = 1.0f };
        AddChild(new MeshInstance3D { Name = "Book", Mesh = new BoxMesh { Size = new Vector3(0.44f, 0.04f, 0.3f) }, Position = at, MaterialOverride = cover });
        AddPage(at + new Vector3(0.0f, 0.025f, 0.0f), new Vector3(-90.0f, 0.0f, 0.0f));
    }

    private void AddCandle(Vector3 at)
    {
        float height = AddModel(CandleModel, 0.22f, Vector3.Zero, at);
        if (height <= 0.0f)
        {
            return;
        }
        AddChild(new OmniLight3D { LightColor = CandleColor, LightEnergy = 0.6f, OmniRange = 3.0f, Position = at + Vector3.Up * 0.35f });
    }

    private Label3D Label(int size, float height, float range)
    {
        Label3D label = new()
        {
            FontSize = size,
            PixelSize = 0.005f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 8,
            Position = Vector3.Up * height,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Width = 620.0f,
            Modulate = PaperColor,
        };
        AddChild(WorldLabel.Readable(label, range));
        return label;
    }

    public void SetFocused(bool focused)
    {
        if (_text != null)
        {
            _text.Visible = focused;
        }
    }

    public bool Interact(Node3D interactor)
    {
        if (Entry == null || _run == null || HasBeenRead)
        {
            return false;
        }
        _run.Lore.Read(Entry.Id, out bool firstThisRun, out bool firstEver);
        MarkRead();
        GameEvents.PublishLoreRead(new LoreNote(Entry.Id, Entry.Heading, Entry.Text, firstThisRun, firstEver));
        if (ReferenceEquals(GameEvents.Focused, this))
        {
            GameEvents.PublishFocus(this);
        }
        return true;
    }

    private void MarkRead()
    {
        HasBeenRead = true;
        if (_heading != null)
        {
            _heading.Modulate = ReadColor;
        }
    }
}
