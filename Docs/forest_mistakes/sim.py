import math, random, numpy as np
from skimage.graph import MCP_Geometric
from load import grid
from feats import load, HANDRAIL
G,HH,MINX,MINY,CELL = grid(); ROWS,COLS = G.shape
F = load()
V100 = 3.6   # m/s on speed-1 ground for a speed-100 runner
POINT_CTRL = {'sten','storsten','knoll','grop','rose','stengrupp'}
# --- precompute: distinct line samples (junction / point feature near) and point uniqueness
_S, _Sl, _Sc = F['S'], F['Sl'], F['Sc']
dist_flag = np.zeros(len(_S), bool)
for i, nb in enumerate(F['St'].query_ball_point(_S, 12)):
    if any(_Sl[j] != _Sl[i] and _Sc[j] >= 0.35 for j in nb): dist_flag[i] = True
for i, nb in enumerate(F['Pt'].query_ball_point(_S, 12)):
    if any(F['Pc'][j] >= 0.35 for j in nb): dist_flag[i] = True
Pu = np.array([1.0/len([j for j in nb if F['Pg'][j]==F['Pg'][i]]) for i, nb in enumerate(F['Pt'].query_ball_point(F['P'], 80))])

def cell(p):
    c = int(round((p[0]-MINX)/CELL)); r = int(round((p[1]-MINY)/CELL))
    return min(max(r,0),ROWS-1), min(max(c,0),COLS-1)
def g_at(p): return G[cell(p)]
def cpos(rc): return np.array([MINX+rc[1]*CELL, MINY+rc[0]*CELL])

_R = {}
def readability(p):
    k = cell(p)
    if k in _R: return _R[k]
    q = cpos(k); r = 0.0
    for j in F['Pt'].query_ball_point(q, 40): r += F['Pc'][j]*Pu[j]
    seen = {}
    for j in F['St'].query_ball_point(q, 40):
        l = _Sl[j]; seen[l] = max(seen.get(l,0), _Sc[j])
    r += sum(seen.values())
    _R[k] = r; return r

def handrail(p):
    return any(_Sc[j] >= HANDRAIL for j in F['St'].query_ball_point(p, 7))

class Leg:
    def __init__(s, a, b):
        s.a = np.array(a, float); s.b = np.array(b, float)
        cost = np.where(G > 0.05, 1.0/(V100*np.maximum(G,0.05)), np.inf)
        s.m = MCP_Geometric(cost, fully_connected=True)
        s.cum, _ = s.m.find_costs([cell(s.b)])
        s.cum = s.cum * CELL
    def path_from(s, p):
        rc = cell(p)
        if not np.isfinite(s.cum[rc]):
            # nearest passable cell
            rr, cc = np.where(np.isfinite(s.cum[max(0,rc[0]-5):rc[0]+6, max(0,rc[1]-5):rc[1]+6]))
            if len(rr)==0: return [p, s.b]
            k = np.argmin((rr-5)**2+(cc-5)**2); rc = (max(0,rc[0]-5)+rr[k], max(0,rc[1]-5)+cc[k])
        tb = s.m.traceback(rc)[::-1]
        pts = [cpos(t) for t in tb]
        return [np.array(p,float)] + pts[1:-1] + [s.b.copy()]

def rot(a):
    c, s_ = math.cos(a), math.sin(a); return np.array([[c,-s_],[s_,c]])

def signature(p, rad=35):
    """clear things you see round p: group -> count"""
    sig = {}
    for j in F['Pt'].query_ball_point(p, rad):
        if F['Pc'][j] >= 0.3: sig[F['Pg'][j]] = sig.get(F['Pg'][j],0)+1
    lines = {}
    for j in F['St'].query_ball_point(p, rad):
        if _Sc[j] >= 0.35: lines[_Sl[j]] = F['Sg'][j]
    for l, gname in lines.items(): sig['L'+gname] = sig.get('L'+gname,0)+1
    return sig
