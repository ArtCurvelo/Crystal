"""Gera os quadros da HQ de abertura (assets/story/panel_N.png) em estilo cartoon:
contorno preto grosso, cores chapadas, sombra em retícula e moldura de quadrinho.
Uso: python3 tools/comic.py [pasta_saida]"""
import math, os, random, sys
from PIL import Image, ImageDraw, ImageFilter

W, H = 1600, 900
K = 2                     # desenha em 2x e reduz (antisserrilhado)
SW, SH = W * K, H * K
INK = (22, 16, 24)
OUT = max(4, 5 * K)       # espessura do contorno

def s(v):                 # escala de coordenada
    return v * K

class Pen:
    def __init__(self, img):
        self.img = img
        self.d = ImageDraw.Draw(img)

    def poly(self, pts, fill, w=OUT):
        pts = [(s(x), s(y)) for x, y in pts]
        self.d.polygon(pts, fill=fill)
        self.d.line(pts + [pts[0]], fill=INK, width=w, joint="curve")
        r = w / 2
        for x, y in pts:
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=INK)

    def ell(self, cx, cy, rx, ry, fill, w=OUT):
        self.d.ellipse([s(cx - rx), s(cy - ry), s(cx + rx), s(cy + ry)], fill=fill, outline=INK if w else None, width=w)

    def rect(self, x0, y0, x1, y1, fill, r=6, w=OUT):
        self.d.rounded_rectangle([s(x0), s(y0), s(x1), s(y1)], radius=s(r), fill=fill, outline=INK if w else None, width=w)

    def line(self, pts, w=OUT, col=INK):
        pts = [(s(x), s(y)) for x, y in pts]
        self.d.line(pts, fill=col, width=w, joint="curve")
        r = w / 2
        for x, y in (pts[0], pts[-1]):
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=col)

    def arc(self, cx, cy, rx, ry, a0, a1, w=OUT, col=INK):
        self.d.arc([s(cx - rx), s(cy - ry), s(cx + rx), s(cy + ry)], a0, a1, fill=col, width=w)

    def limb(self, p0, p1, width, fill):
        """Braço/perna: retângulo arredondado de p0 a p1."""
        (x0, y0), (x1, y1) = p0, p1
        dx, dy = x1 - x0, y1 - y0
        L = math.hypot(dx, dy) or 1
        nx, ny = -dy / L * width / 2, dx / L * width / 2
        self.poly([(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)], fill)
        self.ell(x1, y1, width / 2, width / 2, fill)

def darker(c, f=0.7):
    return tuple(int(v * f) for v in c[:3])

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def gradient(top, bottom):
    img = Image.new("RGB", (SW, SH))
    d = ImageDraw.Draw(img)
    for y in range(SH):
        d.line([(0, y), (SW, y)], fill=lerp(top, bottom, y / SH))
    return img

def halftone(img, box, col=(0, 0, 0), alpha=60, step=14, grow=True):
    """Retícula de pontos (sombra de HQ) dentro de box=(x0,y0,x1,y1)."""
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    x0, y0, x1, y1 = [s(v) for v in box]
    st = s(step)
    for yi, y in enumerate(range(int(y0), int(y1), st)):
        off = st / 2 if yi % 2 else 0
        t = (y - y0) / max(1, (y1 - y0))
        r = st * (0.12 + 0.3 * (t if grow else 1 - t))
        for x in range(int(x0 + off), int(x1), st):
            d.ellipse([x - r, y - r, x + r, y + r], fill=col + (alpha,))
    img.alpha_composite(lay)

def burst(img, cx, cy, c1, c2, n=28):
    """Fundo de explosão (raios alternados), clássico de quadro dramático."""
    d = ImageDraw.Draw(img)
    R = s(2000)
    for i in range(n):
        a0 = 2 * math.pi * i / n
        a1 = 2 * math.pi * (i + 0.5) / n
        d.polygon([(s(cx), s(cy)), (s(cx) + R * math.cos(a0), s(cy) + R * math.sin(a0)), (s(cx) + R * math.cos(a1), s(cy) + R * math.sin(a1))], fill=c1 if i % 2 else c2)

