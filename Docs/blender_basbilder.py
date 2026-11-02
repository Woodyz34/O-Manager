"""Render placeholder portraits for the runner cards in Blender (3D, no photos or scans).

Every head, body and hair style is built from code here, so there is nobody to ask permission from.

    blender --background --python Docs/blender_basbilder.py -- <out folder> [count per sex] [first index]
    (or with the bpy module: python Docs/blender_basbilder.py <out folder> ...)

Writes Herr_NN.png and Dam_NN.png, 600 x 900 with a transparent background.
"""
import math
import random
import sys

import bpy
import bmesh
from mathutils import Vector, noise

# ---------- settings ----------

import os
QUICK = os.environ.get("QUICK") == "1"
RES = (300, 450) if QUICK else (600, 900)
SAMPLES = 16 if QUICK else 48

SKINS = [(0.93, 0.74, 0.62), (0.87, 0.64, 0.50), (0.78, 0.55, 0.40), (0.62, 0.41, 0.28),
         (0.45, 0.29, 0.19), (0.96, 0.80, 0.70), (0.70, 0.48, 0.33), (0.33, 0.21, 0.14)]
HAIRS = {"blond": (0.62, 0.45, 0.22), "ljus": (0.80, 0.66, 0.42), "brun": (0.24, 0.13, 0.07),
         "mork": (0.10, 0.06, 0.04), "svart": (0.03, 0.025, 0.022), "rod": (0.48, 0.16, 0.06),
         "gra": (0.55, 0.53, 0.50), "kastanj": (0.36, 0.17, 0.08)}
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
MEN_HAIR = ["short", "short", "side", "side", "buzz", "curly", "quiff", "bald"]
WOMEN_HAIR = ["pony", "pony", "bun", "long", "long", "bob", "braid", "curly", "short_w"]


def srgb_to_lin(c):
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c)


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
    sc.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.52, 0.45, 1)
    bg.inputs[1].default_value = 0.35
    return sc


def light(name, loc, energy, size, color=(1, 1, 1), target=(0, 0, 0.1)):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.size = size
    data.color = color
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
    obj.location = (0.3, -10.5, -0.5)
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
        bsdf.inputs["Subsurface Scale"].default_value = 0.08
    if sheen:
        bsdf.inputs["Sheen Weight"].default_value = sheen
    return mat, bsdf


def ellipse_mask(nt, xyz, cx, cz, rx, rz, soft, y_max=None, loc=(0, 0)):
    """1 inside an ellipse in the face's x/z plane (front only), softly fading out."""
    sx = node(nt, "ShaderNodeMath", loc, inputs={1: cx}); sx.operation = "SUBTRACT"
    nt.links.new(xyz.outputs[0], sx.inputs[0])
    dx = node(nt, "ShaderNodeMath", loc, inputs={1: rx}); dx.operation = "DIVIDE"
    nt.links.new(sx.outputs[0], dx.inputs[0])
    sz = node(nt, "ShaderNodeMath", loc, inputs={1: cz}); sz.operation = "SUBTRACT"
    nt.links.new(xyz.outputs[2], sz.inputs[0])
    dz = node(nt, "ShaderNodeMath", loc, inputs={1: rz}); dz.operation = "DIVIDE"
    nt.links.new(sz.outputs[0], dz.inputs[0])
    vec = node(nt, "ShaderNodeCombineXYZ", loc)
    nt.links.new(dx.outputs[0], vec.inputs[0]); nt.links.new(dz.outputs[0], vec.inputs[1])
    length = node(nt, "ShaderNodeVectorMath", loc); length.operation = "LENGTH"
    nt.links.new(vec.outputs[0], length.inputs[0])
    ramp = node(nt, "ShaderNodeMapRange", loc, inputs={1: 1.0 - soft, 2: 1.0, 3: 1.0, 4: 0.0})
    nt.links.new(length.outputs[1], ramp.inputs[0])
    out = ramp
    if y_max is not None:     # only on the front of the head
        front = node(nt, "ShaderNodeMapRange", loc, inputs={1: y_max, 2: y_max - 0.15, 3: 0.0, 4: 1.0})
        nt.links.new(xyz.outputs[1], front.inputs[0])
        mul = node(nt, "ShaderNodeMath", loc); mul.operation = "MULTIPLY"
        nt.links.new(ramp.outputs[0], mul.inputs[0]); nt.links.new(front.outputs[0], mul.inputs[1])
        out = mul
    return out


