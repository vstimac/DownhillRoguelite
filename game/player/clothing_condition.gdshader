shader_type spatial;
render_mode cull_disabled;
uniform vec4 cloth_colour : source_color = vec4(0.28, 0.35, 0.43, 1.0);
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform float dirt : hint_range(0.0, 1.0) = 0.0;
uniform float wear : hint_range(0.0, 1.0) = 0.0;
// A modelled garment's own textures (ClothingVisual.Textured); plain cloth leaves them off.
uniform bool textured = false;
uniform sampler2D albedo_texture : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool has_normal = false;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap, repeat_enable;
float grain(vec2 p) { return fract(sin(dot(floor(p), vec2(12.9898, 78.233))) * 43758.5453); }
void fragment() {
    float weave = mix(0.94, 1.04, grain(UV * 240.0));
    vec3 fabric = cloth_colour.rgb * weave;
    if (textured) {
        fabric = cloth_colour.rgb * texture(albedo_texture, UV).rgb;
    }
    fabric = mix(fabric, vec3(0.15, 0.11, 0.065), dirt * (0.25 + grain(UV * 13.0) * 0.65));
    fabric = mix(fabric, fabric * 0.62, wetness);
    fabric = mix(fabric, mix(fabric, vec3(0.62, 0.58, 0.48), 0.5), wear * grain(UV * 70.0));
    ALBEDO = fabric;
    ROUGHNESS = mix(0.94, 0.28, wetness);
    if (has_normal) {
        NORMAL_MAP = texture(normal_texture, UV).rgb;
    }
}
