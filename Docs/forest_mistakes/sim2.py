# Version 2: the runner always moves along a terrain path (A*) to where it THINKS the control is.
import math, random, numpy as np
from skimage.graph import MCP_Geometric
import sim as S
from sim import F, G, CELL, MINX, MINY, ROWS, COLS, V100, cell, g_at, cpos, readability, handrail, dist_flag, Pu, similarity, signature, rot, Leg, POINT_CTRL
_S, _Sl, _Sc = F['S'], F['Sl'], F['Sc']
COST = np.where(G > 0.05, 1.0/(V100*np.maximum(G, 0.05)), np.inf)

def tpath(a, b, margin=25):
    """terrain path from a to b (both true coords) with A*-like cost, in a window round them"""
    ra, rb = cell(a), cell(b)
    r0 = max(0, min(ra[0], rb[0]) - margin); r1 = min(ROWS, max(ra[0], rb[0]) + margin + 1)
    c0 = max(0, min(ra[1], rb[1]) - margin); c1 = min(COLS, max(ra[1], rb[1]) + margin + 1)
    sub = COST[r0:r1, c0:c1]
    m = MCP_Geometric(sub, fully_connected=True)
    tb_ = (rb[0]-r0, rb[1]-c0)
    if not np.isfinite(sub[tb_]):
        return [np.array(a, float), np.array(b, float)]
    cum, _ = m.find_costs([tb_])
    st = (ra[0]-r0, ra[1]-c0)
    if not np.isfinite(cum[st]): return [np.array(a, float), np.array(b, float)]
    tb = m.traceback(st)
    pts = [np.array([MINX+(c+c0)*CELL, MINY+(r+r0)*CELL]) for r, c in tb]
    return [np.array(a, float)] + pts[1:-1] + [np.array(b, float)]

def plen(p): return sum(float(np.hypot(*(p[i]-p[i-1]))) for i in range(1, len(p)))

