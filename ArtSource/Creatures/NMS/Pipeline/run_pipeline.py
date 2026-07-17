"""Public entry point for the NMS creature Blender review pipeline."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import bpy
from mathutils import Matrix

from descriptors import library_stage
from extraction import extraction_stage
from nms_creature_pipeline import load_configuration
from review import review_stage
from stages import actions_stage, baseline_stage
from validation import validation_stage
from unity_publish import unity_publish_stage


def load_baseline(family):
    path = family["outputRoot"] / f"{family['familyId']}Baseline.blend"
    bpy.ops.wm.open_mainfile(filepath=str(path))
    armature = next(
        obj for obj in bpy.context.scene.objects
        if obj.type == "ARMATURE" and obj.get("nms_rig_baseline") == "inverse_bind_v1"
    )
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    manifest_path = family["outputRoot"] / "Manifests" / f"{family['familyId']}_skeleton.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    bind_matrices = {
        record["name"]: Matrix([
            record["bindMatrix"][row:row + 4] for row in range(0, 16, 4)
        ])
        for record in manifest["joints"]
    }
    return armature, meshes, bind_matrices, manifest


def run_family(
    family_id,
    stages=("baseline", "actions", "library", "validate", "review"),
    approval=None,
):
    family = load_configuration(family_id)
    stage_set = set(stages)
    result = {"familyId": family_id, "pipelineVersion": family["pipelineVersion"]}
    if "unity_publish" in stage_set:
        result["unityPublish"] = unity_publish_stage(family, approval)
        if stage_set == {"unity_publish"}:
            print("NMS_CREATURE_PIPELINE_RESULT", json.dumps(result, ensure_ascii=False))
            return result
    if "extract" in stage_set:
        extraction_path, extraction = extraction_stage(family)
        result["extraction"] = {
            "path": extraction_path,
            "fileCount": extraction["fileCount"],
            "warnings": extraction["warnings"],
        }
    active_build_stages = stage_set.intersection(
        {"baseline", "actions", "library", "validate", "review"}
    )
    if not active_build_stages:
        print("NMS_CREATURE_PIPELINE_RESULT", json.dumps(result, ensure_ascii=False))
        return result
    if "baseline" in stage_set:
        armature, meshes, bind_matrices, records = baseline_stage(family)
    else:
        armature, meshes, bind_matrices, manifest = load_baseline(family)
        records = manifest["joints"]
    if "actions" in stage_set:
        animations, action_records = actions_stage(
            family, armature, meshes, bind_matrices, records
        )
        result["actions"] = action_records
    else:
        manifest = json.loads(
            (family["outputRoot"] / "Manifests" / f"{family_id}_skeleton.json").read_text()
        )
        action_records = manifest["availableActions"]
    if "library" in stage_set:
        result["library"] = library_stage(family, armature, meshes, action_records)
    if "validate" in stage_set:
        armature, meshes, _, _ = load_baseline(family)
        try:
            report_path, report = validation_stage(
                family, armature, meshes, action_records
            )
        except RuntimeError:
            failed_path = (
                family["outputRoot"]
                / "Validation"
                / f"{family_id}_validation.json"
            )
            if "library" not in stage_set or not failed_path.is_file():
                raise
            failed_report = json.loads(
                failed_path.read_text(encoding="utf-8")
            )
            quarantined = (
                failed_report.get("moduleValidation", {})
                .get("quarantinedModules", [])
            )
            if not quarantined:
                raise
            armature, meshes, _, _ = load_baseline(family)
            result["library"] = library_stage(
                family, armature, meshes, action_records
            )
            armature, meshes, _, _ = load_baseline(family)
            report_path, report = validation_stage(
                family, armature, meshes, action_records
            )
            result["validationRetry"] = {
                "quarantinedModules": quarantined
            }
        digest = hashlib.sha256(Path(report_path).read_bytes()).hexdigest()
        result["validation"] = {"path": report_path, "passed": report["passed"]}
        result["validationHash"] = digest
    if "review" in stage_set:
        result["reviewImages"] = review_stage(family, action_records)
    print("NMS_CREATURE_PIPELINE_RESULT", json.dumps(result, ensure_ascii=False))
    return result
