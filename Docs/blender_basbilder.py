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
        layer(jaw, srgb_to_lin(tuple(v * 0.9 for v in p["hair_color"])), 0.6 if p["beard"] == "full" else 0.3)
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
    mat, bsdf = principled("Hair", c, rough=0.4, sheen=0.35)
    bsdf.inputs["Specular IOR Level"].default_value = 0.6
    nt = mat.node_tree
    # locks a shade lighter and darker
    var = node(nt, "ShaderNodeTexNoise", (-500, 200), Scale=9.0, Detail=3.0)
    ramp = node(nt, "ShaderNodeMix", (-300, 200))
    ramp.data_type = "RGBA"
    nt.links.new(var.outputs[0], ramp.inputs[0])
    ramp.inputs[6].default_value = (*[v * 0.75 for v in c], 1)
    ramp.inputs[7].default_value = (*[min(1, v * 1.35 + 0.01) for v in c], 1)
    nt.links.new(ramp.outputs[2], bsdf.inputs["Base Color"])
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


def make_beard(p, mat):
    """A full beard and moustache made of short strands on the jaw and upper lip."""
    if p["beard"] != "full":
        return []
    rnd = random.Random(p["seed"] * 17 + 3)
    strands = []
    for i in range(9000):
        z = 1 - (i + 0.5) / 9000 * 2
        r = math.sqrt(max(0.0, 1 - z * z))
        th = math.pi * (3 - math.sqrt(5)) * i
        d = Vector((r * math.cos(th), r * math.sin(th), z))
        s = head_shape(d, p)
        if s.y > 0.15:
            continue
        top = -0.3 + 0.22 * min(1.0, abs(s.x) / 0.55)        # higher towards the ears
        moustache = abs(s.x) < 0.2 and -0.43 < s.z < -0.36
        mouth = abs(s.x) < 0.19 and -0.54 < s.z < -0.41
        if not ((s.z < top and not mouth) or moustache):
            continue
        n = s.normalized()
        down = tangent(Vector((0.15 * s.x, -0.1, -1.0)), s).normalized()
        L = rnd.uniform(0.05, 0.09)
        pts = [s + n * 0.005, s + n * 0.025 + down * L * 0.5, s + n * 0.035 + down * L]
        strands.append((pts, rnd.uniform(0.7, 1.2), 0.3))
    return [strand_curve("Beard", mat, strands, 0.008)] if strands else []


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
        if isinstance(edge, S):
            lo = edge - soft
            hi = edge + soft
            self.nt.links.new(lo.out, n.inputs[1])
            self.nt.links.new(hi.out, n.inputs[2])
        else:
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
        # the top split corner to corner in two colours, with thin speed lines along the edge
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
    # raglan shoulder panels in the second colour, with a trim along the seam
    seam = ax - 0.36 - (0.86 - Z) * 1.15
    layers.append((seam.above(0.0), second if design != "split" else dark))
    layers.append((seam.abs().below(0.018), trim))
    # side panels
    layers.append(((ax - 1.32).above(0.0, 0.05), dark))
    # collar trim
    neck = (X * X / 0.16 + (Z - 0.95) * (Z - 0.95) / 0.09)
    layers.append((neck.below(1.0, 0.04) * Y.below(0.0, 0.05), trim))
    # the control flag on the chest: a square split corner to corner, white and orange
    fx, fz = X - 0.58, Z - 0.36
    box = (fx.abs().max(fz.abs())).below(0.105, 0.006) * Y.below(-0.2, 0.05)
    inner = (fx.abs().max(fz.abs())).below(0.09, 0.006) * Y.below(-0.2, 0.05)
    layers.append((box, (0.02, 0.02, 0.02)))
    layers.append((inner, (0.92, 0.92, 0.9)))
    layers.append((inner * (fz - fx).above(0.0, 0.004), srgb_to_lin((0.95, 0.42, 0.05))))
    color = paint_layers(nt, base, layers)
    nt.links.new(color, bsdf.inputs["Base Color"])
    # technical mesh fabric
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


# ---------- hair made of strands ----------

