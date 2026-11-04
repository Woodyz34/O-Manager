"""Render placeholder portraits for the runner cards in Blender (3D, no photos or scans).

The head, neck and shoulders are the MakeHuman base mesh with its shape targets (CC0, free for any use,
packed in Docs/makehuman/head.npz): every person gets their own mix of sex, build, ancestry and face
sliders (nose, mouth, chin, cheeks, eyes ...). Eyes, brows, lashes, beards, hair and the top are made here.
OLD_HEAD=1 uses the older head made from a deformed sphere instead.

    blender --background --python Docs/blender_basbilder.py -- <out folder> [count per sex] [first index] [Herr|Dam]
    (or with the bpy module: python Docs/blender_basbilder.py <out folder> ...)

Writes Herr_NN.png and Dam_NN.png, 600 x 900 with a transparent background.
Set QUICK=1 for small fast test renders, and STYLE=<name> to force one hair style.

Hair is real hair: a few thousand guide strands are grown over the scalp for each style
(side parting, messy, curly top, swept back, ponytail, braids, waves ...) and every guide gets a
group of child strands that blend between neighbouring guides, rendered as Cycles hair with the
Principled Hair BSDF. Long hair is let fall under gravity over the shoulders and down the back.
"""
import math
import os
import random
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector, noise

# ---------- settings ----------

QUICK = os.environ.get("QUICK") == "1"
RES = (300, 450) if QUICK else (600, 900)
SAMPLES = 24 if QUICK else 64
CHILD_SCALE = 0.5 if QUICK else 1.0
DENSITY = 2.5      # child hairs per guide, times the style's own number
INTERP = os.environ.get("INTERP", "1") == "1"   # children blend between neighbouring guides
THIN = 0.5         # strand thickness, times the style's own number (fine hair, no spaghetti)

# skin tones and how often each is picked (mostly light and medium, a few darker)
SKINS = [((0.89, 0.68, 0.54), 4), ((0.86, 0.64, 0.49), 4), ((0.88, 0.66, 0.50), 3), ((0.82, 0.59, 0.44), 3),
         ((0.77, 0.53, 0.38), 2), ((0.68, 0.46, 0.31), 1.5), ((0.52, 0.34, 0.23), 0.8), ((0.38, 0.24, 0.16), 0.5)]
# hair colours as (melanin, redness, shown colour for brows/caps)
HAIRS = {"blond": (0.22, 0.45, (0.62, 0.45, 0.22)), "ljus": (0.10, 0.35, (0.80, 0.66, 0.42)),
         "brun": (0.55, 0.40, (0.30, 0.17, 0.09)), "mork": (0.78, 0.30, (0.12, 0.07, 0.04)),
         "svart": (0.97, 0.15, (0.03, 0.025, 0.022)), "rod": (0.40, 0.95, (0.50, 0.17, 0.06)),
         "gra": (0.30, 0.02, (0.55, 0.53, 0.50)), "kastanj": (0.62, 0.70, (0.36, 0.17, 0.08))}
EYES = [(0.20, 0.38, 0.58), (0.25, 0.40, 0.22), (0.30, 0.17, 0.08), (0.12, 0.07, 0.04), (0.38, 0.45, 0.50)]
JERSEYS = [((0.05, 0.30, 0.13), (0.95, 0.45, 0.08), (0.96, 0.92, 0.82)),
           ((0.70, 0.12, 0.08), (0.96, 0.92, 0.82), (0.07, 0.10, 0.20)),
           ((0.05, 0.20, 0.50), (0.95, 0.72, 0.10), (1, 1, 1)),
           ((0.95, 0.45, 0.08), (0.06, 0.22, 0.11), (0.96, 0.92, 0.82)),
           ((0.30, 0.10, 0.40), (0.15, 0.70, 0.62), (1, 1, 1)),
           ((0.08, 0.09, 0.12), (0.88, 0.22, 0.16), (0.96, 0.92, 0.82)),
           ((0.03, 0.42, 0.45), (0.96, 0.92, 0.82), (0.04, 0.20, 0.22)),
           ((0.85, 0.70, 0.12), (0.05, 0.20, 0.50), (0.07, 0.10, 0.20)),
           ((0.80, 0.80, 0.80), (0.05, 0.30, 0.13), (0.05, 0.30, 0.13)),
           ((0.55, 0.05, 0.25), (0.98, 0.80, 0.20), (1, 1, 1))]
# how many of each style among 30 portraits (shuffled once, so every style shows up)
MEN_HAIR = {"side": 6, "middle": 3, "mullet": 4, "messy": 4, "curly": 3, "slick": 2, "quiff": 2, "crop": 1,
            "fade_crop": 2, "fluffy": 1, "buzz": 1, "bald": 1}
WOMEN_HAIR = {"long": 3, "long_side": 3, "curtain": 4, "wavy": 4, "bob": 2, "lob": 1,
              "pony": 2, "side_pony": 3, "low_pony": 1, "bubble": 2, "braids": 2, "bun": 1, "messy_bun": 1,
              "curly": 1}


def style_order(table, seed):
    order = [k for k, n in table.items() for _ in range(n)]
    random.Random(seed).shuffle(order)
    return order


def srgb_to_lin(c):
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c)


