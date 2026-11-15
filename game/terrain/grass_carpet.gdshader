// A thick carpet of short grass over the ground near the carrier, drawn entirely on the GPU.
// Every streamed chunk shows four quarters of one shared MultiMesh (a clump of blades per instance,
// on a grid with a jitter); this shader stands each clump on the chunk's rendered ground (heights
// from a small texture, following the same triangle diagonal as the mesh), colours it from the
// ground beneath and thins it by the chunk's carpet mask, so it stops crisply at the paths' trodden
// bands, on rock and snow. Clumps fade out with distance. Wind and the carrier's legs move it like
// the tufts (environment/FoliageWind's shader globals). Opaque, no collision, no shadows.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

uniform sampler2D heights : filter_nearest;
uniform sampler2D carpet : filter_linear;
uniform int grid_points = 65;
uniform float cell_size = 1.0;
uniform int columns = 64;
uniform float spacing = 0.5;
uniform float draw_distance = 32.0;
uniform float blade_height = 0.3;
/// The ground's soil texture, its tile in m and how strongly the ground shows it (0: flat colour
/// only). The blades' roots take exactly what the ground shows there, so the carpet grows out of it.
uniform sampler2D soil : source_color, filter_linear_mipmap, repeat_enable;
uniform float soil_metres = 3.2;
uniform float soil_strength = 0.0;
/// How far the blades keep the meadow's own green (the ground's flat colour) over the soil's brown.
uniform float greenness = 0.3;
/// How much greener the blade tips are than that, and how dark the roots.
uniform vec3 tip_tint = vec3(0.96, 1.04, 0.86);
uniform float root_shade = 0.72;
/// Richer than the ground's muted colour (1: as the ground). Much above 1.1 and the carpet reads as
/// a bright lawn laid over the olive ground instead of growing out of it.
uniform float saturation = 1.05;
uniform float bend = 1.1;
uniform float steady_share = 0.35;
uniform float gust_scale = 0.02;
uniform float flutter = 0.07;
uniform float flutter_speed = 3.0;
uniform float push_strength = 0.8;
uniform float gust_sheen = 0.25;
uniform float translucency = 0.3;

instance uniform vec2 quarter_origin;

global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform vec2 wind_scroll;
global uniform sampler2D wind_noise;
global uniform vec4 foliage_pusher;

varying vec3 ground_colour;
varying vec3 blade_colour;
varying vec3 lit_normal;
varying float sheen;

