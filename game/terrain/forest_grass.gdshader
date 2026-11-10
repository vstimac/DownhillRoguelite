// Grass that lives in the wind: gusts roll across the meadow as waves (a noise field drifting
// downwind), each blade bends with them (more at the tip, taller blades more), flutters a little
// on its own, parts round the carrier's legs, and catches the light where a gust flattens it.
// Opaque geometry: no alpha overdraw, no collision, shared by all streamed grass batches.
// The wind comes from the shader globals that environment/FoliageWind sets from the weather.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

uniform vec4 grass_colour : source_color = vec4(0.5, 0.62, 0.4, 1.0);
uniform float draw_distance = 45.0;
/// Tip shift (m) per square metre of blade height in a full wind: a 0.4 m blade bends about 0.15 m.
uniform float bend = 0.95;
/// How much of the bend is always there in a wind, against what the gusts add.
uniform float steady_share = 0.35;
/// Gust waves: noise per metre (smaller: wider waves).
uniform float gust_scale = 0.02;
/// Each blade's own flutter (m per metre of height), even in a breeze.
uniform float flutter = 0.06;
uniform float flutter_speed = 3.2;
/// The carrier's legs push blades this far aside (share of the blade's height).
uniform float push_strength = 0.75;
/// Brighter where a gust lays the grass over (the shimmer of a wave), and at the tips.
uniform float gust_sheen = 0.22;
uniform float tip_light = 0.12;
uniform float translucency = 0.25;

global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform vec2 wind_scroll;
global uniform sampler2D wind_noise;
global uniform vec4 foliage_pusher;

varying float sheen;
varying float tipness;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 root = MODEL_MATRIX[3].xyz;
	float distance_xz = length(world.xz - INV_VIEW_MATRIX[3].xz);
	float visible_height = 1.0 - smoothstep(draw_distance * 0.8, draw_distance, distance_xz);
	float height = max(world.y - root.y, 0.0);
	// The whole tuft (its root) samples one gust, so blades of a tuft move together.
	float gust = textureLod(wind_noise, fract((root.xz - wind_scroll) * gust_scale), 0.0).r;
	gust = smoothstep(0.32, 0.78, gust);
	float wind = clamp(wind_strength, 0.0, 1.5);
	float push_amount = wind * (steady_share + (1.0 - steady_share) * gust);
	vec2 direction = normalize(wind_direction + vec2(0.0001, 0.0));
	vec2 across = vec2(-direction.y, direction.x);
	float phase = hash(root.xz) * 6.2831;
	float wobble = sin(TIME * flutter_speed + phase + dot(root.xz, direction) * 0.35);
	vec2 shift = direction * push_amount * bend * height * height;
	shift += (across * wobble + direction * wobble * 0.4) * flutter * (0.25 + wind) * height;
	// Parting round the carrier: blades lean away from him, the nearer the more.
	vec2 away = world.xz - foliage_pusher.xz;
	float near = 1.0 - smoothstep(0.0, max(foliage_pusher.w, 0.01), length(away));
	near *= 1.0 - smoothstep(0.6, 1.4, abs(root.y - foliage_pusher.y));
	shift += normalize(away + vec2(0.0001, 0.0)) * near * push_strength * height;
	// Keep the blade's length: a bent blade's tip comes down.
	float lowered = dot(shift, shift) / max(2.0 * height, 0.02);
	vec3 offset = vec3(shift.x, -min(lowered, height * 0.8), shift.y);
	// World offset into the model's space (rotation and a uniform scale).
	mat3 basis = mat3(MODEL_MATRIX);
	VERTEX += transpose(basis) * offset / max(dot(basis[1], basis[1]), 0.000001);
	VERTEX.y *= visible_height;
	// Softer, meadow-like light: the normals lean toward the sky.
	NORMAL = normalize(mix(NORMAL, normalize(transpose(basis) * vec3(0.0, 1.0, 0.0)), 0.55));
	sheen = gust * wind;
	tipness = clamp(height * 3.0, 0.0, 1.0);
}

void fragment() {
	vec3 colour = grass_colour.rgb * COLOR.rgb;
	colour *= 1.0 + sheen * gust_sheen * tipness + tip_light * tipness;
	ALBEDO = colour;
	ROUGHNESS = 1.0;
	BACKLIGHT = colour * translucency;
}
