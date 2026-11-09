# Version 3: circling search, areas (houses/olive/lakes) and hills as landmarks, contour reading,
# sight that grows in open ground and from high ground, smaller direction errors for good runners.
import math, random, numpy as np
from scipy import ndimage
from skimage.graph import MCP_Geometric
from sim import F, G, HH, CELL, MINX, MINY, ROWS, COLS, V100, cell, g_at, cpos, readability, handrail, dist_flag, Pu, similarity, signature, Leg
from scipy.spatial import cKDTree
_S, _Sl, _Sc = F['S'], F['Sl'], F['Sc']
OPEN = G > 0.05
COST = np.where(OPEN, 1.0/(V100*np.maximum(G, 0.05)), np.inf)
SMOOTH_G = ndimage.uniform_filter(G, 5)

# --- areas you cannot enter (houses, gardens, olive, lakes): their edges are clear landmarks
lab, nlab = ndimage.label(~OPEN)
sizes = ndimage.sum(~OPEN, lab, range(1, nlab+1))
edge = (~OPEN) & ndimage.binary_dilation(OPEN)
er, ec = np.nonzero(edge)
A = np.array([[MINX+c*CELL, MINY+r*CELL] for r, c in zip(er, ec)])
Aid = lab[er, ec]
keep = sizes[Aid-1] >= 3
A, Aid = A[keep], Aid[keep]
At = cKDTree(A)
def area_clarity(i): return 0.9
def area_distinct(i): return sizes[Aid[i]-1] < 2000      # a house or a plot has a shape; the map edge does not

# --- hills: tops with at least 10 m rise over the ground round them
mx = ndimage.maximum_filter(HH, size=21); lo = ndimage.percentile_filter(HH, 10, size=41)
top = (HH >= mx-0.01) & (HH-lo >= 10) & OPEN
tl, tn = ndimage.label(top)
cm = ndimage.center_of_mass(top, tl, range(1, tn+1))
prom = np.array(ndimage.maximum(HH-lo, tl, range(1, tn+1)))
Hp = np.array([[MINX+c*CELL, MINY+r*CELL] for r, c in cm]); Hc = np.minimum(0.95, prom/25.0); Ht = cKDTree(Hp)
Hh = np.array([HH[int(round(r)), int(round(c))] for r, c in cm])
Hsize = np.clip(prom*3, 30, 80)          # how far from the top you still feel "on the hill"

def h_at(p): return HH[cell(p)]
def relief(p):
    r, c = cell(p); w = HH[max(0,r-2):r+3, max(0,c-2):c+3]
    return min(1.0, float(w.std())/4.0)

def nearest_open(rc):
    if OPEN[rc]: return rc
    for rad in range(1, 30):
        r0, r1 = max(0, rc[0]-rad), min(ROWS, rc[0]+rad+1); c0, c1 = max(0, rc[1]-rad), min(COLS, rc[1]+rad+1)
        rr, cc = np.nonzero(OPEN[r0:r1, c0:c1])
        if len(rr):
            k = np.argmin((rr+r0-rc[0])**2 + (cc+c0-rc[1])**2); return (rr[k]+r0, cc[k]+c0)
    return rc

def tpath(a, b, margin=25):
    """terrain path a -> b over open ground only; never straight through closed cells"""
    ra = nearest_open(cell(a)); rb = nearest_open(cell(b))
    for mg in (margin, 80, 10000):
        r0 = max(0, min(ra[0], rb[0]) - mg); r1 = min(ROWS, max(ra[0], rb[0]) + mg + 1)
        c0 = max(0, min(ra[1], rb[1]) - mg); c1 = min(COLS, max(ra[1], rb[1]) + mg + 1)
        sub = COST[r0:r1, c0:c1]; m = MCP_Geometric(sub, fully_connected=True)
        cum, _ = m.find_costs([(rb[0]-r0, rb[1]-c0)])
        st = (ra[0]-r0, ra[1]-c0)
        if np.isfinite(cum[st]):
            tb = m.traceback(st)[::-1]
            pts = [np.array([MINX+(c+c0)*CELL, MINY+(r+r0)*CELL]) for r, c in tb]
            return [np.array(a, float)] + pts[1:] if len(pts) > 1 else [np.array(a, float), pts[0]]
    return [np.array(a, float)]

