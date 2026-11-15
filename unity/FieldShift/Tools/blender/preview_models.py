"""Render quick previews of the exported FBX models (developer check only).
    Blender -b -P Tools/blender/preview_models.py -- <models folder> <out folder> [hdri]
"""
import bpy, math, os, sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
hdri = args[2] if len(args) > 2 else None
os.makedirs(out, exist_ok=True)

for name, dist in (("Tractor", 9), ("Barn", 32), ("Farmhouse", 26), ("Seeder", 10), ("Harvester", 11), ("GrainBin", 20)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(src, name + ".fbx"))
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    lo = Vector((min(min((o.matrix_world @ Vector(c)).x for c in o.bound_box) for o in objs), min(min((o.matrix_world @ Vector(c)).y for c in o.bound_box) for o in objs), 0))
    hi = Vector((max(max((o.matrix_world @ Vector(c)).x for c in o.bound_box) for o in objs), max(max((o.matrix_world @ Vector(c)).y for c in o.bound_box) for o in objs), max(max((o.matrix_world @ Vector(c)).z for c in o.bound_box) for o in objs)))
    centre = (lo + hi) / 2
    scn = bpy.context.scene
    scn.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_WORKBENCH"
    scn.render.resolution_x, scn.render.resolution_y = 960, 600
    world = bpy.data.worlds.new("W"); scn.world = world
    world.use_nodes = True
    nt = world.node_tree
    if hdri:
        env = nt.nodes.new("ShaderNodeTexEnvironment"); env.image = bpy.data.images.load(hdri)
        nt.links.new(env.outputs["Color"], nt.nodes["Background"].inputs["Color"])
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); scn.collection.objects.link(sun)
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
    g = bpy.context.active_object; gm = bpy.data.materials.new("Ground"); gm.diffuse_color = (0.25, 0.32, 0.15, 1)
    try:
        gm.use_nodes = True; gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.32, 0.15, 1)
    except Exception: pass
    g.data.materials.append(gm)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scn.collection.objects.link(cam); scn.camera = cam
    cam.data.lens = 40
    d = Vector((-0.75, -1.0, 0.45)).normalized() * dist
    cam.location = centre + d + Vector((0, 0, (hi.z - lo.z) * 0.15))
    look = (centre + Vector((0, 0, (hi.z) * 0.1))) - cam.location
    cam.rotation_euler = look.to_track_quat("-Z", "Y").to_euler()
    scn.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", name)