def speed_lines(img, cx, cy, n=70, col=(255, 255, 255, 90), r0=330):
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    rnd = random.Random(7)
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        r1 = r0 + rnd.uniform(0, 120)
        r2 = 1400
        w = rnd.uniform(1.5, 5) * K
        d.line([(s(cx + r1 * math.cos(a)), s(cy + r1 * math.sin(a))), (s(cx + r2 * math.cos(a)), s(cy + r2 * math.sin(a)))], fill=col, width=int(w))
    img.alpha_composite(lay)

def glow(img, cx, cy, r, col, a=150):
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).ellipse([s(cx - r), s(cy - r), s(cx + r), s(cy + r)], fill=col + (a,))
    lay = lay.filter(ImageFilter.GaussianBlur(s(r) * 0.45))
    img.alpha_composite(lay)

# ------------------------------------------------------------------ personagens
SKIN = (238, 190, 150)
SKIN_SMITH = (205, 145, 110)

def face(p, cx, cy, r, mood, facing=1, skin=SKIN):
    """Olhos, sobrancelhas e boca conforme o humor."""
    ex = r * 0.36
    ey = cy - r * 0.05
    fx = cx + facing * r * 0.12
    for sx in (-1, 1):
        x = fx + sx * ex
        if mood == "sad":
            p.ell(x, ey + r * 0.05, r * 0.11, r * 0.13, (255, 255, 255), w=max(3, OUT // 2))
            p.ell(x + facing * r * 0.02, ey + r * 0.1, r * 0.06, r * 0.07, INK, w=0)
            p.line([(x - r * 0.15, ey - r * 0.12 - sx * r * 0.06), (x + r * 0.15, ey - r * 0.12 + sx * r * 0.06)], w=max(4, OUT - 2))
        elif mood == "scared":
            p.ell(x, ey, r * 0.15, r * 0.18, (255, 255, 255), w=max(3, OUT // 2))
            p.ell(x, ey, r * 0.05, r * 0.05, INK, w=0)
            p.line([(x - r * 0.15, ey - r * 0.3 - sx * r * 0.06), (x + r * 0.15, ey - r * 0.3 + sx * r * 0.06)], w=max(4, OUT - 2))
        elif mood == "angry":
            p.ell(x, ey + r * 0.04, r * 0.11, r * 0.09, (255, 255, 255), w=max(3, OUT // 2))
            p.ell(x + facing * r * 0.03, ey + r * 0.05, r * 0.05, r * 0.05, INK, w=0)
            p.line([(x - r * 0.18, ey - r * 0.2 + sx * r * 0.1), (x + r * 0.18, ey - r * 0.08 - sx * r * 0.1)], w=OUT + 2)
        else:  # happy, determined, neutral
            p.ell(x, ey, r * 0.1, r * 0.14, (255, 255, 255), w=max(3, OUT // 2))
            p.ell(x + facing * r * 0.03, ey + r * 0.02, r * 0.055, r * 0.075, INK, w=0)
            if mood == "determined":
                p.line([(x - r * 0.17, ey - r * 0.26 + sx * r * 0.05), (x + r * 0.17, ey - r * 0.2 - sx * r * 0.05)], w=OUT)
            else:
                p.arc(x, ey - r * 0.2, r * 0.15, r * 0.08, 200, 340, w=max(4, OUT - 2))
    my = cy + r * 0.42
    mx = cx + facing * r * 0.1
    if mood == "happy":
        p.d.chord([s(mx - r * 0.28), s(my - r * 0.16), s(mx + r * 0.28), s(my + r * 0.2)], 0, 180, fill=(120, 30, 40), outline=INK, width=max(4, OUT - 2))
    elif mood in ("sad", "scared"):
        p.arc(mx, my + r * 0.12, r * 0.2, r * 0.1, 200, 340, w=OUT - 1)
        if mood == "sad":
            p.ell(cx - facing * r * 0.4, ey + r * 0.35, r * 0.05, r * 0.09, (120, 190, 255), w=2 * K)
    elif mood == "angry":
        p.rect(mx - r * 0.25, my - r * 0.05, mx + r * 0.25, my + r * 0.12, (255, 255, 255), r=3, w=max(4, OUT - 2))
        p.line([(mx - r * 0.25, my + r * 0.035), (mx + r * 0.25, my + r * 0.035)], w=2 * K)
    else:
        p.line([(mx - r * 0.18, my), (mx + r * 0.15, my - r * 0.03)], w=OUT - 1)
    # nariz
    p.arc(cx + facing * r * 0.25, cy + r * 0.2, r * 0.1, r * 0.08, 90 - facing * 60, 90 + facing * 60 + 120 if facing < 0 else 210, w=max(3, OUT - 3))

def hero(p, x, y, sc=1.0, mood="happy", facing=1, helmet=False, arm_l=(-30, 40), arm_r=(30, 40), chain=False, pick=False, shirt=(70, 120, 200)):
    """Escavador (pés em x,y). Ângulos dos braços em graus (0 = para baixo, positivo = para a frente)."""
    u = 100 * sc
    pants = (95, 80, 60)
    hip = (x, y - u * 1.05)
    neck = (x, y - u * 2.0)
    # pernas
    for dx in (-0.22, 0.22):
        p.limb((x + dx * u, hip[1]), (x + dx * u * 1.2, y - u * 0.1), u * 0.3, pants)
        p.rect(x + dx * u * 1.2 - u * 0.2 + facing * u * 0.05, y - u * 0.18, x + dx * u * 1.2 + u * 0.2 + facing * u * 0.08, y + u * 0.02, (70, 45, 30), r=8 * sc)
    if chain:
        bx = x - facing * u * 0.9
        for i in range(5):
            t = i / 5
            p.ell(x - facing * u * 0.25 + (bx - x + facing * u * 0.25) * t, y - u * 0.12 + math.sin(t * 3) * u * 0.05, u * 0.09, u * 0.065, (190, 190, 200), w=3 * K)
        p.ell(bx, y - u * 0.18, u * 0.2, u * 0.2, (125, 125, 140))
        p.arc(bx - u * 0.05, y - u * 0.24, u * 0.1, u * 0.08, 200, 280, w=3 * K, col=(160, 160, 170))
    # tronco
    p.poly([(x - u * 0.45, neck[1] + u * 0.05), (x + u * 0.45, neck[1] + u * 0.05), (x + u * 0.38, hip[1] + u * 0.08), (x - u * 0.38, hip[1] + u * 0.08)], shirt)
    p.rect(x - u * 0.4, hip[1] - u * 0.06, x + u * 0.4, hip[1] + u * 0.1, (90, 60, 35), r=3)
    # remendo
    p.rect(x - u * 0.25, neck[1] + u * 0.4, x - u * 0.05, neck[1] + u * 0.6, darker(shirt, 0.8), r=2, w=3 * K)
    # braços
    sh_y = neck[1] + u * 0.2
    hands = []
    for side, (a_deg, ln) in ((-1, arm_l), (1, arm_r)):
        a = math.radians(a_deg)
        sx = x + side * u * 0.42
        ex = sx + math.sin(a) * u * ln / 40 * 0.95 * (1 if side > 0 else 1)
        ey = sh_y + math.cos(a) * u * ln / 40 * 0.95
        p.limb((sx, sh_y), (ex, ey), u * 0.24, shirt)
        p.ell(ex, ey, u * 0.14, u * 0.14, SKIN)
        hands.append((ex, ey))
    if pick:
        hx, hy = hands[1]
        top = (hx + facing * u * 0.25, hy - u * 1.1)
        p.limb((hx - facing * u * 0.05, hy + u * 0.35), top, u * 0.11, (150, 100, 55))
        p.poly([(top[0] - u * 0.55, top[1] + u * 0.2), (top[0], top[1] - u * 0.12), (top[0] + u * 0.55, top[1] + u * 0.2), (top[0], top[1] + u * 0.08)], (150, 150, 160))
        p.ell(hx, hy, u * 0.14, u * 0.14, SKIN)
    # cabeça
    hr = u * 0.55
    hc = (x, neck[1] - hr * 0.85)
    p.ell(hc[0], hc[1], hr, hr * 1.02, SKIN)
    p.ell(hc[0] - facing * hr * 0.92, hc[1] + hr * 0.05, hr * 0.16, hr * 0.22, SKIN)
    if helmet:
        p.d.chord([s(hc[0] - hr * 1.05), s(hc[1] - hr * 1.2), s(hc[0] + hr * 1.05), s(hc[1] + hr * 0.5)], 180, 360, fill=(240, 190, 40), outline=INK, width=OUT)
        p.rect(hc[0] - hr * 1.15, hc[1] - hr * 0.42, hc[0] + hr * 1.15, hc[1] - hr * 0.28, (210, 160, 30), r=4)
        lx = hc[0] + facing * hr * 0.2
        p.rect(lx - hr * 0.22, hc[1] - hr * 0.95, lx + hr * 0.22, hc[1] - hr * 0.55, (90, 90, 100), r=4)
        p.ell(lx, hc[1] - hr * 0.75, hr * 0.14, hr * 0.14, (255, 250, 200), w=3 * K)
    else:
        # cabelo bagunçado
        hair = (110, 65, 35)
        pts = []
        for i in range(9):
            a = math.pi + i * math.pi / 8
            rr = hr * (1.12 if i % 2 else 0.98)
            pts.append((hc[0] + math.cos(a) * rr, hc[1] - hr * 0.1 + math.sin(a) * rr))
        pts += [(hc[0] + hr * 0.95, hc[1] - hr * 0.15), (hc[0] + hr * 0.5, hc[1] - hr * 0.5), (hc[0] - hr * 0.3, hc[1] - hr * 0.42), (hc[0] - hr * 0.95, hc[1] - hr * 0.1)]
        p.poly(pts, hair)
    face(p, hc[0], hc[1] + hr * 0.1, hr, mood, facing)
    return hands

def smith(p, x, y, sc=1.0, mood="angry", facing=-1, hammer=True, chain_gold=False, apron=(120, 75, 40), shirt=(170, 60, 50), bald=True, arm_up=False):
    """Ferreiro grande e barbudo (pés em x,y)."""
    u = 100 * sc
    hip = (x, y - u * 1.0)
    neck = (x, y - u * 2.25)
    for dx in (-0.3, 0.3):
        p.limb((x + dx * u, hip[1]), (x + dx * u, y - u * 0.12), u * 0.38, (60, 50, 50))
        p.rect(x + dx * u - u * 0.26, y - u * 0.2, x + dx * u + u * 0.26, y + u * 0.02, (40, 30, 25), r=8 * sc)
    # tronco largo
    p.poly([(x - u * 0.75, neck[1]), (x + u * 0.75, neck[1]), (x + u * 0.62, hip[1] + u * 0.1), (x - u * 0.62, hip[1] + u * 0.1)], shirt)
    p.poly([(x - u * 0.45, neck[1] + u * 0.3), (x + u * 0.45, neck[1] + u * 0.3), (x + u * 0.5, hip[1] + u * 0.35), (x - u * 0.5, hip[1] + u * 0.35)], apron)
    if chain_gold:
        p.arc(x, neck[1] + u * 0.05, u * 0.42, u * 0.5, 20, 160, w=4 * K, col=(250, 200, 60))
        p.ell(x, neck[1] + u * 0.55, u * 0.12, u * 0.12, (250, 200, 60), w=3 * K)
    sh_y = neck[1] + u * 0.25
    hands = []
    for side in (-1, 1):
        sx = x + side * u * 0.72
        if arm_up and side == facing:
            ex, ey = sx + side * u * 0.25, sh_y - u * 0.75
        else:
            ex, ey = sx + side * u * 0.18, sh_y + u * 0.95
        p.limb((sx, sh_y), (ex, ey), u * 0.34, SKIN_SMITH)
        p.ell(ex, ey, u * 0.2, u * 0.2, SKIN_SMITH)
        hands.append((ex, ey))
    if hammer:
        hx, hy = hands[1 if facing > 0 else 0]
        if arm_up:
            top = (hx + facing * u * 0.4, hy - u * 0.9)
        else:
            top = (hx + facing * u * 0.1, hy - u * 0.95)
        p.limb((hx, hy + u * 0.2), top, u * 0.13, (130, 85, 45))
        dx, dy = top[0] - hx, top[1] - hy
        L = math.hypot(dx, dy)
        nx, ny = -dy / L, dx / L
        c = top
        hw, hl = u * 0.28, u * 0.42
        p.poly([(c[0] + nx * hl - dx / L * hw, c[1] + ny * hl - dy / L * hw), (c[0] + nx * hl + dx / L * hw, c[1] + ny * hl + dy / L * hw),
                (c[0] - nx * hl + dx / L * hw, c[1] - ny * hl + dy / L * hw), (c[0] - nx * hl - dx / L * hw, c[1] - ny * hl - dy / L * hw)], (110, 110, 120))
        p.ell(hx, hy, u * 0.2, u * 0.2, SKIN_SMITH)
    # cabeça + barba
    hr = u * 0.55
    hc = (x, neck[1] - hr * 0.75)
    p.ell(hc[0], hc[1], hr, hr, SKIN_SMITH)
    beard = (60, 35, 25) if not chain_gold else (150, 150, 155)
    p.poly([(hc[0] - hr * 0.95, hc[1] + hr * 0.05), (hc[0] + hr * 0.95, hc[1] + hr * 0.05), (hc[0] + hr * 0.6, hc[1] + hr * 1.25), (hc[0], hc[1] + hr * 1.6), (hc[0] - hr * 0.6, hc[1] + hr * 1.25)], beard)
    if not bald:
        p.d.chord([s(hc[0] - hr * 1.02), s(hc[1] - hr * 1.05), s(hc[0] + hr * 1.02), s(hc[1] + hr * 0.2)], 180, 360, fill=(160, 40, 40), outline=INK, width=OUT)
    else:
        p.arc(hc[0] - hr * 0.3, hc[1] - hr * 0.55, hr * 0.25, hr * 0.15, 200, 300, w=3 * K, col=(255, 230, 210))
    face(p, hc[0], hc[1] - hr * 0.12, hr * 0.95, mood, facing, SKIN_SMITH)
    # bigode por cima
    p.poly([(hc[0] - hr * 0.5, hc[1] + hr * 0.35), (hc[0], hc[1] + hr * 0.18), (hc[0] + hr * 0.5, hc[1] + hr * 0.35), (hc[0], hc[1] + hr * 0.45)], darker(beard, 0.8))
    return hands

# ------------------------------------------------------------------ cenários
def house(p, x, y, w, h, wall, roof, door=True, lit=True):
    p.rect(x, y - h, x + w, y, wall, r=2)
    p.poly([(x - w * 0.12, y - h), (x + w / 2, y - h - h * 0.65), (x + w * 1.12, y - h)], roof)
    if door:
        p.rect(x + w * 0.4, y - h * 0.5, x + w * 0.6, y, (90, 55, 30), r=4)
    for wx in (0.12, 0.7):
        p.rect(x + w * wx, y - h * 0.8, x + w * (wx + 0.18), y - h * 0.55, (255, 210, 110) if lit else (60, 70, 90), r=2)

def mountains(p, base, cols, seed=1):
    rnd = random.Random(seed)
    for i, col in enumerate(cols):
        pts = [(-50, base)]
        x = -50
        while x < W + 100:
            x += rnd.uniform(120, 260)
            pts.append((x, base - rnd.uniform(140, 330) + i * 70))
        pts.append((W + 100, base))
        p.poly(pts, col, w=OUT - 2)

def crystal(p, x, y, h, col, lean=0):
    w = h * 0.28
    p.poly([(x - w, y), (x - w * 0.8 + lean, y - h * 0.75), (x + lean, y - h), (x + w * 0.8 + lean, y - h * 0.75), (x + w, y)], col)
    p.line([(x + lean, y - h), (x + lean * 0.3, y)], w=max(3, OUT // 2), col=lerp(col, (255, 255, 255), 0.5))

def scroll(p, x, y, w, h, text_lines=4):
    p.rect(x, y, x + w, y + h, (245, 230, 190), r=6)
    for i in range(text_lines):
        p.line([(x + w * 0.12, y + h * (0.2 + i * 0.17)), (x + w * (0.85 if i % 2 else 0.7), y + h * (0.2 + i * 0.17))], w=3 * K, col=(120, 100, 80))
    p.ell(x, y + h / 2, w * 0.06, h / 2, (225, 205, 160))
    p.ell(x + w, y + h / 2, w * 0.06, h / 2, (225, 205, 160))

def coin(p, x, y, r=16):
    p.ell(x, y, r, r, (255, 210, 70), w=3 * K)
    p.poly([(x, y - r * 0.55), (x + r * 0.4, y), (x, y + r * 0.55), (x - r * 0.4, y)], (120, 230, 255), w=2 * K)

def sfx(img, text, x, y, size, fill, angle=-8):
    """Onomatopeia em letras grossas com contorno."""
    from PIL import ImageFont
    fp = os.path.join(os.path.dirname(__file__), "..", "assets", "fonts", "ChakraPetch-Bold.ttf")
    f = ImageFont.truetype(fp, s(size))
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.text((s(x), s(y)), text, font=f, fill=fill, stroke_width=s(size) // 9, stroke_fill=INK, anchor="mm")
    lay = lay.rotate(angle, center=(s(x), s(y)), resample=Image.BICUBIC)
    img.alpha_composite(lay)

def finish(img, path):
    """Moldura de quadrinho e redução para o tamanho final."""
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, SW - 1, SH - 1], outline=(250, 246, 235), width=s(18))
    d.rectangle([s(18), s(18), SW - s(18), SH - s(18)], outline=INK, width=s(6))
    img.convert("RGB").resize((W, H), Image.LANCZOS).save(path, optimize=True)

# ------------------------------------------------------------------ quadros
def panel1():
    img = gradient((255, 150, 90), (255, 215, 150)).convert("RGBA")
    p = Pen(img)
    glow(img, 1250, 300, 160, (255, 240, 180), 220)
    p.ell(1250, 300, 85, 85, (255, 235, 160), w=0)
    mountains(p, 560, [(170, 100, 120), (130, 80, 105)])
    p.rect(-20, 560, W + 20, H + 20, (120, 160, 90), r=0)
    halftone(img, (0, 560, W, H), (40, 60, 30), 50, 16)
    house(p, 120, 640, 260, 170, (215, 180, 140), (150, 60, 50))
    house(p, 980, 620, 230, 150, (200, 170, 150), (110, 70, 60))
    # oficina do ferreiro com chaminé e fumaça
    house(p, 520, 600, 330, 180, (170, 150, 140), (80, 60, 60), lit=True)
    p.rect(760, 360, 805, 470, (120, 110, 105), r=2)
    for i, (dx, r) in enumerate([(0, 30), (25, 40), (60, 52), (105, 62)]):
        p.ell(783 + dx, 335 - i * 60, r, r * 0.8, (235, 225, 225), w=OUT - 2)
    p.rect(-20, 700, W + 20, 760, (190, 160, 110), r=0, w=OUT - 2)
    hero(p, 1150, 860, 1.55, "happy", facing=-1, arm_l=(-150, 44), arm_r=(25, 40))
    finish(img, OUTDIR + "/panel_1.png")

def panel2():
    img = gradient((90, 55, 40), (150, 95, 60)).convert("RGBA")
    p = Pen(img)
    # parede de pedra da guilda e brasão
    for row in range(6):
        for col in range(9):
            off = 90 if row % 2 else 0
            p.rect(col * 190 - off, row * 95, col * 190 - off + 180, row * 95 + 85, (120, 85, 65), r=4, w=3 * K)
    p.ell(800, 170, 120, 120, (190, 150, 60))
    p.limb((740, 230), (860, 110), 26, (140, 140, 150))
    p.poly([(820, 90), (900, 130), (880, 150), (800, 110)], (110, 110, 120))
    glow(img, 800, 600, 330, (255, 200, 120), 90)
    smith(p, 1060, 900, 2.0, "happy", facing=-1, hammer=False, chain_gold=True, shirt=(110, 60, 130))
    # mesa (na frente do ferreiro)
    p.rect(330, 600, 1270, 660, (110, 70, 40), r=6)
    p.rect(380, 660, 420, 900, (90, 55, 30), r=4)
    p.rect(1180, 660, 1220, 900, (90, 55, 30), r=4)
    scroll(p, 600, 530, 250, 80)
    # pena de escrever na mão do herói
    p.poly([(745, 575), (805, 455), (822, 463), (757, 580)], (250, 250, 245), w=3 * K)
    hero(p, 480, 900, 1.65, "happy", facing=1, arm_r=(100, 52), arm_l=(-20, 40))
    p.ell(960, 560, 70, 55, (150, 110, 60))
    p.rect(935, 495, 985, 515, (120, 85, 45), r=4)
    for i, (cx, cy) in enumerate([(880, 590), (915, 595), (1040, 590), (1010, 598), (1065, 575)]):
        coin(p, cx, cy, 18)
    glow(img, 960, 560, 90, (120, 230, 255), 90)
    sfx(img, "ASSINADO!", 330, 140, 64, (255, 230, 90))
    finish(img, OUTDIR + "/panel_2.png")

def panel3():
    img = gradient((40, 45, 70), (70, 70, 95)).convert("RGBA")
    p = Pen(img)
    # oficina vazia: tábuas, teia, bancada quebrada
    for i in range(10):
        p.rect(i * 170 - 10, 0, i * 170 + 160, 620, (85, 75, 80) if i % 2 else (95, 85, 85), r=0, w=3 * K)
    p.rect(-20, 620, W + 20, H + 20, (60, 50, 50), r=0)
    halftone(img, (0, 0, W, 620), (0, 0, 20), 70, 18, grow=False)
    # janela com chuva
    p.rect(1080, 120, 1380, 380, (50, 70, 110), r=4)
    p.line([(1230, 120), (1230, 380)])
    p.line([(1080, 250), (1380, 250)])
    for i in range(14):
        rx = 1095 + (i * 47) % 280
        ry = 135 + (i * 83) % 230
        p.line([(rx, ry), (rx - 8, ry + 26)], w=2 * K, col=(170, 200, 255))
    # teia
    for a in range(0, 90, 18):
        p.line([(30, 30), (30 + 200 * math.cos(math.radians(a)), 30 + 200 * math.sin(math.radians(a)))], w=2 * K, col=(200, 200, 210))
    for rr in (60, 110, 160):
        p.arc(30, 30, rr, rr, 0, 90, w=2 * K, col=(200, 200, 210))
    # bancada quebrada
    p.poly([(200, 640), (560, 600), (570, 640), (210, 690)], (110, 75, 45))
    p.limb((240, 680), (220, 820), 24, (90, 60, 35))
    p.limb((520, 640), (600, 800), 24, (90, 60, 35))
    hero(p, 820, 870, 1.65, "sad", facing=1, arm_l=(-5, 42), arm_r=(5, 42))
    # bolsos vazios virados
    p.poly([(745, 690), (790, 690), (770, 740)], (230, 230, 220), w=3 * K)
    p.poly([(855, 690), (900, 690), (880, 740)], (230, 230, 220), w=3 * K)
    # conta da dívida, enorme, caindo até o chão
    p.rect(1040, 430, 1380, 880, (245, 240, 225), r=4)
    from PIL import ImageFont
    fp = os.path.join(os.path.dirname(__file__), "..", "assets", "fonts", "ChakraPetch-Bold.ttf")
    f = ImageFont.truetype(fp, s(46))
    fs = ImageFont.truetype(fp, s(30))
    d = ImageDraw.Draw(img)
    d.text((s(1210), s(480)), "DÍVIDA", font=f, fill=(190, 30, 40), anchor="mm")
    for i, v in enumerate(["500 Lúmens", "1.200 Lúmens", "3.800 Lúmens", "9.400 Lúmens", "27.000 Lúmens", "???.??? Lúmens"]):
        d.text((s(1210), s(545 + i * 52)), v, font=fs, fill=INK if i < 5 else (190, 30, 40), anchor="mm")
    sfx(img, "...", 640, 150, 90, (200, 210, 255), 0)
    finish(img, OUTDIR + "/panel_3.png")

def panel4():
    img = Image.new("RGBA", (SW, SH))
    burst(img, 800, 420, (200, 40, 40), (240, 110, 50))
    p = Pen(img)
    speed_lines(img, 800, 420, 80, (255, 230, 200, 110))
    smith(p, 1180, 900, 2.35, "angry", facing=-1, arm_up=True, bald=True)
    smith(p, 1450, 920, 2.0, "angry", facing=-1, hammer=False, bald=False, shirt=(60, 90, 140), apron=(100, 70, 45))
    hero(p, 380, 900, 1.5, "scared", facing=1, arm_l=(-150, 40), arm_r=(150, 40))
    # gotas de suor
    for (dx, dy) in [(-120, -330), (110, -360), (140, -300)]:
        p.ell(380 + dx, 900 + dy, 10, 16, (150, 210, 255), w=3 * K)
    sfx(img, "PAGUE!", 700, 120, 96, (255, 240, 80), -10)
    finish(img, OUTDIR + "/panel_4.png")

def panel5():
    img = gradient((30, 25, 45), (55, 45, 60)).convert("RGBA")
    p = Pen(img)
    # boca da mina: arco de pedra com escoras de madeira, escuridão dentro
    p.ell(800, 520, 470, 430, (80, 70, 75))
    p.ell(800, 560, 360, 360, (10, 8, 18))
    glow(img, 800, 560, 260, (90, 60, 160), 120)
    for x in (430, 1170):
        p.rect(x - 30, 260, x + 30, 900, (120, 80, 45), r=3)
    p.rect(400, 230, 1200, 290, (120, 80, 45), r=3)
    p.rect(-20, 800, W + 20, H + 20, (70, 60, 60), r=0)
    # trilho
    p.line([(700, 900), (760, 640)], w=4 * K, col=(150, 150, 160))
    p.line([(900, 900), (840, 640)], w=4 * K, col=(150, 150, 160))
    for i in range(6):
        yy = 880 - i * 45
        p.rect(690 + i * 10, yy - 6, 910 - i * 10, yy + 6, (100, 70, 40), r=2, w=3 * K)
    # placa
    p.rect(1240, 300, 1500, 420, (200, 170, 120), r=6)
    from PIL import ImageFont
    fp = os.path.join(os.path.dirname(__file__), "..", "assets", "fonts", "ChakraPetch-Bold.ttf")
    d = ImageDraw.Draw(img)
    d.text((s(1370), s(345)), "MINA DA", font=ImageFont.truetype(fp, s(36)), fill=INK, anchor="mm")
    d.text((s(1370), s(390)), "GUILDA", font=ImageFont.truetype(fp, s(36)), fill=INK, anchor="mm")
    p.limb((1370, 420), (1370, 800), 24, (110, 75, 45))
    smith(p, 1120, 900, 1.9, "angry", facing=-1, hammer=True, bald=False, shirt=(60, 90, 140))
    hero(p, 560, 900, 1.5, "sad", facing=1, helmet=True, chain=True, pick=True, arm_r=(40, 38), arm_l=(-10, 40))
    finish(img, OUTDIR + "/panel_5.png")

def panel6():
    img = gradient((12, 10, 30), (30, 20, 55)).convert("RGBA")
    p = Pen(img)
    # caverna gigante com cristais e o Coração lá no fundo
    glow(img, 860, 430, 220, (255, 60, 90), 170)
    p.ell(860, 430, 55, 70, (255, 90, 110))
    p.ell(860, 430, 28, 36, (255, 210, 210), w=0)
    for i, (x, y, h, c, lean) in enumerate([(120, 820, 380, (90, 220, 255), 20), (300, 860, 260, (180, 110, 255), -15), (1350, 840, 420, (110, 255, 200), -25),
                                             (1510, 880, 300, (90, 220, 255), 10), (560, 700, 140, (255, 120, 220), 5), (1120, 690, 160, (120, 230, 255), -8),
                                             (980, 640, 90, (180, 110, 255), 4), (700, 630, 80, (110, 255, 200), -3)]):
        glow(img, x, y - h * 0.5, h * 0.55, c, 70)
        crystal(p, x, y, h, c, lean)
    # estalactites
    for i in range(12):
        x = 40 + i * 140
        hh = 90 + (i * 53) % 120
        p.poly([(x - 40, -10), (x + 40, -10), (x + 5, hh)], (45, 35, 60), w=OUT - 2)
    # caminho de luz da lanterna
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).polygon([(s(520), s(470)), (s(860), s(380)), (s(860), s(480))], fill=(255, 250, 200, 55))
    img.alpha_composite(lay.filter(ImageFilter.GaussianBlur(s(12))))
    p.rect(-20, 860, W + 20, H + 20, (25, 20, 35), r=0)
    hero(p, 470, 900, 1.6, "determined", facing=1, helmet=True, pick=True, arm_r=(35, 38), arm_l=(-15, 40), shirt=(60, 110, 190))
    sfx(img, "?!", 900, 260, 70, (255, 160, 170), 8)
    finish(img, OUTDIR + "/panel_6.png")

if __name__ == "__main__":
    OUTDIR = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "assets", "story")
    os.makedirs(OUTDIR, exist_ok=True)
    for f in (panel1, panel2, panel3, panel4, panel5, panel6):
        f()
    print("ok", OUTDIR)