def skin_material(p):
    skin = srgb_to_lin(p["skin"])
    mat, bsdf = principled("Skin", skin, rough=0.48, sss=0.25)
    nt = mat.node_tree
    tex = node(nt, "ShaderNodeTexCoord", (-1400, 0))
    xyz = node(nt, "ShaderNodeSeparateXYZ", (-1200, 0))
    nt.links.new(tex.outputs["Object"], xyz.inputs[0])
    color = node(nt, "ShaderNodeRGB", (-600, 300))
    color.outputs[0].default_value = (*skin, 1)
    current = color.outputs[0]

    def layer(mask, rgb, strength):
        nonlocal current
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MIX"
        f = node(nt, "ShaderNodeMath", (-400, 0), inputs={1: strength}); f.operation = "MULTIPLY"
        nt.links.new(mask.outputs[0], f.inputs[0])
        nt.links.new(f.outputs[0], mix.inputs[0])
        nt.links.new(current, mix.inputs[6])
        mix.inputs[7].default_value = (*rgb, 1)
        current = mix.outputs[2]

    lip = srgb_to_lin(tuple(min(1, s * 0.78 + d) for s, d in zip(p["skin"], (0.16, 0.0, 0.0))))
    # lips, a little rosier on women
    lips = ellipse_mask(nt, xyz, 0.0, -0.475, 0.155, 0.055, 0.45, y_max=-0.55)
    layer(lips, lip, 0.75 if p["woman"] else 0.55)
    # cheeks
    for side in (-1, 1):
        cheek = ellipse_mask(nt, xyz, side * 0.36, -0.2, 0.16, 0.12, 0.9, y_max=-0.4)
        layer(cheek, srgb_to_lin(tuple(v * 0.85 + d for v, d in zip(p["skin"], (0.1, 0.0, 0.0)))), 0.35 if p["woman"] else 0.15)
    # eyebrows: thin arcs above the eyes
    brow = srgb_to_lin(tuple(v * 0.8 for v in p["hair_color"]))
    for side in (-1, 1):
        for i, (bx, bz) in enumerate(((0.16, 0.235), (0.27, 0.262), (0.38, 0.245))):
            m = ellipse_mask(nt, xyz, side * bx * p["face_w"], bz + 0.02, 0.08, 0.022 if p["woman"] else 0.028, 0.3, y_max=-0.2)
            layer(m, brow, 0.92)
    # stubble or beard
    if p["beard"]:
        jaw = ellipse_mask(nt, xyz, 0.0, -0.55, 0.52, 0.32, 0.25, y_max=-0.2)
        layer(jaw, srgb_to_lin(tuple(v * 0.9 for v in p["hair_color"])), 0.85 if p["beard"] == "full" else 0.32)
        if p["beard"] == "full":
            mouth = ellipse_mask(nt, xyz, 0.0, -0.475, 0.14, 0.05, 0.4, y_max=-0.55)
            layer(mouth, lip, 0.9)
    nt.links.new(current, bsdf.inputs["Base Color"])
    # fine pores
    bump = node(nt, "ShaderNodeBump", (-200, -300), Strength=0.04)
    tex_noise = node(nt, "ShaderNodeTexNoise", (-400, -300), Scale=180.0)
    nt.links.new(tex_noise.outputs[0], bump.inputs["Height"])
    nt.links.new(bump.outputs[0], bsdf.inputs["Normal"])
    return mat


def hair_material(color):
    c = srgb_to_lin(color)
    mat, bsdf = principled("Hair", c, rough=0.6, sheen=0.2)
    nt = mat.node_tree
    wave = node(nt, "ShaderNodeTexWave", (-500, -300), Scale=40.0, Distortion=6.0)
    wave.wave_type = "BANDS"
    wave.bands_direction = "Z"
    bump = node(nt, "ShaderNodeBump", (-200, -300), Strength=0.6)
    nt.links.new(wave.outputs[1], bump.inputs["Height"])
    nt.links.new(bump.outputs[0], bsdf.inputs["Normal"])
    # strands a shade lighter and darker
    ramp = node(nt, "ShaderNodeMix", (-300, 200))
    ramp.data_type = "RGBA"
    nt.links.new(wave.outputs[1], ramp.inputs[0])
    ramp.inputs[6].default_value = (*[v * 0.75 for v in c], 1)
    ramp.inputs[7].default_value = (*[min(1, v * 1.35 + 0.01) for v in c], 1)
    nt.links.new(ramp.outputs[2], bsdf.inputs["Base Color"])
    return mat


