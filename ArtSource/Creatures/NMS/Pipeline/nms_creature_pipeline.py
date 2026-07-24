"""Blender-only NMS creature family review pipeline.

The pipeline deliberately stops before FBX/Unity export.  Its invariant is that
mesh, inverse bind matrices, armature and native actions all come from the same
source family.  Never replace the inverse-bind armature with one inferred from
joint display positions.
"""

from __future__ import annotations

import contextlib
import hashlib
import importlib
import io
import json
import math
import random
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector
from nmsdk.ModelImporter.import_scene import ImportScene
from nmsdk.ModelImporter.readers import read_anim


PIPELINE_DIR = Path(__file__).resolve().parent
CONFIG_PATH = PIPELINE_DIR / "creature_families.json"
MIN_EDGE = 1.0e-4
MIN_TRIANGLE_AREA = 1.0e-7


def load_configuration(family_id: str):
    config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    if family_id not in config["families"]:
        raise KeyError(f"Unknown creature family: {family_id}")
    family = dict(config["families"][family_id])
    family["familyId"] = family_id
    family["pipelineVersion"] = config["pipelineVersion"]
    variants = list(family.get("sourceVariants", []))
    if variants:
        active_id = family.get("activeVariantId", variants[0].get("variantId"))
        active = next(
            (variant for variant in variants if variant.get("variantId") == active_id),
            None,
        )
        if active is None:
            raise KeyError(f"{family_id}: unknown activeVariantId {active_id}")
        for key in (
            "sourceScene",
            "descriptorSource",
            "descriptor",
            "actions",
            "expectedFrames",
        ):
            if key in active:
                family[key] = active[key]
        family["variantIds"] = [
            variant["variantId"] for variant in variants if variant.get("variantId")
        ]
    family["supportsRun"] = bool(family.get(
        "supportsRun", "run" in family.get("actions", {})))
    for key in (
        "extractedRoot",
        "outputRoot",
        "mbinCompiler",
        "gamePakRoot",
        "hgpakTool",
        "texconv",
        "unityProjectRoot",
    ):
        value = family.get(key, config.get(key))
        if value:
            family[key] = Path(value)
    return family


def require_file(path: Path):
    if not path.is_file():
        raise RuntimeError(f"Required source is missing: {path}")


def matrix_from_flat_transposed(values) -> Matrix:
    result = Matrix((values[0:4], values[4:8], values[8:12], values[12:16]))
    result.transpose()
    return result


def matrix_to_flat(matrix: Matrix):
    return [float(matrix[r][c]) for r in range(4) for c in range(4)]


def _float_bytes(values):
    return b"".join(struct.pack("<d", float(value)) for value in values)


def skeleton_hash(records):
    digest = hashlib.sha256()
    for record in sorted(records, key=lambda item: (item["jointIndex"], item["name"])):
        digest.update(record["name"].encode("utf-8") + b"\0")
        digest.update(record["parent"].encode("utf-8") + b"\0")
        digest.update(struct.pack("<i", record["jointIndex"]))
        digest.update(_float_bytes(record["inverseBindMatrix"]))
    return digest.hexdigest()


class _OutputMirror(io.StringIO):
    def __init__(self, target):
        super().__init__()
        self.target = target

    def write(self, value):
        self.target.write(value)
        return super().write(value)

    def flush(self):
        self.target.flush()
        return super().flush()


def install_material_compatibility():
    material_module = importlib.import_module("nmsdk.NMS.material_node")
    scene_module = importlib.import_module("nmsdk.ModelImporter.import_scene")
    original = material_module.create_material_node
    if getattr(original, "_nms_pipeline_safe", False):
        scene_module.create_material_node = original
        return
    def safe_create_material_node(*args, **kwargs):
        before = set(bpy.data.materials)
        try:
            return original(*args, **kwargs)
        except UnboundLocalError as exc:
            if "lColourVec4" not in str(exc):
                raise
            created = [material for material in bpy.data.materials if material not in before]
            material = created[-1] if created else bpy.data.materials.new("NMS_MissingDiffuse")
            material["nms_pipeline_fallback"] = "missing_diffuse_texture"
            print(f"Warning: using neutral material fallback for {args[0]}")
            return material
    safe_create_material_node._nms_pipeline_safe = True
    material_module.create_material_node = safe_create_material_node
    scene_module.create_material_node = safe_create_material_node


