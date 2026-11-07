using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The journal UI's visual system in one place: ink and paper colours, the two fonts (Patrick Hand
/// for text, IM Fell English SC for headings), sizes and a factory for parchment backgrounds and
/// labels. Every panel and widget takes its look from here so the interface stays one family.
/// </summary>
public static class UiStyle
{
    public static readonly Color Ink = new(0.17f, 0.12f, 0.08f);
    public static readonly Color InkSoft = new(0.17f, 0.12f, 0.08f, 0.55f);
    public static readonly Color Paper = new(0.86f, 0.79f, 0.64f);
    public static readonly Color PaperDark = new(0.62f, 0.52f, 0.38f);
    public static readonly Color PaperShadow = new(0.0f, 0.0f, 0.0f, 0.28f);
    public static readonly Color Health = new(0.66f, 0.16f, 0.12f);
    public static readonly Color Hunger = new(0.85f, 0.55f, 0.18f);
    public static readonly Color Thirst = new(0.25f, 0.45f, 0.7f);
    public static readonly Color Stamina = new(0.42f, 0.55f, 0.25f);
    public static readonly Color Cold = new(0.45f, 0.6f, 0.75f);
    public static readonly Color Warning = new(0.8f, 0.12f, 0.08f);
    public static readonly Color Highlight = new(0.95f, 0.8f, 0.4f);

    public const int TextSize = 20;
    public const int SmallSize = 16;
    public const int HeadingSize = 30;

    private static FontFile? _hand;
    private static FontFile? _heading;
    private static Shader? _parchment;
    private static Shader? _inkIcon;

    public static Font Hand => _hand ??= Load("res://ui/fonts/PatrickHand-Regular.ttf");
    public static Font Heading => _heading ??= Load("res://ui/fonts/IMFeENsc28P.ttf");

    private static FontFile Load(string path)
    {
        if (ResourceLoader.Load(path) is FontFile font)
        {
            return font;
        }
        return new FontFile();
    }

    public static Shader ParchmentShader => _parchment ??= ResourceLoader.Load<Shader>("res://ui/shaders/parchment.gdshader");
    public static Shader InkIconShader => _inkIcon ??= ResourceLoader.Load<Shader>("res://ui/shaders/ink_icon.gdshader");

    /// <summary>A parchment material for a control of <paramref name="size"/>; each seed tears differently.</summary>
    public static ShaderMaterial Parchment(Vector2 size, float seed, float border = 4.0f, float tear = 5.0f)
    {
        ShaderMaterial material = new() { Shader = ParchmentShader };
        material.SetShaderParameter("rect_size", size);
        material.SetShaderParameter("seed", seed);
        material.SetShaderParameter("border_px", border);
        material.SetShaderParameter("tear_px", tear);
        return material;
    }

    /// <summary>How big text is once wrapped at a width, the way a WordSmart label wraps it: widest line, total height.</summary>
    public static Vector2 Wrapped(string text, int size, float width, bool heading = false)
    {
        Font font = Hand;
        if (heading)
        {
            font = Heading;
        }
        TextParagraph paragraph = new() { Width = width, BreakFlags = TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive };
        paragraph.AddString(text, font, size);
        float widest = 0.0f;
        float height = 0.0f;
        for (int i = 0; i < paragraph.GetLineCount(); i++)
        {
            Vector2 line = paragraph.GetLineSize(i);
            widest = Mathf.Max(widest, line.X);
            height += line.Y;
        }
        return new Vector2(Mathf.Ceil(widest) + 2.0f, Mathf.Ceil(height) + 2.0f);
    }

    public static Label MakeLabel(string text, int size = TextSize, bool heading = false, Color? colour = null)
    {
        Label label = new() { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        Font font = Hand;
        if (heading)
        {
            font = Heading;
        }
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour ?? Ink);
        return label;
    }

    /// <summary>A theme so built-in controls (buttons, sliders) inside panels use the hand font and ink.</summary>
    public static Theme MakeTheme()
    {
        Theme theme = new() { DefaultFont = Hand, DefaultFontSize = TextSize };
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_color", "Button", Ink);
        theme.SetColor("font_hover_color", "Button", new Color(0.4f, 0.2f, 0.05f));
        StyleBoxEmpty empty = new();
        theme.SetStylebox("normal", "Button", empty);
        theme.SetStylebox("hover", "Button", empty);
        theme.SetStylebox("pressed", "Button", empty);
        theme.SetStylebox("focus", "Button", empty);
        return theme;
    }
}
