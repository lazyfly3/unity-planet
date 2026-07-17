"""Reusable Blender stages for inverse-bind creature families."""

from __future__ import annotations

import json
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector
from nmsdk.ModelImporter.readers import read_anim

from nms_creature_pipeline import (
    build_inverse_bind_armature,
    capture_bind_data,
    import_source,
    rebind_meshes,
    require_file,
    skeleton_hash,
)


def animation_value(animation, node, frame, channel, index_name):
    animated = getattr(animation.AnimFrameData[0], channel)
    index = getattr(node, index_name)
    if index < len(animated):
        return getattr(animation.AnimFrameData[frame], channel)[index]
    return getattr(animation.StillFrameData, channel)[index - len(animated)]


def animation_local_matrix(animation, node, frame):
    translation = animation_value(animation, node, frame, "Translations", "TransIndex")
    raw_rotation = animation_value(animation, node, frame, "Rotations", "RotIndex")
    scale = animation_value(animation, node, frame, "Scales", "ScaleIndex")
    rotation = Quaternion((raw_rotation[3], raw_rotation[0], raw_rotation[1], raw_rotation[2]))
    return Matrix.LocRotScale(Vector(translation[:3]), rotation, Vector(scale[:3]))


def build_animation_targets(animation, frame, armature, bind_matrices):
    nodes = {node.Node: node for node in animation.NodeData if node.Node in armature.data.bones}
    animated_names = set(nodes)
    animated_globals = {}

    def animated_global(name):
        if name in animated_globals:
            return animated_globals[name]
        parent = armature.data.bones[name].parent
        while parent is not None and parent.name not in animated_names:
            parent = parent.parent
        local = animation_local_matrix(animation, nodes[name], frame)
        result = animated_global(parent.name) @ local if parent is not None else local
        animated_globals[name] = result
        return result

    for name in animated_names:
        animated_global(name)
    complete_globals = {}

    def complete_global(name):
        if name in complete_globals:
            return complete_globals[name]
        bone = armature.data.bones[name]
        if name in animated_globals:
            result = animated_globals[name]
        elif bone.parent is not None:
            parent = bone.parent.name
            bind_local = bind_matrices[parent].inverted() @ bind_matrices[name]
            result = complete_global(parent) @ bind_local
        else:
            result = bind_matrices[name]
        complete_globals[name] = result
        return result

    rest = {bone.name: bone.matrix_local.copy() for bone in armature.data.bones}
    return {
        bone.name: complete_global(bone.name)
        @ bind_matrices[bone.name].inverted()
        @ rest[bone.name]
        for bone in armature.data.bones
    }


def basis_from_target(bone, target, targets, rest):
    if bone.parent is None:
        return rest[bone.name].inverted() @ target
    parent = bone.parent.name
    inherited = targets[parent] @ rest[parent].inverted() @ rest[bone.name]
    return inherited.inverted() @ target


def bake_action(armature, bind_matrices, source_path, action_name):
    animation = read_anim(str(source_path))
    old = bpy.data.actions.get(action_name)
    if old is not None:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(action_name)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action
    rest = {bone.name: bone.matrix_local.copy() for bone in armature.data.bones}
    ordered = sorted(armature.data.bones, key=lambda bone: len(bone.parent_recursive))
    previous_rotations = {}
    maximum_scale_delta = 0.0
    for pose in armature.pose.bones:
        pose.rotation_mode = "QUATERNION"
        pose.matrix_basis = Matrix.Identity(4)
    for frame in range(animation.FrameCount):
        targets = build_animation_targets(animation, frame, armature, bind_matrices)
        bpy.context.scene.frame_set(frame)
        for bone in ordered:
            pose = armature.pose.bones[bone.name]
            basis = basis_from_target(bone, targets[bone.name], targets, rest)
            location, rotation, scale = basis.decompose()
            previous = previous_rotations.get(bone.name)
            if previous is not None and previous.dot(rotation) < 0.0:
                rotation.negate()
            previous_rotations[bone.name] = rotation.copy()
            pose.location = location
            pose.rotation_quaternion = rotation
            pose.scale = scale
            maximum_scale_delta = max(maximum_scale_delta, *(abs(v - 1.0) for v in scale))
            pose.keyframe_insert("location", frame=frame, group=bone.name)
            pose.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
            pose.keyframe_insert("scale", frame=frame, group=bone.name)
    action.frame_start = 0
    action.frame_end = animation.FrameCount - 1
    if hasattr(action, "fcurves"):
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    return animation, action, maximum_scale_delta


