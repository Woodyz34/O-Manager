import json, random, sys, math, numpy as np
from PIL import Image, ImageDraw, ImageFont
from sim import *
from sim5 import Runner, clean_time
Image.MAX_IMAGE_PIXELS = None
PIC = Image.open(PK+'picture.png') if False else None
from load import PK
pic = Image.open(PK + 'picture.png'); PH = pic.height; PPM = 1.6
def px(p): return ((p[0]-(MINX-5))*PPM, PH-(p[1]-(MINY-5))*PPM)
FB = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'; FR = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
f1 = ImageFont.truetype(FB, 30); f2 = ImageFont.truetype(FR, 22); f3 = ImageFont.truetype(FB, 22)
COL = [(220,30,40), (30,90,220), (240,140,0)]
LEVELS = [0, 60, 80, 90, 99]
L = json.load(open('legs.json'))
def pick(leg, T, ct, rng, n=100):
    runs = []
    for _ in range(n):
        r = Runner(leg, T, 80, rng); x = r.run() - ct; runs.append((x, r))
    runs.sort(key=lambda z: z[0])
    mist = [z for z in runs if z[0] > 15]
    sel = [runs[len(runs)//2]]
    if mist: sel.append(mist[len(mist)//2])
    if len(mist) > 1: sel.append(mist[-1])
    return sel, sum(z[0] for z in runs)/n, len(mist)/n
def render(li, seed, out):
    leg = Leg(*L[li]); ct, ctrack = clean_time(leg); rng = random.Random(seed)
    panels = [(T,)+pick(leg, T, ct, rng) for T in LEVELS]
    allp = np.array([p for _,sel,_,_ in panels for _,r in sel for p in r.track] + list(ctrack))
    lo = allp.min(0) - 60; hi = allp.max(0) + 60
    # keep a sane window: at most 1100 m round the leg
    c = (leg.a+leg.b)/2; lo = np.maximum(lo, c-650); hi = np.minimum(hi, c+650)
    a = px((lo[0], hi[1])); b = px((hi[0], lo[1]))
    base = pic.crop((int(a[0]), int(a[1]), int(b[0]), int(b[1]))).convert('RGB')
    W = 820; sc = W / base.width; Hh = int(base.height*sc)
    base = base.resize((W, Hh), Image.LANCZOS)
    def T_(p): q = px(p); return ((q[0]-int(a[0]))*sc, (q[1]-int(a[1]))*sc)
    tiles = []
    for T, sel, mean, rate in panels:
        im = base.copy(); d = ImageDraw.Draw(im, 'RGBA')
        m = 1.0*PPM*sc  # px per metre
        for k, (x, r) in reversed(list(enumerate(sel))):
            col = COL[k]
            d.line([T_(p) for p in r.track[::2]] + [T_(r.track[-1])], fill=col+(235,), width=5, joint='curve')
            for kind, pos, t, extra in r.events:
                q = T_(pos)
                if kind == 'riktningsfel': d.line([(q[0]-9,q[1]-9),(q[0]+9,q[1]+9)], fill=(0,0,0,255), width=4); d.line([(q[0]-9,q[1]+9),(q[0]+9,q[1]-9)], fill=(0,0,0,255), width=4)
                elif kind == 'parallell':
                    e = T_(extra); n_ = max(1, int(math.dist(q, e)/8))
                    for i_ in range(0, n_, 2):
                        d.line([(q[0]+(e[0]-q[0])*i_/n_, q[1]+(e[1]-q[1])*i_/n_), (q[0]+(e[0]-q[0])*(i_+1)/n_, q[1]+(e[1]-q[1])*(i_+1)/n_)], fill=(90,90,90,200), width=2)
                    d.ellipse((e[0]-8,e[1]-8,e[0]+8,e[1]+8), outline=col+(255,), width=3)
                    d.rectangle((q[0]-6,q[1]-6,q[0]+6,q[1]+6), fill=(255,255,255,255), outline=col+(255,), width=3)
                elif kind == 'ingen_skarm': d.ellipse((q[0]-10,q[1]-10,q[0]+10,q[1]+10), fill=(255,255,255,200), outline=(0,0,0,255), width=3); d.text((q[0]-4,q[1]-13),'?',fill=(0,0,0),font=f3)
                elif kind == "sakert": d.rectangle((q[0]-8,q[1]-8,q[0]+8,q[1]+8), fill=(0,0,0,255))
                elif kind in ("fix", "hojdkurvor", "lasin_ungefar"): d.ellipse((q[0]-7,q[1]-7,q[0]+7,q[1]+7), fill=(20,160,60,255), outline=(255,255,255,255), width=2)
        R = 30*m; s0 = T_(leg.a); e0 = T_(leg.b)
        d.polygon([(s0[0], s0[1]-R*1.1), (s0[0]-R*0.95, s0[1]+R*0.55), (s0[0]+R*0.95, s0[1]+R*0.55)], outline=(190,30,160,255), width=4)
        d.ellipse((e0[0]-R, e0[1]-R, e0[0]+R, e0[1]+R), outline=(190,30,160,255), width=4)
        d.rectangle((0,0,W,44), fill=(255,255,255,230)); d.text((12,6), f"Teknik {T}", fill=(20,20,20), font=f1)
        d.text((230,12), f"snitt {mean:.0f} s bom, {round(rate*100)} % bommar", fill=(40,40,40), font=f2)
        d.rectangle((0,Hh-38,W,Hh), fill=(255,255,255,230)); xo = 12
        for k,(x,r) in enumerate(sel):
            d.rectangle((xo, Hh-28, xo+22, Hh-12), fill=COL[k]); d.text((xo+28, Hh-33), f"{max(0,x):.0f} s", fill=(20,20,20), font=f2); xo += 130
        tiles.append(im)
    gap = 10; cols = 3
    out_im = Image.new('RGB', (cols*W + (cols-1)*gap, 2*Hh + gap + 50), (255,255,255))
    for i, t in enumerate(tiles): out_im.paste(t, ((i%cols)*(W+gap), 50 + (i//cols)*(Hh+gap)))
    d = ImageDraw.Draw(out_im)
    d.text((12, 10), f"Illern 2025 – sträcka {li+1}: {round(np.hypot(*(leg.b-leg.a))/10)*10} m fågelväg, felfri tid {ct:.0f} s (fart 80). 100 körningar per nivå. Tre visas: mittenkörningen, en typisk bom och den värsta bommen.", fill=(20,20,20), font=f2)
    x0, y0 = 2*(W+gap) + 30, 50 + Hh + gap + 30
    lg = [("röd", "mittenkörningen (oftast felfri)"), ("blå", "en typisk bom"), ("orange", "värsta av 100"), ("X", "riktningsfel börjar"), ("□ ··· ○", "ser något vid □, tror att det är ○"),
          ("", "(parallellfel)"), ("?", "ingen skärm där man trodde"), ("grön prick", "vet var man är igen")]
    d.text((x0, y0), "Teckenförklaring", fill=(20,20,20), font=f1)
    for k,(a_,b_) in enumerate(lg):
        d.text((x0, y0+50+k*44), a_, fill=(20,20,20), font=f3); d.text((x0+200, y0+50+k*44), b_ if len(b_) < 40 else b_[:38]+'\n'+b_[38:], fill=(40,40,40), font=f2)
    out_im.save(out, quality=88)
if __name__ == '__main__':
    for li in map(int, sys.argv[1].split(',')): render(li, 100+li, f'v6-bom-stracka-{li+1}.jpg')
