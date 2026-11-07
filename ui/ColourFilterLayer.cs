using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A colour-blind filter over the whole screen (GameOptions.Filter): daltonisation, which shifts the
/// colours a viewer with protanopia, deuteranopia or tritanopia can't tell apart into ones they can.
/// Off (hidden) by default.
/// </summary>
public partial class ColourFilterLayer : CanvasLayer
{
    private const string ShaderCode = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear;
uniform int mode = 0;

vec3 simulate(vec3 c) {
    // Into LMS, drop what the missing cone would see, back to RGB.
    mat3 rgb2lms = mat3(vec3(17.8824, 3.45565, 0.0299566), vec3(43.5161, 27.1554, 0.184309), vec3(4.11935, 3.86714, 1.46709));
    mat3 lms2rgb = mat3(vec3(0.0809444479, -0.0102485335, -0.000365296938), vec3(-0.130504409, 0.0540193266, -0.00412161469), vec3(0.116721066, -0.113614708, 0.693511405));
    vec3 lms = rgb2lms * c;
    if (mode == 1) {
        lms.x = 2.02344 * lms.y - 2.52581 * lms.z;
    } else if (mode == 2) {
        lms.y = 0.494207 * lms.x + 1.24827 * lms.z;
    } else {
        lms.z = -0.395913 * lms.x + 0.801109 * lms.y;
    }
    return lms2rgb * lms;
}

void fragment() {
    vec3 c = texture(screen, SCREEN_UV).rgb;
    vec3 seen = simulate(c);
    vec3 lost = c - seen;
    // Shift what is lost into channels still seen.
    vec3 shift = vec3(0.0, lost.r * 0.7 + lost.g, lost.r * 0.7 + lost.b);
    COLOR = vec4(clamp(c + shift, 0.0, 1.0), 1.0);
}
";

    private ColorRect? _rect;
    private ShaderMaterial? _material;

    public override void _Ready()
    {
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _rect = new ColorRect { Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
        GameOptions.Changed += Apply;
        Apply();
    }

    public override void _ExitTree()
    {
        GameOptions.Changed -= Apply;
    }

    private void Apply()
    {
        Visible = GameOptions.Filter != ColourFilter.None;
        _material?.SetShaderParameter("mode", (int)GameOptions.Filter);
    }
}
