using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Red darkening at the screen edges: a flash on every hit (bigger hits flash harder) and a steady
/// edge below LowHealthFraction. A screen effect, not an interface, so it stays during runs.
/// Reads Damaged and VitalsSnapshot events.
/// </summary>
public partial class HurtVignette : CanvasLayer
{
    [Export] public Color TintColor { get; set; } = new(0.6f, 0.0f, 0.0f);
    /// <summary>Health fraction below which the edge stays red.</summary>
    [Export] public float LowHealthFraction { get; set; } = 0.4f;
    /// <summary>Vignette strength at zero health.</summary>
    [Export] public float MaxLowHealthStrength { get; set; } = 0.7f;
    /// <summary>Flash strength per point of damage.</summary>
    [Export] public float FlashPerDamage { get; set; } = 0.04f;
    [Export] public float MaxFlash { get; set; } = 0.9f;
    /// <summary>How fast a flash fades, in strength per second.</summary>
    [Export] public float FlashFade { get; set; } = 1.5f;

    private const string ShaderCode = @"
shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.6, 0.0, 0.0, 1.0);
uniform float strength = 0.0;
void fragment() {
    vec2 centred = UV - vec2(0.5);
    float edge = smoothstep(0.25, 0.75, length(centred) * 1.4);
    COLOR = vec4(tint.rgb, edge * strength);
}";

    private ShaderMaterial? _material;
    private float _flash;
    private float _lowHealth;

    public override void _Ready()
    {
        Layer = 2;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _material.SetShaderParameter("tint", TintColor);
        ColorRect rect = new()
        {
            Material = _material,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(rect);

        GameEvents.Damaged += OnDamaged;
        GameEvents.VitalsUpdated += OnVitalsUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.Damaged -= OnDamaged;
        GameEvents.VitalsUpdated -= OnVitalsUpdated;
        _material?.Dispose();
        _material = null;
    }

    private void OnDamaged(float amount)
    {
        _flash = Mathf.Min(MaxFlash, _flash + amount * FlashPerDamage);
    }

    private void OnVitalsUpdated(VitalsSnapshot vitals)
    {
        _lowHealth = 0.0f;
        if (vitals.MaxHealth > 0.0f)
        {
            float fraction = vitals.Health / vitals.MaxHealth;
            if (fraction < LowHealthFraction)
            {
                _lowHealth = (1.0f - fraction / LowHealthFraction) * MaxLowHealthStrength;
            }
        }
    }

    public override void _Process(double delta)
    {
        _flash = Mathf.MoveToward(_flash, 0.0f, FlashFade * (float)delta);
        _material?.SetShaderParameter("strength", Mathf.Max(_flash, _lowHealth));
    }
}
