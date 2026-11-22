// Ground fog: a ray-marched layer of fog lying on the terrain, drawn from the inside of a box
// around the simulated patch (cull_front, so it works with the camera inside or outside).
// Per pixel the ray runs from where it enters the box to the nearest of: the box's far side,
// the scene's depth (so trees and the carrier stand in it, softly) and max_distance. Over open air
// it strides quickly toward the layer; inside it takes fine steps, adding light and absorbing it
// (Beer-Lambert) by the density the simulation gives (the last two steps blended), its depth over
// the ground, and wisps of 3D noise carried along the flow (two phases of a flow map, so they
// never stretch). The patch fades out at its edges and over ground not yet sampled.
shader_type spatial;
render_mode unshaded, cull_front, depth_test_disabled, depth_draw_never, blend_mix, shadows_disabled, fog_disabled;

uniform sampler2D fog_now : filter_linear, repeat_disable;
uniform sampler2D fog_prev : filter_linear, repeat_disable;
uniform sampler2D ground : filter_linear, repeat_disable;
uniform sampler3D wisps : filter_linear, repeat_enable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;

uniform vec2 now_origin;
uniform vec2 prev_origin;
uniform float patch_size = 96.0;
uniform float blend = 1.0;
uniform vec3 box_min;
uniform vec3 box_max;

uniform float depth = 1.1;
uniform float density = 1.6;
uniform float max_density = 2.2;
uniform vec3 fog_colour : source_color = vec3(0.86, 0.88, 0.9);
uniform float top_light = 0.35;
uniform float noise_size = 3.0;
uniform float noise_amount = 0.65;
uniform float flow_period = 2.0;
uniform float ragged_top = 0.45;
uniform int steps = 40;
uniform float max_distance = 60.0;
uniform float fine_step = 0.22;
// Inside the fog, steps grow with distance by this share of it (far fog needs less detail).
uniform float distance_step = 0.035;
uniform float edge_fade = 18.0;
// The fog thins out from this share of max_distance to nothing at max_distance, so it has no far edge.
uniform float far_fade = 0.55;
// The fog thins toward the camera inside this distance (m), so the view never starts in a white wall.
uniform float near_clear = 3.5;
// How much of the layer's height the top thins over (0: a hard top, 1: thinning from the ground up).
uniform float top_soft = 0.7;
// 0 off, 1 density (blue thin to red thick), 2 flow (direction as colour), 3 ground (1 m bands, grey where unsampled).
uniform int debug_mode = 0;

// A fixed per-pixel offset for where the steps fall (interleaved gradient noise): no banding, and still between frames.
float interleaved(vec2 pixel) {
	return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
}

float wisp_noise(vec3 p, vec2 flow) {
	float t0 = fract(TIME / flow_period);
	float t1 = fract(TIME / flow_period + 0.5);
	float mixing = abs(0.5 - t0) * 2.0;
	vec3 scaled = vec3(p.x, p.y * 1.6, p.z) / noise_size;
	vec3 drift = vec3(0.0, -TIME * 0.02, 0.0);
	vec3 a = scaled - vec3(flow.x, 0.0, flow.y) * (t0 * flow_period / noise_size) + drift;
	vec3 b = scaled - vec3(flow.x, 0.0, flow.y) * (t1 * flow_period / noise_size) + drift + vec3(0.37, 0.11, 0.71);
	float carried = mix(texture(wisps, a).r, texture(wisps, b).r, mixing);
	float fine = texture(wisps, scaled * 2.3 + vec3(TIME * 0.013, 0.0, -TIME * 0.017)).r;
	return carried * 0.68 + fine * 0.32;
}

vec3 debug_tint(vec4 cell, float height, float shown) {
	if (debug_mode == 1) {
		float d = clamp(cell.r / max_density, 0.0, 1.0);
		return mix(vec3(0.1, 0.3, 1.0), vec3(1.0, 0.15, 0.05), d);
	}
	if (debug_mode == 2) {
		return vec3(0.5) + vec3(cell.g, 0.0, cell.b) * 0.25;
	}
	float band = step(0.5, fract(height));
	return mix(vec3(0.5), mix(vec3(0.2, 0.7, 0.3), vec3(0.9, 0.9, 0.3), band), shown);
}

// How much of the layer is at this height over the ground (1 inside, thinning toward its ragged top).
float layer_body(float above, float top) {
	float body = 1.0 - smoothstep(top * (1.0 - top_soft), top + 0.1, above);
	// Fog may sit a little below the sampled ground: the ray already ends at the real surface, and a
	// hard cut here drew the cells' coarser ground as contour stripes across the slope.
	return body * smoothstep(-1.0, -0.2, above);
}

