using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The character page's chooser: the character's name in large old print between inked arrows,
/// their description beneath, and a dot per character. Left/right (keys, D-pad, stick) or clicking
/// an arrow steps through every character in the roster; those whose model isn't in the game yet
/// are shown as "coming soon" and can't be picked.
/// </summary>
public partial class CharacterCarousel : Control
{
    public int NameSize { get; set; } = 46;
    public int TextSize { get; set; } = UiStyle.TextSize;
    /// <summary>Raised with the newly shown character (available or not).</summary>
    public event Action<CarrierCharacter>? Shown;

    public IReadOnlyList<CarrierCharacter> Characters => _characters;
    public int Index { get; private set; }
    public CarrierCharacter? Character
    {
        get
        {
            if (_characters.Count == 0)
            {
                return null;
            }
            return _characters[Index];
        }
    }

    private readonly List<CarrierCharacter> _characters = new();
    private float _lit;
    private float _slide;
    private Tween? _tween;

    private const float ArrowZone = 56.0f;

    /// <summary>Every character in the roster, in order, starting on <paramref name="selectedId"/>.</summary>
    public void SetCharacters(IEnumerable<CarrierCharacter> characters, string selectedId)
    {
        _characters.Clear();
        _characters.AddRange(characters);
        Index = 0;
        for (int i = 0; i < _characters.Count; i++)
        {
            if (_characters[i].Id == selectedId)
            {
                Index = i;
            }
        }
        FitHeight();
        QueueRedraw();
    }

    /// <summary>The carousel never shows less height than this, in px.</summary>
    [Export] public float MinHeight { get; set; } = 170.0f;

