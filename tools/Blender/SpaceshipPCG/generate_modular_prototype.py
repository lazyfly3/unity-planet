"""Generate the editable modular player-ship prototype and Blender review file.

This is intentionally separate from the production fleet publisher.  It reuses
the stable material, UV, LOD, collision, FBX, preview, and geometry-hash
contracts from generate_fleet.py, but writes only to Library until the design
has been reviewed.
"""

from __future__ import annotations

import argparse
import json
import math
import random
import sys
from dataclasses import asdict, dataclass
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parents[2]
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import generate_fleet as pipeline


PROTOTYPE_ID = "modular_ship_v1"
DIMENSIONS = (3.0, 2.2, 6.0)
MATERIAL_NAMES = ("HullPrimary", "HullDark", "HullEmission", "Accent")
DEFAULT_OUTPUT_ROOT = (
    PROJECT_ROOT / "Library" / "SpaceshipPCGStaging" / PROTOTYPE_ID
)
DEFAULT_REVIEW_FILE = (
    PROJECT_ROOT / "Library" / "SpaceshipPCGReview" / f"{PROTOTYPE_ID}_review.blend"
)


@dataclass(frozen=True)
class PrototypeParameters:
    seed: str = "modular-ship-v1"
    cabin_length: float = 1.75
    armor_density: float = 0.62
    wing_type: str = "swept-industrial"
    engine_cowl_type: str = "split-cage"
    asymmetry_strength: float = 0.12


@dataclass(frozen=True)
class SocketSpec:
    socket_id: str
    position: tuple[float, float, float]
    normal: tuple[float, float, float]
    connector_type: str
    connector_size: str
    mirror_id: str = ""


SOCKETS = (
    SocketSpec(
        "SOCKET_WING_L",
        (-0.84, 0.18, 0.02),
        (-1.0, 0.0, 0.0),
        "wing",
        "M",
        "SOCKET_WING_R",
    ),
    SocketSpec(
        "SOCKET_WING_R",
        (0.84, 0.18, 0.02),
        (1.0, 0.0, 0.0),
        "wing",
        "M",
        "SOCKET_WING_L",
    ),
    SocketSpec(
        "SOCKET_ENGINE_L",
        (-0.69, -1.58, 0.00),
        (0.0, -1.0, 0.0),
        "engine",
        "M",
        "SOCKET_ENGINE_R",
    ),
    SocketSpec(
        "SOCKET_ENGINE_R",
        (0.69, -1.58, 0.00),
        (0.0, -1.0, 0.0),
        "engine",
        "M",
        "SOCKET_ENGINE_L",
    ),
    SocketSpec(
        "SOCKET_HARDPOINT_FORE_L",
        (-0.48, 0.55, -0.84),
        (0.0, 0.0, -1.0),
        "hardpoint",
        "S1",
        "SOCKET_HARDPOINT_FORE_R",
    ),
    SocketSpec(
        "SOCKET_HARDPOINT_FORE_R",
        (0.48, 0.55, -0.84),
        (0.0, 0.0, -1.0),
        "hardpoint",
        "S1",
        "SOCKET_HARDPOINT_FORE_L",
    ),
    SocketSpec(
        "SOCKET_HARDPOINT_AFT_L",
        (-0.48, -0.55, -0.84),
        (0.0, 0.0, -1.0),
        "hardpoint",
        "S2",
        "SOCKET_HARDPOINT_AFT_R",
    ),
    SocketSpec(
        "SOCKET_HARDPOINT_AFT_R",
        (0.48, -0.55, -0.84),
        (0.0, 0.0, -1.0),
        "hardpoint",
        "S2",
        "SOCKET_HARDPOINT_AFT_L",
    ),
    SocketSpec(
        "SOCKET_DORSAL",
        (0.0, -0.10, 0.98),
        (0.0, 0.0, 1.0),
        "utility",
        "M",
    ),
    SocketSpec(
        "SOCKET_VENTRAL",
        (0.0, -0.10, -0.90),
        (0.0, 0.0, -1.0),
        "utility",
        "M",
    ),
)


def parse_args() -> argparse.Namespace:
    argv = sys.argv
    argv = argv[argv.index("--") + 1 :] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-root", default=str(DEFAULT_OUTPUT_ROOT))
    parser.add_argument("--review-file", default=str(DEFAULT_REVIEW_FILE))
    parser.add_argument("--report", default="")
    parser.add_argument("--seed", default=PrototypeParameters.seed)
    return parser.parse_args(argv)


def ensure_collection(
    name: str,
    parent: bpy.types.Collection | None = None,
) -> bpy.types.Collection:
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
    target = parent or bpy.context.scene.collection
    if collection.name not in {child.name for child in target.children}:
        target.children.link(collection)
    return collection


def move_to_collection(
    obj: bpy.types.Object,
    collection: bpy.types.Collection,
) -> None:
    for owner in tuple(obj.users_collection):
        owner.objects.unlink(obj)
    collection.objects.link(obj)


def assign_palette(obj: bpy.types.Object, material_index: int) -> None:
    obj.data.materials.clear()
    for material in pipeline.stable_materials():
        obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.material_index = material_index


