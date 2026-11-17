// Cachoeira estilizada: faixas de espuma rolando para baixo, bordas suaves, aditiva.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

uniform vec4 water_color : source_color = vec4(0.4, 0.8, 1.0, 1.0);
uniform float speed = 1.4;
uniform float intensity = 0.8;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void fragment() {
	vec2 uv = UV;
	float stripes = noise(vec2(uv.x * 9.0, uv.y * 3.0 - TIME * speed * 3.0));
	stripes += 0.5 * noise(vec2(uv.x * 23.0, uv.y * 7.0 - TIME * speed * 5.0));
	float edge = smoothstep(0.0, 0.18, uv.x) * smoothstep(1.0, 0.82, uv.x);
	float fade = smoothstep(0.0, 0.08, uv.y) * smoothstep(1.0, 0.9, uv.y);
	float foam = smoothstep(0.55, 1.2, stripes);
	ALBEDO = water_color.rgb * (0.35 + foam * 0.9) * intensity * edge * fade;
}
