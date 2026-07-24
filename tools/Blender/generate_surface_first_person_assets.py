import bpy
import math
import os
from mathutils import Vector


PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE_DIR = os.path.join(PROJECT_ROOT, "tools", "Blender", "Source")
MODEL_DIR = os.path.join(PROJECT_ROOT, "Assets", "Art", "FirstPerson", "Models")
RESOURCE_DIR = os.path.join(PROJECT_ROOT, "Assets", "Resources", "SurfaceTools")
TEMP_DIR = os.path.join(PROJECT_ROOT, "Temp")
BLEND_PATH = os.path.join(SOURCE_DIR, "SurfaceFirstPersonKit.blend")
ARMS_FBX_PATH = os.path.join(MODEL_DIR, "SurfaceFirstPersonArms.fbx")
SCANNER_FBX_PATH = os.path.join(MODEL_DIR, "SurfaceScanner.fbx")
SCANNER_ICON_PATH = os.path.join(RESOURCE_DIR, "ScannerIcon.png")
PREVIEW_PATH = os.path.join(TEMP_DIR, "SurfaceFirstPersonKit_preview.png")


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for blocks in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.armatures,
        bpy.data.materials,
        bpy.data.cameras,
        bpy.data.lights,
    ):
        for block in list(blocks):
            if block.users == 0:
                blocks.remove(block)


def material(name, base, metallic=0.0, roughness=0.5, emission=None, strength=0.0):
    value = bpy.data.materials.new(name)
    value.use_nodes = True
    bsdf = value.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*base, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    emission_input = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
    emission_strength = bsdf.inputs.get("Emission Strength")
    if emission is not None and emission_input is not None:
        emission_input.default_value = (*emission, 1.0)
    if emission_strength is not None:
        emission_strength.default_value = strength
    return value


def set_material(obj, value):
    obj.data.materials.clear()
    obj.data.materials.append(value)


def add_bevel(obj, width, segments=2):
    bevel = obj.modifiers.new("EdgeBevel", "BEVEL")
    bevel.width = width
    bevel.segments = segments
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=bevel.name)


def weight_all(obj, bone_name):
    group = obj.vertex_groups.new(name=bone_name)
    group.add(range(len(obj.data.vertices)), 1.0, "REPLACE")


def cylinder_between(name, start, end, radius, mat, bone_name=None, vertices=10):
    start_value = Vector(start)
    end_value = Vector(end)
    delta = end_value - start_value
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=delta.length,
        location=(start_value + end_value) * 0.5,
    )
    obj = bpy.context.object
    obj.name = name
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = delta.to_track_quat("Z", "Y")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    add_bevel(obj, min(radius * 0.18, 0.012), 2)
    set_material(obj, mat)
    if bone_name:
        weight_all(obj, bone_name)
    return obj


