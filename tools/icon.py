import math, sys
from PIL import Image, ImageDraw, ImageFilter, ImageChops
S = 2048
def hexagon(cx, cy, r, rot=math.pi/6):
    return [(cx + r*math.cos(rot + i*math.pi/3), cy + r*math.sin(rot + i*math.pi/3)) for i in range(6)]
def radial(size, inner, outer, cx, cy, rad):
    img = Image.new('RGB', (size, size))
    px = img.load()
    for y in range(0, size, 4):
        for x in range(0, size, 4):
            t = min(1, math.hypot(x-cx, y-cy)/rad)
            c = tuple(int(inner[k]*(1-t)+outer[k]*t) for k in range(3))
            for yy in range(4):
                for xx in range(4):
                    if x+xx < size and y+yy < size: px[x+xx, y+yy] = c
    return img
c = S/2
# fundo: hexágono com gradiente radial
bg = radial(S, (52, 30, 92), (8, 6, 20), c, c*0.85, S*0.62)
mask = Image.new('L', (S, S), 0)
ImageDraw.Draw(mask).polygon(hexagon(c, c, S*0.49), fill=255)
img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
img.paste(bg, (0, 0), mask)
d = ImageDraw.Draw(img)
# chão de caverna: silhueta escura embaixo
ground = [(c - S*0.45, S*0.78), (c - S*0.2, S*0.72), (c, S*0.75), (c + S*0.22, S*0.71), (c + S*0.45, S*0.77), (c + S*0.45, S), (c - S*0.45, S)]
gl = Image.new('RGBA', (S, S), (0, 0, 0, 0))
ImageDraw.Draw(gl).polygon(ground, fill=(14, 9, 26, 255))
img = Image.alpha_composite(img, Image.composite(gl, Image.new('RGBA', (S, S), (0, 0, 0, 0)), mask))
# brilho atrás dos cristais
glow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([c - S*0.3, S*0.25, c + S*0.3, S*0.8], fill=(90, 230, 255, 150))
glow = glow.filter(ImageFilter.GaussianBlur(S*0.08))
img = Image.alpha_composite(img, Image.composite(glow, Image.new('RGBA', (S, S), (0, 0, 0, 0)), mask))
layer = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(layer)
# picareta atrás (diagonal)
def thick_line(p0, p1, w, col):
    dx, dy = p1[0]-p0[0], p1[1]-p0[1]; L = math.hypot(dx, dy); nx, ny = -dy/L*w/2, dx/L*w/2
    d.polygon([(p0[0]+nx, p0[1]+ny), (p1[0]+nx, p1[1]+ny), (p1[0]-nx, p1[1]-ny), (p0[0]-nx, p0[1]-ny)], fill=col)
thick_line((c + S*0.3, S*0.84), (c - S*0.17, S*0.24), S*0.045, (138, 88, 48))
thick_line((c + S*0.3, S*0.84), (c - S*0.17, S*0.24), S*0.018, (176, 120, 70))
# cabeça da picareta (arco dourado)
head = []
for i in range(25):
    a = math.radians(200 + i*5.8)
    head.append((c - S*0.1 + math.cos(a)*S*0.3, S*0.42 + math.sin(a)*S*0.2))
inner = []
for i in range(25):
    a = math.radians(200 + i*5.8)
    inner.append((c - S*0.1 + math.cos(a)*S*0.3, S*0.42 + math.sin(a)*S*0.2 + S*0.05*math.sin(math.pi*i/24) + S*0.01))
d.polygon(head + inner[::-1], fill=(255, 196, 90))
d.line(head, fill=(255, 236, 170), width=int(S*0.008))
# cristais: (base x, largura, altura, inclinação, cor clara, cor média, cor escura)
def crystal(bx, by, w, h, lean, light, mid, dark):
    tip = (bx + lean, by - h)
    l = (bx - w/2, by - h*0.22); r = (bx + w/2, by - h*0.22)
    bl = (bx - w/2*0.9, by); br = (bx + w/2*0.9, by)
    m = (bx + lean*0.15, by - h*0.3)
    d.polygon([l, tip, m, (bx, by)], fill=mid)
    d.polygon([m, tip, r, (bx, by)], fill=dark)
    d.polygon([bl, l, (bx, by)], fill=mid)
    d.polygon([(bx, by), r, br], fill=dark)
    d.polygon([l, tip, (bx - w*0.12 + lean*0.5, by - h*0.55)], fill=light)
    d.line([l, tip, r], fill=(235, 252, 255), width=int(S*0.006))
crystal(c - S*0.2, S*0.76, S*0.16, S*0.36, -S*0.05, (196, 168, 255), (140, 104, 255), (86, 58, 190))
crystal(c + S*0.2, S*0.76, S*0.15, S*0.32, S*0.06, (196, 168, 255), (140, 104, 255), (86, 58, 190))
crystal(c, S*0.78, S*0.22, S*0.58, S*0.01, (214, 252, 255), (94, 231, 255), (32, 150, 214))
crystal(c - S*0.08, S*0.8, S*0.1, S*0.2, -S*0.04, (214, 252, 255), (94, 231, 255), (32, 150, 214))
# faíscas
for (x, y, r) in [(0.3, 0.3, 0.012), (0.72, 0.26, 0.009), (0.66, 0.5, 0.007), (0.36, 0.55, 0.006)]:
    d.ellipse([S*x - S*r, S*y - S*r, S*x + S*r, S*y + S*r], fill=(230, 252, 255))
img = Image.alpha_composite(img, Image.composite(layer, Image.new('RGBA', (S, S), (0, 0, 0, 0)), mask))
d = ImageDraw.Draw(img)
# moldura hexagonal dupla (ciano + dourado)
d.line(hexagon(c, c, S*0.485) + [hexagon(c, c, S*0.485)[0]], fill=(94, 231, 255), width=int(S*0.022), joint='curve')
d.line(hexagon(c, c, S*0.455) + [hexagon(c, c, S*0.455)[0]], fill=(255, 202, 87), width=int(S*0.008), joint='curve')
out = sys.argv[1]
big = img.resize((512, 512), Image.LANCZOS)
big.save(out + '/icon_512.png')
img.resize((1024, 1024), Image.LANCZOS).save(out + '/icon_1024.png')
big.save(out + '/icon.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print('ok')
