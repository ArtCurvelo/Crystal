// Lago luminoso: ondulações animadas, cáusticas brilhantes e transparência leve.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;

uniform vec4 deep_color : source_color = vec4(0.02, 0.12, 0.2, 1.0);
uniform vec4 shallow_color : source_color = vec4(0.1, 0.5, 0.6, 1.0);
uniform vec4 glow_color : source_color = vec4(0.2, 0.9, 1.0, 1.0);
uniform float scale = 10.0;

void fragment() {
	vec2 uv = UV * scale;
	float t = TIME;
	float w = sin(uv.x * 1.3 + t * 0.8) + sin(uv.y * 1.7 - t * 0.6) + sin((uv.x + uv.y) * 0.9 + t * 1.1);
	float w2 = sin(uv.x * 3.1 - t * 1.4 + sin(uv.y * 2.3 + t)) * 0.5;
	float caustic = smoothstep(1.4, 2.6, w + w2);
	ALBEDO = mix(deep_color.rgb, shallow_color.rgb, 0.5 + 0.25 * w);
	EMISSION = glow_color.rgb * (0.18 + caustic * 0.9);
	ROUGHNESS = 0.05;
	METALLIC = 0.35;
	ALPHA = 0.8;
	NORMAL_MAP = normalize(vec3(0.5 + 0.08 * cos(uv.x * 1.3 + t * 0.8), 0.5 + 0.08 * cos(uv.y * 1.7 - t * 0.6), 1.0));
}
