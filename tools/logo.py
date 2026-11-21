import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops
F='/home/user/crystal-excavator/assets/fonts/ChakraPetch-Bold.ttf'
W,H=1800,560
def vgrad(w,h,stops):
    g=Image.new('RGB',(w,h)); px=g.load()
    for y in range(h):
        t=y/(h-1)
        for i in range(len(stops)-1):
            t0,c0=stops[i]; t1,c1=stops[i+1]
            if t0<=t<=t1:
                k=(t-t0)/(t1-t0) if t1>t0 else 0
                col=tuple(int(c0[j]*(1-k)+c1[j]*k) for j in range(3)); break
        for x in range(w): px[x,y]=col
    return g
def text_mask(text,font,spacing=0):
    w=int(font.getlength(text)+spacing*len(text))+40
    m=Image.new('L',(w,font.size+60),0); d=ImageDraw.Draw(m); x=20
    for ch in text:
        d.text((x,10),ch,font=font,fill=255); x+=font.getlength(ch)+spacing
    return m.crop(m.getbbox())
def styled(text,size,stops,spacing,outline,glowcol,facet=True):
    f=ImageFont.truetype(F,size)
    m=text_mask(text,f,spacing)
    pad=40; w,h=m.size[0]+pad*2,m.size[1]+pad*2
    mask=Image.new('L',(w,h),0); mask.paste(m,(pad,pad))
    fill=vgrad(w,h,stops).convert('RGBA')
    if facet:
        # faixa diagonal mais clara (aresta de cristal) na metade de cima
        band=Image.new('L',(w,h),0); bd=ImageDraw.Draw(band)
        bd.polygon([(0,pad),(w,pad),(w,pad+h*0.28),(0,pad+h*0.42)],fill=90)
        white=Image.new('RGBA',(w,h),(255,255,255,255))
        fill=Image.composite(white,fill,band)
    out=Image.new('RGBA',(w,h),(0,0,0,0))
    glow=Image.new('RGBA',(w,h),glowcol+(0,)); glow.putalpha(mask.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(16)))
    out=Image.alpha_composite(out,glow)
    stroke=Image.new('RGBA',(w,h),(10,8,30,255)); stroke.putalpha(mask.filter(ImageFilter.MaxFilter(outline)))
    out=Image.alpha_composite(out,stroke)
    body=fill.copy(); body.putalpha(mask)
    out=Image.alpha_composite(out,body)
    return out
crystal=styled('CRYSTAL',250,[(0,(235,253,255)),(0.45,(110,232,255)),(1,(30,120,210))],8,13,(80,220,255))
exc=styled('EXCAVATOR',112,[(0,(222,206,255)),(0.5,(160,128,255)),(1,(92,60,200))],30,9,(150,110,255),False)
emb=Image.open('icon_1024.png').resize((430,430),Image.LANCZOS)
img=Image.new('RGBA',(W,H),(0,0,0,0))
img.alpha_composite(emb,(0,(H-430)//2))
tx=450
top=(H-(crystal.size[1]-40+exc.size[1]))//2
img.alpha_composite(crystal,(tx,top))
cy=top+crystal.size[1]-40
img.alpha_composite(exc,(tx+18,cy))
# linha dourada entre título e subtítulo
d=ImageDraw.Draw(img)
img=img.crop(img.getbbox())
img.save(sys.argv[1])
print(img.size)