def smooth(a, b, v):
    t = max(0.0, min(1.0, (v - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ---------- scene ----------

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = SAMPLES
    sc.cycles.use_denoising = True
    sc.cycles.device = "CPU"
    sc.render.resolution_x, sc.render.resolution_y = RES
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = os.environ.get("LOOK", "AgX - Punchy")
    sc.cycles_curves.shape = "THICK"
    sc.cycles_curves.subdivisions = 2
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.52, 0.45, 1)
    bg.inputs[1].default_value = 0.3
    return sc


def light(name, loc, energy, size, color=(1, 1, 1), target=(0, 0, 0.1)):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.size = size
    data.color = color
    data.shape = "DISK"            # round catchlights in the eyes
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    direction = Vector(target) - Vector(loc)
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    return obj


def camera():
    cam = bpy.data.cameras.new("Cam")
    cam.lens = 85
    cam.sensor_width = 36
    obj = bpy.data.objects.new("Cam", cam)
    bpy.context.collection.objects.link(obj)
    obj.location = (0.3, -10.5, -0.55)
    obj.rotation_euler = (math.radians(90.0), 0, math.radians(1.6))
    bpy.context.scene.camera = obj
    return obj


# ---------- materials ----------

def node(nt, kind, loc=(0, 0), inputs=None, **named):
    n = nt.nodes.new(kind)
    n.location = loc
    for k, v in list((inputs or {}).items()) + list(named.items()):
        n.inputs[k].default_value = v
    return n


def principled(name, color, rough=0.5, sss=0.0, sss_radius=(1.0, 0.4, 0.25), sheen=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = rough
    if sss:
        bsdf.inputs["Subsurface Weight"].default_value = sss
        bsdf.inputs["Subsurface Radius"].default_value = sss_radius
        bsdf.inputs["Subsurface Scale"].default_value = 0.06
    if sheen:
        bsdf.inputs["Sheen Weight"].default_value = sheen
    return mat, bsdf


# ---------- node maths ----------

class S:
    """A shader value that can be combined with + - * / to build node graphs."""

    def __init__(self, nt, out):
        self.nt, self.out = nt, out

    def _op(self, other, op, swap=False):
        n = self.nt.nodes.new("ShaderNodeMath")
        n.operation = op
        a, b = (other, self) if swap else (self, other)
        for i, v in enumerate((a, b)):
            if isinstance(v, S):
                self.nt.links.new(v.out, n.inputs[i])
            else:
                n.inputs[i].default_value = v
        return S(self.nt, n.outputs[0])

    def __add__(self, o): return self._op(o, "ADD")
    __radd__ = __add__
    def __sub__(self, o): return self._op(o, "SUBTRACT")
    def __rsub__(self, o): return self._op(o, "SUBTRACT", True)
    def __mul__(self, o): return self._op(o, "MULTIPLY")
    __rmul__ = __mul__
    def __truediv__(self, o): return self._op(o, "DIVIDE")
    def __neg__(self): return self * -1.0
    def abs(self): return self._op(0.0, "ABSOLUTE")
    def max(self, o): return self._op(o, "MAXIMUM")
    def min(self, o): return self._op(o, "MINIMUM")

    def above(self, edge, soft=0.012):
        """1 where the value is above edge, with a soft (anti-aliased) step."""
        n = self.nt.nodes.new("ShaderNodeMapRange")
        self.nt.links.new(self.out, n.inputs[0])
        n.inputs[1].default_value = edge - soft
        n.inputs[2].default_value = edge + soft
        return S(self.nt, n.outputs[0])

    def below(self, edge, soft=0.012):
        return 1.0 - self.above(edge, soft)


def paint_layers(nt, base, layers):
    """Mix colours on top of each other: layers is a list of (mask S, colour)."""
    rgb = nt.nodes.new("ShaderNodeRGB")
    rgb.outputs[0].default_value = (*base, 1)
    current = rgb.outputs[0]
    for mask, color in layers:
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        nt.links.new(mask.out, mix.inputs[0])
        nt.links.new(current, mix.inputs[6])
        if isinstance(color, tuple):
            mix.inputs[7].default_value = (*color, 1)
        else:
            nt.links.new(color, mix.inputs[7])
        current = mix.outputs[2]
    return current


def blob_mask(X, Z, Y, cx, cz, rx, rz, soft=0.5, y_max=-0.3):
    """1 inside an ellipse on the face (front only), fading out softly."""
    d = ((X - cx) / rx) * ((X - cx) / rx) + ((Z - cz) / rz) * ((Z - cz) / rz)
    return d.below(1.0 - soft * 0.5, soft * 0.5) * Y.below(y_max, 0.1)


def skin_material(p):
    skin = p["skin"]
    lin = srgb_to_lin(skin)
    mat, bsdf = principled("Skin", lin, rough=0.5, sss=0.3, sss_radius=(1.0, 0.35, 0.2))
    if "mh_V" in p:
        # real skin: light goes in and comes out a few millimetres away, reddest in the shadows
        bsdf.inputs["Subsurface Weight"].default_value = 1.0
        bsdf.inputs["Subsurface Scale"].default_value = 0.025
        bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.4, 0.22)
        bsdf.inputs["IOR"].default_value = 1.4
    nt = mat.node_tree
    tex = node(nt, "ShaderNodeTexCoord")
    sep = node(nt, "ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["Object"], sep.inputs[0])
    X, Y, Z = S(nt, sep.outputs[0]), S(nt, sep.outputs[1]), S(nt, sep.outputs[2])
    if "mh_V" in p:
        # the MakeHuman head turns in the mesh, so read the position before the turn,
        # and move its heights to where the old face had its eyes, nose, mouth and chin
        rest = nt.nodes.new("ShaderNodeAttribute")
        rest.attribute_name = "rest"
        nt.links.new(rest.outputs["Vector"], sep.inputs[0])
        Zr = S(nt, sep.outputs[2])
        pts = [(p["chin_z"], -0.8), (p["mouth_z"], -0.466), (p["nose_z"], -0.24), (p["eye_z"], 0.11)]
        Z = (Zr - pts[0][0]).min(0.0) * 1.2 + pts[0][1] + (Zr - pts[-1][0]).max(0.0)
        for (a, fa), (b, fb) in zip(pts, pts[1:]):
            Z = Z + ((Zr - a) / (b - a)).max(0.0).min(1.0) * (fb - fa)
    fw = p["face_w"]

    def tint(rgb_mul, add=(0, 0, 0)):
        return srgb_to_lin(tuple(min(1.0, v * m + a) for v, m, a in zip(skin, rgb_mul, add)))

    redder = tint((1.0, 0.92, 0.92), (0.015, 0, 0))
    darker = tint((0.86, 0.82, 0.84))
    layers = []
    # blotchy natural variation
    mott = node(nt, "ShaderNodeTexNoise", Scale=14.0, Detail=4.0)
    layers.append((S(nt, mott.outputs[0]).above(0.55, 0.12) * 0.35, tint((0.95, 0.9, 0.9))))
    # warmer nose, cheeks and ears, darker round the eyes
    layers.append((blob_mask(X, Z, Y, 0.0, -0.18, 0.12, 0.16, 0.8) * 0.5, redder))
    for side in (-1, 1):
        layers.append((blob_mask(X, Z, Y, side * 0.38 * fw, -0.18, 0.2, 0.16, 0.9) * (0.55 if p["woman"] else 0.4),
                       redder))
        layers.append((blob_mask(X, Z, Y, side * 0.27 * fw, 0.13, 0.17, 0.1, 0.9) * 0.6, darker))
        # nasolabial fold
        layers.append((blob_mask(X, Z, Y, side * 0.2, -0.36, 0.035, 0.1, 0.9) * 0.45, darker))
    layers.append(((X.abs() - 0.62 * fw).above(0.0, 0.05) * 0.5, redder))
    # lips
    lip = srgb_to_lin(tuple(min(1, s * 0.7 + d) for s, d in zip(skin, (0.2, 0.02, 0.04))))
    if not p["woman"]:
        lip = srgb_to_lin(tuple(min(1, s * 0.82 + d) for s, d in zip(skin, (0.1, 0.01, 0.02))))
    if p["woman"] and p.get("lipstick"):
        lip = srgb_to_lin(tuple(min(1, s * 0.6 + d) for s, d in zip(skin, (0.25, 0.02, 0.05))))
    lips = blob_mask(X, Z, Y, 0.0, -0.468, 0.19, 0.072, 0.35 if p["woman"] else 0.7, y_max=-0.5)
    layers.append((lips, lip))
    line = srgb_to_lin(tuple(v * 0.45 for v in skin))
    layers.append((blob_mask(X, Z, Y, 0.0, -0.466, 0.17 * p["mouth_w"], 0.011, 0.6, y_max=-0.5) * (0.85 if p["woman"] else 0.5), line))
    # beard shadow on men
    if not p["woman"]:
        shade = srgb_to_lin(tuple(v * 0.72 + h * 0.12 for v, h in zip(skin, p["hair_rgb"])))
        amount = {"stubble": 0.8, "full": 0.75, "short": 0.75}.get(p["beard"], 0.3 if skin[0] > 0.6 else 0.15)
        jaw = (Z.below(-0.33, 0.06) * (1 - lips) * Y.below(0.1, 0.15)
               * ((X.abs() - 0.18).above(0.0, 0.08) + Z.below(-0.55, 0.05) + Z.above(-0.43, 0.02) * Z.below(-0.37, 0.02)).min(1.0))
        grain = node(nt, "ShaderNodeTexNoise", Scale=420.0, Detail=1.0)
        layers.append((jaw * amount * (0.6 + S(nt, grain.outputs[0]) * 0.6), shade))
    color = paint_layers(nt, lin, layers)
    nt.links.new(color, bsdf.inputs["Base Color"])
    # shinier on forehead and nose, matte cheeks
    tzone = (blob_mask(X, Z, Y, 0.0, 0.45, 0.4, 0.25, 0.9) + blob_mask(X, Z, Y, 0.0, -0.1, 0.1, 0.25, 0.9)).min(1.0)
    rough = 0.56 - tzone * 0.1
    nt.links.new(rough.out, bsdf.inputs["Roughness"])
    # pores and fine skin texture
    mh = "mh_V" in p
    pores = node(nt, "ShaderNodeTexVoronoi", Scale=420.0 if mh else 520.0)
    fine = node(nt, "ShaderNodeTexNoise", Scale=90.0, Detail=6.0)
    b1 = node(nt, "ShaderNodeBump", Strength=0.14 if mh else 0.06, Distance=0.002)
    b2 = node(nt, "ShaderNodeBump", Strength=0.08 if mh else 0.05, Distance=0.004)
    nt.links.new(pores.outputs["Distance"], b1.inputs["Height"])
    nt.links.new(fine.outputs[0], b2.inputs["Height"])
    nt.links.new(b1.outputs[0], b2.inputs["Normal"])
    nt.links.new(b2.outputs[0], bsdf.inputs["Normal"])
    return mat


def hair_material(p):
    mel, red, rgb = p["hair_col"]
    mat = bpy.data.materials.new("Hair")
    mat.use_nodes = True
    nt = mat.node_tree
    out = nt.nodes["Material Output"]
    for n in list(nt.nodes):
        if n != out:
            nt.nodes.remove(n)
    h = nt.nodes.new("ShaderNodeBsdfHairPrincipled")
    h.parametrization = "MELANIN"
    h.inputs["Melanin"].default_value = mel
    h.inputs["Melanin Redness"].default_value = red
    h.inputs["Roughness"].default_value = 0.3
    h.inputs["Radial Roughness"].default_value = 0.35
    h.inputs["Coat"].default_value = 0.05
    h.inputs["Random Color"].default_value = 0.18
    h.inputs["Random Roughness"].default_value = 0.2
    if p["hair"] == "curly" or p["hair_name"] == "gra":
        h.inputs["Roughness"].default_value = 0.42
    nt.links.new(h.outputs[0], out.inputs[0])
    return mat


def scalp_material(p):
    """The hair-coloured layer under the strands, so the scalp never shines through."""
    grey = sum(p["hair_rgb"]) / 3
    c = srgb_to_lin(tuple((v * 0.5 + grey * 0.5) * 0.45 + sk * 0.4 for v, sk in zip(p["hair_rgb"], p["skin"])))
    if "mh_V" in p:
        c = srgb_to_lin(tuple(v * 0.75 + sk * 0.2 for v, sk in zip(p["hair_rgb"], p["skin"])))
    mat, bsdf = principled("Scalp", c, rough=0.7)
    nt = mat.node_tree
    streak = node(nt, "ShaderNodeTexWave", Scale=120.0, Distortion=2.0)
    streak.wave_type = "BANDS"
    streak.bands_direction = "Z"
    mix = node(nt, "ShaderNodeMix")
    mix.data_type = "RGBA"
    nt.links.new(streak.outputs[1], mix.inputs[0])
    mix.inputs[6].default_value = (*[v * 0.85 for v in c], 1)
    mix.inputs[7].default_value = (*[min(1, v * 1.1) for v in c], 1)
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


# ---------- shapes ----------

def gauss(d2, r):
    return math.exp(-d2 / (r * r))


def sphere(name, segments=96, rings=64, radius=1.0):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for poly in mesh.polygons:
        poly.use_smooth = True
    return obj


def subsurf(obj, levels=2):
    mod = obj.modifiers.new("Subsurf", "SUBSURF")
    mod.levels = 1
    mod.render_levels = levels


# ---------- MakeHuman head (CC0 base mesh and targets, see Docs/makehuman) ----------

MH_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "makehuman", "head.npz")
MH_SCALE = 0.8          # MakeHuman units (decimetres) to ours
MH_EYE_DEPTH = 0.7     # how far in front of the middle of the head the eyes are (MakeHuman units)
_MH = {}


def mh_data():
    if not _MH:
        z = np.load(MH_FILE)
        _MH["verts"] = z["verts"].astype(np.float64)
        _MH["faces"] = z["faces"]
        _MH["joints"] = {k[6:]: z[k] for k in z.files if k.startswith("joint_")}
        targets = {}
        for k, name in enumerate(z["names"]):
            targets.setdefault(str(name), []).append((z[f"i_{k}"], z[f"d_{k}"].astype(np.float64)))
        _MH["targets"] = targets
    return _MH


def mh_add(V, name, w):
    if abs(w) < 1e-6:
        return
    for idx, d in mh_data()["targets"][name]:
        np.add.at(V, idx, d * w)


def mh_three(v):
    """MakeHuman's min/average/max split of a 0..1 slider."""
    if v < 0.5:
        return {"min": (0.5 - v) * 2, "average": 1 - (0.5 - v) * 2, "max": 0.0}
    return {"min": 0.0, "average": 1 - (v - 0.5) * 2, "max": (v - 0.5) * 2}


# face sliders: (name, low, high) for men and women; a negative value uses the "decr"/"down"/... target
MH_SLIDERS = {
    "head/head-oval": ((0.0, 0.5), (0.1, 0.7)), "head/head-rectangular": ((0.0, 0.5), (0.0, 0.2)),
    "head/head-square": ((0.0, 0.35), (0.0, 0.1)), "head/head-invertedtriangular": ((0.0, 0.3), (0.0, 0.35)),
    "head/head-round": ((0.0, 0.1), (0.0, 0.12)), "head/head-triangular": ((0.0, 0.15), (0.0, 0.15)),
    "head/head-scale-horiz": ((-0.45, 0.1), (-0.45, 0.05)), "head/head-scale-vert": ((-0.1, 0.4), (-0.1, 0.35)),
    "head/head-scale-depth": ((-0.2, 0.25), (-0.2, 0.2)), "head/head-fat": ((-0.5, 0.0), (-0.5, 0.0)),
    "head/head-angle": ((-0.2, 0.2), (-0.2, 0.2)),
    "forehead/forehead-scale-vert": ((-0.4, 0.4), (-0.4, 0.3)), "forehead/forehead-trans": ((-0.3, 0.3), (-0.3, 0.3)),
    "forehead/forehead-temple": ((-0.3, 0.3), (-0.3, 0.3)), "forehead/forehead-nubian": ((-0.3, 0.3), (-0.3, 0.2)),
    "nose/nose-scale-horiz": ((-0.3, 0.4), (-0.5, 0.15)), "nose/nose-scale-vert": ((-0.3, 0.4), (-0.4, 0.2)),
    "nose/nose-scale-depth": ((-0.3, 0.4), (-0.4, 0.2)), "nose/nose-hump": ((-0.3, 0.6), (-0.5, 0.2)),
    "nose/nose-point-width": ((-0.4, 0.3), (-0.5, 0.1)), "nose/nose-flaring": ((-0.4, 0.3), (-0.5, 0.1)),
    "nose/nose-nostrils-width": ((-0.3, 0.3), (-0.4, 0.1)), "nose/nose-point": ((-0.4, 0.3), (-0.3, 0.5)),
    "nose/nose-greek": ((-0.3, 0.3), (-0.3, 0.3)), "nose/nose-curve": ((-0.4, 0.4), (-0.4, 0.2)),
    "nose/nose-trans": ((-0.2, 0.2), (-0.2, 0.2)), "nose/nose-volume": ((-0.3, 0.3), (-0.5, 0.0)),
    "nose/nose-width1": ((-0.3, 0.3), (-0.4, 0.1)), "nose/nose-width2": ((-0.3, 0.3), (-0.4, 0.1)),
    "nose/nose-septumangle": ((-0.3, 0.3), (-0.3, 0.3)),
    "mouth/mouth-scale-horiz": ((-0.2, 0.3), (-0.3, 0.2)), "mouth/mouth-lowerlip-volume": ((-0.3, 0.3), (0.0, 0.5)),
    "mouth/mouth-upperlip-volume": ((-0.4, 0.2), (0.0, 0.5)), "mouth/mouth-cupidsbow": ((-0.3, 0.3), (0.0, 0.5)),
    "mouth/mouth-trans": ((-0.2, 0.2), (-0.2, 0.2)), "mouth/mouth-angles": ((-0.2, 0.3), (-0.1, 0.4)),
    "mouth/mouth-lowerlip-height": ((-0.3, 0.3), (-0.2, 0.3)), "mouth/mouth-upperlip-height": ((-0.3, 0.3), (-0.2, 0.3)),
    "mouth/mouth-philtrum-volume": ((-0.3, 0.3), (-0.3, 0.3)), "mouth/mouth-laugh-lines": ((-0.4, 0.2), (-0.4, 0.0)),
    "chin/chin-width": ((-0.2, 0.5), (-0.5, 0.0)), "chin/chin-height": ((-0.3, 0.4), (-0.4, 0.1)),
    "chin/chin-prominent": ((-0.2, 0.5), (-0.3, 0.2)), "chin/chin-jaw-drop": ((-0.3, 0.3), (-0.3, 0.1)),
    "chin/chin-bones": ((0.0, 0.6), (-0.5, 0.0)), "chin/chin-prognathism": ((-0.3, 0.3), (-0.3, 0.2)),
    "chin/chin-cleft": ((0.0, 0.3), (0.0, 0.0)), "chin/chin-triangle": ((0.0, 0.0), (0.0, 0.4)),
    "cheek/cheek-bones": ((-0.3, 0.5), (-0.1, 0.5)), "cheek/cheek-volume": ((-0.5, 0.1), (-0.4, 0.2)),
    "cheek/cheek-inner": ((-0.3, 0.3), (-0.3, 0.3)), "cheek/cheek-trans": ((-0.3, 0.3), (-0.3, 0.3)),
    "eyes/eye-scale": ((-0.2, 0.2), (0.0, 0.3)), "eyes/eye-trans": ((-0.2, 0.2), (-0.2, 0.2)),
    "eyes/eye-height1": ((0.0, 0.4), (0.1, 0.4)), "eyes/eye-height2": ((0.1, 0.5), (0.15, 0.5)),
    "eyes/eye-height3": ((0.0, 0.4), (0.1, 0.4)), "eyes/eye-corner1": ((-0.3, 0.3), (-0.3, 0.3)),
    "eyes/eye-corner2": ((-0.3, 0.3), (-0.2, 0.4)), "eyes/eye-eyefold": ((-0.3, 0.3), (-0.3, 0.3)),
    "eyes/eye-bag": ((-0.5, 0.1), (-0.6, 0.0)), "eyes/eye-push1": ((-0.3, 0.3), (-0.3, 0.3)),
    "eyes/eye-push2": ((-0.3, 0.3), (-0.3, 0.3)),
    "eyebrows/eyebrows-trans": ((-0.4, 0.2), (-0.2, 0.4)), "eyebrows/eyebrows-angle": ((-0.3, 0.3), (-0.2, 0.4)),
    "neck/neck-scale-horiz": ((0.0, 0.5), (-0.4, 0.0)), "neck/neck-double": ((-0.6, 0.0), (-0.6, 0.0)),
    "ears/ear-scale": ((-0.3, 0.1), (-0.4, 0.0)), "ears/ear-wing": ((-0.6, 0.0), (-0.6, -0.1)),
    "ears/ear-lobe": ((-0.3, 0.3), (-0.3, 0.3)),
}
SMILE = {"mouth/mouth-angles": 1.0, "mouth/mouth-scale-horiz": 0.25, "mouth/mouth-laugh-lines": 0.6,
         "cheek/cheek-volume": 0.3, "mouth/mouth-dimples": -0.3}
MH_PAIRS = [("decr", "incr"), ("down", "up"), ("in", "out"), ("backward", "forward"), ("compress", "uncompress"),
            ("concave", "convex")]


def mh_slider(V, key, value):
    folder, base = key.split("/")
    names = mh_data()["targets"]
    if base in names:
        mh_add(V, base, max(0.0, value))
        return
    for neg, pos in MH_PAIRS:
        a, b = f"{base}-{neg}", f"{base}-{pos}"
        if a in names and b in names:
            mh_add(V, a if value < 0 else b, abs(value))
            return


def mh_sliders(rnd, woman):
    out = {}
    for key, (men, women) in MH_SLIDERS.items():
        lo, hi = women if woman else men
        out[key] = rnd.uniform(lo, hi) if hi > lo else lo
    return out


def mh_verts(p):
    """The person's head, neck and shoulders in our coordinates (before the head turns)."""
    d = mh_data()
    V = d["verts"].copy()
    g = p["mh_gender"]
    musc, weight = mh_three(p["mh_muscle"]), mh_three(p["mh_weight"])
    for gender, gw in (("female", 1 - g), ("male", g)):
        for m, mw in musc.items():
            for w, ww in weight.items():
                mh_add(V, f"universal-{gender}-young-{m}muscle-{w}weight", gw * mw * ww)
        for race, rw in p["mh_race"].items():
            mh_add(V, f"{race}-{gender}-young", gw * rw)
    for key, value in p["mh_sliders"].items():
        mh_slider(V, key, value)
    # a small closed-mouth smile: corners up, a little wider, cheeks lifted, laugh lines
    sm = p.get("smile", 0.0)
    for key, k in SMILE.items():
        mh_slider(V, key, k * sm)
    # arms down from MakeHuman's A pose, turned about the shoulder joints
    J = d["joints"]
    for side, j in ((1, "l-shoulder"), (-1, "r-shoulder")):
        c = V[J[j]].mean(axis=0)
        w = np.clip((V[:, 0] * side - (c[0] * side - 0.5)) / 0.7, 0, 1)
        w = w * w * (3 - 2 * w) * (V[:, 1] < c[1] + 0.6)
        a = -side * math.radians(32) * w
        dx, dy = V[:, 0] - c[0], V[:, 1] - c[1]
        V[:, 0] = c[0] + dx * np.cos(a) - dy * np.sin(a)
        V[:, 1] = c[1] + dx * np.sin(a) + dy * np.cos(a)
    # eyes at z = 0.11 and the middle of the head at y = 0, whatever the targets did
    eye = (V[J["l-eye"]].mean(axis=0) + V[J["r-eye"]].mean(axis=0)) / 2
    out = np.empty_like(V)
    out[:, 0] = V[:, 0] * MH_SCALE
    out[:, 1] = -(V[:, 2] - (eye[2] - MH_EYE_DEPTH)) * MH_SCALE
    out[:, 2] = (V[:, 1] - eye[1]) * MH_SCALE + 0.11
    return out


def mh_setup(p):
    """Build the person's mesh and the ray tracers the hair, brows and beard use to find the skin."""
    from mathutils.bvhtree import BVHTree
    V = mh_verts(p)
    F = mh_data()["faces"]
    J = mh_data()["joints"]
    p["mh_V"] = V
    keep = [f for f in F if V[f, 2].min() > -1.15]           # head and neck only
    # the hair sees the ears pressed flat against the head, and grows nowhere on them
    ear = np.zeros(len(V), bool)
    for idx, d in mh_data()["targets"]["ear-wing-incr"]:      # moves only the ear itself
        ear[idx[np.abs(d).max(axis=1) > 1e-4]] = True
    H = V.copy()
    for sign in (-1, 1):
        m = ear & (V[:, 0] * sign > 0)
        if m.any():
            x0 = np.abs(V[m, 0]).min()
            ax = np.abs(V[m, 0])
            H[m, 0] = sign * np.where(ax > x0, x0 + (ax - x0) * 0.15, ax)
    p["mh_bvh"] = BVHTree.FromPolygons([tuple(v) for v in H], [tuple(f) for f in keep])
    # below the jaw (neck, shoulders, chest) there is nothing inside the skin, so nearest-point tests work;
    # the head has the mouth and eye sockets inside it, so it is tested with rays from outside
    low = [f for f in F if H[f, 2].max() < -0.8]
    p["mh_hair_bvh"] = BVHTree.FromPolygons([tuple(v) for v in H], [tuple(f) for f in low])
    ear_faces = [f for f in keep if ear[f].all()]
    p["mh_ear"] = BVHTree.FromPolygons([tuple(v) for v in H], [tuple(f) for f in ear_faces]) if ear_faces else None
    p["mh_body"] = BVHTree.FromPolygons([tuple(v) for v in V], [tuple(f) for f in F])
    p["eye_c"] = [Vector(V[J["r-eye"]].mean(axis=0)), Vector(V[J["l-eye"]].mean(axis=0))]
    # landmarks down the middle of the face
    def front_y(x, z):
        hit = p["mh_bvh"].ray_cast(Vector((x, -3.0, z)), Vector((0, 1, 0)), 6.0)
        return hit[0].y if hit[0] else 1.0
    zs = np.linspace(-1.05, 0.0, 211)
    prof = np.array([front_y(0.0, z) for z in zs])
    tip = int(np.argmin(prof))
    p["nose_z"] = float(zs[tip])
    below = np.where(zs < zs[tip] - 0.12)[0]
    # lips: the two forward bumps below the nose, the mouth line the dip between them
    seg = below[::-1]
    ups = [i for i in seg[1:-1] if prof[i] <= prof[i - 1] and prof[i] <= prof[i + 1]]
    lip_up = ups[0] if ups else seg[len(seg) // 3]
    dips = [i for i in seg if zs[i] < zs[lip_up] and i > 0 and prof[i] >= prof[i - 1] and prof[i] >= prof[i + 1]]
    mouth = dips[0] if dips else lip_up - 6
    p["mouth_z"] = float(zs[mouth])
    # chin: the lowest point of the face where the profile still faces forwards
    chin = next((i for i in range(len(zs)) if prof[i] < prof[mouth] + 0.25 and zs[i] < zs[mouth] - 0.15), 0)
    p["chin_z"] = float(zs[chin])
    p["eye_z"] = float((p["eye_c"][0].z + p["eye_c"][1].z) / 2)
    xs = V[np.abs(V[:, 2] - 0.45) < 0.04, 0]
    p["face_w"] = float(np.abs(xs).max() / 0.68)
    p["eye_x"] = float(abs(p["eye_c"][1].x) / p["face_w"])


def mh_ray(bvh, d, far=4.0):
    d = d.normalized()
    hit = bvh.ray_cast(d * far, -d, far)
    return hit[0] if hit[0] is not None else d * 0.9


def face_z(p, z):
    """Our old face heights (mouth at -0.466, chin at -0.8) for a height z on this face."""
    if "mh_V" not in p:
        return z
    pts = [(p["chin_z"], -0.8), (p["mouth_z"], -0.466), (p["nose_z"], -0.24), (p["eye_z"], 0.11)]
    if z <= pts[0][0]:
        return -0.8 + (z - pts[0][0]) * 1.2
    for (a, fa), (b, fb) in zip(pts, pts[1:]):
        if z <= b:
            return fa + (z - a) / (b - a) * (fb - fa)
    return z


def make_mh_body(p, skin_mat):
    """The MakeHuman mesh: head, neck and shoulders in one piece. The head part turns with the head."""
    V = p["mh_V"].copy()
    pivot = Vector((0, 0.1, -0.6))
    rot = Matrix.Rotation(math.radians(p["tilt"]), 3, "X")
    rot = Matrix.Rotation(math.radians(p["turn"]), 3, "Z") @ rot
    R = np.array(rot)
    w = np.clip((V[:, 2] + 1.2) / 0.5, 0, 1)
    w = w * w * (3 - 2 * w)
    turned = (V - np.array(pivot)) @ R.T + np.array(pivot)
    V = V * (1 - w[:, None]) + turned * w[:, None]
    # the skin under the top is never seen and pokes through it where the arms meet the body: drop it,
    # but keep the chest under the neckline
    R0 = p["mh_V"]
    faces = []
    for f in mh_data()["faces"]:
        c = R0[f].mean(axis=0)
        # well inside the top (see make_mh_jersey), so no gap opens between the top's edge and the skin
        under = c[2] < -1.3 + 0.1 * smooth(-0.1, 0.4, c[1]) or (abs(c[0]) > 0.8 and c[2] < -1.15)
        if under and not (c[2] > -1.75 and c[1] < 0.05 and abs(c[0]) < 0.55):
            continue
        faces.append(tuple(f))
    me = bpy.data.meshes.new("Body")
    me.from_pydata([tuple(v) for v in V], [], faces)
    obj = bpy.data.objects.new("Body", me)
    bpy.context.collection.objects.link(obj)
    for poly in me.polygons:
        poly.use_smooth = True
    rest = me.attributes.new("rest", "FLOAT_VECTOR", "POINT")
    rest.data.foreach_set("vector", p["mh_V"].astype(np.float32).ravel())
    me.materials.append(skin_mat)
    subsurf(obj, 2)
    p["mh_world"] = V
    return obj


def make_mh_jersey(p, jersey_mat):
    """The top: a copy of the chest and shoulders, a little out from the skin. The shader cuts the neckline."""
    V = p["mh_world"]
    F = mh_data()["faces"]
    me = bpy.data.meshes.new("Torso")
    neck_line = lambda y: -1.12 + 0.1 * smooth(-0.1, 0.4, y)
    faces = [f for f in F if (V[f, 2] < neck_line(V[f, 1].mean())).all()]
    used = sorted({i for f in faces for i in f})
    remap = {i: k for k, i in enumerate(used)}
    me.from_pydata([tuple(V[i]) for i in used], [], [tuple(remap[i] for i in f) for f in faces])
    # cloth does not follow every bump of the body: smooth it, then lift it off the skin
    bm = bmesh.new()
    bm.from_mesh(me)
    for _ in range(12):
        bmesh.ops.smooth_vert(bm, verts=[v for v in bm.verts if not v.is_boundary], factor=0.5,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = True
    me.update()
    from mathutils.bvhtree import BVHTree
    body = BVHTree.FromPolygons([tuple(v) for v in V], [tuple(f) for f in F])
    off = 0.025
    loc = Vector((0, 0.18, -1.78))
    for v in me.vertices:
        q = v.co + v.normal * 0.01
        for _ in range(3):
            near, n, _, _ = body.find_nearest(q)
            if near is None or (q - near).dot(n) >= off:
                break
            q = near + n * off
        v.co = q - loc
    obj = bpy.data.objects.new("Torso", me)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    me.materials.append(jersey_mat)
    sol = obj.modifiers.new("Solid", "SOLIDIFY")    # the cloth has an inside, seen at the collar
    sol.thickness = 0.012
    sol.offset = -1.0
    subsurf(obj, 2)
    return obj


def head_shape(v, p):
    """Turn a unit sphere point into a head. Front of the face is -Y, up is +Z."""
    if "mh_bvh" in p:
        return mh_ray(p["mh_bvh"], Vector((v[0] * 0.68 * p["face_w"], v[1] * 0.88, v[2])))
    x, y, z = v
    w = p["face_w"]
    x *= 0.68 * w
    y *= 0.88
    if z > 0:
        x *= 1 + (p["skull_w"] - 1) * smooth(0.0, 0.6, z)
        y *= 1 + (p["skull_d"] - 1) * smooth(0.0, 0.6, z) * (1 if y > 0 else 0.3)
        z *= 1 + (p["skull_h"] - 1) * smooth(0.1, 0.7, z)
    if z < 0:
        t = -z
        z *= p["len"]
        # lower face: keeps its width down to the jaw corner, then narrows to the chin
        x *= 1 - p["jaw_taper"] * t ** p["taper_pow"]
        x *= 1 + p["cheek_full"] * gauss((t - 0.3) ** 2, 0.2)
        x *= 1 + p["jaw"] * gauss((t - 0.68) ** 2, 0.17) * (1 if y < 0.2 else 0.5)
        y *= 1 - 0.18 * t ** 2
        if y < 0:
            y -= 0.05 * t ** 2   # chin a little forward
        # flatter underside of the jaw
        if t > 0.82:
            z = -p["len"] * (0.82 + (t - 0.82) * 0.55)
    # flatter face plane
    if y < -0.45:
        y = -0.45 + (y + 0.45) * 0.6
    if y > 0 and z > -0.2:
        y *= 1.06
    front = smooth(0.0, 0.55, -y)
    # forehead slopes back a little
    if z > 0.3:
        y += 0.09 * (z - 0.3) ** 1.5 * front
    # temples
    for side in (-1, 1):
        y += 0.03 * gauss((x - side * 0.6 * w) ** 2 + (z - 0.3) ** 2, 0.15) * front
    for side in (-1, 1):
        ex = side * p["eye_x"] * w
        # eye sockets
        d2 = (x - ex) ** 2 + ((z - 0.11) * 1.5) ** 2
        y += 0.09 * gauss(d2, 0.12) * front
        # cheekbones forward, a hollow below them
        d2 = (x - side * 0.4 * w) ** 2 + ((z + 0.06) * 1.4) ** 2
        y -= 0.06 * gauss(d2, 0.15) * front * p["cheek"]
        d2 = (x - side * 0.42 * w) ** 2 + ((z + 0.34) * 1.2) ** 2
        y += 0.03 * gauss(d2, 0.12) * front * p["lean"]
        # nasolabial fold: cheek above it a little forward
        d2 = (x - side * 0.25) ** 2 + ((z + 0.3) * 0.8) ** 2
        y -= 0.025 * gauss(d2, 0.07) * front
        # mouth corners in
        d2 = (x - side * 0.2) ** 2 + (z + 0.47) ** 2
        y += 0.02 * gauss(d2, 0.04) * front
    # brow ridge
    d2 = (x / 2.4) ** 2 + ((z - 0.26) * 1.3) ** 2
    y -= p["brow"] * gauss(d2, 0.09) * front
    # nose: bridge from between the eyes down to a round tip, nostril wings either side
    nz = p["nose"]
    if abs(x) < 0.35:
        top, tip = 0.14, p["tip"]
        k = max(0.0, min(1.0, (top - z) / (top - tip)))
        ridge = gauss(x * x, 0.055 + 0.022 * k) * k ** 1.25 * gauss(max(0.0, z - top) ** 2, 0.05)
        if z < tip:
            ridge *= gauss((tip - z) ** 2, 0.055)    # rounds off under the tip, no overhang
        y -= nz * 0.16 * ridge * front
        d2 = (x * 1.05) ** 2 + ((z - tip) * 1.1) ** 2
        y -= nz * 0.035 * gauss(d2, 0.07) * front                     # tip
        for side in (-1, 1):
            d2 = (x - side * 0.1 * p["nose_w"]) ** 2 + ((z - tip + 0.035) * 1.2) ** 2
            y -= 0.07 * gauss(d2, 0.06) * front                       # nostril wings
            d2 = ((x - side * 0.055) * 0.8) ** 2 + ((z + 0.3) * 1.4) ** 2
            y += 0.003 * gauss(d2, 0.025) * front                     # nostrils
        # dip between the eyes
        y += 0.02 * gauss(x * x + (z - 0.16) ** 2, 0.07) * front
    # philtrum
    for side in (-1, 1):
        d2 = ((x - side * 0.035) * 3) ** 2 + ((z + 0.38) * 0.9) ** 2
        y -= 0.012 * gauss(d2, 0.05) * front
    # lips: upper lip a gentle bow, lower lip fuller
    lw = p["mouth_w"]
    for cz, amount, r in ((-0.43, 0.05, 0.035), (-0.505, 0.06, 0.045)):
        d2 = (x / (2.4 * lw)) ** 2 + (z - cz) ** 2
        y -= amount * gauss(d2, r) * front * p["lips"]
    d2 = (x / (3.2 * lw)) ** 2 + (z + 0.465) ** 2       # the line between the lips
    y += 0.03 * gauss(d2, 0.01) * front
    # dip under the lower lip, then the chin
    d2 = (x / 2.0) ** 2 + (z + 0.585) ** 2
    y += 0.02 * gauss(d2, 0.04) * front
    d2 = (x / (p["chin_w"] + p["jaw"])) ** 2 + ((z + 0.72 * p["len"]) * 1.1) ** 2
    y -= p["chin"] * gauss(d2, 0.1) * front
    return Vector((x, y, z))


def surface_point(p, x, z):
    """The point on the front of the head that is at x, z (found by a few Newton steps)."""
    if "mh_bvh" in p:
        hit = p["mh_bvh"].ray_cast(Vector((x, -3.0, z)), Vector((0, 1, 0)), 6.0)
        if hit[0] is not None:
            return hit[0]
    d = Vector((x / 0.68, -0.8, z)).normalized()
    for _ in range(12):
        s = head_shape(d, p)
        ex, ez = x - s.x, z - s.z
        d = Vector((d.x + ex * 1.1, d.y, d.z + ez)).normalized()
        if d.y > -0.05:
            d.y = -0.05
            d.normalize()
    return head_shape(d, p)


def make_head(p, skin_mat):
    head = sphere("Head", 192, 128)
    for vert in head.data.vertices:
        vert.co = head_shape(vert.co, p)
    head.data.materials.append(skin_mat)
    subsurf(head, 1)
    return head


def make_ears(p, skin_mat):
    ears = []
    for side in (-1, 1):
        ear = sphere("Ear", 48, 32)
        for vert in ear.data.vertices:
            x, y, z = vert.co
            # a shell with a rolled rim and a hollow bowl
            rim = math.sqrt(y * y + z * z)
            bowl = 0.05 * gauss(rim * rim, 0.55) if x > 0 else 0.0
            x = x * 0.07 - bowl
            vert.co = Vector((x, y * 0.16, z * (0.26 if z > 0 else 0.22) - 0.02 * max(0, -z) ** 2))
        ear.location = (side * 0.665 * p["face_w"], 0.1, 0.0)
        ear.rotation_euler = (math.radians(-6), 0, side * math.radians(-22))
        ear.scale = (-side, 1, 1)
        ear.data.materials.append(skin_mat)
        subsurf(ear)
        ears.append(ear)
    return ears


def hair_object(name, strands, mat, radius=0.0045, tip=0.35):
    """strands: float array (count, points, 3). Rendered as Cycles hair."""
    strands = np.asarray(strands, dtype=np.float32)
    count, n, _ = strands.shape
    if name not in ("Brows", "Lashes"):
        radius *= THIN
    hc = bpy.data.hair_curves.new(name)
    hc.add_curves([n] * count)
    hc.attributes["position"].data.foreach_set("vector", strands.ravel())
    t = np.linspace(0, 1, n, dtype=np.float32)
    per = np.random.default_rng(count).uniform(0.75, 1.25, count).astype(np.float32)
    rad = (per[:, None] * radius * (1 - t * (1 - tip))[None, :]).ravel()
    if "radius" not in hc.attributes:
        hc.attributes.new("radius", "FLOAT", "POINT")
    hc.attributes["radius"].data.foreach_set("value", rad)
    obj = bpy.data.objects.new(name, hc)
    bpy.context.collection.objects.link(obj)
    hc.materials.append(mat)
    return obj


def eye_material(p):
    iris_c = srgb_to_lin(p["eye"])
    mat = bpy.data.materials.new("Eye")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.05
    bsdf.inputs["Coat Weight"].default_value = 1.0
    tex = node(nt, "ShaderNodeTexCoord")
    sep = node(nt, "ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["Object"], sep.inputs[0])
    xz = node(nt, "ShaderNodeCombineXYZ")
    nt.links.new(sep.outputs[0], xz.inputs[0]); nt.links.new(sep.outputs[2], xz.inputs[1])
    scale = node(nt, "ShaderNodeVectorMath"); scale.operation = "SCALE"
    scale.inputs["Scale"].default_value = 1 / 0.118
    nt.links.new(xz.outputs[0], scale.inputs[0])
    r = node(nt, "ShaderNodeVectorMath"); r.operation = "LENGTH"
    nt.links.new(scale.outputs[0], r.inputs[0])
    # iris fibres
    fib = node(nt, "ShaderNodeTexNoise", Scale=30.0, Detail=6.0)
    nt.links.new(xz.outputs[0], fib.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    nt.links.new(r.outputs[1], ramp.inputs[0])
    els = ramp.color_ramp.elements
    els[0].position = 0.0; els[0].color = (0.004, 0.003, 0.003, 1)
    els[1].position = 0.56; els[1].color = (0.62, 0.58, 0.55, 1)
    e = els.new(0.17); e.color = (0.004, 0.003, 0.003, 1)
    e = els.new(0.2); e.color = (*[min(1, v * 1.4) for v in iris_c], 1)
    e = els.new(0.42); e.color = (*[v * 0.7 for v in iris_c], 1)
    e = els.new(0.47); e.color = (0.03, 0.025, 0.02, 1)
    e = els.new(0.51); e.color = (0.62, 0.58, 0.55, 1)
    mul = node(nt, "ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs[0].default_value = 0.35
    nt.links.new(ramp.outputs[0], mul.inputs[6])
    nt.links.new(fib.outputs[1], mul.inputs[7])
    front = node(nt, "ShaderNodeMapRange", inputs={1: -0.04, 2: -0.07, 3: 0.0, 4: 1.0})
    nt.links.new(sep.outputs[1], front.inputs[0])
    mix = node(nt, "ShaderNodeMix")
    mix.data_type = "RGBA"
    nt.links.new(front.outputs[0], mix.inputs[0])
    mix.inputs[6].default_value = (0.6, 0.55, 0.52, 1)
    nt.links.new(mul.outputs[2], mix.inputs[7])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def lid_margin(p, c, R, dx, up=True):
    """How far above (or below) the eye centre the lid edge is, dx to the side: where the
    skin starts to cover the eyeball when seen from the front."""
    step = 0.003 if up else -0.003
    dz = 0.0
    while abs(dz) < R * 1.3:
        hit = p["mh_bvh"].ray_cast(Vector((c.x + dx, -3.0, c.z + dz)), Vector((0, 1, 0)), 6.0)[0]
        q = R * R - dx * dx - dz * dz
        ey = c.y - math.sqrt(q) if q > 0 else 9.0
        if hit is not None and hit.y < ey - 0.001:
            return dz, hit
        dz += step
    return None, None


def make_mh_eyes(p, hair_mat):
    """Eyeballs in the MakeHuman eye sockets (the mesh has its own lids) and lashes along the upper lid."""
    mat = eye_material(p)
    objs = []
    R = p["eye_r"]
    look = p["look"]
    lash_strands = []
    L = 0.04 if p["woman"] else 0.028
    for side, c in zip((-1, 1), p["eye_c"]):
        c = c + Vector((0, p["eye_back"], 0))
        eye = sphere("Eye", 48, 32, R)
        eye.location = c
        eye.rotation_euler = (math.radians(look[1]), 0, math.radians(look[0]))
        eye.data.materials.append(mat)
        objs.append(eye)
        rnd = random.Random(p["seed"] * 13 + side)
        n = 60 if p["woman"] else 30
        for i in range(n):
            u = (i + rnd.random()) / n * 1.8 - 0.9
            dx = u * R
            dz, hit = lid_margin(p, c, R, dx)
            if hit is None:
                continue
            root = hit + Vector((0, 0.002, -0.002))
            out = Vector((dx * 0.6 * side * side, -1.0, 0.25)).normalized()
            l = L * (0.55 + 0.45 * max(0.0, 1 - u * u)) * rnd.uniform(0.8, 1.1)
            if u * side > 0:
                l *= 1.15   # longer towards the outer corner
            pts = [root + out * l * tt + Vector((0, 0, 1)) * l * 0.8 * tt * tt + Vector((dx * 0.4 * tt, 0, 0))
                   for tt in np.linspace(0, 1, 5)]
            lash_strands.append([tuple(q) for q in pts])
    if lash_strands:
        objs.append(hair_object("Lashes", lash_strands, hair_mat_dark(p), 0.0026 if p["woman"] else 0.0016, 0.3))
    return objs


def make_eyes(p, skin_mat, hair_mat):
    """Eyeballs, almond shaped eyelids that cover the top of the iris, and lashes."""
    if "mh_bvh" in p:
        return make_mh_eyes(p, hair_mat)
    mat = eye_material(p)
    objs = []
    R = p["eye_r"]
    look = p["look"]
    lash_strands = []
    for side in (-1, 1):
        ex, ez = side * p["eye_x"] * p["face_w"], 0.11
        sy = surface_point(p, ex, ez).y
        c = Vector((ex, sy + 0.06, ez))
        eye = sphere("Eye", 48, 32, R)
        eye.location = c
        eye.rotation_euler = (math.radians(look[1]), 0, math.radians(look[0]))
        eye.data.materials.append(mat)
        objs.append(eye)
        # eyelids: a skin shell round the eye with an almond shaped opening
        RL = R * 1.07
        up, lo = p["lid"] * R, p["lid_lo"] * R
        tilt = p["tilt_eye"] * side

        def opening(x):
            k = max(0.0, 1 - (x / (RL * 0.92)) ** 2)
            top = up * k ** 0.65 + tilt * x / RL * R * 0.25
            bot = -lo * k ** 0.8 + tilt * x / RL * R * 0.15
            # the inner corner sits a little lower
            inner = smooth(0.0, 1.0, -x * side / RL) * 0.012
            return top - inner, bot - inner * 0.5
        lid = sphere("Lid", 96, 64, RL)
        bm = bmesh.new()
        bm.from_mesh(lid.data)
        cut = []
        for v in bm.verts:
            x, y, z = v.co
            top, bot = opening(x)
            if y < -0.25 * RL and bot < z < top:
                cut.append(v)
        bmesh.ops.delete(bm, geom=cut, context="VERTS")
        bm.to_mesh(lid.data)
        bm.free()
        lid.location = c
        lid.data.materials.append(skin_mat)
        sol = lid.modifiers.new("Solid", "SOLIDIFY")
        sol.thickness = 0.014
        subsurf(lid, 2)
        objs.append(lid)
        # upper and lower lid creases: thin rolls of skin
        for which, zoff, rr in (("up", 0.022, 0.012), ("lo", -0.012, 0.008)):
            pts = []
            for i in range(21):
                x = -RL * 0.9 + i * RL * 1.8 / 20
                top, bot = opening(x)
                z = (top + zoff) if which == "up" else (bot + zoff)
                if which == "up":
                    z += 0.012 * max(0.0, 1 - (x / RL) ** 2)
                y = -math.sqrt(max(0.0, (RL + 0.006) ** 2 - x * x - z * z))
                pts.append(Vector((x, y, z)))
            cu = bpy.data.curves.new("Crease", "CURVE")
            cu.dimensions = "3D"
            cu.bevel_depth = rr
            cu.bevel_resolution = 3
            sp = cu.splines.new("NURBS")
            sp.points.add(len(pts) - 1)
            for i, q in enumerate(pts):
                tt = i / (len(pts) - 1)
                sp.points[i].co = (q.x, q.y, q.z, 1)
                sp.points[i].radius = math.sin(math.pi * tt) ** 0.7
            sp.order_u = 4
            sp.use_endpoint_u = True
            co = bpy.data.objects.new("Crease", cu)
            bpy.context.collection.objects.link(co)
            co.location = c
            co.data.materials.append(skin_mat)
            objs.append(co)
        # lashes along the upper lid edge, curling out and up
        L = 0.045 if p["woman"] else 0.032
        rnd = random.Random(p["seed"] * 13 + side)
        for i in range(70 if p["woman"] else 50):
            x = rnd.uniform(-0.85, 0.9) * RL * (1 if side > 0 else -1)
            top, _ = opening(x)
            z = top - 0.002
            y = -math.sqrt(max(0.0, (RL + 0.012) ** 2 - x * x - z * z))
            root = c + Vector((x, y, z))
            out = Vector((x * 0.5, y, z * 0.3)).normalized()
            l = L * (0.6 + 0.4 * max(0.0, 1 - (x / RL) ** 2)) * rnd.uniform(0.8, 1.1)
            if x * side > 0:
                l *= 1.15   # longer at the outer corner
            pts = [root + out * l * tt + Vector((0, 0, 1)) * l * 0.7 * tt * tt + Vector((x * 0.3 * tt * l, 0, 0)) for tt in np.linspace(0, 1, 5)]
            lash_strands.append([tuple(q) for q in pts])
    objs.append(hair_object("Lashes", lash_strands, hair_mat_dark(p), 0.0028 if p["woman"] else 0.0022, 0.3))
    return objs


def hair_mat_dark(p):
    """Lashes are dark whatever the hair colour."""
    mat = bpy.data.materials.new("Lash")
    mat.use_nodes = True
    nt = mat.node_tree
    out = nt.nodes["Material Output"]
    for n in list(nt.nodes):
        if n != out:
            nt.nodes.remove(n)
    h = nt.nodes.new("ShaderNodeBsdfHairPrincipled")
    h.parametrization = "MELANIN"
    h.inputs["Melanin"].default_value = max(0.8, p["hair_col"][0])
    h.inputs["Roughness"].default_value = 0.4
    nt.links.new(h.outputs[0], out.inputs[0])
    return mat


def make_brows(p, mat):
    """Eyebrows made of short hairs lying flat on the brow, pointing outwards."""
    rnd = random.Random(p["seed"] * 101 + 5)
    strands = []
    fw = p["face_w"]
    thick = p["brow_thick"]
    mh = "mh_bvh" in p
    for side in (-1, 1):
        for i in range(int(420 * thick)):
            u = rnd.random()                         # 0 = inner end, 1 = outer end
            bx = side * (0.09 + u * 0.33) * fw
            arch = 0.255 + (0.065 if p["woman"] else 0.045) * math.sin(min(1.0, u / 0.7) * math.pi * 0.5) - (0.06 * max(0.0, u - 0.7) / 0.3) ** 1.3
            arch += p["brow_raise"]
            if mh:
                ex = abs(p["eye_c"][0].x)
                bx = side * ex * (0.32 + u * 1.25)
                arch += p["eye_z"] - 0.11 + p["brow_h"]
            width = (0.042 - (0.03 if p["woman"] else 0.022) * u) * thick
            bz = arch + rnd.uniform(-0.5, 0.5) * width
            root = surface_point(p, bx, bz)
            n = Vector((root.x * 0.2, -1.0, 0.25)).normalized()
            # inner hairs point up, the rest outwards and a little down
            ang = math.radians(70 * max(0.0, 1 - u / 0.25) + 18 - 30 * u + rnd.uniform(-12, 12))
            d = Vector((side * math.cos(ang), 0.15 * side * math.cos(ang) * (abs(bx) / 0.4), math.sin(ang)))
            L = rnd.uniform(0.03, 0.05) if p["woman"] else rnd.uniform(0.035, 0.06)
            pts = [root + n * (0.003 + 0.006 * tt) + d * L * tt for tt in (0, 0.33, 0.66, 1.0)]
            strands.append([tuple(q) for q in pts])
    return hair_object("Brows", strands, mat, 0.0042 * (1.15 if not p["woman"] else 0.8), 0.4)


def make_neck_and_body(p, skin_mat, jersey_mat):
    neck = bpy.data.meshes.new("Neck")
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=48, radius1=0.42, radius2=0.36, depth=1.0)
    bm.to_mesh(neck)
    bm.free()
    n = bpy.data.objects.new("Neck", neck)
    bpy.context.collection.objects.link(n)
    n.location = (0, 0.12, -0.88)
    n.scale = (p["neck_w"], 0.92, 1)
    n.data.materials.append(skin_mat)
    for poly in neck.polygons:
        poly.use_smooth = True
    subsurf(n)

    torso = sphere("Torso", 96, 48)
    sw = p["shoulders"]
    for vert in torso.data.vertices:
        x, y, z = vert.co
        z = z * (0.72 if z > 0 else 2.0)
        x *= sw
        y *= 0.6
        z += 0.2 * gauss(x * x, 0.45) * max(0.0, vert.co.z)
        vert.co = Vector((x, y, z))
    torso.location = (0, 0.18, -1.78)
    torso.data.materials.append(jersey_mat)
    subsurf(torso)

    return [n, torso]


def blob(name, mat, loc, scale, rot=(0, 0, 0), wobble=0.0):
    obj = sphere(name, 48, 32)
    if wobble:
        for v in obj.data.vertices:
            v.co += v.co.normalized() * wobble * noise.noise(v.co * 4.0)
    obj.location = loc
    obj.scale = scale
    obj.rotation_euler = rot
    obj.data.materials.append(mat)
    subsurf(obj, 1)
    return obj


def make_extra(p):
    objs = []
    if p["extra"] == "band":
        mat, b = principled("Band", srgb_to_lin(p["jersey"][1]), rough=0.7, sheen=0.5)
        bpy.ops.mesh.primitive_torus_add(major_radius=1.0, minor_radius=0.045, location=(0, 0.06, 0.42),
                                         major_segments=64, minor_segments=12)
        t = bpy.context.active_object
        t.scale = (0.7 * p["face_w"] + p["vol"] * 0.3, 0.86 + p["vol"] * 0.3, 1.6)
        t.rotation_euler = (math.radians(-8), 0, 0)
        t.data.materials.append(mat)
        bpy.ops.object.shade_smooth()
        objs.append(t)
    elif p["extra"] == "glasses":
        frame, b = principled("Frame", (0.02, 0.02, 0.02), rough=0.3)
        for side in (-1, 1):
            ex = side * 0.265 * p["face_w"]
            fy = surface_point(p, ex, 0.11).y - 0.1
            bpy.ops.mesh.primitive_torus_add(major_radius=0.13, minor_radius=0.016,
                                             location=(ex, fy, 0.1),
                                             rotation=(math.radians(90), 0, 0), major_segments=48, minor_segments=8)
            g = bpy.context.active_object
            g.scale = (1.12, 0.78, 0.82)
            g.data.materials.append(frame)
            objs.append(g)
            bpy.ops.mesh.primitive_cylinder_add(radius=0.01, depth=0.62, location=(side * 0.66 * p["face_w"], fy + 0.31, 0.13),
                                                rotation=(math.radians(90), 0, 0))
            bpy.context.active_object.data.materials.append(frame)
            objs.append(bpy.context.active_object)
        bpy.ops.mesh.primitive_cylinder_add(radius=0.01, depth=0.15, location=(0, fy + 0.0, 0.14),
                                            rotation=(0, math.radians(90), 0))
        bpy.context.active_object.data.materials.append(frame)
        objs.append(bpy.context.active_object)
    elif p["extra"] == "cap":
        mat, b = principled("Cap", srgb_to_lin(p["jersey"][0]), rough=0.7, sheen=0.3)
        crown = blob("Crown", mat, (0, 0.05, 0.4), (0.76 * p["face_w"] + 0.06, 0.96, 0.66))
        bm = bmesh.new()
        bm.from_mesh(crown.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 0.0], context="VERTS")
        bm.to_mesh(crown.data)
        bm.free()
        objs.append(crown)
        objs.append(blob("Visor", mat, (0, -0.87, 0.44), (0.56, 0.42, 0.03), (math.radians(-8), 0, 0)))
    return objs


# ---------- orienteering jersey ----------

def jersey_material(p):
    """A sublimated club top: base colour, a bold graphic, raglan shoulders, trims and
    the orienteering control flag on the chest."""
    base, second, trim = (srgb_to_lin(c) for c in p["jersey"])
    dark = tuple(v * 0.55 for v in base)
    mat, bsdf = principled("Jersey", base, rough=0.62, sheen=0.5)
    bsdf.inputs["Sheen Tint"].default_value = (1, 1, 1, 1)
    nt = mat.node_tree
    tex = node(nt, "ShaderNodeTexCoord")
    sep = node(nt, "ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["Object"], sep.inputs[0])
    X, Y, Z = S(nt, sep.outputs[0]), S(nt, sep.outputs[1]), S(nt, sep.outputs[2])
    ax = X.abs()
    flip = p["design_flip"]
    Xd = X * flip
    layers = []
    design = p["design"]
    if design == "split":
        edge = Z - (Xd * 0.9 - 0.05)
        layers.append((edge.below(0.0), second))
        for k, off in enumerate((0.07, 0.13, 0.19)):
            layers.append(((edge - off).abs().below(0.014 - 0.003 * k), trim))
        layers.append(((-0.55 - Z).above(0.0, 0.25), dark))
    elif design == "chevron":
        v = Z - (ax * 0.62 - 0.32)
        layers.append((v.abs().below(0.17), second))
        layers.append(((v.abs() - 0.17).abs().below(0.022), trim))
        v2 = Z - (ax * 0.62 - 0.72)
        layers.append((v2.abs().below(0.06), second))
    else:  # swoosh
        line = Z - (Xd * 0.42 - 0.12)
        layers.append((line.abs().below(0.24), second))
        layers.append(((line.abs() - 0.24).abs().below(0.025), trim))
        lower = Z - (Xd * 0.42 - 0.62)
        layers.append((lower.abs().below(0.05), dark))
    seam = ax - 0.36 - (0.86 - Z) * 1.15
    layers.append((seam.above(0.0), second if design != "split" else dark))
    layers.append((seam.abs().below(0.018), trim))
    layers.append(((ax - 1.32).above(0.0, 0.05), dark))
    # neckline: an open V or U neck, trimmed, with skin showing inside
    front = Y.below(0.05, 0.08)
    if p["neckline"] == "v":
        cut = ax * 0.75 + (0.8 - Z)
        depth, band = 0.55, 0.05
    else:
        cut = (X * X / 0.3 + (Z - 0.75) * (Z - 0.75) / 0.2) * 0.55
        depth, band = 0.55, 0.06
    inside = cut.below(depth, 0.008) * front
    layers.append(((cut - depth - band * 0.5).abs().below(band * 0.5, 0.006) * front, trim))
    fx, fz = X - 0.58, Z - 0.36
    box = (fx.abs().max(fz.abs())).below(0.105, 0.006) * Y.below(-0.2, 0.05)
    inner = (fx.abs().max(fz.abs())).below(0.09, 0.006) * Y.below(-0.2, 0.05)
    layers.append((box, (0.02, 0.02, 0.02)))
    layers.append((inner, (0.92, 0.92, 0.9)))
    layers.append((inner * (fz - fx).above(0.0, 0.004), srgb_to_lin((0.95, 0.42, 0.05))))
    color = paint_layers(nt, base, layers)
    nt.links.new(color, bsdf.inputs["Base Color"])
    # chest skin inside the neckline
    sk = nt.nodes.new("ShaderNodeBsdfPrincipled")
    sk.inputs["Base Color"].default_value = (*srgb_to_lin(tuple(v * 0.97 for v in p["skin"])), 1)
    sk.inputs["Roughness"].default_value = 0.5
    sk.inputs["Subsurface Weight"].default_value = 0.3
    sk.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
    sk.inputs["Subsurface Scale"].default_value = 0.06
    if "mh_V" in p:
        sk = nt.nodes.new("ShaderNodeBsdfTransparent")    # the real neck and chest show through
    out = nt.nodes["Material Output"]
    mixsh = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(inside.out, mixsh.inputs[0])
    nt.links.new(bsdf.outputs[0], mixsh.inputs[1])
    nt.links.new(sk.outputs[0], mixsh.inputs[2])
    nt.links.new(mixsh.outputs[0], out.inputs[0])
    vor2 = nt.nodes.new("ShaderNodeTexVoronoi")
    vor2.inputs["Scale"].default_value = 260.0
    bump = node(nt, "ShaderNodeBump", Strength=0.12)
    nt.links.new(vor2.outputs["Distance"], bump.inputs["Height"])
    wr = node(nt, "ShaderNodeTexWave", Scale=3.0, Distortion=2.0)
    bump2 = node(nt, "ShaderNodeBump", Strength=0.1)
    nt.links.new(wr.outputs[1], bump2.inputs["Height"])
    nt.links.new(bump.outputs[0], bump2.inputs["Normal"])
    nt.links.new(bump2.outputs[0], bsdf.inputs["Normal"])
    return mat


# ---------- hair ----------

def to_sphere(pt, p):
    return Vector((pt.x / (0.68 * p["face_w"]), pt.y / 0.88, pt.z)).normalized()


def head_radius(pt, p):
    """Distance from the head's centre to its skin in the direction of pt."""
    if "mh_bvh" in p:
        return mh_ray(p["mh_bvh"], pt).length
    return head_shape(to_sphere(pt, p), p).length


def tangent(v, pos):
    n = pos.normalized()
    return v - n * v.dot(n)


class Style:
    """How one hair style grows. Every function gets the root (a point on the scalp)."""

    def __init__(self, front, side_low=0.0, back_low=-0.45, sideburn=0.05):
        self.front = front            # hairline height at the face, front(x)
        self.side_low = side_low      # hairline height above the ears
        self.back_low = back_low      # hairline height at the neck
        self.sideburn = sideburn


def hairline(p, st, x, y):
    side = smooth(0.42 * p["face_w"], 0.72 * p["face_w"], abs(x))
    if "mh_bvh" in p:
        side = smooth(0.52 * p["face_w"], 0.74 * p["face_w"], abs(x))   # the temples are further in
    back = smooth(-0.2, 0.4, y)
    limit = st.front(x) * (1 - side) + st.side_low * side
    return limit * (1 - back) + st.back_low * back


def scalp_roots(p, st, count, rnd):
    pts = []
    golden = math.pi * (3 - math.sqrt(5))
    for i in range(count):
        z = 1 - (i + 0.5) / count * 2
        r = math.sqrt(1 - z * z)
        th = golden * i + rnd.uniform(-0.05, 0.05)
        d = Vector((r * math.cos(th), r * math.sin(th), z))
        s = head_shape(d, p)
        if p.get("mh_ear") is not None:
            near = p["mh_ear"].find_nearest(s, 0.02)
            if near[0] is not None:
                continue
            if s.y < -0.2 and s.z < st.front(0.0) - 0.1 and abs(s.x) < 0.66 * p["face_w"]:
                continue    # no hair on the temples in front, below the forehead hairline
        # no hair in front of the ears, below the sideburns
        if abs(s.x) > 0.6 * p["face_w"] and -0.05 > s.y > -0.4 and s.z < st.sideburn:
            continue
        lim = hairline(p, st, s.x, s.y)
        if s.z >= lim:
            # thinner right at the hairline, so it fades into the skin instead of ending in a hard line
            if "mh_bvh" in p and rnd.random() > 0.35 + 0.65 * smooth(0.0, 0.06, s.z - lim):
                continue
            pts.append(s)
    return pts


def add_waves(pts, root, amp, wavelength=0.55):
    """Soft waves along a hanging lock. Neighbouring locks share the phase so they wave together."""
    a = resample(pts, max(80, len(pts)))
    seg = np.linalg.norm(np.diff(a, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    phase = noise.noise(root * 1.5) * 2.0
    out = []
    for q, d in zip(a, s):
        k = smooth(0.0, 0.25, d) * min(1.0, 0.4 + d)
        if q[2] > -0.1:
            k = 0.0
        radial = Vector((q[0], q[1] - 0.15, 0.0))
        radial = radial.normalized() if radial.length > 1e-6 else Vector((1, 0, 0))
        side = Vector((-radial.y, radial.x, 0.0))
        w = math.sin(d / wavelength * 2 * math.pi + phase)
        out.append(Vector(q) + (radial * 0.7 + side * 0.5) * amp * k * w)
    return out


def torso_y(p, x, z):
    """Front and back of the torso (y) at x, z, or None above or beside it. Mirrors make_neck_and_body."""
    if "mh_body" in p:
        a = p["mh_body"].ray_cast(Vector((x, -3.0, z)), Vector((0, 1, 0)), 6.0)[0]
        b = p["mh_body"].ray_cast(Vector((x, 3.0, z)), Vector((0, -1, 0)), 6.0)[0]
        if a is None or b is None:
            return None
        return a.y - 0.02, b.y + 0.02
    zl = z + 1.78
    xs = x / p["shoulders"]
    zs = zl / (0.72 + 0.2 * gauss(x * x, 0.45)) if zl > 0 else zl / 2.0
    q = 1 - xs * xs - zs * zs
    if q <= 0:
        return None
    d = 0.6 * math.sqrt(q)
    return 0.18 - d, 0.18 + d


def grow(p, root, length, h, field, rnd, steps=14, hang_below=None, rise=3.0, face_guard=None, wobble=1.0,
         drape=0, layer=0.0, stand=0.6):
    """Walk a strand from its root along field(pos, t), keeping it h above the skin.

    Hair stands off the scalp quickly (rise) and keeps that height, which is what gives a
    style its volume. Below hang_below the strand hangs freely beside the face and neck.
    face_guard: the lowest a strand may come in front of the face (a fringe cut line)."""
    n = root.normalized()
    pts = [root - n * 0.01]
    pos = root
    seg = length / steps
    wob = (noise.noise_vector(root * 2.0) * 0.3 + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * 0.05) * wobble
    for k in range(steps):
        t = (k + 1) / steps
        d = (field(pos, t) + wob * (0.3 + 0.5 * t)).normalized()
        if k == 0:
            d = (d + n * stand).normalized()
        new = pos + d * seg
        lift = h * min(1.0, 0.15 + rise * t)
        if "mh_hair_bvh" in p and (hang_below is None or new.z > hang_below):
            pass
        elif hang_below is None or new.z > hang_below:
            r_head = head_radius(new, p)
            if new.length < r_head + lift:
                new = new.normalized() * (r_head + lift)
        else:
            # beside the face, then closer in beside the neck
            min_x = (0.62 * p["face_w"] + lift) if new.z > -0.75 else (0.5 + lift)
            if abs(new.x) < min_x and new.y < 0.35:
                new.x = math.copysign(min_x, new.x if new.x != 0 else 1)
            if new.z > -0.95 or drape <= 0:
                new.y = max(new.y, -0.3)
            ty = torso_y(p, new.x, new.z - lift)
            if ty is not None:
                front, back = ty
                if drape > 0:
                    # lies over the shoulder and down the front of the chest
                    new.y = min(new.y, front - lift * 0.6)
                elif new.y < back + lift:
                    # falls down the back, behind the shoulder
                    new.y = back + lift
        if face_guard is not None and new.y < -0.3 and abs(new.x) < 0.55 * p["face_w"] and new.z < face_guard:
            new.z = face_guard
        if "mh_hair_bvh" in p:
            # on the head the hair keeps its height over the skin (it hugs the head, never inside it);
            # below hang_below it only stays out of the skin
            # hair from further up lies on top of the hair that grows lower down, so a strand
            # stands further off the head the further it has come over the scalp (layer)
            hugging = hang_below is None or new.z > hang_below
            new = collide(p, new, lift + layer * seg * (k + 1), hug=hugging)
            # a push out along the neck must not stretch the strand (stray long hairs at the nape)
            step = new - pos
            if step.length > seg * 1.5:
                new = pos + step.normalized() * seg * 1.5
        pts.append(new)
        pos = new
    return pts


def drape(p, strands, hang_below, front, gaps, n=40, iters=36):
    """Hair below hang_below hangs free: gravity pulls it down, it keeps its length and slides off the
    head, neck and shoulders. front[i]: that lock falls forward over the shoulder, else down the back."""
    bvh = p["mh_hair_bvh"]
    P = np.stack([resample(s, n) for s in strands])                      # (g, n, 3)
    g = len(P)
    seg = np.linalg.norm(np.diff(P, axis=1), axis=2)                    # (g, n-1)
    below = P[:, :, 2] < hang_below
    first = np.where(below.any(axis=1), below.argmax(axis=1), n - 1)
    free = np.arange(n)[None, :] >= np.maximum(first, 2)[:, None]
    front = np.asarray(front, bool)
    gaps = np.asarray(gaps)
    step = seg.mean() * 0.35
    for it in range(iters):
        D = np.zeros_like(P)
        D[:, :, 2] -= step
        low = P[:, :, 2] < -0.85
        D[:, :, 1] += np.where(front[:, None], -0.45, 0.45) * step * low
        D[:, :, 0] += np.sign(P[:, :, 0]) * 0.25 * step * (P[:, :, 2] > -0.9)   # clear of the face
        P = np.where(free[:, :, None], P + D, P)
        for i in range(1, n):
            d = P[:, i] - P[:, i - 1]
            d /= np.linalg.norm(d, axis=1, keepdims=True) + 1e-9
            P[:, i] = np.where(free[:, i, None], P[:, i - 1] + d * seg[:, i - 1, None], P[:, i])
        if it % 3 == 2 or it == iters - 1:
            for j in range(g):
                for i in np.nonzero(free[j])[0]:
                    P[j, i] = collide(p, Vector(P[j, i]), gaps[j])
    return [[Vector(q) for q in s] for s in P]


def collide(p, q, gap, hug=False):
    """Keep q at least gap outside the skin (exactly gap over the head when hug)."""
    if q.z > -0.95:
        r = mh_ray(p["mh_bvh"], q).length
        if hug:
            gap = max(gap, p.get("shell_h", 0.0) + 0.006)
        if q.length < r + gap or hug:
            q = q.normalized() * (r + gap)
    if q.z < -0.7:
        near, nrm, _, _ = p["mh_hair_bvh"].find_nearest(q)
        if near is not None and (q - near).dot(nrm) < gap:
            q = near + nrm * gap
    return q


def keep_outside(p, C, gap):
    """Lift every hair point that ended up under the scalp (blending guides cuts corners) back out.
    Uses a table of the head's radius in every direction."""
    if "rad_tab" not in p:
        th = np.linspace(0, math.pi, 97)
        ph = np.linspace(-math.pi, math.pi, 193)
        tab = np.zeros((len(th), len(ph)))
        for i, a in enumerate(th):
            for j, b in enumerate(ph):
                d = Vector((math.sin(a) * math.cos(b), math.sin(a) * math.sin(b), math.cos(a)))
                tab[i, j] = mh_ray(p["mh_bvh"], d).length
        p["rad_tab"] = tab
    tab = p["rad_tab"]
    P = C.reshape(-1, 3)
    r = np.linalg.norm(P, axis=1) + 1e-9
    i = np.clip(np.rint(np.arccos(np.clip(P[:, 2] / r, -1, 1)) / math.pi * 96).astype(int), 0, 96)
    j = np.clip(np.rint((np.arctan2(P[:, 1], P[:, 0]) + math.pi) / (2 * math.pi) * 192).astype(int), 0, 192)
    need = tab[i, j] + gap
    # only small lifts: towards the neck the table radius can hit the shoulders far away,
    # and pushing a nape hair out there drew long stray lines beside the neck
    m = (P[:, 2] > -0.9) & (r < need) & (need - r < 0.1)
    P[m] *= (need[m] / r[m])[:, None]
    return P.reshape(C.shape)


def push_out(p, q, gap):
    near, nrm, _, _ = p["mh_hair_bvh"].find_nearest(q)
    if near is not None and (q - near).dot(nrm) < gap:
        return near + nrm * gap
    return q


def smooth_line(pts, iters):
    """Take the kinks out of a grown strand (the root stays where it is)."""
    a = np.asarray([tuple(q) for q in pts], dtype=np.float64)
    for _ in range(iters):
        a[1:-1] = 0.5 * a[1:-1] + 0.25 * (a[:-2] + a[2:])
    return [Vector(q) for q in a]


def resample(pts, n):
    """Spread a polyline evenly over n points."""
    a = np.asarray([tuple(q) for q in pts], dtype=np.float64)
    seg = np.linalg.norm(np.diff(a, axis=0), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)])
    if s[-1] <= 0:
        return np.repeat(a[:1], n, axis=0)
    u = np.linspace(0, s[-1], n)
    return np.stack([np.interp(u, s, a[:, i]) for i in range(3)], axis=1)


def children(guides, normals, per, spread, clump, rnd_seed, frizz=0.0, curl=0.0, curl_r=0.0, curl_turns=0.0):
    """Many hairs round each guide: offset at the root, drawn together towards the tip."""
    rng = np.random.default_rng(rnd_seed)
    G = np.asarray(guides)                       # (g, n, 3)
    N = np.asarray(normals)                      # (g, 3)
    g, n, _ = G.shape
    per = max(1, int(round(per * CHILD_SCALE * DENSITY)))
    idx = np.repeat(np.arange(g), per)
    m = len(idx)
    t = np.linspace(0, 1, n)
    # random offsets in the plane of the scalp
    a = rng.normal(size=(m, 3))
    nn = N[idx]
    a -= nn * np.sum(a * nn, axis=1, keepdims=True)
    a /= np.linalg.norm(a, axis=1, keepdims=True) + 1e-9
    r = spread * np.sqrt(rng.uniform(0, 1, m))
    off = a * r[:, None]
    shrink = 1 - clump * t ** 0.9
    C = G[idx] + off[:, None, :] * shrink[None, :, None]
    if INTERP and g >= 30:
        # interpolated children: each hair is a blend of its guide and two neighbouring guides that
        # go the same way, so the hair is a continuous mass and not separate ropes
        R0 = G[:, 0]
        D = np.linalg.norm(R0[:, None, :] - R0[None, :, :], axis=2)
        k = min(7, g - 1)
        nb = np.argsort(D, axis=1)[:, 1:k + 1]                             # (g, k)
        cd = np.linalg.norm(G[:, None, :, :] - G[nb], axis=3).mean(axis=2)  # mean distance of the curves
        ok = cd < 0.04 + 1.6 * np.take_along_axis(D, nb, axis=1)
        pick = rng.integers(0, k, (m, 2))
        n1, n2 = nb[idx, pick[:, 0]], nb[idx, pick[:, 1]]
        o1, o2 = ok[idx, pick[:, 0]], ok[idx, pick[:, 1]]
        # long hair blends smoothly; short hair keeps more of its own guide, for texture
        glen = np.linalg.norm(np.diff(G, axis=1), axis=2).sum(axis=1)
        longness = np.clip((glen - 0.4) / 0.6, 0, 1)[idx]
        w = rng.dirichlet((2.0, 1.0, 1.0), m)
        w[:, 1] *= o1 * (0.35 + 0.65 * longness)
        w[:, 2] *= o2 * (0.35 + 0.65 * longness)
        w /= w.sum(axis=1, keepdims=True)
        blend = w[:, 0, None, None] * G[idx] + w[:, 1, None, None] * G[n1] + w[:, 2, None, None] * G[n2]
        pull = (clump * (0.9 - 0.4 * longness))[:, None, None] * t[None, :, None] ** 1.5
        C = blend * (1 - pull) + G[idx] * pull + off[:, None, :] * 0.25 * (1 - t[None, :, None] * 0.5)
    if frizz:
        ph = rng.uniform(0, 6.3, (m, 3))
        fr = rng.uniform(4, 9, (m, 1))
        C += frizz * (t[None, :, None] ** 1.5) * np.sin(fr[:, :, None] * t[None, :, None] * 6.28 + ph[:, None, :])
    if curl:
        # spiral each hair round its guide
        tang = np.gradient(G[idx], axis=1)
        tang /= np.linalg.norm(tang, axis=2, keepdims=True) + 1e-9
        u = np.cross(tang, nn[:, None, :])
        u /= np.linalg.norm(u, axis=2, keepdims=True) + 1e-9
        v = np.cross(tang, u)
        ph = rng.uniform(0, 6.3, (m, 1))
        ang = ph + t[None, :] * curl_turns * 6.283 * rng.uniform(0.85, 1.15, (m, 1))
        amp = curl_r * np.minimum(1, t * 5)[None, :, None] * rng.uniform(0.6, 1.2, (m, 1, 1))
        C += amp * (u * np.cos(ang)[:, :, None] + v * np.sin(ang)[:, :, None]) * curl
    return np.concatenate([G, C], axis=0)


def scalp_shell(p, st, mat, thick=0.03, shell_extra=None):
    """A hair-coloured shell under the strands. Thicker where the hair is fuller."""
    p["shell_h"] = max(p.get("shell_h", 0.0), thick)
    cap = sphere("HairCap", 128, 96)
    bm = bmesh.new()
    bm.from_mesh(cap.data)
    drop = []
    for v in bm.verts:
        hv = head_shape(v.co.copy(), p)
        lim = hairline(p, st, hv.x, hv.y)
        if abs(hv.x) > 0.6 * p["face_w"] and -0.05 > hv.y > -0.4 and hv.z < st.sideburn:
            lim = max(lim, st.sideburn)
        extra = shell_extra(hv) if shell_extra else 0.0
        if "mh_bvh" in p:
            # starts a little inside the hairline, so the hairline itself is skin with thin hair over it
            t = smooth(0.03, 0.15, hv.z - lim)
            v.co = hv + hv.normalized() * ((thick + extra) * t - 0.04 * (1 - t))
            if hv.z - lim < 0.0:
                drop.append(v)
            continue
        t = smooth(-0.03, 0.12, hv.z - lim)
        v.co = hv + hv.normalized() * ((thick + extra) * t - 0.015 * (1 - t))
    if drop:
        bmesh.ops.delete(bm, geom=drop, context="VERTS")
    bm.to_mesh(cap.data)
    bm.free()
    cap.data.materials.append(mat)
    subsurf(cap, 1)
    return cap


def fade_sides(p, front, rnd, seed, hair, zmax):
    """Very short hair on the sides and back, shortest at the bottom (a fade)."""
    st = Style(front, side_low=0.03, back_low=-0.45, sideburn=-0.02)
    roots = [r for r in scalp_roots(p, st, 9000, rnd) if r.z < zmax or r.y > 0.55]
    guides = []
    for r in roots:
        n = r.normalized()
        L = 0.006 + 0.03 * smooth(-0.3, zmax, r.z)
        d = tangent(Vector((0.2 * r.x, 0.4, -0.6)), r).normalized()
        guides.append(resample([r - n * 0.004, r + n * L * 0.6 + d * L * 0.5, r + n * L + d * L], 4))
    return hair_object("Fade", children(guides, [r.normalized() for r in roots], 3, 0.015, 0.0, seed + 11),
                       hair, 0.0033, 0.5)


def style_for(p):
    """Build guide strands for the person's style. Returns (objects, volume)."""
    style = p["hair"]
    fw = p["face_w"]
    rnd = random.Random(p["seed"] * 31 + 7)
    hair = p["hair_mat"]
    scalp = p["scalp_mat"]
    seed = p["seed"] * 977 + (1 if p["woman"] else 0)
    objs = []
    if style == "bald":
        # hair only round the back and sides, cut short
        st = Style(lambda x: 2.0, side_low=0.05, back_low=-0.4)
        st.front = lambda x: 0.35 if abs(x) > 0.45 else 2.0
        roots = [r for r in scalp_roots(p, st, 9000, rnd) if r.z < 0.35 or r.y > 0.45 and r.z < 0.5]
        guides = [resample([r, r + r.normalized() * 0.01 + Vector((0, 0.01, -0.02))], 4) for r in roots]
        objs.append(hair_object("Hair", children(guides, [r.normalized() for r in roots], 2, 0.012, 0.0, seed),
                                hair, 0.004, 0.5))
        return objs, 0.0
    if style == "buzz":
        st = Style(lambda x: 0.5 + (0.09 if "mh_bvh" in p else 0.0) - 0.08 * x * x, side_low=0.02, back_low=-0.45)
        objs.append(scalp_shell(p, st, scalp, 0.006))
        roots = scalp_roots(p, st, 9000, rnd)
        guides = []
        for r in roots:
            n = r.normalized()
            d = tangent(Vector((0.2 * r.x, 0.5, -0.5)), r).normalized()
            guides.append(resample([r - n * 0.004, r + n * 0.012 + d * 0.015, r + n * 0.018 + d * 0.035], 4))
        objs.append(hair_object("Hair", children(guides, [r.normalized() for r in roots], 3, 0.015, 0.0, seed),
                                hair, 0.0035, 0.5))
        return objs, 0.01

    if style == "curly":
        woman = p["woman"]
        top = rnd.uniform(0.18, 0.28) if not woman else rnd.uniform(0.3, 0.42)
        dzc = 0.09 if "mh_bvh" in p else 0.0
        st = Style(lambda x: 0.46 + dzc - 0.1 * x * x, side_low=0.0 if not woman else -0.1, back_low=-0.4)
        if "mh_bvh" in p:
            objs.append(scalp_shell(p, st, scalp, 0.02, lambda hv: top * 0.45 * max(0.0, hv.z - 0.3)))
        else:
            objs.append(scalp_shell(p, st, scalp, 0.04, lambda hv: top * 0.55 * max(0.0, hv.z + 0.2)))
        roots = scalp_roots(p, st, 1500, rnd)
        guides, normals = [], []
        for r in roots:
            n = (r.normalized() + Vector((0, 0.1, 0.3))).normalized()
            L = top * (0.55 + 0.45 * smooth(-0.4, 0.8, r.z)) * rnd.uniform(0.85, 1.15)
            if r.y < -0.3 and r.z < 0.5:
                L *= 0.5
            pts = [r - n * 0.01] + [r + n * L * tt + Vector((0, 0, -0.08 * tt * tt * (1 if woman else 0.3))) for tt in np.linspace(0.1, 1, 9)]
            guides.append(resample(pts, 12))
            normals.append(n)
        strands = children(guides, normals, 14, 0.03, 0.2, seed, curl=1.0, curl_r=0.025, curl_turns=3.5)
        objs.append(hair_object("Hair", strands, hair, 0.005, 0.6))
        return objs, top * 0.7

    # ---- combed styles: a field says which way the hair lies ----
    part_x = None
    H = 0.06           # how far the hair stands off the head (volume)
    base_len = 0.3
    hang_below = None
    face_guard = None
    shell = 0.02
    per, spread, clump, frizz = 12, 0.03, 0.55, 0.004
    curl = 0.0
    length = None      # optional custom length(root)
    lift = None        # optional custom volume(root)
    count = 2600
    tie = None
    sleek = 1.0        # how much neighbouring locks wander (low = straight, smooth hair)
    drape_front = False   # long hair: the front part falls over the shoulders onto the chest
    wave_amp = 0.0        # soft waves in long hair (whole locks wave together)
    stand = 0.6           # how steeply hair leaves the scalp at the root
    layer = 0.12          # how much thicker the hair gets per unit it has run over the scalp
    sw = rnd.choice((-1, 1))      # which side the hair is swept to

    if style in ("side", "undercut", "long_side"):
        part_x = sw * rnd.uniform(0.22, 0.32) * fw
    elif style in ("middle", "long", "wavy", "lob", "bob", "half_up", "long_m", "curtain", "shag"):
        part_x = rnd.uniform(-0.04, 0.04)

    dz = 0.09 if "mh_bvh" in p else 0.0     # the MakeHuman forehead is higher
    if not p["woman"]:
        front = lambda x: 0.47 + dz - 0.06 * x * x - (0.05 * smooth(0.15, 0.4, abs(x)) if p["recede"] else 0)
        st = Style(front, side_low=0.03, back_low=-0.45, sideburn=-0.02)
    else:
        front = lambda x: 0.5 + dz - 0.15 * x * x
        st = Style(front, side_low=-0.05, back_low=-0.5, sideburn=0.08)

    def short_sides(r, top_len, side_len):
        """Length for a men's cut: long on top, shorter on the sides and back."""
        top = smooth(0.35, 0.6, r.z)
        return side_len + (top_len - side_len) * top

    if style == "side":
        top_len = rnd.uniform(0.4, 0.5)
        H = rnd.uniform(0.1, 0.14)

        def field(pos, t):
            if pos.z > 0.42 and (pos.x - part_x) * sw > -0.02:
                # the big side: up and over, away from the parting and slightly back
                return tangent(Vector((-sw * 1.0, 0.3, 0.5 * (1 - t) - 0.2)), pos)
            if pos.z > 0.45:
                return tangent(Vector((sw * 0.9, 0.35, -0.1)), pos)
            return tangent(Vector((0.25 * math.copysign(1, pos.x), 0.45, -1.0)), pos)
        length = lambda r: short_sides(r, top_len, 0.15)
        lift = lambda r: H * smooth(0.2, 0.6, r.z) + 0.02

    elif style == "undercut":
        top_len = rnd.uniform(0.4, 0.5)
        H = 0.1
        st.side_low = 0.42
        st.back_low = 0.3
        objs.append(scalp_shell(p, Style(front, 0.03, -0.45, -0.02), scalp, 0.008))

        def field(pos, t):
            return tangent(Vector((-sw * 0.8, 0.6, 0.6 * (1 - t) - 0.3 * t)), pos)
        length = lambda r: top_len
        lift = lambda r: H

    elif style == "middle":
        top_len = rnd.uniform(0.45, 0.6)
        H = rnd.uniform(0.06, 0.09)
        face_guard = 0.3

        def field(pos, t):
            s = math.copysign(1, pos.x - part_x)
            if pos.z > 0.35:
                # curtains: from the parting out to the temples
                return tangent(Vector((s * 1.0, -0.15 if pos.y < 0 else 0.4, -0.55)), pos)
            return tangent(Vector((0.2 * s, 0.4, -1.0)), pos)
        length = lambda r: short_sides(r, top_len, 0.2)
        lift = lambda r: H * smooth(0.1, 0.6, r.z) + 0.02

    elif style == "mullet":
        top_len = rnd.uniform(0.25, 0.32)
        back_len = rnd.uniform(0.75, 1.0)
        H = rnd.uniform(0.06, 0.09)
        hang_below = -0.15
        st.back_low = -0.65
        perm = rnd.random() < 0.2
        if perm:
            curl = 1.0

        def field(pos, t):
            if pos.z > 0.45 and pos.y < 0.35:
                # modern mullet: textured top swept up and to one side, no straight fringe
                nz = noise.noise_vector(pos * 3.0 + Vector((p["seed"], 0, 0)))
                return tangent(Vector((-sw * 0.7 + 0.3 * pos.x, -0.15, 0.45)) + nz * 0.4, pos)
            if pos.y > 0.15:
                return Vector((0.25 * pos.x, 0.25, -1.0))
            return tangent(Vector((0.3 * math.copysign(1, pos.x), 0.6, -1.0)), pos)

        def length(r):
            back = smooth(0.0, 0.5, r.y) * smooth(0.65, 0.2, r.z)
            side = 0.18 if r.y < 0.0 else 0.25
            return max(top_len if r.z > 0.45 else side, back_len * back)
        lift = lambda r: H * (0.6 + 0.4 * smooth(0.2, 0.6, r.z))
        frizz = 0.008

    elif style == "quiff":
        H = rnd.uniform(0.12, 0.17)

        def field(pos, t):
            if pos.y < -0.05 and pos.z > 0.42:
                return tangent(Vector((0, 0.15, 1.0)), pos) * (1 - t) + Vector((0, 0.9, 0.1)) * t
            if pos.z > 0.5:
                return tangent(Vector((0.1 * pos.x, 1.0, 0.2)), pos)
            return tangent(Vector((0.3 * math.copysign(1, pos.x), 0.5, -1.0)), pos)
        length = lambda r: short_sides(r, 0.4, 0.13)
        lift = lambda r: H * smooth(0.35, 0.65, r.z) * (1.2 if r.y < 0 else 0.6) + 0.015

    elif style == "crop":
        H = rnd.uniform(0.05, 0.07)
        face_guard = 0.42
        fringe = rnd.uniform(0.0, 1.0)

        def field(pos, t):
            if pos.z > 0.4:
                return tangent(Vector((0.2 * pos.x, -1.0, -0.1 + 0.2 * fringe)), pos)
            return tangent(Vector((0.3 * math.copysign(1, pos.x), 0.3, -1.0)), pos)
        length = lambda r: short_sides(r, 0.3, 0.12)
        lift = lambda r: H * smooth(0.3, 0.6, r.z) + 0.012
        frizz = 0.01
        clump = 0.7

    elif style == "slick":
        H = rnd.uniform(0.04, 0.06)

        def field(pos, t):
            return tangent(Vector((0.15 * pos.x, 1.0, -0.15 - 0.8 * smooth(0.3, -0.2, pos.z))), pos)
        length = lambda r: short_sides(r, 0.5, 0.2)
        # some volume at the front where it is pushed back, flat over the crown
        lift = lambda r: H * smooth(0.0, 0.6, r.z) + 0.05 * smooth(0.4, 0.6, r.z) * smooth(0.1, -0.3, r.y) + 0.015
        clump = 0.4

    elif style == "messy":
        H = rnd.uniform(0.08, 0.12)
        face_guard = 0.33

        def field(pos, t):
            nz = noise.noise_vector(pos * 3.0 + Vector((p["seed"], 0, 0)))
            if pos.z > 0.4:
                return tangent(Vector((0.3 * pos.x, -0.5, 0.15)) + nz * 0.7, pos)
            return tangent(Vector((0.4 * math.copysign(1, pos.x), 0.4, -1.0)) + nz * 0.35, pos)
        length = lambda r: short_sides(r, rnd.uniform(0.36, 0.46), 0.2)
        lift = lambda r: H * smooth(0.0, 0.6, r.z) + 0.02
        frizz = 0.006
        clump = 0.5

    elif style in ("fade_crop", "fluffy"):
        # skin fade on the sides, the length on top
        st.side_low = 0.4
        st.back_low = 0.28
        objs.append(scalp_shell(p, Style(front, 0.03, -0.45, -0.02), scalp, 0.006))
        objs.append(fade_sides(p, front, rnd, seed, hair, 0.45))
        if style == "fade_crop":
            H = rnd.uniform(0.05, 0.08)
            face_guard = 0.42

            def field(pos, t):
                return tangent(Vector((0.25 * pos.x, -1.0, 0.1)), pos)
            length = lambda r: 0.26 + 0.06 * smooth(0.5, 0.8, r.z)
            frizz, clump = 0.012, 0.7
        else:
            H = rnd.uniform(0.1, 0.14)
            face_guard = 0.36
            curl = 1.0

            def field(pos, t):
                return tangent(Vector((0.3 * pos.x, -0.7, 0.4)), pos)
            length = lambda r: 0.3
            frizz, clump = 0.006, 0.65
        lift = lambda r: H

    elif style == "flow":
        # medium length, swept back, curling out at the collar
        H = rnd.uniform(0.07, 0.1)
        hang_below = -0.15
        st.back_low = -0.55

        def field(pos, t):
            if pos.z > 0.2:
                return tangent(Vector((0.35 * pos.x, 1.0, -0.2)), pos)
            return Vector((0.3 * math.copysign(1, pos.x), 0.35, -1.0))
        length = lambda r: 0.45 + 0.25 * smooth(0.3, 0.8, r.z)
        lift = lambda r: H * (0.5 + 0.5 * smooth(0.0, 0.6, r.z))
        frizz, clump = 0.006, 0.6

    elif style in ("long", "long_side", "wavy", "lob", "bob", "long_m", "half_up", "curtain", "shag"):
        end_z = {"long": -1.95, "long_side": -1.9, "wavy": -1.85, "lob": -1.15, "bob": -0.6,
                 "long_m": -0.9, "half_up": -1.8, "curtain": -1.85, "shag": -1.2}[style] + rnd.uniform(-0.12, 0.12)
        drape_front = style in ("long", "long_side", "wavy", "curtain", "half_up", "shag")
        hang_below = -0.05
        H = rnd.uniform(0.035, 0.055)
        sleek = 0.3
        layer = 0.2
        st.back_low = -0.55
        if style == "wavy" or style == "shag":
            curl = 1.0
        if style == "shag":
            frizz = 0.012
        curtain = style == "curtain"
        if curtain:
            face_guard = 0.2
        if style == "shag" or style in ("bob", "lob") and rnd.random() < 0.6:
            face_guard = 0.33   # with a fringe
            fringe = True
        else:
            fringe = False
        if style == "half_up":
            tie = Vector((0, 0.82, 0.42))

        def field(pos, t):
            s = math.copysign(1, pos.x - part_x)
            if fringe and pos.y < -0.25 and pos.z > 0.35:
                return tangent(Vector((0.15 * pos.x, -0.8, -1.0)), pos)
            if curtain and pos.y < -0.25 and pos.z > 0.1 and abs(pos.x) < 0.5:
                # curtain bangs: from the middle parting out over the temples
                return tangent(Vector((s * 1.0, -0.3, -0.75)), pos)
            if pos.z > 0.3 and pos.y < 0.45:
                return tangent(Vector((s, 0.1, -0.45)), pos)
            if pos.y > 0.45 and pos.z > 0:
                return tangent(Vector((0.15 * s, 0.35, -1)), pos)
            if pos.z < -0.3:
                # gravity, drawn in a little towards the neck
                return Vector((-0.08 * math.copysign(1, pos.x), 0.0, -1))
            return Vector((0.1 * s, 0.1 if pos.y > -0.1 else 0.25, -1))

        def length(r):
            if fringe and r.y < -0.25 and r.z > 0.35:
                return 0.38 if style != "shag" else 0.34 * rnd.uniform(0.8, 1.15)
            if curtain and r.y < -0.3 and r.z > 0.38:
                return rnd.uniform(0.5, 0.65)
            full = (r.z - end_z) * rnd.uniform(0.95, 1.08) + 0.2
            if style == "shag":
                full *= rnd.uniform(0.55, 1.0)     # choppy layers
            return full
        # flat at the parting, fuller further out
        lift = lambda r: H * (0.7 + 0.3 * smooth(0.0, 0.8, r.z)) * (0.25 + 0.75 * smooth(0.0, 0.15, abs(r.x - part_x)))
        clump = 0.3
        frizz = 0.0 if style != "shag" else 0.004
        curl = 0.0
        wave_amp = {"wavy": 0.065, "shag": 0.045}.get(style, 0.03 if p["hair_wavy"] and style != "bob" else 0.0)
        count = 3000

    elif style in ("pony", "low_pony", "bun", "messy_bun", "manbun", "braids", "pixie", "side_pony", "bubble",
                   "space_buns"):
        if style == "pixie":
            H = rnd.uniform(0.06, 0.09)
            face_guard = 0.3

            def field(pos, t):
                if pos.z > 0.4:
                    return tangent(Vector((-sw * 0.8, -0.5, -0.2)), pos)
                return tangent(Vector((0.3 * math.copysign(1, pos.x), 0.4, -1.0)), pos)
            length = lambda r: 0.18 + 0.22 * smooth(0.3, 0.7, r.z)
            lift = lambda r: H * smooth(0.0, 0.6, r.z) + 0.015
            frizz = 0.008
        else:
            H = 0.035 if style != "messy_bun" else 0.055
            pony_side = rnd.choice((-1, 1))
            if style == "pony":
                tie = Vector((0, 0.8, 0.55))
            elif style in ("side_pony", "bubble"):
                tie = Vector((pony_side * 0.3, 0.7, -0.38))
            elif style == "space_buns":
                tie = None
            elif style == "low_pony":
                tie = Vector((0, 0.78, -0.35))
            elif style == "braids":
                tie = None
            elif style == "manbun":
                tie = Vector((0, 0.62, 0.78))
            else:
                tie = Vector((0, 0.45, 0.98))

            buns = [Vector((-0.42, 0.3, 0.8)), Vector((0.42, 0.3, 0.8))]

            def target_for(pos):
                if style == "braids":
                    return Vector((math.copysign(1, pos.x + 1e-4) * 0.52, 0.55, -0.45))
                if style == "space_buns":
                    return buns[0] if pos.x < 0 else buns[1]
                return tie

            def field(pos, t):
                target = target_for(pos)
                return tangent(target - pos, pos) + (target - pos) * 0.3

            def length(r):
                return (target_for(r) - r).length * (1.25 if style == "braids" else 1.2)
            lift = lambda r: H
            clump = 0.45
            frizz = 0.006
            # pulled-back hair lies flat: no layering, or it piles up into a crest over the top
            layer = 0.05
            stand = 0.15
            if p["woman"]:
                # hair pulled back shows the real hairline: arched, lower at the temples
                st.front = lambda x: 0.53 + dz - 0.32 * x * x
            if style == "braids":
                part_x = 0.0
        count = 2800
    else:
        raise ValueError(style)

    if face_guard is not None:
        face_guard += dz
    if p["extra"] == "cap":
        # under a cap: the top is pressed flat, only the sides and the back show
        base_length, base_lift = length, lift
        length = lambda r: base_length(r) if r.z < 0.38 else 0.06
        lift = lambda r: min(base_lift(r), 0.02)

    # ---- grow the guides ----
    if not any(isinstance(o, bpy.types.Object) and o.name.startswith("HairCap") for o in objs):
        objs.append(scalp_shell(p, st, scalp, shell))
    roots = scalp_roots(p, st, count, rnd)
    guides, normals = [], []
    hanging = []
    for r in roots:
        if part_x is not None and abs(r.x - part_x) < 0.012 and r.z > 0.4 and r.y < 0.4:
            continue    # keep the parting clean
        L = length(r) * rnd.uniform(0.9, 1.1)
        h = lift(r) * rnd.uniform(0.45, 1.0)
        steps = 14 if L < 0.6 else 22
        dr = 1 if (drape_front and r.y < 0.25 and rnd.random() < 0.85) else 0
        pts = grow(p, r, L, h, field, rnd, steps if L < 1.0 else 30, hang_below=hang_below, face_guard=face_guard,
                   wobble=sleek, drape=dr, layer=layer, stand=stand)
        if style in ("pony", "low_pony", "bun", "messy_bun", "manbun", "side_pony", "bubble", "space_buns"):
            # stop at the tie
            goal = target_for(r) if style == "space_buns" else tie
            cut = next((i for i, q in enumerate(pts) if (q - goal).length < 0.06), len(pts) - 1)
            pts = pts[:max(cut + 1, 3)]
        if style == "half_up" and r.z > 0.35 and abs(r.x) < 0.45:
            # the top section pulled back to a small clip
            pts = grow(p, r, (tie - r).length * 1.2, h, lambda pos, t: tangent(tie - pos, pos) + (tie - pos) * 0.3, rnd, 12)
            cut = next((i for i, q in enumerate(pts) if (q - tie).length < 0.05), len(pts) - 1)
            pts = pts[:max(cut + 1, 3)]
        if ("mh_hair_bvh" in p and hang_below is not None and (tie is None or style == "half_up")
                and sum((b - a).length for a, b in zip(pts, pts[1:])) > 0.7):
            # long hanging hair: let it fall under gravity over the shoulders (below)
            off = max((q.length - mh_ray(p["mh_bvh"], q).length for q in pts if q.z > hang_below), default=h)
            hanging.append((len(guides), pts, r, dr, max(h, min(off, 0.15))))
            guides.append(None)
            normals.append(r.normalized())
            continue
        if len(pts) > 6:
            pts = smooth_line(pts, 6 if hang_below is not None else 2)
        if wave_amp and len(pts) > 8:
            pts = add_waves(pts, r, wave_amp)
        guides.append(resample(pts, 44 if hang_below is not None else 16))
        normals.append(r.normalized())
    if hanging:
        fallen = drape(p, [h[1] for h in hanging], hang_below, [h[3] for h in hanging], [h[4] for h in hanging])
        for (k, _, r, _, _), pts in zip(hanging, fallen):
            pts = smooth_line(pts, 3)
            if wave_amp:
                pts = add_waves(pts, r, wave_amp)
            guides[k] = resample(pts, 44)
    curl_r, curl_turns = {"wavy": (0.035, 2.0), "shag": (0.03, 2.0), "fluffy": (0.016, 3.0),
                          "mullet": (0.018, 2.5)}.get(style, (0.03, 3.5))
    strands = children(guides, normals, per, spread, clump, seed, frizz=frizz, curl=curl,
                       curl_r=curl_r, curl_turns=curl_turns)
    if "mh_bvh" in p:
        strands = keep_outside(p, strands, p.get("shell_h", 0.0) + 0.005)
    objs.append(hair_object("Hair", strands, hair, 0.0042, 0.35))

    # ---- tails, buns and braids ----
    trng = random.Random(seed + 3)
    extra = []
    if style in ("pony", "low_pony", "half_up", "side_pony", "bubble"):
        side = trng.choice((-1, 1))
        bubbles = style == "bubble"
        if style in ("side_pony", "bubble"):
            # tied low beside the neck and brought forward over the shoulder
            side = 1 if tie.x > 0 else -1
            mid, end = Vector((side * 0.74, 0.18, -0.98)), Vector((side * 0.66, -0.52, -1.8))
        elif style == "pony":
            mid, end = Vector((side * 0.15, 1.12, 0.35)), Vector((side * 0.35, 0.95, -0.85))
        elif style == "low_pony":
            if trng.random() < 0.5:
                mid, end = Vector((side * 0.72, 0.55, -0.95)), Vector((side * 0.7, -0.05, -1.75))
            else:
                mid, end = Vector((side * 0.05, 0.95, -0.85)), Vector((side * 0.1, 0.95, -1.6))
        else:
            mid, end = Vector((0, 0.95, 0.0)), Vector((side * 0.1, 0.9, -0.7))
        for i in range(500):
            off = Vector((trng.gauss(0, 0.045), trng.gauss(0, 0.045), trng.gauss(0, 0.04)))
            pts = []
            for k in range(16):
                t = k / 15
                c = tie * (1 - t) ** 2 + mid * 2 * t * (1 - t) + end * t * t
                spread_t = 1 + t * 2.2 - t * t * 1.4
                if bubbles:
                    spread_t = (0.45 + 1.3 * abs(math.sin(t * math.pi * 3.5))) * (1.2 - 0.3 * t)
                pts.append(c + off * spread_t + noise.noise_vector(c * 3 + off * 5) * 0.025)
            extra.append(resample(pts, 32 if bubbles else 16))
        objs.append(blob("Tie", p["tie_mat"], tuple(tie), (0.07, 0.06, 0.07)))
        if bubbles:
            for k in range(1, 4):
                t = k / 3.5
                c = tie * (1 - t) ** 2 + mid * 2 * t * (1 - t) + end * t * t
                objs.append(blob("Tie", p["tie_mat"], tuple(c), (0.04, 0.04, 0.035)))
        tails = children(extra, [Vector((0, 1, 0))] * len(extra), 10, 0.02, 0.5, seed + 5,
                         curl=1.0 if p["hair_wavy"] else 0.0, curl_r=0.03, curl_turns=2.5, frizz=0.005)
        objs.append(hair_object("Tail", tails, hair, 0.0042, 0.35))
    elif style in ("bun", "messy_bun", "manbun", "space_buns"):
        rb = {"manbun": 0.14, "space_buns": 0.17}.get(style, 0.2)
        messy = style == "messy_bun"
        for centre, i in [(c + c.normalized() * rb * 0.6, i)
                          for c in ([tie] if style != "space_buns" else buns) for i in range(700)]:
            axis = Vector((trng.gauss(0, 1), trng.gauss(0, 1), trng.gauss(0, 1))).normalized()
            u = axis.cross(Vector((0.3, 0.2, 1))).normalized()
            v = axis.cross(u)
            rr = trng.uniform(0.75, 1.05) * rb
            a0 = trng.uniform(0, 6.3)
            sweep = trng.uniform(1.6, 3.2)
            pts = [centre + (u * math.cos(a0 + k * sweep / 15) + v * math.sin(a0 + k * sweep / 15)) * rr * (1 + (0.25 * noise.noise(Vector((i, k * 0.2, 0))) if messy else 0))
                   for k in range(16)]
            extra.append(resample(pts, 16))
        tails = children(extra, [Vector((0, 0, 1))] * len(extra), 6, 0.015, 0.2, seed + 6, frizz=0.012 if messy else 0.003)
        for c in ([tie] if style != "space_buns" else buns):
            objs.append(blob("BunCore", p["scalp_mat"], tuple(c + c.normalized() * rb * 0.6), (rb * 0.85,) * 3))
        centre = tie + tie.normalized() * rb * 0.6 if tie is not None else None
        objs.append(hair_object("Bun", tails, hair, 0.0042, 0.5))
        if messy:
            # loose ends sticking out
            loose = []
            for i in range(120):
                d = Vector((trng.gauss(0, 1), trng.gauss(0, 1), abs(trng.gauss(0, 1)))).normalized()
                a = centre + d * rb * 0.9
                loose.append(resample([a, a + d * 0.08 + Vector((0, 0, 0.03)), a + d * 0.14], 6))
            objs.append(hair_object("Loose", children(loose, [Vector((0, 0, 1))] * len(loose), 3, 0.01, 0.6, seed + 7), hair, 0.004, 0.3))
    elif style == "braids":
        # two braids from behind the ears, hanging forward over the shoulders or down the back
        front_braids = trng.random() < 0.6
        for s in (-1, 1):
            start = Vector((s * 0.52, 0.55, -0.45))
            if front_braids:
                ctrl = [start, Vector((s * 0.66, 0.25, -0.85)), Vector((s * 0.64, -0.3, -1.2)), Vector((s * 0.6, -0.52, -1.75))]
            else:
                ctrl = [start, Vector((s * 0.55, 0.85, -0.8)), Vector((s * 0.45, 0.95, -1.3)), Vector((s * 0.4, 0.95, -1.8))]
            axis = resample(ctrl, 60)
            braid = []
            width = 0.075
            for strand in range(3):
                ph = strand * 2 * math.pi / 3
                for j in range(110):
                    jitter = np.array([trng.gauss(0, 0.012), trng.gauss(0, 0.012), trng.gauss(0, 0.012)])
                    pts = []
                    for k in range(60):
                        a = axis[k]
                        tan = axis[min(k + 1, 59)] - axis[max(k - 1, 0)]
                        tan /= np.linalg.norm(tan) + 1e-9
                        side_v = np.cross(tan, np.array([0, -1.0, 0]) if not front_braids else np.array([1.0, 0, 0]) * s)
                        side_v /= np.linalg.norm(side_v) + 1e-9
                        up_v = np.cross(side_v, tan)
                        w = width * (1 - 0.45 * k / 59)
                        u = k * 0.42 + ph
                        o = side_v * math.sin(u) * w + up_v * math.sin(2 * u) * w * 0.35
                        pts.append(a + o + jitter * (1 - 0.5 * k / 59))
                    braid.append(np.array(pts))
            # loose tuft at the end
            tip = axis[-1]
            for j in range(160):
                d = np.array([trng.gauss(0, 0.5), trng.gauss(0, 0.5), -1.0])
                d /= np.linalg.norm(d)
                a = axis[-3]
                braid.append(resample([Vector(a), Vector(tip + d * 0.08), Vector(tip + d * 0.18)], 60))
            objs.append(hair_object("Braid", children(braid, [Vector((0, 0, 1))] * len(braid), 2, 0.006, 0.5, seed + s),
                                    hair, 0.0042, 0.5))
            objs.append(blob("Tie", p["tie_mat"], tuple(axis[-4]), (0.035, 0.035, 0.03)))
    if (style in ("pony", "low_pony", "bun", "messy_bun", "braids", "side_pony", "bubble", "space_buns")
            and trng.random() < 0.7 and "mh_bvh" not in p):
        # a few loose strands framing the face
        st2 = Style(lambda x: 0.5, -0.05, -0.5)
        wisps = []
        for i in range(40):
            sd = trng.choice((-1, 1))
            d = Vector((sd * trng.uniform(0.55, 0.72), -trng.uniform(0.5, 0.7), trng.uniform(0.32, 0.5))).normalized()
            r = head_shape(d, p)
            L = trng.uniform(0.5, 0.8)
            wisps.append(resample(grow(p, r, L, 0.03, lambda pos, t, sd=sd: Vector((sd * 0.25, 0.05, -1)), trng, 12,
                                       hang_below=0.1), 16))
        objs.append(hair_object("Wisps", children(wisps, [Vector((0, -1, 0))] * len(wisps), 4, 0.01, 0.7, seed + 9, frizz=0.006),
                                hair, 0.004, 0.3))
    return objs, H


def make_beard(p, mat):
    """Full beard or moustache made of strands on the jaw and upper lip."""
    kind = p["beard"]
    if kind not in ("full", "short", "moustache", "goatee"):
        return []
    rnd = random.Random(p["seed"] * 17 + 3)
    guides, normals = [], []
    N = 7000
    for i in range(N):
        z = 1 - (i + 0.5) / N * 2
        r = math.sqrt(max(0.0, 1 - z * z))
        th = math.pi * (3 - math.sqrt(5)) * i
        s = head_shape(Vector((r * math.cos(th), r * math.sin(th), z)), p)
        if s.y > 0.18:
            continue
        sz = face_z(p, s.z)
        if "mh_V" in p and s.z < p["chin_z"] - 0.07 + 0.3 * smooth(0.15, 0.45, abs(s.x)):
            continue    # not down the neck (the jaw line rises towards the ears)
        top = -0.37 + 0.22 * smooth(0.0, 0.6, abs(s.x))        # cheek line, rising towards the ears
        moustache = abs(s.x) < 0.2 * p["mouth_w"] and -0.425 < sz < -0.355 + 0.02 * max(0.0, 1 - abs(s.x) / 0.1)
        mouth = abs(s.x) < 0.17 and -0.5 < sz < -0.41     # a narrow bare strip under the lower lip
        if kind == "moustache":
            ok = moustache
        elif kind == "goatee":
            ok = moustache or (abs(s.x) < 0.17 and -0.95 * p["len"] < sz < -0.555)
        else:
            ok = (sz < top and not mouth and sz > -1.05) or moustache
        if not ok:
            continue
        # thinner towards the cheek line and the edge under the lip, so the beard grows out of the skin
        # instead of ending in a hard edge
        edge = smooth(0.0, 0.07, top - sz) if not moustache else 1.0
        if kind in ("full", "short") and abs(s.x) < 0.17 and sz < -0.41:
            edge = min(edge, smooth(-0.5, -0.56, sz))
        if rnd.random() > 0.25 + 0.75 * edge:
            continue
        n = s.normalized()
        if moustache:
            down = tangent(Vector((math.copysign(0.6, s.x), -0.2, -1.0)), s).normalized()
        else:
            down = tangent(Vector((0.1 * s.x, -0.15, -1.0)), s).normalized()
        L = (0.085 if kind == "full" else 0.05) * rnd.uniform(0.7, 1.2) * (0.5 + 0.5 * edge)
        if moustache:
            L = 0.045 * rnd.uniform(0.8, 1.1)
        # the hairs lie along the skin, pointing down, and only stand a little off it
        lift = 1.0 if kind == "full" else 0.85
        pts = [s - n * 0.004, s + n * 0.016 * lift + down * L * 0.5, s + n * 0.024 * lift + down * L]
        guides.append(resample([Vector(q) for q in pts], 5))
        normals.append(n)
    if not guides:
        return []
    return [hair_object("Beard", children(guides, normals, 4, 0.014, 0.2, p["seed"], frizz=0.006, curl=1.0, curl_r=0.005, curl_turns=1.2), mat, 0.004, 0.4)]


# ---------- one portrait ----------

def person(seed, woman):
    rnd = random.Random(seed * 7919 + (1 if woman else 0))
    skin = rnd.choices([c for c, w in SKINS], [w for c, w in SKINS])[0]
    dark = skin[0] < 0.65
    if dark:
        hair_name = rnd.choice(["mork", "svart", "svart", "brun"])
    else:
        hair_name = rnd.choice(list(HAIRS) + ["brun", "blond", "ljus", "mork"])
    if hair_name == "gra" and rnd.random() < 0.5:
        hair_name = "ljus"
    order = style_order(WOMEN_HAIR if woman else MEN_HAIR, 2026 + woman)
    style = order[(seed - 1) % len(order)]
    style = os.environ.get("STYLE", style)
    beard = None
    if not woman:
        r = rnd.random()
        beard = ("full" if r < 0.06 else "short" if r < 0.18 else "moustache" if r < 0.26 else
                 "stubble" if r < 0.6 else None)
    extra = None
    if style in ("bun", "quiff", "curly", "manbun", "messy_bun", "pony", "half_up") and extra == "cap":
        extra = None
    if style in ("bun", "messy_bun", "manbun", "pony", "half_up", "space_buns", "fluffy") and extra == "band":
        extra = None
    # MakeHuman body: slim and fit, ancestry roughly from the skin tone
    if skin[0] < 0.6:
        race = {"african": 0.85, "caucasian": 0.15, "asian": 0.0}
    elif skin[0] < 0.75:
        a = rnd.choice((0.0, 0.0, 0.6))
        race = {"african": 0.15, "caucasian": 0.85 - a, "asian": a}
    else:
        a = rnd.choice((0.0,) * 7 + (0.5,))
        race = {"african": 0.0, "caucasian": 1.0 - a, "asian": a}
    mh = {"mh_gender": rnd.uniform(0.0, 0.12) if woman else rnd.uniform(0.88, 1.0),
          "mh_muscle": rnd.uniform(0.45, 0.65), "mh_weight": rnd.uniform(0.25, 0.5), "mh_race": race,
          "mh_sliders": mh_sliders(rnd, woman), "eye_back": rnd.uniform(0.0, 0.004), "brow_h": rnd.uniform(0.0, 0.03)}
    return {
        **mh,
        "seed": seed, "woman": woman, "skin": skin, "hair_name": hair_name, "hair_col": HAIRS[hair_name],
        "hair_rgb": HAIRS[hair_name][2], "hair": style, "hair_wavy": rnd.random() < 0.3,
        "eye": rnd.choice(EYES if not dark else EYES[2:4]),
        "jersey": JERSEYS[rnd.randrange(len(JERSEYS))], "neckline": rnd.choice(("v", "u")),
        "design": rnd.choice(("swoosh", "chevron", "split")), "design_flip": rnd.choice((-1, 1)),
        **face_shape(rnd, woman),
        "cheek": rnd.uniform(0.7, 1.2), "lean": rnd.uniform(0.3, 1.0),
        "brow": rnd.uniform(0.01, 0.022) if woman else rnd.uniform(0.045, 0.07),
        "nose": rnd.uniform(0.7, 0.88) if woman else rnd.uniform(0.95, 1.2),
        "nose_w": (rnd.uniform(0.82, 0.95) if woman else rnd.uniform(0.95, 1.08)) * (1.2 if dark else 1.0),
        "tip": rnd.uniform(-0.235, -0.205) if woman else rnd.uniform(-0.27, -0.225),
        "lips": rnd.uniform(1.15, 1.45) if woman else rnd.uniform(0.75, 1.0),
        "mouth_w": rnd.uniform(0.86, 0.98) if woman else rnd.uniform(0.95, 1.1),
        "eye_x": rnd.uniform(0.255, 0.285), "eye_r": 0.1,
        "lid": rnd.uniform(0.3, 0.38) if woman else rnd.uniform(0.24, 0.34),
        "lid_lo": rnd.uniform(0.38, 0.46), "tilt_eye": rnd.uniform(0.1, 0.5) if woman else rnd.uniform(-0.1, 0.35),
        "look": (rnd.uniform(-4, 4), rnd.uniform(-3, 2)),
        "brow_thick": rnd.uniform(0.45, 0.7) if woman else rnd.uniform(0.8, 1.1),
        "brow_raise": rnd.uniform(-0.01, 0.015),
        "lipstick": rnd.random() < 0.15,
        "recede": (not woman) and rnd.random() < 0.15,
        "neck_w": 0.84 if woman else 1.1, "shoulders": rnd.uniform(1.35, 1.5) if woman else rnd.uniform(1.6, 1.78),
        "smile": float(os.environ.get("SMILE", random.Random(seed * 13 + woman).uniform(0.45, 1.0))),
        "beard": beard, "extra": extra, "turn": rnd.uniform(-7, 7), "tilt": rnd.uniform(-2, 2),
    }


FACES_MEN = {   # face_w, len, jaw_taper, taper_pow, jaw (corner), chin, chin_w, cheek_full
    "square": ((1.04, 1.08), (0.99, 1.04), (0.27, 0.32), 2.8, (0.07, 0.1), (0.05, 0.07), 1.6, (0.0, 0.01)),
    "oval": ((1.0, 1.04), (1.02, 1.07), (0.33, 0.39), 2.3, (0.03, 0.05), (0.04, 0.06), 1.4, (0.0, 0.02)),
    "long": ((0.94, 0.98), (1.1, 1.17), (0.3, 0.35), 2.4, (0.04, 0.06), (0.05, 0.07), 1.3, (0.0, 0.01)),
    "narrow": ((0.92, 0.96), (1.05, 1.1), (0.34, 0.4), 2.2, (0.02, 0.04), (0.05, 0.07), 1.2, (0.0, 0.01)),
    "round": ((1.05, 1.1), (0.94, 0.99), (0.3, 0.36), 2.0, (0.0, 0.03), (0.03, 0.05), 1.5, (0.05, 0.08)),
    "diamond": ((0.99, 1.03), (1.02, 1.07), (0.4, 0.45), 2.0, (0.0, 0.02), (0.05, 0.07), 1.1, (0.03, 0.05)),
}
FACES_WOMEN = {
    "oval": ((0.94, 0.98), (0.96, 1.01), (0.44, 0.5), 2.0, (0.0, 0.0), (0.02, 0.035), 1.0, (0.01, 0.03)),
    "heart": ((0.97, 1.0), (0.95, 1.0), (0.52, 0.58), 1.8, (0.0, 0.0), (0.02, 0.03), 0.85, (0.02, 0.04)),
    "round": ((0.99, 1.03), (0.92, 0.96), (0.38, 0.44), 1.9, (0.0, 0.0), (0.02, 0.03), 1.1, (0.05, 0.08)),
    "long": ((0.91, 0.94), (1.04, 1.1), (0.44, 0.5), 2.2, (0.0, 0.01), (0.025, 0.04), 1.0, (0.0, 0.02)),
    "narrow": ((0.9, 0.93), (1.0, 1.05), (0.46, 0.52), 2.0, (0.0, 0.0), (0.025, 0.035), 0.95, (0.0, 0.01)),
    "square": ((0.97, 1.0), (0.96, 1.0), (0.38, 0.42), 2.6, (0.02, 0.035), (0.025, 0.04), 1.2, (0.01, 0.03)),
}


def face_shape(rnd, woman):
    """Pick a face shape (oval, round, square, heart ...) and the numbers that make it."""
    table = FACES_WOMEN if woman else FACES_MEN
    weights = FACE_WEIGHTS_W if woman else FACE_WEIGHTS_M
    name = rnd.choices(list(weights), list(weights.values()))[0]
    fw, ln, taper, pw, jaw, chin, chin_w, full = table[name]
    u = lambda r: rnd.uniform(*r)
    return {"face": name, "face_w": u(fw), "len": u(ln), "jaw_taper": u(taper), "taper_pow": pw,
            "jaw": u(jaw), "chin": u(chin), "chin_w": chin_w, "cheek_full": u(full),
            "skull_w": rnd.uniform(0.95, 1.04), "skull_h": rnd.uniform(0.95, 1.07), "skull_d": rnd.uniform(0.96, 1.08)}


# long and narrow faces look most natural, round ones are rare
FACE_WEIGHTS_M = {"long": 3, "narrow": 3, "oval": 3, "square": 1.5, "round": 0.3}
FACE_WEIGHTS_W = {"long": 3, "narrow": 3, "oval": 3, "heart": 1.5, "square": 0.7, "round": 0.4}


def render_person(p, path):
    reset()
    camera()
    if "mh_V" in p:
        # more from the side, so the face has shape (nose, cheekbones, eye sockets)
        light("Key", (-6.0, -4.8, 3.6), 1250, 3.0, (1.0, 0.95, 0.88))
        light("Fill", (5.5, -5.0, 0.5), 120, 6.0, (0.9, 0.95, 1.0))
    else:
        light("Key", (-4.5, -6.0, 3.2), 1000, 3.5, (1.0, 0.95, 0.88))
        light("Fill", (5.0, -5.5, 0.5), 200, 6.0, (0.9, 0.95, 1.0))
    light("Rim", (2.5, 5.0, 3.0), 750, 3.0, (1.0, 0.95, 0.85), target=(0, 0, 0.3))
    light("Rim2", (-3.0, 4.5, 1.5), 420, 3.0, (0.95, 1.0, 1.0), target=(0, 0, 0.3))
    light("Bounce", (0.0, -5.0, -4.0), 260, 6.0, (1.0, 0.92, 0.85), target=(0, 0, 0))

    use_mh = os.path.exists(MH_FILE) and os.environ.get("OLD_HEAD") != "1"
    if use_mh:
        mh_setup(p)
    skin = skin_material(p)
    hair = hair_material(p)
    p["hair_mat"] = hair
    p["scalp_mat"] = scalp_material(p)
    p["tie_mat"] = principled("Tie", srgb_to_lin(p["jersey"][1]), rough=0.5)[0]
    jersey = jersey_material(p)
    p["vol"] = 0.0
    hair_objs, vol = style_for(p)
    p["vol"] = vol
    face = [] if use_mh else [make_head(p, skin)] + make_ears(p, skin)
    head_parts = (face + make_eyes(p, skin, hair) + [make_brows(p, hair)]
                  + hair_objs + make_beard(p, hair) + make_extra(p))
    pivot = bpy.data.objects.new("HeadPivot", None)
    bpy.context.collection.objects.link(pivot)
    pivot.location = (0, 0.1, -0.6)
    bpy.context.view_layer.update()
    for obj in head_parts:
        obj.parent = pivot
        obj.matrix_parent_inverse = pivot.matrix_world.inverted()
    pivot.rotation_euler = (math.radians(p["tilt"]), 0, math.radians(p["turn"]))
    if use_mh:
        make_mh_body(p, skin)
        make_mh_jersey(p, jersey)
    else:
        make_neck_and_body(p, skin, jersey)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv
    args = argv[argv.index("--") + 1:] if "--" in argv else argv[1:]
    out = args[0] if args else "."
    count = int(args[1]) if len(args) > 1 else 30
    first = int(args[2]) if len(args) > 2 else 1
    only = args[3] if len(args) > 3 else None
    os.makedirs(out, exist_ok=True)
    for i in range(first, first + count):
        for woman, prefix in ((False, "Herr"), (True, "Dam")):
            if only and only != prefix:
                continue
            render_person(person(i, woman), os.path.join(os.path.abspath(out), f"{prefix}_{i:02d}.png"))
            print(prefix, i, flush=True)


if __name__ == "__main__":
    main()
