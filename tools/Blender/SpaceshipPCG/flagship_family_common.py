"""Shared export, validation, and review helpers for the flagship family."""

from __future__ import annotations

import math
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

import bpy
from mathutils import Vector

import generate_flagship_v2 as base
from flagship_contract import HULL_BUDGETS, MATERIAL_NAMES, MODULE_SPECS


LoadoutEntry = tuple[
    str,
    tuple[float, float, float],
    tuple[float, float, float],
    float,
    float,
]
HullBuilder = Callable[
    [bpy.types.Collection, "HullSpec"],
    tuple[list[bpy.types.Object], list[bpy.types.Object]],
]


@dataclass(frozen=True)
class HullSpec:
    key: str
    asset_id: str
    display_name: str
    seed: str
    dimensions: tuple[float, float, float]
    silhouette: str
    builder: HullBuilder
    balanced_loadout: tuple[LoadoutEntry, ...]
    asymmetric_loadout: tuple[LoadoutEntry, ...]


def tag_source(
    obj: bpy.types.Object,
    spec: HullSpec,
    role: str,
    group: str,
) -> bpy.types.Object:
    obj["asset_id"] = spec.asset_id
    obj["source_role"] = role
    obj["source_group"] = group
    obj["forward_axis"] = "+Y"
    obj["unity_forward_axis"] = "+Z"
    obj["fixed_hardpoint"] = False
    return obj


def hide_sources(
    parts: list[bpy.types.Object],
    collection: bpy.types.Collection,
) -> None:
    for obj in parts:
        obj.hide_set(True)
        obj.hide_render = True
    collection.hide_render = True


def _join_placement(
    placement_parts: list[bpy.types.Object],
    collection: bpy.types.Collection,
) -> bpy.types.Object:
    clones = [
        base.duplicate_object(
            obj,
            f"PLACEMENT_{obj.name.removeprefix('SRC_')}",
            collection,
        )
        for obj in placement_parts
    ]
    if len(clones) == 1:
        placement = clones[0]
        placement.name = "PlacementSurface"
        placement.data.name = "PlacementSurface"
    else:
        placement = base.pipeline.join_geometry(clones, "PlacementSurface")
        base.move_to_collection(placement, collection)
    base.pipeline.apply_hard_surface_shading(placement)
    base.pipeline.cube_uv(placement)
    base.pipeline.decimate_to(
        placement,
        HULL_BUDGETS["placementTriangles"],
    )
    placement["placement_surface"] = True
    placement["fixed_hardpoints"] = False
    placement["surface_grid_hint"] = 0.25
    return placement


