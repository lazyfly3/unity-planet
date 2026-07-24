"""Publish an approved, validated creature family to the Unity project."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import xml.etree.ElementTree as ET
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


def _texture_source_path(image):
    if image is None:
        return ""
    path = image.filepath_from_user()
    if not path:
        path = image.filepath
    return str(Path(bpy.path.abspath(path)).resolve()) if path else ""


def _normalize_name(value):
    return "".join(ch for ch in (value or "").lower() if ch.isalnum())


def _property_value(node, name, default=""):
    child = node.find(f"./Property[@name='{name}']")
    return child.get("value", default) if child is not None else default


def _material_mxml_records(family):
    records = {}
    root = family.get("extractedRoot")
    if not root:
        return records
    for path in Path(root).rglob("*.material.MXML"):
        try:
            tree = ET.parse(path)
            data = tree.getroot()
        except ET.ParseError:
            continue
        name = _property_value(data, "Name", path.stem.replace(".material", ""))
        record = {
            "sourceMxmlPath": str(path.resolve()),
            "sourceMxmlSha256": sha256_file(path),
            "materialPreset": "",
            "mainTexture": "",
            "normalTexture": "",
            "maskTexture": "",
            "emissionTexture": "",
            "metallic": -1.0,
            "roughness": -1.0,
            "glow": -1.0,
            "paletteStrength": -1.0,
            "warnings": [],
        }

        flags = " ".join(
            prop.get("value", "")
            for prop in data.findall(".//Property[@name='MaterialFlag']")
        ).lower()
        record["transparencyMode"] = (
            "Cutout"
            if any(value in flags for value in ("transparent", "alphacutout", "alpha"))
            else "Opaque"
        )
        record["maskChannelLayout"] = "R=Secondary,G=Accent,B=Smoothness,A=Emission"
        create_fur = _property_value(data, "CreateFur", "false").lower() == "true"
        if create_fur or "fur" in flags or "fur" in path.name.lower():
            record["materialPreset"] = "Fur"
        elif "metal" in path.name.lower() or "robot" in path.name.lower():
            record["materialPreset"] = "Mechanical"

        for sampler in data.findall(".//Property[@value='TkMaterialSampler']"):
            sampler_name = _property_value(sampler, "Name").lower()
            texture_path = _property_value(sampler, "Map")
            if not texture_path:
                continue
            texture_name = texture_path.replace("\\", "/").lower()
            if "diffuse" in sampler_name or "albedo" in sampler_name:
                record["mainTexture"] = texture_name
            elif "normal" in sampler_name:
                record["normalTexture"] = texture_name
            elif "mask" in sampler_name:
                record["maskTexture"] = texture_name
            elif "emiss" in sampler_name or "glow" in sampler_name:
                record["emissionTexture"] = texture_name

        params = data.find(".//Property[@name='Uniforms_Float']")
        if params is not None:
            for uniform in params.findall("./Property[@value='TkMaterialUniform_Float']"):
                uniform_name = _property_value(uniform, "Name").lower()
                values = uniform.find("./Property[@name='Values']")
                if values is None:
                    continue
                x = float(_property_value(values, "X", "-1") or -1)
                y = float(_property_value(values, "Y", "-1") or -1)
                z = float(_property_value(values, "Z", "-1") or -1)
                if uniform_name == "gmaterialparamsvec4":
                    record["roughness"] = max(0.0, min(1.0, y))
                    record["metallic"] = max(0.0, min(1.0, z))
                elif "sfx" in uniform_name or "glow" in uniform_name:
                    record["glow"] = max(record["glow"], max(0.0, x, y, z))

        if not record["materialPreset"]:
            record["materialPreset"] = _infer_material_preset_name(
                " ".join([name, path.name, record["mainTexture"], record["maskTexture"]]))
        records.setdefault(_normalize_name(name), []).append(record)
        records.setdefault(
            _normalize_name(path.stem.replace(".material", "")), []).append(record)
    return records


def _infer_material_preset_name(key):
    key = (key or "").lower()
    if _contains_any(key, ("glow", "emiss", "light", "neon", "energy")):
        return "Glow"
    if _contains_any(key, ("metal", "mech", "robot", "tech", "armour", "armor")):
        return "Mechanical"
    if _contains_any(key, ("bone", "skel", "teeth", "tooth", "claw", "rib")):
        return "Bone"
    if _contains_any(key, ("horn", "tusk", "spine", "spike", "shell", "plate", "hoof", "antler")):
        return "Horn"
    if _contains_any(key, ("fur", "hair", "mane", "wool")):
        return "Fur"
    return "Skin"


def _material_texture_slots(material):
    slots = {
        "mainTexture": "",
        "normalTexture": "",
        "maskTexture": "",
        "emissionTexture": "",
    }
    if material is None or not material.use_nodes:
        return slots
    for node in material.node_tree.nodes:
        if node.bl_idname != "ShaderNodeTexImage" or node.image is None:
            continue
        label = f"{node.name} {node.label} {_texture_source_path(node.image)}".lower()
        texture_name = _texture_source_path(node.image)
        if not texture_name:
            continue
        if any(key in label for key in ("normal", "nrm", ".normal", "_n")):
            slots["normalTexture"] = texture_name
        elif any(key in label for key in ("mask", "masks", "rough", "metal")):
            slots["maskTexture"] = texture_name
        elif any(key in label for key in ("emiss", "glow", "light")):
            slots["emissionTexture"] = texture_name
        elif not slots["mainTexture"]:
            slots["mainTexture"] = texture_name
    return slots


def _resolve_source_texture(family, texture_reference):
    if not texture_reference:
        return None
    reference = str(texture_reference).replace("\\", "/")
    path = Path(reference)
    candidates = []
    if path.is_absolute():
        candidates.append(path)
    else:
        candidates.append(family["extractedRoot"] / reference.lower())
        candidates.append(family["extractedRoot"] / reference)
    for candidate in candidates:
        if candidate.is_file():
            return candidate.resolve()
    return None


def _material_record_score(record, blender_slots):
    score = 0
    source_names = {
        Path(value).name.casefold()
        for value in blender_slots.values() if value
    }
    for key in ("mainTexture", "normalTexture", "maskTexture", "emissionTexture"):
        value = record.get(key, "")
        if value and Path(value).name.casefold() in source_names:
            score += 10
    return score


def _select_mxml_record(records, material_name, blender_slots):
    matches = records.get(_normalize_name(material_name), [])
    if not matches:
        return {}
    return max(matches, key=lambda value: _material_record_score(value, blender_slots))


def _convert_texture_for_unity(family, source, destination):
    if source is None:
        return "", ""
    source_hash = sha256_file(source)
    textures = destination / "Textures"
    textures.mkdir(parents=True, exist_ok=True)
    output_name = f"{source_hash[:12]}_{source.stem}.tga"
    output = textures / output_name
    if not output.is_file():
        texconv = family.get("texconv")
        if not texconv or not Path(texconv).is_file():
            return "", source_hash
        temporary = textures / "_texconv"
        temporary.mkdir(parents=True, exist_ok=True)
        result = subprocess.run(
            [
                str(texconv),
                "-y",
                "-f",
                "R8G8B8A8_UNORM",
                "-ft",
                "TGA",
                "-o",
                str(temporary),
                str(source),
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        generated = temporary / f"{source.stem}.TGA"
        if not generated.is_file():
            generated = temporary / f"{source.stem}.tga"
        if result.returncode != 0 or not generated.is_file():
            raise RuntimeError(
                f"texconv failed for {source}: {(result.stdout + result.stderr).strip()}")
        os.replace(generated, output)
        shutil.rmtree(temporary, ignore_errors=True)
    return f"Textures/{output_name}", source_hash


def _contains_any(value, keys):
    value = (value or "").lower()
    return any(key in value for key in keys)


def _infer_material_preset(material, slots):
    key = " ".join([
        material.name if material else "",
        slots.get("mainTexture", ""),
        slots.get("normalTexture", ""),
        slots.get("maskTexture", ""),
        slots.get("emissionTexture", ""),
    ]).lower()
    return _infer_material_preset_name(key)


def _preset_material_parameters(preset, has_main):
    values = {
        "metallic": 0.0,
        "roughness": 0.64,
        "glow": 0.0,
        "paletteStrength": 0.28 if has_main else 1.0,
    }
    if preset == "Fur":
        values.update({"roughness": 0.82, "paletteStrength": 0.18 if has_main else 0.85})
    elif preset == "Horn":
        values.update({"roughness": 0.58, "paletteStrength": 0.12 if has_main else 0.55})
    elif preset == "Shell":
        values.update({"roughness": 0.48, "paletteStrength": 0.16 if has_main else 0.62})
    elif preset == "Bone":
        values.update({"roughness": 0.69, "paletteStrength": 0.08 if has_main else 0.35})
    elif preset == "Mechanical":
        values.update({"metallic": 0.38, "roughness": 0.42, "paletteStrength": 0.18 if has_main else 0.7})
    elif preset == "Glow":
        values.update({"roughness": 0.5, "glow": 1.35, "paletteStrength": 0.22 if has_main else 0.85})
    return values


def _write_material_manifest(family, destination, family_manifest):
    family_id = family["familyId"]
    path = destination / f"{family_id}_materials.json"
    mxml_records = _material_mxml_records(family)
    materials = {}
    renderer_records = []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        module_names = [
            module["name"]
            for module in family_manifest.get("modules", [])
            if obj.name in module.get("objects", [])
        ]
        slot_records = []
        for index, slot in enumerate(obj.material_slots):
            material = slot.material
            if material is None:
                continue
            slots = _material_texture_slots(material)
            mxml = _select_mxml_record(mxml_records, material.name, slots)
            identity = mxml.get("sourceMxmlSha256", "")
            if not identity:
                identity = hashlib.sha256(
                    "|".join(sorted(value for value in slots.values() if value))
                    .encode("utf-8")
                ).hexdigest()
            material_id = (
                f"{material.name}_{identity[:10]}" if identity else material.name
            )
            if material_id not in materials:
                merged_slots = dict(slots)
                for slot_key in ("mainTexture", "normalTexture", "maskTexture", "emissionTexture"):
                    if mxml.get(slot_key):
                        merged_slots[slot_key] = mxml[slot_key]
                preset = mxml.get("materialPreset") or _infer_material_preset(material, merged_slots)
                parameters = _preset_material_parameters(preset, bool(merged_slots["mainTexture"]))
                record = {
                    "materialId": material_id,
                    "sourceMaterialName": material.name,
                    "sourceMxmlPath": mxml.get("sourceMxmlPath", ""),
                    "sourceMxmlSha256": mxml.get("sourceMxmlSha256", ""),
                    "materialPreset": preset,
                    "transparencyMode": mxml.get("transparencyMode", "Opaque"),
                    "maskChannelLayout": mxml.get(
                        "maskChannelLayout",
                        "R=Secondary,G=Accent,B=Smoothness,A=Emission"),
                    "mainTexture": "",
                    "normalTexture": "",
                    "maskTexture": "",
                    "emissionTexture": "",
                    "metallic": parameters["metallic"],
                    "roughness": parameters["roughness"],
                    "glow": parameters["glow"],
                    "paletteStrength": parameters["paletteStrength"],
                    "warnings": [],
                }
                for slot_key in (
                    "mainTexture",
                    "normalTexture",
                    "maskTexture",
                    "emissionTexture",
                ):
                    source = _resolve_source_texture(family, merged_slots[slot_key])
                    converted, source_hash = _convert_texture_for_unity(
                        family, source, destination)
                    record[slot_key] = converted
                    record[slot_key + "SourcePath"] = (
                        str(source) if source is not None else merged_slots[slot_key])
                    record[slot_key + "SourceSha256"] = source_hash
                    if merged_slots[slot_key] and source is None:
                        record["warnings"].append(
                            f"missing source texture: {merged_slots[slot_key]}")
                for param_key in ("metallic", "roughness", "glow", "paletteStrength"):
                    if mxml.get(param_key, -1.0) >= 0.0:
                        record[param_key] = mxml[param_key]
                if not record["mainTexture"]:
                    record["warnings"].append("no main texture found in Blender material")
                if not mxml:
                    record["warnings"].append("no material MXML matched; used Blender material fallback")
                materials[material_id] = record
            slot_records.append({
                "slot": index,
                "materialId": material_id,
            })
        renderer_records.append({
            "rendererName": obj.name,
            "modules": module_names,
            "slots": slot_records,
        })
    manifest = {
        "pipelineVersion": family["pipelineVersion"],
        "familyId": family_id,
        "skeletonHash": family_manifest["skeletonHash"],
        "materials": sorted(materials.values(), key=lambda value: value["materialId"]),
        "renderers": sorted(renderer_records, key=lambda value: value["rendererName"]),
    }
    _write_atomic(path, json.dumps(manifest, indent=2).encode("utf-8"))
    return path


def unity_publish_stage(family, approval):
    if not family.get("publishEnabled", True):
        reason = family.get("publishDisabledReason", "family is diagnostic-only")
        raise RuntimeError(f"{family['familyId']} cannot be published: {reason}")
    validation_path, _, approved_hash = validation_hash(family)
    required_approval = approved_hash
    review_batch = family.get("reviewBatch")
    if review_batch:
        aggregate_path = (
            family["outputRoot"].parent
            / f"{review_batch}_aggregate_review.json"
        )
        if not aggregate_path.is_file():
            raise RuntimeError(
                f"Aggregate review report is missing: {aggregate_path}")
        aggregate = json.loads(aggregate_path.read_text(encoding="utf-8"))
        member = next(
            (
                item for item in aggregate.get("families", [])
                if item.get("familyId") == family["familyId"]
            ),
            None,
        )
        if member is None or member.get("validationHash") != approved_hash:
            raise RuntimeError(
                "Aggregate review no longer matches the current family validation")
        required_approval = aggregate.get("aggregateHash", "")
    if not approval or approval != required_approval:
        raise RuntimeError(
            "unity_publish requires the exact approved review hash returned "
            "after the human-reviewed validate/review run"
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
    supports_run = bool(family.get("supportsRun", "run" in actions))
    required_actions = ("idle", "walk", "run") if supports_run else ("idle", "walk")
    if any(key not in actions for key in required_actions):
        raise RuntimeError(
            f"Required native actions are missing: {', '.join(required_actions)}")

    project_root = Path(family["unityProjectRoot"]).resolve()
    generated_root = project_root / family.get(
        "unityOutputRoot", "Assets/Creatures/Generated/NMS"
    )
    destination = generated_root / family_id
    destination.mkdir(parents=True, exist_ok=True)
    model_path = destination / f"{family_id}.fbx"
    export = _export_fbx(family, model_path, family_manifest)
    material_manifest_path = _write_material_manifest(
        family,
        destination,
        family_manifest)

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
        for key in required_actions
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
        "materialManifestAsset": _asset_path(project_root, material_manifest_path),
        "locomotionType": family["locomotionType"],
        "surfaceMode": unity.get("surfaceMode", "Ground"),
        "variantIds": list(family.get("variantIds", [family_id])),
        "supportsRun": supports_run,
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
