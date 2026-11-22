// Scattered rocks that grow out of the ground instead of standing on it: the rock texture in world
// space (as the boulders' material), with the ground the rock stands on (its colour, snow included,
// sent per instance) laid over its upward faces, and a soiled, darker foot where it meets the ground.
// Per instance (MultiMesh custom data, or for a single boulder the ground_here instance uniform):
// rgb the ground's colour there (0 for none), a how far the ground lies above the model's origin
// along its up (m).
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_opaque;

uniform sampler2D rock_colour : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float metres = 1.5;
uniform float normal_strength = 0.5;
// The palette's tint (WorldStyle reads it): the texture is shown as it is.
uniform vec4 tint : source_color = vec4(0.5, 0.5, 0.52, 1.0);
// Faces turned up this far (normal's y) take the ground's look, fully from top_end.
uniform float top_start = 0.5;
uniform float top_end = 0.85;
uniform float top_amount = 0.7;
// The foot: this high over the ground (m) the rock is soiled and darkened toward the ground's colour.
uniform float foot_height = 0.3;
uniform float foot_soil = 0.55;
uniform float foot_dark = 0.35;
// A boulder drawn on its own (not in a MultiMesh) gets its ground from the instance uniform.
uniform bool from_instance = false;
instance uniform vec4 ground_here = vec4(0.0);

varying vec3 world_pos;
varying vec3 world_normal;
varying vec4 ground;
varying float above;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize(mat3(MODEL_MATRIX) * NORMAL);
	ground = from_instance ? ground_here : INSTANCE_CUSTOM;
	vec3 up = normalize(MODEL_MATRIX[1].xyz);
	above = dot(world_pos - MODEL_MATRIX[3].xyz, up) - ground.a;
}

void fragment() {
	vec3 w = pow(abs(world_normal), vec3(4.0));
	w /= max(w.x + w.y + w.z, 0.0001);
	vec2 ux = world_pos.zy / metres;
	vec2 uy = world_pos.xz / metres;
	vec2 uz = world_pos.xy / metres;
	vec3 rock = texture(rock_colour, ux).rgb * w.x + texture(rock_colour, uy).rgb * w.y + texture(rock_colour, uz).rgb * w.z;

	// Triplanar normal (each plane's tangent normal swizzled onto the world axes, added to the face's).
	vec2 nx = (texture(rock_normal, ux).rg * 2.0 - 1.0) * normal_strength;
	vec2 ny = (texture(rock_normal, uy).rg * 2.0 - 1.0) * normal_strength;
	vec2 nz = (texture(rock_normal, uz).rg * 2.0 - 1.0) * normal_strength;
	vec3 bumped = normalize(world_normal + w.x * vec3(0.0, nx.y, nx.x) * sign(world_normal.x)
		+ w.y * vec3(ny.x, 0.0, ny.y) * sign(world_normal.y) + w.z * vec3(nz.x, nz.y, 0.0) * sign(world_normal.z));
	NORMAL = normalize((VIEW_MATRIX * vec4(bumped, 0.0)).xyz);

	float has_ground = step(0.002, ground.r + ground.g + ground.b);
	float luma = dot(rock, vec3(0.3333));
	vec3 earth = ground.rgb * (0.7 + 0.6 * luma);
	float top = smoothstep(top_start, top_end, world_normal.y) * top_amount * has_ground;
	float foot = (1.0 - smoothstep(0.0, foot_height, above)) * has_ground;
	vec3 colour = mix(rock, earth, top);
	colour = mix(colour, earth, foot * foot_soil);
	colour *= 1.0 - foot * foot_dark;
	ALBEDO = colour;
	ROUGHNESS = 1.0;
}
