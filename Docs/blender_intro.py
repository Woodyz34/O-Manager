# Renders the intro film that plays before the main menu (Assets/Resources/Intro/intro.mp4).
#
# A low run through a Nordic forest in morning light that ends at a control flag in a clearing.
# The title (O-MANAGER) is not in the film: the game puts it on top (IntroScene.cs), so it stays sharp.
#
# Run with the bpy module (pip install bpy):
#   python Docs/blender_intro.py --out frames_dir [--res 1920x1080] [--samples 32] [--frames 1-180] [--still 120]
# then join the frames with ffmpeg (see the bottom of this file).

import argparse
import math
import random
import sys

import bpy
from mathutils import Vector

FPS = 30
LENGTH = 180          # frames (6 s)
SEED = 7

ORANGE = (1.0, 0.22, 0.0, 1)
WHITE = (0.95, 0.95, 0.93, 1)


def args():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--res", default="1920x1080")
    p.add_argument("--samples", type=int, default=32)
    p.add_argument("--frames", default=f"1-{LENGTH}")
    p.add_argument("--still", default="")
    p.add_argument("--blend", default="")
    p.add_argument("--engine", default="CYCLES")
    argv = sys.argv[1:]
    return p.parse_args(argv)


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, color, rough=0.8, emit=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = color
    b.inputs["Roughness"].default_value = rough
    if emit > 0:
        b.inputs["Emission Color"].default_value = color
        b.inputs["Emission Strength"].default_value = emit
    return m


def noise_mat(name, dark, light, scale=3.0, rough=0.9):
    # A colour that varies between two tones, so large surfaces do not look flat
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    tc = nt.nodes.new("ShaderNodeTexCoord")
    n = nt.nodes.new("ShaderNodeTexNoise")
    n.inputs["Scale"].default_value = scale
    n.inputs["Detail"].default_value = 6
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = dark
    ramp.color_ramp.elements[1].color = light
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[1].position = 0.7
    nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
    nt.links.new(n.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    return m


def link(obj, coll):
    for c in obj.users_collection:
        c.objects.unlink(obj)
    coll.objects.link(obj)


def ground(path_pts):
    bpy.ops.mesh.primitive_plane_add(size=220, location=(0, -40, 0))
    g = bpy.context.active_object
    g.name = "Ground"
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.subdivide(number_cuts=120)
    bpy.ops.object.mode_set(mode="OBJECT")
    rnd = random.Random(SEED)
    # Soft hummocks, flat along the path the camera runs
    bumps = [(rnd.uniform(-110, 110), rnd.uniform(-150, 70), rnd.uniform(3, 12), rnd.uniform(-0.6, 1.4)) for _ in range(120)]
    for v in g.data.vertices:
        wx, wy = v.co.x, v.co.y - 40     # the plane sits 40 m back
        h = 0.0
        for bx, by, r, a in bumps:
            d2 = (wx - bx) ** 2 + (wy - by) ** 2
            h += a * math.exp(-d2 / (r * r))
        dpath = min(math.hypot(wx - px, wy - py) for px, py in path_pts)
        dpath = min(dpath, math.hypot(wx, wy))   # and the clearing
        flat = min(1.0, max(0.0, (dpath - 2.5) / 8.0))
        v.co.z = h * flat
    m = noise_mat("Moss", (0.07, 0.13, 0.04, 1), (0.22, 0.27, 0.08, 1), scale=0.35)
    g.data.materials.append(m)
    bpy.ops.object.shade_smooth()
    return g


def ground_height(g, x, y):
    # Height of the ground under (x, y), by casting a ray down
    hit, loc, *_ = g.ray_cast(Vector((x - g.location.x, y - g.location.y, 50)), Vector((0, 0, -1)))
    return loc.z if hit else 0.0


def spruce(name, mats):
    # A spruce: a dark trunk and a stack of drooping cones that get smaller towards the top
    parts = []
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.22, depth=14, location=(0, 0, 7))
    t = bpy.context.active_object
    t.data.materials.append(mats["bark_dark"])
    parts.append(t)
    levels = 9
    for i in range(levels):
        f = i / (levels - 1)
        r = 3.0 * (1 - f) + 0.5
        z = 1.6 + f * 13.0
        bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=r, radius2=0.05, depth=r * 1.25, location=(0, 0, z + r * 0.5))
        c = bpy.context.active_object
        c.rotation_euler = (0, 0, i * 0.7)
        c.data.materials.append(mats["needles"])
        parts.append(c)
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.shade_flat()
    return o


