shader_type spatial;
render_mode cull_disabled;
uniform vec4 cloth_colour : source_color = vec4(0.28, 0.35, 0.43, 1.0);
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform float dirt : hint_range(0.0, 1.0) = 0.0;
uniform float wear : hint_range(0.0, 1.0) = 0.0;
float grain(vec2 p) { return fract(sin(dot(floor(p), vec2(12.9898, 78.233))) * 43758.5453); }
void fragment() {
    float weave = mix(0.94, 1.04, grain(UV * 240.0));
    vec3 fabric = cloth_colour.rgb * weave;
    fabric = mix(fabric, vec3(0.15, 0.11, 0.065), dirt * (0.25 + grain(UV * 13.0) * 0.65));
    fabric = mix(fabric, fabric * 0.62, wetness);
    fabric = mix(fabric, mix(fabric, vec3(0.62, 0.58, 0.48), 0.5), wear * grain(UV * 70.0));
    ALBEDO = fabric;
    ROUGHNESS = mix(0.94, 0.28, wetness);
}