def plen(p): return sum(float(np.hypot(*(p[i]-p[i-1]))) for i in range(1, len(p)))

def sight(p, q, c):
    """how far away a thing of clarity c at q can be seen from p"""
    rc = cell(p); open_ = SMOOTH_G[rc] >= 1.15
    base = (6 + 18*c) * (2.5 if open_ else min(1.0, G[rc]))
    dh = h_at(p) - h_at(q)
    return base * float(np.clip(1 + dh/8.0, 0.6, 2.5))

class Runner:
    H0 = 1/2300.0
    def __init__(s, leg, tech, speed=80, rng=None):
        s.leg, s.s, s.P = leg, tech/100.0, speed
        s.rng = rng or random.Random()
        s.pos = leg.a.copy(); s.t = 0.0; s.track = [s.pos.copy()]; s.events = []
        s.seen = {}; s.travel = 0.0; s.since_fix = 0.0; s.U = 8.0; s.next_contour = 25.0
        s.nomiss = False; s.mode = 'route'; s.misses = 0
        s.set_aim(leg.b.copy(), plan=leg.path_from(s.pos))
    @property
    def bel(s): return s.lost_bel if s.mode == 'lost' else s.leg.b - (s.aim - s.pos)
    def set_aim(s, aim, plan=None):
        s.aim = np.array(aim, float)
        s.plan = plan if plan is not None else tpath(s.pos, s.aim); s.pi = 1
        s.expect = plen(s.plan); s.run_since_aim = 0.0
    def ev(s, kind, extra=None):
        s.events.append((kind, s.pos.copy(), s.t, extra))
        if kind in ('parallell', 'ingen_skarm', 'inser', 'for_langt'):
            s.s = 1 - (1 - s.s) * 0.4
    def vis(s): return 10 + 15*min(1.0, g_at(s.pos)) * (1.6 if SMOOTH_G[cell(s.pos)] >= 1.15 else 1)
    def step(s, d):
        n = s.pos + d; g = g_at(n)
        if g <= 0.05: g = max(g_at(s.pos), 0.3)   # only grid corners: the plan never goes through closed ground
        ln = float(np.hypot(*d)); s.t += ln/(V100*g*s.P/100); s.pos = n
        s.travel += ln; s.since_fix += ln; s.run_since_aim += ln; s.track.append(n.copy()); return True
    def fix(s, why='fix'):
        s.ev(why); s.U = 8; s.since_fix = 0; s.mode = 'route'; s.set_aim(s.leg.b.copy())
    # ---------------- observing
    def noticed(s, c, boost):
        return s.rng.random() < min(1, c*(0.55+0.45*s.s)*boost)
    def observe(s, boost=1.0):
        pos = s.pos
        for j in F['Pt'].query_ball_point(pos, 70):
            c = F['Pc'][j]
            if np.hypot(*(F['P'][j]-pos)) > sight(pos, F['P'][j], c): continue
            key = ('p', j)
            if s.seen.get(key, -1e9) > s.travel - 200: continue
            s.seen[key] = s.travel
            if s.noticed(c, boost):
                grp = F['Pg'][j]
                ks = [k for k in F['Pt'].query_ball_point(s.bel, s.U+25) if F['Pg'][k] == grp]
                if s.resolve([F['P'][k] for k in ks], F['P'][j], c, ids=ks, true_id=j): return
        for j in F['St'].query_ball_point(pos, 4):
            l = _Sl[j]; key = ('l', l)
            if s.seen.get(key, -1e9) > s.travel - 120: continue
            s.seen[key] = s.travel; c = _Sc[j]
            if s.noticed(c, boost*1.1):
                grp = F['Sg'][j]; distinct = (dist_flag[j] or len(At.query_ball_point(_S[j], 25)) > 0) and s.rng.random() < 0.4 + 0.5*s.s
                best = {}
                for k in F['St'].query_ball_point(s.bel, s.U+25):
                    if F['Sg'][k] != grp or (distinct and not dist_flag[k] and not At.query_ball_point(_S[k], 25)): continue
                    d = np.sum((_S[k]-s.bel)**2); lk = _Sl[k]
                    if lk not in best or d < best[lk][0]: best[lk] = (d, _S[k])
                if s.resolve([v[1] for v in best.values()], _S[j], c, ids=list(best.keys()), true_id=l): return
        # houses, gardens, olive, lakes
        for j in At.query_ball_point(pos, 60)[::2]:
            a = Aid[j]; key = ('a', a)
            if np.hypot(*(A[j]-pos)) > sight(pos, A[j], 0.9): continue
            if s.seen.get(key, -1e9) > s.travel - 150: continue
            s.seen[key] = s.travel
            if s.noticed(0.9, boost*1.2):
                best = {}
                for k in At.query_ball_point(s.bel, s.U+40):
                    if not area_distinct(j) == area_distinct(k): continue
                    d = np.sum((A[k]-s.bel)**2); ak = Aid[k]
                    if ak not in best or d < best[ak][0]: best[ak] = (d, A[k])
                # an area is big: matching it to the right one puts you within ~20 m
                if s.resolve([v[1] for v in best.values()], A[j], 0.9, tol=25, ids=list(best.keys()), true_id=a): return
        # big hills: you feel the top
        for j in Ht.query_ball_point(pos, 90):
            if np.hypot(*(Hp[j]-pos)) > Hsize[j] or h_at(pos) < Hh[j] - 6: continue
            key = ('h', j)
            if s.seen.get(key, -1e9) > s.travel - 300: continue
            s.seen[key] = s.travel
            if s.noticed(Hc[j], boost*1.3):
                ks = Ht.query_ball_point(s.bel, s.U+80)
                if s.resolve([Hp[k] for k in ks], Hp[j], Hc[j], tol=Hsize[j]+10, hill=True, ids=ks, true_id=j): return
        # reading the contours as you go: works in hilly ground, for good runners
        if s.travel >= s.next_contour:
            s.next_contour = s.travel + 25
            if s.mode != 'route' and s.rng.random() < 0.08*s.s*relief(pos):
                s.fix('hojdkurvor')
    def resolve(s, cands, true_p, clarity, tol=10, hill=False, ids=None, true_id=None):
        if s.mode == 'route': return False
        sk = s.s; frac = s.run_since_aim / max(s.expect, 1)
        if not cands:
            if s.mode != 'lost' and clarity >= 0.7 and frac > 0.5 and s.rng.random() < 0.5 + 0.5*sk:
                s.ev('inser'); s.go_lost(); return True
            s.U = min(80.0, s.U + 10); return False
        U = max(s.U, 10)
        w = np.array([math.exp(-np.sum((c-s.bel)**2)/(2*U*U)) for c in cands]) + 1e-12
        isright = [ids[i] == true_id for i in range(len(cands))]
        for i in range(len(cands)):
            if isright[i]: w[i] *= 1 + 8*sk
        w /= w.sum()
        k = s.rng.choices(range(len(cands)), w)[0]; ch = cands[k]
        if w.max() < 0.75 and s.rng.random() < sk*(1-w.max())*1.5:
            s.U = min(80.0, s.U + 5); return False
        right = isright[k]
        if right and np.hypot(*(true_p - s.pos)) < tol and np.hypot(*(ch - true_p)) < tol and not hill:
            s.fix(); return True
        if right:
            # the right hill / area, but not exactly where on it: aim from its middle
            s.ev('lasin_ungefar', ch.copy()); s.U = 20; s.since_fix = 0; s.mode = 'drift'
            s.set_aim(s.pos + (s.leg.b - ch)); return True
        s.ev('parallell', ch.copy()); s.U = 15; s.since_fix = 0; s.mode = 'drift'
        s.set_aim(s.pos + (s.leg.b - (ch + (s.pos - true_p)))); return True
    def go_lost(s):
        if s.mode != 'lost': s.lost_bel = s.bel.copy()
        s.mode = 'lost'; s.U = max(s.U, 50); s.t += 3 + 10*(1-s.s)
        s.lost_targets = set(); s.lplan = None; s.lost_m = 0.0
        s.centre = s.pos.copy(); s.turn = s.rng.choice((-1, 1)); s.visited = [s.pos.copy()]
        if not hasattr(s, 'ctrl_sig'): s.ctrl_sig = signature(s.leg.b, 30); s.ctrl_g = G[cell(s.leg.b)]
    # ---------------- running
    def run(s, tmax=1500):
        leg = s.leg
        while s.t < tmax:
            if np.hypot(*(s.pos - leg.b)) < s.vis():
                if s.mode != 'route': s.ev('ser_skarm')
                d = leg.b - s.pos; n = np.hypot(*d)
                while n > 2:
                    s.step(d/n*2); d = leg.b - s.pos; n = np.hypot(*d)
                s.track.append(leg.b.copy()); return s.t
            if s.mode == 'lost': s.lost_step(); continue
            if s.pi >= len(s.plan): s.at_phantom(); continue
            tgt = s.plan[s.pi]; d = tgt - s.pos; n = np.hypot(*d)
            if n < 2.5: s.pi += 1; continue
            s.step(d/n*min(2.0, n))
            if s.mode == 'drift':
                s.U = min(60.0, 12 + 0.18*s.since_fix)
                if s.run_since_aim > (1.15 + 0.35*(1-s.s))*s.expect + 20:
                    s.ev('for_langt'); s.go_lost(); continue
            if s.mode == 'route' and not s.nomiss and not handrail(s.pos):
                left = np.hypot(*(leg.b - s.pos))
                diff = 0.6 + max(0.0, 1.2 - readability(s.pos)/3.0)
                near = 2.5 if left < 70 else 1.0
                if s.rng.random() < 2.0*(s.H0*(1-s.s)**0.15*diff*near + 1/250000):
                    bad = 1 - s.s
                    a = math.radians(min(2 + 18*bad + abs(s.rng.gauss(0,1))*(3 + 8*bad), 4 + 36*bad)) * s.rng.choice((-1,1))
                    k = 1 + s.rng.gauss(0, 0.03 + 0.09*bad)
                    s.ev('riktningsfel', math.degrees(a)); s.mode = 'drift'; s.since_fix = 0
                    c, sn = math.cos(a), math.sin(a); v = leg.b - s.pos
                    s.set_aim(s.pos + np.array([v[0]*c - v[1]*sn, v[0]*sn + v[1]*c]) * k)
            if s.mode != 'route': s.observe()
        return s.t
    def at_phantom(s):
        leg = s.leg; sim = similarity(signature(s.pos), signature(leg.b))
        s.ev('ingen_skarm', sim); s.t += 2 + 5*(1-s.s); s.misses += 1
        s.lost_bel = s.bel.copy()
        need = 1 + 3*(1-s.s)
        if sim > 0.3:
            # look round here: a loose loop of 4-5 stops 25-45 m out, on ground like the control's
            centre = s.pos.copy(); doubt = 0.0; ang = s.rng.random()*6.28; turn = s.rng.choice((-1, 1))
            gc = G[cell(leg.b)]
            for _ in range(s.rng.randint(4, 5)):
                best, bs = None, -1
                for _try in range(6):
                    a = ang + turn*s.rng.uniform(0.9, 1.6); r = s.rng.uniform(25, 45)
                    q = centre + r*np.array([math.cos(a), math.sin(a)])
                    if not OPEN[cell(q)]: continue
                    sc = 1.0/(1.0 + abs(G[cell(q)] - gc)*3) + s.rng.random()*0.3
                    if sc > bs: bs, best, ba = sc, q, a
                if best is None: break
                ang = ba
                for p in tpath(s.pos, best, 8)[1:]:
                    d = p - s.pos; n = np.hypot(*d)
                    while n > 2:
                        s.step(d/n*2); d = p - s.pos; n = np.hypot(*d)
                        if np.hypot(*(s.pos - leg.b)) < s.vis(): return
                    for jj in F['Pt'].query_ball_point(s.pos, 15):
                        key = ('c', jj)
                        if key in s.seen or F['Pc'][jj] < 0.3: continue
                        s.seen[key] = 1
                        if s.rng.random() < F['Pc'][jj]*(0.5+0.5*s.s): doubt += (1-sim)+0.3
                if doubt >= need: break
        s.go_lost()
    def lost_step(s):
        if s.lplan is None:
            if s.lost_m > 300 + 200*s.s: s.fix(); return
            safe = s.misses >= 2
            best, bs = None, 0
            # go round the area: prefer things ahead along a loop round where you got lost, and new ground
            r = s.pos - s.centre; rn = np.hypot(*r)
            tang = np.array([-r[1], r[0]])*s.turn/rn if rn > 5 else None
            def score(q, base):
                d = np.hypot(*(q-s.pos))
                if d < 40: return 0
                for v in s.visited:
                    if np.hypot(*(q-v)) < 70: return 0
                f = 1.0
                if tang is not None:
                    u = (q-s.pos)/d; f = 0.4 + 0.8*max(0.0, float(u @ tang)) + 0.3*max(0.0, float(u @ (r/rn)))*(rn < 80)
                # the control is in green / open / rocky ground: look in ground like that
                like = 1.0/(1.0 + abs(G[cell(q)] - s.ctrl_g)*3) + 0.5*similarity(signature(q, 30), s.ctrl_sig)
                return base*f*like/(d+30)
            if safe:
                for j in F['St'].query_ball_point(s.pos, 400)[::2]:
                    if _Sc[j] < 0.6: continue
                    sc = score(_S[j], _Sc[j]*(1.5 if dist_flag[j] else 1))
                    if sc > bs: bs, best = sc, (('safe', j), _S[j])
                if best is not None:
                    s.ev('sakert'); s.lplan = tpath(s.pos, best[1], 15); s.lpi = 1; s.safe_target = True; return
            for j in F['Pt'].query_ball_point(s.pos, 150):
                if ('p', j) in s.lost_targets: continue
                sc = score(F['P'][j], F['Pc'][j]*Pu[j])
                if sc > bs: bs, best = sc, (('p', j), F['P'][j])
            for j in F['St'].query_ball_point(s.pos, 150)[::3]:
                if ('l', _Sl[j]) in s.lost_targets or _Sc[j] < 0.5: continue
                sc = score(_S[j], _Sc[j]*(1.3 if dist_flag[j] else 0.7))
                if sc > bs: bs, best = sc, (('l', _Sl[j]), _S[j])
            for j in At.query_ball_point(s.pos, 200)[::3]:
                if ('a', Aid[j]) in s.lost_targets: continue
                sc = score(A[j], 1.2)
                if sc > bs: bs, best = sc, (('a', Aid[j]), A[j])
            if best is None: s.fix(); return
            s.lost_targets.add(best[0]); s.seen.pop(best[0], None)
            s.lplan = tpath(s.pos, best[1], 10); s.lpi = 1
            if len(s.lplan) < 2: s.lplan = None; return
        if s.lpi >= len(s.lplan):
            if getattr(s, 'safe_target', False):
                s.safe_target = False; s.misses = 0; s.fix(); return
            s.lplan = None; s.t += 2 + 4*(1-s.s)
            s.seen = {k: v for k, v in s.seen.items() if k[0] == 'c'}
            s.observe(1.5); return
        tgt = s.lplan[s.lpi]; d = tgt - s.pos; n = np.hypot(*d)
        if n < 2.5: s.lpi += 1; return
        s.step(d/n*min(2.0, n)); s.lost_m += min(2.0, n); s.U = min(80.0, s.U + 0.15)
        if np.hypot(*(s.pos - s.visited[-1])) > 25: s.visited.append(s.pos.copy())
        s.observe(1.5)

def clean_time(leg, speed=80):
    r = Runner(leg, 100, speed, random.Random(1)); r.nomiss = True; r.observe = lambda boost=1.0: None
    t = r.run(); return t, r.track
