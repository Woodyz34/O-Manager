import json, random, sys, numpy as np
from sim2 import *
L = json.load(open('legs.json'))
legs = [Leg(*l) for l in L]; cts = [clean_time(l)[0] for l in legs]
N = int(sys.argv[1]) if len(sys.argv)>1 else 10
H = float(sys.argv[2]) if len(sys.argv)>2 else None
if H: Runner.H0 = H
rng = random.Random(7)
print('clean mean', np.mean(cts))
for T in [0, 40, 60, 80, 90, 95, 99]:
    raw=[]; thr=[]; cnt=0
    for leg, ct in zip(legs, cts):
        for _ in range(N*(3 if T>=90 else 1)):
            x = Runner(leg, T, 80, rng).run() - ct
            raw.append(x); 
            if x > 15 and x > 0.25*ct: thr.append(x); cnt+=1
            else: thr.append(0)
    print(f"T{T:3d} target {0.3*(100-T):5.1f}s  thr {np.mean(thr):6.1f}s raw {np.mean(raw):6.1f}s  miss-rate {cnt/len(raw):.2f}  med-miss {np.median([t for t in thr if t>0]) if cnt else 0:.0f}s  max {max(raw):.0f}")
