"""
Field Shift farm models, built procedurally in Blender and exported as FBX for Unity.

    /Applications/Blender.app/Contents/MacOS/Blender -b -P Tools/blender/build_models.py -- <output folder>

Everything is generic (no real-world brand liveries). Objects face -Y in Blender. Materials are named by role
(Paint, Dark, Glass, Tire, Rim, Chrome, LightWhite, LightAmber, Siding, Trim, Roof, Shingles, Steel, Implement)
and the game assigns its own URP materials and textures by those names.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.path.join(os.path.dirname(__file__), "out")
os.makedirs(OUT, exist_ok=True)

COLORS = {
    "Paint": (0.55, 0.07, 0.05), "Dark": (0.05, 0.05, 0.055), "Glass": (0.12, 0.16, 0.18), "Tire": (0.03, 0.03, 0.03),
    "Rim": (0.42, 0.42, 0.42), "Chrome": (0.8, 0.8, 0.82), "LightWhite": (1, 1, 0.95), "LightAmber": (1, 0.55, 0.05),
    "Implement": (0.15, 0.22, 0.32), "Siding": (0.62, 0.12, 0.09), "Trim": (0.92, 0.91, 0.87), "Roof": (0.4, 0.42, 0.44),
    "Shingles": (0.22, 0.22, 0.24), "HouseSiding": (0.88, 0.87, 0.82), "Steel": (0.7, 0.72, 0.74), "Brick": (0.42, 0.18, 0.13),
}


# ------------------------------------------------------------------ helpers
def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        c = COLORS.get(name, (0.5, 0.5, 0.5))
        m.diffuse_color = (*c, 1)
        try:
            m.use_nodes = True
            bsdf = m.node_tree.nodes.get("Principled BSDF")
            if bsdf:
                bsdf.inputs["Base Color"].default_value = (*c, 1)
                bsdf.inputs["Roughness"].default_value = 0.35 if name in ("Paint", "Chrome", "Glass") else 0.7
                if name in ("Chrome", "Rim", "Steel"):
                    bsdf.inputs["Metallic"].default_value = 0.8
        except Exception:
            pass
    return m


def link(obj, parent=None):
    if obj.name not in bpy.context.scene.collection.objects:
        for c in obj.users_collection:
            c.objects.unlink(obj)
        bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
        obj.matrix_parent_inverse = parent.matrix_world.inverted()
    return obj


def mesh_obj(name, bm, material, parent=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    obj = bpy.data.objects.new(name, me)
    obj.data.materials.append(mat(material))
    return link(obj, parent)


def box(name, size, loc, material, parent=None, bevel=0.0, rot=(0, 0, 0), taper=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if taper:  # taper the -Y end: scale x/z of verts with y < 0
        for v in bm.verts:
            if v.co.y < 0:
                v.co.x *= taper[0]; v.co.z = v.co.z * taper[1] + (taper[2] if len(taper) > 2 else 0)
    obj = mesh_obj(name, bm, material, parent)
    obj.location = loc
    obj.rotation_euler = [math.radians(a) for a in rot]
    if bevel > 0:
        b = obj.modifiers.new("Bevel", "BEVEL"); b.width = bevel; b.segments = 3; b.limit_method = "ANGLE"
    return obj


def cyl(name, r, depth, loc, material, parent=None, axis="Z", verts=32, r2=None, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r if r2 is None else r2, depth=depth)
    if axis == "X":
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    elif axis == "Y":
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, "X"))
    obj = mesh_obj(name, bm, material, parent)
    obj.location = loc
    if bevel > 0:
        b = obj.modifiers.new("Bevel", "BEVEL"); b.width = bevel; b.segments = 2; b.limit_method = "ANGLE"
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    return link(e, parent)


def box_uv(obj, scale=1.0):
    """World-scale box mapping so tiling textures keep a real size (1 UV unit = 1/scale metres)."""
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            co = obj.matrix_world @ l.vert.co
            if ax == 0: u, v = co.y, co.z
            elif ax == 1: u, v = co.x, co.z
            else: u, v = co.x, co.y
            l[uv].uv = (u * scale, v * scale)
    bm.to_mesh(me); bm.free()


def export(name, root):
    bpy.ops.object.select_all(action="DESELECT")
    def sel(o):
        o.select_set(True)
        for c in o.children:
            sel(c)
    sel(root)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, object_types={"MESH", "EMPTY"}, use_mesh_modifiers=True, mesh_smooth_type="FACE",
                             add_leaf_bones=False, bake_anim=False)
    print("exported", path)


# ------------------------------------------------------------------ wheel with chevron lugs
def wheel(name, radius, width, loc, parent, rim_mat="Rim", lugs=26, side=1):
    hub = empty(name, loc, parent)
    tire_r = radius * 0.93
    t = cyl(name + "_Tire", tire_r, width, (0, 0, 0), "Tire", hub, axis="X", verts=48, bevel=width * 0.18)
    # recessed steel rim with a dished centre and wheel nuts; most of the face is rubber sidewall
    cyl(name + "_Wall", radius * 0.82, width * 1.005, (0, 0, 0), "Tire", hub, axis="X", verts=48, bevel=width * 0.06)
    cyl(name + "_Rim", radius * 0.5, width * 0.94, (side * 0.005, 0, 0), rim_mat, hub, axis="X", verts=32, bevel=0.015)
    cyl(name + "_Dish", radius * 0.34, width * 0.98, (side * 0.01, 0, 0), "Dark", hub, axis="X", verts=24)
    cyl(name + "_Hub", radius * 0.13, width * 1.12, (side * 0.03, 0, 0), "Chrome", hub, axis="X", verts=16)
    for k in range(8):
        a = k / 8 * math.tau
        cyl(f"{name}_Nut{k}", radius * 0.025, width * 1.1, (side * 0.03, math.cos(a) * radius * 0.2, math.sin(a) * radius * 0.2), "Chrome", hub, axis="X", verts=6)
    # chevron lugs: two angled bars per pitch, arrayed around the axle
    for half in (-1, 1):
        lug_len = width * 0.55
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=Vector((lug_len, 0.07, radius * 0.08)), verts=bm.verts)
        lug = mesh_obj(f"{name}_Lugs{'L' if half < 0 else 'R'}", bm, "Tire", hub)
        lug.location = (half * width * 0.24, 0, tire_r + radius * 0.03)
        lug.rotation_euler = (0, 0, math.radians(28 * half))
        pivot = empty(f"{name}_LugPivot{half}", (0, 0, 0), hub)
        pivot.rotation_euler = (math.radians(360 / lugs), 0, 0)
        arr = lug.modifiers.new("Array", "ARRAY")
        arr.count = lugs; arr.use_relative_offset = False; arr.use_object_offset = True; arr.offset_object = pivot
        # array pivots around the lug's origin: move origin to the axle so the copies go round the wheel
        lug.data.transform(Matrix.Translation(Vector((0, 0, 0))))
        me = lug.data
        off = Vector(lug.location)
        rot = lug.rotation_euler.to_matrix().to_4x4()
        me.transform(Matrix.Translation(off) @ rot)
        lug.location = (0, 0, 0); lug.rotation_euler = (0, 0, 0)
        if half > 0:
            for p in me.polygons:
                pass
        # offset alternate side by half a pitch
        lug.rotation_euler = (math.radians(180 / lugs) if half > 0 else 0, 0, 0)
    return hub


# ------------------------------------------------------------------ tractor
def tractor():
    reset()
    root = empty("Tractor", (0, 0, 0))
    # chassis and engine
    box("Chassis", (0.85, 4.0, 0.45), (0, -0.55, 0.72), "Dark", root, bevel=0.03)
    box("Hood", (1.0, 2.75, 0.85), (0, -1.38, 1.35), "Paint", root, bevel=0.09, taper=(0.86, 0.88, -0.02))
    box("HoodTop", (0.78, 2.6, 0.06), (0, -1.38, 1.8), "Paint", root, bevel=0.03, taper=(0.86, 1, 0))
    g = box("Grille", (0.78, 0.06, 0.62), (0, -2.76, 1.32), "Dark", root, bevel=0.02)
    for i in range(7):
        box(f"GrilleSlat{i}", (0.8, 0.05, 0.03), (0, -2.8, 1.08 + i * 0.075), "Chrome", root)
    for sx in (-1, 1):
        box(f"Headlight{sx}", (0.16, 0.05, 0.08), (sx * 0.28, -2.79, 1.58), "LightWhite", root, bevel=0.01)
        box(f"HoodVent{sx}", (0.02, 1.2, 0.25), (sx * 0.5, -1.2, 1.35), "Dark", root)
    box("FrontWeights", (0.95, 0.35, 0.45), (0, -3.0, 0.92), "Dark", root, bevel=0.03)
    for i in range(5):
        box(f"Weight{i}", (0.16, 0.42, 0.52), (-0.36 + i * 0.18, -3.12, 0.92), "Dark", root, bevel=0.02)
    box("FrontAxle", (1.7, 0.18, 0.16), (0, -1.75, 0.62), "Dark", root)
    # cab
    cab_y, cab_z = 0.45, 2.2
    box("CabBase", (1.75, 1.75, 0.35), (0, cab_y, 1.62), "Dark", root, bevel=0.03)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"Pillar{sx}{sy}", (0.08, 0.08, 1.3), (sx * 0.84, cab_y + sy * 0.84, cab_z + 0.1), "Dark", root, bevel=0.015)
    box("GlassFront", (1.62, 0.03, 1.22), (0, cab_y - 0.85, cab_z + 0.1), "Glass", root)
    box("GlassBack", (1.62, 0.03, 1.22), (0, cab_y + 0.85, cab_z + 0.1), "Glass", root)
    box("GlassL", (0.03, 1.62, 1.22), (-0.85, cab_y, cab_z + 0.1), "Glass", root)
    box("GlassR", (0.03, 1.62, 1.22), (0.85, cab_y, cab_z + 0.1), "Glass", root)
    box("Roof", (1.95, 2.05, 0.16), (0, cab_y + 0.05, 2.86), "Paint", root, bevel=0.06)
    box("RoofCap", (1.7, 1.8, 0.08), (0, cab_y + 0.05, 2.96), "Paint", root, bevel=0.04)
    cyl("Beacon", 0.07, 0.16, (0.55, cab_y + 0.6, 3.08), "LightAmber", root, verts=16)
    for sx in (-1, 1):
        box(f"WorkLight{sx}", (0.14, 0.06, 0.08), (sx * 0.6, cab_y - 0.98, 2.84), "LightWhite", root)
        box(f"MirrorArm{sx}", (0.5, 0.03, 0.03), (sx * 1.1, cab_y - 0.75, 2.5), "Dark", root)
        box(f"Mirror{sx}", (0.04, 0.16, 0.28), (sx * 1.35, cab_y - 0.75, 2.38), "Dark", root, bevel=0.01)
    box("SteeringWheel", (0.32, 0.04, 0.32), (0, cab_y - 0.45, 2.05), "Dark", root, rot=(35, 0, 0))
    box("Seat", (0.5, 0.45, 0.5), (0, cab_y + 0.2, 1.95), "Dark", root, bevel=0.05)
    # fenders over the rear wheels (curved shells)
    for sx in (-1, 1):
        bm = bmesh.new()
        segs, r_in, w = 14, 1.08, 0.78
        verts = []
        for i in range(segs + 1):
            a = math.radians(-10 + 125 * i / segs)
            for rr in (r_in, r_in + 0.06):
                for xx in (-w / 2, w / 2):
                    verts.append(bm.verts.new((xx, -math.cos(a) * rr, math.sin(a) * rr)))
        bm.verts.ensure_lookup_table()
        for i in range(segs):
            q = lambda k, ri, xi: bm.verts[(k * 4) + ri * 2 + xi]
            for (a1, a2, a3, a4) in (((i, 0, 0), (i + 1, 0, 0), (i + 1, 0, 1), (i, 0, 1)), ((i, 1, 0), (i, 1, 1), (i + 1, 1, 1), (i + 1, 1, 0)),
                                     ((i, 0, 0), (i, 1, 0), (i + 1, 1, 0), (i + 1, 0, 0)), ((i, 0, 1), (i + 1, 0, 1), (i + 1, 1, 1), (i, 1, 1))):
                bm.faces.new([q(*a1), q(*a2), q(*a3), q(*a4)])
        f = mesh_obj(f"Fender{sx}", bm, "Paint", root)
        f.location = (sx * 1.02, 0.95, 1.0)
        box(f"FenderLight{sx}", (0.1, 0.06, 0.12), (sx * 1.25, -0.05, 2.1), "LightAmber", root)
    # exhaust, steps, tank, hitch
    cyl("Exhaust", 0.065, 1.55, (0.42, -0.45, 2.45), "Chrome", root, verts=16)
    cyl("ExhaustCap", 0.08, 0.1, (0.42, -0.45, 3.24), "Dark", root, verts=16)
    cyl("AirIntake", 0.08, 1.1, (-0.42, -0.45, 2.2), "Paint", root, verts=16)
    for i in range(3):
        box(f"Step{i}", (0.4, 0.22, 0.04), (-0.98, -0.3 + i * 0.02, 0.55 + i * 0.32), "Dark", root)
    box("FuelTank", (0.4, 0.9, 0.5), (-0.68, -0.45, 1.0), "Dark", root, bevel=0.06)
    box("HitchBar", (1.4, 0.12, 0.12), (0, 1.95, 0.6), "Dark", root)
    for sx in (-1, 1):
        box(f"HitchArm{sx}", (0.08, 0.9, 0.1), (sx * 0.5, 1.55, 0.7), "Dark", root, rot=(-10, 0, 0))
    box("RearAxle", (2.2, 0.2, 0.2), (0, 0.95, 0.95), "Dark", root)
    # wheels (origins at the hubs so the game can spin them)
    wheel("WheelRL", 0.98, 0.62, (-1.05, 0.95, 0.98), root, side=-1)
    wheel("WheelRR", 0.98, 0.62, (1.05, 0.95, 0.98), root, side=1)
    wheel("WheelFL", 0.66, 0.42, (-0.92, -1.75, 0.66), root, side=-1)
    wheel("WheelFR", 0.66, 0.42, (0.92, -1.75, 0.66), root, side=1)
    export("Tractor", root)


# ------------------------------------------------------------------ implements
def seeder():
    reset()
    root = empty("Seeder", (0, 0, 0))
    W = 6.0
    box("Tongue", (0.14, 2.2, 0.14), (0, -1.4, 0.75), "Dark", root)
    box("Frame", (W, 0.18, 0.18), (0, 0, 0.75), "Implement", root, bevel=0.02)
    box("FrameRear", (W, 0.12, 0.12), (0, 0.9, 0.6), "Implement", root)
    for i, x in enumerate((-1.6, 1.6)):
        box(f"Hopper{i}", (2.6, 1.0, 0.9), (x, 0.3, 1.45), "Implement", root, bevel=0.06, taper=(1, 1, 0))
        box(f"Lid{i}", (2.65, 1.05, 0.06), (x, 0.3, 1.93), "Dark", root, bevel=0.02)
    for i in range(16):
        x = -W / 2 + 0.2 + i * (W - 0.4) / 15
        cyl(f"Disc{i}", 0.22, 0.03, (x, 0.45, 0.3), "Chrome", root, axis="X", verts=20)
        cyl(f"Press{i}", 0.15, 0.08, (x, 0.95, 0.2), "Dark", root, axis="X", verts=16)
        box(f"Shank{i}", (0.04, 0.5, 0.06), (x, 0.65, 0.48), "Dark", root, rot=(30, 0, 0))
    for sx in (-1, 1):
        cyl(f"Transport{sx}", 0.38, 0.25, (sx * 2.6, 0.1, 0.38), "Tire", root, axis="X", verts=24, bevel=0.04)
    export("Seeder", root)


def harvester():
    reset()
    root = empty("Harvester", (0, 0, 0))
    W = 6.0
    box("Tongue", (0.14, 2.4, 0.14), (0.8, -1.5, 0.8), "Dark", root)
    box("Header", (W, 1.2, 0.7), (0, 0.4, 0.55), "Implement", root, bevel=0.05)
    cyl("Reel", 0.32, W * 0.96, (0, -0.25, 0.45), "Dark", root, axis="X", verts=20)
    for i in range(30):
        x = -W / 2 + 0.15 + i * (W - 0.3) / 29
        box(f"Tine{i}", (0.02, 0.25, 0.02), (x, -0.55, 0.3), "Chrome", root, rot=(40, 0, 0))
    box("Body", (1.5, 2.2, 1.7), (1.9, 1.3, 1.3), "Implement", root, bevel=0.08)
    box("BodyTop", (1.3, 1.9, 0.2), (1.9, 1.3, 2.2), "Dark", root, bevel=0.05)
    cyl("SpoutBase", 0.22, 1.2, (1.9, 1.0, 2.7), "Implement", root, verts=16)
    # spout: from the top of the base, up and out over the side
    start = Vector((1.9, 1.0, 3.25)); d = Vector((0.55, -0.55, 0.62)).normalized(); length = 2.6
    sp = cyl("Spout", 0.2, length, tuple(start + d * (length / 2)), "Implement", root, verts=16)
    sp.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    cyl("SpoutElbow", 0.22, 0.3, tuple(start), "Implement", root, verts=16)
    hood = cyl("SpoutHood", 0.26, 0.5, tuple(start + d * length), "Dark", root, verts=16)
    hood.rotation_euler = sp.rotation_euler
    for sx in (1.2, 2.6):
        cyl(f"Wheel{sx}", 0.5, 0.35, (sx, 1.6, 0.5), "Tire", root, axis="X", verts=24, bevel=0.05)
    export("Harvester", root)


# ------------------------------------------------------------------ buildings
def prism(name, profile, length, material, parent, loc=(0, 0, 0)):
    """Extrude a closed 2D profile (x, z) along Y."""
    bm = bmesh.new()
    front = [bm.verts.new((x, -length / 2, z)) for x, z in profile]
    back = [bm.verts.new((x, length / 2, z)) for x, z in profile]
    bm.faces.new(front[::-1]); bm.faces.new(back)
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([front[i], front[j], back[j], back[i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = mesh_obj(name, bm, material, parent)
    obj.location = loc
    return obj


def slab(name, p0, p1, length, thick, material, parent):
    """A roof plane from p0 to p1 (x, z) along Y, with thickness."""
    dx, dz = p1[0] - p0[0], p1[1] - p0[1]
    L = math.hypot(dx, dz)
    obj = box(name, (L, length, thick), ((p0[0] + p1[0]) / 2, 0, (p0[1] + p1[1]) / 2), material, parent)
    obj.rotation_euler = (0, -math.atan2(dz, dx), 0)
    return obj


def barn():
    reset()
    root = empty("Barn", (0, 0, 0))
    Wd, L, wall, knee, ridge = 12.0, 18.0, 4.6, 7.4, 9.2
    prof = [(-Wd / 2, 0), (Wd / 2, 0), (Wd / 2, wall), (Wd / 2 - 2.3, knee), (0, ridge), (-Wd / 2 + 2.3, knee), (-Wd / 2, wall)]
    walls = prism("Walls", prof, L, "Siding", root)
    box_uv(walls, 0.5)
    o = 0.45
    for side in (-1, 1):
        p_eave = (side * (Wd / 2 + o), wall - 0.25)
        p_knee = (side * (Wd / 2 - 2.3), knee)
        slab(f"RoofLow{side}", p_eave, (p_knee[0] + side * 0.05, p_knee[1] + 0.02), L + 2 * o, 0.12, "Roof", root)
        slab(f"RoofHigh{side}", (p_knee[0] + side * 0.05, p_knee[1]), (0, ridge + 0.1), L + 2 * o, 0.12, "Roof", root)
    for r in [o for o in root.children if o.name.startswith("Roof")]:
        box_uv(r, 0.5)
    # trim on the gable ends and corners
    for sy in (-1, 1):
        y = sy * (L / 2 + 0.03)
        for (a, b) in zip(prof[2:] + [prof[0]], prof[3:] + [prof[1]]):
            pass
        for i in range(2, len(prof)):
            a, b = prof[i], prof[(i + 1) % len(prof)] if i + 1 < len(prof) else prof[2]
        for (a, b) in ((prof[2], prof[3]), (prof[3], prof[4]), (prof[4], prof[5]), (prof[5], prof[6])):
            dx, dz = b[0] - a[0], b[1] - a[1]
            t = box(f"GableTrim{sy}{a}", (math.hypot(dx, dz) + 0.1, 0.06, 0.22), ((a[0] + b[0]) / 2, y, (a[1] + b[1]) / 2), "Trim", root)
            t.rotation_euler = (0, -math.atan2(dz, dx), 0)
        # big sliding doors with white frame and X braces
        dw, dh = 4.2, 3.9
        box(f"Door{sy}", (dw, 0.08, dh), (0, y, dh / 2), "Siding", root)
        for (w_, h_, x_, z_) in ((dw + 0.3, 0.2, 0, dh), (0.2, dh, -dw / 2, dh / 2), (0.2, dh, dw / 2, dh / 2), (0.2, dh, 0, dh / 2), (dw + 0.3, 0.2, 0, 0.1)):
            box(f"DoorFrame{sy}{x_}{z_}", (w_, 0.12, h_), (x_, y + sy * 0.03, z_), "Trim", root)
        for half in (-1, 1):
            cx = half * dw / 4
            d = math.hypot(dw / 2, dh)
            for sgn in (-1, 1):
                b = box(f"Brace{sy}{half}{sgn}", (d - 0.2, 0.1, 0.16), (cx, y + sy * 0.04, dh / 2), "Trim", root)
                b.rotation_euler = (0, sgn * math.atan2(dh, dw / 2), 0)
        box(f"Loft{sy}", (2.2, 0.08, 1.6), (0, y, 6.4), "Siding", root)
        box(f"LoftFrame{sy}", (2.5, 0.1, 1.9), (0, y - sy * 0.01, 6.4), "Trim", root)
        box(f"Corner{sy}L", (0.22, 0.22, wall), (-Wd / 2, y - sy * 0.1, wall / 2), "Trim", root)
        box(f"Corner{sy}R", (0.22, 0.22, wall), (Wd / 2, y - sy * 0.1, wall / 2), "Trim", root)
    for sx in (-1, 1):
        for i in range(4):
            yy = -L / 2 + 2.5 + i * 4.3
            box(f"Window{sx}{i}", (0.06, 1.2, 1.0), (sx * (Wd / 2 + 0.02), yy, 2.6), "Glass", root)
            box(f"WindowFrame{sx}{i}", (0.08, 1.45, 1.25), (sx * (Wd / 2 + 0.01), yy, 2.6), "Trim", root)
    # cupola on the ridge
    box("Cupola", (1.4, 1.4, 1.3), (0, 0, ridge + 0.55), "Siding", root)
    box("CupolaTrim", (1.55, 1.55, 0.15), (0, 0, ridge + 1.2), "Trim", root)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=4, radius1=1.25, radius2=0.02, depth=0.9)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(45), 3, "Z"))
    cap = mesh_obj("CupolaRoof", bm, "Roof", root); cap.location = (0, 0, ridge + 1.7)
    box("Foundation", (Wd + 0.2, L + 0.2, 0.4), (0, 0, 0.0), "Steel", root)
    export("Barn", root)


def farmhouse():
    reset()
    root = empty("Farmhouse", (0, 0, 0))
    Wd, L, wall, ridge = 9.0, 11.0, 5.8, 8.6
    prof = [(-Wd / 2, 0), (Wd / 2, 0), (Wd / 2, wall), (0, ridge), (-Wd / 2, wall)]
    walls = prism("Walls", prof, L, "HouseSiding", root)
    box_uv(walls, 0.5)
    o = 0.5
    for side in (-1, 1):
        r = slab(f"Roof{side}", (side * (Wd / 2 + o), wall - 0.3), (0, ridge + 0.12), L + 2 * o, 0.15, "Shingles", root)
        box_uv(r, 0.5)
    # windows on all sides, two storeys
    for sy in (-1, 1):
        for x in (-2.6, 2.6):
            for z in (1.6, 4.2):
                if sy < 0 and x < 0 and z < 2: continue          # front door goes here
                box(f"W{sy}{x}{z}", (1.0, 0.06, 1.4), (x, sy * (L / 2 + 0.02), z), "Glass", root)
                box(f"WF{sy}{x}{z}", (1.25, 0.08, 1.65), (x, sy * (L / 2 + 0.01), z), "Trim", root)
                box(f"WS{sy}{x}{z}", (1.4, 0.2, 0.08), (x, sy * (L / 2 + 0.08), z - 0.85), "Trim", root)
        box(f"Gable{sy}", (0.8, 0.06, 0.8), (0, sy * (L / 2 + 0.02), 6.8), "Glass", root)
    for sx in (-1, 1):
        for y in (-3, 0, 3):
            for z in (1.6, 4.2):
                box(f"Ws{sx}{y}{z}", (0.06, 1.0, 1.4), (sx * (Wd / 2 + 0.02), y, z), "Glass", root)
                box(f"WFs{sx}{y}{z}", (0.08, 1.25, 1.65), (sx * (Wd / 2 + 0.01), y, z), "Trim", root)
    box("Door", (1.1, 0.08, 2.2), (-2.6, -(L / 2 + 0.03), 1.1), "Implement", root)
    box("DoorFrame", (1.35, 0.1, 2.4), (-2.6, -(L / 2 + 0.02), 1.2), "Trim", root)
    # porch across the front
    box("PorchDeck", (Wd + 0.6, 2.4, 0.3), (0, -(L / 2 + 1.2), 0.15), "Trim", root)
    r = slab("PorchRoof", (-(Wd / 2 + 0.3), 0), ((Wd / 2 + 0.3), 0), 2.6, 0.12, "Shingles", root)
    r.location = (0, -(L / 2 + 1.25), 3.0); r.rotation_euler = (math.radians(-12), 0, 0)
    for i in range(5):
        x = -Wd / 2 + i * Wd / 4
        box(f"Post{i}", (0.18, 0.18, 2.7), (x, -(L / 2 + 2.25), 1.65), "Trim", root, bevel=0.02)
    box("Rail", (Wd, 0.08, 0.08), (0, -(L / 2 + 2.25), 1.0), "Trim", root)
    box("Chimney", (0.8, 0.8, 3.6), (2.2, 2.5, 7.4), "Brick", root)
    for sy in (-1, 1):
        for (a, b) in ((prof[2], prof[3]), (prof[3], prof[4])):
            dx, dz = b[0] - a[0], b[1] - a[1]
            t = box(f"Fascia{sy}{a}", (math.hypot(dx, dz) + 0.5, 0.06, 0.2), ((a[0] + b[0]) / 2, sy * (L / 2 + o), (a[1] + b[1]) / 2 - 0.1), "Trim", root)
            t.rotation_euler = (0, -math.atan2(dz, dx), 0)
    box("Foundation", (Wd + 0.2, L + 0.2, 0.5), (0, 0, 0.0), "Brick", root)
    export("Farmhouse", root)


def grain_bin():
    reset()
    root = empty("GrainBin", (0, 0, 0))
    r, h = 4.2, 7.0
    body = cyl("Body", r, h, (0, 0, h / 2), "Steel", root, verts=48)
    # cylindrical UVs so corrugations run vertically
    me = body.data
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            co = l.vert.co
            l[uv].uv = (math.atan2(co.y, co.x) * r * 0.5, (co.z + h / 2) * 0.5)
    bm.to_mesh(me); bm.free()
    roof = cyl("Roof", r + 0.2, 2.4, (0, 0, h + 1.2), "Steel", root, verts=48, r2=0.5)
    cyl("Cap", 0.6, 0.6, (0, 0, h + 2.6), "Steel", root, verts=16, r2=0.3)
    for i in range(7):
        box(f"Ring{i}", (0.06, 0.06, 0.06), (0, 0, 0), "Steel", root)
    box("Ladder", (0.5, 0.06, h + 1.5), (0, -(r + 0.15), (h + 1.5) / 2), "Dark", root)
    export("GrainBin", root)


only = sys.argv[sys.argv.index("--") + 2:] if "--" in sys.argv else []
for fn in (tractor, seeder, harvester, barn, farmhouse, grain_bin):
    if not only or fn.__name__ in only:
        fn()
print("ALL MODELS DONE")
