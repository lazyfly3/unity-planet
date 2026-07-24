import bpy
import math
import os
import random
from mathutils import Vector


ROOT = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.abspath(os.path.join(ROOT, "..", ".."))
EXPORT_ROOT = os.path.join(PROJECT_ROOT, "Assets", "Models", "PlanetLowPolyKit")
REVIEW_ROOT = os.path.join(ROOT, "Reviews")
BLEND_PATH = os.path.join(ROOT, "LowPolyPlanetKit.blend")

os.makedirs(EXPORT_ROOT, exist_ok=True)
os.makedirs(REVIEW_ROOT, exist_ok=True)
for generated_name in os.listdir(EXPORT_ROOT):
    is_kit_asset = generated_name.startswith(
        ("Rock_", "Plant_", "Crystal_", "Landmark_", "Cloud_")
    )
    if is_kit_asset and generated_name.lower().endswith(".fbx"):
        os.remove(os.path.join(EXPORT_ROOT, generated_name))


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def material(name, color, roughness=0.8, metallic=0.0):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (*color, 1.0)
        mat.roughness = roughness
        mat.metallic = metallic
        mat.use_nodes = True
        principled = mat.node_tree.nodes.get("Principled BSDF")
        if principled is not None:
            principled.inputs["Base Color"].default_value = (*color, 1.0)
            principled.inputs["Roughness"].default_value = roughness
            principled.inputs["Metallic"].default_value = metallic
    return mat


MAT_ROCK = material("LP_Rock", (0.23, 0.29, 0.34), 0.92)
MAT_ROCK_LIGHT = material("LP_RockLight", (0.38, 0.43, 0.45), 0.9)
MAT_STEM = material("LP_Stem", (0.12, 0.28, 0.21), 0.88)
MAT_LEAF = material("LP_Leaf", (0.15, 0.48, 0.32), 0.82)
MAT_ACCENT = material("LP_Accent", (0.72, 0.28, 0.2), 0.72)
MAT_CRYSTAL = material("LP_Crystal", (0.14, 0.68, 0.82), 0.3, 0.15)
MAT_CRYSTAL_ALT = material("LP_CrystalAccent", (0.52, 0.22, 0.78), 0.28, 0.12)
MAT_CLOUD = material("LP_Cloud", (0.78, 0.84, 0.9), 0.95)


def active_material(obj, mat):
    if len(obj.data.materials) == 0:
        obj.data.materials.append(mat)
    else:
        obj.data.materials[0] = mat


def add_ico(name, location, scale, mat, subdivisions=1):
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=subdivisions,
        radius=1.0,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    active_material(obj, mat)
    return obj