def jersey_material(colors, angle):
    base, pat, trim = (srgb_to_lin(c) for c in colors)
    mat, bsdf = principled("Jersey", base, rough=0.65, sheen=0.4)
    nt = mat.node_tree
    tex = node(nt, "ShaderNodeTexCoord", (-1200, 0))
    mapping = node(nt, "ShaderNodeMapping", (-1000, 0))
    mapping.inputs["Rotation"].default_value = (0, angle, 0)
    nt.links.new(tex.outputs["Object"], mapping.inputs[0])
    wave = node(nt, "ShaderNodeTexWave", (-800, 0), Scale=2.2, Distortion=0.0)
    wave.wave_profile = "SAW"
    nt.links.new(mapping.outputs[0], wave.inputs[0])
    stripes = node(nt, "ShaderNodeMapRange", (-600, 0), inputs={1: 0.48, 2: 0.52})
    nt.links.new(wave.outputs[1], stripes.inputs[0])
    mix = node(nt, "ShaderNodeMix", (-300, 0))
    mix.data_type = "RGBA"
    nt.links.new(stripes.outputs[0], mix.inputs[0])
    mix.inputs[6].default_value = (*base, 1)
    mix.inputs[7].default_value = (*pat, 1)
    # a zig-zag band across the chest
    sep = node(nt, "ShaderNodeSeparateXYZ", (-1000, -300))
    nt.links.new(tex.outputs["Object"], sep.inputs[0])
    zig = node(nt, "ShaderNodeMath", (-800, -300), inputs={1: 0.45}); zig.operation = "PINGPONG"
    nt.links.new(sep.outputs[0], zig.inputs[0])
    zz = node(nt, "ShaderNodeMath", (-650, -300)); zz.operation = "ADD"
    nt.links.new(zig.outputs[0], zz.inputs[0]); nt.links.new(sep.outputs[2], zz.inputs[1])
    band = node(nt, "ShaderNodeMapRange", (-500, -300), inputs={1: -1.62, 2: -1.6, 3: 0.0, 4: 1.0})
    nt.links.new(zz.outputs[0], band.inputs[0])
    band2 = node(nt, "ShaderNodeMapRange", (-500, -450), inputs={1: -1.48, 2: -1.5, 3: 0.0, 4: 1.0})
    nt.links.new(zz.outputs[0], band2.inputs[0])
    both = node(nt, "ShaderNodeMath", (-350, -350)); both.operation = "MULTIPLY"
    nt.links.new(band.outputs[0], both.inputs[0]); nt.links.new(band2.outputs[0], both.inputs[1])
    mix2 = node(nt, "ShaderNodeMix", (-150, 0))
    mix2.data_type = "RGBA"
    nt.links.new(both.outputs[0], mix2.inputs[0])
    nt.links.new(mix.outputs[2], mix2.inputs[6])
    mix2.inputs[7].default_value = (*trim, 1)
    nt.links.new(mix2.outputs[2], bsdf.inputs["Base Color"])
    # knit texture
    bump = node(nt, "ShaderNodeBump", (-200, -500), Strength=0.08)
    n = node(nt, "ShaderNodeTexNoise", (-400, -500), Scale=300.0)
    nt.links.new(n.outputs[0], bump.inputs["Height"])
    nt.links.new(bump.outputs[0], bsdf.inputs["Normal"])
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