def box(name, location, scale, mat, bone_name=None, bevel=0.01, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        add_bevel(obj, bevel, 2)
    set_material(obj, mat)
    if bone_name:
        weight_all(obj, bone_name)
    return obj


def uv_sphere(name, location, scale, mat, bone_name=None):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=12,
        ring_count=8,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    set_material(obj, mat)
    if bone_name:
        weight_all(obj, bone_name)
    return obj


def add_empty(name, location, parent=None, display="PLAIN_AXES", size=0.04):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.empty_display_type = display
    obj.empty_display_size = size
    obj.parent = parent
    return obj


def add_edit_bone(armature, name, head, tail, parent=None, connected=False):
    bone = armature.edit_bones.new(name)
    bone.head = head
    bone.tail = tail
    bone.roll = 0.0
    if parent is not None:
        bone.parent = parent
        bone.use_connect = connected
    return bone


def join_skinned_meshes(meshes, armature):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = "SurfaceFirstPersonArms_Mesh"
    modifier = result.modifiers.new("ArmatureDeform", "ARMATURE")
    modifier.object = armature
    result.parent = armature
    return result


def build_armature():
    data = bpy.data.armatures.new("SurfaceFirstPersonArmature")
    armature = bpy.data.objects.new("SurfaceFirstPersonArmature", data)
    bpy.context.collection.objects.link(armature)
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    root = add_edit_bone(data, "Root", (0, 0.06, -0.06), (0, -0.04, -0.06))
    anatomy = {}
    for side, sign in (("L", -1.0), ("R", 1.0)):
        shoulder = Vector((0.25 * sign, -0.01, -0.08))
        upper_mid = Vector((0.31 * sign, -0.13, -0.12))
        elbow = Vector((0.33 * sign, -0.27, -0.17))
        fore_mid = Vector((0.29 * sign, -0.40, -0.20))
        wrist = Vector((0.22 * sign, -0.54, -0.22))
        palm = Vector((0.20 * sign, -0.64, -0.21))

        clavicle = add_edit_bone(
            data,
            f"{side}_Clavicle",
            (0.03 * sign, 0.02, -0.07),
            shoulder,
            root,
        )
        upper = add_edit_bone(
            data,
            f"{side}_UpperArm",
            shoulder,
            upper_mid,
            clavicle,
            True,
        )
        upper_twist = add_edit_bone(
            data,
            f"{side}_UpperArmTwist",
            upper_mid,
            elbow,
            upper,
            True,
        )
        forearm = add_edit_bone(
            data,
            f"{side}_Forearm",
            elbow,
            fore_mid,
            upper_twist,
            True,
        )
        forearm_twist = add_edit_bone(
            data,
            f"{side}_ForearmTwist",
            fore_mid,
            wrist,
            forearm,
            True,
        )
        hand = add_edit_bone(
            data,
            f"{side}_Hand",
            wrist,
            palm,
            forearm_twist,
            True,
        )

        finger_bones = {}
        finger_specs = (
            ("Thumb", 0.050, -0.008, 0.038, 0.82),
            ("Index", 0.037, -0.014, 0.047, 1.00),
            ("Middle", 0.012, -0.016, 0.051, 1.05),
            ("Ring", -0.013, -0.012, 0.047, 0.98),
            ("Little", -0.037, -0.006, 0.040, 0.86),
        )
        for finger_name, lateral, vertical, segment_length, length_scale in finger_specs:
            base = palm + Vector((lateral * sign, -0.012, vertical))
            direction = Vector((0.13 * sign if finger_name == "Thumb" else 0, -1, -0.08))
            direction.normalize()
            previous = hand
            chain = []
            current = base
            for segment in range(1, 4):
                length = segment_length * length_scale * (1.0 - 0.11 * (segment - 1))
                target = current + direction * length
                bone = add_edit_bone(
                    data,
                    f"{side}_{finger_name}_{segment}",
                    current,
                    target,
                    previous,
                    segment > 1,
                )
                chain.append((bone.name, Vector(current), Vector(target)))
                previous = bone
                current = target
            finger_bones[finger_name] = chain

        anatomy[side] = {
            "shoulder": shoulder,
            "upper_mid": upper_mid,
            "elbow": elbow,
            "fore_mid": fore_mid,
            "wrist": wrist,
            "palm": palm,
            "fingers": finger_bones,
        }

    bpy.ops.object.mode_set(mode="OBJECT")
    armature.show_in_front = True
    return armature, anatomy


def build_arms(materials):
    armature, anatomy = build_armature()
    sleeve, armor, glove, cyan, rubber = materials
    meshes = []
    for side, sign in (("L", -1.0), ("R", 1.0)):
        points = anatomy[side]
        meshes.extend(
            [
                cylinder_between(
                    f"{side}_UpperSleeve_A",
                    points["shoulder"],
                    points["upper_mid"],
                    0.075,
                    sleeve,
                    f"{side}_UpperArm",
                ),
                cylinder_between(
                    f"{side}_UpperSleeve_B",
                    points["upper_mid"],
                    points["elbow"],
                    0.071,
                    sleeve,
                    f"{side}_UpperArmTwist",
                ),
                cylinder_between(
                    f"{side}_Forearm_A",
                    points["elbow"],
                    points["fore_mid"],
                    0.066,
                    armor,
                    f"{side}_Forearm",
                ),
                cylinder_between(
                    f"{side}_Forearm_B",
                    points["fore_mid"],
                    points["wrist"],
                    0.058,
                    armor,
                    f"{side}_ForearmTwist",
                ),
            ]
        )
        meshes.append(
            box(
                f"{side}_Palm",
                points["palm"] + Vector((0, -0.005, 0)),
                (0.062, 0.080, 0.026),
                glove,
                f"{side}_Hand",
                0.012,
                (math.radians(4), 0, 0),
            )
        )
        meshes.append(
            cylinder_between(
                f"{side}_CuffGlow",
                points["fore_mid"] + (points["wrist"] - points["fore_mid"]) * 0.72,
                points["fore_mid"] + (points["wrist"] - points["fore_mid"]) * 0.82,
                0.061,
                cyan,
                f"{side}_ForearmTwist",
                12,
            )
        )
        meshes.append(
            box(
                f"{side}_ForearmPlate",
                points["fore_mid"] + Vector((0, -0.015, 0.052)),
                (0.051, 0.085, 0.014),
                armor,
                f"{side}_Forearm",
                0.008,
                (math.radians(-8), 0, 0),
            )
        )
        for finger_name, chain in points["fingers"].items():
            radius = 0.014 if finger_name not in ("Thumb", "Little") else 0.0125
            for segment_index, (bone_name, start, end) in enumerate(chain, 1):
                meshes.append(
                    cylinder_between(
                        f"{side}_{finger_name}_Mesh_{segment_index}",
                        start,
                        end,
                        radius * (1.0 - 0.09 * (segment_index - 1)),
                        glove if segment_index != 2 else rubber,
                        bone_name,
                        8,
                    )
                )

    mesh = join_skinned_meshes(meshes, armature)
    root = add_empty("SurfaceFirstPersonArms", (0, 0, 0))
    armature.parent = root
    armature.data.display_type = "OCTAHEDRAL"

    for side in ("L", "R"):
        hand_bone = armature.pose.bones.get(f"{side}_Hand")
        socket = add_empty(
            "RightToolSocket" if side == "R" else "LeftToolSocket",
            (0, 0, 0),
            armature,
            "ARROWS",
            0.035,
        )
        socket.parent_type = "BONE"
        socket.parent_bone = hand_bone.name
        socket.location = (0, -0.05, 0)
    return root, armature, mesh


def parent_mesh(obj, root):
    obj.parent = root
    return obj


def build_scanner(materials):
    _, armor, glove, cyan, rubber = materials
    root = add_empty("SurfaceScanner", (0, 0, 0))
    body = parent_mesh(
        box(
            "ScannerBody",
            (0, -0.07, 0.055),
            (0.080, 0.120, 0.043),
            armor,
            bevel=0.018,
            rotation=(math.radians(-8), 0, 0),
        ),
        root,
    )
    parent_mesh(
        box(
            "ScannerScreen",
            (0, -0.092, 0.102),
            (0.057, 0.070, 0.008),
            cyan,
            bevel=0.008,
            rotation=(math.radians(-8), 0, 0),
        ),
        root,
    )
    parent_mesh(
        box(
            "ScannerHandle",
            (0.045, 0.055, -0.055),
            (0.030, 0.075, 0.036),
            glove,
            bevel=0.012,
            rotation=(math.radians(15), 0, math.radians(-8)),
        ),
        root,
    )
    parent_mesh(
        box(
            "ScannerSupportRail",
            (-0.092, -0.055, 0.025),
            (0.018, 0.094, 0.024),
            rubber,
            bevel=0.009,
        ),
        root,
    )
    bpy.ops.mesh.primitive_torus_add(
        major_radius=0.066,
        minor_radius=0.009,
        major_segments=24,
        minor_segments=6,
        location=(0, -0.19, 0.06),
        rotation=(math.radians(90), 0, 0),
    )
    ring = bpy.context.object
    ring.name = "ScannerEmitterRing"
    set_material(ring, cyan)
    ring.parent = root
    for side in (-1, 1):
        parent_mesh(
            box(
                f"ScannerSideFin_{side}",
                (0.088 * side, -0.10, 0.045),
                (0.012, 0.090, 0.040),
                armor,
                bevel=0.007,
                rotation=(0, math.radians(4 * side), 0),
            ),
            root,
        )

    primary = add_empty("PrimaryGrip", (0.045, 0.055, -0.055), root, "ARROWS", 0.035)
    primary.rotation_euler = (math.radians(15), 0, math.radians(-8))
    support = add_empty("SupportGrip", (-0.092, -0.055, 0.025), root, "ARROWS", 0.035)
    support.rotation_euler = (math.radians(8), 0, math.radians(90))
    aim = add_empty("AimPoint", (0, -0.30, 0.065), root, "SINGLE_ARROW", 0.06)
    effect = add_empty("EffectOrigin", (0, -0.205, 0.06), root, "SPHERE", 0.035)
    return root, body


def descendants(root):
    result = [root]
    for child in root.children:
        result.extend(descendants(child))
    return result


def export_root(root, path):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in descendants(root):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        global_scale=1.0,
        apply_unit_scale=True,
        bake_space_transform=True,
        path_mode="AUTO",
        add_leaf_bones=False,
        bake_anim=False,
    )