def similarity(a, b):
    keys = set(a)|set(b)
    if not keys: return 1.0
    return sum(min(a.get(k,0),b.get(k,0)) for k in keys)/sum(max(a.get(k,0),b.get(k,0)) for k in keys)

class Runner:
    H0 = 1/1250.0      # direction-error hazard per metre for technique 0 in average terrain
    def __init__(s, leg, tech, speed=80, rng=None, record=True):
        s.leg, s.s, s.P = leg, tech/100.0, speed
        s.rng = rng or random.Random()
        s.pos = leg.a.copy(); s.bel = leg.a.copy(); s.M = np.eye(2)
        s.t = 0.0; s.track = [s.pos.copy()]; s.events = []; s.U = 8.0; s.since_fix = 0.0
        s.seen = {}; s.mode = 'route'; s.plan = leg.path_from(s.pos); s.pi = 1; s.travel = 0.0
        s.lost_t = 0.0; s.nomiss = False
    # ---------- helpers
    def ev(s, kind, extra=None):
        s.events.append((kind, s.pos.copy(), s.t, extra))
        # every surprise makes the runner more careful for the rest of the leg
        if kind in ('parallell', 'ingen_skarm', 'inser', 'osaker', 'hinder'):
            s.s = 1 - (1 - s.s) * 0.55
    def vis(s):
        g = g_at(s.pos); return 10 + 15*min(1.0, g)   # flag/feature visibility in m
    def step_true(s, d, force=False):
        n = s.pos + d
        g = g_at(n)
        if g <= 0.05:
            if not force: return False
            g = max(g_at(s.pos), 0.3)
        ln = float(np.hypot(*d)); s.t += ln/(V100*g*s.P/100); s.pos = n; s.travel += ln
        s.since_fix += ln; s.track.append(n.copy()); return True
    def replan(s):
        s.plan = s.leg.path_from(s.bel); s.pi = 1
    # ---------- observation of features at the true position
    def observe(s):
        sk = s.s; out = None
        for j in F['Pt'].query_ball_point(s.pos, 25):
            c = F['Pc'][j]
            if np.hypot(*(F['P'][j]-s.pos)) > 6 + 18*c*min(1, g_at(s.pos)): continue
            key = ('p', j)
            if s.seen.get(key, -1e9) > s.travel - 200: continue
            s.seen[key] = s.travel
            pn = c*(0.55+0.45*sk)*(1.5 if s.mode=='lost' else 1)
            if s.rng.random() < pn: out = s.interpret_point(j) or out
        for j in F['St'].query_ball_point(s.pos, 4):
            l = _Sl[j]; key = ('l', l)
            if s.seen.get(key, -1e9) > s.travel - 120: continue
            s.seen[key] = s.travel
            c = _Sc[j]; pn = min(1, c*(0.6+0.4*sk)*(1.5 if s.mode=='lost' else 1))
            if s.rng.random() < pn: out = s.interpret_line(j) or out
        return out
    def choose(s, cands, true_pos):
        """cands: list of positions. weight by closeness to belief. returns chosen pos, share"""
        U = max(s.U, 10)
        w = np.array([math.exp(-np.sum((c-s.bel)**2)/(2*U*U)) for c in cands]) + 1e-12
        w /= w.sum(); k = s.rng.choices(range(len(cands)), w)[0]
        return cands[k], w.max()
    def interpret_point(s, j):
        if s.mode == 'route': return None
        grp = F['Pg'][j]; rad = s.U + 25
        cands = [F['P'][k] for k in F['Pt'].query_ball_point(s.bel, rad) if F['Pg'][k]==grp]
        return s.resolve(cands, F['P'][j], F['Pc'][j], grp)
    def interpret_line(s, j):
        if s.mode == 'route': return None
        grp = F['Sg'][j]; rad = s.U + 25
        distinct = dist_flag[j] and s.rng.random() < 0.4 + 0.5*s.s
        best = {}
        for k in F['St'].query_ball_point(s.bel, rad):
            if F['Sg'][k] != grp: continue
            if distinct and not dist_flag[k]: continue
            d = np.sum((_S[k]-s.bel)**2); l = _Sl[k]
            if l not in best or d < best[l][0]: best[l] = (d, _S[k])
        cands = [v[1] for v in best.values()]
        return s.resolve(cands, _S[j], _Sc[j], 'L'+grp)
    def resolve(s, cands, true_p, clarity, grp):
        sk = s.s
        if not cands:
            # this does not fit where I think I am
            if s.rng.random() < min(1, clarity*(0.5+0.5*sk) + 0.25):
                if s.mode != 'lost': s.ev('inser'); s.go_lost()
            return None
        ch, share = s.choose(cands, true_p)
        if share < 0.75 and s.rng.random() < sk*(1-share)*1.5:
            if s.mode != 'lost': s.ev('osaker'); s.go_lost()
            return None
        if np.hypot(*(ch - s.pos)) < 10:
            s.ev('fix'); s.bel = s.pos.copy(); s.M = np.eye(2); s.U = 8; s.since_fix = 0
            s.mode = 'route'; s.replan(); return 'fix'
        # believes it is somewhere else: parallel error (or along-line error)
        s.ev('parallell' if grp[0] != 'L' or np.hypot(*(ch-true_p)) > 40 else 'langs_linje', ch.copy())
        s.bel = np.array(ch, float); s.M = np.eye(2); s.U = 15; s.since_fix = 0
        s.mode = 'drift'; s.replan()
        s.expect = sum(np.hypot(*(s.plan[i]-s.plan[i-1])) for i in range(1, len(s.plan)))
        return 'par'
    def go_lost(s):
        s.mode = 'lost'; s.M = np.eye(2); s.U = max(s.U, 50)
        s.t += 3 + 10*(1-s.s); s.lost_targets = []; s.plan = None
    # ---------- the leg
    def run(s, tmax=1500):
        leg = s.leg
        while s.t < tmax:
            if np.hypot(*(s.pos - leg.b)) < s.vis():
                # sees the flag
                if s.mode != 'route': s.ev('ser_skarm')
                d = leg.b - s.pos; n = np.hypot(*d)
                while n > 2:
                    if not s.step_true(d/n*2): break
                    d = leg.b - s.pos; n = np.hypot(*d)
                s.track.append(leg.b.copy()); return s.t
            if s.mode == 'lost':
                s.lost_step(); continue
            # follow the plan in belief space
            if s.pi >= len(s.plan):
                s.at_phantom(); continue
            tgt = s.plan[s.pi]; d = tgt - s.bel; n = np.hypot(*d)
            if n < 2.5: s.pi += 1; continue
            db = d/n*2.0; dt = s.M @ db
            if not s.step_true(dt, force=(s.mode=='route')):
                s.ev('hinder'); s.go_lost(); continue
            s.bel = s.bel + db
            if s.mode == 'drift':
                s.U = 12 + 0.18*s.since_fix
                if s.since_fix > (1.25 + 0.5*(1-s.s))*getattr(s, 'expect', 300) + 40:
                    s.ev('for_langt'); s.go_lost(); continue
            # start of a direction error
            if s.mode == 'route' and not s.nomiss and not handrail(s.pos):
                dctrl = np.hypot(*(s.bel-leg.b))
                diff = 0.6 + max(0.0, 1.2 - readability(s.pos)/3.0)
                near = 2.5 if dctrl < 70 else 1.0
                h = s.H0 * (1-s.s)**1.0 * diff * near + 1/60000
                if s.rng.random() < h*2.0:
                    a = math.radians(6 + abs(s.rng.gauss(0,1))*(5 + 16*(1-s.s))) * s.rng.choice((-1,1))
                    k = 1 + s.rng.gauss(0, 0.04 + 0.12*(1-s.s))
                    s.M = rot(a)*k; s.mode = 'drift'; s.since_fix = 0; s.ev('riktningsfel', math.degrees(a))
                    s.expect = sum(np.hypot(*(s.plan[i]-s.plan[i-1])) for i in range(s.pi, len(s.plan))) + np.hypot(*(s.plan[s.pi-1]-s.bel)) if s.pi < len(s.plan) else 50
            s.observe()
        return s.t
    def at_phantom(s):
        """Believes to be at the control but sees no flag. Similar ground round here?"""
        leg = s.leg
        sim = similarity(signature(s.pos), signature(leg.b))
        s.ev('ingen_skarm', sim)
        s.t += 2 + 5*(1-s.s)
        # circle here while what I see still fits the control area
        doubt_needed = 1 + 3*(1-s.s)
        if sim > 0.3:
            centre = s.pos.copy(); doubt = 0.0; r = 15; ang = s.rng.random()*6.28
            while doubt < doubt_needed and r < 60:
                ang += 2.0/max(r,5); r += 0.25
                nxt = centre + r*np.array([math.cos(ang), math.sin(ang)])
                d = nxt - s.pos; n = np.hypot(*d)
                if n > 0.1 and not s.step_true(d/n*min(n,2.0)): ang += 0.6; continue
                if np.hypot(*(s.pos - leg.b)) < s.vis(): return
                # every clear thing seen that is not in the control's picture adds doubt
                for j in F['Pt'].query_ball_point(s.pos, 12):
                    key=('c',j)
                    if key in s.seen or F['Pc'][j] < 0.3: continue
                    s.seen[key]=1
                    if s.rng.random() < F['Pc'][j]*(0.5+0.5*s.s): doubt += (1-sim)+0.3
            s.ev('cirklar_klart', r)
        s.go_lost()
    def lost_step(s):
        # pick the clearest thing round me and go there to read the map
        if s.plan is None:
            if len(s.lost_targets) > 8 or s.lost_t > 150 + 60*s.s:
                s.ev('fix'); s.bel = s.pos.copy(); s.mode='route'; s.U=8; s.replan(); return
            best, bs = None, 0
            for j in F['Pt'].query_ball_point(s.pos, 150):
                if j in s.lost_targets: continue
                d = np.hypot(*(F['P'][j]-s.pos)); sc = F['Pc'][j]*Pu[j]/(d+30)
                if sc > bs: bs, best = sc, ('p', j, F['P'][j])
            for j in F['St'].query_ball_point(s.pos, 150)[::3]:
                if _Sl[j] in s.lost_targets or _Sc[j] < 0.5: continue
                d = np.hypot(*(_S[j]-s.pos)); sc = _Sc[j]*(1.3 if dist_flag[j] else 0.7)/(d+30)
                if sc > bs: bs, best = sc, ('l', _Sl[j], _S[j])
            if best is None:
                s.ev('fix'); s.bel = s.pos.copy(); s.mode='route'; s.replan(); return
            s.lost_targets.append(best[1]); s.plan = [best[2].copy()]
            # re-open so it can be seen again
            s.seen.pop(best[:2], None)
        tgt = s.plan[0]; d = tgt - s.pos; n = np.hypot(*d)
        if n < 3:
            s.plan = None; s.t += 2 + 4*(1-s.s); s.seen = {k:v for k,v in s.seen.items() if k[0]=='c'}
            s.observe(); return
        t0 = s.t
        if not s.step_true(d/n*min(2.0, n)):
            s.plan = None; return
        s.lost_t += s.t - t0
        s.bel = s.bel + d/n*min(2.0,n); s.U += 0.15
        s.observe()

def clean_time(leg, speed=80):
    r = Runner(leg, 100, speed, random.Random(1)); r.nomiss = True; r.observe = lambda: None
    t = r.run(); return t, r.track