def head_shape(v, p):
    """Turn a unit sphere point into a head. Front of the face is -Y, up is +Z."""
    x, y, z = v
    w = p["face_w"]
    # skull: a bit narrower than deep, taller than wide
    x *= 0.74 * w
    y *= 0.86
    z *= 1.0
    # jaw and chin narrow towards the bottom
    if z < 0:
        t = -z
        z *= 0.9
        x *= 1 - p["jaw_taper"] * t ** 2.2
        y *= 1 - 0.15 * t ** 2
        if y < 0:
            y -= 0.04 * t ** 2   # chin a little forward
    # flatter face plane
    if y < -0.45:
        y = -0.45 + (y + 0.45) * 0.62
    # back of the head rounder
    if y > 0 and z > -0.2:
        y *= 1.05
    front = max(0.0, -y / 0.6)
    # eye sockets
    for side in (-1, 1):
        d2 = (x - side * 0.27) ** 2 + ((z - 0.12) * 1.3) ** 2
        y += 0.075 * gauss(d2, 0.13) * front
        # cheekbones
        d2 = (x - side * 0.4) ** 2 + (z + 0.08) ** 2
        y -= 0.05 * gauss(d2, 0.15) * front * p["cheek"]
    # brow ridge
    d2 = (x / 2.2) ** 2 + (z - 0.25) ** 2
    y -= p["brow"] * gauss(d2, 0.08) * front
    # nose: a ridge down the middle and a rounder tip
    if abs(x) < 0.3:
        ridge = gauss(x * x, 0.07) * (1 if -0.28 < z < 0.08 else gauss((z - (0.08 if z > 0 else -0.28)) ** 2, 0.05))
        ramp = min(1.0, max(0.0, (0.08 - z) / 0.36))
        y -= p["nose"] * 0.15 * ridge * ramp ** 1.6 * front
        d2 = x * x + ((z + 0.27) * 1.2) ** 2
        y -= p["nose"] * 0.09 * gauss(d2, 0.07) * front
        for side in (-1, 1):   # nostrils
            d2 = (x - side * 0.075) ** 2 + (z + 0.31) ** 2
            y -= 0.035 * gauss(d2, 0.045) * front
    # lips
    for cz, amount, r in ((-0.445, 0.05, 0.05), (-0.51, 0.055, 0.05)):
        d2 = (x / 2.6) ** 2 + (z - cz) ** 2
        y -= amount * gauss(d2, r) * front * p["lips"]
    d2 = (x / 3.0) ** 2 + (z + 0.475) ** 2   # the line between the lips
    y += 0.025 * gauss(d2, 0.012) * front
    # chin
    d2 = (x / 1.4) ** 2 + (z + 0.72) ** 2
    y -= 0.04 * gauss(d2, 0.09) * front
    return Vector((x, y, z))


def surface_y(p, x, z):
    """How far forward the face is at x, z (found by searching the sphere for that spot)."""
    best, best_d = None, 1e9
    for i in range(0, 91):
        for j in range(0, 91):
            th = math.radians(-90 + i * 2)      # around, -90..90 covers the front half
            ph = math.radians(j * 2)            # from the top down
            d = Vector((math.sin(ph) * math.sin(th), -math.sin(ph) * math.cos(th), math.cos(ph)))
            v = head_shape(d, p)
            dd = (v.x - x) ** 2 + (v.z - z) ** 2
            if dd < best_d and v.y < 0:
                best, best_d = v.y, dd
    return best


def make_head(p, skin_mat):
    head = sphere("Head")
    for vert in head.data.vertices:
        vert.co = head_shape(vert.co, p)
    head.data.materials.append(skin_mat)
    subsurf(head)
    head.location = (0, 0, 0)
    return head


def make_ears(p, skin_mat):
    ears = []
    for side in (-1, 1):
        ear = sphere("Ear", 32, 20)
        for vert in ear.data.vertices:
            x, y, z = vert.co
            # a flat shell, thicker at the rim
            vert.co = Vector((x * 0.07, y * 0.17, z * 0.25))
        ear.location = (side * 0.70 * p["face_w"], 0.08, -0.02)
        ear.rotation_euler = (0, 0, side * math.radians(-18))
        ear.data.materials.append(skin_mat)
        subsurf(ear)
        ears.append(ear)
    return ears