def look_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def add_preview_scene(arms_root, scanner_root):
    bpy.ops.object.camera_add(location=(1.18, 1.15, 0.55))
    camera = bpy.context.object
    camera.name = "PreviewCamera"
    camera.data.lens = 56
    look_at(camera, (0, -0.24, -0.12))
    bpy.context.scene.camera = camera
    bpy.ops.object.light_add(type="AREA", location=(0.9, 0.4, 1.1))
    key = bpy.context.object
    key.name = "PreviewKey"
    key.data.energy = 900
    key.data.shape = "DISK"
    key.data.size = 4.0
    look_at(key, (0, -0.25, -0.1))
    bpy.ops.object.light_add(type="AREA", location=(-0.9, -0.6, 0.3))
    fill = bpy.context.object
    fill.name = "PreviewFill"
    fill.data.energy = 650
    fill.data.color = (0.05, 0.55, 1.0)
    fill.data.size = 3.0
    look_at(fill, (0, -0.25, -0.1))
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = True
    scene.render.filepath = PREVIEW_PATH
    scene.world.color = (0.003, 0.006, 0.012)
    bpy.ops.render.render(write_still=True)

    for obj in descendants(arms_root):
        obj.hide_render = True
    scanner_root.rotation_euler = (math.radians(72), 0, math.radians(-28))
    scanner_root.location = (0, -0.12, 0)
    camera.location = (0.55, 0.62, 0.48)
    camera.data.lens = 64
    look_at(camera, (0, -0.10, 0.02))
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.filepath = SCANNER_ICON_PATH
    bpy.ops.render.render(write_still=True)
    for obj in descendants(arms_root):
        obj.hide_render = False


