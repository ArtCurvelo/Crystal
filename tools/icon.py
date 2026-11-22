import math, sys
from PIL import Image, ImageDraw, ImageFilter
S=2048; c=S/2
def rounded_mask(r):
    m=Image.new('L',(S,S),0); ImageDraw.Draw(m).rounded_rectangle([S*0.04,S*0.04,S*0.96,S*0.96],radius=r,fill=255); return m
mask=rounded_mask(S*0.2)
# fundo: gradiente radial roxo profundo -> quase preto
bg=Image.new('RGB',(S,S)); px=bg.load()
for y in range(0,S,4):
    for x in range(0,S,4):
        t=min(1,math.hypot(x-c,y-S*0.45)/(S*0.7))
        col=(int(70*(1-t)+10*t),int(36*(1-t)+6*t),int(120*(1-t)+24*t))
        for yy in range(4):
            for xx in range(4): px[x+xx,y+yy]=col
img=Image.new('RGBA',(S,S),(0,0,0,0)); img.paste(bg,(0,0),mask)
layer=Image.new('RGBA',(S,S),(0,0,0,0)); d=ImageDraw.Draw(layer)
# brilho grande atrás
glow=Image.new('RGBA',(S,S),(0,0,0,0)); ImageDraw.Draw(glow).ellipse([c-S*0.36,S*0.18,c+S*0.36,S*0.9],fill=(80,225,255,190))
glow=glow.filter(ImageFilter.GaussianBlur(S*0.09)); layer=Image.alpha_composite(layer,glow); d=ImageDraw.Draw(layer)
# picareta grande na diagonal, atrás dos cristais
def thick(p0,p1,w,col):
    dx,dy=p1[0]-p0[0],p1[1]-p0[1]; L=math.hypot(dx,dy); nx,ny=-dy/L*w/2,dx/L*w/2
    d.polygon([(p0[0]+nx,p0[1]+ny),(p1[0]+nx,p1[1]+ny),(p1[0]-nx,p1[1]-ny),(p0[0]-nx,p0[1]-ny)],fill=col)
thick((S*0.8,S*0.86),(S*0.34,S*0.3),S*0.065,(110,66,34)); thick((S*0.8,S*0.86),(S*0.34,S*0.3),S*0.028,(170,110,62))
# cabeça: arco grosso perpendicular ao cabo, pontas afiadas
ang=math.atan2(0.3-0.86,0.34-0.8); hx,hy=S*0.355,S*0.315
perp=ang+math.pi/2
outer=[];inner=[]
for i in range(31):
    t=-1+2*i/30
    bend=(1-t*t)*S*0.07
    w=S*0.07*(1-abs(t)**1.6)+S*0.006
    px_=hx+math.cos(perp)*t*S*0.25 - math.cos(ang)*bend
    py_=hy+math.sin(perp)*t*S*0.25 - math.sin(ang)*bend
    outer.append((px_-math.cos(ang)*w*0.5,py_-math.sin(ang)*w*0.5)); inner.append((px_+math.cos(ang)*w*0.5,py_+math.sin(ang)*w*0.5))
d.polygon(outer+inner[::-1],fill=(255,190,70)); d.line(outer,fill=(255,236,170),width=int(S*0.012))
# cristais
def crystal(bx,by,w,h,lean,light,mid,dark):
    tip=(bx+lean,by-h); l=(bx-w/2,by-h*0.25); r=(bx+w/2,by-h*0.25); m=(bx+lean*0.15,by-h*0.32)
    d.polygon([l,tip,m,(bx,by)],fill=mid); d.polygon([m,tip,r,(bx,by)],fill=dark)
    d.polygon([(bx-w/2*0.9,by),l,(bx,by)],fill=mid); d.polygon([(bx,by),r,(bx+w/2*0.9,by)],fill=dark)
    d.polygon([l,tip,(bx-w*0.1+lean*0.5,by-h*0.55)],fill=light)
    d.line([l,tip,r],fill=(240,253,255),width=int(S*0.009))
V=((206,180,255),(146,108,255),(84,56,196)); C=((222,253,255),(96,232,255),(28,146,214))
crystal(c-S*0.24,S*0.84,S*0.2,S*0.42,-S*0.06,*V)
crystal(c+S*0.24,S*0.84,S*0.19,S*0.38,S*0.07,*V)
crystal(c,S*0.86,S*0.28,S*0.68,S*0.01,*C)
crystal(c-S*0.1,S*0.88,S*0.13,S*0.26,-S*0.05,*C)
crystal(c+S*0.11,S*0.88,S*0.11,S*0.2,S*0.04,*C)
# chão escuro
d.rectangle([0,S*0.86,S,S],fill=(16,10,30,255))
for (x,y,r) in [(0.22,0.33,0.014),(0.76,0.3,0.011),(0.7,0.55,0.008)]:
    d.ellipse([S*x-S*r,S*y-S*r,S*x+S*r,S*y+S*r],fill=(235,252,255))
img=Image.alpha_composite(img,Image.composite(layer,Image.new('RGBA',(S,S),(0,0,0,0)),mask))
d=ImageDraw.Draw(img)
d.rounded_rectangle([S*0.04,S*0.04,S*0.96,S*0.96],radius=S*0.2,outline=(94,231,255),width=int(S*0.028))
out=sys.argv[1]
img.resize((512,512),Image.LANCZOS).save(out+'/icon_512.png')
img.resize((1024,1024),Image.LANCZOS).save(out+'/icon_1024.png')
img.resize((512,512),Image.LANCZOS).save(out+'/icon.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print('ok')
