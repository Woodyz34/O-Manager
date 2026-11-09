# Forest mistakes: the model outside Unity

Python model of `Assets/Scripts/Mistakes/ForestMistakes.cs`, used to tune it (2026-11-09).
Needs numpy, scipy, scikit-image and Pillow. Reads the Illern 2025 map package.

* `legs.py` picks 40 legs between stones/knolls in the forest (`legs.json`).
* `calib2.py N` runs every leg N times per technique level and prints the time lost per control,
  counted like the Eventor points (a leg is a mistake when > 15 s and > 25 %). Target: 0.3 * (100 - technique) s.
* `render2.py 28,35,38` draws the pictures (`v2-bom-stracka-*.jpg`).

`sim3.py` is the model the game uses (v3: circling search, houses/olive/lakes and hills as landmarks, contour reading, longer sight in open ground and from high ground). `sim2.py` and `sim.py` are earlier versions; `sim.py` also holds shared parts. Run `calib3.py` and `render3.py` for the current model.