def pine(name, mats):
    # A pine: a tall bare trunk, orange near the top, with a round crown
    parts = []
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.25, depth=20, location=(0, 0, 10))
    t = bpy.context.active_object
    t.data.materials.append(mats["bark_pine"])
    parts.append(t)
    rnd = random.Random(hash(name) & 0xffff)
    for i in range(5):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=rnd.uniform(1.3, 2.0),
                                              location=(rnd.uniform(-1.2, 1.2), rnd.uniform(-1.2, 1.2), 19.5 + rnd.uniform(-1.5, 1.5)))
        c = bpy.context.active_object
        c.scale = (1, 1, 0.65)
        c.data.materials.append(mats["pine_needles"])
        parts.append(c)
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    bpy.ops.object.shade_flat()
    return o


def rock(name, mats, rnd):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1, location=(0, 0, 0))
    o = bpy.context.active_object
    for v in o.data.vertices:
        v.co *= rnd.uniform(0.75, 1.15)
    o.scale = (rnd.uniform(0.8, 1.6), rnd.uniform(0.7, 1.3), rnd.uniform(0.4, 0.8))
    o.data.materials.append(mats["rock"])
    o.name = name
    bpy.ops.object.shade_flat()
    return o


def bush(name, mats, rnd):
    # A low blueberry/heather shrub for the forest floor
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.38, location=(0, 0, 0.12))
    o = bpy.context.active_object
    for v in o.data.vertices:
        v.co *= rnd.uniform(0.7, 1.2)
    o.scale = (rnd.uniform(0.8, 1.3), rnd.uniform(0.8, 1.3), 0.8)
    o.data.materials.append(mats["shrub"] if rnd.random() < 0.7 else mats["heather"])
    o.name = name
    bpy.ops.object.shade_flat()
    return o


def log(name, mats):
    # A fallen trunk
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.22, depth=7, location=(0, 0, 0.2), rotation=(0, math.pi / 2, 0))
    o = bpy.context.active_object
    o.data.materials.append(mats["bark_pine"])
    o.name = name
    return o


def control_flag(mats):
    # The orienteering control: a three-sided prism, each side white over orange split along the diagonal,
    # hung from a wooden stake with an SI unit on it.
    coll = bpy.data.collections.new("Control")
    bpy.context.scene.collection.children.link(coll)
    s = 0.3   # side length of the flag
    hang_z = 0.95
    # Stake
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.025, depth=1.45, location=(0, 0, 0.72))
    stake = bpy.context.active_object
    stake.data.materials.append(mats["wood"])
    link(stake, coll)
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.012, depth=0.32, location=(0.16, 0, 1.38), rotation=(0, math.pi / 2, 0))
    arm = bpy.context.active_object
    arm.data.materials.append(mats["wood"])
    link(arm, coll)
    # SI unit
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, -0.045, 0.82))
    si = bpy.context.active_object
    si.scale = (0.05, 0.03, 0.11)
    si.data.materials.append(mats["si"])
    link(si, coll)
    # Flag: 3 faces round a triangle; each face is two triangles (white top-left, orange bottom-right)
    r = s / math.sqrt(3)
    corners = [(r * math.cos(a), r * math.sin(a)) for a in (math.pi / 2, math.pi / 2 + 2 * math.pi / 3, math.pi / 2 + 4 * math.pi / 3)]
    verts, faces, mat_idx = [], [], []
    top, bot = hang_z + s / 2, hang_z - s / 2
    for i in range(3):
        (x0, y0), (x1, y1) = corners[i], corners[(i + 1) % 3]
        b = len(verts)
        verts += [(x0, y0, top), (x1, y1, top), (x1, y1, bot), (x0, y0, bot)]
        faces.append((b + 0, b + 1, b + 3)); mat_idx.append(0)   # white half
        faces.append((b + 1, b + 2, b + 3)); mat_idx.append(1)   # orange half
    me = bpy.data.meshes.new("Flag")
    me.from_pydata(verts, [], faces)
    me.update()
    flag = bpy.data.objects.new("Flag", me)
    coll.objects.link(flag)
    flag.data.materials.append(mats["flag_white"])
    flag.data.materials.append(mats["flag_orange"])
    for p, i in zip(flag.data.polygons, mat_idx):
        p.material_index = i
    flag.location = (0.30, 0, 1.38 - top)  # top edge right under the arm
    flag.location.z += 0.0
    sol = flag.modifiers.new("Thick", "SOLIDIFY")
    sol.thickness = 0.004
    # Gently swinging in the wind
    flag.rotation_mode = "XYZ"
    for f, a in ((1, -0.25), (60, 0.2), (120, -0.12), (180, 0.15)):
        flag.rotation_euler = (0, 0, a)
        flag.keyframe_insert("rotation_euler", frame=f)
    return coll