uint pcg(uint v) {
	uint state = v * 747796405u + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

float rand(uint seed) {
	return float(pcg(seed) & 16777215u) / 16777215.0;
}

float grid_height(vec2 local) {
	float limit = float(grid_points) - 1.001;
	vec2 g = clamp(local / cell_size, vec2(0.0), vec2(limit));
	ivec2 c = ivec2(g);
	vec2 f = g - vec2(c);
	float h00 = texelFetch(heights, c, 0).r;
	float h10 = texelFetch(heights, c + ivec2(1, 0), 0).r;
	float h01 = texelFetch(heights, c + ivec2(0, 1), 0).r;
	float h11 = texelFetch(heights, c + ivec2(1, 1), 0).r;
	// The rendered a-b-d / a-d-c diagonal (TerrainGenerator.GridHeight).
	if (f.x >= f.y) {
		return h00 + (h10 - h00) * f.x + (h11 - h10) * f.y;
	}
	return h00 + (h11 - h01) * f.x + (h01 - h00) * f.y;
}

void vertex() {
	vec3 chunk = MODEL_MATRIX[3].xyz;
	int id = INSTANCE_ID;
	vec2 cell = vec2(float(id % columns), float(id / columns));
	ivec2 chunk_cell = ivec2(floor(chunk.xz / 8.0 + 0.5));
	ivec2 quarter_cell = ivec2(floor(quarter_origin / spacing + 0.5));
	uint seed = pcg(uint(id) ^ pcg(uint(chunk_cell.x + 1048576) ^ pcg(uint(chunk_cell.y + 1048576) ^ pcg(uint(quarter_cell.x * 4096 + quarter_cell.y + 7)))));
	vec2 jitter = vec2(rand(seed), rand(seed + 1u)) - 0.5;
	float keep = rand(seed + 2u);
	float tall = rand(seed + 3u);
	float turn = rand(seed + 4u) * 6.2831;
	float extent = float(grid_points - 1) * cell_size;
	vec2 local = clamp(quarter_origin + (cell + 0.5 + jitter * 0.95) * spacing, vec2(0.0), vec2(extent - 0.001));
	vec2 uv = (local / cell_size + 0.5) / float(grid_points);
	vec4 mask = textureLod(carpet, uv, 0.0);
	float ground = grid_height(local);
	vec3 root = vec3(local.x, ground, local.y) + chunk;
	float distance_xz = length(root.xz - CAMERA_POSITION_WORLD.xz);
	float fade = 1.0 - smoothstep(draw_distance * 0.65, draw_distance, distance_xz);
	// Thin edges: a clump survives where its roll is under the carpet's thickness there.
	float grow = clamp((mask.a - keep) * 6.0, 0.0, 1.0) * fade;
	sheen = 0.0;
	// Lit like the ground it stands on (nearly straight up, a little its own way), on both faces.
	lit_normal = normalize(vec3(sin(turn) * 0.22, 1.0, cos(turn) * 0.22));
	if (grow <= 0.001) {
		VERTEX = vec3(0.0);
	} else {
		float s = sin(turn);
		float c = cos(turn);
		float scale = blade_height * (0.6 + 0.8 * tall) * grow;
		vec3 blade = vec3(VERTEX.x * c - VERTEX.z * s, VERTEX.y, VERTEX.x * s + VERTEX.z * c);
		float height = blade.y * scale;
		vec3 world = root + vec3(blade.x * (0.6 + 0.4 * grow), height, blade.z * (0.6 + 0.4 * grow));
		float gust = textureLod(wind_noise, fract((root.xz - wind_scroll) * gust_scale), 0.0).r;
		gust = smoothstep(0.32, 0.78, gust);
		float wind = clamp(wind_strength, 0.0, 1.5);
		vec2 direction = normalize(wind_direction + vec2(0.0001, 0.0));
		vec2 across = vec2(-direction.y, direction.x);
		float wobble = sin(TIME * flutter_speed + turn + dot(root.xz, direction) * 0.35);
		vec2 shift = direction * wind * (steady_share + (1.0 - steady_share) * gust) * bend * height * height * 4.0;
		shift += (across * wobble + direction * wobble * 0.4) * flutter * (0.25 + wind) * height;
		vec2 away = world.xz - foliage_pusher.xz;
		float near = 1.0 - smoothstep(0.0, max(foliage_pusher.w, 0.01), length(away));
		near *= 1.0 - smoothstep(0.6, 1.4, abs(root.y - foliage_pusher.y));
		shift += normalize(away + vec2(0.0001, 0.0)) * near * push_strength * height;
		float lowered = dot(shift, shift) / max(2.0 * height, 0.02);
		world += vec3(shift.x, -min(lowered, height * 0.8), shift.y);
		VERTEX = world - chunk;
		NORMAL = lit_normal;
		sheen = gust * wind;
	}
	vec3 shown = mask.rgb;
	if (soil_strength > 0.001) {
		// As terrain_ground.gdshader mixes it, from a blurred mip so a clump is one colour.
		vec3 earth = textureLod(soil, root.xz / soil_metres, 3.0).rgb;
		shown = mix(shown, earth * mix(vec3(1.0), mask.rgb * 1.7, 0.15), soil_strength);
	}
	ground_colour = shown;
	blade_colour = mix(shown, mask.rgb, greenness);
}

void fragment() {
	float up = clamp(UV.y, 0.0, 1.0);
	vec3 tip = blade_colour * tip_tint;
	vec3 colour = mix(ground_colour * root_shade, tip, smoothstep(0.0, 0.7, up));
	float grey = dot(colour, vec3(0.299, 0.587, 0.114));
	colour = max(mix(vec3(grey), colour, saturation), vec3(0.0));
	colour *= 1.0 + sheen * gust_sheen * up;
	ALBEDO = colour;
	ROUGHNESS = 1.0;
	// A blade's back face would otherwise be lit from below (the engine flips its normal) and go black.
	NORMAL = normalize((VIEW_MATRIX * vec4(lit_normal, 0.0)).xyz);
	BACKLIGHT = colour * translucency;
}
