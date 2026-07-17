"""Build and validate the Antelope native-animation baseline.

Run this script inside the project-configured Blender/NMSDK environment.  It
intentionally rebuilds the rig from the geometry inverse bind matrices instead
of using the joint-position armature produced by the old NMSDK importer.
"""

from __future__ import annotations

import json
import math
import os
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector
from nmsdk.ModelImporter.import_scene import ImportScene
from nmsdk.ModelImporter.readers import read_anim


PROJECT_ROOT = Path(r"D:\unity planet\unity-planet")
WORK_ROOT = Path(r"D:\blender\creature\NMSImport")
EXTRACTED_ROOT = WORK_ROOT / "Extracted"
SOURCE_SCENE = (
    EXTRACTED_ROOT
    / "models"
    / "planets"
    / "creatures"
    / "anteloperig"
    / "antelope.scene.mbin"
)
WALK_ANIMATION = (
    WORK_ROOT
    / "ExtractedAnimations"
    / "MODELS"
    / "PLANETS"
    / "BIOMES"
    / "RAINFOREST"
    / "MEDIUMCREATURE"
    / "ANTELOPE"
    / "ANIMS"
    / "ANTEWALK.ANIM.MBIN"
)
OUTPUT_BLEND = WORK_ROOT / "AntelopeInverseBindBaseline.blend"
OUTPUT_REPORT = WORK_ROOT / "Validation" / "AntelopeWalkInverseBind.json"
OUTPUT_FBX = (
    PROJECT_ROOT
    / "Assets"
    / "Creatures"
    / "Authorized"
    / "NMS"
    / "AntelopeBaseline"
    / "AntelopeInverseBindBaseline.fbx"
)

SELECTED_MODULES = (
    "_Body_Deer",
    "_Head_Deer",
    "DeerEyes",
    "_HDEars_1",
    "_Tail_Alien1",
)

LOAD_BEARING_CHAINS = (
    ("LF2ShoulderJNT", "LF2ElbowJNT", "LF2WristJNT", "LF2FootJNT"),
    ("RF2ShoulderJNT", "RF2ElbowJNT", "RF2WristJNT", "RF2FootJNT"),
    ("LBLegJNT", "LBKneeJNT", "LBAnkleJNT", "LBFootJNT"),
    ("RBLegJNT", "RBKneeJNT", "RBAnkleJNT", "RBFootJNT"),
)

ACTION_NAME = "Antelope_WALK_InvBind"
MIN_MEANINGFUL_EDGE = 1.0e-4
MIN_MEANINGFUL_TRIANGLE_AREA = 1.0e-7

PREFERRED_CHILDREN = {
    "RootJNT": "HipJNT",
    "HipJNT": "Back1JNT",
    "Back1JNT": "Back2JNT",
    "Back2JNT": "Back3JNT",
    "Back3JNT": "Neck1JNT",
    "Neck1JNT": "Neck2JNT",
    "Neck2JNT": "HeadJNT",
    "HeadJNT": "SnoutJNT",
    "SnoutJNT": "NoseJNT",
    "TailJNT": "Tail2JNT",
    "TailLong1JNT": "TailLong2JNT",
    "TailLong2JNT": "TailLong3JNT",
    "TailLong3JNT": "TailLong4JNT",
}


def require_file(path: Path) -> None:
    if not path.is_file():
        raise RuntimeError(f"Required source file is missing: {path}")


def matrix_from_flat_transposed(values) -> Matrix:
    matrix = Matrix(
        (
            values[0:4],
            values[4:8],
            values[8:12],
            values[12:16],
        )
    )
    matrix.transpose()
    return matrix


def matrix_to_flat(matrix: Matrix) -> list[float]:
    return [float(matrix[row][column]) for row in range(4) for column in range(4)]


