# Forest mistakes: the model outside Unity

Python model of `Assets/Scripts/Mistakes/ForestMistakes.cs`, used to tune it (2026-11-09).
Needs numpy, scipy, scikit-image and Pillow. Reads the Illern 2025 map package.

* `legs.py` picks 40 legs between stones/knolls in the forest (`legs.json`).
* `calib2.py N` runs every leg N times per technique level and prints the time lost per control,
  counted like the Eventor points (a leg is a mistake when > 15 s and > 25 %). Target: 0.3 * (100 - technique) s.
* `render2.py 28,35,38` draws the pictures (`v2-bom-stracka-*.jpg`).

`sim3.py` is the model the game uses (v3: circling search, houses/olive/lakes and hills as landmarks, contour reading, longer sight in open ground and from high ground). `sim2.py` and `sim.py` are earlier versions; `sim.py` also holds shared parts. Run `calib3.py` and `render3.py` for the current model.

## v4 (2026-11-09)
sim4.py: right/wrong feature by id (road side), overshoot 1.15-1.5x, direction error max 40 deg at technique 0, loose look-round loop on ground like the control (replaces the spiral), lost search spreads out (40 m min step, 70 m visited) and prefers ground like the control. Calibration N=25: T0 32, T60 13, T80 3.4, T90 1.5 s/control.

## v5 (2026-11-09)
Bug fix: skimage traceback returns the path from the cost source to the point, so every simulated path was reversed (jump straight to the target, then walk back). That made the star shapes, straight lines and olive crossings in v2-v4 images. The game uses its own A*, so it never had this bug. Recalibrated: H0 1/2300, hazard (1-skill)^0.15. Runner stops 3.5 s when it relocates.

## v6 (2026-11-09)
sim5.py: roads split the map into sides (you always notice crossing a road): candidates and lost-search targets must be on the right side, wrong side at the control is known at once. Right house/plot = exact fix (you see the corner). Direction errors spread along the whole leg (near-control factor 1.3). Runner stops 3.5 s when relocating.

## v7 (2026-11-09)
Road junctions (with another road or big path, not the other edge line of the same road) and roads by houses/gardens (closed areas < 300 cells within 25 m) give a 100 % sure fix (Sample.roadSure).

## v8 (2026-11-09, C# only)
Junctions are sure only when no other junction lies within 300 m or a clear point (clarity >= 0.6) is within 15 m; otherwise the runner picks among the junctions near where it thinks it is (parallel error possible). Roads by houses stay sure.

## v9 (2026-11-09)
sim6.py: elite direction errors 8-16 deg, technique 0 20-40; after a direction error the runner sees nothing new for 50 m, after a surprise 40 m (no instant parallel error + fix in the same second); surprise makes the runner only a bit more careful (1-(1-s)*0.8); hazard (1-skill)^0.65. Calibration N=10: T0 33, T40 15, T60 11, T80 8, T90 1.3, T99 1.4 s/control.