class Runner:
    H0 = 1/4000.0
    def __init__(s, leg, tech, speed=80, rng=None):
        s.leg, s.s, s.P = leg, tech/100.0, speed
        s.rng = rng or random.Random()
        s.pos = leg.a.copy(); s.t = 0.0; s.track = [s.pos.copy()]; s.events = []
        s.seen = {}; s.travel = 0.0; s.since_fix = 0.0; s.U = 8.0
        s.nomiss = False; s.mode = 'route'; s.misses = 0
        s.set_aim(leg.b.copy(), plan=leg.path_from(s.pos))
    # where the runner thinks it is: the control minus the way it thinks is left
    @property
    def bel(s): return s.leg.b - (s.aim - s.pos)
    def set_aim(s, aim, plan=None):
        s.aim = np.array(aim, float)
        s.plan = plan if plan is not None else tpath(s.pos, s.aim); s.pi = 1
        s.expect = plan_len = plen(s.plan); s.run_since_aim = 0.0
    def ev(s, kind, extra=None):
        s.events.append((kind, s.pos.copy(), s.t, extra))
        if kind in ('parallell', 'ingen_skarm', 'inser', 'for_langt'):
            s.s = 1 - (1 - s.s) * 0.4
    def vis(s): return 10 + 15*min(1.0, g_at(s.pos))
    def step(s, d, force=False):
        n = s.pos + d; g = g_at(n)
        if g <= 0.05:
            if not force: return False
            g = max(g_at(s.pos), 0.3)
        ln = float(np.hypot(*d)); s.t += ln/(V100*g*s.P/100); s.pos = n
        s.travel += ln; s.since_fix += ln; s.run_since_aim += ln; s.track.append(n.copy()); return True
    def fix(s):
        s.ev('fix'); s.U = 8; s.since_fix = 0; s.mode = 'route'; s.set_aim(s.leg.b.copy())
    # ---------------- observing
    def observe(s):
        sk = s.s
        for j in F['Pt'].query_ball_point(s.pos, 25):
            c = F['Pc'][j]
            if np.hypot(*(F['P'][j]-s.pos)) > 6 + 18*c*min(1, g_at(s.pos)): continue
            key = ('p', j)
            if s.seen.get(key, -1e9) > s.travel - 200: continue
            s.seen[key] = s.travel
            if s.rng.random() < c*(0.55+0.45*sk)*(1.5 if s.mode == 'lost' else 1):
                grp = F['Pg'][j]
                cands = [F['P'][k] for k in F['Pt'].query_ball_point(s.bel, s.U+25) if F['Pg'][k] == grp]
                if s.resolve(cands, F['P'][j], c): return
        for j in F['St'].query_ball_point(s.pos, 4):
            l = _Sl[j]; key = ('l', l)
            if s.seen.get(key, -1e9) > s.travel - 120: continue
            s.seen[key] = s.travel; c = _Sc[j]
            if s.rng.random() < min(1, c*(0.6+0.4*sk)*(1.5 if s.mode == 'lost' else 1)):
                grp = F['Sg'][j]; distinct = dist_flag[j] and s.rng.random() < 0.4 + 0.5*sk
                best = {}
                for k in F['St'].query_ball_point(s.bel, s.U+25):
                    if F['Sg'][k] != grp or (distinct and not dist_flag[k]): continue
                    d = np.sum((_S[k]-s.bel)**2); lk = _Sl[k]
                    if lk not in best or d < best[lk][0]: best[lk] = (d, _S[k])
                if s.resolve([v[1] for v in best.values()], _S[j], c): return
    def resolve(s, cands, true_p, clarity):
        if s.mode == 'route': return False          # on the plan: nothing to correct
        sk = s.s; frac = s.run_since_aim / max(s.expect, 1)
        if not cands:
            # does not fit. Only something big, late in the leg, makes you stop and think.
            if s.mode != 'lost' and clarity >= 0.7 and frac > 0.5 and s.rng.random() < 0.5 + 0.5*sk:
                s.ev('inser'); s.go_lost(); return True
            s.U += 10; return False
        U = max(s.U, 10)
        w = np.array([math.exp(-np.sum((c-s.bel)**2)/(2*U*U)) for c in cands]) + 1e-12
        # reading the detail round it: the right one gets a bonus that grows with technique
        for i, c in enumerate(cands):
            if np.hypot(*(c - s.pos)) < 10: w[i] *= 1 + 8*sk
        w /= w.sum()
        k = s.rng.choices(range(len(cands)), w)[0]; ch = cands[k]
        if w.max() < 0.75 and s.rng.random() < sk*(1-w.max())*1.5:
            s.U += 5; return False                  # not sure which one: keep going the same way
        if np.hypot(*(ch - s.pos)) < 10:
            s.fix(); return True
        # thinks it is at ch: from here it aims at the control as seen from ch (parallel error)
        s.ev('parallell', ch.copy()); s.U = 15; s.since_fix = 0
        s.mode = 'drift'; s.set_aim(s.pos + (s.leg.b - ch)); return True
    def go_lost(s):
        s.mode = 'lost'; s.U = max(s.U, 50); s.t += 3 + 10*(1-s.s)
        s.lost_targets = []; s.lplan = None; s.lost_t = 0.0
    # ---------------- running
    def run(s, tmax=1500):
        leg = s.leg
        while s.t < tmax:
            if np.hypot(*(s.pos - leg.b)) < s.vis():
                if s.mode != 'route': s.ev('ser_skarm')
                d = leg.b - s.pos; n = np.hypot(*d)
                while n > 2:
                    s.step(d/n*2, True); d = leg.b - s.pos; n = np.hypot(*d)
                s.track.append(leg.b.copy()); return s.t
            if s.mode == 'lost': s.lost_step(); continue
            if s.pi >= len(s.plan): s.at_phantom(); continue
            tgt = s.plan[s.pi]; d = tgt - s.pos; n = np.hypot(*d)
            if n < 2.5: s.pi += 1; continue
            s.step(d/n*min(2.0, n), True)
            if s.mode == 'drift':
                s.U = 12 + 0.18*s.since_fix
                # distance sense: you know roughly how far it is
                if s.run_since_aim > (1.15 + 0.15*(1-s.s))*s.expect + 20:
                    s.ev('for_langt'); s.go_lost(); continue
            if s.mode == 'route' and not s.nomiss and not handrail(s.pos):
                left = np.hypot(*(leg.b - s.pos))
                diff = 0.6 + max(0.0, 1.2 - readability(s.pos)/3.0)
                near = 2.5 if left < 70 else 1.0
                if s.rng.random() < 2.0*(s.H0*(1-s.s)**0.8*diff*near + 1/250000):
                    a = math.radians(6 + abs(s.rng.gauss(0,1))*(5 + 12*(1-s.s))) * s.rng.choice((-1,1))
                    k = 1 + s.rng.gauss(0, 0.04 + 0.08*(1-s.s))
                    s.ev('riktningsfel', math.degrees(a)); s.mode = 'drift'; s.since_fix = 0
                    s.set_aim(s.pos + rot(a) @ (leg.b - s.pos) * k)
            s.observe()
        return s.t
    def at_phantom(s):
        leg = s.leg; sim = similarity(signature(s.pos), signature(leg.b))
        s.ev('ingen_skarm', sim); s.t += 2 + 5*(1-s.s); s.misses += 1
        need = 1 + 3*(1-s.s)
        if sim > 0.3:
            centre = s.pos.copy(); doubt = 0.0; r = 15; ang = s.rng.random()*6.28
            while doubt < need and r < 60:
                ang += 2.0/max(r, 5); r += 0.25
                nxt = centre + r*np.array([math.cos(ang), math.sin(ang)]); d = nxt - s.pos; n = np.hypot(*d)
                if n > 0.1 and not s.step(d/n*min(n, 2.0)): ang += 0.6; continue
                if np.hypot(*(s.pos - leg.b)) < s.vis(): return
                for j in F['Pt'].query_ball_point(s.pos, 12):
                    key = ('c', j)
                    if key in s.seen or F['Pc'][j] < 0.3: continue
                    s.seen[key] = 1
                    if s.rng.random() < F['Pc'][j]*(0.5+0.5*s.s): doubt += (1-sim)+0.3
        s.go_lost()
    def lost_step(s):
        if s.lplan is None:
            if len(s.lost_targets) > 8 or s.lost_t > 90 + 60*s.s: s.fix(); return
            best, bs = None, 0
            safe = s.misses >= 2
            if safe:
                for j in F['St'].query_ball_point(s.pos, 400)[::2]:
                    if _Sc[j] < 0.6: continue
                    d = np.hypot(*(_S[j]-s.pos)); sc = _Sc[j]*(1.5 if dist_flag[j] else 1)/(d+30)
                    if sc > bs: bs, best = sc, (('safe', j), _S[j])
                if best is not None:
                    s.ev('sakert'); s.lplan = tpath(s.pos, best[1], 15); s.lpi = 1; s.safe_target = True
                    s.lost_targets.append(best[0]); return
            for j in F['Pt'].query_ball_point(s.pos, 150):
                if ('p', j) in s.lost_targets: continue
                d = np.hypot(*(F['P'][j]-s.pos)); sc = F['Pc'][j]*Pu[j]/(d+30)
                if sc > bs: bs, best = sc, (('p', j), F['P'][j])
            for j in F['St'].query_ball_point(s.pos, 150)[::3]:
                if ('l', _Sl[j]) in s.lost_targets or _Sc[j] < 0.5: continue
                d = np.hypot(*(_S[j]-s.pos)); sc = _Sc[j]*(1.3 if dist_flag[j] else 0.7)/(d+30)
                if sc > bs: bs, best = sc, (('l', _Sl[j]), _S[j])
            if best is None: s.fix(); return
            s.lost_targets.append(best[0]); s.seen.pop(best[0], None)
            s.lplan = tpath(s.pos, best[1], 10); s.lpi = 1
        if s.lpi >= len(s.lplan):
            if getattr(s, 'safe_target', False):
                s.safe_target = False; s.misses = 0; s.fix(); return
            s.lplan = None; s.t += 2 + 4*(1-s.s)
            s.seen = {k: v for k, v in s.seen.items() if k[0] == 'c'}
            s.observe(); return
        tgt = s.lplan[s.lpi]; d = tgt - s.pos; n = np.hypot(*d)
        if n < 2.5: s.lpi += 1; return
        t0 = s.t; s.step(d/n*min(2.0, n), True); s.lost_t += s.t - t0
        # moving while lost: you keep track of your own movement
        s.aim = s.aim + d/n*min(2.0, n); s.U += 0.15
        s.observe()

def clean_time(leg, speed=80):
    r = Runner(leg, 100, speed, random.Random(1)); r.nomiss = True; r.observe = lambda: None
    t = r.run(); return t, r.track
