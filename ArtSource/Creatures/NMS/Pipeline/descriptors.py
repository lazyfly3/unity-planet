"""Descriptor parsing and deterministic species showcase generation."""

from __future__ import annotations

import json
import xml.etree.ElementTree as ET

import bpy
from mathutils import Matrix, Vector


def direct_property(element, name):
    for child in element:
        if child.tag == "Property" and child.get("name") == name:
            return child
    return None


def parse_groups(container, parent_path=()):
    groups = []
    for group in container:
        if group.tag != "Property":
            continue
        if group.get("value") != "TkResourceDescriptorList":
            groups.extend(parse_groups(group, parent_path))
            continue
        type_property = direct_property(group, "TypeId")
        descriptors = direct_property(group, "Descriptors")
        if descriptors is None:
            continue
        type_id = type_property.get("value") if type_property is not None else ""
        parsed = {"typeId": type_id, "path": list(parent_path), "candidates": []}
        for candidate in descriptors:
            if candidate.get("value") != "TkResourceDescriptorData":
                continue
            name_property = direct_property(candidate, "Name")
            id_property = direct_property(candidate, "Id")
            chance_property = direct_property(candidate, "Chance")
            name = name_property.get("value") if name_property is not None else ""
            item_id = id_property.get("value") if id_property is not None else ""
            chance = float(chance_property.get("value", "0")) if chance_property is not None else 0.0
            child_property = direct_property(candidate, "Children")
            children = parse_groups(
                child_property, parent_path + (type_id, name)
            ) if child_property is not None else []
            parsed["candidates"].append({
                "id": item_id,
                "name": name,
                "chance": chance,
                "path": list(parent_path + (type_id, name)),
                "children": children,
            })
        if parsed["candidates"]:
            groups.append(parsed)
    return groups


def flatten_groups(groups):
    output = []
    for group in groups:
        output.append(group)
        for candidate in group["candidates"]:
            output.extend(flatten_groups(candidate["children"]))
    return output


_MASK_64 = (1 << 64) - 1
_DESCRIPTOR_STREAM = 0xE7037ED1A0B428DB


