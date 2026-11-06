// Sky for the descent: a gradient driven by DayNightCycle, a sun disc by day that becomes a pale
// moon at night (the same light moves), stars that come out in the dark and drifting clouds whose
// cover follows the weather (overcast). Everything here is driven by uniforms, not TIME, so
// DayNightCycle decides when the sky changes.
shader_type sky;

uniform vec3 top_color : source_color = vec3(0.32, 0.33, 0.36);
uniform vec3 horizon_color : source_color = vec3(0.55, 0.55, 0.56);
uniform vec3 ground_color : source_color = vec3(0.12, 0.12, 0.13);
uniform float darkness : hint_range(0.0, 1.0) = 0.0;
uniform float overcast : hint_range(0.0, 1.0) = 0.0;
uniform float base_clouds : hint_range(0.0, 1.0) = 0.35;
uniform vec3 cloud_color : source_color = vec3(0.78, 0.78, 0.8);
uniform vec3 cloud_night_color : source_color = vec3(0.05, 0.055, 0.07);
uniform vec2 cloud_offset = vec2(0.0);
uniform float cloud_scale = 0.6;
uniform float star_density = 0.0025;
uniform float star_brightness = 1.6;
uniform float twinkle_time = 0.0;
uniform vec3 sun_disc_color : source_color = vec3(1.0, 0.9, 0.75);
uniform vec3 moon_disc_color : source_color = vec3(0.8, 0.85, 1.0);
uniform float sun_size = 0.9995;
uniform float moon_size = 0.9996;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p) {
	float total = 0.0;
	float amplitude = 0.5;
	for (int i = 0; i < 5; i++) {
		total += value_noise(p) * amplitude;
		p = p * 2.03 + vec2(17.1, 9.2);
		amplitude *= 0.5;
	}
	return total;
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	float up = dir.y;

	vec3 color;
	if (up >= 0.0) {
		color = mix(horizon_color, top_color, pow(up, 0.6));
	} else {
		color = mix(horizon_color, ground_color, clamp(-up * 4.0, 0.0, 1.0));
	}

	float cover = clamp(base_clouds + overcast * 0.9, 0.0, 1.0);

	if (up > 0.0) {
		// Stars, hidden by clouds and daylight.
		if (!AT_CUBEMAP_PASS) {
			vec2 cell = floor(vec2(atan(dir.z, dir.x) * 180.0, asin(up) * 180.0));
			float star = step(1.0 - star_density, hash12(cell));
			float twinkle = 0.6 + 0.4 * sin(twinkle_time * (1.0 + hash12(cell + 3.0) * 4.0) + hash12(cell) * 50.0);
			float stars_visible = darkness * darkness * (1.0 - cover);
			color += vec3(star * twinkle * star_brightness * stars_visible) * smoothstep(0.0, 0.15, up);
		}

		// Sun by day, moon by night: the same light, drawn as a disc.
		if (LIGHT0_ENABLED) {
			float facing = dot(dir, LIGHT0_DIRECTION);
			float size = mix(sun_size, moon_size, darkness);
			float disc = smoothstep(size, size + 0.0002, facing);
			float glow = pow(max(facing, 0.0), 64.0) * (1.0 - darkness) * 0.35;
			vec3 disc_color = mix(sun_disc_color * 6.0, moon_disc_color * 1.5, darkness);
			color += (disc * disc_color + glow * sun_disc_color) * (1.0 - cover * 0.9);
		}

		// Clouds on a flat layer above.
		vec2 uv = dir.xz / max(up, 0.05) * cloud_scale + cloud_offset;
		float density = fbm(uv);
		float threshold = mix(0.75, 0.25, cover);
		float cloud = smoothstep(threshold, threshold + 0.25, density) * smoothstep(0.0, 0.2, up);
		vec3 lit = mix(cloud_color, cloud_night_color, darkness);
		color = mix(color, lit * (0.8 + 0.4 * density), cloud * 0.9);
	}

	COLOR = color;
}