def write_manifest(family, records, meshes, actions=None):
    path = family["outputRoot"] / "Manifests" / f"{family['familyId']}_skeleton.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "pipelineVersion": family["pipelineVersion"],
        "rigBaseline": "inverse_bind_v1",
        "familyId": family["familyId"],
        "variantIds": list(family.get("variantIds", [family["familyId"]])),
        "sourceScene": str(family["extractedRoot"] / family["sourceScene"]),
        "skeletonHash": skeleton_hash(records),
        "jointCount": len(records),
        "meshCount": len(meshes),
        "vertexCount": sum(len(mesh.data.vertices) for mesh in meshes),
        "locomotionType": family["locomotionType"],
        "legCount": family["legCount"],
        "joints": records,
        "availableActions": actions or {},
    }
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    return path, payload




def validate_family_skeleton(family, records):
    by_name = {record["name"]: record for record in records}
    failures = []
    for chain in family["loadBearingChains"]:
        missing = [name for name in chain if name not in by_name]
        if missing:
            failures.append(f"missing chain bones {missing}")
            continue
        for parent, child in zip(chain, chain[1:]):
            actual = by_name[child]["parent"]
            if actual != parent:
                failures.append(
                    f"{child} parent is {actual or '<root>'}, expected {parent}"
                )
    current_hash = skeleton_hash(records)
    for manifest_path in family.get("mustMatchManifests", []):
        path = Path(manifest_path)
        if not path.is_file():
            raise RuntimeError(f"Comparison skeleton manifest is missing: {path}")
        other = json.loads(path.read_text(encoding="utf-8"))
        if other.get("skeletonHash") != current_hash:
            failures.append(
                f"skeleton hash does not match {other.get('familyId', path.stem)}"
            )
    for manifest_path in family.get("mustDifferFromManifests", []):
        path = Path(manifest_path)
        if not path.is_file():
            raise RuntimeError(f"Comparison skeleton manifest is missing: {path}")
        other = json.loads(path.read_text(encoding="utf-8"))
        if other.get("skeletonHash") == current_hash:
            failures.append(
                f"skeleton hash unexpectedly matches {other.get('familyId', path.stem)}"
            )
    if failures:
        raise RuntimeError("Invalid family skeleton: " + "; ".join(failures))
    return current_hash

def baseline_stage(family):
    importer = import_source(family)
    bind_matrices, parent_names, records = capture_bind_data(importer)
    validate_family_skeleton(family, records)
    source_armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if len(source_armatures) != 1:
        raise RuntimeError(f"Expected one source armature, found {len(source_armatures)}")
    armature = build_inverse_bind_armature(
        source_armatures[0], family, bind_matrices, parent_names
    )
    meshes = rebind_meshes(armature)
    manifest_path, manifest = write_manifest(family, records, meshes)
    scene = bpy.context.scene
    scene["nms_family_id"] = family["familyId"]
    scene["nms_skeleton_hash"] = manifest["skeletonHash"]
    scene["nms_manifest"] = str(manifest_path)
    blend_path = family["outputRoot"] / f"{family['familyId']}Baseline.blend"
    blend_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    return armature, meshes, bind_matrices, records


def actions_stage(family, armature, meshes, bind_matrices, records):
    action_records = {}
    animations = {}
    for clip, relative in family["actions"].items():
        source = family["extractedRoot"] / relative
        require_file(source)
        name = f"{family['familyId']}_{clip.upper()}_InvBind"
        animation, action, scale_delta = bake_action(
            armature, bind_matrices, source, name
        )
        expected = family.get("expectedFrames", {}).get(clip)
        if expected is not None and animation.FrameCount != expected:
            raise RuntimeError(
                f"{clip} frame count is {animation.FrameCount}, expected {expected}"
            )
        animations[clip] = animation
        action_records[clip] = {
            "name": action.name,
            "source": str(source),
            "frames": animation.FrameCount,
            "maximumScaleDelta": scale_delta,
        }
    armature.animation_data.action = bpy.data.actions[action_records["walk"]["name"]]
    scene = bpy.context.scene
    scene.render.fps = 30
    scene.frame_start = 0
    scene.frame_end = action_records["walk"]["frames"] - 1
    scene.frame_set(0)
    write_manifest(family, records, meshes, action_records)
    blend_path = family["outputRoot"] / f"{family['familyId']}Baseline.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    return animations, action_records