def finish_hard_surface(
    obj: bpy.types.Object,
    collection: bpy.types.Collection,
    material_index: int,
    bevel: float,
    bevel_segments: int = 3,
) -> bpy.types.Object:
    move_to_collection(obj, collection)
    assign_palette(obj, material_index)
    if bevel > 0.0:
        modifier = obj.modifiers.new("PrototypeBevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = bevel_segments
        modifier.profile = 0.42
        if hasattr(modifier, "harden_normals"):
            modifier.harden_normals = True
    pipeline.apply_hard_surface_shading(obj)
    pipeline.cube_uv(obj)
    return obj


def create_box(
    name: str,
    center: tuple[float, float, float],
    size: tuple[float, float, float],
    collection: bpy.types.Collection,
    material_index: int = 0,
    bevel: float = 0.035,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=center)
    obj = bpy.context.object
    obj.name = name
    obj.scale = (size[0] * 0.5, size[1] * 0.5, size[2] * 0.5)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_hard_surface(obj, collection, material_index, bevel)


def create_cylinder(
    name: str,
    center: tuple[float, float, float],
    radius: float,
    depth: float,
    collection: bpy.types.Collection,
    material_index: int = 0,
    vertices: int = 24,
    along_y: bool = True,
    bevel: float = 0.025,
) -> bpy.types.Object:
    rotation = (math.pi * 0.5, 0.0, 0.0) if along_y else (0.0, 0.0, 0.0)
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        location=center,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    return finish_hard_surface(obj, collection, material_index, bevel, 2)


def create_cone(
    name: str,
    center: tuple[float, float, float],
    radius_front: float,
    radius_back: float,
    depth: float,
    collection: bpy.types.Collection,
    material_index: int = 0,
    vertices: int = 32,
    along_y: bool = True,
    bevel: float = 0.018,
) -> bpy.types.Object:
    rotation = (math.pi * 0.5, 0.0, 0.0) if along_y else (0.0, 0.0, 0.0)
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius_front,
        radius2=radius_back,
        depth=depth,
        location=center,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    return finish_hard_surface(obj, collection, material_index, bevel, 2)


def create_torus(
    name: str,
    center: tuple[float, float, float],
    major_radius: float,
    minor_radius: float,
    collection: bpy.types.Collection,
    material_index: int = 1,
    major_segments: int = 32,
    minor_segments: int = 10,
    along_y: bool = True,
) -> bpy.types.Object:
    rotation = (math.pi * 0.5, 0.0, 0.0) if along_y else (0.0, 0.0, 0.0)
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=major_segments,
        minor_segments=minor_segments,
        location=center,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    return finish_hard_surface(obj, collection, material_index, 0.0, 1)


def create_strut(
    name: str,
    start: tuple[float, float, float],
    end: tuple[float, float, float],
    thickness: float,
    collection: bpy.types.Collection,
    material_index: int = 1,
) -> bpy.types.Object:
    start_vector = Vector(start)
    end_vector = Vector(end)
    direction = end_vector - start_vector
    obj = create_box(
        name,
        tuple((start_vector + end_vector) * 0.5),
        (thickness, direction.length, thickness),
        collection,
        material_index,
        min(0.018, thickness * 0.22),
    )
    obj.rotation_euler = Vector((0.0, 1.0, 0.0)).rotation_difference(
        direction.normalized()
    ).to_euler()
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.select_set(False)
    return obj


def chamfered_ring(
    half_width: float,
    bottom: float,
    top: float,
    chamfer: float,
) -> list[tuple[float, float]]:
    width = max(0.02, half_width)
    height = max(0.02, top - bottom)
    cut = min(max(0.005, chamfer), width * 0.48, height * 0.48)
    return [
        (-width + cut, bottom),
        (width - cut, bottom),
        (width, bottom + cut),
        (width, top - cut),
        (width - cut, top),
        (-width + cut, top),
        (-width, top - cut),
        (-width, bottom + cut),
    ]


def create_loft(
    name: str,
    stations: list[tuple[float, float, float, float, float]],
    collection: bpy.types.Collection,
    material_index: int = 0,
    bevel: float = 0.025,
) -> bpy.types.Object:
    vertices: list[tuple[float, float, float]] = []
    for y, half_width, bottom, top, chamfer in stations:
        vertices.extend(
            (x, y, z)
            for x, z in chamfered_ring(half_width, bottom, top, chamfer)
        )

    ring_size = 8
    faces: list[tuple[int, ...]] = []
    faces.append(tuple(reversed(range(ring_size))))
    for station_index in range(len(stations) - 1):
        first = station_index * ring_size
        second = (station_index + 1) * ring_size
        for index in range(ring_size):
            following = (index + 1) % ring_size
            faces.append(
                (
                    first + index,
                    first + following,
                    second + following,
                    second + index,
                )
            )
    last = (len(stations) - 1) * ring_size
    faces.append(tuple(last + index for index in range(ring_size)))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    return finish_hard_surface(obj, collection, material_index, bevel)


def create_prism(
    name: str,
    points_xy: list[tuple[float, float]],
    z_min: float,
    z_max: float,
    collection: bpy.types.Collection,
    material_index: int = 0,
    bevel: float = 0.022,
) -> bpy.types.Object:
    count = len(points_xy)
    vertices = [(x, y, z_min) for x, y in points_xy]
    vertices += [(x, y, z_max) for x, y in points_xy]
    faces: list[tuple[int, ...]] = [
        tuple(reversed(range(count))),
        tuple(count + index for index in range(count)),
    ]
    for index in range(count):
        following = (index + 1) % count
        faces.append((index, following, count + following, count + index))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    return finish_hard_surface(obj, collection, material_index, bevel)


def mirror_points_x(
    points: list[tuple[float, float]],
) -> list[tuple[float, float]]:
    return [(-x, y) for x, y in reversed(points)]


def add_source_part(
    target: dict[str, bpy.types.Object],
    obj: bpy.types.Object,
    role: str,
    module_group: str,
) -> None:
    obj["prototype_role"] = role
    obj["module_group"] = module_group
    obj["forward_axis"] = "+Y"
    obj["unity_forward_axis"] = "+Z"
    target[obj.name] = obj


def build_source_modules(
    parameters: PrototypeParameters,
    collection: bpy.types.Collection,
) -> tuple[dict[str, bpy.types.Object], dict[str, list[str]]]:
    rng = random.Random(parameters.seed)
    parts: dict[str, bpy.types.Object] = {}
    groups: dict[str, list[str]] = {
        "base": [],
        "bare": [],
        "explorer": [],
        "combat": [],
    }

    def register(
        obj: bpy.types.Object,
        role: str,
        module_group: str,
        configuration_groups: tuple[str, ...],
    ) -> None:
        add_source_part(parts, obj, role, module_group)
        for group in configuration_groups:
            groups[group].append(obj.name)

    register(
        create_loft(
            "SRC_CockpitShell",
            [
                (3.00, 0.05, -0.12, 0.16, 0.018),
                (2.72, 0.34, -0.34, 0.38, 0.10),
                (2.18, 0.57, -0.48, 0.61, 0.13),
                (1.28, 0.70, -0.56, 0.72, 0.15),
            ],
            collection,
            0,
            0.028,
        ),
        "cockpit",
        "skeleton",
        ("base",),
    )
    register(
        create_loft(
            "SRC_CockpitGlazing",
            [
                (2.57, 0.25, 0.30, 0.43, 0.07),
                (2.14, 0.45, 0.38, 0.64, 0.10),
                (1.58, 0.50, 0.43, 0.70, 0.11),
            ],
            collection,
            1,
            0.014,
        ),
        "cockpit_glazing",
        "skeleton",
        ("base",),
    )
    register(
        create_loft(
            "SRC_CorePressureHull",
            [
                (1.42, 0.69, -0.59, 0.73, 0.15),
                (0.72, 0.81, -0.70, 0.81, 0.17),
                (-0.45, 0.79, -0.72, 0.79, 0.17),
                (-1.28, 0.66, -0.62, 0.68, 0.14),
            ],
            collection,
            0,
            0.035,
        ),
        "core_hull",
        "skeleton",
        ("base",),
    )
    register(
        create_box(
            "SRC_DorsalServiceSpine",
            (0.0, -0.05, 0.79),
            (0.34, 2.92, 0.22),
            collection,
            1,
            0.035,
        ),
        "service_spine",
        "skeleton",
        ("base",),
    )
    register(
        create_box(
            "SRC_VentralKeel",
            (0.0, -0.12, -0.73),
            (0.28, 2.72, 0.20),
            collection,
            1,
            0.028,
        ),
        "keel",
        "skeleton",
        ("base",),
    )
    register(
        create_loft(
            "SRC_RearPowerBulkhead",
            [
                (-1.18, 0.64, -0.60, 0.66, 0.13),
                (-1.72, 0.72, -0.57, 0.60, 0.14),
                (-2.05, 0.70, -0.50, 0.53, 0.13),
            ],
            collection,
            1,
            0.032,
        ),
        "rear_bulkhead",
        "skeleton",
        ("base",),
    )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        register(
            create_strut(
                f"SRC_RearFrameRail_{label}",
                (side * 0.55, -1.62, -0.42),
                (side * 0.72, -2.72, -0.34),
                0.10,
                collection,
                3,
            ),
            "rear_frame",
            "skeleton",
            ("base",),
        )
        register(
            create_strut(
                f"SRC_RearFrameUpper_{label}",
                (side * 0.49, -1.58, 0.43),
                (side * 0.69, -2.67, 0.32),
                0.09,
                collection,
                3,
            ),
            "rear_frame",
            "skeleton",
            ("base",),
        )

    for y in (-1.70, -2.28):
        register(
            create_box(
                f"SRC_RearCrossBrace_{abs(int(y * 100)):03d}",
                (0.0, y, 0.0),
                (1.42, 0.10, 0.10),
                collection,
                3,
                0.014,
            ),
            "rear_frame",
            "skeleton",
            ("base",),
        )

    # Cockpit framing and hull surface language.  These are separate editable
    # objects so the canopy, armor lips, vents, and maintenance covers can be
    # replaced without rebuilding the pressure hull.
    for name, start, end, thickness in (
        (
            "SRC_CanopyCenterRib",
            (0.0, 2.58, 0.445),
            (0.0, 1.50, 0.735),
            0.040,
        ),
        (
            "SRC_CanopyBowRib",
            (-0.25, 2.55, 0.435),
            (0.25, 2.55, 0.435),
            0.034,
        ),
        (
            "SRC_CanopyMidRib",
            (-0.47, 2.08, 0.655),
            (0.47, 2.08, 0.655),
            0.036,
        ),
        (
            "SRC_CanopyRearRib",
            (-0.50, 1.55, 0.715),
            (0.50, 1.55, 0.715),
            0.044,
        ),
        (
            "SRC_CanopyRail_L",
            (-0.27, 2.52, 0.430),
            (-0.51, 1.54, 0.695),
            0.032,
        ),
        (
            "SRC_CanopyRail_R",
            (0.27, 2.52, 0.430),
            (0.51, 1.54, 0.695),
            0.032,
        ),
    ):
        register(
            create_strut(
                name,
                start,
                end,
                thickness,
                collection,
                3,
            ),
            "cockpit_frame",
            "skeleton",
            ("base",),
        )

    register(
        create_loft(
            "SRC_CockpitChinArmor",
            [
                (2.67, 0.27, -0.39, -0.33, 0.035),
                (2.16, 0.49, -0.53, -0.45, 0.050),
                (1.48, 0.58, -0.61, -0.51, 0.060),
            ],
            collection,
            1,
            0.016,
        ),
        "cockpit_armor",
        "skeleton",
        ("base",),
    )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        register(
            create_box(
                f"SRC_CoreSideArmor_{label}",
                (side * 0.805, 0.18, 0.04),
                (0.055, 1.24, 0.74),
                collection,
                1,
                0.018,
            ),
            "side_armor",
            "skeleton",
            ("base",),
        )
        register(
            create_box(
                f"SRC_CoreAccessPanel_{label}",
                (side * 0.838, 0.38, 0.12),
                (0.025, 0.49, 0.38),
                collection,
                3,
                0.010,
            ),
            "maintenance_panel",
            "skeleton",
            ("base",),
        )
        for vent_index, y in enumerate((1.43, 1.30, 1.17)):
            register(
                create_box(
                    f"SRC_CheekVent_{label}_{vent_index:02d}",
                    (side * 0.625, y, 0.12),
                    (0.045, 0.19, 0.075),
                    collection,
                    1,
                    0.007,
                ),
                "cockpit_vent",
                "skeleton",
                ("base",),
            )

    for index, y in enumerate((0.78, 0.16, -0.48, -1.02)):
        register(
            create_box(
                f"SRC_SpineClamp_{index:02d}",
                (0.0, y, 0.902),
                (0.50, 0.12, 0.08),
                collection,
                3 if index in (0, 3) else 1,
                0.014,
            ),
            "spine_detail",
            "skeleton",
            ("base",),
        )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        for node_index, (y, z) in enumerate(
            ((-1.68, -0.42), (-1.68, 0.43), (-2.28, -0.37), (-2.28, 0.37))
        ):
            register(
                create_cylinder(
                    f"SRC_FrameNode_{label}_{node_index:02d}",
                    (side * (0.57 if node_index < 2 else 0.66), y, z),
                    0.085,
                    0.13,
                    collection,
                    3,
                    16,
                    False,
                    0.010,
                ),
                "frame_node",
                "skeleton",
                ("base",),
            )
        register(
            create_strut(
                f"SRC_CoolantPipeUpper_{label}",
                (side * 0.44, -1.28, 0.29),
                (side * 0.58, -2.56, 0.19),
                0.045,
                collection,
                2,
            ),
            "coolant_line",
            "skeleton",
            ("base",),
        )
        register(
            create_strut(
                f"SRC_CoolantPipeLower_{label}",
                (side * 0.41, -1.31, -0.28),
                (side * 0.57, -2.52, -0.18),
                0.038,
                collection,
                3,
            ),
            "coolant_line",
            "skeleton",
            ("base",),
        )

    # The sealed core hull is independently flight-capable.  External engine
    # pods attach to the reinforced collars below as booster/long-range drive
    # modules; removing them never leaves the ship without propulsion.
    register(
        create_loft(
            "SRC_IntegratedCruiseDrive",
            [
                (-1.82, 0.34, -0.29, 0.31, 0.085),
                (-2.30, 0.31, -0.28, 0.29, 0.080),
                (-2.78, 0.24, -0.22, 0.23, 0.065),
                (-3.00, 0.18, -0.17, 0.18, 0.050),
            ],
            collection,
            1,
            0.024,
        ),
        "integrated_drive",
        "skeleton",
        ("base",),
    )
    register(
        create_torus(
            "SRC_IntegratedDriveNozzleRing",
            (0.0, -2.965, 0.0),
            0.145,
            0.025,
            collection,
            3,
            28,
            10,
            True,
        ),
        "integrated_drive",
        "skeleton",
        ("base",),
    )
    register(
        create_cylinder(
            "SRC_IntegratedDriveGlow",
            (0.0, -2.987, 0.0),
            0.118,
            0.024,
            collection,
            2,
            28,
            True,
            0.0,
        ),
        "integrated_drive",
        "skeleton",
        ("base",),
    )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        register(
            create_box(
                f"SRC_EngineMountBulkhead_{label}",
                (side * 0.69, -1.54, 0.0),
                (0.54, 0.18, 0.62),
                collection,
                1,
                0.025,
            ),
            "engine_mount",
            "skeleton",
            ("base",),
        )
        register(
            create_torus(
                f"SRC_EngineMountCollar_{label}",
                (side * 0.69, -1.64, 0.0),
                0.235,
                0.035,
                collection,
                3,
                28,
                10,
                True,
            ),
            "engine_mount",
            "skeleton",
            ("base",),
        )
        register(
            create_cylinder(
                f"SRC_EngineMountCap_{label}",
                (side * 0.69, -1.675, 0.0),
                0.205,
                0.055,
                collection,
                0,
                28,
                True,
                0.010,
            ),
            "engine_mount_cover",
            "empty_mount_cover",
            ("bare",),
        )
        register(
            create_box(
                f"SRC_WingMountFrame_{label}",
                (side * 0.835, 0.18, 0.02),
                (0.075, 0.68, 0.58),
                collection,
                1,
                0.018,
            ),
            "wing_mount",
            "skeleton",
            ("base",),
        )
        register(
            create_box(
                f"SRC_WingMountCover_{label}",
                (side * 0.878, 0.18, 0.02),
                (0.028, 0.49, 0.38),
                collection,
                0,
                0.010,
            ),
            "wing_mount_cover",
            "empty_mount_cover",
            ("bare",),
        )

    for socket_role, y in (("FORE", 0.55), ("AFT", -0.55)):
        for side, label in ((-1.0, "L"), (1.0, "R")):
            register(
                create_box(
                    f"SRC_HardpointFrame_{socket_role}_{label}",
                    (side * 0.48, y, -0.835),
                    (0.30, 0.38, 0.065),
                    collection,
                    1,
                    0.012,
                ),
                "hardpoint_mount",
                "skeleton",
                ("base",),
            )
            cover_groups = (
                ("bare", "explorer")
                if socket_role == "FORE"
                else ("bare", "explorer", "combat")
            )
            register(
                create_box(
                    f"SRC_HardpointCover_{socket_role}_{label}",
                    (side * 0.48, y, -0.875),
                    (0.24, 0.31, 0.035),
                    collection,
                    0,
                    0.008,
                ),
                "hardpoint_cover",
                "empty_mount_cover",
                cover_groups,
            )

    register(
        create_box(
            "SRC_DorsalBayFrame",
            (0.0, -0.10, 0.955),
            (0.60, 0.82, 0.060),
            collection,
            1,
            0.014,
        ),
        "utility_mount",
        "skeleton",
        ("base",),
    )
    register(
        create_box(
            "SRC_DorsalBayCover",
            (0.0, -0.10, 0.995),
            (0.48, 0.67, 0.035),
            collection,
            0,
            0.009,
        ),
        "utility_mount_cover",
        "empty_mount_cover",
        ("bare",),
    )
    register(
        create_box(
            "SRC_VentralBayFrame",
            (0.0, -0.10, -0.865),
            (0.60, 0.82, 0.060),
            collection,
            1,
            0.014,
        ),
        "utility_mount",
        "skeleton",
        ("base",),
    )
    register(
        create_box(
            "SRC_VentralBayCover",
            (0.0, -0.10, -0.905),
            (0.48, 0.67, 0.035),
            collection,
            0,
            0.009,
        ),
        "utility_mount_cover",
        "empty_mount_cover",
        ("bare", "explorer"),
    )

    # Always-installed reaction-control clusters keep the base hull credible
    # during docking and attitude control even without external drive pods.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        for cluster, y, z in (
            ("FORE_UP", 1.12, 0.31),
            ("FORE_DOWN", 1.12, -0.31),
            ("AFT_UP", -1.14, 0.28),
            ("AFT_DOWN", -1.14, -0.28),
        ):
            rcs = create_cone(
                f"SRC_RCS_{cluster}_{label}",
                (side * 0.805, y, z),
                0.055,
                0.038,
                0.055,
                collection,
                2,
                16,
                False,
                0.0,
            )
            rcs.rotation_euler.y = side * math.pi * 0.5
            bpy.context.view_layer.objects.active = rcs
            rcs.select_set(True)
            bpy.ops.object.transform_apply(
                location=False,
                rotation=True,
                scale=False,
            )
            rcs.select_set(False)
            register(
                rcs,
                "reaction_control",
                "skeleton",
                ("base",),
            )

    explorer_left = [
        (-0.64, 0.78),
        (-1.33, 0.30),
        (-1.28, -0.66),
        (-0.70, -0.80),
    ]
    combat_left = [
        (-0.62, 0.92),
        (-1.50, 0.28),
        (-1.43, -0.72),
        (-0.72, -0.88),
    ]
    for name, points, configurations in (
        ("ExplorerWing", explorer_left, ("explorer",)),
        ("CombatWing", combat_left, ("combat",)),
    ):
        for label, wing_points in (
            ("L", points),
            ("R", mirror_points_x(points)),
        ):
            register(
                create_prism(
                    f"SRC_{name}_{label}",
                    wing_points,
                    -0.09,
                    0.09,
                    collection,
                    0,
                    0.028,
                ),
                "wing",
                name,
                configurations,
            )

    wing_detail_specs = (
        (
            "Explorer",
            [
                (-0.72, 0.63),
                (-1.18, 0.25),
                (-1.14, -0.48),
                (-0.76, -0.60),
            ],
            explorer_left,
            ("explorer",),
        ),
        (
            "Combat",
            [
                (-0.71, 0.73),
                (-1.33, 0.24),
                (-1.27, -0.55),
                (-0.77, -0.68),
            ],
            combat_left,
            ("combat",),
        ),
    )
    for style, inset_left, outline_left, configurations in wing_detail_specs:
        for label, inset_points, outline_points in (
            ("L", inset_left, outline_left),
            (
                "R",
                mirror_points_x(inset_left),
                mirror_points_x(outline_left),
            ),
        ):
            register(
                create_prism(
                    f"SRC_{style}WingInset_{label}",
                    inset_points,
                    0.095,
                    0.132,
                    collection,
                    1 if style == "Explorer" else 3,
                    0.012,
                ),
                "wing_armor",
                f"{style.lower()}_wing_detail",
                configurations,
            )
            register(
                create_box(
                    f"SRC_{style}WingHinge_{label}",
                    (
                        -0.70 if label == "L" else 0.70,
                        0.22,
                        0.145,
                    ),
                    (0.16, 0.54, 0.12),
                    collection,
                    3,
                    0.018,
                ),
                "wing_hinge",
                f"{style.lower()}_wing_detail",
                configurations,
            )
            leading_start = inset_points[0]
            leading_end = inset_points[1]
            trailing_start = inset_points[2]
            trailing_end = inset_points[3]
            for edge_name, start_xy, end_xy in (
                ("Leading", leading_start, leading_end),
                ("Trailing", trailing_start, trailing_end),
            ):
                register(
                    create_strut(
                        f"SRC_{style}Wing{edge_name}Rail_{label}",
                        (start_xy[0], start_xy[1], 0.13),
                        (end_xy[0], end_xy[1], 0.13),
                        0.045,
                        collection,
                        3,
                    ),
                    "wing_edge",
                    f"{style.lower()}_wing_detail",
                    configurations,
                )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        engine_x = side * 0.69
        register(
            create_loft(
                f"SRC_EngineCowl_{label}",
                [
                    (-1.52, 0.24, -0.36, 0.37, 0.09),
                    (-2.02, 0.31, -0.43, 0.44, 0.11),
                    (-2.64, 0.29, -0.40, 0.41, 0.10),
                    (-3.00, 0.22, -0.33, 0.34, 0.08),
                ],
                collection,
                1,
                0.028,
            ),
            "engine_cowl",
            "engine",
            ("explorer", "combat"),
        )
        cowl = parts[f"SRC_EngineCowl_{label}"]
        cowl.location.x = engine_x
        bpy.context.view_layer.objects.active = cowl
        cowl.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
        cowl.select_set(False)

        register(
            create_cylinder(
                f"SRC_EngineCore_{label}",
                (engine_x, -2.32, 0.0),
                0.17,
                1.28,
                collection,
                3,
                24,
                True,
                0.018,
            ),
            "engine_core",
            "engine",
            ("explorer", "combat"),
        )
        register(
            create_cylinder(
                f"SRC_EngineGlow_{label}",
                (engine_x, -2.975, 0.0),
                0.16,
                0.05,
                collection,
                2,
                24,
                True,
                0.006,
            ),
            "engine_emission",
            "engine",
            ("explorer", "combat"),
        )
        register(
            create_cone(
                f"SRC_EngineNozzle_{label}",
                (engine_x, -2.865, 0.0),
                0.235,
                0.165,
                0.24,
                collection,
                1,
                32,
                True,
                0.014,
            ),
            "engine_nozzle",
            "engine_detail",
            ("explorer", "combat"),
        )
        for ring_index, (y, radius, thickness) in enumerate(
            (
                (-1.78, 0.245, 0.024),
                (-2.28, 0.300, 0.026),
                (-2.74, 0.268, 0.030),
                (-2.955, 0.215, 0.032),
            )
        ):
            register(
                create_torus(
                    f"SRC_EngineRing_{label}_{ring_index:02d}",
                    (engine_x, y, 0.0),
                    radius,
                    thickness,
                    collection,
                    3 if ring_index in (0, 3) else 1,
                    32,
                    10,
                    True,
                ),
                "engine_ring",
                "engine_detail",
                ("explorer", "combat"),
            )
        for rail_index, (x_offset, z) in enumerate(
            ((-0.20, 0.0), (0.20, 0.0), (0.0, 0.30), (0.0, -0.30))
        ):
            register(
                create_box(
                    f"SRC_EngineCowlRail_{label}_{rail_index:02d}",
                    (engine_x + x_offset, -2.27, z),
                    (
                        0.050 if x_offset else 0.31,
                        1.05,
                        0.050 if x_offset else 0.045,
                    ),
                    collection,
                    3,
                    0.010,
                ),
                "engine_rail",
                "engine_detail",
                ("explorer", "combat"),
            )
        register(
            create_strut(
                f"SRC_EngineFeedLine_{label}",
                (side * 0.47, -1.42, 0.23),
                (engine_x, -2.33, 0.23),
                0.050,
                collection,
                2,
            ),
            "engine_feed",
            "engine_detail",
            ("explorer", "combat"),
        )
        for fin_index, z in enumerate((-0.24, 0.24)):
            register(
                create_box(
                    f"SRC_NozzleVane_{label}_{fin_index:02d}",
                    (engine_x, -2.97, z),
                    (0.08, 0.055, 0.18),
                    collection,
                    1,
                    0.008,
                ),
                "nozzle_vane",
                "engine_detail",
                ("explorer", "combat"),
            )

    armor_count = 3 + int(round(parameters.armor_density * 2.0))
    for index in range(armor_count):
        y = 0.72 - index * 0.50
        width = 1.15 - index * 0.045
        register(
            create_box(
                f"SRC_DorsalArmor_{index:02d}",
                (0.0, y, 0.875 + (0.008 if index % 2 else 0.0)),
                (width, 0.38, 0.07),
                collection,
                0 if index % 3 else 3,
                0.018,
            ),
            "armor",
            "combat_armor",
            ("combat",),
        )
        register(
            create_box(
                f"SRC_DorsalArmorLatch_{index:02d}",
                (0.0, y, 0.928 + (0.008 if index % 2 else 0.0)),
                (0.15, 0.11, 0.055),
                collection,
                1,
                0.010,
            ),
            "armor_latch",
            "combat_armor",
            ("combat",),
        )
        for side, label in ((-1.0, "L"), (1.0, "R")):
            register(
                create_box(
                    f"SRC_DorsalArmorBolt_{index:02d}_{label}",
                    (side * (width * 0.39), y, 0.925),
                    (0.075, 0.075, 0.052),
                    collection,
                    3,
                    0.012,
                ),
                "armor_fastener",
                "combat_armor",
                ("combat",),
            )

    register(
        create_box(
            "SRC_DorsalUtilityPod",
            (0.0, -0.38, 0.985),
            (0.48, 0.78, 0.23),
            collection,
            3,
            0.042,
        ),
        "utility_pod",
        "explorer_utility",
        ("explorer",),
    )
    register(
        create_cylinder(
            "SRC_UtilitySensorCap",
            (0.0, -0.38, 1.055),
            0.105,
            0.09,
            collection,
            2,
            24,
            False,
            0.010,
        ),
        "sensor",
        "explorer_utility",
        ("explorer",),
    )
    for side, label in ((-1.0, "L"), (1.0, "R")):
        for vent_index, y in enumerate((-0.16, -0.36, -0.56)):
            register(
                create_box(
                    f"SRC_UtilityPodVent_{label}_{vent_index:02d}",
                    (side * 0.247, y, 0.995),
                    (0.032, 0.11, 0.075),
                    collection,
                    1,
                    0.006,
                ),
                "utility_vent",
                "explorer_utility",
                ("explorer",),
            )
    register(
        create_box(
            "SRC_DorsalCombatPlate",
            (0.0, -0.18, 1.035),
            (0.82, 0.82, 0.13),
            collection,
            3,
            0.026,
        ),
        "armor",
        "combat_armor",
        ("combat",),
    )
    register(
        create_box(
            "SRC_VentralCombatPlate",
            (0.0, -0.12, -1.035),
            (0.86, 0.90, 0.13),
            collection,
            1,
            0.026,
        ),
        "armor",
        "combat_armor",
        ("combat",),
    )
    for vent_index, x in enumerate((-0.27, -0.09, 0.09, 0.27)):
        register(
            create_box(
                f"SRC_CombatPlateVent_{vent_index:02d}",
                (x, -0.18, 1.085),
                (0.095, 0.48, 0.030),
                collection,
                1,
                0.008,
            ),
            "armor_vent",
            "combat_armor",
            ("combat",),
        )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        x = side * 0.48
        register(
            create_loft(
                f"SRC_WeaponFairing_{label}",
                [
                    (0.78, 0.13, -0.26, 0.12, 0.04),
                    (0.22, 0.19, -0.31, 0.16, 0.06),
                    (-0.18, 0.15, -0.27, 0.12, 0.05),
                ],
                collection,
                1,
                0.020,
            ),
            "weapon_fairing",
            "combat_weapon",
            ("combat",),
        )
        fairing = parts[f"SRC_WeaponFairing_{label}"]
        fairing.location.x = x
        fairing.location.z = -0.62
        bpy.context.view_layer.objects.active = fairing
        fairing.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
        fairing.select_set(False)
        register(
            create_cylinder(
                f"SRC_WeaponBarrel_{label}",
                (x, 0.72, -0.84),
                0.045,
                0.70,
                collection,
                3,
                12,
                True,
                0.008,
            ),
            "weapon",
            "combat_weapon",
            ("combat",),
        )
        register(
            create_cylinder(
                f"SRC_WeaponBarrelSleeve_{label}",
                (x, 0.47, -0.84),
                0.068,
                0.22,
                collection,
                1,
                16,
                True,
                0.010,
            ),
            "weapon_sleeve",
            "combat_weapon",
            ("combat",),
        )
        for ring_index, y in enumerate((0.47, 1.055)):
            register(
                create_torus(
                    f"SRC_WeaponRing_{label}_{ring_index:02d}",
                    (x, y, -0.84),
                    0.055,
                    0.012,
                    collection,
                    3,
                    20,
                    8,
                    True,
                ),
                "weapon_ring",
                "combat_weapon",
                ("combat",),
            )
        register(
            create_box(
                f"SRC_WeaponServiceHatch_{label}",
                (x, 0.16, -0.49),
                (0.18, 0.28, 0.035),
                collection,
                3,
                0.010,
            ),
            "weapon_hatch",
            "combat_weapon",
            ("combat",),
        )

    asymmetry = max(0.0, min(0.3, parameters.asymmetry_strength))
    if asymmetry > 0.0:
        side = -1.0 if rng.random() < 0.5 else 1.0
        register(
            create_box(
                "SRC_AsymmetricServiceBox",
                (side * 0.79, -0.34, 0.28),
                (0.10 + asymmetry * 0.22, 0.42, 0.30),
                collection,
                3,
                0.018,
            ),
            "service_detail",
            "explorer_utility",
            ("explorer", "combat"),
        )

    for obj in parts.values():
        obj.hide_render = True
        obj.hide_set(True)
    collection.hide_render = True
    return parts, groups


