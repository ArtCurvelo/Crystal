"""HQ de abertura, versão 2: desenho com sombreamento em tons (cel shading), luz de contorno,
traço com peso (mais grosso no lado da sombra), retícula, textura de papel e cenários em camadas.
Gera assets/story/panel_1..6.png (1600x900).  Uso: python3 tools/comic2.py [pasta]"""
import math, os, random, sys
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageFont

W, H = 1600, 900
K = 2
SW, SH = W * K, H * K
INK = (24, 17, 26)
FONT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "fonts", "ChakraPetch-Bold.ttf")

def s(v): return int(round(v * K))

def mix(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))
def shade(c, k=0.62, tint=(60, 40, 110)):
    """Sombra: escurece puxando para um tom frio (fica mais 'pintado' que só preto)."""
    d = tuple(int(v * k) for v in c[:3])
    return mix(d, tint, 0.18)
def light(c, k=0.3): return mix(c[:3], (255, 250, 235), k)

# ------------------------------------------------------------------ tela e formas sombreadas
class Canvas:
    def __init__(self, bg):
        self.img = bg.convert("RGBA")
        # camadas para a cutscene animada: cenário (bg), personagens (fg) e textos de efeito (fx)
        self.layers = {"bg": self.img, "fg": Image.new("RGBA", self.img.size, (0, 0, 0, 0)), "fx": Image.new("RGBA", self.img.size, (0, 0, 0, 0))}
        self.light = (-0.6, -0.8)      # luz vindo de cima/esquerda (sombra para baixo/direita)
        self.rim = None                # cor da luz de contorno (do outro lado)

    # partes: ('poly', pts) | ('ell', cx, cy, rx, ry) | ('rect', x0, y0, x1, y1, r) | ('pie', cx, cy, rx, ry, a0, a1)
    @staticmethod
    def _bbox(parts):
        xs, ys = [], []
        for p in parts:
            if p[0] == 'poly':
                xs += [q[0] for q in p[1]]; ys += [q[1] for q in p[1]]
            elif p[0] in ('ell', 'pie'):
                xs += [p[1] - p[3], p[1] + p[3]]; ys += [p[2] - p[4], p[2] + p[4]]
            else:
                xs += [p[1], p[3]]; ys += [p[2], p[4]]
        return min(xs), min(ys), max(xs), max(ys)

    @staticmethod
    def _draw(d, parts, ox, oy, fill=255):
        for p in parts:
            if p[0] == 'poly':
                d.polygon([(s(x) - ox, s(y) - oy) for x, y in p[1]], fill=fill)
            elif p[0] == 'ell':
                _, cx, cy, rx, ry = p
                d.ellipse([s(cx - rx) - ox, s(cy - ry) - oy, s(cx + rx) - ox, s(cy + ry) - oy], fill=fill)
            elif p[0] == 'pie':
                _, cx, cy, rx, ry, a0, a1 = p
                d.pieslice([s(cx - rx) - ox, s(cy - ry) - oy, s(cx + rx) - ox, s(cy + ry) - oy], a0, a1, fill=fill)
            else:
                _, x0, y0, x1, y1, r = p
                d.rounded_rectangle([s(x0) - ox, s(y0) - oy, s(x1) - ox, s(y1) - oy], radius=s(r), fill=fill)

    def shape(self, parts, color, line=3.2, shadow=0.42, hi=0.12, rim=True, dots=False, ink=INK, shadow_k=0.64):
        """Forma com contorno de peso variável, sombra em tom frio, brilho e luz de contorno."""
        x0, y0, x1, y1 = self._bbox(parts)
        pad = s(line * 3 + 4)
        bx, by = s(x0) - pad, s(y0) - pad
        bw, bh = s(x1) - s(x0) + 2 * pad, s(y1) - s(y0) + 2 * pad
        if bw <= 2 or bh <= 2: return
        m = Image.new("L", (bw, bh), 0)
        self._draw(ImageDraw.Draw(m), parts, bx, by)
        size = min(x1 - x0, y1 - y0)
        lx, ly = self.light
        # sombra: o que sobra da forma quando ela é deslocada na direção da luz
        off = max(2.0, size * shadow * 0.5)
        sh = ImageChops.subtract(m, ImageChops.offset(m, s(-lx * off), s(-ly * off)))
        # brilho: tira fina no lado da luz
        offh = max(1.5, size * hi * 0.5)
        hl = ImageChops.subtract(m, ImageChops.offset(m, s(lx * offh), s(ly * offh)))
        # contorno: forma engordada (mais grossa no lado da sombra)
        lw = max(3, s(line)) | 1
        dil = m.filter(ImageFilter.MaxFilter(lw))
        heavy = ImageChops.offset(m, s(-lx * line * 0.45), s(-ly * line * 0.45)).filter(ImageFilter.MaxFilter(lw))
        outline = ImageChops.lighter(dil, heavy)
        tile = Image.new("RGBA", (bw, bh), (0, 0, 0, 0))
        tile.paste(ink + (255,), (0, 0), outline)
        tile.paste(tuple(color[:3]) + (255,), (0, 0), m)
        sc = shade(color, shadow_k)
        tile.paste(sc + (255,), (0, 0), sh)
        if dots:
            dm = Image.new("L", (bw, bh), 0)
            dd = ImageDraw.Draw(dm)
            st = s(7)
            for yy in range(0, bh, st):
                for xx in range((yy // st) % 2 * st // 2, bw, st):
                    dd.ellipse([xx - s(1.3), yy - s(1.3), xx + s(1.3), yy + s(1.3)], fill=255)
            tile.paste(shade(sc, 0.75) + (255,), (0, 0), ImageChops.multiply(dm, sh))
        tile.paste(light(color, 0.35) + (255,), (0, 0), ImageChops.subtract(hl, sh))
        if rim and self.rim:
            # luz de contorno colorida do lado oposto ao da luz principal
            offr = max(1.5, size * 0.06)
            rm = ImageChops.subtract(m, ImageChops.offset(m, s(-lx * offr), s(-ly * offr)))
            tile.paste(self.rim + (255,), (0, 0), rm)
        self.img.alpha_composite(tile, (bx, by))

    def flat(self, parts, color, alpha=255):
        lay = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        self._draw(ImageDraw.Draw(lay), parts, 0, 0, fill=tuple(color[:3]) + (alpha,))
        self.img.alpha_composite(lay)

    def line(self, pts, w=2.4, col=INK):
        d = ImageDraw.Draw(self.img)
        d.line([(s(x), s(y)) for x, y in pts], fill=col, width=max(1, s(w)), joint="curve")

    def glow(self, cx, cy, r, col, a=140):
        size = s(r * 2.6)
        lay = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        ImageDraw.Draw(lay).ellipse([size * 0.2, size * 0.2, size * 0.8, size * 0.8], fill=tuple(col) + (a,))
        lay = lay.filter(ImageFilter.GaussianBlur(size * 0.13))
        self.img.alpha_composite(lay, (s(cx) - size // 2, s(cy) - size // 2))

    def layer(self, name):
        """A partir daqui, desenha na camada 'bg', 'fg' ou 'fx'."""
        self.img = self.layers[name]

    def text(self, t, x, y, size, fill, angle=0, stroke=None, scene=False):
        """Texto: por padrão vai para a camada de efeitos (estoura na tela); scene=True fica no cenário/personagem."""
        target = self.img if scene else self.layers["fx"]
        f = ImageFont.truetype(FONT, s(size))
        lay = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(lay)
        d.text((s(x), s(y)), t, font=f, fill=fill, stroke_width=s(stroke if stroke is not None else size / 9), stroke_fill=INK, anchor="mm")
        if angle: lay = lay.rotate(angle, center=(s(x), s(y)), resample=Image.BICUBIC)
        target.alpha_composite(lay)

def limb(p0, p1, w0, w1=None):
    """Parte cilíndrica (braço/perna) afinando de w0 para w1, com pontas redondas."""
    w1 = w0 if w1 is None else w1
    (x0, y0), (x1, y1) = p0, p1
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy) or 1
    nx, ny = -dy / L, dx / L
    return [('poly', [(x0 + nx * w0 / 2, y0 + ny * w0 / 2), (x1 + nx * w1 / 2, y1 + ny * w1 / 2), (x1 - nx * w1 / 2, y1 - ny * w1 / 2), (x0 - nx * w0 / 2, y0 - ny * w0 / 2)]),
            ('ell', x0, y0, w0 / 2, w0 / 2), ('ell', x1, y1, w1 / 2, w1 / 2)]

def blob(cx, cy, rx, ry, n=14, jitter=0.0, seed=1, start=0.0):
    r = random.Random(seed)
    return [(cx + math.cos(start + i * math.tau / n) * rx * (1 + r.uniform(-jitter, jitter)), cy + math.sin(start + i * math.tau / n) * ry * (1 + r.uniform(-jitter, jitter))) for i in range(n)]

# ------------------------------------------------------------------ fundos
def gradient(top, bottom, mid=None, midpos=0.6):
    img = Image.new("RGB", (SW, SH))
    d = ImageDraw.Draw(img)
    for y in range(SH):
        t = y / SH
        if mid is None: c = mix(top, bottom, t)
        elif t < midpos: c = mix(top, mid, t / midpos)
        else: c = mix(mid, bottom, (t - midpos) / (1 - midpos))
        d.line([(0, y), (SW, y)], fill=c)
    return img

def paper(img, strength=10):
    """Grão de papel e leve vinheta: tira o aspecto 'digital liso'."""
    noise = Image.effect_noise((SW // 2, SH // 2), 40).resize((SW, SH), Image.BILINEAR).convert("L")
    grain = Image.merge("RGBA", (noise, noise, noise, Image.new("L", (SW, SH), strength)))
    img.alpha_composite(grain)
    vig = Image.new("L", (SW, SH), 0)
    ImageDraw.Draw(vig).ellipse([-SW * 0.15, -SH * 0.2, SW * 1.15, SH * 1.2], fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(SW * 0.08))
    dark = Image.new("RGBA", (SW, SH), (20, 10, 30, 0))
    dark.putalpha(ImageChops.invert(vig).point(lambda v: int(v * 0.55)))
    img.alpha_composite(dark)

def finish(cv, path):
    """Salva o quadro pronto (fallback e testes) e as 3 camadas para a cutscene animada."""
    bg, fg, fx = cv.layers["bg"], cv.layers["fg"], cv.layers["fx"]
    paper(bg)
    base = path[:-4]
    bg.convert("RGB").resize((W, H), Image.LANCZOS).save(base + "_bg.png", optimize=True)
    fg.resize((W, H), Image.LANCZOS).save(base + "_fg.png", optimize=True)
    fx.resize((W, H), Image.LANCZOS).save(base + "_fx.png", optimize=True)
    full = bg.copy()
    full.alpha_composite(fg)
    full.alpha_composite(fx)
    d = ImageDraw.Draw(full)
    d.rectangle([0, 0, SW - 1, SH - 1], outline=(250, 246, 235), width=s(16))
    d.rectangle([s(16), s(16), SW - s(16), SH - s(16)], outline=INK, width=s(5))
    full.convert("RGB").resize((W, H), Image.LANCZOS).save(path, optimize=True)

def mountains(cv, base, layers, seed=3):
    r = random.Random(seed)
    for i, (col, hmin, hmax) in enumerate(layers):
        pts = [(-60, base + 40)]
        x = -60
        while x < W + 120:
            x += r.uniform(90, 200)
            pts.append((x, base - r.uniform(hmin, hmax)))
        pts.append((W + 120, base + 40))
        cv.shape([('poly', pts)], col, line=2.2 if i < len(layers) - 1 else 3, shadow=0.08, hi=0.02, rim=False)

def timber_house(cv, x, y, w, h, wall, roof, beams=(70, 45, 32), lit=True, chimney=True, seed=1):
    """Casa enxaimel (a mesma arquitetura da vila do jogo)."""
    cv.shape([('rect', x, y - h, x + w, y, 2)], wall, line=3, shadow=0.25)
    for bx in (x, x + w * 0.5, x + w):
        cv.flat([('rect', bx - 5, y - h, bx + 5, y, 1)], beams)
    cv.flat([('rect', x, y - h * 0.52, x + w, y - h * 0.46, 1)], beams)
    for (ax, ay, bx2, by2) in ((x, y - h * 0.5, x + w * 0.25, y - h), (x + w * 0.5, y - h * 0.5, x + w * 0.25, y - h), (x + w * 0.5, y - h * 0.5, x + w * 0.75, y - h), (x + w, y - h * 0.5, x + w * 0.75, y - h)):
        cv.line([(ax, ay), (bx2, by2)], w=6, col=beams)
    if chimney:
        cv.shape([('rect', x + w * 0.66, y - h - h * 0.75, x + w * 0.78, y - h - h * 0.25, 2)], (150, 130, 120), line=2.6)
        for i in range(4):
            cv.shape([('ell', x + w * 0.72 + i * 14, y - h - h * 0.85 - i * 32, 14 + i * 6, 11 + i * 5)], (230, 226, 226), line=2, shadow=0.3, rim=False)
    cv.shape([('poly', [(x - w * 0.12, y - h + 4), (x + w / 2, y - h - h * 0.62), (x + w * 1.12, y - h + 4)])], roof, line=3, shadow=0.32)
    for i in range(1, 5):
        t = i / 5
        cv.line([(x - w * 0.12 + (w * 0.62) * t, y - h + 4 - (h * 0.66) * t), (x + w * 1.12 - (w * 0.62) * t, y - h + 4 - (h * 0.66) * t)], w=1.6, col=shade(roof, 0.7))
    cv.shape([('rect', x + w * 0.38, y - h * 0.42, x + w * 0.62, y, 3)], (110, 68, 40), line=2.6)
    for wx in (0.1, 0.72):
        col = (255, 214, 120) if lit else (70, 85, 110)
        cv.shape([('rect', x + w * wx, y - h * 0.86, x + w * (wx + 0.18), y - h * 0.6, 2)], col, line=2.4, shadow=0.05, rim=False)
        if lit: cv.glow(x + w * (wx + 0.09), y - h * 0.73, 26, (255, 200, 110), 90)
        cv.line([(x + w * (wx + 0.09), y - h * 0.86), (x + w * (wx + 0.09), y - h * 0.6)], w=2.2, col=beams)
        cv.shape([('rect', x + w * wx - 4, y - h * 0.6, x + w * (wx + 0.18) + 4, y - h * 0.55, 2)], (120, 80, 48), line=2)
        r = random.Random(seed * 13 + int(wx * 10))
        for k in range(5):
            cv.shape([('ell', x + w * wx + 6 + k * (w * 0.18 - 8) / 4, y - h * 0.6 - 5, 6, 6)], [(240, 70, 90), (255, 200, 60), (250, 250, 250), (180, 90, 230)][r.randrange(4)], line=1.6, shadow=0.3, rim=False)

def bunting(cv, x0, y0, x1, y1, sag=40, n=14):
    cols = [(230, 60, 60), (255, 205, 60), (60, 150, 230), (80, 190, 90), (230, 120, 210), (255, 140, 40)]
    pts = []
    for i in range(n + 1):
        t = i / n
        pts.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t + math.sin(t * math.pi) * sag))
    cv.line(pts, w=2, col=(80, 60, 50))
    for i in range(n):
        (ax, ay), (bx, by) = pts[i], pts[i + 1]
        mx, my = (ax + bx) / 2, (ay + by) / 2
        cv.shape([('poly', [(ax + (bx - ax) * 0.15, ay + (by - ay) * 0.15), (ax + (bx - ax) * 0.85, ay + (by - ay) * 0.85), (mx, my + 26)])], cols[i % len(cols)], line=1.8, shadow=0.2, rim=False)

def crystal(cv, x, y, h, col, lean=0.0, glow=True):
    w = h * 0.26
    if glow: cv.glow(x + lean * 0.5, y - h * 0.5, h * 0.6, col, 90)
    pts = [(x - w, y), (x - w * 0.85 + lean, y - h * 0.72), (x + lean, y - h), (x + w * 0.85 + lean, y - h * 0.72), (x + w, y)]
    cv.shape([('poly', pts)], col, line=2.8, shadow=0.5, hi=0.25)
    cv.line([(x + lean, y - h), (x + lean * 0.25, y)], w=2, col=light(col, 0.55))

def star(cv, x, y, r, col):
    pts = []
    for i in range(8):
        rr = r if i % 2 == 0 else r * 0.38
        a = -math.pi / 2 + i * math.pi / 4
        pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr))
    cv.shape([('poly', pts)], col, line=1.8, shadow=0.15, rim=False)

def contact(cv, x, y, rx, ry=None, a=110):
    """Sombra de contato no chão (assenta o personagem/objeto)."""
    ry = ry or rx * 0.18
    lay = Image.new("RGBA", (s(rx * 2.6), s(ry * 2.6 + 4)), (0, 0, 0, 0))
    ImageDraw.Draw(lay).ellipse([s(rx * 0.3), s(ry * 0.3), s(rx * 2.3), s(ry * 2.3)], fill=(20, 10, 30, a))
    lay = lay.filter(ImageFilter.GaussianBlur(s(ry * 0.35)))
    cv.img.alpha_composite(lay, (s(x - rx * 1.3), s(y - ry * 1.3)))

def grass(cv, y0, y1, col, n=500, seed=2):
    r = random.Random(seed)
    for _ in range(n):
        x, y = r.uniform(0, W), r.uniform(y0, y1)
        h = r.uniform(6, 14) * (0.6 + 0.6 * (y - y0) / max(1, y1 - y0))
        c = shade(col, r.uniform(0.6, 0.85)) if r.random() < 0.6 else light(col, 0.25)
        cv.line([(x, y), (x + r.uniform(-4, 4), y - h)], w=1.6, col=c)

def coin(cv, x, y, r=13):
    cv.shape([('ell', x, y, r, r)], (250, 200, 60), line=2, shadow=0.35, hi=0.3)
    cv.shape([('poly', [(x, y - r * 0.55), (x + r * 0.42, y), (x, y + r * 0.55), (x - r * 0.42, y)])], (120, 230, 255), line=1.4, shadow=0.2, rim=False)

def scroll(cv, x, y, w, h, lines=5, seal=True):
    cv.shape([('rect', x, y, x + w, y + h, 4)], (246, 232, 196), line=2.6, shadow=0.18)
    for i in range(lines):
        yy = y + h * (0.18 + i * 0.64 / max(1, lines - 1))
        cv.line([(x + w * 0.1, yy), (x + w * (0.88 if i % 2 else 0.7), yy)], w=1.5, col=(140, 120, 95))
    cv.line([(x + w * 0.1, y + h * 0.92), (x + w * 0.9, y + h * 0.92)], w=0.8, col=(150, 130, 110))   # letras miúdas
    for e in (x, x + w):
        cv.shape([('ell', e, y + h / 2, w * 0.05, h * 0.55)], (226, 206, 160), line=2.2, shadow=0.3)
    if seal: cv.shape([('ell', x + w * 0.82, y + h * 0.72, 12, 12)], (190, 40, 50), line=2, shadow=0.3)

# ------------------------------------------------------------------ personagens
SKIN = (240, 196, 158)
SKIN_D = (206, 148, 110)

def eye(cv, x, y, r, iris, look=(0.0, 0.0), mood="n", side=1):
    if mood == "closed":
        cv.line([(x - r, y), (x, y + r * 0.4), (x + r, y)], w=2.6)
        return
    ry = r * (0.75 if mood == "angry" else 1.15 if mood == "scared" else 1.0)
    cv.shape([('ell', x, y, r * 0.85, ry)], (252, 252, 248), line=2.2, shadow=0.15, hi=0.0, rim=False)
    ix, iy = x + look[0] * r * 0.3, y + look[1] * r * 0.3 + (r * 0.1 if mood == "sad" else 0)
    ir = r * (0.38 if mood == "scared" else 0.55)
    cv.flat([('ell', ix, iy, ir, ir)], iris)
    cv.flat([('ell', ix, iy, ir * 0.55, ir * 0.55)], (20, 14, 20))
    cv.flat([('ell', ix - ir * 0.35, iy - ir * 0.4, ir * 0.3, ir * 0.3)], (255, 255, 255))
    cv.flat([('ell', ix + ir * 0.3, iy + ir * 0.35, ir * 0.13, ir * 0.13)], (255, 255, 255))

def brow(cv, x, y, r, mood, side, col=(80, 45, 25), thick=4.5):
    side = -side   # 'a' é a ponta de fora (longe do nariz), 'b' a de dentro
    if mood == "angry": a, b = (x - side * r * 1.1, y - r * 0.2), (x + side * r * 0.9, y + r * 0.35)
    elif mood in ("sad", "scared"): a, b = (x - side * r * 1.0, y + r * 0.2), (x + side * r * 0.9, y - r * 0.35)
    elif mood == "determined": a, b = (x - side * r * 1.0, y - r * 0.1), (x + side * r * 0.9, y + r * 0.15)
    else: a, b = (x - side * r * 1.0, y), (x + side * r * 0.9, y - r * 0.2)
    cv.shape(limb(a, b, thick, thick * 0.7), col, line=1.2, shadow=0.1, rim=False)

def mouth(cv, x, y, r, mood):
    if mood == "happy":
        cv.shape([('pie', x, y - r * 0.25, r, r * 0.8, 0, 180)], (130, 36, 46), line=2.4, shadow=0.1, rim=False)
        cv.flat([('rect', x - r * 0.7, y - r * 0.25, x + r * 0.7, y - r * 0.02, 2)], (250, 250, 245))
        cv.flat([('ell', x, y + r * 0.35, r * 0.4, r * 0.18)], (220, 90, 100))
    elif mood == "scared":
        cv.shape([('ell', x, y, r * 0.55, r * 0.7)], (110, 30, 40), line=2.4, shadow=0.1, rim=False)
    elif mood == "sad":
        cv.line([(x - r * 0.7, y + r * 0.3), (x - r * 0.2, y), (x + r * 0.3, y + 0.05 * r), (x + r * 0.7, y + r * 0.35)], w=2.8)
    elif mood == "angry":
        cv.shape([('rect', x - r, y - r * 0.3, x + r, y + r * 0.35, 4)], (250, 250, 245), line=2.6, shadow=0.1, rim=False)
        cv.line([(x - r, y + r * 0.02), (x + r, y + r * 0.02)], w=1.8)
        for k in (-0.5, 0, 0.5): cv.line([(x + r * k, y - r * 0.3), (x + r * k, y + r * 0.35)], w=1.4)
    elif mood == "determined":
        cv.line([(x - r * 0.7, y), (x + r * 0.2, y + r * 0.05), (x + r * 0.7, y - r * 0.15)], w=3)
    else:
        cv.line([(x - r * 0.6, y), (x, y + r * 0.2), (x + r * 0.6, y)], w=2.6)

def hand(cv, x, y, r, col=SKIN, ang=0.0):
    """Mão em luva de desenho: palma + polegar."""
    cv.shape([('ell', x, y, r, r * 1.05)], col, line=2.4, shadow=0.35)
    tx, ty = x + math.cos(ang) * r * 0.9, y + math.sin(ang) * r * 0.9
    cv.shape([('ell', tx, ty, r * 0.42, r * 0.42)], col, line=2.2, shadow=0.3)

def hero(cv, x, y, sc=1.0, mood="happy", facing=1, helmet=False, armL=(-20, 0.9), armR=(20, 0.9), shirt=(70, 125, 205),
         chain=False, pick=False, sit=False, look=(0.3, 0), tears=False, quill=False, lamp_on=False, dirty=False, hair=(112, 66, 34), lantern=False):
    """Escavador (pés em x,y). Braços: (ângulo em graus a partir de 'para baixo', positivo = para a frente; comprimento)."""
    u = 100 * sc
    f = facing
    contact(cv, x, y + u * 0.02, u * 0.75)
    pants, boots = (92, 74, 58), (78, 50, 32)
    hipy = y - u * 1.25 if not sit else y - u * 0.75
    shy = hipy - u * 1.05
    # pernas
    for side in (-1, 1):
        hx = x + side * u * 0.2
        if sit:
            kx, ky = hx + f * u * 0.6, hipy + u * 0.05
            fx, fy = kx + f * u * 0.08, y - u * 0.05
        else:
            kx, ky = hx + side * u * 0.03, hipy + u * 0.6
            fx, fy = hx + side * u * 0.05, y - u * 0.12
        cv.shape(limb((hx, hipy), (kx, ky), u * 0.34, u * 0.3), pants, line=3, shadow=0.45, dots=True)
        cv.shape(limb((kx, ky), (fx, fy), u * 0.3, u * 0.26), pants, line=3, shadow=0.45, dots=True)
        cv.shape([('rect', fx - u * 0.17 + f * u * 0.04, fy - u * 0.08, fx + u * 0.2 + f * u * 0.1, fy + u * 0.12, 9 * sc)], boots, line=3, shadow=0.4)
        cv.line([(kx - u * 0.1, ky + u * 0.12), (kx + u * 0.06, ky + u * 0.2)], w=1.6, col=shade(pants, 0.55))   # dobra
    if chain:
        bx = x - f * u * 1.05
        for i in range(6):
            t = (i + 0.5) / 6
            cv.shape([('ell', x - f * u * 0.2 + (bx - x + f * u * 0.2) * t, y - u * 0.1 + math.sin(t * 3.2) * u * 0.05, u * 0.08, u * 0.055)], (175, 175, 188), line=2, shadow=0.4, rim=False)
        cv.shape([('ell', bx, y - u * 0.2, u * 0.22, u * 0.22)], (95, 95, 108), line=3, shadow=0.5, hi=0.25)
        cv.shape([('rect', x - f * u * 0.32, y - u * 0.3, x - f * u * 0.08, y - u * 0.16, 3)], (140, 140, 150), line=2, shadow=0.3)
    # tronco: camisa com gola, suspensórios e cinto
    torso = [('poly', [(x - u * 0.44, shy), (x + u * 0.44, shy), (x + u * 0.4, hipy + u * 0.05), (x - u * 0.4, hipy + u * 0.05)]),
             ('ell', x - u * 0.38, shy + u * 0.1, u * 0.14, u * 0.12), ('ell', x + u * 0.38, shy + u * 0.1, u * 0.14, u * 0.12)]
    cv.shape(torso, shirt, line=3.2, shadow=0.45, dots=True)
    if dirty:
        for (dx, dy, r) in ((-0.2, 0.4, 0.07), (0.15, 0.7, 0.05), (0.25, 0.25, 0.04)):
            cv.flat([('ell', x + dx * u, shy + dy * u, r * u, r * u * 0.7)], shade(shirt, 0.6))
    cv.shape([('poly', [(x - u * 0.16, shy - u * 0.02), (x, shy + u * 0.16), (x + u * 0.16, shy - u * 0.02), (x + u * 0.04, shy - u * 0.06), (x - u * 0.04, shy - u * 0.06)])], light(shirt, 0.25), line=2.2, shadow=0.2)
    for sx in (-0.2, 0.2):
        cv.shape(limb((x + sx * u, shy + u * 0.02), (x + sx * u * 0.9, hipy), u * 0.07), (120, 70, 40), line=1.8, shadow=0.3, rim=False)
    cv.shape([('rect', x - u * 0.41, hipy - u * 0.04, x + u * 0.41, hipy + u * 0.1, 3)], (92, 56, 32), line=2.4, shadow=0.3)
    cv.shape([('rect', x - u * 0.07, hipy - u * 0.05, x + u * 0.07, hipy + u * 0.11, 2)], (220, 180, 80), line=2, shadow=0.3)
    cv.shape([('rect', x + f * u * 0.08 - u * 0.12, shy + u * 0.42, x + f * u * 0.08 + u * 0.08, shy + u * 0.64, 2)], shade(shirt, 0.82), line=1.8, shadow=0.2, rim=False)   # remendo
    cv.line([(x + f * u * 0.08 - u * 0.1, shy + u * 0.45), (x + f * u * 0.08 + u * 0.06, shy + u * 0.61)], w=1, col=(240, 230, 200))
    # braços (manga dobrada + antebraço)
    hands = []
    for side, (deg, ln) in ((-1, armL), (1, armR)):
        a = math.radians(deg)
        sx_, sy_ = x + side * u * 0.42, shy + u * 0.12
        ex, ey = sx_ + math.sin(a) * u * 0.55 * ln, sy_ + math.cos(a) * u * 0.55 * ln
        hx, hy = ex + math.sin(a * 1.1) * u * 0.5 * ln, ey + math.cos(a * 1.1) * u * 0.5 * ln
        cv.shape(limb((sx_, sy_), (ex, ey), u * 0.27, u * 0.24), shirt, line=3, shadow=0.45, dots=True)
        cv.shape([('ell', ex, ey, u * 0.15, u * 0.11)], light(shirt, 0.15), line=2.2, shadow=0.3)
        cv.shape(limb((ex, ey), (hx, hy), u * 0.17, u * 0.15), SKIN, line=2.8, shadow=0.4)
        hands.append((hx, hy, a))
    if pick:
        hx, hy, _ = hands[1]
        top = (hx + f * u * 0.3, hy - u * 1.3)
        cv.shape(limb((hx - f * u * 0.06, hy + u * 0.4), top, u * 0.1, u * 0.09), (160, 108, 60), line=2.6, shadow=0.4)
        cv.shape([('poly', [(top[0] - u * 0.66, top[1] + u * 0.32), (top[0] - u * 0.1, top[1] - u * 0.12), (top[0] + u * 0.1, top[1] - u * 0.12), (top[0] + u * 0.66, top[1] + u * 0.32), (top[0], top[1] + u * 0.06)])], (170, 172, 185), line=3, shadow=0.45, hi=0.3)
    if quill:
        hx, hy, _ = hands[1]
        cv.shape([('poly', [(hx, hy), (hx + f * u * 0.55, hy - u * 0.6), (hx + f * u * 0.62, hy - u * 0.55), (hx + f * u * 0.06, hy + u * 0.03)])], (250, 250, 244), line=2, shadow=0.25)
    if lantern:
        lx_, ly_, _ = hands[1]
        cv.line([(lx_, ly_), (lx_, ly_ + u * 0.2)], w=2, col=(60, 60, 70))
        cv.glow(lx_, ly_ + u * 0.38, u * 0.5, (255, 220, 140), 150)
        cv.shape([('rect', lx_ - u * 0.12, ly_ + u * 0.2, lx_ + u * 0.12, ly_ + u * 0.52, 4)], (255, 220, 130), line=2.6, shadow=0.2, rim=False)
    for hx, hy, a in hands:
        hand(cv, hx, hy, u * 0.12, SKIN, ang=-math.pi / 2 if f > 0 else -math.pi / 2)
    # pescoço e cabeça (cabeça grande, estilo HQ)
    cv.shape(limb((x, shy + u * 0.02), (x, shy - u * 0.18), u * 0.2), SKIN, line=2.6, shadow=0.5)
    hr = u * 0.62
    hx, hy = x + f * u * 0.02, shy - u * 0.18 - hr * 0.88
    cv.shape([('ell', hx - f * hr * 0.92, hy + hr * 0.08, hr * 0.17, hr * 0.24)], SKIN, line=2.4, shadow=0.4)
    head = [('ell', hx, hy, hr, hr * 1.02), ('poly', [(hx - hr * 0.8, hy + hr * 0.2), (hx + hr * 0.8, hy + hr * 0.2), (hx + f * hr * 0.2, hy + hr * 1.08), (hx - f * hr * 0.25 + 0, hy + hr * 1.0)])]
    cv.shape(head, SKIN, line=3.4, shadow=0.38, hi=0.1)
    # rosto
    ex_ = hx + f * hr * 0.14
    for side in (-1, 1):
        exx = ex_ + side * hr * 0.36
        eye(cv, exx, hy + hr * 0.05, hr * 0.2, (90, 140, 80), look=look, mood="sad" if mood == "sad" else "scared" if mood == "scared" else "angry" if mood == "angry" else "n", side=side)
        brow(cv, exx, hy - hr * 0.28, hr * 0.2, mood, side)
    cv.flat([('ell', ex_ - hr * 0.5, hy + hr * 0.42, hr * 0.13, hr * 0.07)], (240, 150, 140), alpha=140)
    cv.flat([('ell', ex_ + hr * 0.5, hy + hr * 0.42, hr * 0.13, hr * 0.07)], (240, 150, 140), alpha=140)
    cv.shape([('ell', ex_ + f * hr * 0.08, hy + hr * 0.32, hr * 0.11, hr * 0.09)], mix(SKIN, SKIN_D, 0.4), line=1.8, shadow=0.35, hi=0.25, rim=False)
    mouth(cv, ex_ + f * hr * 0.02, hy + hr * 0.62, hr * 0.24, mood)
    if tears:
        for side in (-1, 1):
            tx = ex_ + side * hr * 0.42
            cv.shape([('poly', [(tx - 6, hy + hr * 0.25), (tx + 6, hy + hr * 0.25), (tx + 4, hy + hr * 0.6), (tx - 4, hy + hr * 0.6)])], (140, 200, 255), line=1.6, shadow=0.2, rim=False)
    # sujeira de mina
    if dirty:
        cv.flat([('ell', hx - f * hr * 0.4, hy + hr * 0.62, hr * 0.15, hr * 0.08)], (120, 100, 90), alpha=150)
    # cabelo em mechas ou capacete
    if helmet:
        cv.shape([('pie', hx, hy - hr * 0.08, hr * 1.12, hr * 1.12, 180, 360)], (246, 196, 40), line=3.4, shadow=0.42, hi=0.25)
        cv.shape([('rect', hx - hr * 1.25, hy - hr * 0.16, hx + hr * 1.25, hy + hr * 0.02, 4)], (220, 170, 30), line=2.8, shadow=0.3)
        lx = hx + f * hr * 0.15
        cv.shape([('rect', lx - hr * 0.25, hy - hr * 1.0, lx + hr * 0.25, hy - hr * 0.55, 5)], (96, 96, 108), line=2.6, shadow=0.35)
        cv.shape([('ell', lx, hy - hr * 0.78, hr * 0.16, hr * 0.16)], (255, 250, 210), line=2.2, shadow=0.05, rim=False)
        if lamp_on: cv.glow(lx, hy - hr * 0.78, hr * 0.7, (255, 245, 190), 170)
        for side in (-1, 1):
            cv.shape([('poly', [(hx + side * hr * 0.9, hy - hr * 0.1), (hx + side * hr * 1.08, hy + hr * 0.18), (hx + side * hr * 0.75, hy + hr * 0.12)])], hair, line=2, shadow=0.3, rim=False)
    else:
        pts = []
        # topo com pontas (do lado esquerdo ao direito, passando por cima)
        n = 9
        for i in range(n + 1):
            a = math.pi * (1.0 + i / n)
            rr = hr * (1.32 if i % 2 else 1.02)
            pts.append((hx + math.cos(a) * rr + (hr * 0.1 if i % 2 else 0), hy - hr * 0.08 + math.sin(a) * rr * 0.98))
        # lateral direita descendo até a orelha, franja em zigue-zague voltando pela testa
        pts.append((hx + hr * 0.98, hy + hr * 0.05))
        fr = [(0.82, -0.18), (0.62, -0.42), (0.45, -0.22), (0.22, -0.48), (0.02, -0.26), (-0.22, -0.5), (-0.42, -0.24), (-0.62, -0.46), (-0.85, -0.15)]
        for fx_, fy_ in fr:
            pts.append((hx + fx_ * hr, hy + fy_ * hr))
        pts.append((hx - hr * 0.98, hy + hr * 0.05))
        if f < 0:
            pts = [(2 * hx - px, py) for px, py in pts]
        locks = [('poly', pts)]
        cv.shape(locks, hair, line=3.2, shadow=0.45, hi=0.18)
        for i in range(5):
            a = math.pi * (1.15 + i * 0.17)
            cv.line([(hx + math.cos(a) * hr * 0.45, hy - hr * 0.1 + math.sin(a) * hr * 0.45), (hx + math.cos(a) * hr * 0.95, hy - hr * 0.1 + math.sin(a) * hr * 0.92)], w=1.6, col=shade(hair, 0.55))
    return hands

def smith(cv, x, y, sc=1.0, mood="angry", facing=-1, hammer=True, arm_up=False, bandana=False, shirt=(160, 62, 52), beard=(70, 40, 28), point=False, look=(-0.3, 0)):
    """Ferreiro: ombros enormes, antebraços grossos, avental de couro, barba cheia."""
    u = 100 * sc
    f = facing
    contact(cv, x, y + u * 0.02, u * 1.0)
    hipy = y - u * 1.15
    shy = hipy - u * 1.15
    for side in (-1, 1):
        hx = x + side * u * 0.3
        cv.shape(limb((hx, hipy), (hx + side * u * 0.04, y - u * 0.15), u * 0.42, u * 0.36), (62, 52, 54), line=3.2, shadow=0.45, dots=True)
        cv.shape([('rect', hx - u * 0.26 + f * u * 0.04, y - u * 0.22, hx + u * 0.28 + f * u * 0.1, y + u * 0.03, 10 * sc)], (44, 32, 26), line=3, shadow=0.4)
    body = [('poly', [(x - u * 0.82, shy), (x + u * 0.82, shy), (x + u * 0.6, hipy + u * 0.1), (x - u * 0.6, hipy + u * 0.1)]),
            ('ell', x - u * 0.72, shy + u * 0.15, u * 0.3, u * 0.24), ('ell', x + u * 0.72, shy + u * 0.15, u * 0.3, u * 0.24)]
    cv.shape(body, shirt, line=3.6, shadow=0.48, dots=True)
    apron = [('poly', [(x - u * 0.48, shy + u * 0.35), (x + u * 0.48, shy + u * 0.35), (x + u * 0.56, hipy + u * 0.5), (x - u * 0.56, hipy + u * 0.5)])]
    cv.shape(apron, (124, 78, 42), line=3.2, shadow=0.45)
    cv.shape([('rect', x - u * 0.22, shy + u * 0.75, x + u * 0.22, shy + u * 1.0, 3)], (104, 64, 34), line=2, shadow=0.3, rim=False)
    for t in range(9):
        yy = shy + u * 0.4 + t * u * 0.11
        cv.line([(x - u * 0.44 - t * u * 0.008, yy), (x - u * 0.44 - t * u * 0.008, yy + u * 0.05)], w=1.2, col=(210, 180, 140))
        cv.line([(x + u * 0.44 + t * u * 0.008, yy), (x + u * 0.44 + t * u * 0.008, yy + u * 0.05)], w=1.2, col=(210, 180, 140))
    hands = []
    for side in (-1, 1):
        sx_, sy_ = x + side * u * 0.8, shy + u * 0.2
        if arm_up and side == f:
            ex, ey = sx_ + side * u * 0.35, sy_ - u * 0.55
            hx, hy = ex + side * u * 0.1, ey - u * 0.6
        elif point and side == f:
            ex, ey = sx_ + side * u * 0.5, sy_ + u * 0.2
            hx, hy = ex + side * u * 0.65, ey - u * 0.05
        else:
            ex, ey = sx_ + side * u * 0.2, sy_ + u * 0.65
            hx, hy = ex + side * u * 0.05, ey + u * 0.6
        cv.shape(limb((sx_, sy_), (ex, ey), u * 0.42, u * 0.38), shirt, line=3.4, shadow=0.48, dots=True)
        cv.shape(limb((ex, ey), (hx, hy), u * 0.38, u * 0.3), SKIN_D, line=3.2, shadow=0.45)
        # pelos do antebraço
        for k in range(3):
            t = 0.3 + k * 0.2
            px, py = ex + (hx - ex) * t, ey + (hy - ey) * t
            cv.line([(px - 4, py), (px + 3, py - 4)], w=1.2, col=shade(SKIN_D, 0.5))
        hands.append((hx, hy))
    if hammer:
        hx, hy = hands[1 if f > 0 else 0]
        top = (hx + f * u * 0.25, hy - u * 1.0) if not arm_up else (hx + f * u * 0.45, hy - u * 0.95)
        cv.shape(limb((hx - f * u * 0.02, hy + u * 0.3), top, u * 0.14, u * 0.12), (140, 92, 50), line=2.8, shadow=0.4)
        dx, dy = top[0] - hx, top[1] - hy
        L = math.hypot(dx, dy); nx, ny = -dy / L, dx / L; ux, uy = dx / L, dy / L
        hw, hl = u * 0.3, u * 0.5
        c = top
        cv.shape([('poly', [(c[0] + nx * hl - ux * hw, c[1] + ny * hl - uy * hw), (c[0] + nx * hl + ux * hw, c[1] + ny * hl + uy * hw),
                            (c[0] - nx * hl * 0.8 + ux * hw * 0.7, c[1] - ny * hl * 0.8 + uy * hw * 0.7), (c[0] - nx * hl * 0.8 - ux * hw * 0.7, c[1] - ny * hl * 0.8 - uy * hw * 0.7)])], (120, 122, 135), line=3.4, shadow=0.45, hi=0.3)
    for hx, hy in hands:
        hand(cv, hx, hy, u * 0.2, SKIN_D)
    hr = u * 0.6
    hx, hy = x + f * u * 0.03, shy - hr * 0.72
    cv.shape([('ell', hx - f * hr * 0.92, hy + hr * 0.05, hr * 0.17, hr * 0.24)], SKIN_D, line=2.4, shadow=0.4)
    cv.shape([('ell', hx, hy, hr, hr * 1.02)], SKIN_D, line=3.4, shadow=0.4, hi=0.12)
    if bandana:
        cv.shape([('pie', hx, hy - hr * 0.05, hr * 1.06, hr * 1.06, 180, 360)], (170, 40, 40), line=3, shadow=0.4)
        cv.shape([('poly', [(hx - f * hr * 0.95, hy - hr * 0.2), (hx - f * hr * 1.5, hy + hr * 0.1), (hx - f * hr * 1.35, hy - hr * 0.35)])], (170, 40, 40), line=2.6, shadow=0.35)
    else:
        cv.flat([('ell', hx - hr * 0.35, hy - hr * 0.62, hr * 0.28, hr * 0.12)], light(SKIN_D, 0.5))   # careca brilhando
    ex_ = hx + f * hr * 0.12
    for side in (-1, 1):
        exx = ex_ + side * hr * 0.34
        eye(cv, exx, hy - hr * 0.02, hr * 0.17, (80, 60, 40), look=look, mood="angry" if mood == "angry" else "n", side=side)
        brow(cv, exx, hy - hr * 0.3, hr * 0.22, mood, side, col=shade(beard, 0.8), thick=7)
    cv.shape([('ell', ex_ + f * hr * 0.1, hy + hr * 0.25, hr * 0.15, hr * 0.13)], shade(SKIN_D, 0.85), line=2, shadow=0.3, rim=False)
    # barba em mechas + bigode
    bpts = [(hx - hr * 0.98, hy + hr * 0.1), (hx - hr * 0.9, hy + hr * 0.9), (hx - hr * 0.5, hy + hr * 1.45), (hx - hr * 0.15, hy + hr * 1.25), (hx, hy + hr * 1.75),
            (hx + hr * 0.18, hy + hr * 1.3), (hx + hr * 0.5, hy + hr * 1.5), (hx + hr * 0.9, hy + hr * 0.9), (hx + hr * 0.98, hy + hr * 0.1), (hx + hr * 0.6, hy + hr * 0.55), (hx - hr * 0.6, hy + hr * 0.55)]
    cv.shape([('poly', bpts)], beard, line=3.2, shadow=0.45, hi=0.2)
    for i in range(6):
        t = -0.6 + i * 0.24
        cv.line([(hx + t * hr, hy + hr * 0.7), (hx + t * hr * 0.8, hy + hr * 1.3)], w=1.5, col=shade(beard, 0.5))
    if mood == "angry":
        mouth(cv, ex_ + f * hr * 0.05, hy + hr * 0.72, hr * 0.26, "angry")
    elif mood == "happy":
        mouth(cv, ex_ + f * hr * 0.05, hy + hr * 0.7, hr * 0.24, "happy")
    cv.shape([('poly', [(hx - hr * 0.55, hy + hr * 0.52), (hx, hy + hr * 0.38), (hx + hr * 0.55, hy + hr * 0.52), (hx + hr * 0.3, hy + hr * 0.62), (hx - hr * 0.3, hy + hr * 0.62)])], shade(beard, 0.85), line=2.6, shadow=0.3)
    return hands

def guildmaster(cv, x, y, sc=1.0, mood="happy", facing=-1):
    """Mestre da guilda: barrigudo, roupa roxa com dourado, corrente de ouro, monóculo."""
    u = 100 * sc
    f = facing
    gold = (240, 190, 60)
    robe = (110, 50, 130)
    belly = [('ell', x, y - u * 1.45, u * 0.95, u * 1.0), ('poly', [(x - u * 0.8, y - u * 2.1), (x + u * 0.8, y - u * 2.1), (x + u * 0.95, y - u * 1.2), (x - u * 0.95, y - u * 1.2)])]
    cv.shape(belly, robe, line=3.6, shadow=0.48, dots=True)
    cv.shape([('rect', x - u * 0.1, y - u * 2.15, x + u * 0.1, y - u * 0.55, 3)], gold, line=2.4, shadow=0.35)
    for k in range(4):
        cv.shape([('ell', x, y - u * 1.95 + k * u * 0.38, u * 0.06, u * 0.06)], (250, 240, 200), line=1.6, shadow=0.3, rim=False)
    cv.shape([('rect', x - u * 0.98, y - u * 1.0, x + u * 0.98, y - u * 0.82, 4)], gold, line=2.6, shadow=0.3)
    hands = []
    for side in (-1, 1):
        sx_, sy_ = x + side * u * 0.82, y - u * 2.0
        ex, ey = sx_ + side * u * 0.25, sy_ + u * 0.55
        hx, hy = ex - side * u * 0.05 + (f * u * 0.4 if side == f else 0), ey + u * 0.45
        cv.shape(limb((sx_, sy_), (ex, ey), u * 0.38, u * 0.34), robe, line=3.2, shadow=0.45)
        cv.shape(limb((ex, ey), (hx, hy), u * 0.34, u * 0.3), robe, line=3, shadow=0.45)
        cv.shape([('ell', hx, hy - u * 0.05, u * 0.2, u * 0.08)], gold, line=2, shadow=0.3, rim=False)
        hands.append((hx, hy))
    for hx, hy in hands:
        hand(cv, hx, hy + u * 0.08, u * 0.15, SKIN)
        cv.shape([('ell', hx + u * 0.05, hy + u * 0.06, u * 0.05, u * 0.05)], (90, 220, 255), line=1.4, shadow=0.2, rim=False)   # anel
    hr = u * 0.58
    hx, hy = x + f * u * 0.03, y - u * 2.1 - hr * 0.75
    for side in (-1, 1):   # tufos de cabelo branco atrás das orelhas
        cv.shape([('ell', hx + side * hr * 0.95, hy + hr * 0.05, hr * 0.2, hr * 0.32)], (232, 232, 238), line=2.4, shadow=0.3)
    cv.shape([('ell', hx, hy, hr, hr * 1.02)], SKIN, line=3.4, shadow=0.38)
    cv.shape([('rect', hx - hr * 0.7, hy - hr * 1.35, hx + hr * 0.7, hy - hr * 0.75, 5)], robe, line=3, shadow=0.4)
    cv.shape([('rect', hx - hr * 0.95, hy - hr * 0.82, hx + hr * 0.95, hy - hr * 0.68, 4)], gold, line=2.4, shadow=0.3)
    ex_ = hx + f * hr * 0.12
    for side in (-1, 1):
        exx = ex_ + side * hr * 0.34
        eye(cv, exx, hy, hr * 0.15, (70, 60, 120), look=(f * 0.3, 0), mood="closed" if mood == "happy" else "n", side=side)
    cv.shape([('ell', ex_ + f * hr * 0.34, hy, hr * 0.24, hr * 0.24)], (230, 240, 255), line=2.6, shadow=0.1, hi=0.0, rim=False)
    cv.line([(ex_ + f * hr * 0.55, hy + hr * 0.1), (ex_ + f * hr * 0.6, hy + hr * 0.9)], w=1.4, col=gold)
    cv.shape([('ell', ex_ + f * hr * 0.08, hy + hr * 0.3, hr * 0.17, hr * 0.14)], shade(SKIN, 0.88), line=2, shadow=0.3, rim=False)
    cv.shape([('poly', [(hx - hr * 0.9, hy + hr * 0.25), (hx - hr * 0.4, hy + hr * 1.3), (hx, hy + hr * 1.6), (hx + hr * 0.4, hy + hr * 1.3), (hx + hr * 0.9, hy + hr * 0.25), (hx, hy + hr * 0.62)])], (240, 240, 244), line=3, shadow=0.35)
    mouth(cv, ex_ + f * hr * 0.05, hy + hr * 0.72, hr * 0.22, "happy")
    return hands

def slave(cv, x, y, sc, pose=0, shirt=(140, 120, 100), facing=1):
    """Outro escravo trabalhando ao fundo (silhueta mais simples, sem rosto)."""
    u = 100 * sc
    col = shirt
    hipy, shy = y - u * 1.2, y - u * 2.2
    for side in (-1, 1):
        cv.shape(limb((x + side * u * 0.2, hipy), (x + side * u * 0.3, y), u * 0.3), (70, 60, 55), line=2.4, shadow=0.4)
    cv.shape([('poly', [(x - u * 0.42, shy), (x + u * 0.42, shy), (x + u * 0.36, hipy), (x - u * 0.36, hipy)])], col, line=2.6, shadow=0.45)
    a = -2.4 if pose == 0 else -0.6
    hx, hy = x + facing * u * 0.5 + math.sin(a) * u * 0.6 * facing, shy + math.cos(a) * u * 0.6
    cv.shape(limb((x + facing * u * 0.4, shy + u * 0.1), (hx, hy), u * 0.22), col, line=2.4, shadow=0.4)
    if pose == 0:
        cv.shape(limb((hx, hy + u * 0.2), (hx + facing * u * 0.6, hy - u * 0.4), u * 0.08), (150, 100, 60), line=2, shadow=0.3)
        cv.shape([('poly', [(hx + facing * u * 0.3, hy - u * 0.55), (hx + facing * u * 0.6, hy - u * 0.42), (hx + facing * u * 0.95, hy - u * 0.1), (hx + facing * u * 0.62, hy - u * 0.32)])], (160, 160, 170), line=2.2, shadow=0.4)
    else:
        cv.shape([('rect', hx - u * 0.35, hy - u * 0.1, hx + u * 0.35, hy + u * 0.35, 4)], (120, 85, 50), line=2.4, shadow=0.4)   # carrinho/caixa
    cv.shape([('ell', x, shy - u * 0.4, u * 0.36, u * 0.38)], (200, 150, 120), line=2.6, shadow=0.45)
    cv.shape([('pie', x, shy - u * 0.45, u * 0.4, u * 0.38, 180, 360)], (230, 180, 50), line=2.4, shadow=0.4)
    cv.shape([('ell', x + facing * u * 0.1, shy - u * 0.78, u * 0.08, u * 0.08)], (255, 245, 200), line=1.6, shadow=0.1, rim=False)
    cv.glow(x + facing * u * 0.1, shy - u * 0.78, u * 0.35, (255, 240, 180), 110)

def oldbram(cv, x, y, sc=1.0, mood="n", facing=1, look=(0.3, 0), sit=False):
    """Velho Bram: careca com tufos brancos, barba longa, tapa-olho, rugas, colete remendado, perna de pau e muleta."""
    u = 100 * sc
    f = facing
    contact(cv, x, y + u * 0.02, u * 0.8)
    pants, shirt, vest = (84, 72, 60), (150, 140, 120), (92, 82, 70)
    hipy = y - (u * 0.78 if sit else u * 1.2)
    shy = hipy - u * 1.0
    if sit:
        cv.shape([('rect', x - u * 0.45, hipy + u * 0.05, x + u * 0.45, hipy + u * 0.16, 4)], (110, 74, 44), line=2.6, shadow=0.35)
        for lx in (-0.35, 0.35):
            cv.shape(limb((x + lx * u, hipy + u * 0.14), (x + lx * u * 1.1, y), u * 0.07), (90, 60, 36), line=2, shadow=0.3, rim=False)
    # perna boa e perna de pau
    kx, ky = (x - u * 0.2 + f * u * 0.55, hipy + u * 0.05) if sit else (x - u * 0.2, hipy + u * 0.6)
    cv.shape(limb((x - u * 0.2, hipy), (kx, ky), u * 0.32, u * 0.28), pants, line=3, shadow=0.45, dots=True)
    cv.shape(limb((kx, ky), (kx, y - u * 0.1), u * 0.28, u * 0.24), pants, line=3, shadow=0.45, dots=True)
    cv.shape([('rect', kx - u * 0.16 + f * u * 0.03, y - u * 0.18, kx + u * 0.2 + f * u * 0.08, y + u * 0.02, 8 * sc)], (70, 46, 30), line=3, shadow=0.4)
    k2x, k2y = (x + u * 0.2 + f * u * 0.55, hipy + u * 0.05) if sit else (x + u * 0.2, hipy + u * 0.55)
    cv.shape(limb((x + u * 0.2, hipy), (k2x, k2y), u * 0.32, u * 0.3), pants, line=3, shadow=0.45, dots=True)
    cv.shape([('ell', k2x, k2y + u * 0.04, u * 0.17, u * 0.12)], shade(pants, 0.8), line=2.4, shadow=0.3)   # amarra da calça
    cv.shape(limb((k2x, k2y + u * 0.1), (k2x + f * u * 0.04, y - u * 0.02), u * 0.11, u * 0.08), (150, 105, 60), line=2.6, shadow=0.4, hi=0.25)
    # tronco com camisa e colete remendado
    cv.shape([('poly', [(x - u * 0.44, shy), (x + u * 0.44, shy), (x + u * 0.42, hipy + u * 0.05), (x - u * 0.42, hipy + u * 0.05)]),
              ('ell', x - u * 0.38, shy + u * 0.1, u * 0.14, u * 0.12), ('ell', x + u * 0.38, shy + u * 0.1, u * 0.14, u * 0.12)], shirt, line=3.2, shadow=0.45, dots=True)
    for side in (-1, 1):
        cv.shape([('poly', [(x + side * u * 0.44, shy + u * 0.02), (x + side * u * 0.08, shy + u * 0.05), (x + side * u * 0.1, hipy), (x + side * u * 0.42, hipy)])], vest, line=2.6, shadow=0.4)
    cv.shape([('rect', x - u * 0.34, shy + u * 0.5, x - u * 0.14, shy + u * 0.68, 2)], (130, 60, 50), line=1.8, shadow=0.2, rim=False)
    # braços: um apoiado na muleta
    cx_ = x + f * u * 0.85
    cv.shape(limb((cx_, shy - u * 0.05), (cx_ + f * u * 0.08, y), u * 0.07), (130, 90, 52), line=2.4, shadow=0.35)
    cv.shape([('rect', cx_ - u * 0.18, shy - u * 0.12, cx_ + u * 0.18, shy, 4)], (100, 66, 40), line=2.4, shadow=0.3)
    for side in (-1, 1):
        sx_, sy_ = x + side * u * 0.42, shy + u * 0.12
        if side == f:
            ex, ey, hx, hy = sx_ + side * u * 0.3, sy_ + u * 0.3, cx_, shy + u * 0.18
        else:
            ex, ey, hx, hy = sx_ - side * u * 0.02, sy_ + u * 0.5, sx_ + side * u * 0.05 + (f * u * 0.3 if sit else 0), sy_ + u * 0.95
        cv.shape(limb((sx_, sy_), (ex, ey), u * 0.25, u * 0.22), shirt, line=3, shadow=0.45, dots=True)
        cv.shape(limb((ex, ey), (hx, hy), u * 0.17, u * 0.15), SKIN_D, line=2.8, shadow=0.4)
        hand(cv, hx, hy, u * 0.12, SKIN_D)
    # cabeça: careca, tufos brancos, rugas, tapa-olho, barba longa
    hr = u * 0.6
    hx, hy = x + f * u * 0.03, shy - hr * 0.85
    for side in (-1, 1):
        cv.shape([('ell', hx + side * hr * 0.92, hy - hr * 0.1, hr * 0.22, hr * 0.34)], (236, 236, 240), line=2.4, shadow=0.3)
    cv.shape([('ell', hx, hy, hr, hr * 1.04)], SKIN_D, line=3.4, shadow=0.4, hi=0.1)
    cv.flat([('ell', hx - hr * 0.35, hy - hr * 0.65, hr * 0.26, hr * 0.1)], light(SKIN_D, 0.5))
    for k in range(3):
        cv.line([(hx - hr * 0.4, hy - hr * (0.42 - k * 0.1)), (hx + hr * 0.4, hy - hr * (0.45 - k * 0.1))], w=1.4, col=shade(SKIN_D, 0.6))
    ex_ = hx + f * hr * 0.12
    good, bad = ex_ + f * hr * 0.34, ex_ - f * hr * 0.34
    eye(cv, good, hy + hr * 0.02, hr * 0.17, (110, 100, 80), look=look, mood="angry" if mood == "angry" else "n", side=f)
    brow(cv, good, hy - hr * 0.28, hr * 0.24, "angry" if mood in ("angry", "n") else mood, f, col=(220, 220, 225), thick=7)
    cv.shape([('ell', bad, hy + hr * 0.03, hr * 0.22, hr * 0.2)], (30, 24, 26), line=2.4, shadow=0.2, hi=0.15, rim=False)
    cv.line([(hx - hr * 0.98, hy - hr * 0.35), (bad, hy + hr * 0.0), (hx + hr * 0.98, hy - hr * 0.42)], w=3, col=(30, 24, 26))
    cv.line([(bad - hr * 0.05, hy - hr * 0.35), (bad + hr * 0.05, hy + hr * 0.3)], w=1.6, col=(170, 100, 90))   # cicatriz
    cv.shape([('ell', ex_ + f * hr * 0.08, hy + hr * 0.3, hr * 0.15, hr * 0.13)], shade(SKIN_D, 0.85), line=2, shadow=0.3, rim=False)
    bpts = [(hx - hr * 0.85, hy + hr * 0.3), (hx - hr * 0.7, hy + hr * 1.2), (hx - hr * 0.3, hy + hr * 1.9), (hx, hy + hr * 2.2), (hx + hr * 0.3, hy + hr * 1.9), (hx + hr * 0.7, hy + hr * 1.2), (hx + hr * 0.85, hy + hr * 0.3), (hx, hy + hr * 0.62)]
    cv.shape([('poly', bpts)], (236, 236, 240), line=3, shadow=0.4, hi=0.15)
    for i in range(5):
        t = -0.5 + i * 0.25
        cv.line([(hx + t * hr, hy + hr * 0.8), (hx + t * hr * 0.6, hy + hr * 1.7)], w=1.3, col=(170, 170, 180))
    cv.shape([('poly', [(hx - hr * 0.55, hy + hr * 0.55), (hx, hy + hr * 0.42), (hx + hr * 0.55, hy + hr * 0.55), (hx + hr * 0.3, hy + hr * 0.65), (hx - hr * 0.3, hy + hr * 0.65)])], (210, 210, 218), line=2.4, shadow=0.3)
    if mood == "talk":
        cv.shape([('ell', ex_ + f * hr * 0.05, hy + hr * 0.75, hr * 0.15, hr * 0.1)], (90, 30, 40), line=2, shadow=0.1, rim=False)

def beast(cv, x, y, sc=1.0):
    """Criatura de Lúmen bruto: corpo curvado, cabeça com mandíbula, espinhos de cristal, olhos vermelhos e garras."""
    u = 100 * sc
    dark = (46, 30, 66)
    cv.glow(x - u * 0.5, y - u * 1.6, u * 2.4, (120, 40, 160), 110)
    for lx in (-0.9, 0.2, 0.9):   # patas
        cv.shape(limb((x + lx * u, y - u * 1.0), (x + lx * u - u * 0.15, y - u * 0.05), u * 0.45, u * 0.3), shade(dark, 0.85), line=3.2, shadow=0.4)
        for c in range(3):
            cv.shape([('poly', [(x + lx * u - u * 0.35 + c * u * 0.18, y - u * 0.1), (x + lx * u - u * 0.42 + c * u * 0.18, y + u * 0.05), (x + lx * u - u * 0.25 + c * u * 0.18, y - u * 0.02)])], (200, 240, 255), line=1.8, shadow=0.2, rim=False)
    cv.shape([('poly', blob(x, y - u * 1.45, u * 1.5, u * 0.9, 16, 0.08, 7))], dark, line=4, shadow=0.4, hi=0.12, dots=True)
    for i in range(6):   # espinhos de cristal nas costas
        t = i / 5
        bx, by = x - u * 0.9 + t * u * 2.0, y - u * 2.15 + math.sin(t * math.pi) * -u * 0.25
        crystal(cv, bx, by + u * 0.25, u * (0.6 + (i % 3) * 0.3), (120, 230, 255) if i % 2 else (190, 120, 255), lean=(t - 0.5) * u * 0.4)
    hx, hy = x - u * 1.55, y - u * 1.75   # cabeça
    cv.shape([('poly', blob(hx, hy, u * 0.75, u * 0.6, 12, 0.06, 3))], dark, line=4, shadow=0.4, hi=0.12)
    cv.shape([('poly', [(hx - u * 0.75, hy + u * 0.1), (hx + u * 0.2, hy + u * 0.25), (hx - u * 0.1, hy + u * 0.7), (hx - u * 0.85, hy + u * 0.45)])], shade(dark, 0.8), line=3.4, shadow=0.35)
    for k in range(5):   # dentes
        tx = hx - u * 0.7 + k * u * 0.18
        cv.shape([('poly', [(tx, hy + u * 0.18), (tx + u * 0.12, hy + u * 0.2), (tx + u * 0.06, hy + u * 0.4)])], (240, 240, 250), line=1.6, shadow=0.15, rim=False)
    for k in (-1, 1):
        ex, ey = hx - u * 0.25 + k * u * 0.25, hy - u * 0.2
        cv.glow(ex, ey, u * 0.35, (255, 40, 60), 220)
        cv.shape([('poly', [(ex - u * 0.13, ey - u * 0.04), (ex + u * 0.13, ey - u * 0.08), (ex, ey + u * 0.08)])], (255, 80, 90), line=2, shadow=0.1, rim=False)

def cathedral_bg(cv, warm=False):
    for i, (x, h, col) in enumerate(((120, 700, (90, 70, 140)), (380, 760, (70, 60, 120)), (1220, 740, (80, 66, 130)), (1480, 700, (90, 70, 140)))):
        cv.shape([('rect', x - 70, 900 - h, x + 70, 900, 8)], col, line=3, shadow=0.3, rim=False, dots=True)
        crystal(cv, x, 900 - h + 40, 120, (150, 230, 255), 0)
    cv.shape([('rect', -20, 780, W + 20, H + 20, 0)], (44, 36, 70), line=3, shadow=0.0, rim=False)

def ancient_gate(cv, x, y, w, h, open_=0.0, sealed=False):
    stone = (120, 112, 140)
    cv.shape([('rect', x - w / 2 - 70, y - h, x - w / 2, y, 6), ('rect', x + w / 2, y - h, x + w / 2 + 70, y, 6), ('rect', x - w / 2 - 90, y - h - 80, x + w / 2 + 90, y - h + 10, 6)], stone, line=3.4, shadow=0.35, dots=True)
    for k in range(4):
        for sx in (-1, 1):
            cv.shape([('poly', [(x + sx * (w / 2 + 35), y - h + 60 + k * 110), (x + sx * (w / 2 + 50), y - h + 75 + k * 110), (x + sx * (w / 2 + 35), y - h + 90 + k * 110), (x + sx * (w / 2 + 20), y - h + 75 + k * 110)])], (130, 230, 255), line=1.6, shadow=0.1, rim=False)
    cv.shape([('rect', x - w / 2, y - h, x + w / 2, y, 2)], (12, 6, 18), line=2, shadow=0.0, hi=0.0, rim=False)
    cv.glow(x, y - h * 0.4, w * 0.5, (200, 40, 70), 120)
    dw = w / 2 * (1 - open_)
    for sx in (-1, 1):
        if dw > 4:
            x0 = x - w / 2 if sx < 0 else x + w / 2 - dw
            cv.shape([('rect', x0, y - h, x0 + dw, y, 2)], (80, 78, 86), line=3, shadow=0.35, hi=0.2)
            for r_ in range(4):
                cv.shape([('rect', x0, y - h + 40 + r_ * (h - 60) / 3, x0 + dw, y - h + 56 + r_ * (h - 60) / 3, 1)], (110, 80, 60), line=1.6, shadow=0.3, rim=False)
    if sealed:
        for sx in (-1, 1):
            for i in range(14):
                t = i / 13
                cv.shape([('ell', x + sx * (-w / 2 + w * t), y - h + 40 + (h - 80) * t, 13, 8)], (170, 170, 180), line=1.8, shadow=0.3, rim=False)
        cv.glow(x, y - h * 0.5, 90, (255, 170, 70), 200)
        cv.shape([('ell', x, y - h * 0.5, 55, 55)], (255, 190, 80), line=3, shadow=0.3, hi=0.3)
        cv.shape(limb((x - 20, y - h * 0.5 + 25), (x + 18, y - h * 0.5 - 20), 9), (120, 80, 50), line=1.8, shadow=0.2, rim=False)
        cv.shape([('poly', [(x + 5, y - h * 0.5 - 32), (x + 38, y - h * 0.5 - 12), (x + 30, y - h * 0.5), (x - 3, y - h * 0.5 - 20)])], (120, 120, 135), line=1.8, shadow=0.3, rim=False)

def bram1():
    cv = Canvas(gradient((26, 20, 50), (60, 44, 90)))
    cv.light = (-0.6, -0.8); cv.rim = (150, 220, 255)
    cathedral_bg(cv)
    ancient_gate(cv, 860, 800, 420, 560, open_=0.55)
    cv.layer("fg")
    smith(cv, 1270, 900, 2.0, "angry", facing=-1, point=True, hammer=False, bandana=True, shirt=(50, 50, 58), beard=(25, 22, 26), look=(-0.8, 0.1))
    hero(cv, 640, 900, 1.45, "scared", facing=1, armL=(-25, 0.9), armR=(40, 0.95), shirt=(120, 110, 95), hair=(40, 30, 24), lantern=True, look=(0.6, -0.2))
    cv.text("ENTRA, BRAM!", 1250, 140, 60, (255, 225, 90), 6)
    finish(cv, OUT + "/bram_1.png")

def bram2():
    cv = Canvas(gradient((10, 6, 18), (34, 14, 40)))
    cv.light = (0.7, -0.7); cv.rim = (255, 80, 110)
    for i in range(60):
        r_ = random.Random(i)
        a = r_.uniform(0, math.tau)
        cv.line([(520 + math.cos(a) * 180, 430 + math.sin(a) * 180), (520 + math.cos(a) * 1300, 430 + math.sin(a) * 1300)], w=r_.uniform(1, 3), col=(90, 40, 110))
    cv.layer("fg")
    beast(cv, 1300, 900, 2.0)
    for i in range(3):
        cv.shape([('poly', [(520 + i * 70, 180 + i * 40), (900 + i * 60, 560 + i * 40), (880 + i * 60, 575 + i * 40), (500 + i * 70, 195 + i * 40)])], (255, 255, 255), line=1.4, shadow=0.0, rim=False)
    hero(cv, 470, 900, 1.5, "scared", facing=1, armL=(-160, 0.9), armR=(150, 0.95), shirt=(120, 110, 95), hair=(40, 30, 24), look=(0.8, -0.3))
    cv.shape([('rect', 250, 860, 300, 890, 4)], (255, 220, 130), line=2.4, shadow=0.2, rim=False)   # lanterna caída
    cv.glow(275, 870, 60, (255, 220, 140), 110)
    cv.text("AAAAH!", 330, 130, 84, (255, 110, 110), -10)
    finish(cv, OUT + "/bram_2.png")

def bram3():
    cv = Canvas(gradient((18, 22, 50), (60, 48, 70)))
    cv.light = (0.8, -0.5); cv.rim = (255, 170, 90)
    mountains(cv, 520, [((50, 46, 80), 160, 300), ((38, 34, 62), 100, 220)])
    timber_house(cv, 40, 640, 220, 150, (200, 190, 170), (120, 50, 40), seed=4)
    timber_house(cv, 1320, 630, 230, 150, (210, 196, 176), (70, 72, 90), seed=6)
    cv.shape([('rect', -20, 600, W + 20, H + 20, 0)], (60, 70, 52), line=2, shadow=0.05, rim=False)
    for tx in (300, 1240):
        cv.shape(limb((tx, 600), (tx, 420), 14), (90, 60, 36), line=2.4, shadow=0.3)
        cv.glow(tx, 400, 90, (255, 170, 80), 170)
        cv.shape([('poly', [(tx - 18, 420), (tx, 360), (tx + 18, 420)])], (255, 180, 70), line=2, shadow=0.1, rim=False)
    for (vx, col) in ((420, (180, 70, 70)), (500, (70, 120, 170)), (1090, (110, 150, 90)), (1170, (200, 160, 80)), (1000, (150, 90, 150))):
        cv.shape([('rect', vx - 22, 520, vx + 22, 620, 10), ('ell', vx, 500, 20, 20)], col, line=2.4, shadow=0.45, rim=True)
    cv.layer("fg")
    guildmaster(cv, 820, 860, 1.5, "n", facing=1)
    cv.shape(limb((910, 560), (1040, 500), 30), (110, 50, 130), line=3, shadow=0.4)
    hand(cv, 1050, 495, 18, SKIN)
    smith(cv, 1330, 920, 1.7, "angry", facing=-1, hammer=True, bandana=True, shirt=(50, 50, 58), beard=(25, 22, 26), look=(-0.9, 0.1))
    # Bram ferido na maca, carregado por dois moradores
    cv.shape([('rect', 120, 760, 520, 800, 6)], (150, 120, 80), line=3, shadow=0.35)
    cv.shape([('rect', 170, 720, 470, 770, 14)], (200, 195, 185), line=3, shadow=0.35, dots=True)
    cv.shape([('ell', 160, 720, 48, 44)], SKIN, line=3, shadow=0.4)
    cv.shape([('rect', 120, 700, 200, 722, 6)], (240, 240, 235), line=2.4, shadow=0.2, rim=False)   # atadura no olho
    cv.shape([('ell', 470, 742, 30, 22)], (240, 240, 235), line=2.4, shadow=0.2, rim=False)        # perna enfaixada
    for px in (110, 530):
        cv.shape([('rect', px - 22, 660, px + 22, 900, 10), ('ell', px, 640, 22, 22)], (70, 60, 70), line=2.6, shadow=0.4)
    cv.text("FORA DA VILA, CORVO!", 820, 150, 56, (255, 225, 90), -4)
    finish(cv, OUT + "/bram_3.png")

def bram4():
    cv = Canvas(gradient((22, 16, 40), (50, 36, 76)))
    cv.light = (-0.5, -0.85); cv.rim = (255, 80, 90)
    cathedral_bg(cv)
    ancient_gate(cv, 640, 800, 440, 580, open_=0.12, sealed=True)
    # trabalhadores da Guilda pregando as correntes
    for (wx, flip) in ((330, 1), (960, -1)):
        cv.shape([('rect', wx - 30, 640, wx + 30, 790, 12), ('ell', wx, 618, 26, 26)], (90, 80, 70), line=2.6, shadow=0.45)
        cv.shape(limb((wx + flip * 20, 670), (wx + flip * 80, 600), 16), (90, 80, 70), line=2.4, shadow=0.4)
        cv.shape([('rect', wx + flip * 80 - 20, 575, wx + flip * 80 + 20, 600, 3)], (120, 120, 135), line=2, shadow=0.3, rim=False)
    cv.text("CLANG!", 470, 520, 36, (255, 220, 120), -8)
    cv.layer("fg")
    # a silhueta do Corvo descendo pela fresta, olhos vermelhos
    cv.flat([('poly', [(1180, 900), (1600, 900), (1600, 300), (1360, 260)])], (8, 4, 10), alpha=235)
    cv.shape([('ell', 1400, 520, 140, 170), ('rect', 1260, 560, 1540, 900, 30)], (14, 10, 16), line=3, shadow=0.0, hi=0.0, rim=True)
    for k in (-1, 1):
        cv.glow(1395 + k * 42, 500, 40, (255, 40, 60), 230)
        cv.shape([('poly', [(1375 + k * 42, 495), (1415 + k * 42, 492), (1395 + k * 42, 508)])], (255, 90, 90), line=1.6, shadow=0.0, rim=False)
    cv.text("...", 1400, 200, 70, (255, 120, 120), 0)
    finish(cv, OUT + "/bram_4.png")

def portraits():
    """Retratos para as conversas (cabeça e ombros, fundo de cor lisa)."""
    for name, bg, draw in (("corvo", ((60, 10, 14), (140, 30, 30)), lambda cv: corvo(cv, 800, 1120, 2.0, "angry", facing=1, look=(0.6, 0), hammer=False)),("bram", ((120, 80, 50), (200, 140, 90)), lambda cv: oldbram(cv, 800, 1170, 2.2, "talk", facing=1, look=(0.6, 0))),
                           ("hero", ((50, 80, 140), (110, 160, 220)), lambda cv: hero(cv, 800, 1230, 2.2, "happy", facing=-1, look=(-0.6, 0)))):
        cv = Canvas(gradient(bg[0], bg[1]))
        cv.light = (-0.6, -0.8); cv.rim = (255, 230, 190)
        draw(cv)
        paper(cv.img, 8)
        img = cv.img.crop((s(800 - 300), s(300), s(800 + 300), s(900))).convert("RGB").resize((512, 512), Image.LANCZOS)
        img.save(OUT + f"/portrait_{name}.png", optimize=True)

def corvo(cv, x, y, sc=1.0, mood="angry", facing=-1, look=(-0.4, 0), hammer=True, arm_up=False):
    """Mestre Corvo corrompido: o ferreiro de barba preta com rachaduras de Lúmen rubro e cristais nos ombros."""
    hands = smith(cv, x, y, sc, mood, facing=facing, hammer=hammer, arm_up=arm_up, bandana=True, shirt=(40, 34, 40), beard=(18, 14, 18), look=look)
    u = 100 * sc
    hr = u * 0.6
    shy = y - u * 1.15 - u * 1.15
    hx, hy = x + facing * u * 0.03, shy - hr * 0.72
    r = random.Random(7)
    # rachaduras de brasa no rosto, no peito e nos braços
    for (cx, cy, ln) in ((hx - hr * 0.5, hy - hr * 0.1, hr * 0.5), (hx + hr * 0.45, hy + hr * 0.05, hr * 0.4), (x - u * 0.3, shy + u * 0.3, u * 0.5), (x + u * 0.25, shy + u * 0.6, u * 0.45), (x - u * 0.85, shy + u * 0.5, u * 0.4)):
        pts = [(cx, cy)]
        for k in range(4):
            pts.append((pts[-1][0] + r.uniform(-0.3, 0.3) * ln, pts[-1][1] + ln * 0.3))
        cv.line(pts, w=3.2, col=(255, 90, 60))
        cv.glow(cx, cy + ln * 0.5, ln * 0.6, (255, 60, 40), 70)
    for side in (-1, 1):
        for k in range(3):
            crystal(cv, x + side * (u * 0.6 + k * u * 0.13), shy + u * 0.05, u * (0.35 + k * 0.1), (255, 70, 80), lean=side * u * 0.12)
    return hands

def seed(cv, x, y, r):
    """A Semente do Povo Lúmen: esfera dourada com folhas de cristal verde e luz."""
    cv.glow(x, y, r * 3.5, (255, 230, 150), 200)
    for k in range(5):
        a = -math.pi / 2 + (k - 2) * 0.5
        cv.shape([('poly', [(x, y - r * 0.2), (x + math.cos(a) * r * 1.6 - r * 0.25, y + math.sin(a) * r * 1.6), (x + math.cos(a) * r * 2.0, y + math.sin(a) * r * 2.0), (x + math.cos(a) * r * 1.6 + r * 0.25, y + math.sin(a) * r * 1.6)])], (150, 255, 150), line=2.2, shadow=0.3)
    cv.shape([('ell', x, y, r, r * 1.1)], (255, 215, 110), line=3, shadow=0.35, hi=0.35)
    cv.flat([('ell', x - r * 0.3, y - r * 0.35, r * 0.3, r * 0.22)], (255, 250, 220))

def end1():
    cv = Canvas(gradient((30, 6, 10), (70, 14, 18)))
    cv.light = (-0.4, -0.9); cv.rim = (255, 220, 150)
    for i in range(14):
        x = 30 + i * 118
        hh = 90 + (i * 53) % 150
        cv.shape([('poly', [(x - 44, -10), (x + 44, -10), (x + 6, hh)])], (50, 22, 28), line=2.6, shadow=0.3, rim=False)
    cv.shape([('rect', -20, 780, W + 20, H + 20, 0)], (40, 18, 22), line=3, shadow=0.0, rim=False)
    # bigorna partida e o martelo largado
    cv.shape([('rect', 1180, 700, 1420, 790, 6)], (60, 58, 66), line=3.4, shadow=0.4)
    cv.shape([('poly', [(1300, 700), (1320, 760), (1290, 790)])], (255, 120, 80), line=1.6, shadow=0.1, rim=False)
    cv.layer("fg")
    corvo(cv, 1150, 940, 1.7, "sad", facing=-1, look=(-0.6, 0.6), hammer=False)
    hs = hero(cv, 470, 900, 1.6, "determined", facing=1, armR=(160, 1.0), armL=(-20, 0.9), helmet=True, dirty=True, look=(0.2, -0.8))
    hx_, hy_, _ = hs[1]
    seed(cv, hx_ + 10, hy_ - 70, 38)
    cv.text("A SEMENTE...", 800, 130, 54, (255, 225, 140), -4)
    finish(cv, OUT + "/end_1.png")

def end2():
    cv = Canvas(gradient((12, 14, 38), (40, 34, 64)))
    cv.light = (0.0, -1.0); cv.rim = (255, 225, 150)
    mountains(cv, 520, [((40, 38, 70), 160, 300), ((30, 28, 56), 100, 220)])
    timber_house(cv, 40, 640, 220, 150, (200, 190, 170), (120, 50, 40), seed=4)
    timber_house(cv, 1330, 630, 230, 150, (210, 196, 176), (70, 72, 90), seed=6)
    cv.shape([('rect', -20, 600, W + 20, H + 20, 0)], (60, 66, 60), line=2, shadow=0.05, rim=False)
    cv.shape([('ell', 800, 760, 420, 90)], (110, 106, 102), line=3, shadow=0.1, rim=False)
    # feixe de luz subindo do monumento
    lay = Image.new("RGBA", cv.img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).polygon([(s(740), s(680)), (s(860), s(680)), (s(960), 0), (s(640), 0)], fill=(255, 235, 160, 110))
    cv.img.alpha_composite(lay.filter(ImageFilter.GaussianBlur(s(18))))
    cv.shape([('rect', 730, 640, 870, 700, 8)], (150, 145, 140), line=3, shadow=0.3)
    crystal(cv, 800, 640, 170, (150, 235, 255))
    seed(cv, 800, 600, 30)
    cv.layer("fg")
    # moradores olhando e o Velho Bram
    for (vx, col) in ((300, (180, 70, 70)), (380, (70, 120, 170)), (1150, (110, 150, 90)), (1240, (200, 160, 80)), (1090, (150, 90, 150))):
        cv.shape([('rect', vx - 22, 600, vx + 22, 700, 10), ('ell', vx, 580, 20, 20)], col, line=2.4, shadow=0.45, rim=True)
    oldbram(cv, 1310, 900, 1.25, "talk", facing=-1, look=(-0.6, -0.3))
    hero(cv, 560, 910, 1.4, "happy", facing=1, armR=(150, 1.0), armL=(120, 1.0), look=(0.6, -0.6))
    finish(cv, OUT + "/end_2.png")

def end3():
    cv = Canvas(gradient((60, 40, 20), (255, 210, 120)))
    cv.light = (0.0, 1.0); cv.rim = (255, 240, 190)
    for i in range(70):
        r_ = random.Random(i)
        a = r_.uniform(0, math.tau)
        cv.line([(800 + math.cos(a) * 160, 460 + math.sin(a) * 160), (800 + math.cos(a) * 1300, 460 + math.sin(a) * 1300)], w=r_.uniform(1, 4), col=(255, 240, 190))
    cv.glow(800, 460, 300, (255, 245, 200), 230)
    cv.layer("fg")
    # escravos com correntes se partindo
    for (sx, sc_, f) in ((260, 0.95, 1), (520, 0.8, 1), (1080, 0.8, -1), (1340, 0.95, -1)):
        slave(cv, sx, 880, sc_, 1, (130, 115, 100), f)
        for k in range(5):
            r_ = random.Random(int(sx) + k)
            cx, cy = sx + r_.uniform(-80, 80), 760 + r_.uniform(-60, 60)
            cv.shape([('ell', cx, cy, 14, 9)], (190, 190, 200), line=2, shadow=0.3, rim=False)
        cv.text("CRACK!", sx, 520, 30, (255, 255, 255), r_.uniform(-12, 12), 3)
    # o livro de dívidas virando pó nas mãos do mestre da guilda
    guildmaster(cv, 800, 1060, 1.5, "n", facing=1)
    scroll(cv, 900, 790, 200, 70, 4, seal=False)
    for k in range(40):
        r_ = random.Random(100 + k)
        cv.flat([('ell', 1100 + r_.uniform(0, 220), 760 + r_.uniform(-140, 40), r_.uniform(2, 6), r_.uniform(2, 6))], (120, 100, 80))
    cv.text("AS DÍVIDAS...", 800, 120, 56, (90, 40, 20), 0, 4)
    finish(cv, OUT + "/end_3.png")

def end4():
    cv = Canvas(gradient((255, 170, 120), (255, 235, 190), (255, 205, 150), 0.5))
    cv.light = (0.7, -0.7); cv.rim = (255, 230, 170)
    cv.glow(1250, 300, 260, (255, 250, 210), 220)
    mountains(cv, 520, [((210, 140, 150), 180, 320), ((170, 110, 130), 120, 250)])
    cv.shape([('rect', -30, 520, W + 30, H + 30, 0)], (120, 175, 95), line=2, shadow=0.05, hi=0.0, rim=False)
    grass(cv, 560, 890, (120, 175, 95), 500)
    timber_house(cv, 40, 640, 220, 150, (240, 226, 196), (170, 62, 48), seed=1)
    timber_house(cv, 1320, 620, 220, 150, (240, 220, 200), (150, 110, 60), seed=3)
    bunting(cv, 260, 420, 790, 380, 40)
    bunting(cv, 810, 380, 1330, 430, 40)
    # monumento com a árvore de cristal dourada
    cv.shape([('rect', 740, 560, 860, 600, 6)], (150, 145, 140), line=2.6)
    cv.shape(limb((800, 560), (800, 360), 26, 14), (200, 170, 90), line=2.6, shadow=0.35)
    for (bx, by, r_) in ((800, 330, 70), (740, 370, 50), (860, 370, 50), (800, 280, 45)):
        cv.shape([('ell', bx, by, r_, r_ * 0.8)], (170, 255, 170), line=2.6, shadow=0.35, hi=0.3)
    cv.glow(800, 330, 160, (255, 240, 170), 120)
    cv.shape([('rect', 1000, 470, 1230, 540, 6)], (200, 170, 120), line=3, shadow=0.25)
    cv.text("MINA LIVRE", 1115, 505, 32, INK, 0, 0, scene=True)
    cv.layer("fg")
    # comemoração: moradores, Bram e o herói
    for (vx, col) in ((330, (200, 80, 80)), (430, (80, 140, 200)), (1060, (120, 170, 90)), (1160, (220, 170, 80))):
        cv.shape([('rect', vx - 22, 600, vx + 22, 700, 10), ('ell', vx, 580, 20, 20)], col, line=2.4, shadow=0.4, rim=False)
        cv.shape(limb((vx - 18, 615), (vx - 40, 560), 10), col, line=2, shadow=0.3, rim=False)
        cv.shape(limb((vx + 18, 615), (vx + 40, 560), 10), col, line=2, shadow=0.3, rim=False)
    oldbram(cv, 1260, 900, 1.3, "talk", facing=-1, look=(-0.6, -0.3))
    hero(cv, 560, 900, 1.6, "happy", facing=1, armL=(-160, 0.95), armR=(160, 0.95), look=(0.4, -0.6))
    cv.text("FIM", 800, 130, 96, (255, 245, 210), 0, 8)
    finish(cv, OUT + "/end_4.png")

# ------------------------------------------------------------------ quadros
def panel1():
    cv = Canvas(gradient((255, 128, 80), (255, 222, 160), (255, 170, 110), 0.55))
    cv.light = (0.75, -0.65)          # sol à direita
    cv.rim = (255, 210, 140)
    cv.glow(1180, 330, 260, (255, 240, 190), 210)
    cv.shape([('ell', 1180, 330, 95, 95)], (255, 236, 170), line=0.1, shadow=0.0, hi=0.0, rim=False)
    for i, (cx, cy) in enumerate([(300, 160), (520, 120), (860, 190)]):
        cv.shape([('ell', cx, cy, 90, 28), ('ell', cx + 60, cy - 18, 60, 30), ('ell', cx - 55, cy - 10, 50, 24)], (255, 200, 170), line=1.8, shadow=0.35, rim=False)
    mountains(cv, 520, [((200, 120, 140), 180, 320), ((160, 95, 120), 120, 250), ((120, 75, 100), 60, 160)])
    cv.shape([('rect', -30, 520, W + 30, H + 30, 0)], (118, 168, 92), line=2, shadow=0.05, hi=0.0, rim=False)
    grass(cv, 560, 690, (118, 168, 92), 350)
    grass(cv, 770, 890, (118, 168, 92), 420, 5)
    # vila com casas enxaimel, bandeirolas e monumento de cristal
    timber_house(cv, 60, 640, 230, 160, (240, 226, 196), (170, 62, 48), seed=1)
    timber_house(cv, 330, 600, 190, 135, (232, 214, 186), (90, 92, 110), seed=2)
    timber_house(cv, 1260, 620, 220, 150, (240, 220, 200), (150, 110, 60), seed=3)
    cv.shape([('rect', 740, 560, 860, 600, 6)], (150, 145, 140), line=2.6)
    crystal(cv, 800, 560, 150, (120, 225, 255))
    crystal(cv, 770, 562, 80, (150, 235, 255), -12)
    crystal(cv, 830, 562, 90, (150, 235, 255), 14)
    bunting(cv, 300, 470, 790, 420, 46)
    bunting(cv, 800, 420, 1300, 480, 46)
    # moradores ao fundo
    for (vx, col) in ((560, (200, 80, 80)), (650, (80, 140, 200)), (1050, (120, 170, 90)), (1140, (220, 170, 80))):
        cv.shape([('rect', vx - 14, 545, vx + 14, 600, 8), ('ell', vx, 532, 13, 13)], col, line=2.2, shadow=0.4, rim=False)
    cv.shape([('rect', -30, 690, W + 30, 760, 0)], (196, 160, 110), line=2.4, shadow=0.1, rim=False)
    for i in range(30):
        cv.flat([('ell', 30 + i * 55, 725 + (i % 3) * 8, 18, 7)], (170, 135, 92))
    # herói com caixa de ferramentas e balão de pensamento (a oficina dos sonhos)
    cv.layer("fg")
    contact(cv, 640, 880, 90)
    hs = hero(cv, 470, 890, 1.75, "happy", facing=1, armL=(-25, 0.9), armR=(10, 0.95), look=(0.6, -0.4))
    hx_, hy_, _ = hs[1]
    cv.shape([('rect', hx_ - 15, hy_ - 10, hx_ + 15, hy_ + 30, 5)], (120, 120, 130), line=2.4, shadow=0.3, rim=False)
    cv.shape([('rect', hx_ - 75, hy_ + 25, hx_ + 75, hy_ + 105, 6)], (190, 60, 50), line=3, shadow=0.4)
    cv.shape([('rect', hx_ - 75, hy_ + 50, hx_ + 75, hy_ + 58, 1)], (150, 40, 35), line=1.4, shadow=0.1, rim=False)
    hand(cv, hx_, hy_, 21, SKIN)
    for (bx, by, r) in ((598, 258, 11), (645, 222, 17)):  # bolinhas saindo da cabeça
        cv.shape([('ell', bx, by, r, r)], (255, 255, 250), line=2.4, shadow=0.15, rim=False)
    cv.shape([('ell', 900, 330, 240, 150)], (255, 255, 250), line=3.4, shadow=0.12, hi=0.0, rim=False)
    timber_house(cv, 780, 420, 230, 140, (240, 228, 200), (60, 120, 170), chimney=False, seed=9)
    cv.shape([('rect', 830, 230, 970, 262, 4)], (120, 80, 45), line=2.4, shadow=0.3)
    cv.text("OFICINA", 900, 247, 24, (255, 230, 150), 0, 2, scene=True)
    for i in range(5):
        star(cv, 700 + i * 90, 215 + (i % 2) * 50, 14, (255, 225, 120))
    finish(cv, OUT + "/panel_1.png")

def panel2():
    cv = Canvas(gradient((70, 42, 34), (130, 82, 54), (100, 62, 44), 0.5))
    cv.light = (-0.5, -0.85)
    cv.rim = (255, 200, 120)
    # salão da guilda: parede de pedra, estandartes e brasão
    r = random.Random(4)
    for row in range(7):
        for col in range(10):
            off = 85 if row % 2 else 0
            c = mix((112, 82, 64), (132, 98, 76), r.random())
            cv.shape([('rect', col * 170 - off + 4, row * 86 + 4, col * 170 - off + 166, row * 86 + 82, 6)], c, line=2, shadow=0.2, hi=0.05, rim=False)
    for bx in (220, 1380):
        cv.shape([('poly', [(bx - 70, 40), (bx + 70, 40), (bx + 70, 330), (bx, 290), (bx - 70, 330)])], (120, 30, 40), line=3, shadow=0.4)
        cv.shape([('ell', bx, 160, 40, 40)], (240, 190, 60), line=2.4, shadow=0.3)
        cv.shape(limb((bx - 22, 182), (bx + 20, 140), 10), (90, 90, 100), line=2, shadow=0.3, rim=False)
    cv.shape([('ell', 800, 150, 120, 120)], (200, 150, 60), line=3.4, shadow=0.35, hi=0.2)
    cv.shape(limb((745, 210), (860, 95), 22), (130, 130, 145), line=2.6, shadow=0.3)
    cv.shape([('poly', [(815, 70), (905, 115), (885, 140), (795, 95)])], (110, 110, 125), line=2.6, shadow=0.35)
    cv.text("GUILDA DOS FERREIROS", 800, 300, 34, (255, 220, 140), 0, 4, scene=True)
    cv.glow(800, 560, 420, (255, 190, 110), 70)
    cv.layer("fg")
    guildmaster(cv, 1100, 1010, 2.25, "happy", facing=-1)
    # mesa com tampo de madeira (na frente do mestre)
    cv.shape([('rect', 300, 600, 1300, 660, 8)], (120, 76, 44), line=3.4, shadow=0.35)
    for i in range(6): cv.line([(320 + i * 160, 610), (420 + i * 160, 650)], w=1.4, col=(95, 58, 32))
    cv.shape([('rect', 340, 660, 1260, 900, 4)], (98, 60, 34), line=3, shadow=0.4, dots=True)
    scroll(cv, 520, 520, 290, 90)
    cv.shape([('ell', 900, 560, 70, 58)], (160, 120, 66), line=3, shadow=0.45)
    cv.shape([('rect', 872, 494, 928, 515, 5)], (120, 86, 46), line=2.4, shadow=0.3)
    cv.glow(900, 560, 110, (120, 230, 255), 80)
    for (cx, cy) in ((830, 600), (855, 612), (985, 596), (960, 608), (1010, 584), (880, 618)):
        coin(cv, cx, cy, 15)
    cv.shape([('ell', 470, 580, 26, 18)], (40, 40, 60), line=2.2, shadow=0.3)   # tinteiro
    hero(cv, 420, 900, 1.6, "happy", facing=1, armR=(85, 1.2), armL=(-15, 0.9), quill=True, look=(0.6, 0.3))
    cv.shape([('ell', 1380, 250, 175, 70)], (255, 255, 250), line=3.2, shadow=0.1, hi=0.0, rim=False)
    cv.shape([('poly', [(1260, 290), (1205, 360), (1300, 300)])], (255, 255, 250), line=2.6, shadow=0.05, hi=0.0, rim=False)
    cv.flat([('ell', 1380, 250, 168, 63)], (255, 255, 250))
    cv.text("Assine aqui, rapaz!", 1380, 250, 30, (40, 30, 40), 0, 0, scene=True)
    finish(cv, OUT + "/panel_2.png")

def panel3():
    cv = Canvas(gradient((26, 28, 52), (54, 50, 76), (40, 40, 66), 0.5))
    cv.light = (0.8, -0.55)    # vela à direita
    cv.rim = (255, 190, 110)
    for i in range(11):
        cv.shape([('rect', i * 150 - 10, -20, i * 150 + 140, 640, 2)], mix((70, 62, 72), (84, 76, 86), (i % 3) / 2), line=2.2, shadow=0.15, hi=0.04, rim=False)
        cv.line([(i * 150 + 30, 100), (i * 150 + 60, 300)], w=1.2, col=(56, 50, 60))
    cv.shape([('rect', -20, 640, W + 20, H + 20, 0)], (58, 48, 48), line=2.4, shadow=0.05, rim=False)
    # janela com chuva e relâmpago
    cv.shape([('rect', 120, 110, 440, 400, 4)], (40, 60, 110), line=3.4, shadow=0.1, rim=False)
    cv.shape([('poly', [(300, 120), (270, 220), (310, 220), (260, 340), (340, 200), (300, 200), (330, 120)])], (230, 240, 255), line=1.6, shadow=0.0, rim=False)
    cv.line([(280, 110), (280, 400)], w=5); cv.line([(120, 255), (440, 255)], w=5)
    for i in range(22):
        rx, ry = 135 + (i * 53) % 290, 125 + (i * 89) % 260
        cv.line([(rx, ry), (rx - 9, ry + 28)], w=1.4, col=(170, 200, 255))
    # ferramentas penduradas sem uso, teia e mesa vazia
    for i in range(4):
        cv.line([(560 + i * 70, 120), (560 + i * 70, 160)], w=1.5, col=(150, 140, 130))
        cv.shape([('rect', 548 + i * 70, 160, 572 + i * 70, 260 - i * 10, 3)], (120, 110, 100), line=2, shadow=0.35, rim=False)
    for a in range(0, 91, 15):
        cv.line([(1580, 20), (1580 - 190 * math.cos(math.radians(a)), 20 + 190 * math.sin(math.radians(a)))], w=1, col=(190, 190, 205))
    for rr in (50, 100, 150): cv.flat([('pie', 1580, 20, rr, rr, 90, 180)], (200, 200, 215), alpha=0)
    cv.shape([('rect', 640, 600, 1180, 640, 5)], (110, 74, 44), line=3, shadow=0.3)
    cv.shape(limb((670, 640), (660, 860), 22), (88, 58, 32), line=2.6, shadow=0.4)
    cv.shape(limb((1150, 640), (1160, 860), 22), (88, 58, 32), line=2.6, shadow=0.4)
    # vela
    cv.shape([('rect', 1080, 520, 1110, 600, 4)], (245, 240, 220), line=2.4, shadow=0.3)
    cv.glow(1095, 500, 150, (255, 190, 100), 150)
    cv.shape([('poly', [(1095, 470), (1106, 500), (1095, 515), (1084, 500)])], (255, 220, 120), line=1.6, shadow=0.0, rim=False)
    # contas de dívida espalhadas e a conta gigante
    for i, (px, py, a) in enumerate(((760, 590, -8), (880, 585, 5), (980, 592, -3))):
        cv.shape([('poly', [(px - 50, py - 30), (px + 50, py - 36), (px + 54, py + 4), (px - 46, py + 8)])], (240, 234, 214), line=2, shadow=0.2, rim=False)
        cv.text("DÍVIDA", px, py - 12, 15, (190, 40, 50), a, 0, scene=True)
    cv.shape([('poly', [(1240, 380), (1520, 360), (1540, 880), (1250, 890)])], (248, 244, 228), line=3, shadow=0.15)
    cv.text("DÍVIDA", 1390, 420, 40, (200, 30, 40), -2, 0, scene=True)
    for i, v in enumerate(["500 Lúmens", "1.200 Lúmens", "3.800 Lúmens", "9.400 Lúmens", "27.000 Lúmens", "81.000 Lúmens"]):
        cv.text(v, 1392, 485 + i * 58, 24, INK if i < 5 else (200, 30, 40), -2, 0, scene=True)
    cv.text("JUROS!", 1400, 840, 30, (200, 30, 40), -8, 2)
    # herói sentado, mãos na cabeça
    cv.layer("fg")
    hero(cv, 860, 880, 1.6, "sad", facing=1, sit=True, armL=(160, 0.95), armR=(-160, 0.95), tears=True, look=(-0.2, 0.6))
    cv.shape([('rect', 760, 760, 1000, 790, 4)], (110, 74, 44), line=2.6, shadow=0.3)   # banquinho
    cv.text("SUSPIRO...", 560, 470, 30, (190, 200, 240), 4, 3)
    finish(cv, OUT + "/panel_3.png")

def panel4():
    cv = Canvas(gradient((40, 10, 10), (120, 30, 20)))
    cv.light = (-0.2, -0.98)
    cv.rim = (255, 140, 60)
    # porta aberta com a forja acesa atrás: os ferreiros em contraluz
    cv.shape([('rect', 700, 60, 1500, 900, 4)], (255, 150, 60), line=4, shadow=0.0, hi=0.0, rim=False)
    cv.glow(1100, 460, 520, (255, 190, 90), 210)
    for i in range(70):
        r = random.Random(i)
        a = r.uniform(-1.2, 1.2) + math.pi
        cv.line([(1100 + math.cos(a) * 200, 470 + math.sin(a) * 200), (1100 + math.cos(a) * 1200, 470 + math.sin(a) * 1200)], w=r.uniform(1, 4), col=(255, 200, 120))
    cv.shape([('rect', -20, 640, W + 20, H + 20, 0)], (70, 30, 25), line=3, shadow=0.0, rim=False)
    # sombras compridas dos ferreiros no chão
    cv.flat([('poly', [(900, 900), (1150, 900), (500, 660), (330, 660)])], (25, 10, 12), alpha=200)
    cv.flat([('poly', [(1200, 900), (1400, 900), (900, 660), (760, 660)])], (25, 10, 12), alpha=200)
    cv.layer("fg")
    smith(cv, 1080, 900, 2.5, "angry", facing=-1, arm_up=True, look=(-0.7, 0.3))
    smith(cv, 1370, 920, 2.1, "angry", facing=-1, hammer=False, bandana=True, shirt=(60, 90, 140), beard=(40, 30, 26), look=(-0.7, 0.3))
    # herói encolhido no chão
    hero(cv, 330, 900, 1.35, "scared", facing=1, armL=(-150, 0.9), armR=(150, 0.9), look=(0.7, -0.4))
    for (dx, dy) in ((-120, -320), (110, -350), (150, -280)):
        cv.shape([('poly', [(330 + dx, 900 + dy - 18), (330 + dx + 9, 900 + dy), (330 + dx, 900 + dy + 10), (330 + dx - 9, 900 + dy)])], (150, 210, 255), line=1.8, shadow=0.2, rim=False)
    cv.text("HORA DE PAGAR!", 560, 140, 70, (255, 230, 90), -8)
    cv.text("BAM!", 1450, 140, 54, (255, 120, 80), 12)
    finish(cv, OUT + "/panel_4.png")

def panel5():
    cv = Canvas(gradient((22, 18, 36), (52, 40, 58)))
    cv.light = (-0.7, -0.7)
    cv.rim = (140, 120, 255)
    # boca da mina com escoras, trilhos e outros escravos trabalhando lá dentro
    cv.shape([('ell', 800, 520, 520, 470)], (88, 76, 82), line=3.6, shadow=0.3, hi=0.08, rim=False)
    cv.shape([('ell', 800, 570, 400, 400)], (14, 10, 24), line=3, shadow=0.0, hi=0.0, rim=False)
    cv.glow(800, 560, 330, (100, 70, 190), 130)
    for (cx, cy, h, col) in ((560, 760, 90, (120, 220, 255)), (1030, 740, 110, (190, 120, 255)), (930, 700, 60, (120, 255, 200))):
        crystal(cv, cx, cy, h, col)
    slave(cv, 640, 780, 0.75, 0, (130, 110, 90), 1)
    slave(cv, 990, 760, 0.65, 1, (110, 120, 130), -1)
    slave(cv, 820, 700, 0.45, 0, (120, 100, 110), 1)
    for x in (410, 1190):
        cv.shape([('rect', x - 34, 230, x + 34, 900, 4)], (128, 84, 48), line=3.2, shadow=0.4, dots=True)
    cv.shape([('rect', 370, 200, 1230, 268, 4)], (128, 84, 48), line=3.4, shadow=0.4)
    cv.shape([('rect', -20, 820, W + 20, H + 20, 0)], (70, 58, 58), line=3, shadow=0.0, rim=False)
    for i in range(7):
        yy = 880 - i * 34
        cv.shape([('rect', 690 + i * 12, yy - 6, 910 - i * 12, yy + 6, 2)], (110, 74, 44), line=2, shadow=0.3, rim=False)
    cv.line([(700, 900), (775, 650)], w=5, col=(160, 160, 175)); cv.line([(900, 900), (825, 650)], w=5, col=(160, 160, 175))
    cv.shape([('rect', 1260, 300, 1530, 430, 6)], (205, 172, 120), line=3.2, shadow=0.25)
    cv.text("MINA DA", 1395, 342, 32, INK, 0, 0, scene=True)
    cv.text("GUILDA", 1395, 388, 32, INK, 0, 0, scene=True)
    cv.shape(limb((1395, 430), (1395, 840), 22), (110, 74, 44), line=2.6, shadow=0.35)
    cv.layer("fg")
    smith(cv, 1110, 900, 1.95, "angry", facing=-1, point=True, bandana=True, shirt=(60, 90, 140), beard=(40, 30, 26), look=(-0.8, 0.2), hammer=False)
    hero(cv, 470, 900, 1.5, "sad", facing=1, helmet=True, chain=True, pick=True, armR=(35, 0.95), armL=(-10, 0.9), look=(0.6, 0.2), dirty=True)
    cv.text("TRABALHE!", 1180, 120, 52, (255, 230, 90), 6)
    finish(cv, OUT + "/panel_5.png")

def panel6():
    cv = Canvas(gradient((8, 8, 24), (26, 16, 48)))
    cv.light = (0.85, -0.5)
    cv.rim = (120, 230, 255)
    # caverna gigante: o Coração lá no fundo
    cv.glow(1000, 400, 300, (255, 50, 90), 190)
    cv.shape([('ell', 1000, 400, 70, 90)], (255, 80, 110), line=3, shadow=0.3, hi=0.3, rim=False)
    cv.flat([('ell', 1000, 400, 34, 46)], (255, 210, 215))
    for i in range(8):
        a = i * math.tau / 8
        cv.line([(1000 + math.cos(a) * 110, 400 + math.sin(a) * 130), (1000 + math.cos(a) * 190, 400 + math.sin(a) * 210)], w=2.5, col=(255, 120, 150))
    for (x, y, h, col, lean) in ((110, 860, 420, (90, 220, 255), 20), (290, 900, 280, (180, 110, 255), -15), (1380, 870, 460, (110, 255, 200), -25),
                                  (1540, 900, 320, (90, 220, 255), 10), (640, 720, 150, (255, 120, 220), 5), (1200, 700, 170, (120, 230, 255), -8),
                                  (1090, 640, 90, (180, 110, 255), 4), (820, 640, 80, (110, 255, 200), -3)):
        crystal(cv, x, y, h, col, lean)
    for i in range(14):
        x = 30 + i * 118
        hh = 80 + (i * 53) % 140
        cv.shape([('poly', [(x - 44, -10), (x + 44, -10), (x + 6, hh)])], (46, 36, 62), line=2.6, shadow=0.3, rim=False)
    cv.shape([('poly', [(-20, 820), (500, 760), (760, 800), (W + 20, 780), (W + 20, H + 20), (-20, H + 20)])], (32, 24, 44), line=3, shadow=0.0, rim=False)
    # facho da lanterna até o Coração
    lay = Image.new("RGBA", cv.img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).polygon([(s(470), s(470)), (s(1000), s(330)), (s(1000), s(470))], fill=(255, 250, 200, 50))
    cv.img.alpha_composite(lay.filter(ImageFilter.GaussianBlur(s(14))))
    # o diário velho de outro escravo no chão (a história começa aqui)
    cv.shape([('poly', [(700, 820), (860, 800), (880, 850), (720, 872)])], (110, 60, 40), line=3, shadow=0.4)
    cv.shape([('poly', [(712, 818), (850, 800), (866, 838), (726, 858)])], (236, 222, 186), line=1.8, shadow=0.15, rim=False)
    for k in range(4): cv.line([(735 + k * 3, 824 + k * 8), (840 + k * 3, 810 + k * 8)], w=1, col=(120, 100, 80))
    cv.glow(790, 830, 70, (255, 240, 200), 60)
    cv.text("?", 820, 760, 40, (255, 240, 200), 0, 3)
    cv.layer("fg")
    hero(cv, 470, 900, 1.65, "determined", facing=1, helmet=True, pick=True, armR=(35, 0.95), armL=(-15, 0.9), look=(0.8, -0.3), lamp_on=True, dirty=True)
    cv.text("O CORAÇÃO...", 1180, 160, 46, (255, 170, 190), 6)
    finish(cv, OUT + "/panel_6.png")

if __name__ == "__main__":
    OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "story")
    os.makedirs(OUT, exist_ok=True)
    only = sys.argv[2] if len(sys.argv) > 2 else None
    jobs = [("1", panel1), ("2", panel2), ("3", panel3), ("4", panel4), ("5", panel5), ("6", panel6), ("a", bram1), ("b", bram2), ("c", bram3), ("d", bram4), ("p", portraits), ("e", end1), ("f", end2), ("g", end3), ("h", end4)]
    for key, f in jobs:
        if only and key not in only: continue
        f()
        print("quadro", key)