def build_hull_export(
    spec: HullSpec,
    source_parts: list[bpy.types.Object],
    placement_parts: list[bpy.types.Object],
    output_root: Path,
) -> tuple[dict, list[bpy.types.Object]]:
    collection = base.ensure_collection("90_Export_Contract")
    clones = [
        base.duplicate_object(
            obj,
            f"EXPORT_{obj.name.removeprefix('SRC_')}",
            collection,
        )
        for obj in source_parts
    ]
    lod0 = base.pipeline.join_geometry(clones, "Render_LOD0")
    base.move_to_collection(lod0, collection)
    base.pipeline.fit_hull_bounds(lod0, spec.dimensions)
    base.pipeline.cube_uv(lod0)
    base.pipeline.decimate_to(lod0, HULL_BUDGETS["lod0Triangles"])

    lod0_count = base.pipeline.triangle_count(lod0)
    lod1 = base.pipeline.duplicate_mesh(lod0, "Render_LOD1")
    base.move_to_collection(lod1, collection)
    base.pipeline.decimate_to(
        lod1,
        min(
            HULL_BUDGETS["lod1Triangles"],
            max(16000, int(lod0_count * 0.46)),
        ),
    )
    lod2 = base.pipeline.duplicate_mesh(lod0, "Render_LOD2")
    base.move_to_collection(lod2, collection)
    base.pipeline.decimate_to(
        lod2,
        min(
            HULL_BUDGETS["lod2Triangles"],
            max(6000, int(lod0_count * 0.115)),
        ),
    )
    collision = base.pipeline.convex_collision(
        lod2,
        HULL_BUDGETS["collisionTriangles"],
    )
    base.move_to_collection(collision, collection)
    placement = _join_placement(placement_parts, collection)

    validation_budgets = {
        key: HULL_BUDGETS[key]
        for key in (
            "lod0Triangles",
            "lod1Triangles",
            "lod2Triangles",
            "collisionTriangles",
            "dimensionTolerance",
        )
    }
    errors, counts, actual = base.pipeline.validate_mesh_set(
        lod0,
        lod1,
        lod2,
        collision,
        spec.dimensions,
        validation_budgets,
    )
    if counts["lod0"] < HULL_BUDGETS["minimumLod0Triangles"]:
        errors.append(
            f"LOD0 triangles {counts['lod0']} are below "
            f"{HULL_BUDGETS['minimumLod0Triangles']} high-detail target"
        )
    if not counts["lod0"] > counts["lod1"] > counts["lod2"]:
        errors.append("LOD triangle counts are not strictly decreasing")
    if base.pipeline.triangle_count(placement) > HULL_BUDGETS[
        "placementTriangles"
    ]:
        errors.append("PlacementSurface exceeds triangle budget")
    for index, label in enumerate(("width", "height", "length")):
        if abs(actual[index] - spec.dimensions[index]) > 0.01:
            errors.append(
                f"{label} absolute error exceeds 0.01m: "
                f"{actual[index]:.4f} vs {spec.dimensions[index]:.4f}"
            )

    topology = base.strict_mesh_checks(lod0)
    placement_topology = base.strict_mesh_checks(placement)
    if topology["nonManifoldEdges"] or topology["degenerateFaces"]:
        errors.append(f"LOD0 topology check failed: {topology}")
    if (
        placement_topology["nonManifoldEdges"]
        or placement_topology["degenerateFaces"]
    ):
        errors.append(
            f"PlacementSurface topology check failed: {placement_topology}"
        )
    slots = [slot.material.name for slot in lod0.material_slots]
    if slots != list(MATERIAL_NAMES):
        errors.append("material slots do not match flagship contract")

    fbx_path = output_root / "Hulls" / f"{spec.asset_id}.fbx"
    preview_path = output_root / "Thumbnails" / f"{spec.asset_id}.png"
    export_objects = [lod0, lod1, lod2, collision, placement]
    base.pipeline.export_fbx(export_objects, fbx_path)

    render_states = {
        obj.name: obj.hide_render for obj in bpy.context.scene.objects
    }
    for obj in bpy.context.scene.objects:
        obj.hide_render = obj != lod0
    original_rotation = lod0.rotation_euler.copy()
    lod0.rotation_euler.z += math.pi
    base.pipeline.render_preview(lod0, preview_path, 640)
    lod0.rotation_euler = original_rotation
    for obj in bpy.context.scene.objects:
        if obj.name in render_states:
            obj.hide_render = render_states[obj.name]

    for obj in export_objects:
        obj.hide_set(True)
        obj.hide_render = True
    collection.hide_render = True
    return (
        {
            "fbx": str(fbx_path),
            "thumbnail": str(preview_path),
            "triangles": counts,
            "placementTriangles": base.pipeline.triangle_count(placement),
            "actualDimensions": actual,
            "topology": topology,
            "placementTopology": placement_topology,
            "geometryHash": base.pipeline.geometry_hash(export_objects),
            "materialSlots": slots,
            "errors": errors,
        },
        export_objects,
    )