def duplicate_part(
    source: bpy.types.Object,
    name: str,
    collection: bpy.types.Collection,
    offset: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    clone = source.copy()
    clone.data = source.data.copy()
    clone.name = name
    clone.data.name = name
    collection.objects.link(clone)
    clone.location = source.location + Vector(offset)
    clone.hide_render = False
    clone.hide_set(False)
    return clone


def add_label(
    collection: bpy.types.Collection,
    text: str,
    location: tuple[float, float, float],
    size: float = 0.42,
) -> bpy.types.Object:
    curve = bpy.data.curves.new(text + "_LabelCurve", "FONT")
    curve.body = text
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.size = size
    curve.extrude = 0.008
    obj = bpy.data.objects.new(text + "_Label", curve)
    collection.objects.link(obj)
    obj.location = location
    return obj


def create_socket_empty(
    spec: SocketSpec,
    collection: bpy.types.Collection,
    offset: tuple[float, float, float],
) -> bpy.types.Object:
    obj = bpy.data.objects.new(spec.socket_id, None)
    collection.objects.link(obj)
    obj.empty_display_type = "ARROWS"
    obj.empty_display_size = 0.22
    obj.location = Vector(spec.position) + Vector(offset)
    normal = Vector(spec.normal).normalized()
    obj.rotation_euler = Vector((0.0, 0.0, 1.0)).rotation_difference(
        normal
    ).to_euler()
    obj["socket_id"] = spec.socket_id
    obj["connector_type"] = spec.connector_type
    obj["connector_size"] = spec.connector_size
    obj["mirror_socket_id"] = spec.mirror_id
    obj["mount_normal"] = spec.normal
    obj["forward_axis"] = "+Y"
    obj["unity_forward_axis"] = "+Z"
    return obj


def build_configuration(
    name: str,
    part_names: list[str],
    source_parts: dict[str, bpy.types.Object],
    offset: tuple[float, float, float],
    show_sockets: bool,
) -> tuple[bpy.types.Collection, list[bpy.types.Object]]:
    collection = ensure_collection(name)
    visible = [
        duplicate_part(
            source_parts[part_name],
            f"{name}_{part_name.removeprefix('SRC_')}",
            collection,
            offset,
        )
        for part_name in part_names
    ]
    if show_sockets:
        sockets = ensure_collection(name + "_Sockets", collection)
        for spec in SOCKETS:
            create_socket_empty(spec, sockets, offset)
    add_label(
        collection,
        name.split("_", 1)[-1].replace("_", " "),
        (offset[0], offset[1] - 3.65, -1.05),
        0.38,
    )
    return collection, visible


def build_exploded_configuration(
    source_parts: dict[str, bpy.types.Object],
    groups: dict[str, list[str]],
    offset: tuple[float, float, float],
) -> tuple[bpy.types.Collection, list[bpy.types.Object]]:
    collection = ensure_collection("40_Exploded_Configuration")
    visible: list[bpy.types.Object] = []
    base_displacements = {
        "SRC_CockpitShell": (0.0, 1.35, 0.0),
        "SRC_CockpitGlazing": (0.0, 1.35, 0.45),
        "SRC_CorePressureHull": (0.0, 0.0, 0.0),
        "SRC_DorsalServiceSpine": (0.0, 0.0, 1.0),
        "SRC_VentralKeel": (0.0, 0.0, -1.0),
        "SRC_RearPowerBulkhead": (0.0, -1.15, 0.0),
    }
    combat_names = groups["base"] + groups["combat"]
    for index, part_name in enumerate(combat_names):
        source = source_parts[part_name]
        displacement = Vector(base_displacements.get(part_name, (0.0, 0.0, 0.0)))
        if "Wing_L" in part_name or "Fairing_L" in part_name or "Barrel_L" in part_name:
            displacement += Vector((-1.05, 0.0, 0.0))
        elif "Wing_R" in part_name or "Fairing_R" in part_name or "Barrel_R" in part_name:
            displacement += Vector((1.05, 0.0, 0.0))
        elif "Engine" in part_name and part_name.endswith("_L"):
            displacement += Vector((-0.75, -0.55, 0.0))
        elif "Engine" in part_name and part_name.endswith("_R"):
            displacement += Vector((0.75, -0.55, 0.0))
        elif "Armor" in part_name or "CombatPlate" in part_name:
            displacement += Vector((0.0, 0.0, 0.65 + (index % 3) * 0.10))
        clone = duplicate_part(
            source,
            f"EXP_{part_name.removeprefix('SRC_')}",
            collection,
            tuple(Vector(offset) + displacement),
        )
        visible.append(clone)
    sockets = ensure_collection("40_Exploded_Sockets", collection)
    for spec in SOCKETS:
        create_socket_empty(spec, sockets, offset)
    add_label(
        collection,
        "Exploded / Socket Audit",
        (offset[0], offset[1] - 4.45, -1.45),
        0.38,
    )
    return collection, visible


def duplicate_for_export(
    part_names: list[str],
    source_parts: dict[str, bpy.types.Object],
    collection: bpy.types.Collection,
) -> list[bpy.types.Object]:
    return [
        duplicate_part(
            source_parts[name],
            f"EXPORT_{name.removeprefix('SRC_')}",
            collection,
        )
        for name in part_names
    ]


def strict_mesh_checks(obj: bpy.types.Object) -> dict[str, int]:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    result = {
        "nonManifoldEdges": sum(1 for edge in bm.edges if not edge.is_manifold),
        "boundaryEdges": sum(1 for edge in bm.edges if edge.is_boundary),
        "degenerateFaces": sum(1 for face in bm.faces if face.calc_area() < 1e-10),
    }
    bm.free()
    return result


def build_export_contract(
    source_parts: dict[str, bpy.types.Object],
    part_names: list[str],
    output_root: Path,
) -> tuple[dict, list[bpy.types.Object], bpy.types.Object]:
    export_collection = ensure_collection("90_Export_Contract")
    export_parts = duplicate_for_export(part_names, source_parts, export_collection)
    lod0 = pipeline.join_geometry(export_parts, "Render_LOD0")
    move_to_collection(lod0, export_collection)
    pipeline.cube_uv(lod0)
    pipeline.decimate_to(lod0, 80000)

    lod0_count = pipeline.triangle_count(lod0)
    lod1 = pipeline.duplicate_mesh(lod0, "Render_LOD1")
    move_to_collection(lod1, export_collection)
    pipeline.decimate_to(lod1, max(64, int(lod0_count * 0.55)))
    lod2 = pipeline.duplicate_mesh(lod0, "Render_LOD2")
    move_to_collection(lod2, export_collection)
    pipeline.decimate_to(lod2, max(32, int(lod0_count * 0.25)))
    collision = pipeline.convex_collision(lod2, 150)
    move_to_collection(collision, export_collection)
    export_objects = [lod0, lod1, lod2, collision]

    budgets = {
        "lod0Triangles": 80000,
        "lod1Triangles": 30000,
        "lod2Triangles": 10000,
        "collisionTriangles": 150,
        # The shared validator expresses tolerance as a percentage. The
        # explicit absolute 0.01m contract is enforced immediately below.
        "dimensionTolerance": 0.0045,
    }
    errors, counts, actual = pipeline.validate_mesh_set(
        lod0,
        lod1,
        lod2,
        collision,
        DIMENSIONS,
        budgets,
    )
    for index, label in enumerate(("width", "height", "length")):
        if abs(actual[index] - DIMENSIONS[index]) > 0.01:
            errors.append(
                f"{label} absolute error exceeds 0.01m: "
                f"{actual[index]:.4f} vs {DIMENSIONS[index]:.4f}"
            )
    if not counts["lod0"] > counts["lod1"] > counts["lod2"]:
        errors.append("LOD triangle counts are not strictly decreasing")

    topology = strict_mesh_checks(lod0)
    if topology["nonManifoldEdges"] or topology["degenerateFaces"]:
        errors.append(f"LOD0 topology check failed: {topology}")

    fbx_path = output_root / "Hulls" / f"{PROTOTYPE_ID}.fbx"
    preview_path = output_root / "Thumbnails" / f"{PROTOTYPE_ID}.png"
    pipeline.export_fbx(export_objects, fbx_path)
    render_states = {
        obj.name: obj.hide_render for obj in bpy.context.scene.objects
    }
    for obj in bpy.context.scene.objects:
        obj.hide_render = obj != lod0
    original_rotation = lod0.rotation_euler.copy()
    # The project contract uses Blender +Y as the ship's nose. The shared
    # preview camera looks from -Y, so turn the presentation copy to show the
    # cockpit rather than the engine deck.
    lod0.rotation_euler.z += math.pi
    pipeline.render_preview(lod0, preview_path, 512)
    lod0.rotation_euler = original_rotation
    for obj in bpy.context.scene.objects:
        if obj.name in render_states:
            obj.hide_render = render_states[obj.name]

    for obj in export_objects:
        obj.hide_set(True)
        obj.hide_render = True
    export_collection.hide_render = True
    return (
        {
            "fbx": str(fbx_path),
            "thumbnail": str(preview_path),
            "triangles": counts,
            "actualDimensions": actual,
            "topology": topology,
            "geometryHash": pipeline.geometry_hash(export_objects),
            "materialSlots": [
                slot.material.name for slot in lod0.material_slots
            ],
            "errors": errors,
        },
        export_objects,
        lod0,
    )


def create_review_environment(
    all_visible: list[bpy.types.Object],
) -> None:
    scene = bpy.context.scene
    scene.name = "Modular Ship V1 Review"
    scene.render.engine = "BLENDER_EEVEE"
    scene.world = scene.world or bpy.data.worlds.new("PrototypeReviewWorld")
    scene.world.color = (0.018, 0.025, 0.042)
    scene.view_settings.look = "AgX - Medium High Contrast"

    review_collection = ensure_collection("99_Review_Environment")
    floor = create_box(
        "ReviewFloor",
        (0.0, 0.0, -1.62),
        (17.0, 15.0, 0.08),
        review_collection,
        1,
        0.01,
    )
    floor.hide_render = False

    camera_data = bpy.data.cameras.new("ReviewCamera")
    camera = bpy.data.objects.new("ReviewCamera", camera_data)
    review_collection.objects.link(camera)
    camera.location = (12.8, -18.5, 14.5)
    target = Vector((0.0, -0.3, 0.0))
    camera.rotation_euler = (target - camera.location).to_track_quat(
        "-Z", "Y"
    ).to_euler()
    camera_data.lens = 42.0
    camera_data.clip_end = 1000.0
    scene.camera = camera

    for name, location, energy, color, size in (
        (
            "ReviewKey",
            (5.5, -3.5, 11.0),
            1600,
            (0.78, 0.90, 1.0),
            7.0,
        ),
        (
            "ReviewFill",
            (-8.0, 1.0, 6.0),
            1050,
            (0.28, 0.48, 1.0),
            8.0,
        ),
        (
            "ReviewRim",
            (1.0, 9.0, 7.5),
            1800,
            (1.0, 0.42, 0.18),
            6.0,
        ),
    ):
        light_data = bpy.data.lights.new(name, "AREA")
        light_data.energy = energy
        light_data.color = color
        light_data.shape = "DISK"
        light_data.size = size
        light = bpy.data.objects.new(name, light_data)
        review_collection.objects.link(light)
        light.location = location
        light.rotation_euler = (target - light.location).to_track_quat(
            "-Z", "Y"
        ).to_euler()

    bpy.ops.object.select_all(action="DESELECT")
    for obj in all_visible:
        obj.select_set(True)
    if all_visible:
        bpy.context.view_layer.objects.active = all_visible[0]


def build_review_scene(
    parameters: PrototypeParameters,
    output_root: Path,
    review_file: Path,
    report_path: Path,
) -> dict:
    pipeline.clean_scene()
    source_collection = ensure_collection("00_Source_Modules")
    source_parts, groups = build_source_modules(parameters, source_collection)

    export_result, _, _ = build_export_contract(
        source_parts,
        groups["base"] + groups["combat"],
        output_root,
    )
    _, bare = build_configuration(
        "10_Base_Flight_Hull",
        groups["base"] + groups["bare"],
        source_parts,
        (-5.25, 3.55, 0.0),
        True,
    )
    _, explorer = build_configuration(
        "20_Explorer_Configuration",
        groups["base"] + groups["explorer"],
        source_parts,
        (0.0, 3.55, 0.0),
        False,
    )
    _, combat = build_configuration(
        "30_Combat_Configuration",
        groups["base"] + groups["combat"],
        source_parts,
        (5.25, 3.55, 0.0),
        False,
    )
    _, exploded = build_exploded_configuration(
        source_parts,
        groups,
        (0.0, -4.15, 0.0),
    )

    create_review_environment(bare + explorer + combat + exploded)

    report = {
        "success": not export_result["errors"],
        "prototypeId": PROTOTYPE_ID,
        "blenderVersion": bpy.app.version_string,
        "parameters": asdict(parameters),
        "dimensions": DIMENSIONS,
        "forwardAxis": "+Y",
        "unityForwardAxis": "+Z",
        "configurations": {
            "bare": groups["base"] + groups["bare"],
            "explorer": groups["base"] + groups["explorer"],
            "combat": groups["base"] + groups["combat"],
            "exploded": groups["base"] + groups["combat"],
        },
        "sockets": [asdict(spec) for spec in SOCKETS],
        "export": export_result,
        "reviewFile": str(review_file),
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(
        json.dumps(report, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    if export_result["errors"]:
        raise RuntimeError(
            "Modular prototype validation failed:\n"
            + "\n".join(export_result["errors"])
        )

    review_file.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(review_file))
    return report


def main() -> None:
    args = parse_args()
    output_root = Path(args.output_root).resolve()
    review_file = Path(args.review_file).resolve()
    report_path = (
        Path(args.report).resolve()
        if args.report
        else output_root / "prototype_report.json"
    )
    parameters = PrototypeParameters(seed=args.seed)
    report = build_review_scene(
        parameters,
        output_root,
        review_file,
        report_path,
    )
    print(
        "[SpaceshipPCG] modular prototype generated: "
        f"{report['export']['triangles']} "
        f"hash={report['export']['geometryHash']}"
    )
    print(f"[SpaceshipPCG] review file: {review_file}")
    print(f"[SpaceshipPCG] report: {report_path}")


if __name__ == "__main__":
    main()