def import_source_scene():
    preferences = bpy.context.preferences.addons["nmsdk"].preferences
    preferences.unpacked_pcbanks_dir = str(EXTRACTED_ROOT)
    preferences.mbincompiler_path = r"D:\blender\creature\Tools\Downloads\MBINCompiler.exe"

    importer = ImportScene(
        str(SOURCE_SCENE),
        parent_obj=None,
        ref_scenes={},
        settings={
            "clear_scene": True,
            "import_collisions": False,
            "show_collisions": False,
            "import_recursively": False,
            "import_bones": True,
            "import_anims": False,
        },
    )
    importer.render_scene()
    return importer


def capture_bind_data(importer):
    bind_matrices: dict[str, Matrix] = {}
    parent_names: dict[str, str | None] = {}
    for joint in importer.joints:
        joint_index = joint.Attribute("JOINTINDEX", int)
        binding = importer.mesh_binding_data["JointBindings"][joint_index]
        inverse_bind = matrix_from_flat_transposed(binding.InvBindMatrix)
        bind_matrices[joint.Name] = inverse_bind.inverted()
        parent_names[joint.Name] = (
            joint.parent.Name if joint.parent is not None and joint.parent.Type == "JOINT" else None
        )
    if not bind_matrices:
        raise RuntimeError("The source geometry contains no joint bindings.")
    return bind_matrices, parent_names


def collect_required_bones(parent_names: dict[str, str | None]) -> set[str]:
    weighted = set()
    for module_name in SELECTED_MODULES:
        obj = bpy.data.objects.get(module_name)
        if obj is None or obj.type != "MESH":
            raise RuntimeError(f"Required Antelope module is missing: {module_name}")
        for vertex in obj.data.vertices:
            for membership in vertex.groups:
                if membership.weight > 0.0:
                    weighted.add(obj.vertex_groups[membership.group].name)

    required = set(weighted)
    for name in tuple(weighted):
        parent_name = parent_names.get(name)
        while parent_name is not None:
            required.add(parent_name)
            parent_name = parent_names.get(parent_name)
    return required


def choose_bone_endpoint(
    name: str,
    bind_matrices: dict[str, Matrix],
    parent_names: dict[str, str | None],
) -> Vector:
    origin = bind_matrices[name].translation
    children = [
        child_name
        for child_name, matrix in bind_matrices.items()
        if parent_names[child_name] == name and (matrix.translation - origin).length > 1.0e-4
    ]
    preferred = PREFERRED_CHILDREN.get(name)
    if preferred in children:
        return bind_matrices[preferred].translation
    if children:
        children.sort(
            key=lambda child_name: (
                "CTRL" in child_name.upper(),
                "END" in child_name.upper(),
                child_name.lower().startswith("joint"),
                (bind_matrices[child_name].translation - origin).length,
            )
        )
        return bind_matrices[children[0]].translation

    parent_name = parent_names[name]
    if parent_name:
        parent_distance = (origin - bind_matrices[parent_name].translation).length
        if parent_distance > 1.0e-4:
            length = max(0.015, min(0.08, parent_distance * 0.25))
            rotation = bind_matrices[name].to_quaternion()
            return origin + rotation @ Vector((0.0, length, 0.0))
    rotation = bind_matrices[name].to_quaternion()
    return origin + rotation @ Vector((0.0, 0.035, 0.0))


def build_inverse_bind_armature(
    source_armature: bpy.types.Object,
    bind_matrices: dict[str, Matrix],
    parent_names: dict[str, str | None],
) -> bpy.types.Object:
    source_armature.name = "Armature_NMSDK_JointPosition_Broken"
    source_armature.hide_viewport = True
    source_armature.hide_render = True

    armature_data = bpy.data.armatures.new("AntelopeInverseBindArmature")
    armature = bpy.data.objects.new("Armature", armature_data)
    bpy.context.collection.objects.link(armature)
    armature.parent = source_armature.parent
    armature.matrix_local = source_armature.matrix_local.copy()
    armature.show_in_front = True
    armature.data.display_type = "STICK"
    armature.data.show_axes = False

    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        for name, bind_matrix in bind_matrices.items():
            edit_bone = armature_data.edit_bones.new(name)
            location, rotation, _ = bind_matrix.decompose()
            local_z = rotation @ Vector((0.0, 0.0, 1.0))
            edit_bone.head = location
            edit_bone.tail = choose_bone_endpoint(name, bind_matrices, parent_names)
            edit_bone.align_roll(local_z)
            edit_bone.use_connect = False
            edit_bone.use_local_location = True
            edit_bone.inherit_scale = "FULL"

        for name, parent_name in parent_names.items():
            if parent_name:
                armature_data.edit_bones[name].parent = armature_data.edit_bones[parent_name]
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")

    for name, bind_matrix in bind_matrices.items():
        armature_data.bones[name]["nms_bind_matrix"] = matrix_to_flat(bind_matrix)
        upper_name = name.upper()
        armature_data.bones[name].hide = (
            "CTRL" in upper_name
            or "END" in upper_name
            or name.lower().startswith("joint")
        )
    armature["nms_rig_baseline"] = "inverse_bind_v1"
    return armature