def make_eyes(p, skin_mat):
    iris_c = srgb_to_lin(p["eye"])
    mat = bpy.data.materials.new("Eye")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.05
    bsdf.inputs["Coat Weight"].default_value = 1.0
    tex = node(nt, "ShaderNodeTexCoord", (-900, 0))
    sep = node(nt, "ShaderNodeSeparateXYZ", (-700, 0))
    nt.links.new(tex.outputs["Object"], sep.inputs[0])
    xz = node(nt, "ShaderNodeCombineXYZ", (-500, 0))
    nt.links.new(sep.outputs[0], xz.inputs[0]); nt.links.new(sep.outputs[2], xz.inputs[1])
    scale = node(nt, "ShaderNodeVectorMath", (-420, 0)); scale.operation = "SCALE"
    scale.inputs["Scale"].default_value = 1 / 0.105
    nt.links.new(xz.outputs[0], scale.inputs[0])
    r = node(nt, "ShaderNodeVectorMath", (-350, 0)); r.operation = "LENGTH"
    nt.links.new(scale.outputs[0], r.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.location = (-150, 0)
    nt.links.new(r.outputs[1], ramp.inputs[0])
    els = ramp.color_ramp.elements
    els[0].position = 0.0; els[0].color = (0.005, 0.004, 0.004, 1)
    els[1].position = 0.44; els[1].color = (*[v * 0.6 for v in iris_c], 1)
    e = els.new(0.15); e.color = (0.005, 0.004, 0.004, 1)
    e = els.new(0.18); e.color = (*[min(1, v * 1.3) for v in iris_c], 1)
    e = els.new(0.48); e.color = (0.05, 0.04, 0.03, 1)
    e = els.new(0.54); e.color = (0.82, 0.78, 0.74, 1)
    # only the front of the eyeball shows the iris
    front = node(nt, "ShaderNodeMapRange", (-350, -200), inputs={1: -0.05, 2: -0.08, 3: 0.0, 4: 1.0})
    nt.links.new(sep.outputs[1], front.inputs[0])
    mix = node(nt, "ShaderNodeMix", (50, 0))
    mix.data_type = "RGBA"
    nt.links.new(front.outputs[0], mix.inputs[0])
    mix.inputs[6].default_value = (0.82, 0.78, 0.74, 1)
    nt.links.new(ramp.outputs[0], mix.inputs[7])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])

    objs = []
    look = p["look"]
    for side in (-1, 1):
        eye = sphere("Eye", 48, 32, 0.105)
        eye.scale = (1.12, 1.12, 1.12)
        ex, ez = side * 0.28 * p["face_w"], 0.115
        eye.location = (ex, surface_y(p, ex, ez) + 0.05, ez)
        eye.rotation_euler = (math.radians(-2 + look[1]), 0, math.radians(look[0]))
        eye.data.materials.append(mat)
        objs.append(eye)
        # eyelid: a skin shell over the top of the eye, a thin line of lashes on its rim
        lid = sphere("Lid", 48, 32, 0.113 * 1.12)
        bm = bmesh.new()
        bm.from_mesh(lid.data)
        cut = p["lid"]
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < cut and not (v.co.y > 0.02)], context="VERTS")
        bm.to_mesh(lid.data)
        bm.free()
        lid.location = eye.location
        lid.rotation_euler = (math.radians(-14 if p["woman"] else -10), 0, side * math.radians(-6))
        lid.data.materials.append(skin_mat)
        sol = lid.modifiers.new("Solid", "SOLIDIFY")
        sol.thickness = 0.012
        subsurf(lid, 1)
        objs.append(lid)
        lash = bpy.data.materials.new("Lash")
        lash.use_nodes = True
        lash.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.01, 0.008, 0.007, 1)
        ring = bpy.data.meshes.new("LashRing")
        bm = bmesh.new()
        prev = None
        thickness = 0.011 if p["woman"] else 0.007
        for i in range(25):
            a = math.radians(-70 + i * 140 / 24)
            r = 0.114 * 1.12
            y = -r * math.cos(a) * math.cos(math.asin(max(-1, min(1, cut / r))))
            x = r * math.sin(a)
            v1 = bm.verts.new((x, y, cut))
            v2 = bm.verts.new((x * 1.03, y * 1.08, cut + thickness * math.cos(a * 0.9)))
            if prev:
                bm.faces.new((prev[0], prev[1], v2, v1))
            prev = (v1, v2)
        bm.to_mesh(ring)
        bm.free()
        lr = bpy.data.objects.new("Lashes", ring)
        bpy.context.collection.objects.link(lr)
        lr.location = lid.location
        lr.rotation_euler = lid.rotation_euler
        lr.data.materials.append(lash)
        s = lr.modifiers.new("Solid", "SOLIDIFY")
        s.thickness = 0.006
        objs.append(lr)
    return objs


