"""Makes website/assets/contours.svg: a seamless tile of brown contour lines (like an O-map)."""
import numpy as np, matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
rng = np.random.default_rng(7)
N, S = 260, 800
x = np.linspace(0, 1, N); X, Y = np.meshgrid(x, x)
Z = np.zeros_like(X)
for _ in range(14):
    kx, ky = rng.integers(-3, 4, 2)
    if kx == 0 and ky == 0: continue
    Z += rng.uniform(.3, 1) / np.hypot(kx, ky) * np.cos(2*np.pi*(kx*X + ky*Y) + rng.uniform(0, 6.28))
cs = plt.contour(X, Y, Z, levels=np.linspace(Z.min(), Z.max(), 16)[1:-1])
paths = []
for i, segs in enumerate(cs.allsegs):
    for s in segs:
        if len(s) < 8: continue
        s = s[::2] * S
        d = "M" + " ".join(f"{a:.0f} {b:.0f}" for a, b in s)
        paths.append(f'<path d="{d}"{" class=\"i\"" if i % 5 == 2 else ""}/>')
svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {S} {S}" width="{S}" height="{S}" fill="none" '
       f'stroke="#b8752f" stroke-width="1.3" stroke-linejoin="round"><style>.i{{stroke-width:2.6}}</style>{"".join(paths)}</svg>')
open("website/assets/contours.svg", "w").write(svg)
print(len(svg))
