using System;
using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The row of tabs across the top of a sheet: names in old print, the chosen one inked darker with
/// a hand-drawn underline that slides to it. Clicking picks a tab; the sheet steps through them with
/// Q / E or a controller's shoulder buttons (the small key hints at either end say so).
/// </summary>
public partial class TabStrip : Control
{
    public int FontSize { get; set; } = 24;
    public float StripHeight { get; set; } = 46.0f;
    /// <summary>Raised with the tab's index when it is picked.</summary>
    public event Action<int>? TabPicked;

    public int Selected { get; private set; }
    public int Count => _names.Count;

    private readonly List<string> _names = new();
    private readonly List<Rect2> _rects = new();
    private float _underlineX;
    private float _underlineWidth;
    private int _hover = -1;
    private Tween? _tween;

    private const float HintWidth = 34.0f;

    public void SetTabs(IEnumerable<string> names)
    {
        _names.Clear();
        _names.AddRange(names);
        LayoutTabs();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0.0f, StripHeight);
        Resized += LayoutTabs;
        LayoutTabs();
    }

    /// <summary>Tab rectangles share the width between the two key hints, each as wide as its name needs.</summary>
    private void LayoutTabs()
    {
        _rects.Clear();
        if (_names.Count == 0)
        {
            return;
        }
        Font font = UiStyle.Heading;
        float total = 0.0f;
        float[] widths = new float[_names.Count];
        for (int i = 0; i < _names.Count; i++)
        {
            widths[i] = font.GetStringSize(_names[i], HorizontalAlignment.Left, -1, FontSize).X;
            total += widths[i];
        }
        float room = Mathf.Max(Size.X - HintWidth * 2.0f, 1.0f);
        float gap = Mathf.Max((room - total) / _names.Count, 0.0f);
        float x = HintWidth;
        for (int i = 0; i < _names.Count; i++)
        {
            float w = widths[i] + gap;
            _rects.Add(new Rect2(x, 0.0f, w, Size.Y));
            x += w;
        }
        Rect2 chosen = _rects[Mathf.Clamp(Selected, 0, _rects.Count - 1)];
        _underlineX = chosen.Position.X + gap * 0.5f;
        _underlineWidth = chosen.Size.X - gap;
        QueueRedraw();
    }

    /// <summary>The tab names' widest need, to check nothing is clipped.</summary>
    public bool FitsWidth()
    {
        Font font = UiStyle.Heading;
        for (int i = 0; i < _names.Count && i < _rects.Count; i++)
        {
            if (font.GetStringSize(_names[i], HorizontalAlignment.Left, -1, FontSize).X > _rects[i].Size.X + 0.5f)
            {
                return false;
            }
        }
        return _rects.Count == _names.Count && (_rects.Count == 0 || _rects[^1].End.X <= Size.X - HintWidth + 0.5f);
    }

    public void Select(int index, bool animate = true)
    {
        if (_names.Count == 0)
        {
            return;
        }
        Selected = (index + _names.Count) % _names.Count;
        if (_rects.Count != _names.Count)
        {
            return;
        }
        Font font = UiStyle.Heading;
        Rect2 rect = _rects[Selected];
        float width = font.GetStringSize(_names[Selected], HorizontalAlignment.Left, -1, FontSize).X;
        float x = rect.Position.X + (rect.Size.X - width) * 0.5f;
        _tween?.Kill();
        if (!animate)
        {
            _underlineX = x;
            _underlineWidth = width;
            QueueRedraw();
            return;
        }
        _tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tween.TweenMethod(Callable.From<float>(v =>
        {
            _underlineX = v;
            QueueRedraw();
        }), _underlineX, x, 0.2);
        _tween.TweenMethod(Callable.From<float>(v => _underlineWidth = v), _underlineWidth, width, 0.2);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            int hover = TabAt(motion.Position);
            if (hover != _hover)
            {
                _hover = hover;
                QueueRedraw();
            }
        }
        else if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
        {
            int tab = TabAt(click.Position);
            if (tab >= 0)
            {
                TabPicked?.Invoke(tab);
            }
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hover >= 0)
        {
            _hover = -1;
            QueueRedraw();
        }
    }

    private int TabAt(Vector2 point)
    {
        for (int i = 0; i < _rects.Count; i++)
        {
            if (_rects[i].HasPoint(point))
            {
                return i;
            }
        }
        return -1;
    }

    public override void _Draw()
    {
        Font font = UiStyle.Heading;
        float baseline = (Size.Y + font.GetAscent(FontSize) - font.GetDescent(FontSize)) * 0.5f - 4.0f;
        for (int i = 0; i < _names.Count && i < _rects.Count; i++)
        {
            Rect2 rect = _rects[i];
            float width = font.GetStringSize(_names[i], HorizontalAlignment.Left, -1, FontSize).X;
            Color ink = new(UiStyle.Ink, 0.5f);
            if (i == Selected)
            {
                ink = UiStyle.Ink;
            }
            else if (i == _hover)
            {
                ink = new Color(0.36f, 0.15f, 0.04f, 0.85f);
            }
            DrawString(font, new Vector2(rect.Position.X + (rect.Size.X - width) * 0.5f, baseline), _names[i], HorizontalAlignment.Left, -1, FontSize, ink);
        }
        if (_underlineWidth > 1.0f)
        {
            float y = Size.Y - 9.0f;
            DrawPolyline(InkDraw.Wobble(new[] { new Vector2(_underlineX, y), new Vector2(_underlineX + _underlineWidth, y) }, 0.8f, 11), UiStyle.Ink, 3.0f, true);
        }
        // A pencilled rule under the whole strip.
        DrawLine(new Vector2(4.0f, Size.Y - 2.0f), new Vector2(Size.X - 4.0f, Size.Y - 2.0f), new Color(UiStyle.Ink, 0.25f), 1.5f);
        DrawKeyHint("Q", new Vector2(4.0f, baseline));
        DrawKeyHint("E", new Vector2(Size.X - HintWidth + 6.0f, baseline));
    }

    private void DrawKeyHint(string key, Vector2 at)
    {
        Font font = UiStyle.Hand;
        int size = UiStyle.SmallSize;
        Rect2 box = new(at.X, at.Y - size + 1.0f, 24.0f, size + 4.0f);
        DrawRect(box, new Color(UiStyle.Ink, 0.45f), false, 1.5f);
        DrawString(font, new Vector2(box.Position.X, at.Y), key, HorizontalAlignment.Center, box.Size.X, size, new Color(UiStyle.Ink, 0.7f));
    }
}
