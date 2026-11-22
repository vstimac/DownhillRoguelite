using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The UI's visual system in one place, the expedition look: dark see-through slate panels, thin
/// light lines, one warm accent, Barlow for text and Barlow Condensed in spaced capitals for headings.
/// Colours, fonts, sizes and a factory for panel backgrounds and labels; every panel and widget takes
/// its look from here so the interface stays one family. The names (Ink, Paper, Parchment) are the
/// journal style's it replaced: Ink is what is drawn (text, lines), Paper what it is drawn on.
/// </summary>
public static class UiStyle
{
    /// <summary>Text and lines: a cold off-white.</summary>
    public static readonly Color Ink = new(0.91f, 0.93f, 0.95f);
    public static readonly Color InkSoft = new(0.91f, 0.93f, 0.95f, 0.55f);
    /// <summary>The panels' slate (opaque here; the panel shader draws it see-through).</summary>
    public static readonly Color Paper = new(0.06f, 0.08f, 0.1f);
    /// <summary>A raised surface on a panel: a slot, a selected row's ground.</summary>
    public static readonly Color PaperDark = new(0.16f, 0.2f, 0.23f);
    public static readonly Color PaperShadow = new(0.0f, 0.0f, 0.0f, 0.28f);
    /// <summary>Labels, captions and hairlines: blue-grey.</summary>
    public static readonly Color Muted = new(0.62f, 0.7f, 0.75f);
    public static readonly Color Line = new(0.36f, 0.43f, 0.47f);
    public static readonly Color Health = new(0.9f, 0.36f, 0.3f);
    public static readonly Color Hunger = new(0.95f, 0.66f, 0.32f);
    public static readonly Color Thirst = new(0.42f, 0.68f, 0.92f);
    public static readonly Color Stamina = new(0.62f, 0.8f, 0.42f);
    public static readonly Color Cold = new(0.62f, 0.8f, 0.94f);
    public static readonly Color Warning = new(0.95f, 0.36f, 0.26f);
    /// <summary>The one accent: amber, for what's selected, new or worth reading first.</summary>
    public static readonly Color Highlight = new(0.95f, 0.7f, 0.35f);

    public const int TextSize = 19;
    public const int SmallSize = 15;
    public const int HeadingSize = 26;

    private static Font? _hand;
    private static Font? _heading;
    private static Font? _strong;
    private static Shader? _parchment;
    private static Shader? _inkIcon;

    /// <summary>Body text (Barlow).</summary>
    public static Font Hand => _hand ??= Load("res://ui/fonts/Barlow-Regular.ttf");
    /// <summary>Headings: Barlow Condensed with its letters spaced out (headings show in capitals).</summary>
    public static Font Heading => _heading ??= Spaced(Load("res://ui/fonts/BarlowCondensed-Medium.ttf"), 2);
    /// <summary>Emphasis in body text (Barlow Medium).</summary>
    public static Font Strong => _strong ??= Load("res://ui/fonts/Barlow-Medium.ttf");

    private static Font Load(string path)
    {
        if (ResourceLoader.Load(path) is FontFile font)
        {
            return font;
        }
        return new FontFile();
    }

    private static Font Spaced(Font font, int glyph)
    {
        FontVariation spaced = new() { BaseFont = font };
        spaced.SetSpacing(TextServer.SpacingType.Glyph, glyph);
        return spaced;
    }

    public static Shader ParchmentShader => _parchment ??= ResourceLoader.Load<Shader>("res://ui/shaders/parchment.gdshader");
    public static Shader InkIconShader => _inkIcon ??= ResourceLoader.Load<Shader>("res://ui/shaders/ink_icon.gdshader");

    /// <summary>A panel background for a control of <paramref name="size"/> (border 0: the fill alone, for a shadow).</summary>
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
        // Headings in spaced capitals, like the labels on an expedition's kit.
        label.Uppercase = heading;
        return label;
    }

    /// <summary>A theme so built-in controls (buttons, text fields) inside panels use the UI's font and colours.</summary>
    public static Theme MakeTheme()
    {
        Theme theme = new() { DefaultFont = Hand, DefaultFontSize = TextSize };
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_color", "Button", Ink);
        theme.SetColor("font_hover_color", "Button", Highlight);
        theme.SetColor("font_color", "LineEdit", Ink);
        theme.SetColor("caret_color", "LineEdit", Highlight);
        theme.SetColor("font_placeholder_color", "LineEdit", Muted);
        StyleBoxFlat field = new() { BgColor = new Color(PaperDark, 0.6f), BorderColor = Line, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 };
        field.SetBorderWidthAll(1);
        theme.SetStylebox("normal", "LineEdit", field);
        StyleBoxFlat focused = (StyleBoxFlat)field.Duplicate();
        focused.BorderColor = Highlight;
        theme.SetStylebox("focus", "LineEdit", focused);
        StyleBoxEmpty empty = new();
        theme.SetStylebox("normal", "Button", empty);
        theme.SetStylebox("hover", "Button", empty);
        theme.SetStylebox("pressed", "Button", empty);
        theme.SetStylebox("focus", "Button", empty);
        return theme;
    }
}