void fragment() {
	vec3 eye = INV_VIEW_MATRIX[3].xyz;
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(normalize(VERTEX), 0.0)).xyz);

	// How far the scene is along this ray.
	float raw = texture(depth_texture, SCREEN_UV).r;
	vec4 seen = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
	seen.xyz /= seen.w;
	float scene = length(seen.xyz);

	// Where the ray is inside the box.
	vec3 safe = sign(dir) * max(abs(dir), vec3(1e-5)) + vec3(equal(dir, vec3(0.0))) * 1e-5;
	vec3 inv = 1.0 / safe;
	vec3 t0 = (box_min - eye) * inv;
	vec3 t1 = (box_max - eye) * inv;
	vec3 near = min(t0, t1);
	vec3 far = max(t0, t1);
	float enter = max(max(max(near.x, near.y), near.z), 0.0);
	float leave = min(min(min(far.x, far.y), far.z), min(scene, max_distance));
	if (leave <= enter) {
		discard;
	}

	float tallest = depth * max_density * (1.0 + ragged_top) + 0.3;
	// A step inside the layer crosses at most a third of its usual depth (looking down it is crossed steeply).
	float layer_step = max(fine_step, depth * 0.35 / max(abs(dir.y), 0.05));
	float t = enter + interleaved(FRAGCOORD.xy) * fine_step;
	float through = 1.0;
	vec3 light = vec3(0.0);
	for (int i = 0; i < steps; i++) {
		if (t >= leave || through < 0.02) {
			break;
		}
		vec3 p = eye + dir * t;
		vec2 uv = (p.xz - now_origin) / patch_size;
		float height = texture(ground, uv).r;
		float above = p.y - height;
		float stride = min(max(fine_step, t * distance_step), layer_step);
		// Never fewer steps left than the ray needs to reach its end: a ray that ran out of steps (a
		// low or slanting view along the slope crosses the layer for a long way) ended the fog at a
		// distance that moved with the camera's angle.
		stride = max(stride, (leave - t) / float(steps - i));
		if (above > tallest) {
			// Over open air: stride toward the layer (the ground may rise ahead, so not all the way).
			stride = max(min((above - tallest) * 0.6, max(4.0, t * 0.1)), stride);
		} else if (above > -1.0) {
			// The step's middle gives the fog there; the layer's height profile is averaged over the
			// whole step (its start, middle and end, Simpson's rule). A point per step let the thin
			// layer fall between samples at the same distance on every ray: rings round the camera
			// and stripes on far slopes (a per-pixel jitter only turned them into a crawling grain).
			stride = min(stride, leave - t);
			float above_start = above;
			vec3 p_end = p + dir * stride;
			float above_end = p_end.y - texture(ground, (p_end.xz - now_origin) / patch_size).r;
			p = p + dir * (stride * 0.5);
			uv = (p.xz - now_origin) / patch_size;
			height = texture(ground, uv).r;
			above = p.y - height;
			vec4 now = texture(fog_now, uv);
			vec4 before = texture(fog_prev, (p.xz - prev_origin) / patch_size);
			vec4 cell = mix(before, now, blend);
			vec2 edges = min(uv, vec2(1.0) - uv) * patch_size;
			// A round fade inside the square patch and a fade with distance: no straight edge or wall at the horizon.
			float from_middle = length(uv - vec2(0.5)) * patch_size;
			float round_edge = smoothstep(0.0, edge_fade, patch_size * 0.5 - from_middle);
			float far = 1.0 - smoothstep(max_distance * far_fade, max_distance, t);
			float near = smoothstep(near_clear * 0.25, near_clear, t);
			float shown = smoothstep(0.6, 1.0, now.a) * round_edge * far * near;
			float n = smoothstep(0.2, 0.8, wisp_noise(p, cell.gb));
			float top = depth * cell.r * (1.0 + ragged_top * (n - 0.5) * 2.0);
			float body = (layer_body(above_start, top) + 4.0 * layer_body(above, top) + layer_body(above_end, top)) / 6.0;
			float thick = mix(1.0 - noise_amount, 1.0 + noise_amount, n);
			float sigma = density * max(cell.r, 0.0) * thick * body * shown;
			if (debug_mode != 0) {
				sigma = max(sigma, 0.6 * body * smoothstep(0.0, edge_fade, min(edges.x, edges.y)));
			}
			if (sigma > 0.0001) {
				float share = 1.0 - exp(-sigma * stride);
				float lift = clamp(above / max(top, 0.05), 0.0, 1.0);
				vec3 colour = fog_colour * mix(1.0 - top_light, 1.0, lift);
				if (debug_mode != 0) {
					colour = debug_tint(cell, height, shown);
				}
				light += through * share * colour;
				through *= 1.0 - share;
			}
		}
		t += stride;
	}
	float alpha = 1.0 - through;
	if (alpha < 0.002) {
		discard;
	}
	ALBEDO = light / alpha;
	ALPHA = alpha;
}