def isolate_modules(armature: bpy.types.Object) -> list[bpy.types.Object]:
    modules = []
    for name in SELECTED_MODULES:
        obj = bpy.data.objects.get(name)
        if obj is None or obj.type != "MESH":
            raise RuntimeError(f"Required Antelope module is missing: {name}")
        world_matrix = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world_matrix
        for modifier in list(obj.modifiers):
            if modifier.type == "ARMATURE":
                obj.modifiers.remove(modifier)
        modifier = obj.modifiers.new(name="InverseBindArmature", type="ARMATURE")
        modifier.object = armature
        obj.hide_viewport = False
        obj.hide_render = False
        modules.append(obj)

    armature_world = armature.matrix_world.copy()
    armature.parent = None
    armature.matrix_world = armature_world

    keep = set(modules)
    keep.add(armature)
    for obj in list(bpy.data.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)
    return modules


def animation_value(animation, node, frame: int, channel: str, index_name: str):
    animated_values = getattr(animation.AnimFrameData[0], channel)
    index = getattr(node, index_name)
    if index < len(animated_values):
        return getattr(animation.AnimFrameData[frame], channel)[index]
    return getattr(animation.StillFrameData, channel)[index - len(animated_values)]


def animation_local_matrix(animation, node, frame: int) -> Matrix:
    translation = animation_value(animation, node, frame, "Translations", "TransIndex")
    raw_rotation = animation_value(animation, node, frame, "Rotations", "RotIndex")
    scale = animation_value(animation, node, frame, "Scales", "ScaleIndex")
    rotation = Quaternion(
        (raw_rotation[3], raw_rotation[0], raw_rotation[1], raw_rotation[2])
    )
    return Matrix.LocRotScale(Vector(translation[:3]), rotation, Vector(scale[:3]))


def build_animation_targets(
    animation,
    frame: int,
    armature: bpy.types.Object,
    bind_matrices: dict[str, Matrix],
) -> dict[str, Matrix]:
    nodes = {
        node.Node: node
        for node in animation.NodeData
        if node.Node in armature.data.bones
    }
    animated_names = set(nodes)
    animated_globals: dict[str, Matrix] = {}

    def animated_global(name: str) -> Matrix:
        cached = animated_globals.get(name)
        if cached is not None:
            return cached
        parent = armature.data.bones[name].parent
        while parent is not None and parent.name not in animated_names:
            parent = parent.parent
        local_matrix = animation_local_matrix(animation, nodes[name], frame)
        result = (
            animated_global(parent.name) @ local_matrix if parent is not None else local_matrix
        )
        animated_globals[name] = result
        return result

    for name in animated_names:
        animated_global(name)

    animation_globals: dict[str, Matrix] = {}

    def complete_global(name: str) -> Matrix:
        cached = animation_globals.get(name)
        if cached is not None:
            return cached
        bone = armature.data.bones[name]
        if name in animated_globals:
            result = animated_globals[name]
        elif bone.parent is not None:
            parent_name = bone.parent.name
            bind_local = bind_matrices[parent_name].inverted() @ bind_matrices[name]
            result = complete_global(parent_name) @ bind_local
        else:
            result = bind_matrices[name]
        animation_globals[name] = result
        return result

    rest_matrices = {bone.name: bone.matrix_local.copy() for bone in armature.data.bones}
    targets = {}
    for bone in armature.data.bones:
        animated_global_matrix = complete_global(bone.name)
        targets[bone.name] = (
            animated_global_matrix
            @ bind_matrices[bone.name].inverted()
            @ rest_matrices[bone.name]
        )
    return targets