def make_neck_and_body(p, skin_mat, jersey_mat):
    neck = bpy.data.meshes.new("Neck")
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=48, radius1=0.4, radius2=0.33, depth=1.0)
    bm.to_mesh(neck)
    bm.free()
    n = bpy.data.objects.new("Neck", neck)
    bpy.context.collection.objects.link(n)
    n.location = (0, 0.12, -0.85)
    n.scale = (p["neck_w"], 0.95, 1)
    n.data.materials.append(skin_mat)
    for poly in neck.polygons:
        poly.use_smooth = True
    subsurf(n)

    torso = sphere("Torso", 96, 48)
    sw = p["shoulders"]
    for vert in torso.data.vertices:
        x, y, z = vert.co
        # shoulders: wide and rounded on top, chest going down out of the picture
        z = z * (0.72 if z > 0 else 2.0)
        x *= sw
        y *= 0.6
        # trapezius: the slope from the neck to the shoulder
        z += 0.2 * gauss(x * x, 0.45) * max(0.0, vert.co.z)
        vert.co = Vector((x, y, z))
    torso.location = (0, 0.18, -1.83)
    torso.data.materials.append(jersey_mat)
    subsurf(torso)

    # collar
    collar_mat, b = principled("Collar", srgb_to_lin(p["jersey"][2]), rough=0.6)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.36, minor_radius=0.05, location=(0, 0.1, -1.12),
                                     major_segments=64, minor_segments=16)
    c = bpy.context.active_object
    c.scale = (p["neck_w"] * 1.02, 0.95, 1)
    c.rotation_euler = (math.radians(-12), 0, 0)
    c.data.materials.append(collar_mat)
    bpy.ops.object.shade_smooth()
    return [n, torso, c]


def hair_cap(p, mat, front_line, top_extra=0.08, side_low=-0.15, back_low=-0.55, thick=0.07, bulk=0.0):
    """A shell over the skull. The hairline follows front_line(x) on the face."""
    cap = sphere("HairCap", 96, 64)
    bm = bmesh.new()
    bm.from_mesh(cap.data)
    for v in bm.verts:
        hv = head_shape(v.co.copy(), p)
        x, y, z = hv
        # height of the hairline here: front line at the face, lower at the sides and the back
        side = max(0.0, min(1.0, (abs(x) - 0.42 * p["face_w"]) / (0.3 * p["face_w"])))
        side = side * side * (3 - 2 * side)
        back = max(0.0, min(1.0, (y + 0.2) / 0.6))
        limit = front_line(x) * (1 - side) + side_low * side
        limit = limit * (1 - back) + back_low * back
        # full thickness above the hairline, thinning to nothing just below it
        t = max(0.0, min(1.0, (z - limit + 0.06) / 0.12))
        t = t * t * (3 - 2 * t)
        out = hv.normalized()
        grow = thick + top_extra * max(0, z) ** 2 + bulk * (0.5 + 0.5 * noise.noise(hv * 3.0))
        v.co = hv + out * (grow * t - 0.02 * (1 - t))
    bm.to_mesh(cap.data)
    bm.free()
    cap.data.materials.append(mat)
    subsurf(cap)
    for poly in cap.data.polygons:
        poly.use_smooth = True
    return cap


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


