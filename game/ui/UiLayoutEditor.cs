using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Lets the player move the UI around. Pages (inventory, map, journal, conversation) drag by their
/// title strip whenever they're open. Holding Alt frees the mouse and shows every HUD piece (dials,
/// descent note, compass, equipment, hotbar, tooltip, notes) with a dashed frame: drag one to move
/// it; Alt+Backspace puts everything back. Moved pieces are kept on screen when the window size
/// changes and remembered between sessions (user://ui_layout.cfg; not in headless runs, so tests see
/// the default layout). Presentation only: the Hud registers the pieces and lays out the defaults,
/// then this applies the player's own positions on top.
/// </summary>
public partial class UiLayoutEditor : Control
{
    [Export] public string SavePath { get; set; } = "user://ui_layout.cfg";
    /// <summary>How tall the grab strip at the top of a page is, in px (the close button is left free).</summary>
    [Export] public float HeaderGrab { get; set; } = 52.0f;
    [Export] public float CloseButtonSpace { get; set; } = 56.0f;
    /// <summary>At least this much of a moved piece stays on screen, in px.</summary>
    [Export] public float KeepVisible { get; set; } = 48.0f;

    /// <summary>Raised when Alt layout editing starts (true) or ends.</summary>
    public event System.Action<bool>? EditingChanged;

    public bool Editing { get; private set; }
    public IReadOnlyDictionary<string, Vector2> Moved => _moved;
    /// <summary>How the Hud wants a piece placed (the tooltip keeps a home position); default sets Position.</summary>
    public System.Action<Control, Vector2>? Place { get; set; }

    private readonly List<(string Name, Control Piece)> _pieces = new();
    private readonly List<(string Name, ParchmentPanel Page)> _pages = new();
    private readonly Dictionary<string, Vector2> _moved = new();
    private Control? _dragging;
    private string _dragName = "";
    private Vector2 _grab;
    private bool _persist;
    private bool _ownsScreen;

    /// <summary>
    /// True while the only "screen" open is Alt layout editing. Alt+key debug spawns (Tuntun, cart,
    /// enemies) are pressed with Alt held, so they must not count this as a screen.
    /// </summary>
    public static bool OnlyAltEditing { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        // `-- --default-layout` leaves the saved layout alone and unread (rendered checks of the default HUD).
        _persist = DisplayServer.GetName() != "headless" && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--default-layout") < 0;
        Load();
    }

    public void AddPiece(string name, Control? piece)
    {
        if (piece != null)
        {
            _pieces.Add((name, piece));
        }
    }

    public void AddPage(string name, ParchmentPanel? page)
    {
        if (page != null)
        {
            _pages.Add((name, page));
        }
    }

    /// <summary>Puts the moved pieces where the player left them, kept inside a screen of this size.</summary>
    public void Apply(Vector2 screen)
    {
        foreach ((string name, Control piece) in _pieces)
        {
            if (_moved.TryGetValue(name, out Vector2 at))
            {
                PlaceAt(piece, Clamp(at, piece.Size, screen));
            }
        }
        foreach ((string name, ParchmentPanel page) in _pages)
        {
            if (_moved.TryGetValue(name, out Vector2 at))
            {
                page.Position = Clamp(at, page.Size, screen);
            }
        }
    }

    /// <summary>Forgets every moved position (back to the default layout on the next Hud layout).</summary>
    public void ResetLayout()
    {
        _moved.Clear();
        Save();
        QueueRedraw();
    }

    /// <summary>Moves a piece or page by name as if dragged there (tests); false if there's no such name.</summary>
    public bool MoveTo(string name, Vector2 at)
    {
        Vector2 screen = GetViewportRect().Size;
        foreach ((string pieceName, Control piece) in _pieces)
        {
            if (pieceName == name)
            {
                _moved[name] = at;
                PlaceAt(piece, Clamp(at, piece.Size, screen));
                return true;
            }
        }
        foreach ((string pageName, ParchmentPanel page) in _pages)
        {
            if (pageName == name)
            {
                _moved[name] = at;
                page.Position = Clamp(at, page.Size, screen);
                return true;
            }
        }
        return false;
    }

    private Vector2 Clamp(Vector2 at, Vector2 size, Vector2 screen)
    {
        float keepX = Mathf.Min(KeepVisible, size.X);
        float keepY = Mathf.Min(KeepVisible, size.Y);
        return new Vector2(Mathf.Clamp(at.X, keepX - size.X, screen.X - keepX), Mathf.Clamp(at.Y, 0.0f, screen.Y - keepY));
    }

