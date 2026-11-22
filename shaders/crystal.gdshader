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
	// cada face com luz própria (as normais do prisma são por face): as facetas aparecem, em vez de uma mancha
	float face = 0.5 + 0.5 * pow(abs(dot(n, normalize(vec3(0.45, 0.75, 0.5)))), 1.4);
	ALBEDO = facet * (0.45 + 0.55 * h) * face;

	// rachaduras: projeção lateral (xz/y) para cobrir o prisma.
	// Só calcula o Voronoi (caro) em cristal danificado: a grande maioria está inteira.
	float crack = 0.0;
	if (damage > 0.01) {
		vec2 uv = vec2(UV.x * 5.6, UV.y * 2.2) * 2.4 + seed;
		vec2 v = voronoi(uv);
		float reveal = step(v.y, damage * 1.15);
		float width = 0.025 + 0.06 * damage;
		crack = (1.0 - smoothstep(0.0, width, v.x)) * reveal;
	}

	// cintilância de gema lapidada: pontinhos que acendem conforme o ângulo de quem olha
	vec3 gc = floor(lpos * 24.0 + seed);
	float gh = fract(sin(dot(gc, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
	float tw = fract(gh * 7.13 + dot(normalize(VIEW), vec3(3.1, 2.3, 1.7)) * 1.4 + TIME * 0.12);
	float glint = step(0.93, gh) * smoothstep(0.82, 1.0, tw) * (0.4 + fres);

	float inner = glow_energy * (0.25 + 0.75 * h * h);
	EMISSION = glow_color.rgb * (inner * face + fres * 0.7)
		+ mix(glow_color.rgb, vec3(1.0), 0.45) * crack * (1.5 + 2.5 * damage)
		+ mix(glow_color.rgb, vec3(1.0), 0.6) * glint * 2.2
		+ vec3(flash) * 0.6;
	ROUGHNESS = 0.12;
	METALLIC = 0.15;
	SPECULAR = 0.9;
}