def basis_from_target(
    bone: bpy.types.Bone,
    target: Matrix,
    targets: dict[str, Matrix],
    rest_matrices: dict[str, Matrix],
) -> Matrix:
    if bone.parent is None:
        return rest_matrices[bone.name].inverted() @ target
    parent_name = bone.parent.name
    inherited = (
        targets[parent_name]
        @ rest_matrices[parent_name].inverted()
        @ rest_matrices[bone.name]
    )
    return inherited.inverted() @ target


def bake_walk_action(
    armature: bpy.types.Object,
    bind_matrices: dict[str, Matrix],
):
    animation = read_anim(str(WALK_ANIMATION))
    action = bpy.data.actions.get(ACTION_NAME)
    if action is not None:
        bpy.data.actions.remove(action)
    action = bpy.data.actions.new(ACTION_NAME)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action

    scene = bpy.context.scene
    scene.render.fps = 30
    scene.render.fps_base = 1.0
    scene.frame_start = 0
    scene.frame_end = animation.FrameCount - 1
    rest_matrices = {bone.name: bone.matrix_local.copy() for bone in armature.data.bones}
    ordered_bones = sorted(armature.data.bones, key=lambda bone: len(bone.parent_recursive))
    previous_quaternions: dict[str, Quaternion] = {}

    for pose_bone in armature.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.matrix_basis = Matrix.Identity(4)

    for frame in range(animation.FrameCount):
        targets = build_animation_targets(animation, frame, armature, bind_matrices)
        scene.frame_set(frame)
        for bone in ordered_bones:
            pose_bone = armature.pose.bones[bone.name]
            basis = basis_from_target(bone, targets[bone.name], targets, rest_matrices)
            location, rotation, scale = basis.decompose()
            previous = previous_quaternions.get(bone.name)
            if previous is not None and previous.dot(rotation) < 0.0:
                rotation.negate()
            previous_quaternions[bone.name] = rotation.copy()
            pose_bone.location = location
            pose_bone.rotation_quaternion = rotation
            pose_bone.scale = scale
            pose_bone.keyframe_insert("location", frame=frame, group=bone.name)
            pose_bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
            pose_bone.keyframe_insert("scale", frame=frame, group=bone.name)

    action.frame_start = 0
    action.frame_end = animation.FrameCount - 1
    # Blender 5 stores keyed pose channels in layered actions and no longer
    # exposes Action.fcurves directly.  FBX is exported with simplify=0 and
    # samples every source frame, so changing interpolation is unnecessary for
    # the baked interchange result.  Keep the legacy path for Blender 4.x.
    if hasattr(action, "fcurves"):
        for fcurve in action.fcurves:
            for keyframe in fcurve.keyframe_points:
                keyframe.interpolation = "LINEAR"
    scene.frame_set(0)
    return animation, action


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return 0.0
    values.sort()
    index = min(len(values) - 1, int((len(values) - 1) * fraction))
    return float(values[index])


def triangle_area(a: Vector, b: Vector, c: Vector) -> float:
    return 0.5 * (b - a).cross(c - a).length