def add_cone(name, location, radius1, radius2, depth, vertices, mat, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius1,
        radius2=radius2,
        depth=depth,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    active_material(obj, mat)
    return obj


def add_cube(name, location, scale, mat, rotation=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        mod = obj.modifiers.new("SmallBevel", "BEVEL")
        mod.width = bevel
        mod.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    active_material(obj, mat)
    return obj


def add_torus(name, location, major_radius, minor_radius, mat, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=12,
        minor_segments=5,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    active_material(obj, mat)
    return obj


def join_parts(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    min_z = min((obj.matrix_world @ vertex.co).z for vertex in obj.data.vertices)
    obj.location.z -= min_z
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    return obj


def distort_mesh(obj, seed, strength=0.18):
    rng = random.Random(seed)
    for vertex in obj.data.vertices:
        direction = vertex.co.normalized()
        vertex.co += direction * rng.uniform(-strength, strength)


def make_rock(index):
    rng = random.Random(100 + index)
    if index < 3:
        rock = add_ico(
            "rock",
            (0, 0, 0.8),
            (rng.uniform(0.8, 1.3), rng.uniform(0.7, 1.05), rng.uniform(0.7, 1.35)),
            MAT_ROCK if index == 1 else MAT_ROCK_LIGHT,
            2,
        )
        distort_mesh(rock, 500 + index, 0.24)
        parts = [rock]
        if index == 2:
            parts.append(add_ico("chip", (0.75, -0.1, 0.28), (0.45, 0.35, 0.35), MAT_ROCK, 1))
    elif index == 3:
        parts = [
            add_cone("column_a", (-0.34, 0, 1.2), 0.5, 0.35, 2.4, 7, MAT_ROCK, (0.08, 0.04, -0.08)),
            add_cone("column_b", (0.42, 0.1, 0.85), 0.42, 0.22, 1.7, 7, MAT_ROCK_LIGHT, (-0.18, 0.08, 0.13)),
        ]
    else:
        parts = [
            add_cube("arch_left", (-0.78, 0, 1.05), (0.46, 0.62, 2.1), MAT_ROCK, (0, -0.12, 0.06), 0.16),
            add_cube("arch_right", (0.78, 0, 1.05), (0.46, 0.62, 2.1), MAT_ROCK, (0, 0.12, -0.06), 0.16),
            add_cube("arch_top", (0, 0, 2.0), (1.65, 0.62, 0.48), MAT_ROCK_LIGHT, (0, 0, 0.04), 0.18),
        ]
    return join_parts(parts, f"Rock_{index:02d}")


def make_plant(index):
    parts = []
    if index == 1:
        for i, angle in enumerate((-0.7, -0.2, 0.25, 0.72)):
            x = math.sin(angle) * 0.35
            parts.append(add_cone(f"stem_{i}", (x, 0, 0.65 + i * 0.08), 0.13, 0.07, 1.35, 7, MAT_STEM, (0, angle * 0.3, -angle * 0.15)))
            parts.append(add_ico(f"cap_{i}", (x * 1.35, 0, 1.36 + i * 0.08), (0.5, 0.42, 0.22), MAT_ACCENT, 1))
    elif index == 2:
        parts.append(add_cone("core", (0, 0, 0.8), 0.34, 0.18, 1.6, 8, MAT_STEM))
        for i in range(7):
            angle = i * math.tau / 7
            parts.append(add_cone(
                f"leaf_{i}",
                (math.cos(angle) * 0.46, math.sin(angle) * 0.46, 0.5),
                0.34, 0.02, 1.3, 5, MAT_LEAF,
                (math.sin(angle) * 0.75, -math.cos(angle) * 0.75, angle),
            ))
    elif index == 3:
        parts.append(add_cone("trunk", (0, 0, 1.25), 0.28, 0.16, 2.5, 7, MAT_STEM))
        for i in range(6):
            angle = i * math.tau / 6
            parts.append(add_ico(
                f"fan_{i}",
                (math.cos(angle) * 0.72, math.sin(angle) * 0.72, 2.25 + (i % 2) * 0.18),
                (0.72, 0.2, 0.38), MAT_LEAF, 1,
            ))
    else:
        parts.append(add_ico("bulb", (0, 0, 0.9), (0.68, 0.68, 0.9), MAT_ACCENT, 2))
        for i in range(5):
            angle = i * math.tau / 5
            parts.append(add_cone(
                f"root_{i}",
                (math.cos(angle) * 0.4, math.sin(angle) * 0.4, 0.34),
                0.2, 0.04, 1.15, 6, MAT_STEM,
                (math.sin(angle) * 0.7, -math.cos(angle) * 0.7, angle),
            ))
        parts.append(add_cone("antenna", (0, 0, 1.85), 0.1, 0.025, 1.6, 6, MAT_STEM))
    return join_parts(parts, f"Plant_{index:02d}")


def make_crystal(index):
    rng = random.Random(800 + index)
    parts = []
    count = 4 + index
    for i in range(count):
        angle = i * math.tau / count + rng.uniform(-0.18, 0.18)
        radius = rng.uniform(0.12, 0.68)
        height = rng.uniform(0.9, 2.25) * (1.2 if i == 0 else 1)
        parts.append(add_cone(
            f"crystal_{i}",
            (math.cos(angle) * radius, math.sin(angle) * radius, height * 0.5),
            rng.uniform(0.18, 0.34), 0.0, height, 5 if index % 2 else 6,
            MAT_CRYSTAL if (i + index) % 3 else MAT_CRYSTAL_ALT,
            (rng.uniform(-0.18, 0.18), rng.uniform(-0.18, 0.18), angle),
        ))
    parts.append(add_ico("base", (0, 0, 0.2), (0.9, 0.75, 0.3), MAT_ROCK, 1))
    return join_parts(parts, f"Crystal_{index:02d}")


def make_landmark(index):
    parts = []
    if index == 1:
        parts = [
            add_cone("spire_main", (0, 0, 2.4), 0.9, 0.06, 4.8, 7, MAT_ROCK),
            add_cone("spire_side_a", (-0.75, 0.1, 1.5), 0.5, 0.02, 3.0, 6, MAT_ROCK_LIGHT, (0.1, -0.22, 0.08)),
            add_cone("spire_side_b", (0.72, -0.12, 1.25), 0.42, 0.02, 2.5, 6, MAT_ROCK_LIGHT, (-0.08, 0.28, -0.06)),
        ]
    elif index == 2:
        parts = [
            add_cube("leg_a", (-1.25, 0, 1.7), (0.7, 0.82, 3.4), MAT_ROCK, (0, -0.1, 0), 0.2),
            add_cube("leg_b", (1.25, 0, 1.7), (0.7, 0.82, 3.4), MAT_ROCK, (0, 0.1, 0), 0.2),
            add_cube("bridge", (0, 0, 3.2), (2.8, 0.82, 0.65), MAT_ROCK_LIGHT, (0, 0, 0.02), 0.2),
        ]
    elif index == 3:
        parts = [
            add_torus("ancient_ring", (0, 0, 2.2), 1.65, 0.28, MAT_ROCK_LIGHT, (math.pi / 2, 0, 0)),
            add_cube("pedestal", (0, 0, 0.35), (2.2, 1.0, 0.7), MAT_ROCK, (0, 0, 0), 0.15),
        ]
    else:
        parts = [
            add_cube("monolith", (0, 0, 2.2), (1.15, 0.7, 4.4), MAT_ROCK, (0.02, -0.08, 0.08), 0.16),
            add_cube("inlay", (0, -0.72, 2.25), (0.28, 0.08, 2.4), MAT_CRYSTAL, (0.02, -0.08, 0.08), 0.04),
            add_cube("base", (0, 0, 0.28), (1.8, 1.25, 0.56), MAT_ROCK_LIGHT, (0, 0, 0), 0.12),
        ]
    return join_parts(parts, f"Landmark_{index:02d}")


def make_cloud(index):
    rng = random.Random(1300 + index)
    parts = []
    count = 5 + index * 2
    for i in range(count):
        angle = rng.uniform(0, math.tau)
        radius = rng.uniform(0.0, 1.2 + index * 0.12)
        parts.append(add_ico(
            f"puff_{i}",
            (math.cos(angle) * radius, math.sin(angle) * radius * 0.55, rng.uniform(0.25, 0.8)),
            (rng.uniform(0.7, 1.3), rng.uniform(0.45, 0.82), rng.uniform(0.38, 0.72)),
            MAT_CLOUD,
            1,
        ))
    cloud = join_parts(parts, f"Cloud_{index:02d}")
    cloud.scale.z = 0.7
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return cloud


def make_lod(source, level):
    duplicate = source.copy()
    duplicate.data = source.data.copy()
    bpy.context.collection.objects.link(duplicate)
    base_name = source.name[:-5] if source.name.endswith("_LOD0") else source.name
    duplicate.name = f"{base_name}_LOD{level}"
    if level > 0:
        modifier = duplicate.modifiers.new("LowPolyDecimate", "DECIMATE")
        modifier.ratio = 0.55 if level == 1 else 0.28
        modifier.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = duplicate
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return duplicate


def export_fbx(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    kwargs = dict(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        bake_space_transform=False,
        object_types={"MESH"},
        add_leaf_bones=False,
        path_mode="AUTO",
        axis_forward="-Z",
        axis_up="Y",
    )
    bpy.ops.export_scene.fbx(**kwargs)


def setup_studio():
    bpy.ops.object.camera_add(location=(0, -24, 1.0))
    camera = bpy.context.object
    camera.name = "ReviewCamera"
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 17.0
    camera.rotation_euler = (Vector((0, 0, 0.7)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = camera

    bpy.ops.object.light_add(type="AREA", location=(-6, -8, 11))
    key = bpy.context.object
    key.name = "ReviewKey"
    key.data.energy = 1400
    key.data.shape = "DISK"
    key.data.size = 8
    key.rotation_euler = (math.radians(28), 0, math.radians(-28))

    bpy.ops.object.light_add(type="AREA", location=(7, -3, 5))
    fill = bpy.context.object
    fill.name = "ReviewFill"
    fill.data.energy = 650
    fill.data.size = 6
    fill.rotation_euler = (math.radians(65), 0, math.radians(55))

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1500
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.color = (0.025, 0.032, 0.045)


def add_label(text, location):
    bpy.ops.object.text_add(location=location)
    obj = bpy.context.object
    obj.data.body = text
    obj.data.align_x = "CENTER"
    obj.data.size = 0.36
    obj.data.extrude = 0.005
    obj.rotation_euler = (bpy.data.objects["ReviewCamera"].location - obj.location).to_track_quat("Z", "Y").to_euler()
    active_material(obj, MAT_CLOUD)
    return obj


def render_review(source):
    rotations = [
        ("FRONT", (0, 0, 0)),
        ("BACK", (0, 0, math.pi)),
        ("LEFT", (0, 0, math.pi / 2)),
        ("RIGHT", (0, 0, -math.pi / 2)),
        ("TOP", (math.pi / 2, 0, 0)),
        ("BOTTOM", (-math.pi / 2, 0, 0)),
    ]
    positions = [(-5.2, 0, 4.2), (0, 0, 4.2), (5.2, 0, 4.2), (-5.2, 0, -2.8), (0, 0, -2.8), (5.2, 0, -2.8)]
    review_objects = []
    hidden_states = [(obj, obj.hide_render) for obj in asset_collection.objects]
    for obj, _ in hidden_states:
        obj.hide_render = True
    bounds = max(source.dimensions.x, source.dimensions.y, source.dimensions.z, 0.1)
    # Leave a clear caption gutter even for wide landmarks viewed from above.
    display_scale = min(2.75 / bounds, 1.25)
    local_center = sum((Vector(corner) for corner in source.bound_box), Vector()) / 8.0
    for (label, rotation), position in zip(rotations, positions):
        duplicate = source.copy()
        duplicate.data = source.data
        bpy.context.collection.objects.link(duplicate)
        duplicate.rotation_euler = rotation
        duplicate.scale = (display_scale,) * 3
        duplicate.location = Vector(position) - duplicate.rotation_euler.to_matrix() @ (local_center * display_scale)
        duplicate.hide_render = False
        duplicate.hide_viewport = False
        review_objects.append(duplicate)
        label_offset = 2.35 if position[2] > 0.0 else 1.82
        review_objects.append(add_label(label, (position[0], -1.2, position[2] - label_offset)))
    bpy.context.scene.render.filepath = os.path.join(REVIEW_ROOT, f"{source.name}_SixView.png")
    bpy.ops.render.render(write_still=True)
    for obj in review_objects:
        bpy.data.objects.remove(obj, do_unlink=True)
    for obj, was_hidden in hidden_states:
        obj.hide_render = was_hidden


clear_scene()

asset_collection = bpy.data.collections.new("LowPolyPlanetKit")
bpy.context.scene.collection.children.link(asset_collection)

assets = []
for index in range(1, 5):
    assets.append(make_rock(index))
for index in range(1, 5):
    assets.append(make_plant(index))
for index in range(1, 5):
    assets.append(make_crystal(index))
for index in range(1, 5):
    assets.append(make_landmark(index))
for index in range(1, 5):
    assets.append(make_cloud(index))

for obj in assets:
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)
    asset_collection.objects.link(obj)

lod_objects = []
for source in assets:
    source.name = f"{source.name}_LOD0"
    lod_objects.append(source)
    lod_objects.append(make_lod(source, 1))
    lod_objects.append(make_lod(source, 2))

for obj in lod_objects:
    if obj.name.endswith("_LOD1") or obj.name.endswith("_LOD2"):
        for collection in list(obj.users_collection):
            collection.objects.unlink(obj)
        asset_collection.objects.link(obj)

for obj in lod_objects:
    export_fbx(obj, os.path.join(EXPORT_ROOT, f"{obj.name}.fbx"))

setup_studio()
for source in assets:
    render_review(source)

for obj in lod_objects:
    obj.hide_viewport = obj.name.endswith("_LOD1") or obj.name.endswith("_LOD2")
    obj.hide_render = obj.hide_viewport

bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
print(f"Generated {len(assets)} assets, {len(lod_objects)} LOD meshes, and {len(assets)} six-view sheets.")