def make_hair(p, mat):
    style = p["hair"]
    fw = p["face_w"]
    objs = []
    if style == "bald":
        return objs
    if style == "buzz":
        objs.append(hair_cap(p, mat, lambda x: 0.48 - 0.1 * x * x, top_extra=0.0, thick=0.012, side_low=0.0, back_low=-0.45))
    elif style == "short":
        objs.append(hair_cap(p, mat, lambda x: 0.46 - 0.05 * x, top_extra=0.07, thick=0.04, side_low=0.02, back_low=-0.45))
    elif style == "side":
        objs.append(hair_cap(p, mat, lambda x: 0.44 + 0.12 * x, top_extra=0.1, thick=0.05, side_low=0.0,
                             back_low=-0.45, bulk=0.02))
    elif style == "quiff":
        objs.append(hair_cap(p, mat, lambda x: 0.47, top_extra=0.05, thick=0.04, side_low=0.05, back_low=-0.45))
        objs.append(blob("Quiff", mat, (0.02, -0.38, 0.86), (0.5 * fw, 0.32, 0.17), (math.radians(-20), 0, 0), 0.05))
    elif style == "curly":
        objs.append(hair_cap(p, mat, lambda x: 0.42, top_extra=0.04, thick=0.06, side_low=0.0, back_low=-0.4))
        rnd = random.Random(p["seed"])
        for i in range(110):
            th = rnd.uniform(0, math.pi * 2)
            ph = rnd.uniform(0.0, 1.25)
            d = Vector((math.sin(ph) * math.cos(th), math.sin(ph) * math.sin(th), math.cos(ph)))
            pos = head_shape(d, p)
            if pos.y < -0.3 and pos.z < 0.55:
                continue
            r = rnd.uniform(0.08, 0.12) * (1.25 if p["woman"] else 1.0)
            objs.append(blob("Curl", mat, tuple(pos * 1.05), (r, r, r), wobble=0.05))
    elif style in ("pony", "bun", "braid", "long", "short_w"):
        part = 0.07 if style != "short_w" else 0.0
        objs.append(hair_cap(p, mat, lambda x: 0.5 - 0.2 * (x - part) ** 2, top_extra=0.05, thick=0.045,
                             side_low=-0.08 if style != "short_w" else -0.12, back_low=-0.55, bulk=0.015))
        if style == "pony":
            objs.append(blob("Tie", mat, (0, 0.82, 0.25), (0.15, 0.13, 0.15)))
            objs.append(blob("Pony", mat, (0.0, 1.0, -0.35), (0.16, 0.16, 0.62), (math.radians(14), 0, 0), 0.1))
        elif style == "bun":
            objs.append(blob("Bun", mat, (0, 0.55, 0.92), (0.26, 0.24, 0.22), wobble=0.12))
        elif style == "braid":
            for i in range(9):
                objs.append(blob("Braid", mat, (0.0, 0.85 + i * 0.03, 0.1 - i * 0.17),
                                 (0.12 - i * 0.004, 0.11, 0.12), (0, 0, math.radians(30 if i % 2 else -30))))
        elif style == "long":
            for side in (-1, 1):
                objs.append(blob("Long", mat, (side * 0.66 * fw, 0.25, -0.65), (0.17, 0.42, 0.95),
                                 (0, side * math.radians(4), 0), 0.08))
            objs.append(blob("LongBack", mat, (0, 0.5, -0.6), (0.68 * fw, 0.3, 0.95), wobble=0.06))
    elif style == "bob":
        objs.append(hair_cap(p, mat, lambda x: 0.36 - 0.02 * x, top_extra=0.06, thick=0.06, side_low=-0.62,
                             back_low=-0.62, bulk=0.02))
    return objs


def make_extra(p):
    objs = []
    if p["extra"] == "band":
        mat, b = principled("Band", srgb_to_lin(p["jersey"][1]), rough=0.7, sheen=0.5)
        bpy.ops.mesh.primitive_torus_add(major_radius=0.78, minor_radius=0.045, location=(0, 0.03, 0.5),
                                         major_segments=64, minor_segments=12)
        t = bpy.context.active_object
        t.scale = (0.98 * p["face_w"], 1.02, 1.6)
        t.rotation_euler = (math.radians(-8), 0, 0)
        t.data.materials.append(mat)
        bpy.ops.object.shade_smooth()
        objs.append(t)
    elif p["extra"] == "glasses":
        frame, b = principled("Frame", (0.02, 0.02, 0.02), rough=0.3)
        for side in (-1, 1):
            bpy.ops.mesh.primitive_torus_add(major_radius=0.13, minor_radius=0.012,
                                             location=(side * 0.27 * p["face_w"], -0.62, 0.11),
                                             rotation=(math.radians(90), 0, 0), major_segments=48, minor_segments=8)
            g = bpy.context.active_object
            g.scale = (1.0, 0.8, 1.0)
            g.data.materials.append(frame)
            objs.append(g)
        bpy.ops.mesh.primitive_cylinder_add(radius=0.01, depth=0.16, location=(0, -0.66, 0.14),
                                            rotation=(0, math.radians(90), 0))
        bpy.context.active_object.data.materials.append(frame)
        objs.append(bpy.context.active_object)
    elif p["extra"] == "cap":
        mat, b = principled("Cap", srgb_to_lin(p["jersey"][0]), rough=0.7, sheen=0.3)
        crown = blob("Crown", mat, (0, 0.05, 0.42), (0.82 * p["face_w"], 0.9, 0.6))
        bm = bmesh.new()
        bm.from_mesh(crown.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 0.0], context="VERTS")
        bm.to_mesh(crown.data)
        bm.free()
        objs.append(crown)
        objs.append(blob("Visor", mat, (0, -0.85, 0.42), (0.55, 0.42, 0.03), (math.radians(-8), 0, 0)))
    return objs


# ---------- one portrait ----------