def _place_module_library(
    module_sources: dict[str, list[bpy.types.Object]],
    collection: bpy.types.Collection,
) -> None:
    for index, spec in enumerate(MODULE_SPECS):
        column = index % 5
        row = index // 5
        x = -3.6 + column * 1.8
        y = 1.2 - row * 2.0
        base.place_module(
            spec.module_id,
            module_sources,
            collection,
            (x, y, 0.0),
            (0.0, 0.0, 1.0),
            0.0,
            0.88,
        )
        base.add_label(
            collection,
            spec.module_id,
            (x, y - 0.72, 0.02),
            0.20,
        )


def _add_review_environment(
    spec: HullSpec,
    environment: bpy.types.Collection,
) -> list[bpy.types.Object]:
    width, height, length = spec.dimensions
    target = Vector((0.0, 0.0, 0.0))
    visible: list[bpy.types.Object] = []
    floor_z = -height * 0.72 - 0.55
    floor = base.create_box(
        "ReviewFloor",
        (0.0, 0.0, floor_z),
        (max(11.0, width * 2.4), max(12.0, length * 2.0), 0.08),
        environment,
        1,
        0.012,
    )
    visible.append(floor)

    camera_data = bpy.data.cameras.new("ReviewCamera")
    camera = bpy.data.objects.new("ReviewCamera", camera_data)
    environment.objects.link(camera)
    camera.location = (
        max(7.5, width * 1.65),
        max(8.5, length * 1.35),
        max(5.2, height * 3.1),
    )
    camera.rotation_euler = (target - camera.location).to_track_quat(
        "-Z",
        "Y",
    ).to_euler()
    camera_data.lens = 58.0
    camera_data.clip_end = 1000.0

    audit_specs = (
        ("Camera_Top", (0.0, 0.0, max(10.0, width * 2.1))),
        ("Camera_Bottom", (0.0, 0.0, -max(10.0, width * 2.1))),
        ("Camera_Rear", (0.0, -max(10.0, length * 1.8), 0.3)),
        ("Camera_Front", (0.0, max(10.0, length * 1.8), 0.3)),
    )
    for name, location in audit_specs:
        data = bpy.data.cameras.new(name)
        audit = bpy.data.objects.new(name, data)
        environment.objects.link(audit)
        audit.location = location
        audit.rotation_euler = (target - audit.location).to_track_quat(
            "-Z",
            "Y",
        ).to_euler()
        data.lens = 58.0
        data.clip_end = 1000.0

    for name, location, energy, color, size in (
        (
            "ReviewKey",
            (width * 1.4, length * 0.85, height * 3.2 + 3.0),
            1500.0,
            (0.76, 0.87, 1.0),
            5.0,
        ),
        (
            "ReviewFill",
            (-width * 1.7, length * 0.2, height * 1.6 + 2.0),
            950.0,
            (0.32, 0.54, 1.0),
            4.0,
        ),
        (
            "ReviewRim",
            (width * 0.5, -length * 1.1, height * 2.0 + 2.0),
            1300.0,
            (1.0, 0.42, 0.18),
            3.0,
        ),
    ):
        data = bpy.data.lights.new(name, "AREA")
        light = bpy.data.objects.new(name, data)
        environment.objects.link(light)
        light.location = location
        light.rotation_euler = (target - light.location).to_track_quat(
            "-Z",
            "Y",
        ).to_euler()
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = size

    scene = bpy.context.scene
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.world = scene.world or bpy.data.worlds.new("FlagshipFamilyWorld")
    scene.world.color = (0.012, 0.018, 0.030)
    scene.view_settings.look = "AgX - Medium High Contrast"
    return visible


