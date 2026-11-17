// Rocha da caverna: textura triplanar procedural (uma textura de ruído),
// chão mais escuro, estratos nas paredes e pontos minerais brilhantes.
shader_type spatial;
render_mode cull_back;

uniform vec3 rock_color : source_color = vec3(0.45, 0.3, 0.22);
uniform vec3 floor_color : source_color = vec3(0.25, 0.2, 0.17);
uniform vec3 accent : source_color = vec3(0.35, 0.9, 1.0);
uniform float speckle = 1.0;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;

varying vec3 wpos;
varying vec3 wnorm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

// projeção pelo eixo dominante (1 leitura por escala, em vez de 3 do triplanar completo)
vec2 proj(vec3 p, vec3 n) {
	vec3 a = abs(n);
	if (a.y > 0.6) return p.xz;
	return a.x > a.z ? p.zy : p.xy;
}

void fragment() {
	vec2 uv = proj(wpos, wnorm);
	float big = texture(noise_tex, uv * 0.045).r;
	float fine = texture(noise_tex, uv * 0.33).r;
	float up = smoothstep(0.55, 0.9, wnorm.y);
	float strata = 0.88 + 0.12 * sin(wpos.y * 2.6 + big * 7.0);
	vec3 col = mix(rock_color * strata, floor_color, up);
	col *= 0.55 + 0.75 * big;
	col *= 0.8 + 0.4 * fine;
	// fendas escuras
	float crack = smoothstep(0.47, 0.5, fine) * (1.0 - smoothstep(0.5, 0.53, fine));
	col *= 1.0 - crack * 0.5;
	ALBEDO = col;
	ROUGHNESS = 0.95;
	// pontos minerais nas paredes (legíveis mesmo sem luzes)
	float sp = smoothstep(0.78, 0.84, texture(noise_tex, uv * 0.9 + wpos.y * 0.37).r) * (1.0 - up);
	EMISSION = accent * sp * 0.7 * speckle + col * 0.08;
}