def build():
    rnd = random.Random(SEED)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.frame_start, sc.frame_end = 1, LENGTH

    mats = {
        "bark_dark": mat("BarkDark", (0.09, 0.06, 0.04, 1)),
        "bark_pine": noise_mat("BarkPine", (0.25, 0.10, 0.04, 1), (0.55, 0.25, 0.09, 1), scale=1.5),
        "needles": noise_mat("Needles", (0.015, 0.05, 0.02, 1), (0.04, 0.11, 0.04, 1), scale=1.0),
        "pine_needles": noise_mat("PineNeedles", (0.03, 0.08, 0.025, 1), (0.08, 0.16, 0.05, 1), scale=1.0),
        "rock": noise_mat("Rock", (0.18, 0.18, 0.17, 1), (0.38, 0.38, 0.35, 1), scale=2.0),
        "shrub": noise_mat("Shrub", (0.03, 0.09, 0.03, 1), (0.10, 0.20, 0.06, 1), scale=2.0),
        "heather": noise_mat("Heather", (0.18, 0.10, 0.12, 1), (0.30, 0.20, 0.16, 1), scale=2.0),
        "wood": mat("Wood", (0.35, 0.22, 0.12, 1)),
        "si": mat("SI", (0.75, 0.12, 0.05, 1), rough=0.4),
        "flag_white": mat("FlagWhite", WHITE, rough=0.7),
        "flag_orange": mat("FlagOrange", ORANGE, rough=0.7),
    }

    # The camera's run: in from far away, weaving a little between the trees, slowing into the clearing
    path = []
    for i in range(80):
        y = -95 + i * (91 / 79)
        x = 2.2 * math.sin(y * 0.09) * min(1, (-4 - y) / 25)
        path.append((x, y))
    g = ground(path)

    # Tree shapes to copy
    protos = bpy.data.collections.new("Protos")
    sc.collection.children.link(protos)
    shapes = []
    for i in range(3):
        o = spruce(f"Spruce{i}", mats); link(o, protos); shapes.append(o)
    for i in range(3):
        o = pine(f"Pine{i}", mats); link(o, protos); shapes.append(o)
    rocks = []
    for i in range(3):
        o = rock(f"Rock{i}", mats, rnd); link(o, protos); rocks.append(o)
    bushes = []
    for i in range(4):
        o = bush(f"Bush{i}", mats, rnd); link(o, protos); bushes.append(o)
    lg = log("Log", mats); link(lg, protos)
    protos.hide_render = True
    protos.hide_viewport = True

    forest = bpy.data.collections.new("Forest")
    sc.collection.children.link(forest)

    def near_path(x, y, gap):
        for px, py in path:
            if (x - px) ** 2 + (y - py) ** 2 < gap * gap:
                return True
        return False

    placed = 0
    tries = 0
    while placed < 900 and tries < 20000:
        tries += 1
        x, y = rnd.uniform(-60, 60), rnd.uniform(-110, 45)
        if x * x + y * y < 7.5 ** 2:    # the clearing round the control
            continue
        if near_path(x, y, 1.9):
            continue
        proto = rnd.choice(shapes)
        o = proto.copy()
        forest.objects.link(o)
        z = ground_height(g, x, y)
        o.location = (x, y, z - 0.2)
        sc_ = rnd.uniform(0.75, 1.3)
        o.scale = (sc_, sc_, sc_ * rnd.uniform(0.9, 1.2))
        o.rotation_euler = (rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04), rnd.uniform(0, 6.28))
        placed += 1
    for _ in range(70):
        x, y = rnd.uniform(-30, 30), rnd.uniform(-100, 20)
        if near_path(x, y, 1.4) or x * x + y * y < 2.5 ** 2:
            continue
        o = rnd.choice(rocks).copy()
        forest.objects.link(o)
        o.location = (x, y, ground_height(g, x, y) - 0.1)
        s_ = rnd.uniform(0.3, 1.1)
        o.scale = (o.scale[0] * s_, o.scale[1] * s_, o.scale[2] * s_)
        o.rotation_euler = (0, 0, rnd.uniform(0, 6.28))

    for _ in range(4000):
        x, y = rnd.uniform(-35, 35), rnd.uniform(-105, 25)
        if near_path(x, y, 0.9) or x * x + y * y < 1.5 ** 2:
            continue
        o = rnd.choice(bushes).copy()
        forest.objects.link(o)
        o.location = (x, y, ground_height(g, x, y) - 0.05)
        s_ = rnd.uniform(0.5, 1.3)
        o.scale = (o.scale[0] * s_, o.scale[1] * s_, o.scale[2] * s_)
        o.rotation_euler = (0, 0, rnd.uniform(0, 6.28))
    for _ in range(25):
        x, y = rnd.uniform(-30, 30), rnd.uniform(-100, 15)
        if near_path(x, y, 4.5) or x * x + y * y < 6 ** 2:
            continue
        o = lg.copy()
        forest.objects.link(o)
        o.location = (x, y, ground_height(g, x, y))
        o.rotation_euler = (rnd.uniform(-0.05, 0.05), math.pi / 2, rnd.uniform(0, 6.28))

    control_flag(mats)

    # Light: low warm morning sun through the trees, cool sky
    bpy.ops.object.light_add(type="SUN", location=(0, 0, 10))
    sun = bpy.context.active_object
    sun.data.energy = 7.0
    sun.data.color = (1.0, 0.72, 0.42)
    sun.data.angle = math.radians(2)
    sun.rotation_euler = (math.radians(74), 0, math.radians(200))

    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    wn = world.node_tree.nodes
    bg = wn["Background"]
    bg.inputs["Strength"].default_value = 0.6
    # A low morning sky: warm near the horizon, blue above
    try:
        sky = wn.new("ShaderNodeTexSky")
        for t in ("MULTIPLE_SCATTERING", "SINGLE_SCATTERING", "NISHITA"):
            try:
                sky.sky_type = t
                break
            except Exception:
                pass
        sky.sun_elevation = math.radians(9)
        sky.sun_rotation = math.radians(200 - 90)
        sky.sun_disc = False
        world.node_tree.links.new(sky.outputs[0], bg.inputs["Color"])
        bg.inputs["Strength"].default_value = 0.35
    except Exception as e:
        print("no sky texture", e)
        bg.inputs["Color"].default_value = (0.62, 0.66, 0.62, 1)

    # Morning mist in a big box round the forest (a mist on the world itself would hide the sun and sky),
    # lit by the sun so its beams show between the trunks
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, -35, 12))
    mist = bpy.context.active_object
    mist.name = "Mist"
    mist.scale = (140, 170, 24)
    mm = bpy.data.materials.new("Mist")
    mm.use_nodes = True
    nt = mm.node_tree
    nt.nodes.remove(nt.nodes["Principled BSDF"])
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    vol.inputs["Density"].default_value = 0.006
    vol.inputs["Color"].default_value = (1.0, 0.88, 0.7, 1)
    vol.inputs["Anisotropy"].default_value = 0.55
    nt.links.new(vol.outputs[0], nt.nodes["Material Output"].inputs["Volume"])
    mist.data.materials.append(mm)

    # Camera: a running pace at first (with a light bob), then easing into the clearing and turning to the flag
    bpy.ops.object.camera_add()
    cam = bpy.context.active_object
    sc.camera = cam
    cam.data.lens = 24
    cam.data.dof.use_dof = True
    cam.data.dof.aperture_fstop = 2.8
    target = bpy.data.objects.new("Target", None)
    sc.collection.objects.link(target)
    tr = cam.constraints.new("TRACK_TO")
    tr.target = target
    tr.track_axis = "TRACK_NEGATIVE_Z"
    tr.up_axis = "UP_Y"
    cam.data.dof.focus_object = target

    def ease(t):
        # fast at first, slowing to a stop: the run ends at the flag
        return 1 - (1 - t) ** 2.6

    flag_pos = Vector((0.30, 0, 1.15))
    for f in range(1, LENGTH + 1):
        t = (f - 1) / (LENGTH - 1)
        run = min(1.0, t / 0.86)
        k = ease(run)
        y = -95 + k * 92.6          # ends 2.4 m before the flag
        x = 2.2 * math.sin(y * 0.09) * min(1, max(0.0, (-4 - y) / 25))
        speed = 1 - k
        bob = 0.06 * math.sin(f * 0.95) * speed
        z = ground_height(g, x, y) * 0.5 + 1.25 + bob + 0.25 * speed
        # in the last part, swing to the side a little so the flag is seen at an angle
        side = max(0.0, (t - 0.65) / 0.35)
        side = side * side * (3 - 2 * side)
        x += -0.5 * side
        z -= 0.2 * side            # crouch a little: the flag against the sky
        cam.location = (x, y, z)
        cam.keyframe_insert("location", frame=f)
        look_far = Vector((x * 0.3, y + 25, 1.3))
        w = min(1.0, max(0.0, (t - 0.45) / 0.35))
        w = w * w * (3 - 2 * w)
        # at the end the flag sits low in the picture, leaving the sky above it for the title
        target.location = look_far.lerp(flag_pos + Vector((0, 0, 0.32 * side)), w)
        target.keyframe_insert("location", frame=f)
        cam.data.lens = 24 + 11 * w
        cam.data.keyframe_insert("lens", frame=f)

    for fc in (cam.animation_data.action.fcurves if cam.animation_data and cam.animation_data.action and hasattr(cam.animation_data.action, "fcurves") else []):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    return sc


