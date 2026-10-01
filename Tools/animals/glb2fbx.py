# Blender (background): converts a Sketchfab GLB (skinned, animated) into an FBX for Unity – mesh, skin,
# every animation as its own take. No textures: the colours come from glb-textures.py (Blender loses them anyway);
# sizes and materials are set in Unity (Editor/AnimalAssets).
#   python3 Tools/animals/glb-textures.py <in.glb> <name>
#   Blender -b -P Tools/animals/glb2fbx.py -- <in.glb> Assets/PhotoReal/Animals/<name>/<name>.fbx
import bpy, sys
args = sys.argv[sys.argv.index("--") + 1:]
src, dst = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
# the glTF importer adds a sphere as the bones' display shape – not part of the animal
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.parent is None and o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o, do_unlink=True)
# textures are taken from the GLB separately (glb-textures.py) – not a second copy inside the FBX
for img in list(bpy.data.images):
    bpy.data.images.remove(img)
# every action a clip: push each onto the armature as an NLA strip so the exporter bakes them all
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
print("armatures", [a.name for a in arms], "actions", [a.name for a in bpy.data.actions])
bpy.ops.export_scene.fbx(
    filepath=dst, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.5,
    path_mode='STRIP', embed_textures=False, mesh_smooth_type='FACE', use_armature_deform_only=True)
print("written", dst)
