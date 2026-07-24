"""Batch review and approval gate for the remaining ground creature families."""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

_PIPELINE_ROOT = Path(__file__).resolve().parent
if str(_PIPELINE_ROOT) not in sys.path:
    sys.path.insert(0, str(_PIPELINE_ROOT))

from nms_creature_pipeline import load_configuration
from review import ensure_camera_and_lights, scene_bounds
from run_pipeline import run_family
from unity_publish import _write_material_manifest, sha256_file, unity_publish_stage


BATCH_ID = "RemainingLandCreatures"
CANDIDATES = (
    "ArthropodGround",
    "ArthropodGrub",
    "ArthropodQueen",
    "ArthropodBugFiend",
    "BeetleGround",
    "BlobGround",
    "SlugGround",
    "WeirdDigger",
    "WeirdGroundCreature",
    "WeirdRoller",
    "WeirdDiggerVariant",
    "WeirdRigGround",
)
ISOLATED = (
    {
        "source": "models/planets/creatures/weird/plow.scene.mbin",
        "reason": "only native idle exists; no legal ground walk action",
    },
    {
        "source": "models/planets/creatures/weird/diggermolesolo.scene.mbin",
        "reason": "no verified compatible native ground walk action",
    },
    {
        "source": "models/planets/creatures/weird/rollercreature.scene.mbin",
        "reason": "Joint index exceeds the source Geometry JointBindings array; inverse-bind skeleton cannot be reconstructed safely",
    },
    {
        "source": "models/planets/creatures/weird/weirddigger.scene.mbin",
        "reason": "Joint index exceeds the source Geometry JointBindings array; inverse-bind skeleton cannot be reconstructed safely",
    },
    {
        "source": "models/planets/creatures/weird/weirdrigground.scene.mbin",
        "reason": "source Geometry contains no JointBindings; no valid skinned ground rig is available",
    },
)


