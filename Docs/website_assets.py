"""Builds the optimized pictures and the hero video for website/.

Run from the repo root:  python Docs/website_assets.py <folder with source screenshots>
Every picture is written as AVIF + WebP in two widths (small for phones, large for desktops).
"""
import os, subprocess, sys
from PIL import Image

SRC = sys.argv[1] if len(sys.argv) > 1 else "/mnt/project-files"
OUT = "website/assets/img"
os.makedirs(OUT, exist_ok=True)

# name: (source, crop box as fractions (l, t, r, b) or None). Crops are cut to the shape the page shows
# (16:9 everywhere, 4:3 for the big TV picture). New game screenshots from 2026-11-14 are in hemsida/spelbilder.
B = "hemsida/spelbilder/"
PICS = {
    "menu":      (B + "huvudmeny.png", None),
    "runners":   (B + "mina-lopare-herr.png", None),
    "runnersw":  (B + "mina-lopare-dam.png", None),
    "shop":      (B + "paketbutik.png", None),
    "aicourse":  (B + "ai-bana.png", None),
    "maps":      (B + "valj-karta.png", None),
    "route":     (B + "rita-vagval.png", None),
    "race":      (B + "folj-loparen.png", None),
    "finished":  (B + "tavling-slut.png", None),
    "reward":    (B + "belooning.png", None),
    "rcforest":  (B + "vagvalsspel-skog.png", None),
    "rcsprint":  (B + "vagvalsspel-sprint.png", None),
    "lobby":     (B + "lobby.png", None),
    "builder":   (B + "banlaggare.png", None),
    "tvfinish":  (B + "tv-mal-strack1.png", (0.128, 0.0, 0.872, 1.0)),
    "tvcontrol": (B + "3d-kontroll-sten.png", (0.0526, 0.0, 0.9474, 1.0)),
    "tvrunner":  (B + "3d-lopare-kontroll.png", (0.2494, 0.0, 1.0, 1.0)),
    "race2":     (B + "folj-loparen-2.png", None),
    "race3":     (B + "folj-loparen-3.png", None),
    "tvforest":  (B + "3d-grantskog-ovan.png", (0.0574, 0.0, 0.9426, 1.0)),
    "tvhouse":   ("tv-kontroll/steg3/hus.jpg", None),
    "tvspruce":  ("tv-kontroll/steg3/tatt-grant.jpg", None),
}
WIDTHS = [(800, "s"), (1600, "l")]

for name, (src, crop) in PICS.items():
    im = Image.open(os.path.join(SRC, src)).convert("RGB")
    if crop:
        w, h = im.size
        im = im.crop((int(crop[0]*w), int(crop[1]*h), int(crop[2]*w), int(crop[3]*h)))
    for width, tag in WIDTHS:
        t = im if im.width <= width else im.resize((width, round(im.height*width/im.width)), Image.LANCZOS)
        t.save(f"{OUT}/{name}-{tag}.webp", quality=80, method=6)
        t.save(f"{OUT}/{name}-{tag}.avif", quality=58)
    print(name, im.size)

# Fictional runner portraits for the card stack (Blender placeholder people, not real athletes)
for name in ["Herr_07", "Dam_21", "Herr_19"]:
    p = Image.open(f"Assets/Resources/Basbilder/{name}.png")
    p = p.resize((360, 540), Image.LANCZOS)
    p.save(f"{OUT}/card-{name.lower()}.webp", quality=82, method=6)

ico = Image.open("Assets/Art/Icon/OManagerIcon.png").convert("RGBA")
for s in (32, 180, 192, 512):
    ico.resize((s, s), Image.LANCZOS).save(f"website/assets/icon-{s}.png", optimize=True)
ico.resize((128, 128), Image.LANCZOS).save(f"{OUT}/logo.webp", quality=90)

# Social share picture 1200x630
title = Image.open(os.path.join(SRC, "intro/titel-v2.jpg")).convert("RGB")
w, h = title.size
title.crop((0, int((h - w*630/1200)/2), w, int((h + w*630/1200)/2))).resize((1200, 630), Image.LANCZOS).save("website/assets/share.jpg", quality=82)

# Hero video: 2.8-4.4 s of the intro fly-through (skip text cropped away), slowed down and played
# forward then backward so the loop has no jump.
vid = os.path.join(SRC, "intro/intro-forhandsvisning-v2.mp4")
crop = "crop=1766:994:77:0,scale=1280:-2"
fc = f"[0:v]trim=2.8:4.4,setpts=(PTS-STARTPTS)/0.5,{crop},split[a][b];[b]reverse[r];[a][r]concat=n=2:v=1[v]"
os.makedirs("website/assets/video", exist_ok=True)
subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", vid, "-filter_complex", fc, "-map", "[v]", "-an",
                "-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "38", "-row-mt", "1", "website/assets/video/hero.webm"], check=True)
subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", vid, "-filter_complex", fc, "-map", "[v]", "-an",
                "-c:v", "libx264", "-crf", "27", "-preset", "slow", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
                "website/assets/video/hero.mp4"], check=True)
subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", "2.8", "-i", vid, "-frames:v", "1", "-vf", crop,
                "-q:v", "3", "website/assets/video/hero-poster.jpg"], check=True)