def validate_all_frames(
    armature: bpy.types.Object,
    modules: list[bpy.types.Object],
    animation,
) -> dict:
    edge_reference = {}
    triangle_reference = {}
    for obj in modules:
        obj.data.calc_loop_triangles()
        edge_reference[obj.name] = [
            (
                edge.vertices[0],
                edge.vertices[1],
                (obj.data.vertices[edge.vertices[0]].co - obj.data.vertices[edge.vertices[1]].co).length,
            )
            for edge in obj.data.edges
        ]
        triangle_reference[obj.name] = [
            (
                tuple(triangle.vertices),
                triangle_area(
                    obj.data.vertices[triangle.vertices[0]].co,
                    obj.data.vertices[triangle.vertices[1]].co,
                    obj.data.vertices[triangle.vertices[2]].co,
                ),
            )
            for triangle in obj.data.loop_triangles
        ]

    bind_segment_lengths = {}
    for chain in LOAD_BEARING_CHAINS:
        for parent_name, child_name in zip(chain, chain[1:]):
            parent = armature.data.bones[parent_name].matrix_local.translation
            child = armature.data.bones[child_name].matrix_local.translation
            bind_segment_lengths[(parent_name, child_name)] = (parent - child).length

    edge_ratios = {obj.name: [] for obj in modules}
    triangle_ratios = {obj.name: [] for obj in modules}
    max_edge_ratio = {obj.name: 0.0 for obj in modules}
    max_triangle_ratio = {obj.name: 0.0 for obj in modules}
    worst_edge_frame = {obj.name: 0 for obj in modules}
    worst_triangle_frame = {obj.name: 0 for obj in modules}
    min_leg_ratio = float("inf")
    max_leg_ratio = 0.0
    max_bounds_diagonal = 0.0
    non_finite_vertices = 0

    scene = bpy.context.scene
    for frame in range(animation.FrameCount):
        scene.frame_set(frame)
        bpy.context.view_layer.update()

        for (parent_name, child_name), bind_length in bind_segment_lengths.items():
            parent = armature.pose.bones[parent_name].matrix.translation
            child = armature.pose.bones[child_name].matrix.translation
            ratio = (parent - child).length / bind_length
            min_leg_ratio = min(min_leg_ratio, ratio)
            max_leg_ratio = max(max_leg_ratio, ratio)

        dependency_graph = bpy.context.evaluated_depsgraph_get()
        world_points = []
        for obj in modules:
            evaluated = obj.evaluated_get(dependency_graph)
            mesh = evaluated.to_mesh()
            try:
                for vertex in mesh.vertices:
                    if not all(math.isfinite(component) for component in vertex.co):
                        non_finite_vertices += 1
                    world_points.append(obj.matrix_world @ vertex.co)

                frame_edge_max = 0.0
                for first, second, rest_length in edge_reference[obj.name]:
                    if rest_length < MIN_MEANINGFUL_EDGE:
                        continue
                    current_length = (mesh.vertices[first].co - mesh.vertices[second].co).length
                    ratio = current_length / rest_length
                    edge_ratios[obj.name].append(ratio)
                    frame_edge_max = max(frame_edge_max, ratio)
                if frame_edge_max > max_edge_ratio[obj.name]:
                    max_edge_ratio[obj.name] = frame_edge_max
                    worst_edge_frame[obj.name] = frame

                frame_triangle_max = 0.0
                for indices, rest_area in triangle_reference[obj.name]:
                    if rest_area < MIN_MEANINGFUL_TRIANGLE_AREA:
                        continue
                    current_area = triangle_area(
                        mesh.vertices[indices[0]].co,
                        mesh.vertices[indices[1]].co,
                        mesh.vertices[indices[2]].co,
                    )
                    ratio = current_area / rest_area
                    triangle_ratios[obj.name].append(ratio)
                    frame_triangle_max = max(frame_triangle_max, ratio)
                if frame_triangle_max > max_triangle_ratio[obj.name]:
                    max_triangle_ratio[obj.name] = frame_triangle_max
                    worst_triangle_frame[obj.name] = frame
            finally:
                evaluated.to_mesh_clear()

        minimum = Vector(
            (
                min(point.x for point in world_points),
                min(point.y for point in world_points),
                min(point.z for point in world_points),
            )
        )
        maximum = Vector(
            (
                max(point.x for point in world_points),
                max(point.y for point in world_points),
                max(point.z for point in world_points),
            )
        )
        max_bounds_diagonal = max(max_bounds_diagonal, (maximum - minimum).length)

    module_reports = {}
    failures = []
    for obj in modules:
        name = obj.name
        edge_p99 = percentile(edge_ratios[name], 0.99)
        triangle_p99 = percentile(triangle_ratios[name], 0.99)
        module_reports[name] = {
            "vertexCount": len(obj.data.vertices),
            "triangleCount": len(obj.data.loop_triangles),
            "maximumEdgeStretch": max_edge_ratio[name],
            "edgeStretchP99": edge_p99,
            "worstEdgeFrame": worst_edge_frame[name],
            "maximumTriangleAreaStretch": max_triangle_ratio[name],
            "triangleAreaStretchP99": triangle_p99,
            "worstTriangleFrame": worst_triangle_frame[name],
        }
        if max_edge_ratio[name] > 6.0 or edge_p99 > 3.25:
            failures.append(f"{name} has excessive edge stretching")
        if max_triangle_ratio[name] > 24.0 or triangle_p99 > 7.0:
            failures.append(f"{name} has excessive triangle-area stretching")

    if non_finite_vertices:
        failures.append(f"Found {non_finite_vertices} non-finite evaluated vertices")
    if max_bounds_diagonal > 4.5:
        failures.append(f"Animated bounds diagonal is too large: {max_bounds_diagonal:.4f}")
    if min_leg_ratio < 0.98 or max_leg_ratio > 1.02:
        failures.append(
            f"Load-bearing bone lengths changed: {min_leg_ratio:.6f}..{max_leg_ratio:.6f}"
        )

    report = {
        "pipelineVersion": 1,
        "sourceScene": str(SOURCE_SCENE),
        "sourceAnimation": str(WALK_ANIMATION),
        "frameCount": animation.FrameCount,
        "framesPerSecond": 30,
        "armature": armature.name,
        "modules": module_reports,
        "maximumBoundsDiagonal": max_bounds_diagonal,
        "minimumLoadBearingBoneLengthRatio": min_leg_ratio,
        "maximumLoadBearingBoneLengthRatio": max_leg_ratio,
        "nonFiniteVertexCount": non_finite_vertices,
        "passed": not failures,
        "failures": failures,
    }
    OUTPUT_REPORT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_REPORT.write_text(json.dumps(report, indent=2), encoding="utf-8")
    if failures:
        raise RuntimeError("Antelope inverse-bind validation failed: " + "; ".join(failures))
    return report


