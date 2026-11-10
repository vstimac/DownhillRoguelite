using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>Compact expedition brief, with measured wrapping; hidden behind menus and inventory.</summary>
public partial class ExpeditionCard : CanvasLayer
{
    private ParchmentPanel? _card;
    private Label? _title;
    private Label? _body;
    private Label? _progress;
    private Label? _distance;
    private ExpeditionNote? _note;

    public override void _Ready()
    {
        Layer = 8;
        ProcessMode = ProcessModeEnum.Always;
        _card = new ParchmentPanel { Seed = 73, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_card);
        _title = Make(23, true);
        _body = Make(18, false);
        _progress = Make(17, false);
        _distance = Make(17, false);
        _note = GameEvents.CurrentExpedition;
        GameEvents.ExpeditionChanged += OnChanged;
    }
    private Label Make(int size, bool heading)
    {
        Label label = UiStyle.MakeLabel("", size, heading);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        _card!.AddChild(label);
        return label;
    }
    public override void _ExitTree() { GameEvents.ExpeditionChanged -= OnChanged; }
    private void OnChanged(ExpeditionNote? note) { _note = note; }

    public override void _Process(double delta)
    {
        if (_card == null || _title == null || _body == null || _progress == null || _distance == null) { return; }
        _card.Visible = _note != null && !GetTree().Paused && !GameEvents.IsScreenOpen && !GameEvents.IsMapOpen && !GameEvents.IsPacking;
        if (!_card.Visible || _note is not ExpeditionNote note) { return; }
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        float scale = Mathf.Clamp(GameOptions.UiScale, 0.75f, 1.5f);
        float width = Mathf.Min(390, screen.X / scale - 40);
        float inner = width - 32;
        float y = Place(_title, note.Title, inner, 14, 23, true);
        y = Place(_body, note.Instructions, inner, y + 6, 18, false);
        y = Place(_progress, note.Progress, inner, y + 8, 17, false);
        Vector3 offset = note.Target - GameEvents.CarrierPosition;
        string bearing = "left of you";
        if (offset.X >= 0) { bearing = "right of you"; }
        y = Place(_distance, Mathf.RoundToInt(new Vector2(offset.X, offset.Z).Length()) + " m away · " + bearing, inner, y + 6, 17, false);
        _card.Size = new Vector2(width, y + 14);
        _card.Scale = Vector2.One * scale;
        _card.Position = new Vector2(screen.X - width * scale - 20, Mathf.Max(20, screen.Y - (y + 14 + 160) * scale));
    }
    private static float Place(Label label, string text, float width, float y, int size, bool heading)
    {
        float height = UiStyle.Wrapped(text, size, width, heading).Y + 4;
        label.Text = text;
        label.Position = new Vector2(16, y);
        label.Size = new Vector2(width, height);
        return y + height;
    }
}
