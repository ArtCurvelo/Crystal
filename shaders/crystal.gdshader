// Cristal facetado com brilho interno, borda fresnel e rachaduras luminosas
// que se espalham conforme o dano. Compatível com o renderizador Compatibility.
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_lambert, specular_schlick_ggx;

uniform vec4 base_color : source_color = vec4(0.5, 0.9, 1.0, 1.0);
uniform vec4 glow_color : source_color = vec4(0.2, 0.8, 1.0, 1.0);
uniform float glow_energy = 1.0;
uniform float damage : hint_range(0.0, 1.0) = 0.0;
uniform float flash = 0.0;
uniform float seed = 0.0;

varying vec3 lpos;

void vertex() {
	lpos = VERTEX;
}

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}

// distância até a borda da célula de Voronoi mais próxima (x) e valor aleatório da célula (y)
vec2 voronoi(vec2 x) {
	vec2 n = floor(x);
	vec2 f = fract(x);
	vec2 mg, mr;
	float md = 8.0;
	for (int j = -1; j <= 1; j++)
	for (int i = -1; i <= 1; i++) {
		vec2 g = vec2(float(i), float(j));
		vec2 o = hash2(n + g);
		vec2 r = g + o - f;
		float d = dot(r, r);
		if (d < md) { md = d; mr = r; mg = g; }
	}
	md = 8.0;
	for (int j = -1; j <= 1; j++)
	for (int i = -1; i <= 1; i++) {
		vec2 g = mg + vec2(float(i), float(j));
		vec2 o = hash2(n + g);
		vec2 r = g + o - f;
		if (dot(mr - r, mr - r) > 0.00001)
			md = min(md, dot(0.5 * (mr + r), normalize(r - mr)));
	}
	return vec2(md, hash2(n + mg).x);
}

void fragment() {
	vec3 n = normalize(NORMAL);
	float fres = pow(1.0 - clamp(dot(n, normalize(VIEW)), 0.0, 1.0), 2.2);
	float h = clamp(UV.y, 0.0, 1.0);
	vec3 facet = base_color.rgb * COLOR.rgb;
	ALBEDO = facet * (0.45 + 0.55 * h);

	// rachaduras: projeção lateral (xz/y) para cobrir o prisma
	vec2 uv = vec2(UV.x * 5.6, UV.y * 2.2) * 2.4 + seed;
	vec2 v = voronoi(uv);
	float reveal = step(v.y, damage * 1.15);
	float width = 0.025 + 0.06 * damage;
	float crack = (1.0 - smoothstep(0.0, width, v.x)) * reveal * step(0.01, damage);

	float inner = glow_energy * (0.25 + 0.75 * h * h);
	EMISSION = glow_color.rgb * (inner + fres * 1.1)
		+ mix(glow_color.rgb, vec3(1.0), 0.45) * crack * (1.5 + 2.5 * damage)
		+ vec3(flash) * 0.6;
	ROUGHNESS = 0.12;
	METALLIC = 0.15;
	SPECULAR = 0.9;
}
