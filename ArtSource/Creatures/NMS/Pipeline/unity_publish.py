"""Publish an approved, validated creature family to the Unity project."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
from pathlib import Path

import bpy


def sha256_file(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validation_hash(family):
    path = family["outputRoot"] / "Validation" / (
        f"{family['familyId']}_validation.json"
    )
    if not path.is_file():
        raise RuntimeError(f"Validation report is missing: {path}")
    report = json.loads(path.read_text(encoding="utf-8"))
    if not report.get("passed"):
        raise RuntimeError(f"Family validation did not pass: {path}")
    return path, report, sha256_file(path)


def _asset_path(project_root, absolute):
    relative = Path(absolute).resolve().relative_to(Path(project_root).resolve())
    return relative.as_posix()


def _write_atomic(path, payload):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def _copy_atomic(source, destination):
    _write_atomic(destination, Path(source).read_bytes())


def _export_fbx(family, destination, family_manifest):
    library = family["outputRoot"] / (
        f"{family['familyId']}SpeciesLibrary.blend"
    )
    if not library.is_file():
        raise RuntimeError(f"Species library is missing: {library}")
    bpy.ops.wm.open_mainfile(filepath=str(library))
    armatures = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "ARMATURE"
        and obj.get("nms_rig_baseline") == "inverse_bind_v1"
    ]
    if len(armatures) != 1:
        raise RuntimeError(
            f"Expected one approved inverse-bind armature, found {len(armatures)}"
        )
    armature = armatures[0]
    compatible_mesh_names = {
        object_name
        for module in family_manifest.get("modules", [])
        if module.get("compatible", False)
        for object_name in module.get("objects", [])
    }
    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and obj.name in compatible_mesh_names
    ]
    if not meshes:
        raise RuntimeError("No compatible meshes are available for Unity export")
    for obj in bpy.context.view_layer.objects:
        obj.hide_set(True)
        obj.hide_render = True
    armature.hide_set(False)
    armature.hide_render = False
    armature.select_set(True)
    for mesh in meshes:
        mesh.hide_set(False)
        mesh.hide_render = False
        mesh.select_set(True)
    bpy.context.view_layer.objects.active = armature
    for action in bpy.data.actions:
        action.use_fake_user = True
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(".tmp.fbx")
    if temporary.exists():
        temporary.unlink()
    bpy.ops.export_scene.fbx(
        filepath=str(temporary),
        use_selection=False,
        use_visible=True,
        object_types={"ARMATURE", "MESH"},
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        axis_forward="-Z",
        axis_up="Y",
        use_space_transform=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        path_mode="COPY",
        embed_textures=False,
        use_custom_props=True,
    )
    os.replace(temporary, destination)
    return {
        "armature": armature.name,
        "meshCount": len(meshes),
        "fbxSha256": sha256_file(destination),
    }


def unity_publish_stage(family, approval):
    if not family.get("publishEnabled", True):
        reason = family.get("publishDisabledReason", "family is diagnostic-only")
        raise RuntimeError(f"{family['familyId']} cannot be published: {reason}")
    validation_path, _, approved_hash = validation_hash(family)
    if not approval or approval != approved_hash:
        raise RuntimeError(
            "unity_publish requires the exact validationHash returned after "
            "the human-reviewed validate/review run"
        )
    family_id = family["familyId"]
    family_manifest_path = (
        family["outputRoot"] / "Manifests" / f"{family_id}_family.json"
    )
    skeleton_manifest_path = (
        family["outputRoot"] / "Manifests" / f"{family_id}_skeleton.json"
    )
    if not family_manifest_path.is_file() or not skeleton_manifest_path.is_file():
        raise RuntimeError("Family or skeleton manifest is missing")
    family_manifest = json.loads(family_manifest_path.read_text(encoding="utf-8"))
    skeleton_manifest = json.loads(skeleton_manifest_path.read_text(encoding="utf-8"))
    if family_manifest.get("skeletonHash") != skeleton_manifest.get("skeletonHash"):
        raise RuntimeError("Family and skeleton manifests disagree on skeletonHash")
    actions = family_manifest.get("availableActions", {})
    if any(key not in actions for key in ("idle", "walk", "run")):
        raise RuntimeError("Idle, Walk and Run must all be present before publishing")

    project_root = Path(family["unityProjectRoot"]).resolve()
    generated_root = project_root / family.get(
        "unityOutputRoot", "Assets/Creatures/Generated/NMS"
    )
    destination = generated_root / family_id
    destination.mkdir(parents=True, exist_ok=True)
    model_path = destination / f"{family_id}.fbx"
    export = _export_fbx(family, model_path, family_manifest)

    published_family = destination / f"{family_id}_family.json"
    published_skeleton = destination / f"{family_id}_skeleton.json"
    published_validation = destination / f"{family_id}_validation.json"
    _copy_atomic(family_manifest_path, published_family)
    _copy_atomic(skeleton_manifest_path, published_skeleton)
    _copy_atomic(validation_path, published_validation)

    unity = family.get("unity", {})
    chain_records = [
        {"id": f"Leg{index + 1}", "bones": list(chain)}
        for index, chain in enumerate(family["loadBearingChains"])
    ]
    action_records = [
        {
            "key": key,
            "clipName": actions[key]["name"],
            "assetPath": _asset_path(project_root, model_path),
            "frames": int(actions[key]["frames"]),
            "loop": True,
        }
        for key in ("idle", "walk", "run")
    ]
    publish = {
        "pipelineVersion": family["pipelineVersion"],
        "familyId": family_id,
        "skeletonHash": family_manifest["skeletonHash"],
        "validationHash": approved_hash,
        "sourceKind": "Fbx",
        "sourceModelAsset": _asset_path(project_root, model_path),
        "sourcePrefabAsset": "",
        "familyManifestAsset": _asset_path(project_root, published_family),
        "skeletonManifestAsset": _asset_path(project_root, published_skeleton),
        "locomotionType": family["locomotionType"],
        "surfaceMode": unity.get("surfaceMode", "Ground"),
        "variantIds": list(family.get("variantIds", [family_id])),
        "legCount": family["legCount"],
        "allowRareModules": bool(unity.get("allowRareModules", False)),
        "excludedModulePrefixes": list(unity.get("excludedModulePrefixes", [])),
        "selectionWeight": int(unity.get("selectionWeight", 1)),
        "walkSpeed": float(unity.get("walkSpeed", 1.35)),
        "surfaceRootOffset": float(unity.get("surfaceRootOffset", -1.0)),
        "hoverClearance": float(unity.get("hoverClearance", 0.0)),
        "importScale": float(unity.get("importScale", 1.0)),
        "importEuler": {
            "x": float(unity.get("importEuler", [0, 0, 0])[0]),
            "y": float(unity.get("importEuler", [0, 0, 0])[1]),
            "z": float(unity.get("importEuler", [0, 0, 0])[2]),
        },
        "localForwardAxis": {
            "x": float(unity.get("localForwardAxis", [0, 0, 1])[0]),
            "y": float(unity.get("localForwardAxis", [0, 0, 1])[1]),
            "z": float(unity.get("localForwardAxis", [0, 0, 1])[2]),
        },
        "actions": action_records,
        "loadBearingChains": chain_records,
        "sourceFbxSha256": export["fbxSha256"],
    }
    publish_path = destination / f"{family_id}.publish.json"
    _write_atomic(
        publish_path,
        json.dumps(publish, ensure_ascii=False, indent=2).encode("utf-8"),
    )
    return {
        "familyId": family_id,
        "validationHash": approved_hash,
        "fbx": str(model_path),
        "publishManifest": str(publish_path),
        "meshCount": export["meshCount"],
        "fbxSha256": export["fbxSha256"],
    }