def _json(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def _locomotion_profile_hash(family, skeleton_manifest):
    digest = hashlib.sha256()
    digest.update(family["locomotionType"].encode("utf-8"))
    digest.update(str(bool(family["supportsRun"])).encode("ascii"))
    for key in ("idle", "walk", "run"):
        action = skeleton_manifest.get("availableActions", {}).get(key)
        if action is None:
            continue
        source = Path(action["source"])
        digest.update(key.encode("ascii"))
        digest.update(str(action["frames"]).encode("ascii"))
        digest.update(sha256_file(source).encode("ascii"))
    return digest.hexdigest()


def _material_review_manifest(family):
    library = family["outputRoot"] / (
        f"{family['familyId']}SpeciesLibrary.blend")
    bpy.ops.wm.open_mainfile(filepath=str(library))
    manifest = _json(
        family["outputRoot"] / "Manifests"
        / f"{family['familyId']}_family.json")
    destination = family["outputRoot"] / "Reviews" / "Materials"
    destination.mkdir(parents=True, exist_ok=True)
    return _write_material_manifest(family, destination, manifest)


def run_candidate(family_id):
    if family_id not in CANDIDATES:
        raise KeyError(f"{family_id} is not in {BATCH_ID}")
    result = run_family(
        family_id,
        stages=("extract", "baseline", "actions", "library", "validate", "review"),
    )
    family = load_configuration(family_id)
    result["materialManifest"] = str(_material_review_manifest(family))
    return result


def _append_showcase(family_id, grid_index):
    family = load_configuration(family_id)
    showcase = family["outputRoot"] / (
        f"{family_id}SpeciesShowcase.blend")
    with bpy.data.libraries.load(str(showcase), link=False) as (source, target):
        if "SPECIES_SHOWCASE" not in source.collections:
            raise RuntimeError(
                f"{family_id}: showcase collection is missing")
        target.collections = ["SPECIES_SHOWCASE"]
    collection = target.collections[0]
    collection.name = f"{family_id}_SHOWCASE"
    bpy.context.scene.collection.children.link(collection)

    objects = set(collection.all_objects)
    root = bpy.data.objects.new(f"{family_id}_ReviewRoot", None)
    bpy.context.scene.collection.objects.link(root)
    for obj in objects:
        if obj.parent not in objects:
            world = obj.matrix_world.copy()
            obj.parent = root
            obj.matrix_world = world
    column = grid_index % 3
    row = grid_index // 3
    root.location = Vector((column * 32.0, row * 24.0, 0.0))

    label_curve = bpy.data.curves.new(f"{family_id}_Label", "FONT")
    label_curve.body = family_id
    label_curve.align_x = "CENTER"
    label_curve.size = 1.4
    label = bpy.data.objects.new(f"{family_id}_Label", label_curve)
    bpy.context.scene.collection.objects.link(label)
    label.location = root.location + Vector((0.0, -8.0, 7.0))
    return showcase


def _clear_review_scene():
    scene = bpy.context.scene
    for obj in list(scene.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    if scene.world is None:
        scene.world = bpy.data.worlds.new(
            f"{BATCH_ID}_ReviewWorld")


def finalize_batch_review():
    root = load_configuration(CANDIDATES[0])["outputRoot"].parent
    passed = []
    groups = {}
    artifact_paths = []
    for family_id in CANDIDATES:
        family = load_configuration(family_id)
        validation_path = (
            family["outputRoot"] / "Validation"
            / f"{family_id}_validation.json")
        skeleton_path = (
            family["outputRoot"] / "Manifests"
            / f"{family_id}_skeleton.json")
        family_path = (
            family["outputRoot"] / "Manifests"
            / f"{family_id}_family.json")
        material_path = (
            family["outputRoot"] / "Reviews" / "Materials"
            / f"{family_id}_materials.json")
        showcase_path = (
            family["outputRoot"] / f"{family_id}SpeciesShowcase.blend")
        required = (
            validation_path,
            skeleton_path,
            family_path,
            material_path,
            showcase_path,
        )
        if not all(path.is_file() for path in required):
            continue
        validation = _json(validation_path)
        if not validation.get("passed"):
            continue
        skeleton = _json(skeleton_path)
        locomotion_hash = _locomotion_profile_hash(family, skeleton)
        group_key = skeleton["skeletonHash"] + ":" + locomotion_hash
        groups.setdefault(group_key, []).append(family_id)
        entry = {
            "familyId": family_id,
            "skeletonHash": skeleton["skeletonHash"],
            "locomotionProfileHash": locomotion_hash,
            "validationHash": sha256_file(validation_path),
            "supportsRun": bool(family["supportsRun"]),
        }
        passed.append(entry)
        artifact_paths.extend(required)
        review_dir = family["outputRoot"] / "Reviews" / family_id
        artifact_paths.extend(sorted(review_dir.glob("*.png")))

    if not passed:
        raise RuntimeError("No validated remaining-land families are available")

    _clear_review_scene()
    for index, item in enumerate(passed):
        artifact_paths.append(_append_showcase(item["familyId"], index))
    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.hide_render
    ]
    minimum, maximum = scene_bounds(meshes)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    ensure_camera_and_lights(center, size)
    bpy.context.scene.world.color = (0.035, 0.045, 0.06)
    bpy.context.scene.render.engine = "BLENDER_EEVEE"
    review_blend = root / "RemainingLandCreaturesMaterialReview.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(review_blend))
    artifact_paths.append(review_blend)

    artifacts = [
        {
            "path": str(path),
            "sha256": sha256_file(path),
        }
        for path in sorted(
            {Path(path).resolve() for path in artifact_paths},
            key=lambda value: str(value).casefold(),
        )
    ]
    hash_payload = {
        "batchId": BATCH_ID,
        "families": passed,
        "groups": groups,
        "isolated": ISOLATED,
        "artifacts": artifacts,
    }
    aggregate_hash = hashlib.sha256(
        json.dumps(
            hash_payload,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
    ).hexdigest()
    report = dict(hash_payload)
    report["aggregateHash"] = aggregate_hash
    report_path = root / f"{BATCH_ID}_aggregate_review.json"
    report_path.write_text(
        json.dumps(report, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print("NMS_REMAINING_LAND_REVIEW", json.dumps({
        "blend": str(review_blend),
        "report": str(report_path),
        "familyCount": len(passed),
        "aggregateHash": aggregate_hash,
    }, ensure_ascii=False))
    return report


def publish_approved_batch(approval):
    report_path = (
        load_configuration(CANDIDATES[0])["outputRoot"].parent
        / f"{BATCH_ID}_aggregate_review.json")
    report = _json(report_path)
    if approval != report.get("aggregateHash"):
        raise RuntimeError("Approval does not match the aggregate review hash")
    results = {}
    for entry in report["families"]:
        family = load_configuration(entry["familyId"])
        results[entry["familyId"]] = unity_publish_stage(family, approval)
    return results


def _main(argv):
    if not argv:
        raise SystemExit(
            "Usage: remaining_land_batch.py -- "
            "review [familyId ...] | finalize | publish <aggregateHash>")
    command = argv[0].lower()
    if command == "review":
        family_ids = argv[1:] or list(CANDIDATES)
        for family_id in family_ids:
            run_candidate(family_id)
        return
    if command == "finalize":
        finalize_batch_review()
        return
    if command == "publish" and len(argv) == 2:
        publish_approved_batch(argv[1])
        return
    raise SystemExit(f"Unsupported remaining-land batch command: {' '.join(argv)}")


if __name__ == "__main__":
    separator = sys.argv.index("--") + 1 if "--" in sys.argv else len(sys.argv)
    _main(sys.argv[separator:])
