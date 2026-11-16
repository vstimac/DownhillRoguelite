// The mountain's one look for everything placed on it (environment/WorldStyle): a muted natural
// colour from the palette, broken up by a real ground texture (rock or soil) projected from three
// sides, so no surface is a flat, untextured colour. Models that bring their own colour atlas
// (the art packs' "colormap") keep only its light and dark, never its hue.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform vec4 tint : source_color = vec4(0.5, 0.5, 0.5, 1.0);
uniform sampler2D detail : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
/// The detail texture repeats every this many metres; dividing by its mean brightness keeps the tint's level.
uniform float detail_metres = 1.5;
uniform float detail_mean = 0.4;
uniform float detail_strength = 1.0;
/// The model's own atlas: only its brightness shades the tint (0 ignores it).
uniform sampler2D source : source_color, filter_linear_mipmap, repeat_enable;
uniform float source_strength = 0.0;
/// Animals project the texture in their own space, so it doesn't slide over them as they move.
uniform bool object_space = false;
uniform float roughness_value = 0.95;
uniform float metallic_value = 0.0;

varying vec3 tri_position;
varying vec3 tri_normal;

void vertex() {
	if (object_space) {
		tri_position = VERTEX;
		tri_normal = NORMAL;
	} else {
		tri_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		tri_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	}
}

vec3 triplanar(vec3 p, vec3 w) {
	return texture(detail, p.zy).rgb * w.x + texture(detail, p.xz).rgb * w.y + texture(detail, p.xy).rgb * w.z;
}

void fragment() {
	vec3 w = pow(abs(normalize(tri_normal)), vec3(4.0));
	w /= max(w.x + w.y + w.z, 0.0001);
	vec3 grain = triplanar(tri_position / max(detail_metres, 0.01), w);
	// Only the texture's light and dark: the hue is always the palette's.
	float grain_light = dot(grain, vec3(0.299, 0.587, 0.114)) / max(detail_mean, 0.01);
	vec3 colour = tint.rgb * mix(1.0, grain_light, detail_strength);
	if (source_strength > 0.0) {
		float light = dot(texture(source, UV).rgb, vec3(0.299, 0.587, 0.114));
		colour *= mix(1.0, clamp(light / 0.55, 0.45, 1.45), source_strength);
	}
	ALBEDO = clamp(colour, vec3(0.0), vec3(1.0));
	ROUGHNESS = roughness_value;
	METALLIC = metallic_value;
}