class StableRandom:
    """SplitMix64 stream mirrored by NmsRandomCreatureGenerator."""

    def __init__(self, seed):
        value = int(seed) & _MASK_64
        self.state = value if value else 0x9E3779B97F4A7C15

    def next_u64(self):
        self.state = (self.state + 0x9E3779B97F4A7C15) & _MASK_64
        value = self.state
        value = ((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9) & _MASK_64
        value = ((value ^ (value >> 27)) * 0x94D049BB133111EB) & _MASK_64
        return (value ^ (value >> 31)) & _MASK_64

    def randrange(self, maximum):
        return 0 if maximum <= 1 else self.next_u64() % maximum

    def random(self):
        return (self.next_u64() >> 40) * (1.0 / 16777216.0)


def is_excluded(candidate, excluded_names):
    name = candidate.get("name", "").casefold()
    return bool(name) and any(
        name == excluded or name.startswith(excluded)
        for excluded in excluded_names
    )


def candidate_viable(candidate, excluded_names):
    if is_excluded(candidate, excluded_names):
        return False
    for group in candidate.get("children", []):
        if not any(
                candidate_viable(child, excluded_names)
                for child in group["candidates"]):
            return False
    return True


def weighted_choice(candidates, rng, excluded_names=()):
    allowed = [
        item for item in candidates
        if candidate_viable(item, excluded_names)
    ]
    if not allowed:
        raise RuntimeError("Descriptor group has no compatible candidates")
    weighted = [item for item in allowed if item["chance"] > 0.0]
    if not weighted:
        return allowed[rng.randrange(len(allowed))]
    cursor = rng.random() * sum(item["chance"] for item in weighted)
    for item in weighted:
        cursor -= item["chance"]
        if cursor <= 0.0:
            return item
    return weighted[-1]


def choose(groups, seed, excluded_names=()):
    rng = StableRandom((int(seed) & _MASK_64) ^ _DESCRIPTOR_STREAM)
    selected = set()
    records = []

    def visit(current):
        for group in current:
            chosen = weighted_choice(group["candidates"], rng, excluded_names)
            if chosen["name"]:
                selected.add(chosen["name"])
            records.append({
                "typeId": group["typeId"],
                "id": chosen["id"],
                "name": chosen["name"],
                "path": chosen["path"],
            })
            visit(chosen["children"])
    visit(groups)
    return selected, records


def unique_seeds(
        groups, count=8, search_limit=10000, excluded_names=()):
    result = []
    signatures = set()
    for seed in range(search_limit):
        _, records = choose(groups, seed, excluded_names)
        signature = tuple((record["typeId"], record["id"], record["name"]) for record in records)
        if signature in signatures:
            continue
        signatures.add(signature)
        result.append(seed)
        if len(result) == count:
            return result
    if result:
        return result
    raise RuntimeError(f"Only found {len(result)} unique descriptor signatures")


def descriptor_ancestors(obj, descriptor_names):
    stored = obj.get("nms_source_ancestry")
    if stored:
        return [
            name for name in json.loads(stored)
            if name in descriptor_names
        ]
    names = []
    current = obj
    while current is not None:
        base = current.name.split(".")[0]
        if base in descriptor_names:
            names.append(base)
        current = current.parent
    names.reverse()
    return names


def enabled(obj, selected, descriptor_names):
    return all(name in selected for name in descriptor_ancestors(obj, descriptor_names))


def visible_bounds(objects):
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    minimum = Vector(tuple(min(point[i] for point in points) for i in range(3)))
    maximum = Vector(tuple(max(point[i] for point in points) for i in range(3)))
    return minimum, maximum


def library_stage(family, armature, meshes, action_records):
    descriptor_path = family["extractedRoot"] / family["descriptor"]
    if not descriptor_path.is_file():
        raise RuntimeError(f"Descriptor is missing: {descriptor_path}")
    groups = parse_groups(ET.parse(descriptor_path).getroot())
    flat_groups = flatten_groups(groups)
    descriptor_names = {
        candidate["name"] for group in flat_groups
        for candidate in group["candidates"] if candidate["name"]
    }
    output = family["outputRoot"]
    manifest_path = (
        output / "Manifests" / f"{family['familyId']}_family.json"
    )
    validation_input = {
        "pipelineVersion": family["pipelineVersion"],
        "animationTransformPolicy": (
            "freeze_first_frame_and_lock_load_bearing_pose_v2"
            if family.get("stripAnimatedBoneScale", False)
            else "native_trs"
        ),
        "loadBearingChains": family["loadBearingChains"],
        "animationTransformCorrectionScope": family.get(
            "animationTransformCorrectionScope", "allWeighted"),
        "actions": {
            clip: {
                "source": record.get("source", ""),
                "frames": int(record.get("frames", 0)),
            }
            for clip, record in sorted(action_records.items())
        },
    }
    prior_modules = {}
    if manifest_path.is_file():
        prior_manifest = json.loads(
            manifest_path.read_text(encoding="utf-8")
        )
        if prior_manifest.get("validationInput") == validation_input:
            prior_modules = {
                module["name"].casefold(): module
                for module in prior_manifest.get("modules", [])
                if module.get("name") and not module.get("compatible", True)
            }
    runtime_settings = dict(family.get("unity", {}))
    runtime_settings.update(family.get("runtimeSettings", {}))
    configured_exclusions = {
        str(value).casefold()
        for value in runtime_settings.get(
            "excludedModulePrefixes", []
        )
        if value
    }
    excluded_names = tuple(sorted(
        configured_exclusions | set(prior_modules)
    ))
    pose_bones = set(armature.pose.bones.keys())
    excluded_mesh_objects = {
        str(value).casefold()
        for value in runtime_settings.get(
            "excludedMeshObjects", []
        )
        if value
    }
    module_records = []
    for name in sorted(descriptor_names):
        objects = [
            obj for obj in meshes
            if obj.name.casefold() not in excluded_mesh_objects
            if descriptor_ancestors(obj, descriptor_names)
            and descriptor_ancestors(obj, descriptor_names)[-1] == name
        ]
        unknown = sorted({
            group.name for obj in objects for group in obj.vertex_groups
            if group.name not in pose_bones
        })
        prior = prior_modules.get(name.casefold())
        configured_excluded = is_excluded(
            {"name": name}, configured_exclusions
        )
        validation_failures = []
        if unknown:
            validation_failures.append("unknown weighted bones")
        if prior is not None:
            validation_failures.extend(prior.get("validationFailures", []))
        elif configured_excluded:
            validation_failures.append("excluded by family configuration")
        module_records.append({
            "name": name,
            "objects": [obj.name for obj in objects],
            "materials": sorted({
                slot.material.name for obj in objects for slot in obj.material_slots
                if slot.material is not None
            }),
            "unknownWeightGroups": unknown,
            "compatible": not unknown and not prior and not configured_excluded,
            "validationFailures": validation_failures,
            "meshValidation": (
                prior.get("meshValidation", {}) if prior else {}
            ),
        })

    baseline_selected, baseline_choices = choose(
        groups, 0, excluded_names)
    baseline_meshes = []
    for obj in meshes:
        is_enabled = (
            obj.name.casefold() not in excluded_mesh_objects
            and enabled(obj, baseline_selected, descriptor_names)
        )
        obj.hide_render = not is_enabled
        obj.hide_set(not is_enabled)
        if is_enabled and obj.data.vertices:
            baseline_meshes.append(obj)
    if not baseline_meshes:
        raise RuntimeError("Descriptor baseline selected no visible meshes")

    manifest = {
        "pipelineVersion": family["pipelineVersion"],
        "rngAlgorithm": "SplitMix64-v1",
        "familyId": family["familyId"],
        "variantIds": list(family.get("variantIds", [family["familyId"]])),
        "skeletonHash": bpy.context.scene["nms_skeleton_hash"],
        "sourceScene": str(family["extractedRoot"] / family["sourceScene"]),
        "locomotionType": family["locomotionType"],
        "legCount": family["legCount"],
        "descriptorGroups": flat_groups,
        "availableActions": action_records,
        "validationInput": validation_input,
        "modules": module_records,
        "excludedMeshObjects": sorted(excluded_mesh_objects),
        "baselineSeed": 0,
        "baselineChoices": baseline_choices,
    }
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    baseline_path = output / f"{family['familyId']}Baseline.blend"
    bpy.context.scene["nms_family_manifest"] = str(manifest_path)
    bpy.ops.wm.save_as_mainfile(filepath=str(baseline_path))
    library_path = output / f"{family['familyId']}SpeciesLibrary.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(library_path))

    source_meshes = list(meshes)
    for obj in source_meshes:
        obj.hide_set(True)
        obj.hide_render = True
    armature.hide_set(True)
    armature.hide_render = True
    collection = bpy.data.collections.new("SPECIES_SHOWCASE")
    bpy.context.scene.collection.children.link(collection)
    minimum, maximum = visible_bounds(baseline_meshes)
    spacing = max(max(maximum - minimum) * 1.7, 4.0)
    showcase_records = []
    seeds = unique_seeds(groups, 8, excluded_names=excluded_names)
    manifest["showcaseSignatureCount"] = len(seeds)
    manifest["limitedDiversity"] = len(seeds) < 8
    manifest_path.write_text(
        json.dumps(manifest, indent=2), encoding="utf-8")
    for index, seed in enumerate(seeds):
        selected, choices = choose(groups, seed, excluded_names)
        visible = [
            obj for obj in source_meshes
            if (obj.data.vertices
                and obj.name.casefold() not in excluded_mesh_objects
                and enabled(obj, selected, descriptor_names))
        ]
        row, column = divmod(index, 4)
        offset = Vector(((column - 1.5) * spacing, (0.5 - row) * spacing, 0.0))
        translation = Matrix.Translation(offset)
        rig = armature.copy()
        rig.data = armature.data
        rig.name = f"{family['familyId']}_Seed_{seed:04d}_Armature"
        collection.objects.link(rig)
        rig.parent = None
        rig.matrix_world = translation @ armature.matrix_world
        rig.hide_set(False)
        rig.hide_render = True
        rig.show_in_front = False
        rig.animation_data_create()
        rig.animation_data.action = bpy.data.actions[action_records["walk"]["name"]]
        duplicated = []
        for source in visible:
            obj = source.copy()
            obj.data = source.data
            obj.name = f"{family['familyId']}_Seed_{seed:04d}_{source.name}"
            collection.objects.link(obj)
            obj.parent = None
            obj.matrix_world = translation @ source.matrix_world
            obj.hide_set(False)
            obj.hide_render = False
            for modifier in obj.modifiers:
                if modifier.type == "ARMATURE":
                    modifier.object = rig
            duplicated.append(obj)
        showcase_records.append({
            "seed": seed,
            "choices": choices,
            "meshCount": len(duplicated),
            "vertexCount": sum(len(obj.data.vertices) for obj in duplicated),
        })
    scene = bpy.context.scene
    scene.frame_start = 0
    scene.frame_end = action_records["walk"]["frames"] - 1
    scene.frame_set(0)
    scene["nms_showcase_records"] = json.dumps(showcase_records)
    showcase_path = output / f"{family['familyId']}SpeciesShowcase.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(showcase_path))
    return {
        "manifest": str(manifest_path),
        "baseline": str(baseline_path),
        "library": str(library_path),
        "showcase": str(showcase_path),
        "seeds": seeds,
        "showcaseSignatureCount": len(seeds),
        "descriptorGroupCount": len(flat_groups),
        "moduleCount": len(module_records),
    }
