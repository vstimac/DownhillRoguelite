// Trees in the wind: the trunk bends downwind (more toward the crown, more in gusts, with a slow
// swing of its own) and the branches flutter. Generated pines carry each vertex's branch flex in
// UV.x (0 at the trunk, 1 at a twig's tip) and a per-branch phase in UV.y; other models flex by
// height in the crown (model_base, model_height). The wind comes from the shader globals that
// environment/FoliageWind sets from the weather. Opaque, double sided, vertex colours or a colour.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

uniform vec4 albedo : source_color = vec4(1.0);
uniform bool use_vertex_colour = true;
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_texture = false;
uniform float roughness = 1.0;
/// Branch flex and phase come from UV (generated pines), or from height in the model (others).
uniform bool flex_in_uv = false;
uniform float model_base = 0.0;
uniform float model_height = 1.0;
/// The crown's top shifts this share of the tree's height in a full wind.
uniform float sway = 0.03;
uniform float swing_speed = 0.55;
/// Branch tips flutter this far (m) in a full wind, a little even when calm.
uniform float flutter = 0.07;
uniform float flutter_speed = 2.4;
uniform float gust_scale = 0.02;

global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform vec2 wind_scroll;
global uniform sampler2D wind_noise;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void vertex() {
	mat3 basis = mat3(MODEL_MATRIX);
	float scale = length(basis[1]);
	vec3 root = MODEL_MATRIX[3].xyz;
	float up = clamp((VERTEX.y - model_base) / max(model_height, 0.001), 0.0, 1.2);
	float tree_height = model_height * scale;
	float flex = smoothstep(0.25, 1.0, up);
	float branch_phase = hash(VERTEX.xz * 3.1 + vec2(VERTEX.y));
	if (flex_in_uv) {
		flex = UV.x;
		branch_phase = UV.y;
	}
	float gust = textureLod(wind_noise, fract((root.xz - wind_scroll) * gust_scale), 0.0).r;
	gust = smoothstep(0.3, 0.8, gust);
	float wind = clamp(wind_strength, 0.0, 1.5);
	vec2 direction = normalize(wind_direction + vec2(0.0001, 0.0));
	float tree_phase = hash(root.xz) * 6.2831;
	// The trunk: bent downwind, swinging slowly round that.
	float swing = sin(TIME * swing_speed + tree_phase) * (0.25 + 0.5 * wind);
	float lean = wind * (0.45 + 0.55 * gust) + swing * 0.35;
	vec2 bent = direction * lean * sway * tree_height * up * up;
	vec3 offset = vec3(bent.x, -dot(bent, bent) / max(tree_height, 0.5), bent.y);
	// The branches: each flutters on its own.
	float t = TIME * flutter_speed + branch_phase * 6.2831 + tree_phase;
	vec3 flap = vec3(sin(t), sin(t * 1.37 + 1.1) * 0.6, cos(t * 0.83 + 0.4));
	offset += flap * flutter * flex * (0.2 + wind * (0.6 + 0.6 * gust));
	VERTEX += transpose(basis) * offset / max(scale * scale, 0.000001);
}

void fragment() {
	vec3 colour = albedo.rgb;
	if (use_vertex_colour) {
		colour *= COLOR.rgb;
	}
	if (use_texture) {
		colour *= texture(albedo_texture, UV).rgb;
	}
	ALBEDO = colour;
	ROUGHNESS = roughness;
	BACKLIGHT = colour * 0.15;
}
