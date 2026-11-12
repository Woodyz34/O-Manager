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

## v10 (2026-11-09)
User found technique 51 made almost no mistakes in Unity. Now: H0 1/1500, 80 m on the wrong idea after a direction error, right-feature bonus 1+4*skill, a stone among many (unique<0.5) is ignored half the time. About 1.5x the technique-scale targets on purpose (T0 43, T60 21, T80 10, T90 4 s/control).

## v11 (2026-11-09)
More small mistakes: once per leg within 60 m of the control, chance 0.5*(1-s)^1.2*difficulty to aim 25-45 m beside it and look round there. Direction errors depend more on the ground (diff = 0.4 + max(0, 2 - readability/2)). H0 1/2800. Calibration N=8: T0 61 (38% of legs, median 100 s), T40 25, T60 24, T80 8, T90 4 s/control.

## v12 (2026-11-12): every map symbol read, relocation from unique things, the game code run outside Unity
From here on the model is tested with the game's own C# files, not a Python copy: `harness/` holds stand-ins for
the Unity types and a small program. Needs mono (`apt-get install mono-mcs libnewtonsoft-json5.0-cil`).

    harness/build.sh
    mono harness/harness.exe <package folder> legs.json calib 12            # time lost per control per technique
    mono harness/harness.exe <package folder> legs.json worst 20 15         # the worst run at technique 20, with its log
    mono harness/harness.exe <package folder> legs.json tracks 28,35,38 40,70,90 tracks.json 9
    python3 harness/render.py <package folder> tracks.json out-             # pictures
    mono harness/harness.exe <package folder> legs.json zones               # what the index found on the map

What changed in the model:
* `MapFeatureIndex.Classify` has a row for every point and line symbol of ISOM 2017-2 (new: railway 509, major
  power line 511, pylons at the corner points of power lines, impassable wall 515, earth banks, gullies, trench,
  prominent line features, stairway, footbridge, crossing point, building of the smallest size ...). Maps drawn with
  the ISOM 2000 numbers are translated at import (`SymbolRules.Isom2017Major`).
* Zones: marsh, open land, rough open land, the three greens, boulder field, stony ground, bare rock, sand, broken
  ground. The import writes `kinds.bytes` (one `GroundKind` per cell); older packages get the kinds from the speeds.
  You notice a field from 30 m, a marsh only when you are in it (and then only now and then).
* The runner looks for a thing of the kind it noticed round where it thinks it is; the clearer the thing, the wider
  (25 + 250 * clarity^2 m, and 700 * clarity m when nothing fits). The only one: sure (60-100 % by technique).
  Several: nearest first (1 / (1 + d^2 / U^2)), the right one * (1 + 4 * skill), each thing passed lately that fits
  * (1 + 2 * skill) or does not * (1 - 0.6 * skill), a different slope * (1 - 0.8 * skill).
* A candidate further from the true place than 30 m + (15-50 %) of the distance run since the last sure fix is
  not considered (dead reckoning). At most two parallel errors per leg; none on the way to something big.
* Technique no longer rises after a surprise. Ridge tops and valley bottoms count as handrails.
* The flag: always seen within 8 m, else one look per 25 m with chance 0.45 + 0.5 * skill.
* H0 1/1500, hazard (1-skill)^0.45, small miss at the control 0.7 * (1-skill)^0.9 * difficulty.
Calibration (40 legs x 12, Illern 2025, s lost per control / share of legs with a mistake):
T0 76 / 49 %, T40 34 / 27 %, T60 15 / 14 %, T80 9 / 8 %, T90 5 / 4 %, T99 3 / 1 % (about 2 s of every figure is
the harness's own rounding of the path).
