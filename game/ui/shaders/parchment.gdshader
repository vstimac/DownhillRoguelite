// The expedition panel behind every UI window and card: a dark, slightly see-through slate with a
// hairline frame and a bright rule down its left edge (the one accent line), and a faint grain so
// large panels don't read as flat plastic. Works at any size: `rect_size` is the control's size in
// pixels so the lines keep their width. (Named for the parchment it replaced; callers are unchanged.)
// border_px 0 draws the fill alone (a shadow); tear_px is kept for callers and unused.
shader_type canvas_item;

uniform vec2 rect_size = vec2(200.0, 200.0);
uniform vec4 panel_color : source_color = vec4(0.047, 0.063, 0.078, 0.86);
uniform vec4 panel_top : source_color = vec4(0.08, 0.1, 0.118, 0.86);
uniform vec4 line_color : source_color = vec4(0.91, 0.93, 0.95, 1.0);
uniform float border_px = 4.0;
uniform float tear_px = 5.0;
uniform float seed = 0.0;
uniform float shade = 1.0;

void fragment() {
	vec2 px = UV * rect_size;
	// A little lighter at the top, as if lit from above.
	vec4 fill = mix(panel_top, panel_color, smoothstep(0.0, max(rect_size.y, 1.0), px.y));
	float grain = fract(sin(dot(floor(px * 0.5), vec2(12.9898, 78.233)) + seed) * 43758.5453) * 0.018;
	vec3 colour = fill.rgb + grain;
	float alpha = fill.a;
	if (border_px > 0.0) {
		// A hairline frame round the whole panel, faint.
		float edge = min(min(px.x, rect_size.x - px.x), min(px.y, rect_size.y - px.y));
		float frame = 1.0 - smoothstep(0.6, 1.4, edge);
		colour = mix(colour, line_color.rgb, frame * 0.22);
		alpha = max(alpha, frame * 0.5);
		// The rule down the left edge: two pixels, full strength.
		float rule = 1.0 - smoothstep(1.6, 2.4, px.x);
		colour = mix(colour, line_color.rgb, rule * 0.9);
		alpha = max(alpha, rule);
	}
	COLOR = vec4(colour * shade, alpha) * COLOR;
}
