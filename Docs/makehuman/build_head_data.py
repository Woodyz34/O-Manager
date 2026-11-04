"""Pack the head, neck and shoulders of the MakeHuman base mesh and the targets we use into head.npz.

MakeHuman's base mesh and targets are CC0 (https://github.com/makehumancommunity/makehuman,
makehuman/data). Run once with the path to that folder:

    python3 Docs/makehuman/build_head_data.py <makehuman/data> Docs/makehuman/head.npz
"""
import os
import sys

import numpy as np

CUT_Y = 3.4          # keep everything above the chest (MakeHuman units, y up)
FACE_GROUPS = ("head", "nose", "mouth", "chin", "cheek", "eyes", "forehead", "eyebrows", "neck", "ears")


def load_obj(path):
    V, F, G = [], [], []
    g = None
    for line in open(path):
        if line.startswith("v "):
            V.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("g "):
            g = line.split()[1]
        elif line.startswith("f "):
            F.append([int(q.split("/")[0]) - 1 for q in line.split()[1:]])
            G.append(g)
    return np.array(V), F, G


def load_target(path):
    idx, d = [], []
    for line in open(path):
        if line.startswith("#") or not line.strip():
            continue
        a = line.split()
        idx.append(int(a[0]))
        d.append([float(x) for x in a[1:4]])
    return np.array(idx, int), np.array(d, float).reshape(-1, 3)


def main():
    data, out = sys.argv[1], sys.argv[2]
    V, F, G = load_obj(os.path.join(data, "3dobjs", "base.obj"))
    used = sorted({v for f, g in zip(F, G) if g == "body" for v in f if V[v, 1] > CUT_Y - 1.0})
    joints = {}
    for j in ("l-eye", "r-eye", "l-shoulder", "r-shoulder", "neck", "head"):
        joints[j] = sorted({v for f, g in zip(F, G) if g == f"joint-{j}" for v in f})
    keep = sorted(set(used).union(*joints.values()))
    remap = -np.ones(len(V), int)
    remap[keep] = np.arange(len(keep))
    faces = np.array([[remap[v] for v in f] for f, g in zip(F, G)
                      if g == "body" and all(remap[v] >= 0 and V[v, 1] > CUT_Y - 1.0 for v in f)], np.int32)
    pack = {"verts": V[keep].astype(np.float32), "faces": faces}
    for j, idx in joints.items():
        pack["joint_" + j] = remap[idx].astype(np.int32)
    names = []
    tdir = os.path.join(data, "targets")
    files = []
    for g in ("female", "male"):
        for m in ("min", "average", "max"):
            for w in ("min", "average", "max"):
                files.append(("macro", f"universal-{g}-young-{m}muscle-{w}weight"))
        for race in ("african", "asian", "caucasian"):
            files.append(("macro", f"{race}-{g}-young"))
    for grp in FACE_GROUPS:
        for fn in sorted(os.listdir(os.path.join(tdir, grp))):
            if fn.endswith(".target"):
                files.append((grp, fn[:-7]))
    for grp, name in files:
        folder = "macrodetails" if grp == "macro" else grp
        idx, d = load_target(os.path.join(tdir, folder, name + ".target"))
        m = remap[idx] >= 0 if len(idx) else np.zeros(0, bool)
        # one entry for a left/right pair, so both sides move together
        key = name.replace("l-", "", 1) if name.startswith("l-") else name.replace("r-", "", 1) if name.startswith("r-") else name
        names.append(key)
        pack[f"i_{len(names) - 1}"] = remap[idx[m]].astype(np.int32)
        pack[f"d_{len(names) - 1}"] = d[m].astype(np.float16)
    pack["names"] = np.array(names)
    np.savez_compressed(out, **pack)
    print(len(keep), "verts", len(faces), "faces", len(names), "targets", os.path.getsize(out) // 1024, "KB")


if __name__ == "__main__":
    main()
