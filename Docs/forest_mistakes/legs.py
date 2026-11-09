import random, json, numpy as np, math
from sim import *
def make_legs(n=40, seed=3):
    rng = random.Random(seed)
    ok = []
    for i,(p,gname) in enumerate(zip(F['P'], F['Pg'])):
        if gname not in POINT_CTRL: continue
        g = g_at(p)
        if not (0.55 <= g <= 1.15): continue
        if any(F['Sc'][j] >= 0.5 for j in F['St'].query_ball_point(p, 25)): continue
        # stay off the town in the north-west and the map edge
        if p[0] < 2600 or p[1] > 6300 or p[1] < 1500 or p[0] > 7000: continue
        ok.append(p)
    ok = np.array(ok); tree = cKDTree(ok); legs = []
    cur = ok[rng.randrange(len(ok))]
    while len(legs) < n:
        idx = [j for j in tree.query_ball_point(cur, 600) if np.hypot(*(ok[j]-cur)) > 250]
        if not idx: cur = ok[rng.randrange(len(ok))]; continue
        nxt = ok[rng.choice(idx)]
        legs.append((cur.tolist(), nxt.tolist())); cur = nxt
    return legs
from scipy.spatial import cKDTree
if __name__ == '__main__':
    L = make_legs(); json.dump(L, open('legs.json','w')); print(len(L))
