// Turns a rendered item (a 3D model photographed on a transparent background) into an icon for the
// expedition UI: colours flattened into a few cool, muted tones, with a thin dark outline round the
// silhouette so it stands off the dark panels. (Named for the inked style it replaced.)
shader_type canvas_item;

uniform vec4 ink_color : source_color = vec4(0.02, 0.03, 0.04, 0.85);
uniform vec4 tint : source_color = vec4(0.82, 0.88, 0.92, 1.0);
uniform float outline_px = 1.5;
uniform float levels = 5.0;
uniform float sepia = 0.3;
uniform float dim = 1.0;

void fragment() {
	vec4 c = texture(TEXTURE, UV);
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
		vec3 cool = mix(flat_col, vec3(grey) * tint.rgb * 1.2, sepia);
		COLOR = vec4(cool * dim, 1.0);
	}
}