def setup_render(sc, a):
    w, h = (int(v) for v in a.res.split("x"))
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    if a.engine == "CYCLES":
        sc.render.engine = "CYCLES"
        cy = sc.cycles
        cy.device = "CPU"
        cy.samples = a.samples
        cy.use_denoising = True
        cy.max_bounces = 4
        cy.volume_bounces = 0
        cy.volume_step_rate = 4.0
        cy.use_adaptive_sampling = True
    else:
        sc.render.engine = "BLENDER_EEVEE"
        sc.eevee.taa_render_samples = a.samples
    sc.render.use_motion_blur = True
    sc.render.motion_blur_shutter = 0.5
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.look = "AgX - Punchy"
    sc.view_settings.exposure = 0.45
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False


def main():
    a = args()
    clear()
    sc = build()
    setup_render(sc, a)
    if a.blend:
        bpy.ops.wm.save_as_mainfile(filepath=a.blend)
    if a.still:
        for f in (int(v) for v in a.still.split(",")):
            sc.frame_set(f)
            sc.render.filepath = f"{a.out}/still_{f:04d}.png"
            bpy.ops.render.render(write_still=True)
        return
    first, last = (int(v) for v in a.frames.split("-"))
    for f in [last] + list(range(first, last)):
        sc.frame_set(f)
        sc.render.filepath = f"{a.out}/f_{f:04d}.png"
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()

# Joining the frames into the film Unity plays:
#   ffmpeg -framerate 30 -i frames/f_%04d.png -c:v libx264 -pix_fmt yuv420p -crf 20 -profile:v high -movflags +faststart intro.mp4
