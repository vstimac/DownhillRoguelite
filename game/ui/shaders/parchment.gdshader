// Aged parchment for every UI panel: warm paper with grain and blotches, darker towards the edges,
// a hand-inked border that wobbles and an irregular, slightly torn outline. Works at any size:
// `rect_size` is the control's size in pixels so the border and grain keep their scale.
shader_type canvas_item;

uniform vec2 rect_size = vec2(200.0, 200.0);
uniform vec4 paper_color : source_color = vec4(0.86, 0.79, 0.64, 1.0);
uniform vec4 paper_dark : source_color = vec4(0.62, 0.52, 0.38, 1.0);
uniform vec4 ink_color : source_color = vec4(0.17, 0.12, 0.08, 1.0);
uniform float border_px = 4.0;
uniform float tear_px = 5.0;
uniform float seed = 0.0;
uniform float shade = 1.0;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21) + seed);
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += noise(p) * a;
		p *= 2.1;
		a *= 0.5;
	}
	return v;
}

void fragment() {
	vec2 px = UV * rect_size;
	// Distance to the nearest edge, pushed in and out by noise along the edge: a torn outline.
	float edge = min(min(px.x, rect_size.x - px.x), min(px.y, rect_size.y - px.y));
	float along = (px.x + px.y) * 0.08;
	float wobble = (fbm(vec2(along, seed * 3.0)) - 0.5) * 2.0 * tear_px;
	float d = edge + wobble - tear_px;
	if (d < 0.0) {
		discard;
	}
	// Paper: grain, soft blotches, darker towards the edges.
	float grain = noise(px * 0.9) * 0.08 + fbm(px * 0.035) * 0.25;
	float vignette = smoothstep(0.0, min(rect_size.x, rect_size.y) * 0.45, d);
	vec3 paper = mix(paper_dark.rgb, paper_color.rgb, clamp(0.35 + 0.65 * vignette - grain * 0.6 + 0.15, 0.0, 1.0));
	// Hand-inked border: a band whose width wobbles a little.
	float band = border_px * (0.75 + 0.5 * noise(vec2(along * 1.7, 9.0 + seed)));
	float ink = 1.0 - smoothstep(band - 0.8, band + 0.8, d);
	vec3 colour = mix(paper, ink_color.rgb, ink * 0.92);
	COLOR = vec4(colour * shade, 1.0) * COLOR;
}
