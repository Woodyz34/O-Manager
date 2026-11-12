# Draws the runs written by "harness.exe ... tracks" on the map picture.
#   python3 render.py <package folder> <tracks.json> <out prefix>
import json, sys, math
from PIL import Image, ImageDraw, ImageFont
Image.MAX_IMAGE_PIXELS = None
pk, tracks, out = sys.argv[1], sys.argv[2], sys.argv[3]
info = json.load(open(pk + '/map.json'))
pic = Image.open(pk + '/picture.png'); PH = pic.height; PPM = info['pixelsPerMeter']
half = info['cellSize'] / 2
def px(p): return ((p[0] - (info['minx'] - half)) * PPM, PH - (p[1] - (info['miny'] - half)) * PPM)
FB = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'; FR = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
f1 = ImageFont.truetype(FB, 28); f2 = ImageFont.truetype(FR, 19); f3 = ImageFont.truetype(FB, 20)
runs = json.load(open(tracks))
legs = sorted(set(r['leg'] for r in runs))
MARK = [('riktningsfel', 'x'), ('parallellfel', 'p'), ('ingen skärm', '?'), ('liten miss', 'm'), ('vet var', 'f'), ('vet vad', 'f'),
        ('höjdkurvorna', 'f'), ('ser skärmen', 's'), ('springer till', 'b'), ('fel sida', 'v'), ('stämmer inte', '?')]
for leg in legs:
    rs = [r for r in runs if r['leg'] == leg]
    pts = [p for r in rs for p in r['track']] + [rs[0]['from'], rs[0]['to']]
    lo = [min(p[0] for p in pts) - 70, min(p[1] for p in pts) - 70]; hi = [max(p[0] for p in pts) + 70, max(p[1] for p in pts) + 70]
    a = px((lo[0], hi[1])); b = px((hi[0], lo[1]))
    base = pic.crop((int(a[0]), int(a[1]), int(b[0]), int(b[1]))).convert('RGB')
    W = 760; sc = W / base.width; H = int(base.height * sc)
    base = base.resize((W, H), Image.LANCZOS)
    def T(p):
        q = px(p); return ((q[0] - int(a[0])) * sc, (q[1] - int(a[1])) * sc)
    tiles = []
    for r in rs:
        im = base.copy(); d = ImageDraw.Draw(im, 'RGBA')
        d.line([T(p) for p in r['track']], fill=(220, 30, 40, 235), width=5, joint='curve')
        m = PPM * sc; R = 30 * m; s0 = T(r['from']); e0 = T(r['to'])
        d.polygon([(s0[0], s0[1] - R * 1.1), (s0[0] - R * 0.95, s0[1] + R * 0.55), (s0[0] + R * 0.95, s0[1] + R * 0.55)], outline=(190, 30, 160, 255), width=4)
        d.ellipse((e0[0] - R, e0[1] - R, e0[0] + R, e0[1] + R), outline=(190, 30, 160, 255), width=4)
        notes = []
        for e in r['events']:
            xy, t, text = e.split(' ', 2)
            x, y = [float(v) for v in xy.split(',')]
            if x == 0: continue
            q = T((x, y)); what = text.split(': ', 1)[1].rsplit(' (teknik', 1)[0]
            kind = next((k for key, k in MARK if key in what), None)
            if kind == 'x':
                d.line([(q[0] - 9, q[1] - 9), (q[0] + 9, q[1] + 9)], fill=(0, 0, 0, 255), width=4); d.line([(q[0] - 9, q[1] + 9), (q[0] + 9, q[1] - 9)], fill=(0, 0, 0, 255), width=4)
            elif kind == 'p': d.rectangle((q[0] - 7, q[1] - 7, q[0] + 7, q[1] + 7), fill=(255, 255, 255, 255), outline=(30, 90, 220, 255), width=3)
            elif kind == '?':
                d.ellipse((q[0] - 10, q[1] - 10, q[0] + 10, q[1] + 10), fill=(255, 255, 255, 220), outline=(0, 0, 0, 255), width=3); d.text((q[0] - 5, q[1] - 12), '?', fill=(0, 0, 0), font=f3)
            elif kind == 'f': d.ellipse((q[0] - 8, q[1] - 8, q[0] + 8, q[1] + 8), fill=(20, 160, 60, 255), outline=(255, 255, 255, 255), width=2)
            elif kind == 'm': d.polygon([(q[0], q[1] - 9), (q[0] - 9, q[1] + 8), (q[0] + 9, q[1] + 8)], fill=(240, 140, 0, 255), outline=(0, 0, 0, 255))
            elif kind == 'b': d.rectangle((q[0] - 7, q[1] - 7, q[0] + 7, q[1] + 7), fill=(0, 0, 0, 255))
            notes.append(f"{t}: {what}")
        d.rectangle((0, 0, W, 42), fill=(255, 255, 255, 235))
        d.text((12, 5), f"Teknik {r['technique']}", fill=(20, 20, 20), font=f1)
        d.text((210, 11), f"bom {r['loss']:.0f} s (bästa tid {r['best']:.0f} s)", fill=(40, 40, 40), font=f2)
        notes = notes[:9]
        if notes:
            h = 8 + 23 * len(notes)
            d.rectangle((0, H - h, W, H), fill=(255, 255, 255, 215))
            for i, n in enumerate(notes): d.text((10, H - h + 4 + 23 * i), n, fill=(20, 20, 20), font=f2)
        tiles.append(im)
    sheet = Image.new('RGB', (W * len(tiles) + 8 * (len(tiles) - 1), H + 40), (255, 255, 255))
    for i, t in enumerate(tiles): sheet.paste(t, (i * (W + 8), 0))
    d = ImageDraw.Draw(sheet)
    d.text((10, H + 9), "X riktningsfel   □ parallellfel   ? ingen skärm / stämmer inte   ▲ liten miss vid kontrollen   ● läser in sig   ■ springer till något säkert", fill=(30, 30, 30), font=f2)
    sheet.save(f"{out}{leg + 1}.jpg", quality=86)
    print(f"{out}{leg + 1}.jpg", sheet.size)