def to_sphere(pt, p):
    w = p["face_w"]
    return Vector((pt.x / (0.74 * w), pt.y / 0.86, pt.z)).normalized()


def head_radius(pt, p):
    """Distance from the head's centre to its skin in the direction of pt."""
    return head_shape(to_sphere(pt, p), p).length


def hairline(p, x, y, front, side_low, back_low):
    side = max(0.0, min(1.0, (abs(x) - 0.42 * p["face_w"]) / (0.3 * p["face_w"])))
    side = side * side * (3 - 2 * side)
    back = max(0.0, min(1.0, (y + 0.2) / 0.6))
    limit = front(x) * (1 - side) + side_low * side
    return limit * (1 - back) + back_low * back


def roots(p, count, front, side_low, back_low, rnd):
    pts = []
    golden = math.pi * (3 - math.sqrt(5))
    for i in range(count):
        z = 1 - (i + 0.5) / count * 2
        r = math.sqrt(1 - z * z)
        th = golden * i + rnd.uniform(-0.05, 0.05)
        d = Vector((r * math.cos(th), r * math.sin(th), z))
        s = head_shape(d, p)
        if s.z >= hairline(p, s.x, s.y, front, side_low, back_low):
            pts.append(s)
    return pts


def strand_curve(name, mat, strands, radius):
    """strands: list of (points, root radius factor, tip factor)."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = 1
    cu.use_fill_caps = True
    for pts, rf, tip in strands:
        sp = cu.splines.new("NURBS")
        sp.points.add(len(pts) - 1)
        for i, pt in enumerate(pts):
            t = i / (len(pts) - 1)
            sp.points[i].co = (pt.x, pt.y, pt.z, 1)
            sp.points[i].radius = rf * (1 - t * (1 - tip))
        sp.order_u = 4
        sp.use_endpoint_u = True
    obj = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def grow(p, root, length, h, field, rnd, steps=12, hang_below=None):
    """Walk a strand from its root along field(pos, t), keeping it h above the skin."""
    pts = [root + root.normalized() * h * 0.2]
    pos = pts[0]
    seg = length / steps
    # neighbouring strands bend the same way, so the hair falls in locks instead of single threads
    wob = noise.noise_vector(root * 2.2) * 0.35 + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * 0.04
    for k in range(steps):
        t = (k + 1) / steps
        d = (field(pos, t) + wob * (0.5 + 0.5 * t)).normalized()
        new = pos + d * seg
        lift = h * min(1.0, 0.3 + t * 1.2)
        r_head = head_radius(new, p)
        if hang_below is None or new.z > hang_below:
            if new.length < r_head + lift:
                new = new.normalized() * (r_head + lift)
        else:
            # hanging hair: beside the face and neck, never in front of them
            min_x = 0.6 * p["face_w"] + lift
            if abs(new.x) < min_x and new.y < 0.35:
                new.x = math.copysign(min_x, new.x if new.x != 0 else 1)
            new.y = max(new.y, -0.25)
        pts.append(new)
        pos = new
    return pts


def tangent(v, pos):
    n = pos.normalized()
    return v - n * v.dot(n)


def make_hair(p, mat):
    style = p["hair"]
    fw = p["face_w"]
    rnd = random.Random(p["seed"] * 31 + 7)
    objs = []
    if style == "bald":
        return objs
    if style == "buzz":
        front = lambda x: 0.48 - 0.1 * x * x
        objs.append(hair_cap(p, mat, front, top_extra=0.0, thick=0.008, side_low=0.0, back_low=-0.45))
        short = []
        for r in roots(p, 14000, front, 0.0, -0.45, rnd):
            n = r.normalized()
            d = tangent(Vector((0.2 * r.x, 0.6, -0.4)), r).normalized()
            short.append(([r + n * 0.004, r + n * 0.016 + d * 0.02, r + n * 0.022 + d * 0.04], rnd.uniform(0.7, 1.2), 0.3))
        objs.append(strand_curve("Buzz", mat, short, 0.006))
        return objs

    crown = Vector((0.05, 0.4, 0.95))
    strands = []
    if style in ("short", "side", "quiff", "short_w"):
        part = 0.3 * fw if style == "side" else (0.18 if style == "short_w" else None)
        front = (lambda x: 0.46 - 0.05 * x) if style != "short_w" else (lambda x: 0.44)
        objs.append(hair_cap(p, mat, front, top_extra=0.02, thick=0.012, side_low=0.02, back_low=-0.45))
        base_len = 0.26 if style != "short_w" else 0.4

        def field(pos, t):
            if style == "quiff" and pos.y < -0.1 and pos.z > 0.4:
                return Vector((0, 0.35, 1.0)) * (1 - t) + Vector((0, 1, -0.3)) * t
            if part is not None and pos.z > 0.55:
                # swept over from the parting, lifted a little at the front
                return tangent(Vector((math.copysign(1, pos.x - part), 0.35, 0.1)), pos)
            if style == "short" and pos.z > 0.5:
                # top combed up and back
                return tangent(Vector((0.1 * pos.x, 1.0, 0.25)), pos)
            # sides and back lie down towards the neck
            return tangent(Vector((0.3 * math.copysign(1, pos.x), 0.5, -1.0)), pos)
        for r in roots(p, 7000, front, 0.02, -0.45, rnd):
            # shorter towards the hairline, so the fringe stops above the brows
            near_front = max(0.0, min(1.0, (r.z - 0.42) / 0.35)) if r.y < -0.2 else 1.0
            on_top = 1.0 if r.z > 0.5 else 0.55          # sides cut shorter
            length = base_len * rnd.uniform(0.8, 1.15) * (0.45 + 0.55 * near_front) * on_top
            strands.append((grow(p, r, length, 0.03 + 0.03 * max(0, r.z), field, rnd, 8), rnd.uniform(0.7, 1.3), 0.2))
        objs.append(strand_curve("Hair", mat, strands, 0.012))
    elif style == "curly":
        objs.append(hair_cap(p, mat, lambda x: 0.42, top_extra=0.04, thick=0.05, side_low=0.0, back_low=-0.4))
        length = 0.38 if p["woman"] else 0.24
        for r in roots(p, 1400, lambda x: 0.42, 0.0, -0.4, rnd):
            n = (r.normalized() + Vector((0, 0, 0.25))).normalized()
            u = n.cross(Vector((0, 0, 1)))
            if u.length < 0.1:
                u = Vector((1, 0, 0))
            u.normalize()
            v = n.cross(u)
            turns = rnd.uniform(2.5, 3.5)
            cr = rnd.uniform(0.03, 0.045)
            pts = []
            for k in range(25):
                t = k / 24
                a = t * turns * math.pi * 2 + rnd.uniform(0, 6)
                pts.append(r + n * (0.04 + t * length) + (u * math.cos(a) + v * math.sin(a)) * cr * min(1, t * 4))
            strands.append((pts, rnd.uniform(0.8, 1.2), 0.5))
        objs.append(strand_curve("Curls", mat, strands, 0.02))
    else:
        # longer hair with a parting, tied up or hanging down
        part = rnd.choice((0.0, 0.12, -0.12)) * fw
        front = lambda x: 0.5 - 0.2 * (x - part) ** 2
        objs.append(hair_cap(p, mat, front, top_extra=0.0, thick=0.014,
                             side_low=-0.12 if style != "bob" else -0.2, back_low=-0.55))
        side_pony = style == "pony" and rnd.random() < 0.5
        side = rnd.choice((-1, 1))
        if style == "pony":
            tie = Vector((side * 0.42, 0.62, -0.32)) if side_pony else Vector((0, 0.72, 0.62))
        elif style == "braid":
            tie = Vector((0, 0.86, 0.3))
        else:
            tie = Vector((0, 0.42, 0.98))
        hang = style in ("long", "bob")
        end_z = -1.25 if style == "long" else -0.62

        def field(pos, t):
            if not hang:
                return tie - pos
            side = math.copysign(1, pos.x - part)
            if pos.z > 0.35 and pos.y < 0.45:
                return tangent(Vector((side, 0.15, -0.35)), pos)
            if pos.y > 0.45 and pos.z > 0:
                return tangent(Vector((0.15 * side, 0.4, -1)), pos)
            return Vector((0.12 * side, 0.12, -1))
        for r in roots(p, 7000, front, -0.12, -0.55, rnd):
            if hang:
                length = (r.z - end_z) * rnd.uniform(0.95, 1.12) + 0.2
                pts = grow(p, r, length, 0.035, field, rnd, 16, hang_below=-0.1)
            else:
                length = (tie - r).length
                pts = grow(p, r, length, 0.03, field, rnd, 10)
            strands.append((pts, rnd.uniform(0.7, 1.3), 0.4 if hang else 0.9))
        objs.append(strand_curve("Hair", mat, strands, 0.0115))
        tails = []
        if style == "pony":
            if side_pony:
                # tied low at the back and brought forward over one shoulder
                mid = Vector((side * 0.82, 0.25, -0.95))
                end = Vector((side * 0.78, -0.32, -1.75))
            else:
                # a high ponytail that shows behind the head
                mid = Vector((side * 0.55, 1.05, 0.85))
                end = Vector((side * 0.85, 0.75, -0.45))
            for i in range(700):
                off = Vector((rnd.gauss(0, 0.05), rnd.gauss(0, 0.05), rnd.gauss(0, 0.045)))
                pts = []
                for k in range(14):
                    t = k / 13
                    c = tie * (1 - t) ** 2 + mid * 2 * t * (1 - t) + end * t * t
                    pts.append(c + off * (1 + t * 1.8) + noise.noise_vector(c * 3 + off * 5) * 0.03)
                tails.append((pts, rnd.uniform(0.8, 1.3), 0.25))
            objs.append(blob("Tie", mat, tuple(tie), (0.09, 0.08, 0.09)))
        elif style == "bun":
            centre = tie + Vector((0, 0.08, 0.08))
            objs.append(blob("BunCore", mat, tuple(centre), (0.19, 0.17, 0.16)))
            for i in range(360):
                axis = Vector((rnd.gauss(0, 1), rnd.gauss(0, 0.6), rnd.gauss(0, 1))).normalized()
                u = axis.cross(Vector((0.3, 0.2, 1))).normalized()
                v = axis.cross(u)
                rr = rnd.uniform(0.19, 0.22)
                a0 = rnd.uniform(0, 6.3)
                pts = [centre + Vector(((u * math.cos(a0 + k * 0.26) + v * math.sin(a0 + k * 0.26)) * rr)[:]) * 1.0
                       for k in range(14)]
                pts = [centre + Vector(((q - centre).x * 1.0, (q - centre).y * 0.9, (q - centre).z * 0.85)) for q in pts]
                tails.append((pts, rnd.uniform(0.9, 1.3), 0.6))
        elif style == "braid":
            for i in range(9):
                objs.append(blob("Braid", mat, (0.0, 0.95 + i * 0.03, 0.15 - i * 0.16),
                                 (0.11 - i * 0.004, 0.1, 0.11), (0, 0, math.radians(30 if i % 2 else -30))))
        if style in ("pony", "bun", "braid"):
            # a few loose strands framing the face
            for i in range(70):
                sd = rnd.choice((-1, 1))
                d = Vector((sd * rnd.uniform(0.55, 0.75), -rnd.uniform(0.5, 0.7), rnd.uniform(0.3, 0.5))).normalized()
                r = head_shape(d, p)
                L = rnd.uniform(0.45, 0.8)

                def wisp(pos, t, sd=sd):
                    return Vector((sd * 0.25, 0.05, -1))
                tails.append((grow(p, r, L, 0.03, wisp, rnd, 10, hang_below=0.1), rnd.uniform(0.5, 0.8), 0.3))
        if tails:
            objs.append(strand_curve("Tail", mat, tails, 0.017))
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
    if dark and rnd.random() < 0.3:
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
        "jersey": JERSEYS[rnd.randrange(len(JERSEYS))],
        "design": rnd.choice(("swoosh", "chevron", "split")), "design_flip": rnd.choice((-1, 1)),
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
    jersey = jersey_material(p)

    head_parts = ([make_head(p, skin)] + make_ears(p, skin) + make_eyes(p, skin) + make_hair(p, hair)
                  + make_beard(p, hair) + make_extra(p))
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
