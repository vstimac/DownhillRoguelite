// Turns a rendered item (a 3D model photographed on a transparent background) into an illustrated
// icon: colours flattened into a few muted, sepia-leaning tones and a dark hand-inked outline
// around the silhouette, so icons match the parchment UI.
shader_type canvas_item;

uniform vec4 ink_color : source_color = vec4(0.15, 0.1, 0.06, 1.0);
uniform vec4 tint : source_color = vec4(0.93, 0.83, 0.65, 1.0);
uniform float outline_px = 2.0;
uniform float levels = 4.0;
uniform float sepia = 0.45;
uniform float dim = 1.0;

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	// Outline: transparent pixels next to the silhouette turn to ink.
	float near = 0.0;
	vec2 px_step = TEXTURE_PIXEL_SIZE * outline_px;
	for (int i = 0; i < 8; i++) {
		float a = float(i) * 0.785398;
		near = max(near, texture(TEXTURE, UV + vec2(cos(a), sin(a)) * px_step).a);
	}
	if (c.a < 0.5) {
		COLOR = vec4(ink_color.rgb, near * ink_color.a * dim);
	} else {
		vec3 flat_col = floor(c.rgb * levels + 0.5) / levels;
		float grey = dot(flat_col, vec3(0.3, 0.59, 0.11));
		vec3 warm = mix(flat_col, vec3(grey) * tint.rgb * 1.15, sepia);
		COLOR = vec4(warm * dim, 1.0);
	}
}
