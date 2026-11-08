// The ground's look on top of its vertex colours (biome and surface): large patches of lighter and
// darker ground, fine grain, small bumps that catch the light (normals bent by a height noise, so no
// mesh tangents are needed), and steep faces turning darker and rockier. All in world space, so
// chunks join seamlessly. Set up by TerrainStreamer from its exported tunables.
shader_type spatial;
render_mode diffuse_burley;

uniform sampler2D height_noise : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float macro_scale = 0.018;
uniform float macro_strength = 0.2;
uniform float micro_scale = 0.45;
uniform float micro_strength = 0.1;
uniform float bump_scale = 0.35;
uniform float bump_strength = 0.55;
uniform vec3 rock_color : source_color = vec3(0.34, 0.32, 0.3);
uniform float rock_start = 0.3;
uniform float rock_end = 0.62;
uniform float rock_amount = 0.65;
uniform float slope_darkening = 0.28;
uniform vec3 cool_tint : source_color = vec3(0.86, 0.92, 1.0);
uniform vec3 warm_tint : source_color = vec3(1.08, 1.03, 0.92);

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize(mat3(MODEL_MATRIX) * NORMAL);
}

float height_at(vec2 p) {
	return texture(height_noise, p).r;
}

void fragment() {
	vec3 base = COLOR.rgb;
	float steep = 1.0 - clamp(world_normal.y, 0.0, 1.0);
	float rock = smoothstep(rock_start, rock_end, steep) * rock_amount;
	vec3 colour = mix(base, rock_color * (0.75 + 0.5 * base), rock);

	float macro = texture(height_noise, world_pos.xz * macro_scale + vec2(0.31, 0.77)).r;
	float micro = texture(height_noise, world_pos.xz * micro_scale + vec2(0.13, 0.52)).r;
	colour *= 1.0 + (macro - 0.5) * 2.0 * macro_strength;
	// Warm on the rises, cool in the dips: reads as light and depth across open ground.
	colour *= mix(cool_tint, warm_tint, smoothstep(0.3, 0.7, macro));
	colour *= 1.0 + (micro - 0.5) * 2.0 * micro_strength;
	colour *= 1.0 - steep * slope_darkening;
	ALBEDO = colour;
	ROUGHNESS = 1.0;

	// Small bumps: the height noise's slope bends the world normal (stronger on flatter ground).
	vec2 uv = world_pos.xz * bump_scale;
	float e = 0.004;
	float h = height_at(uv);
	float dx = height_at(uv + vec2(e, 0.0)) - h;
	float dz = height_at(uv + vec2(0.0, e)) - h;
	vec3 bent = normalize(world_normal - vec3(dx, 0.0, dz) / e * bump_strength * 0.02 * (1.0 - steep * 0.5));
	NORMAL = normalize((VIEW_MATRIX * vec4(bent, 0.0)).xyz);
}