def save_and_export(armature: bpy.types.Object, modules: list[bpy.types.Object]) -> None:
    OUTPUT_BLEND.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_FBX.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND))

    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    for obj in modules:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.export_scene.fbx(
        filepath=str(OUTPUT_FBX),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        path_mode="COPY",
        embed_textures=False,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        axis_forward="-Z",
        axis_up="Y",
    )


def main() -> None:
    require_file(SOURCE_SCENE)
    require_file(WALK_ANIMATION)
    importer = import_source_scene()
    bind_matrices, parent_names = capture_bind_data(importer)
    required_bones = collect_required_bones(parent_names)
    bind_matrices = {
        name: matrix for name, matrix in bind_matrices.items() if name in required_bones
    }
    parent_names = {
        name: parent_name
        for name, parent_name in parent_names.items()
        if name in required_bones
    }
    source_armature = bpy.data.objects.get("Armature")
    if source_armature is None:
        raise RuntimeError("NMSDK did not import the source Armature.")

    armature = build_inverse_bind_armature(source_armature, bind_matrices, parent_names)
    modules = isolate_modules(armature)
    animation, action = bake_walk_action(armature, bind_matrices)
    report = validate_all_frames(armature, modules, animation)
    save_and_export(armature, modules)
    print(
        "ANTELOPE_INVERSE_BIND_BASELINE_OK",
        json.dumps(
            {
                "blend": str(OUTPUT_BLEND),
                "fbx": str(OUTPUT_FBX),
                "report": str(OUTPUT_REPORT),
                "action": action.name,
                "frames": report["frameCount"],
                "bounds": report["maximumBoundsDiagonal"],
                "legLengthRatio": [
                    report["minimumLoadBearingBoneLengthRatio"],
                    report["maximumLoadBearingBoneLengthRatio"],
                ],
            }
        ),
    )


if __name__ == "__main__":
    main()