def main():
    for directory in (SOURCE_DIR, MODEL_DIR, RESOURCE_DIR, TEMP_DIR):
        os.makedirs(directory, exist_ok=True)
    clear_scene()
    materials = (
        material("FP_Sleeve", (0.018, 0.036, 0.052), 0.1, 0.78),
        material("FP_Armor", (0.025, 0.095, 0.14), 0.62, 0.26),
        material("FP_Glove", (0.012, 0.018, 0.025), 0.25, 0.48),
        material("FP_Cyan", (0.0, 0.18, 0.25), 0.18, 0.22, (0.0, 0.82, 1.0), 4.0),
        material("FP_Rubber", (0.006, 0.008, 0.012), 0.0, 0.9),
    )
    arms_root, _, _ = build_arms(materials)
    scanner_root, _ = build_scanner(materials)
    export_root(arms_root, ARMS_FBX_PATH)
    export_root(scanner_root, SCANNER_FBX_PATH)
    add_preview_scene(arms_root, scanner_root)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print(f"Saved Blender source: {BLEND_PATH}")
    print(f"Exported arms: {ARMS_FBX_PATH}")
    print(f"Exported scanner: {SCANNER_FBX_PATH}")
    print(f"Rendered scanner icon: {SCANNER_ICON_PATH}")


if __name__ == "__main__":
    main()