def import_source(family):
    extracted = family["extractedRoot"]
    source = extracted / family["sourceScene"]
    require_file(source)
    preferences = bpy.context.preferences.addons["nmsdk"].preferences
    preferences.unpacked_pcbanks_dir = str(extracted)
    preferences.mbincompiler_path = str(family["mbinCompiler"])
    install_material_compatibility()
    importer = ImportScene(
        str(source), parent_obj=None, ref_scenes={},
        settings={
            "clear_scene": True,
            "import_collisions": False,
            "show_collisions": False,
            "import_recursively": False,
            "import_bones": True,
            "import_anims": False,
        },
    )
    output = _OutputMirror(sys.stdout)
    with contextlib.redirect_stdout(output):
        importer.render_scene()
    if "An exception ocurred while rendering" in output.getvalue():
        raise RuntimeError("NMSDK swallowed a scene import exception; baseline rejected")
    expected = sum(
        1 for node in importer.scene_node_data.iter()
        if node.Type == "MESH" and node.Name.upper() in importer.mesh_metadata
    )
    actual = sum(
        1 for node in importer.local_objects
        if getattr(node, "Type", None) == "MESH"
    )
    if actual != expected:
        raise RuntimeError(f"Incomplete scene import: {actual}/{expected} mesh nodes")
    return importer


def capture_bind_data(importer):
    bind_matrices = {}
    parent_names = {}
    records = []
    bindings = importer.mesh_binding_data["JointBindings"]
    for joint in importer.joints:
        index = joint.Attribute("JOINTINDEX", int)
        inverse_bind = matrix_from_flat_transposed(bindings[index].InvBindMatrix)
        parent = joint.parent.Name if joint.parent and joint.parent.Type == "JOINT" else None
        bind = inverse_bind.inverted()
        bind_matrices[joint.Name] = bind
        parent_names[joint.Name] = parent
        records.append({
            "name": joint.Name,
            "parent": parent or "",
            "jointIndex": index,
            "inverseBindMatrix": [float(v) for v in bindings[index].InvBindMatrix],
            "bindMatrix": matrix_to_flat(bind),
        })
    if not records:
        raise RuntimeError("Source geometry contains no joint bindings")
    return bind_matrices, parent_names, records


def choose_bone_endpoint(name, bind_matrices, parent_names):
    origin = bind_matrices[name].translation
    children = [
        child for child in bind_matrices
        if parent_names[child] == name
        and (bind_matrices[child].translation - origin).length > 1.0e-4
    ]
    if children:
        children.sort(key=lambda child: (
            "CTRL" in child.upper(), "END" in child.upper(),
            child.lower().startswith("joint"),
            (bind_matrices[child].translation - origin).length,
        ))
        return bind_matrices[children[0]].translation
    parent = parent_names[name]
    if parent:
        distance = (origin - bind_matrices[parent].translation).length
        if distance > 1.0e-4:
            return origin + bind_matrices[name].to_quaternion() @ Vector(
                (0.0, max(0.015, min(0.08, distance * 0.25)), 0.0)
            )
    return origin + bind_matrices[name].to_quaternion() @ Vector((0.0, 0.035, 0.0))


def build_inverse_bind_armature(source_armature, family, bind_matrices, parent_names):
    source_armature.name = "Armature_NMSDK_JointPosition_Broken"
    source_armature.hide_viewport = True
    source_armature.hide_render = True
    data = bpy.data.armatures.new(family["familyId"] + "InverseBindArmature")
    armature = bpy.data.objects.new(family["familyId"] + "_Armature", data)
    bpy.context.collection.objects.link(armature)
    armature.parent = source_armature.parent
    armature.matrix_local = source_armature.matrix_local.copy()
    armature.show_in_front = True
    data.display_type = "STICK"
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        for name, bind in bind_matrices.items():
            bone = data.edit_bones.new(name)
            location, rotation, _ = bind.decompose()
            bone.head = location
            bone.tail = choose_bone_endpoint(name, bind_matrices, parent_names)
            bone.align_roll(rotation @ Vector((0.0, 0.0, 1.0)))
            bone.use_connect = False
            bone.use_local_location = True
            bone.inherit_scale = "FULL"
        for name, parent in parent_names.items():
            if parent:
                data.edit_bones[name].parent = data.edit_bones[parent]
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    for name, bind in bind_matrices.items():
        data.bones[name]["nms_bind_matrix"] = matrix_to_flat(bind)
    armature["nms_rig_baseline"] = "inverse_bind_v1"
    armature["nms_family_id"] = family["familyId"]
    return armature


def rebind_meshes(armature):
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    armature_world = armature.matrix_world.copy()
    armature.parent = None
    armature.matrix_world = armature_world
    for obj in meshes:
        ancestry = []
        current = obj
        while current is not None:
            ancestry.append(current.name.split(".")[0])
            current = current.parent
        obj["nms_source_ancestry"] = json.dumps(list(reversed(ancestry)))
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        for modifier in list(obj.modifiers):
            if modifier.type == "ARMATURE":
                obj.modifiers.remove(modifier)
        modifier = obj.modifiers.new("InverseBindArmature", "ARMATURE")
        modifier.object = armature
        obj.hide_render = False
        obj.hide_set(False)
    return meshes