def person(seed, woman):
    rnd = random.Random(seed * 7919 + (1 if woman else 0))
    skin = SKINS[rnd.randrange(len(SKINS))]
    dark = skin[0] < 0.65
    hair_names = list(HAIRS)
    if dark:
        hair_color = HAIRS[rnd.choice(["mork", "svart", "svart", "brun"])]
    else:
        hair_color = HAIRS[rnd.choice(hair_names)]
    styles = WOMEN_HAIR if woman else MEN_HAIR
    style = rnd.choice(styles)
    if dark and rnd.random() < 0.5:
        style = "curly" if woman or rnd.random() < 0.5 else "buzz"
    if style == "bald" and hair_color == HAIRS["gra"]:
        pass
    beard = None
    if not woman:
        r = rnd.random()
        beard = "full" if r < 0.2 else ("stubble" if r < 0.45 else None)
    extra = rnd.choice([None, None, None, "band", "glasses"] + ([] if woman else ["cap"]))
    if style in ("bun", "quiff", "curly") and extra == "cap":
        extra = None
    return {
        "seed": seed, "woman": woman, "skin": skin, "hair_color": hair_color, "hair": style,
        "eye": rnd.choice(EYES if not dark else EYES[2:4]),
        "jersey": JERSEYS[rnd.randrange(len(JERSEYS))], "stripe": rnd.choice((-1, 1)) * rnd.uniform(0.5, 0.9),
        "face_w": rnd.uniform(0.94, 1.02) if woman else rnd.uniform(1.0, 1.08),
        "jaw_taper": rnd.uniform(0.3, 0.4) if woman else rnd.uniform(0.18, 0.28),
        "cheek": rnd.uniform(0.6, 1.2), "brow": rnd.uniform(0.02, 0.035) if woman else rnd.uniform(0.04, 0.06),
        "nose": rnd.uniform(0.75, 0.95) if woman else rnd.uniform(0.9, 1.15),
        "lips": rnd.uniform(1.0, 1.3) if woman else rnd.uniform(0.75, 1.0),
        "lid": rnd.uniform(0.022, 0.036), "look": (rnd.uniform(-4, 4), rnd.uniform(-3, 2)),
        "neck_w": 0.92 if woman else 1.08, "shoulders": rnd.uniform(1.35, 1.5) if woman else rnd.uniform(1.6, 1.78),
        "beard": beard, "extra": extra, "turn": rnd.uniform(-12, 12), "tilt": rnd.uniform(-3, 3),
    }


def render_person(p, path):
    reset()
    camera()
    light("Key", (-4.5, -6.0, 3.2), 900, 4.0, (1.0, 0.96, 0.9))
    light("Fill", (5.0, -5.5, 0.5), 260, 6.0, (0.9, 0.95, 1.0))
    light("Rim", (2.5, 5.0, 3.0), 700, 3.0, (1.0, 0.95, 0.85), target=(0, 0, 0.3))
    light("Rim2", (-3.0, 4.5, 1.5), 400, 3.0, (0.95, 1.0, 1.0), target=(0, 0, 0.3))

    skin = skin_material(p)
    hair = hair_material(p["hair_color"])
    jersey = jersey_material(p["jersey"], p["stripe"])

    head_parts = [make_head(p, skin)] + make_ears(p, skin) + make_eyes(p, skin) + make_hair(p, hair) + make_extra(p)
    pivot = bpy.data.objects.new("HeadPivot", None)
    bpy.context.collection.objects.link(pivot)
    pivot.location = (0, 0.1, -0.6)
    bpy.context.view_layer.update()
    for obj in head_parts:
        world = obj.matrix_world.copy()
        obj.parent = pivot
        obj.matrix_parent_inverse = pivot.matrix_world.inverted()
    pivot.rotation_euler = (math.radians(p["tilt"]), 0, math.radians(p["turn"]))
    make_neck_and_body(p, skin, jersey)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv
    args = argv[argv.index("--") + 1:] if "--" in argv else argv[1:]
    out = args[0] if args else "."
    count = int(args[1]) if len(args) > 1 else 20
    first = int(args[2]) if len(args) > 2 else 1
    only = args[3] if len(args) > 3 else None
    import os
    os.makedirs(out, exist_ok=True)
    for i in range(first, first + count):
        for woman, prefix in ((False, "Herr"), (True, "Dam")):
            if only and only != prefix:
                continue
            render_person(person(i, woman), os.path.join(os.path.abspath(out), f"{prefix}_{i:02d}.png"))
            print(prefix, i, flush=True)


if __name__ == "__main__":
    main()