def build_review_scene(
    spec: HullSpec,
    hull_sources: list[bpy.types.Object],
    placement_parts: list[bpy.types.Object],
    module_sources: dict[str, list[bpy.types.Object]],
) -> None:
    hero_collection = base.ensure_collection("10_Hero_Unloaded_Hull")
    base.duplicate_group(hull_sources, "HERO", hero_collection)

    placement_collection = base.ensure_collection("15_Placement_Surface")
    placement_review = base.duplicate_group(
        placement_parts,
        "PLACEMENT",
        placement_collection,
    )
    for obj in placement_review:
        obj.display_type = "WIRE"
        obj.color = (0.05, 0.85, 1.0, 0.28)
        obj.show_in_front = True
        obj["placement_surface"] = True
        obj["fixed_hardpoints"] = False
    placement_collection.hide_viewport = True
    placement_collection.hide_render = True

    module_collection = base.ensure_collection("20_Module_Library")
    _place_module_library(module_sources, module_collection)
    module_collection.hide_viewport = True
    module_collection.hide_render = True

    balanced = base.ensure_collection("30_Balanced_Free_Build")
    base.duplicate_group(hull_sources, "BALANCED", balanced)
    for module_id, location, normal, twist, scale in spec.balanced_loadout:
        base.place_module(
            module_id,
            module_sources,
            balanced,
            location,
            normal,
            twist,
            scale,
        )
    balanced.hide_viewport = True
    balanced.hide_render = True

    asymmetric = base.ensure_collection("40_Asymmetric_Free_Build")
    base.duplicate_group(hull_sources, "ASYMMETRIC", asymmetric)
    for module_id, location, normal, twist, scale in spec.asymmetric_loadout:
        base.place_module(
            module_id,
            module_sources,
            asymmetric,
            location,
            normal,
            twist,
            scale,
        )
    asymmetric.hide_viewport = True
    asymmetric.hide_render = True

    environment = base.ensure_collection("99_Review_Environment")
    _add_review_environment(spec, environment)
    scene = bpy.context.scene
    scene.name = f"{spec.display_name} Review"
    scene["asset_id"] = spec.asset_id
    scene["review_mode"] = "unloaded_hull_default"
    scene["free_surface_placement"] = True
    scene["fixed_hardpoints"] = False


def add_gallery_environment(
    dimensions: tuple[float, float, float] = (20.0, 2.5, 10.0),
) -> None:
    environment = base.ensure_collection("99_Review_Environment")
    floor = base.create_box(
        "FleetReviewFloor",
        (0.0, 0.0, -2.0),
        (dimensions[0], dimensions[2], 0.08),
        environment,
        1,
        0.012,
    )
    floor.hide_render = False
    target = Vector((0.0, 0.0, 0.0))
    data = bpy.data.cameras.new("FleetReviewCamera")
    camera = bpy.data.objects.new("FleetReviewCamera", data)
    environment.objects.link(camera)
    camera.location = (18.0, 25.0, 18.0)
    camera.rotation_euler = (target - camera.location).to_track_quat(
        "-Z",
        "Y",
    ).to_euler()
    data.lens = 50.0
    data.clip_end = 1000.0
    for name, location, energy, color, size in (
        ("FleetKey", (7.0, 7.0, 11.0), 1900.0, (0.74, 0.86, 1.0), 6.0),
        ("FleetFill", (-9.0, 3.0, 6.0), 1300.0, (0.30, 0.52, 1.0), 5.0),
        ("FleetRim", (2.0, -9.0, 8.0), 1600.0, (1.0, 0.42, 0.18), 4.0),
    ):
        light_data = bpy.data.lights.new(name, "AREA")
        light = bpy.data.objects.new(name, light_data)
        environment.objects.link(light)
        light.location = location
        light.rotation_euler = (target - light.location).to_track_quat(
            "-Z",
            "Y",
        ).to_euler()
        light_data.energy = energy
        light_data.color = color
        light_data.shape = "DISK"
        light_data.size = size
    scene = bpy.context.scene
    scene.name = "Flagship Family V1 Review"
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.world = scene.world or bpy.data.worlds.new("FlagshipFamilyWorld")
    scene.world.color = (0.012, 0.018, 0.030)
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene["review_mode"] = "three_unloaded_hulls"
    scene["free_surface_placement"] = True
    scene["fixed_hardpoints"] = False
