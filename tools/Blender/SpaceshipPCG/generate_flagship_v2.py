"""Generate the high-detail arrowhead flagship and free-placement module kit.

The generator is intentionally isolated from the production publisher.  It
reuses the stable material, LOD, collision, FBX, preview, validation, and hash
contracts from generate_fleet.py while writing only under Library/.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parents[2]
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import generate_fleet as pipeline
from flagship_contract import HULL_BUDGETS, MATERIAL_NAMES, MODULE_SPECS, ModuleSpec


ASSET_ID = "flagship_arrowhead_v2"
SEED = "flagship-arrowhead-v2-industrial"
DIMENSIONS = (3.0, 2.2, 6.0)
DEFAULT_OUTPUT_ROOT = (
    PROJECT_ROOT / "Library" / "SpaceshipPCGStaging" / ASSET_ID
)
DEFAULT_REVIEW_FILE = (
    PROJECT_ROOT
    / "Library"
    / "SpaceshipPCGReview"
    / f"{ASSET_ID}_review.blend"
)


def parse_args() -> argparse.Namespace:
    argv = sys.argv
    argv = argv[argv.index("--") + 1 :] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-root", default=str(DEFAULT_OUTPUT_ROOT))
    parser.add_argument("--review-file", default=str(DEFAULT_REVIEW_FILE))
    parser.add_argument("--report", default="")
    parser.add_argument("--seed", default=SEED)
    return parser.parse_args(argv)


def ensure_collection(
    name: str,
    parent: bpy.types.Collection | None = None,
) -> bpy.types.Collection:
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
    owner = parent or bpy.context.scene.collection
    if collection.name not in {item.name for item in owner.children}:
        owner.children.link(collection)
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


def finish_mesh(
    obj: bpy.types.Object,
    collection: bpy.types.Collection,
    material_index: int,
    bevel: float = 0.0,
    bevel_segments: int = 3,
    subdivision: int = 0,
) -> bpy.types.Object:
    move_to_collection(obj, collection)
    assign_palette(obj, material_index)
    if subdivision > 0:
        modifier = obj.modifiers.new("FlagshipSubdivision", "SUBSURF")
        # SIMPLE adds the close-range tessellation needed for clean highlights
        # without pulling the authored arrowhead stations into a rounded blob.
        modifier.subdivision_type = "SIMPLE"
        modifier.levels = subdivision
        modifier.render_levels = subdivision
        modifier.show_only_control_edges = True
    if bevel > 0.0:
        modifier = obj.modifiers.new("FlagshipBevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = bevel_segments
        modifier.profile = 0.45
        if hasattr(modifier, "harden_normals"):
            modifier.harden_normals = True
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def create_box(
    name: str,
    center: tuple[float, float, float],
    size: tuple[float, float, float],
    collection: bpy.types.Collection,
    material_index: int = 0,
    bevel: float = 0.025,
    bevel_segments: int = 4,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=center)
    obj = bpy.context.object
    obj.name = name
    obj.scale = (size[0] * 0.5, size[1] * 0.5, size[2] * 0.5)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_mesh(
        obj,
        collection,
        material_index,
        bevel,
        bevel_segments,
    )


def create_cylinder(
    name: str,
    center: tuple[float, float, float],
    radius: float,
    depth: float,
    collection: bpy.types.Collection,
    material_index: int = 1,
    vertices: int = 48,
    axis: str = "Z",
    bevel: float = 0.012,
) -> bpy.types.Object:
    rotation = {
        "X": (0.0, math.pi * 0.5, 0.0),
        "Y": (math.pi * 0.5, 0.0, 0.0),
        "Z": (0.0, 0.0, 0.0),
    }[axis]
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        location=center,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    return finish_mesh(obj, collection, material_index, bevel, 3)


def create_cone(
    name: str,
    center: tuple[float, float, float],
    radius_bottom: float,
    radius_top: float,
    depth: float,
    collection: bpy.types.Collection,
    material_index: int = 1,
    vertices: int = 48,
    axis: str = "Z",
    bevel: float = 0.01,
) -> bpy.types.Object:
    rotation = {
        "X": (0.0, math.pi * 0.5, 0.0),
        "Y": (math.pi * 0.5, 0.0, 0.0),
        "Z": (0.0, 0.0, 0.0),
    }[axis]
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius_bottom,
        radius2=radius_top,
        depth=depth,
        location=center,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    return finish_mesh(obj, collection, material_index, bevel, 3)


def create_torus(
    name: str,
    center: tuple[float, float, float],
    major_radius: float,
    minor_radius: float,
    collection: bpy.types.Collection,
    material_index: int = 3,
    axis: str = "Z",
    major_segments: int = 64,
    minor_segments: int = 16,
) -> bpy.types.Object:
    rotation = {
        "X": (0.0, math.pi * 0.5, 0.0),
        "Y": (math.pi * 0.5, 0.0, 0.0),
        "Z": (0.0, 0.0, 0.0),
    }[axis]
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
    return finish_mesh(obj, collection, material_index)


def create_prism(
    name: str,
    points_xy: list[tuple[float, float]],
    z_min: float,
    z_max: float,
    collection: bpy.types.Collection,
    material_index: int = 0,
    bevel: float = 0.02,
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
    return finish_mesh(obj, collection, material_index, bevel, 4)


def create_surface_panel(
    name: str,
    points_xyz: list[tuple[float, float, float]],
    thickness: float,
    collection: bpy.types.Collection,
    material_index: int = 0,
    bevel: float = 0.012,
) -> bpy.types.Object:
    """Create a closed, lightly raised panel that follows authored hull points."""
    count = len(points_xyz)
    vertices = list(points_xyz)
    vertices += [(x, y, z + thickness) for x, y, z in points_xyz]
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
    return finish_mesh(obj, collection, material_index, bevel, 3)


def create_strut(
    name: str,
    start: tuple[float, float, float],
    end: tuple[float, float, float],
    thickness: float,
    collection: bpy.types.Collection,
    material_index: int = 3,
) -> bpy.types.Object:
    first = Vector(start)
    second = Vector(end)
    direction = second - first
    obj = create_box(
        name,
        tuple((first + second) * 0.5),
        (thickness, direction.length, thickness),
        collection,
        material_index,
        min(0.02, thickness * 0.25),
    )
    obj.rotation_euler = Vector((0.0, 1.0, 0.0)).rotation_difference(
        direction.normalized()
    ).to_euler()
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.select_set(False)
    return obj


def superellipse_point(
    angle: float,
    half_width: float,
    half_height: float,
    exponent: float,
) -> tuple[float, float]:
    cosine = math.cos(angle)
    sine = math.sin(angle)
    power = 2.0 / max(1.01, exponent)
    x = half_width * math.copysign(abs(cosine) ** power, cosine)
    z = half_height * math.copysign(abs(sine) ** power, sine)
    return x, z


def create_smooth_loft(
    name: str,
    stations: list[tuple[float, float, float, float, float]],
    collection: bpy.types.Collection,
    material_index: int = 0,
    segments: int = 64,
    subdivision: int = 2,
    station_steps: int = 1,
) -> bpy.types.Object:
    """Create a closed superellipse loft.

    Stations are (y, half_width, half_height, center_z, exponent).
    """
    loft_stations = stations
    if station_steps > 1 and len(stations) >= 3:
        loft_stations = []
        for interval in range(len(stations) - 1):
            p0 = stations[max(0, interval - 1)]
            p1 = stations[interval]
            p2 = stations[interval + 1]
            p3 = stations[min(len(stations) - 1, interval + 2)]
            for step in range(station_steps):
                t = step / float(station_steps)
                t2 = t * t
                t3 = t2 * t
                values = []
                for component in range(5):
                    value = 0.5 * (
                        2.0 * p1[component]
                        + (-p0[component] + p2[component]) * t
                        + (
                            2.0 * p0[component]
                            - 5.0 * p1[component]
                            + 4.0 * p2[component]
                            - p3[component]
                        )
                        * t2
                        + (
                            -p0[component]
                            + 3.0 * p1[component]
                            - 3.0 * p2[component]
                            + p3[component]
                        )
                        * t3
                    )
                    values.append(value)
                values[1] = max(0.01, values[1])
                values[2] = max(0.01, values[2])
                values[4] = max(1.05, values[4])
                loft_stations.append(tuple(values))
        loft_stations.append(stations[-1])

    vertices: list[tuple[float, float, float]] = []
    for y, width, height, center_z, exponent in loft_stations:
        for index in range(segments):
            angle = math.tau * index / segments
            x, z = superellipse_point(angle, width, height, exponent)
            vertices.append((x, y, z + center_z))

    front_center = len(vertices)
    vertices.append((0.0, loft_stations[0][0], loft_stations[0][3]))
    rear_center = len(vertices)
    vertices.append(
        (0.0, loft_stations[-1][0], loft_stations[-1][3])
    )

    faces: list[tuple[int, ...]] = []
    for station_index in range(len(loft_stations) - 1):
        first = station_index * segments
        second = (station_index + 1) * segments
        for index in range(segments):
            following = (index + 1) % segments
            faces.append(
                (
                    first + index,
                    first + following,
                    second + following,
                    second + index,
                )
            )
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((front_center, following, index))
        base = (len(loft_stations) - 1) * segments
        faces.append((rear_center, base + index, base + following))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    return finish_mesh(
        obj,
        collection,
        material_index,
        subdivision=subdivision,
    )


def tag_source(
    obj: bpy.types.Object,
    role: str,
    group: str,
) -> bpy.types.Object:
    obj["asset_id"] = ASSET_ID
    obj["source_role"] = role
    obj["source_group"] = group
    obj["forward_axis"] = "+Y"
    obj["unity_forward_axis"] = "+Z"
    return obj


def duplicate_object(
    source: bpy.types.Object,
    name: str,
    collection: bpy.types.Collection,
) -> bpy.types.Object:
    clone = source.copy()
    clone.data = source.data.copy()
    clone.name = name
    clone.data.name = name
    collection.objects.link(clone)
    clone.hide_set(False)
    clone.hide_render = False
    return clone


def strict_mesh_checks(obj: bpy.types.Object) -> dict[str, int]:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    result = {
        "nonManifoldEdges": sum(1 for edge in bm.edges if not edge.is_manifold),
        "boundaryEdges": sum(1 for edge in bm.edges if edge.is_boundary),
        "degenerateFaces": sum(
            1 for face in bm.faces if face.calc_area() < 1e-10
        ),
    }
    bm.free()
    return result


def build_hull_source(
    collection: bpy.types.Collection,
) -> tuple[list[bpy.types.Object], bpy.types.Object]:
    parts: list[bpy.types.Object] = []

    def add(obj: bpy.types.Object, role: str, group: str) -> bpy.types.Object:
        parts.append(tag_source(obj, role, group))
        return obj

    hull = add(
        create_smooth_loft(
            "SRC_HullContinuousShell",
            [
                (3.00, 0.035, 0.055, -0.01, 2.0),
                (2.82, 0.23, 0.17, -0.02, 2.8),
                (2.48, 0.49, 0.31, -0.03, 3.2),
                (2.05, 0.76, 0.47, -0.03, 3.6),
                (1.48, 1.05, 0.61, -0.02, 3.9),
                (0.82, 1.30, 0.71, 0.00, 4.2),
                (0.10, 1.43, 0.76, 0.00, 4.4),
                (-0.72, 1.40, 0.74, 0.00, 4.3),
                (-1.42, 1.27, 0.67, 0.00, 4.0),
                (-2.02, 1.05, 0.57, 0.00, 3.7),
                (-2.52, 0.82, 0.48, 0.00, 3.4),
                (-2.66, 0.66, 0.42, 0.00, 3.2),
                (-2.76, 0.57, 0.38, 0.00, 3.0),
            ],
            collection,
            0,
            64,
            2,
            4,
        ),
        "continuous_hull",
        "primary_structure",
    )
    hull["continuous_build_surface"] = True

    add(
        create_smooth_loft(
            "SRC_ArmoredCockpit",
            [
                (2.70, 0.16, 0.07, 0.28, 2.0),
                (2.46, 0.31, 0.10, 0.39, 2.5),
                (2.12, 0.45, 0.13, 0.55, 2.8),
                (1.70, 0.52, 0.15, 0.67, 3.2),
                (1.28, 0.49, 0.14, 0.73, 3.2),
                (1.02, 0.39, 0.09, 0.72, 2.8),
            ],
            collection,
            1,
            48,
            2,
            3,
        ),
        "armored_cockpit",
        "cockpit",
    )

    add(
        create_smooth_loft(
            "SRC_DorsalServiceSpine",
            [
                (1.30, 0.28, 0.065, 0.76, 3.0),
                (0.80, 0.34, 0.075, 0.84, 3.2),
                (0.10, 0.36, 0.080, 0.88, 3.3),
                (-0.70, 0.34, 0.075, 0.84, 3.2),
                (-1.48, 0.27, 0.065, 0.72, 3.0),
            ],
            collection,
            1,
            40,
            2,
            3,
        ),
        "service_spine",
        "primary_structure",
    )

    add(
        create_prism(
            "SRC_VentralKeel",
            [
                (-0.25, 1.65),
                (-0.34, 0.72),
                (-0.33, -1.30),
                (-0.22, -2.24),
                (0.22, -2.24),
                (0.33, -1.30),
                (0.34, 0.72),
                (0.25, 1.65),
            ],
            -1.055,
            -0.91,
            collection,
            1,
            0.025,
        ),
        "ventral_keel",
        "primary_structure",
    )

    left_shoulder = [
        (-0.92, 1.42),
        (-1.42, 0.72),
        (-1.47, -0.15),
        (-1.22, -0.92),
        (-0.86, -1.30),
        (-0.74, -0.78),
        (-0.78, 0.65),
    ]
    right_shoulder = [(-x, y) for x, y in reversed(left_shoulder)]
    for label, points in (("L", left_shoulder), ("R", right_shoulder)):
        add(
            create_prism(
                f"SRC_ShoulderArmor_{label}",
                points,
                0.31,
                0.44,
                collection,
                0,
                0.035,
            ),
            "shoulder_armor",
            "primary_structure",
        )
        add(
            create_prism(
                f"SRC_ShoulderArmorLower_{label}",
                points,
                -0.44,
                -0.31,
                collection,
                1,
                0.030,
            ),
            "shoulder_armor",
            "primary_structure",
        )

    # Armored sensor slit instead of a transparent canopy.
    for index, (width, y, z) in enumerate(
        (
            (0.42, 2.43, 0.62),
            (0.64, 2.16, 0.78),
            (0.73, 1.83, 0.88),
        )
    ):
        add(
            create_box(
                f"SRC_CockpitSensorSlit_{index:02d}",
                (0.0, y, z),
                (width, 0.055, 0.055),
                collection,
                2 if index == 1 else 1,
                0.014,
            ),
            "cockpit_sensor",
            "cockpit",
        )

    # The cockpit reads as an armored pressure capsule: long cheek frames,
    # layered brow armor, and service latches provide useful scale without
    # turning the nose into a field of disconnected plates.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        cheek = create_yz_plate(
            f"SRC_CockpitCheekArmor_{label}",
            [
                (2.48, 0.40),
                (2.34, 0.62),
                (1.90, 0.82),
                (1.34, 0.79),
                (1.08, 0.68),
                (1.18, 0.57),
                (1.78, 0.68),
                (2.26, 0.53),
            ],
            0.026,
            collection,
            3,
            0.012,
        )
        cheek.location.x = side * 0.50
        add(cheek, "cockpit_cheek_armor", "cockpit")
        for index, (y, z) in enumerate(
            ((2.30, 0.60), (1.93, 0.75), (1.52, 0.78), (1.20, 0.67))
        ):
            latch = create_cylinder(
                f"SRC_CockpitCheekLatch_{label}_{index:02d}",
                (side * 0.535, y, z),
                0.026,
                0.020,
                collection,
                1,
                20,
                "X",
                0.004,
            )
            add(latch, "cockpit_latch", "cockpit")
        add(
            create_strut(
                f"SRC_CockpitPressureRail_{label}",
                (side * 0.48, 2.40, 0.49),
                (side * 0.52, 1.12, 0.66),
                0.035,
                collection,
                1,
            ),
            "cockpit_pressure_rail",
            "cockpit",
        )

    for index, (y, width, z) in enumerate(
        ((2.32, 0.56, 0.66), (1.99, 0.86, 0.87), (1.61, 0.94, 0.94))
    ):
        add(
            create_box(
                f"SRC_CockpitBrowArmor_{index:02d}",
                (0.0, y, z),
                (width, 0.075, 0.040),
                collection,
                3 if index == 1 else 1,
                0.010,
            ),
            "cockpit_brow_armor",
            "cockpit",
        )

    # Large clean armor fields are broken only by a controlled service rhythm.
    for index, y in enumerate((0.92, 0.42, -0.10, -0.64, -1.18)):
        width = 0.58 - index * 0.025
        add(
            create_box(
                f"SRC_DorsalMaintenancePanel_{index:02d}",
                (0.0, y, 0.935 - index * 0.028),
                (width, 0.34, 0.035),
                collection,
                0 if index not in (0, 4) else 3,
                0.012,
            ),
            "maintenance_panel",
            "surface_detail",
        )
        add(
            create_box(
                f"SRC_DorsalPanelLatch_{index:02d}",
                (0.0, y, 0.960 - index * 0.028),
                (0.12, 0.08, 0.026),
                collection,
                1,
                0.007,
            ),
            "panel_latch",
            "surface_detail",
        )

    # Paired dorsal heat exchangers sit in the broad shoulder field. Their
    # repeated slats establish manufacturing scale while the outer armor
    # remains continuous and usable for free module placement.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            create_box(
                f"SRC_DorsalHeatExchangerBed_{label}",
                (side * 0.66, -1.60, 0.72),
                (0.42, 0.86, 0.055),
                collection,
                1,
                0.018,
            ),
            "dorsal_heat_exchanger",
            "thermal",
        )
        for index, y in enumerate((-1.91, -1.77, -1.63, -1.49, -1.35, -1.21)):
            add(
                create_box(
                    f"SRC_DorsalHeatSlat_{label}_{index:02d}",
                    (side * 0.66, y, 0.762),
                    (0.34, 0.055, 0.028),
                    collection,
                    3 if index in (0, 5) else 0,
                    0.006,
                ),
                "dorsal_heat_slat",
                "thermal",
            )
        for rail_side in (-1.0, 1.0):
            add(
                create_strut(
                    f"SRC_DorsalHeatRail_{label}_{rail_side:+.0f}",
                    (side * 0.66 + rail_side * 0.20, -2.01, 0.76),
                    (side * 0.66 + rail_side * 0.20, -1.10, 0.76),
                    0.026,
                    collection,
                    3,
                ),
                "dorsal_heat_rail",
                "thermal",
            )

    top_armor_fields = (
        (
            "Forward",
            [
                (-0.52, 1.34, 0.65),
                (-1.00, 1.13, 0.58),
                (-1.15, 0.28, 0.68),
                (-0.62, 0.32, 0.745),
            ],
        ),
        (
            "Midship",
            [
                (-0.62, 0.12, 0.745),
                (-1.16, 0.08, 0.68),
                (-1.07, -0.91, 0.57),
                (-0.55, -0.84, 0.68),
            ],
        ),
    )
    bottom_armor_fields = (
        (
            "Forward",
            [
                (-0.48, 1.24, -0.67),
                (-0.98, 1.05, -0.59),
                (-1.12, 0.20, -0.68),
                (-0.58, 0.27, -0.755),
            ],
        ),
        (
            "Midship",
            [
                (-0.58, 0.06, -0.755),
                (-1.13, 0.02, -0.68),
                (-1.03, -0.78, -0.59),
                (-0.52, -0.73, -0.69),
            ],
        ),
    )
    for side, label in ((-1.0, "L"), (1.0, "R")):
        for field_name, left_points in top_armor_fields:
            points = (
                left_points
                if side < 0.0
                else [(-x, y, z) for x, y, z in reversed(left_points)]
            )
            add(
                create_surface_panel(
                    f"SRC_DorsalArmorField_{label}_{field_name}",
                    points,
                    0.026,
                    collection,
                    0,
                    0.006,
                ),
                "dorsal_armor_field",
                "primary_structure",
            )
        for field_name, left_points in bottom_armor_fields:
            points = (
                left_points
                if side < 0.0
                else [(-x, y, z) for x, y, z in reversed(left_points)]
            )
            add(
                create_surface_panel(
                    f"SRC_VentralArmorField_{label}_{field_name}",
                    points,
                    -0.026,
                    collection,
                    0,
                    0.006,
                ),
                "ventral_armor_field",
                "primary_structure",
            )
        for index, (x, y, z) in enumerate(
            ((0.66, 1.17, 0.69), (1.05, 0.42, 0.69), (0.98, -0.63, 0.61))
        ):
            add(
                create_cylinder(
                    f"SRC_DorsalArmorFastener_{label}_{index:02d}",
                    (side * x, y, z),
                    0.022,
                    0.018,
                    collection,
                    3,
                    18,
                    "Z",
                    0.003,
                ),
                "armor_fastener",
                "surface_detail",
            )
            add(
                create_cylinder(
                    f"SRC_VentralArmorFastener_{label}_{index:02d}",
                    (side * x, y, -z),
                    0.022,
                    0.018,
                    collection,
                    3,
                    18,
                    "Z",
                    0.003,
                ),
                "armor_fastener",
                "surface_detail",
            )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        # Recessed mechanical service trench.
        add(
            create_box(
                f"SRC_ServiceTrench_{label}",
                (side * 1.315, -0.18, 0.05),
                (0.10, 1.72, 0.34),
                collection,
                1,
                0.020,
            ),
            "service_trench",
                "surface_detail",
            )
        for rail_z, rail_name in ((0.235, "Upper"), (-0.235, "Lower")):
            add(
                create_strut(
                    f"SRC_ServiceLongeron{rail_name}_{label}",
                    (side * 1.39, 0.72, rail_z),
                    (side * 1.25, -1.18, rail_z),
                    0.034,
                    collection,
                    3,
                ),
                "service_longeron",
                "primary_structure",
            )
        add(
            create_cylinder(
                f"SRC_ServicePressureLine_{label}",
                (side * 1.405, -0.18, 0.08),
                0.022,
                1.58,
                collection,
                3,
                20,
                "Y",
                0.004,
            ),
            "pressure_line",
            "surface_detail",
        )
        for index, y in enumerate((0.48, 0.06, -0.36, -0.78)):
            add(
                create_box(
                    f"SRC_ServiceTrenchCover_{label}_{index:02d}",
                    (side * 1.375, y, 0.05),
                    (0.035, 0.27, 0.24),
                    collection,
                    3 if index in (0, 3) else 0,
                    0.008,
                ),
                "service_cover",
                "surface_detail",
            )
            add(
                create_strut(
                    f"SRC_ServiceRib_{label}_{index:02d}",
                    (side * 1.405, y, -0.20),
                    (side * 1.405, y, 0.20),
                    0.026,
                    collection,
                    3 if index in (0, 3) else 1,
                ),
                "service_rib",
                "surface_detail",
            )
            for z_index, z in enumerate((-0.15, 0.15)):
                add(
                    create_cylinder(
                        f"SRC_ServiceCoupler_{label}_{index:02d}_{z_index:02d}",
                        (side * 1.425, y, z),
                        0.032,
                        0.026,
                        collection,
                        3,
                        20,
                        "X",
                        0.004,
                    ),
                    "service_coupler",
                    "surface_detail",
                )
        for index, (y, z) in enumerate(
            ((1.18, 0.34), (1.18, -0.34), (-1.32, 0.31), (-1.32, -0.31))
        ):
            rcs = add(
                create_cone(
                    f"SRC_RCSNozzle_{label}_{index:02d}",
                    (
                        side
                        * (1.245 if y > 0.0 else 1.175),
                        y,
                        z,
                    ),
                    0.050,
                    0.033,
                    0.055,
                    collection,
                    1,
                    24,
                    "X",
                    0.0,
                ),
                "integrated_rcs",
                "propulsion",
            )
            if side < 0:
                rcs.rotation_euler.y += math.pi
                bpy.context.view_layer.objects.active = rcs
                rcs.select_set(True)
                bpy.ops.object.transform_apply(
                    location=False,
                    rotation=True,
                    scale=False,
                )
                rcs.select_set(False)
            add(
                create_cylinder(
                    f"SRC_RCSGlow_{label}_{index:02d}",
                    (
                        side
                        * (1.275 if y > 0.0 else 1.205),
                        y,
                        z,
                    ),
                    0.026,
                    0.012,
                    collection,
                    2,
                    24,
                    "X",
                    0.0,
                ),
                "integrated_rcs",
                "propulsion",
            )

    # Ventral systems are deliberately more mechanical than the dorsal skin:
    # a plated keel, twin utility conduits, cross braces, and service grilles
    # make the underside credible when the player mounts modules there.
    for index, (y, width) in enumerate(
        ((1.24, 0.42), (0.72, 0.50), (0.18, 0.54), (-0.38, 0.52), (-0.94, 0.46), (-1.46, 0.38))
    ):
        add(
            create_box(
                f"SRC_VentralKeelPlate_{index:02d}",
                (0.0, y, -1.075),
                (width, 0.42, 0.038),
                collection,
                1 if index % 2 else 3,
                0.010,
            ),
            "ventral_keel_plate",
            "primary_structure",
        )
        if index < 5:
            add(
                create_box(
                    f"SRC_VentralKeelSeam_{index:02d}",
                    (0.0, y - 0.235, -1.096),
                    (width * 0.86, 0.026, 0.018),
                    collection,
                    1,
                    0.004,
                ),
                "ventral_panel_seam",
                "surface_detail",
            )
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            create_cylinder(
                f"SRC_VentralUtilityConduit_{label}",
                (side * 0.25, -0.20, -1.105),
                0.025,
                2.55,
                collection,
                3,
                24,
                "Y",
                0.005,
            ),
            "ventral_utility_conduit",
            "surface_detail",
        )
        add(
            create_box(
                f"SRC_VentralRadiatorBed_{label}",
                (side * 0.58, -1.10, -0.805),
                (0.42, 0.78, 0.045),
                collection,
                1,
                0.014,
            ),
            "ventral_radiator",
            "thermal",
        )
        for index, y in enumerate((-1.40, -1.27, -1.14, -1.01, -0.88, -0.75)):
            add(
                create_box(
                    f"SRC_VentralRadiatorSlat_{label}_{index:02d}",
                    (side * 0.58, y, -0.835),
                    (0.34, 0.050, 0.024),
                    collection,
                    3 if index in (0, 5) else 0,
                    0.005,
                ),
                "ventral_radiator_slat",
                "thermal",
            )

    # Tail power frame and permanently installed cruise nozzle.
    add(
        create_box(
            "SRC_TailPowerBulkhead",
            (0.0, -2.825, 0.0),
            (1.08, 0.045, 0.70),
            collection,
            1,
            0.016,
            6,
        ),
        "tail_bulkhead",
        "propulsion",
    )
    add(
        create_box(
            "SRC_TailPowerBulkheadInset",
            (0.0, -2.865, 0.0),
            (0.76, 0.018, 0.46),
            collection,
            3,
            0.006,
            5,
        ),
        "tail_bulkhead",
        "propulsion",
    )
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            create_box(
                f"SRC_TailStatusStrip_{label}",
                (side * 0.37, -2.892, 0.0),
                (0.10, 0.012, 0.035),
                collection,
                2,
                0.005,
            ),
            "tail_status",
            "propulsion",
        )
    for index, y in enumerate((-1.70, -2.02, -2.34, -2.62)):
        add(
            create_torus(
                f"SRC_TailFrameRing_{index:02d}",
                (0.0, y, 0.0),
                0.50 - index * 0.055,
                0.035,
                collection,
                3 if index in (0, 3) else 1,
                "Y",
                56,
                14,
            ),
            "tail_frame",
            "propulsion",
        )
    add(
        create_cone(
            "SRC_IntegratedCruiseNozzle",
            (0.0, -2.78, 0.0),
            0.34,
            0.23,
            0.36,
            collection,
            1,
            64,
            "Y",
            0.012,
        ),
        "integrated_drive",
        "propulsion",
    )
    add(
        create_torus(
            "SRC_IntegratedCruiseNozzleRing",
            (0.0, -2.955, 0.0),
            0.245,
            0.034,
            collection,
            3,
            "Y",
            72,
            18,
        ),
        "integrated_drive",
        "propulsion",
    )
    add(
        create_cylinder(
            "SRC_IntegratedCruiseGlow",
            (0.0, -2.986, 0.0),
            0.205,
            0.025,
            collection,
            2,
            64,
            "Y",
            0.0,
        ),
        "integrated_drive",
        "propulsion",
    )
    for x_sign, x_label in ((-1.0, "L"), (1.0, "R")):
        for z_sign, z_label in ((-1.0, "D"), (1.0, "U")):
            add(
                create_torus(
                    f"SRC_TailTrimNozzleRing_{x_label}{z_label}",
                    (x_sign * 0.43, -2.930, z_sign * 0.24),
                    0.070,
                    0.014,
                    collection,
                    3,
                    "Y",
                    32,
                    10,
                ),
                "tail_trim_nozzle",
                "propulsion",
            )
            add(
                create_cylinder(
                    f"SRC_TailTrimNozzleGlow_{x_label}{z_label}",
                    (x_sign * 0.43, -2.948, z_sign * 0.24),
                    0.052,
                    0.018,
                    collection,
                    2,
                    32,
                    "Y",
                    0.002,
                ),
                "tail_trim_nozzle",
                "propulsion",
            )
            add(
                create_strut(
                    f"SRC_TailRadialBrace_{x_label}{z_label}",
                    (x_sign * 0.72, -2.865, z_sign * 0.42),
                    (x_sign * 0.28, -2.935, z_sign * 0.16),
                    0.035,
                    collection,
                    3,
                ),
                "tail_radial_brace",
                "primary_structure",
            )
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            create_strut(
                f"SRC_TailFrameUpper_{label}",
                (side * 0.38, -1.66, 0.36),
                (side * 0.29, -2.72, 0.25),
                0.055,
                collection,
                3,
            ),
            "tail_frame",
            "propulsion",
        )
        add(
            create_strut(
                f"SRC_TailFrameLower_{label}",
                (side * 0.38, -1.66, -0.36),
                (side * 0.29, -2.72, -0.25),
                0.055,
                collection,
                3,
            ),
            "tail_frame",
            "propulsion",
        )

    # Small panel fasteners provide scale cues without covering the clean hull.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        for index, (y, z) in enumerate(
            ((0.82, 0.53), (0.28, 0.60), (-0.34, 0.56), (-0.92, 0.46))
        ):
            add(
                create_cylinder(
                    f"SRC_ArmorFastener_{label}_{index:02d}",
                    (side * (1.18 + index * 0.025), y, z),
                    0.026,
                    0.018,
                    collection,
                    3,
                    20,
                    "X",
                    0.004,
                ),
                "armor_fastener",
                "surface_detail",
            )

    for obj in parts:
        obj.hide_set(True)
        obj.hide_render = True
    collection.hide_render = True
    return parts, hull


def create_yz_plate(
    name: str,
    points_yz: list[tuple[float, float]],
    half_thickness: float,
    collection: bpy.types.Collection,
    material_index: int = 0,
    bevel: float = 0.018,
) -> bpy.types.Object:
    count = len(points_yz)
    vertices = [(-half_thickness, y, z) for y, z in points_yz]
    vertices += [(half_thickness, y, z) for y, z in points_yz]
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
    return finish_mesh(obj, collection, material_index, bevel, 4)


def add_module_mount(
    module_id: str,
    parts: list[bpy.types.Object],
    collection: bpy.types.Collection,
    width: float,
    depth: float,
    height: float,
) -> None:
    gasket = create_box(
        f"SRC_{module_id}_MountGasket",
        (0.0, 0.0, 0.025),
        (width, depth, 0.05),
        collection,
        1,
        0.018,
    )
    pad = create_box(
        f"SRC_{module_id}_MountSaddle",
        (0.0, 0.0, 0.075),
        (width * 0.82, depth * 0.82, 0.08),
        collection,
        3,
        0.022,
    )
    parts.extend((gasket, pad))
    for x, y in (
        (-width * 0.32, -depth * 0.30),
        (width * 0.32, -depth * 0.30),
        (-width * 0.32, depth * 0.30),
        (width * 0.32, depth * 0.30),
    ):
        parts.append(
            create_strut(
                f"SRC_{module_id}_Clamp_{len(parts):02d}",
                (x, y, 0.07),
                (x * 0.72, y * 0.72, height),
                max(0.022, min(width, depth) * 0.075),
                collection,
                3,
            )
        )


def create_metadata_empty(
    name: str,
    collection: bpy.types.Collection,
    location: tuple[float, float, float],
    display_type: str,
    metadata: dict,
) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, None)
    collection.objects.link(obj)
    obj.location = location
    obj.empty_display_type = display_type
    obj.empty_display_size = 0.18
    for key, value in metadata.items():
        obj[key] = value
    obj.hide_render = True
    return obj


def build_module_source(
    spec: ModuleSpec,
    parent: bpy.types.Collection,
) -> tuple[list[bpy.types.Object], list[bpy.types.Object]]:
    collection = ensure_collection(f"MODULE_{spec.module_id}", parent)
    parts: list[bpy.types.Object] = []
    metadata: list[bpy.types.Object] = []
    module_id = spec.module_id

    if spec.kind in {"thruster", "engine_nacelle"}:
        size = {
            "ThrusterSmall": 0.72,
            "ThrusterMedium": 1.00,
            "ThrusterLarge": 1.28,
            "EngineNacelle": 1.12,
        }[module_id]
        width = 0.34 * size
        add_module_mount(
            module_id,
            parts,
            collection,
            width * 1.45,
            width * 1.45,
            0.13,
        )
        parts.append(
            create_torus(
                f"SRC_{module_id}_GimbalRing",
                (0.0, 0.0, 0.17),
                width * 0.43,
                width * 0.075,
                collection,
                3,
                "Z",
                48,
                12,
            )
        )
        body_depth = 0.55 * size
        parts.append(
            create_cone(
                f"SRC_{module_id}_Body",
                (0.0, 0.0, 0.22 + body_depth * 0.5),
                width * 0.50,
                width * 0.74,
                body_depth,
                collection,
                1,
                56,
                "Z",
                0.015,
            )
        )
        for ring_index, z in enumerate(
            (0.31, 0.22 + body_depth * 0.56, 0.22 + body_depth * 0.90)
        ):
            parts.append(
                create_torus(
                    f"SRC_{module_id}_BodyRing_{ring_index:02d}",
                    (0.0, 0.0, z),
                    width * (0.53 + ring_index * 0.08),
                    width * 0.055,
                    collection,
                    3 if ring_index != 1 else 1,
                    "Z",
                    48,
                    12,
                )
            )
        nozzle_z = 0.22 + body_depth
        parts.append(
            create_cone(
                f"SRC_{module_id}_Nozzle",
                (0.0, 0.0, nozzle_z + width * 0.17),
                width * 0.62,
                width * 0.43,
                width * 0.34,
                collection,
                1,
                56,
                "Z",
                0.010,
            )
        )
        parts.append(
            create_cylinder(
                f"SRC_{module_id}_Glow",
                (0.0, 0.0, nozzle_z + width * 0.345),
                width * 0.38,
                0.018,
                collection,
                2,
                56,
                "Z",
                0.0,
            )
        )
        for vane_index, angle in enumerate((0.0, math.pi * 0.5)):
            vane = create_box(
                f"SRC_{module_id}_NozzleVane_{vane_index:02d}",
                (0.0, 0.0, nozzle_z + width * 0.29),
                (width * 1.02, width * 0.07, width * 0.10),
                collection,
                3,
                0.007,
            )
            vane.rotation_euler.z = angle
            parts.append(vane)
        metadata.append(
            create_metadata_empty(
                f"{module_id}_ThrustVector",
                collection,
                (0.0, 0.0, nozzle_z + width * 0.36),
                "SINGLE_ARROW",
                {
                    "metadata_kind": "thrust_vector",
                    "direction_local": (0.0, 0.0, -1.0),
                    "exhaust_direction_local": (0.0, 0.0, 1.0),
                },
            )
        )
        metadata.append(
            create_metadata_empty(
                f"{module_id}_ExhaustClearance",
                collection,
                (0.0, 0.0, nozzle_z + width * 0.80),
                "CUBE",
                {
                    "metadata_kind": "exhaust_clearance",
                    "radius": width * 0.78,
                    "length": 1.2 * size,
                },
            )
        )

    elif spec.kind in {"kinetic_weapon", "energy_weapon"}:
        add_module_mount(module_id, parts, collection, 0.42, 0.38, 0.16)
        parts.append(
            create_torus(
                f"SRC_{module_id}_GimbalRing",
                (0.0, 0.0, 0.17),
                0.15,
                0.035,
                collection,
                3,
                "Z",
                48,
                12,
            )
        )
        parts.append(
            create_cylinder(
                f"SRC_{module_id}_TurretBody",
                (0.0, 0.0, 0.25),
                0.17,
                0.18,
                collection,
                1,
                48,
                "Z",
                0.018,
            )
        )
        if spec.kind == "kinetic_weapon":
            for label, x in (("L", -0.075), ("R", 0.075)):
                parts.append(
                    create_cylinder(
                        f"SRC_{module_id}_Barrel_{label}",
                        (x, 0.0, 0.62),
                        0.034,
                        0.62,
                        collection,
                        3,
                        24,
                        "Z",
                        0.006,
                    )
                )
                parts.append(
                    create_torus(
                        f"SRC_{module_id}_MuzzleRing_{label}",
                        (x, 0.0, 0.935),
                        0.035,
                        0.009,
                        collection,
                        1,
                        "Z",
                        24,
                        8,
                    )
                )
        else:
            parts.append(
                create_cone(
                    f"SRC_{module_id}_EmitterHousing",
                    (0.0, 0.0, 0.59),
                    0.12,
                    0.065,
                    0.56,
                    collection,
                    1,
                    48,
                    "Z",
                    0.010,
                )
            )
            for ring_index, z in enumerate((0.42, 0.58, 0.74, 0.88)):
                parts.append(
                    create_torus(
                        f"SRC_{module_id}_Coil_{ring_index:02d}",
                        (0.0, 0.0, z),
                        0.105 - ring_index * 0.012,
                        0.016,
                        collection,
                        2 if ring_index in (1, 2) else 3,
                        "Z",
                        36,
                        10,
                    )
                )
        metadata.append(
            create_metadata_empty(
                f"{module_id}_FireVector",
                collection,
                (0.0, 0.0, 0.96),
                "SINGLE_ARROW",
                {
                    "metadata_kind": "fire_vector",
                    "direction_local": (0.0, 0.0, 1.0),
                    "clearance_length": 4.0,
                },
            )
        )

    elif spec.kind in {"swept_wing", "delta_wing", "canard"}:
        add_module_mount(module_id, parts, collection, 0.34, 0.46, 0.11)
        points = {
            "swept_wing": [
                (-0.14, 0.24),
                (-0.98, 0.06),
                (-1.20, -0.38),
                (-0.26, -0.18),
            ],
            "delta_wing": [
                (-0.14, 0.31),
                (-1.05, -0.08),
                (-0.95, -0.47),
                (-0.22, -0.23),
            ],
            "canard": [
                (-0.12, 0.20),
                (-0.68, 0.02),
                (-0.54, -0.25),
                (-0.18, -0.14),
            ],
        }[spec.kind]
        parts.append(
            create_prism(
                f"SRC_{module_id}_Airfoil",
                points,
                0.10,
                0.18,
                collection,
                0,
                0.025,
            )
        )
        inset = [(x * 0.82, y * 0.82) for x, y in points]
        parts.append(
            create_prism(
                f"SRC_{module_id}_Inset",
                inset,
                0.18,
                0.205,
                collection,
                1,
                0.010,
            )
        )
        parts.append(
            create_strut(
                f"SRC_{module_id}_LeadingSpar",
                (points[0][0], points[0][1], 0.20),
                (points[1][0], points[1][1], 0.20),
                0.040,
                collection,
                3,
            )
        )

    elif spec.kind == "vertical_fin":
        add_module_mount(module_id, parts, collection, 0.36, 0.48, 0.13)
        parts.append(
            create_yz_plate(
                f"SRC_{module_id}_Fin",
                [
                    (0.22, 0.12),
                    (0.04, 0.92),
                    (-0.34, 0.72),
                    (-0.26, 0.12),
                ],
                0.055,
                collection,
                0,
                0.025,
            )
        )
        parts.append(
            create_yz_plate(
                f"SRC_{module_id}_FinInset",
                [
                    (0.12, 0.20),
                    (0.00, 0.72),
                    (-0.20, 0.59),
                    (-0.17, 0.20),
                ],
                0.061,
                collection,
                1,
                0.010,
            )
        )

    elif spec.kind == "radiator":
        add_module_mount(module_id, parts, collection, 0.46, 0.48, 0.15)
        parts.append(
            create_box(
                f"SRC_{module_id}_Panel",
                (0.0, 0.0, 0.23),
                (0.74, 0.84, 0.055),
                collection,
                1,
                0.022,
            )
        )
        for index, x in enumerate((-0.28, -0.14, 0.0, 0.14, 0.28)):
            parts.append(
                create_box(
                    f"SRC_{module_id}_CoolingChannel_{index:02d}",
                    (x, 0.0, 0.268),
                    (0.035, 0.70, 0.025),
                    collection,
                    2 if index == 2 else 3,
                    0.006,
                )
            )

    elif spec.kind == "sensor":
        add_module_mount(module_id, parts, collection, 0.38, 0.38, 0.14)
        parts.append(
            create_cylinder(
                f"SRC_{module_id}_Mast",
                (0.0, 0.0, 0.43),
                0.075,
                0.58,
                collection,
                3,
                32,
                "Z",
                0.010,
            )
        )
        for index, z in enumerate((0.24, 0.46, 0.68)):
            parts.append(
                create_torus(
                    f"SRC_{module_id}_SensorRing_{index:02d}",
                    (0.0, 0.0, z),
                    0.10 + index * 0.025,
                    0.018,
                    collection,
                    2 if index == 2 else 1,
                    "Z",
                    36,
                    10,
                )
            )
        parts.append(
            create_cylinder(
                f"SRC_{module_id}_SensorCap",
                (0.0, 0.0, 0.78),
                0.17,
                0.16,
                collection,
                1,
                40,
                "Z",
                0.018,
            )
        )

    elif spec.kind == "armor_fairing":
        add_module_mount(module_id, parts, collection, 0.52, 0.66, 0.12)
        parts.append(
            create_smooth_loft(
                f"SRC_{module_id}_Shell",
                [
                    (0.34, 0.09, 0.07, 0.15, 2.0),
                    (0.12, 0.24, 0.14, 0.20, 2.3),
                    (-0.22, 0.27, 0.16, 0.21, 2.5),
                    (-0.36, 0.18, 0.10, 0.18, 2.2),
                ],
                collection,
                0,
                32,
                2,
                2,
            )
        )
        parts.append(
            create_box(
                f"SRC_{module_id}_AccessPanel",
                (0.0, -0.05, 0.38),
                (0.26, 0.30, 0.035),
                collection,
                3,
                0.010,
            )
        )

    else:
        raise RuntimeError(f"Unknown module kind: {spec.kind}")

    mount = create_metadata_empty(
        f"{module_id}_MountPlane",
        collection,
        (0.0, 0.0, 0.0),
        "CIRCLE",
        {
            "metadata_kind": "mount_plane",
            "mount_normal_local": (0.0, 0.0, 1.0),
            "surface_conforming": True,
            "fixed_hardpoint": False,
        },
    )
    metadata.append(mount)
    for obj in parts:
        tag_source(obj, spec.kind, module_id)
        obj["module_id"] = module_id
        obj["mount_normal_local"] = (0.0, 0.0, 1.0)
        obj.hide_set(True)
        obj.hide_render = True
    for obj in metadata:
        obj.hide_set(True)
    collection.hide_render = True
    return parts, metadata


def build_module_library(
    parent: bpy.types.Collection,
) -> tuple[
    dict[str, list[bpy.types.Object]],
    dict[str, list[bpy.types.Object]],
]:
    modules: dict[str, list[bpy.types.Object]] = {}
    metadata: dict[str, list[bpy.types.Object]] = {}
    for spec in MODULE_SPECS:
        parts, markers = build_module_source(spec, parent)
        modules[spec.module_id] = parts
        metadata[spec.module_id] = markers
    parent.hide_render = True
    return modules, metadata


def build_hull_export(
    source_parts: list[bpy.types.Object],
    primary_shell: bpy.types.Object,
    output_root: Path,
) -> tuple[dict, list[bpy.types.Object]]:
    collection = ensure_collection("90_Export_Contract")
    clones = [
        duplicate_object(
            obj,
            f"EXPORT_{obj.name.removeprefix('SRC_')}",
            collection,
        )
        for obj in source_parts
    ]
    lod0 = pipeline.join_geometry(clones, "Render_LOD0")
    move_to_collection(lod0, collection)
    pipeline.fit_hull_bounds(lod0, DIMENSIONS)
    pipeline.cube_uv(lod0)
    pipeline.decimate_to(lod0, HULL_BUDGETS["lod0Triangles"])

    lod0_count = pipeline.triangle_count(lod0)
    lod1 = pipeline.duplicate_mesh(lod0, "Render_LOD1")
    move_to_collection(lod1, collection)
    pipeline.decimate_to(
        lod1,
        min(
            HULL_BUDGETS["lod1Triangles"],
            max(16000, int(lod0_count * 0.46)),
        ),
    )
    lod2 = pipeline.duplicate_mesh(lod0, "Render_LOD2")
    move_to_collection(lod2, collection)
    pipeline.decimate_to(
        lod2,
        min(
            HULL_BUDGETS["lod2Triangles"],
            max(6000, int(lod0_count * 0.115)),
        ),
    )
    collision = pipeline.convex_collision(
        lod2,
        HULL_BUDGETS["collisionTriangles"],
    )
    move_to_collection(collision, collection)

    placement = duplicate_object(
        primary_shell,
        "PlacementSurface",
        collection,
    )
    pipeline.apply_hard_surface_shading(placement)
    pipeline.cube_uv(placement)
    pipeline.decimate_to(placement, HULL_BUDGETS["placementTriangles"])
    placement["placement_surface"] = True
    placement["fixed_hardpoints"] = False
    placement["surface_grid_hint"] = 0.25

    budgets = {
        key: HULL_BUDGETS[key]
        for key in (
            "lod0Triangles",
            "lod1Triangles",
            "lod2Triangles",
            "collisionTriangles",
            "dimensionTolerance",
        )
    }
    errors, counts, actual = pipeline.validate_mesh_set(
        lod0,
        lod1,
        lod2,
        collision,
        DIMENSIONS,
        budgets,
    )
    if counts["lod0"] < HULL_BUDGETS["minimumLod0Triangles"]:
        errors.append(
            f"LOD0 triangles {counts['lod0']} are below 120000 high-detail target"
        )
    if not counts["lod0"] > counts["lod1"] > counts["lod2"]:
        errors.append("LOD triangle counts are not strictly decreasing")
    for index, label in enumerate(("width", "height", "length")):
        if abs(actual[index] - DIMENSIONS[index]) > 0.01:
            errors.append(
                f"{label} absolute error exceeds 0.01m: "
                f"{actual[index]:.4f} vs {DIMENSIONS[index]:.4f}"
            )

    topology = strict_mesh_checks(lod0)
    if topology["nonManifoldEdges"] or topology["degenerateFaces"]:
        errors.append(f"LOD0 topology check failed: {topology}")
    placement_topology = strict_mesh_checks(placement)
    if (
        placement_topology["nonManifoldEdges"]
        or placement_topology["degenerateFaces"]
    ):
        errors.append(
            f"PlacementSurface topology check failed: {placement_topology}"
        )

    fbx_path = output_root / "Hulls" / f"{ASSET_ID}.fbx"
    preview_path = output_root / "Thumbnails" / f"{ASSET_ID}.png"
    export_objects = [lod0, lod1, lod2, collision, placement]
    pipeline.export_fbx(export_objects, fbx_path)

    render_states = {
        obj.name: obj.hide_render for obj in bpy.context.scene.objects
    }
    for obj in bpy.context.scene.objects:
        obj.hide_render = obj != lod0
    original_rotation = lod0.rotation_euler.copy()
    lod0.rotation_euler.z += math.pi
    pipeline.render_preview(lod0, preview_path, 640)
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
            "placementTriangles": pipeline.triangle_count(placement),
            "actualDimensions": actual,
            "topology": topology,
            "placementTopology": placement_topology,
            "geometryHash": pipeline.geometry_hash(export_objects),
            "materialSlots": [
                slot.material.name for slot in lod0.material_slots
            ],
            "errors": errors,
        },
        export_objects,
    )


def module_contract_metadata(spec: ModuleSpec) -> dict:
    result = {
        "module_id": spec.module_id,
        "module_kind": spec.kind,
        "mount_plane_origin": (0.0, 0.0, 0.0),
        "mount_plane_normal": (0.0, 0.0, 1.0),
        "surface_conforming": True,
        "fixed_hardpoint": False,
    }
    if spec.kind in {"thruster", "engine_nacelle"}:
        result["thrust_direction_local"] = (0.0, 0.0, -1.0)
        result["exhaust_direction_local"] = (0.0, 0.0, 1.0)
    if spec.kind in {"kinetic_weapon", "energy_weapon"}:
        result["fire_direction_local"] = (0.0, 0.0, 1.0)
    return result


def build_module_exports(
    module_sources: dict[str, list[bpy.types.Object]],
    output_root: Path,
) -> dict[str, dict]:
    results: dict[str, dict] = {}
    for spec in MODULE_SPECS:
        module_id = spec.module_id
        collection = ensure_collection(f"91_Export_{module_id}")
        clones = [
            duplicate_object(
                obj,
                f"EXPORT_{module_id}_{obj.name.removeprefix('SRC_')}",
                collection,
            )
            for obj in module_sources[module_id]
        ]
        lod0 = pipeline.join_geometry(clones, "Render_LOD0")
        move_to_collection(lod0, collection)
        pipeline.cube_uv(lod0)
        for key, value in module_contract_metadata(spec).items():
            lod0[key] = value
        count0 = pipeline.triangle_count(lod0)

        lod1 = pipeline.duplicate_mesh(lod0, "Render_LOD1")
        move_to_collection(lod1, collection)
        pipeline.decimate_to(lod1, max(96, int(count0 * 0.55)))
        lod2 = pipeline.duplicate_mesh(lod0, "Render_LOD2")
        move_to_collection(lod2, collection)
        pipeline.decimate_to(lod2, max(48, int(count0 * 0.25)))
        collision = pipeline.convex_collision(lod2, 96)
        move_to_collection(collision, collection)
        objects = [lod0, lod1, lod2, collision]

        counts = {
            "lod0": pipeline.triangle_count(lod0),
            "lod1": pipeline.triangle_count(lod1),
            "lod2": pipeline.triangle_count(lod2),
            "collision": pipeline.triangle_count(collision),
        }
        errors: list[str] = []
        if not counts["lod0"] > counts["lod1"] > counts["lod2"]:
            errors.append("LOD triangle counts are not strictly decreasing")
        if counts["collision"] > 96:
            errors.append("collision exceeds 96 triangles")
        topology = strict_mesh_checks(lod0)
        if topology["nonManifoldEdges"] or topology["degenerateFaces"]:
            errors.append(f"topology check failed: {topology}")
        if [slot.material.name for slot in lod0.material_slots] != list(
            MATERIAL_NAMES
        ):
            errors.append("material slots do not match Unity contract")

        fbx_path = output_root / "Modules" / f"{module_id}.fbx"
        preview_path = output_root / "Thumbnails" / f"{module_id}.png"
        pipeline.export_fbx(objects, fbx_path)
        render_states = {
            obj.name: obj.hide_render for obj in bpy.context.scene.objects
        }
        for obj in bpy.context.scene.objects:
            obj.hide_render = obj != lod0
        pipeline.render_preview(lod0, preview_path, 384)
        for obj in bpy.context.scene.objects:
            if obj.name in render_states:
                obj.hide_render = render_states[obj.name]
        for obj in objects:
            obj.hide_set(True)
            obj.hide_render = True
        collection.hide_render = True

        results[module_id] = {
            "kind": spec.kind,
            "fbx": str(fbx_path),
            "thumbnail": str(preview_path),
            "triangles": counts,
            "topology": topology,
            "geometryHash": pipeline.geometry_hash(objects),
            "metadata": module_contract_metadata(spec),
            "errors": errors,
        }
    return results


def duplicate_group(
    sources: list[bpy.types.Object],
    prefix: str,
    collection: bpy.types.Collection,
    parent: bpy.types.Object | None = None,
) -> list[bpy.types.Object]:
    clones: list[bpy.types.Object] = []
    for source in sources:
        clone = duplicate_object(
            source,
            f"{prefix}_{source.name.removeprefix('SRC_')}",
            collection,
        )
        if parent is not None:
            clone.parent = parent
        clones.append(clone)
    return clones


def create_group_root(
    name: str,
    collection: bpy.types.Collection,
    location: tuple[float, float, float],
    normal: tuple[float, float, float] = (0.0, 0.0, 1.0),
    twist_degrees: float = 0.0,
) -> bpy.types.Object:
    root = bpy.data.objects.new(name, None)
    collection.objects.link(root)
    root.location = location
    normal_vector = Vector(normal).normalized()
    root.rotation_euler = Vector((0.0, 0.0, 1.0)).rotation_difference(
        normal_vector
    ).to_euler()
    root.rotation_euler.rotate_axis(
        "Z",
        math.radians(twist_degrees),
    )
    return root


def place_module(
    module_id: str,
    sources: dict[str, list[bpy.types.Object]],
    collection: bpy.types.Collection,
    location: tuple[float, float, float],
    normal: tuple[float, float, float],
    twist_degrees: float = 0.0,
    scale: float = 1.0,
) -> list[bpy.types.Object]:
    root = create_group_root(
        f"{collection.name}_{module_id}_Root",
        collection,
        location,
        normal,
        twist_degrees,
    )
    root.scale = (scale, scale, scale)
    clones = duplicate_group(
        sources[module_id],
        f"{collection.name}_{module_id}",
        collection,
        root,
    )
    return clones


def add_label(
    collection: bpy.types.Collection,
    text: str,
    location: tuple[float, float, float],
    size: float = 0.28,
) -> bpy.types.Object:
    curve = bpy.data.curves.new(text + "_Curve", "FONT")
    curve.body = text
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.size = size
    curve.extrude = 0.006
    obj = bpy.data.objects.new(text + "_Label", curve)
    collection.objects.link(obj)
    obj.location = location
    return obj


def build_review_scene(
    hull_sources: list[bpy.types.Object],
    primary_shell: bpy.types.Object,
    module_sources: dict[str, list[bpy.types.Object]],
) -> list[bpy.types.Object]:
    visible: list[bpy.types.Object] = []

    hero_collection = ensure_collection("10_Hero_Unloaded_Hull")
    hero = duplicate_group(
        hull_sources,
        "HERO",
        hero_collection,
    )
    visible.extend(hero)

    placement_collection = ensure_collection("15_Placement_Surface")
    placement = duplicate_object(
        primary_shell,
        "PlacementSurface_Review",
        placement_collection,
    )
    pipeline.apply_hard_surface_shading(placement)
    placement.display_type = "WIRE"
    placement.color = (0.05, 0.85, 1.0, 0.28)
    placement.show_in_front = True
    placement["placement_surface"] = True
    placement["fixed_hardpoints"] = False
    placement_collection.hide_viewport = True
    placement_collection.hide_render = True

    catalog_collection = ensure_collection("20_Module_Library")
    for index, spec in enumerate(MODULE_SPECS):
        column = index % 5
        row = index // 5
        x = -3.6 + column * 1.8
        y = 1.2 - row * 2.0
        place_module(
            spec.module_id,
            module_sources,
            catalog_collection,
            (x, y, 0.0),
            (0.0, 0.0, 1.0),
            0.0,
            0.88,
        )
        add_label(
            catalog_collection,
            spec.module_id,
            (x, y - 0.72, 0.02),
            0.20,
        )
    catalog_collection.hide_viewport = True
    catalog_collection.hide_render = True

    balanced_collection = ensure_collection("30_Balanced_Free_Build")
    duplicate_group(
        hull_sources,
        "BALANCED",
        balanced_collection,
    )
    for module_id, location, normal, twist, scale in (
        (
            "ThrusterLarge",
            (-0.57, -2.64, 0.02),
            (0.0, -1.0, 0.0),
            0.0,
            0.82,
        ),
        (
            "ThrusterLarge",
            (0.57, -2.64, 0.02),
            (0.0, -1.0, 0.0),
            0.0,
            0.82,
        ),
        (
            "KineticRepeater",
            (-0.54, 0.64, 0.91),
            (0.0, 0.0, 1.0),
            0.0,
            0.72,
        ),
        (
            "EnergyPulse",
            (0.54, 0.64, 0.91),
            (0.0, 0.0, 1.0),
            0.0,
            0.72,
        ),
        (
            "SweptWing",
            (-1.35, -0.02, 0.0),
            (-1.0, 0.0, 0.0),
            -90.0,
            0.82,
        ),
        (
            "SweptWing",
            (1.35, -0.02, 0.0),
            (1.0, 0.0, 0.0),
            90.0,
            0.82,
        ),
        (
            "SensorMast",
            (0.0, -0.36, 1.00),
            (0.0, 0.0, 1.0),
            0.0,
            0.72,
        ),
        (
            "Radiator",
            (0.0, -0.44, -0.97),
            (0.0, 0.0, -1.0),
            0.0,
            0.70,
        ),
    ):
        place_module(
            module_id,
            module_sources,
            balanced_collection,
            location,
            normal,
            twist,
            scale,
        )
    balanced_collection.hide_viewport = True
    balanced_collection.hide_render = True

    asymmetric_collection = ensure_collection("40_Asymmetric_Free_Build")
    duplicate_group(
        hull_sources,
        "ASYMMETRIC",
        asymmetric_collection,
    )
    for module_id, location, normal, twist, scale in (
        (
            "EngineNacelle",
            (-0.62, -2.62, 0.05),
            (0.0, -1.0, 0.0),
            0.0,
            0.82,
        ),
        (
            "ThrusterMedium",
            (0.60, -2.62, 0.06),
            (0.0, -1.0, 0.0),
            0.0,
            0.82,
        ),
        (
            "DeltaWing",
            (-1.36, 0.04, -0.02),
            (-1.0, 0.0, 0.0),
            -90.0,
            0.90,
        ),
        (
            "Canard",
            (1.28, 1.18, -0.02),
            (1.0, 0.0, 0.0),
            90.0,
            0.88,
        ),
        (
            "ArmorFairing",
            (0.48, -0.52, 0.94),
            (0.0, 0.0, 1.0),
            -12.0,
            0.84,
        ),
        (
            "Radiator",
            (-0.58, -0.55, -0.90),
            (0.0, 0.0, -1.0),
            18.0,
            0.72,
        ),
        (
            "VerticalFin",
            (0.0, -1.18, 0.78),
            (0.0, 0.0, 1.0),
            0.0,
            0.70,
        ),
    ):
        place_module(
            module_id,
            module_sources,
            asymmetric_collection,
            location,
            normal,
            twist,
            scale,
        )
    asymmetric_collection.hide_viewport = True
    asymmetric_collection.hide_render = True

    environment = ensure_collection("99_Review_Environment")
    floor = create_box(
        "ReviewFloor",
        (0.0, 0.0, -1.52),
        (10.5, 11.0, 0.08),
        environment,
        1,
        0.012,
    )
    floor.hide_render = False
    visible.append(floor)

    scene = bpy.context.scene
    scene.name = "Flagship Arrowhead V2 Review"
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.world = scene.world or bpy.data.worlds.new("FlagshipReviewWorld")
    scene.world.color = (0.012, 0.018, 0.030)
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene["asset_id"] = ASSET_ID
    scene["review_mode"] = "unloaded_hull_default"
    scene["free_surface_placement"] = True
    scene["fixed_hardpoints"] = False

    camera_data = bpy.data.cameras.new("ReviewCamera")
    camera = bpy.data.objects.new("ReviewCamera", camera_data)
    environment.objects.link(camera)
    camera.location = (8.2, 9.8, 6.9)
    target = Vector((0.0, 0.05, 0.0))
    camera.rotation_euler = (target - camera.location).to_track_quat(
        "-Z",
        "Y",
    ).to_euler()
    camera_data.lens = 62.0
    camera_data.clip_end = 1000.0
    scene.camera = camera

    camera_specs = (
        ("Camera_Top", (0.0, 0.0, 10.0)),
        ("Camera_Bottom", (0.0, 0.0, -10.0)),
        ("Camera_Rear", (0.0, -10.0, 0.5)),
        ("Camera_Front", (0.0, 10.0, 0.5)),
    )
    for name, location in camera_specs:
        data = bpy.data.cameras.new(name)
        audit_camera = bpy.data.objects.new(name, data)
        environment.objects.link(audit_camera)
        audit_camera.location = location
        audit_camera.rotation_euler = (
            target - audit_camera.location
        ).to_track_quat("-Z", "Y").to_euler()
        data.lens = 62.0
        data.clip_end = 1000.0

    for name, location, energy, color, size in (
        (
            "ReviewKey",
            (4.8, 5.2, 7.5),
            1450.0,
            (0.78, 0.88, 1.0),
            5.0,
        ),
        (
            "ReviewFill",
            (-5.8, 2.0, 3.6),
            980.0,
            (0.34, 0.56, 1.0),
            4.0,
        ),
        (
            "ReviewRim",
            (1.5, -6.8, 4.8),
            1250.0,
            (1.0, 0.44, 0.20),
            3.0,
        ),
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

    return visible


def save_review(
    review_file: Path,
) -> None:
    review_file.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(review_file))


def main() -> None:
    args = parse_args()
    output_root = Path(args.output_root).resolve()
    review_file = Path(args.review_file).resolve()
    report_path = (
        Path(args.report).resolve()
        if args.report
        else output_root / "flagship_report.json"
    )

    pipeline.clean_scene()
    source_collection = ensure_collection("00_Source_Hull")
    module_collection = ensure_collection("01_Source_Modules")
    hull_sources, primary_shell = build_hull_source(source_collection)
    module_sources, module_metadata = build_module_library(module_collection)

    hull_result, _ = build_hull_export(
        hull_sources,
        primary_shell,
        output_root,
    )
    module_results = build_module_exports(
        module_sources,
        output_root,
    )
    build_review_scene(
        hull_sources,
        primary_shell,
        module_sources,
    )

    module_errors = [
        f"{module_id}: {error}"
        for module_id, result in module_results.items()
        for error in result["errors"]
    ]
    errors = list(hull_result["errors"]) + module_errors
    report = {
        "success": not errors,
        "assetId": ASSET_ID,
        "seed": args.seed,
        "blenderVersion": bpy.app.version_string,
        "dimensions": DIMENSIONS,
        "forwardAxis": "+Y",
        "unityForwardAxis": "+Z",
        "artDirection": "industrial-hard-sci-fi",
        "silhouette": "arrowhead",
        "cockpit": "armored-closed",
        "fixedHardpoints": False,
        "freeSurfacePlacement": True,
        "surfaceGridHint": 0.25,
        "materials": MATERIAL_NAMES,
        "hull": hull_result,
        "modules": module_results,
        "moduleMetadataObjects": {
            module_id: [obj.name for obj in markers]
            for module_id, markers in module_metadata.items()
        },
        "reviewFile": str(review_file),
        "errors": errors,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(
        json.dumps(report, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    if errors:
        raise RuntimeError(
            "Flagship v2 validation failed:\n" + "\n".join(errors)
        )

    save_review(review_file)
    print(
        f"[SpaceshipPCG] {ASSET_ID} generated: "
        f"{hull_result['triangles']} "
        f"hash={hull_result['geometryHash']}"
    )
    print(
        f"[SpaceshipPCG] modules: {len(module_results)} "
        f"review={review_file}"
    )
    print(f"[SpaceshipPCG] report: {report_path}")


if __name__ == "__main__":
    main()