    private void PlaceAt(Control piece, Vector2 at)
    {
        if (Place != null)
        {
            Place(piece, at);
        }
        else
        {
            piece.Position = at;
        }
    }

    public override void _Process(double delta)
    {
        bool editing = Input.IsKeyPressed(Key.Alt) && !GameEvents.IsPacking && DisplayServer.GetName() != "headless";
        if (editing == Editing)
        {
            if (Editing)
            {
                QueueRedraw();
            }
            return;
        }
        Editing = editing;
        if (editing)
        {
            // The mouse is the player's while arranging: no looking around, no leaning.
            _ownsScreen = !GameEvents.IsScreenOpen;
            if (_ownsScreen)
            {
                GameEvents.PublishScreenOpen(true);
            }
            OnlyAltEditing = _ownsScreen;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            EndDrag();
            OnlyAltEditing = false;
            if (_ownsScreen)
            {
                _ownsScreen = false;
                GameEvents.PublishScreenOpen(false);
                if (!GameEvents.IsPacking && !GameEvents.IsMapOpen)
                {
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                }
            }
        }
        EditingChanged?.Invoke(editing);
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (Editing && @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Backspace)
        {
            ResetLayout();
            GetTree().CallGroup(Hud.Group, Hud.MethodName.Layout);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed)
            {
                if (BeginDrag(button.Position))
                {
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (_dragging != null)
            {
                EndDrag();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging != null)
        {
            Vector2 at = Clamp(motion.Position - _grab, _dragging.Size, GetViewportRect().Size);
            if (_dragging is ParchmentPanel)
            {
                _dragging.Position = at;
            }
            else
            {
                PlaceAt(_dragging, at);
            }
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Starts dragging what's under the mouse: an open page's title strip, or (with Alt) a HUD piece.</summary>
    private bool BeginDrag(Vector2 mouse)
    {
        for (int i = _pages.Count - 1; i >= 0; i--)
        {
            (string name, ParchmentPanel page) = _pages[i];
            if (!page.Visible || !page.IsOpen)
            {
                continue;
            }
            Rect2 strip = new(page.Position, new Vector2(page.Size.X - CloseButtonSpace, HeaderGrab));
            if (strip.HasPoint(mouse) || (Editing && new Rect2(page.Position, page.Size).HasPoint(mouse)))
            {
                StartDrag(name, page, mouse);
                return true;
            }
        }
        if (!Editing)
        {
            return false;
        }
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            (string name, Control piece) = _pieces[i];
            if (piece.IsVisibleInTree() && piece.GetGlobalRect().HasPoint(mouse))
            {
                StartDrag(name, piece, mouse);
                return true;
            }
        }
        return false;
    }

    private void StartDrag(string name, Control piece, Vector2 mouse)
    {
        _dragging = piece;
        _dragName = name;
        _grab = mouse - piece.Position;
    }

    private void EndDrag()
    {
        if (_dragging == null)
        {
            return;
        }
        _moved[_dragName] = _dragging.Position;
        _dragging = null;
        Save();
    }

    public override void _Draw()
    {
        if (!Editing)
        {
            return;
        }
        foreach ((string name, Control piece) in _pieces)
        {
            if (!piece.IsVisibleInTree())
            {
                continue;
            }
            Rect2 rect = piece.GetGlobalRect().Grow(4.0f);
            Color colour = UiStyle.Highlight;
            if (piece == _dragging)
            {
                colour = UiStyle.Warning;
            }
            DrawDashedLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), colour, 2.0f, 8.0f);
            DrawDashedLine(new Vector2(rect.End.X, rect.Position.Y), rect.End, colour, 2.0f, 8.0f);
            DrawDashedLine(rect.End, new Vector2(rect.Position.X, rect.End.Y), colour, 2.0f, 8.0f);
            DrawDashedLine(new Vector2(rect.Position.X, rect.End.Y), rect.Position, colour, 2.0f, 8.0f);
        }
    }

    private void Load()
    {
        if (!_persist)
        {
            return;
        }
        ConfigFile file = new();
        if (file.Load(SavePath) != Error.Ok)
        {
            return;
        }
        foreach (string key in file.GetSectionKeys("layout"))
        {
            Variant value = file.GetValue("layout", key);
            if (value.VariantType == Variant.Type.Vector2)
            {
                _moved[key] = value.AsVector2();
            }
        }
    }

    private void Save()
    {
        if (!_persist)
        {
            return;
        }
        ConfigFile file = new();
        foreach ((string name, Vector2 at) in _moved)
        {
            file.SetValue("layout", name, at);
        }
        file.Save(SavePath);
    }
}