    /// <summary>Tall enough for the shown character's name and description (a long one wraps more).</summary>
    public void FitHeight()
    {
        float needed = Mathf.Max(MinHeight, NeededHeight() + 8.0f);
        CustomMinimumSize = new Vector2(CustomMinimumSize.X, needed);
        if (Size.Y < needed)
        {
            Size = new Vector2(Size.X, needed);
        }
    }

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => GrabFocus();
        FocusEntered += () => Light(1.0f);
        FocusExited += () => Light(0.0f);
        Resized += QueueRedraw;
    }

    private void Light(float target)
    {
        Tween tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(v =>
        {
            _lit = v;
            QueueRedraw();
        }), _lit, target, 0.12);
    }

    public void Step(int direction)
    {
        if (_characters.Count < 2)
        {
            return;
        }
        Index = (Index + direction + _characters.Count) % _characters.Count;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(v =>
        {
            _slide = v;
            QueueRedraw();
        }), 28.0f * direction, 0.0f, 0.25).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        Shown?.Invoke(_characters[Index]);
        FitHeight();
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_left", true))
        {
            Step(-1);
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_right", true))
        {
            Step(1);
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
        {
            if (click.Position.X < ArrowZone)
            {
                Step(-1);
            }
            else if (click.Position.X > Size.X - ArrowZone)
            {
                Step(1);
            }
            AcceptEvent();
        }
    }

    /// <summary>The description wrapped to the width it is drawn at (also used to check it fits).</summary>
    public Vector2 DescriptionSize()
    {
        CarrierCharacter? character = Character;
        if (character == null)
        {
            return Vector2.Zero;
        }
        return UiStyle.Wrapped(DescriptionOf(character), TextSize, TextWidth());
    }

    /// <summary>Height the name, status, description and dots need.</summary>
    public float NeededHeight()
    {
        return ShownNameSize() * 1.3f + TextSize * 1.6f + DescriptionSize().Y + 30.0f;
    }

    public float NameWidth()
    {
        CarrierCharacter? character = Character;
        if (character == null)
        {
            return 0.0f;
        }
        return UiStyle.Heading.GetStringSize(character.DisplayName, HorizontalAlignment.Left, -1, ShownNameSize()).X;
    }

    /// <summary>The name's font size: NameSize, smaller for a long name (an outfit's) so it fits between the arrows.</summary>
    public int ShownNameSize()
    {
        CarrierCharacter? character = Character;
        if (character == null)
        {
            return NameSize;
        }
        float room = Size.X - ArrowZone * 2.0f - 8.0f;
        float full = UiStyle.Heading.GetStringSize(character.DisplayName, HorizontalAlignment.Left, -1, NameSize).X;
        if (full <= room || room <= 0.0f)
        {
            return NameSize;
        }
        return Mathf.Max(18, Mathf.FloorToInt(NameSize * room / full));
    }

    public float TextWidth()
    {
        return Mathf.Max(Size.X - ArrowZone * 2.0f, 40.0f);
    }

    private static string DescriptionOf(CarrierCharacter character)
    {
        if (character.Description.Length > 0)
        {
            return character.Description;
        }
        return "Carries the load like anyone else.";
    }

    public override void _Draw()
    {
        CarrierCharacter? character = Character;
        if (character == null)
        {
            return;
        }
        if (_lit > 0.01f)
        {
            DrawRect(new Rect2(0.0f, 0.0f, Size.X, Size.Y), new Color(UiStyle.Ink, 0.07f * _lit));
        }
        Font heading = UiStyle.Heading;
        Font hand = UiStyle.Hand;
        bool open = character.Available;
        Color ink = UiStyle.Ink;
        if (!open)
        {
            ink = new Color(UiStyle.Ink, 0.45f);
        }
        float nameTop = 8.0f;
        int nameSize = ShownNameSize();
        float nameBase = nameTop + heading.GetAscent(nameSize);
        float nameWidth = NameWidth();
        DrawString(heading, new Vector2((Size.X - nameWidth) * 0.5f + _slide, nameBase), character.DisplayName, HorizontalAlignment.Left, -1, nameSize, ink);

        // Arrows either side of the name.
        float mid = nameTop + nameSize * 0.55f;
        Color arrow = new(UiStyle.Ink, 0.35f + 0.6f * _lit);
        if (_characters.Count > 1)
        {
            InkDraw.Shape(this, new[] { new Vector2(18.0f, mid), new Vector2(36.0f, mid - 13.0f), new Vector2(36.0f, mid + 13.0f) }, arrow, 2.0f, 5);
            InkDraw.Shape(this, new[] { new Vector2(Size.X - 18.0f, mid), new Vector2(Size.X - 36.0f, mid - 13.0f), new Vector2(Size.X - 36.0f, mid + 13.0f) }, arrow, 2.0f, 6);
        }

        float y = nameTop + nameSize * 1.3f;
        string status = "Ready to carry";
        Color statusInk = new(UiStyle.Stamina, 0.95f);
        if (!open)
        {
            status = "Coming soon";
            statusInk = new Color(UiStyle.Warning, 0.8f);
        }
        else if (character.Id == GameOptions.Character)
        {
            status = "Chosen";
        }
        DrawString(hand, new Vector2(0.0f, y + hand.GetAscent(TextSize - 2)), status, HorizontalAlignment.Center, Size.X, TextSize - 2, statusInk);
        y += TextSize * 1.6f;

        TextParagraph paragraph = new() { Width = TextWidth(), Alignment = HorizontalAlignment.Center, BreakFlags = TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive };
        paragraph.AddString(DescriptionOf(character), hand, TextSize);
        paragraph.Draw(GetCanvasItem(), new Vector2(ArrowZone, y), new Color(UiStyle.Ink, 0.85f));
        y += paragraph.GetSize().Y + 18.0f;

        float gap = 16.0f;
        float start = Size.X * 0.5f - gap * (_characters.Count - 1) * 0.5f;
        for (int i = 0; i < _characters.Count; i++)
        {
            float radius = 3.0f;
            Color dot = new(UiStyle.Ink, 0.3f);
            if (i == Index)
            {
                radius = 5.0f;
                dot = UiStyle.Ink;
            }
            DrawCircle(new Vector2(start + gap * i, y), radius, dot);
        }
    }
}
