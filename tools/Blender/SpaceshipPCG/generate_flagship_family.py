"""Generate three high-detail free-placement flagship-family hulls.

The script writes only to Library/.  It shares the approved arrowhead material,
module, validation, LOD, collision, and metadata contracts.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parents[2]
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import generate_flagship_v2 as base
import flagship_family_common as family
from flagship_contract import MATERIAL_NAMES


FAMILY_ID = "flagship_family_v1"
DEFAULT_OUTPUT_ROOT = (
    PROJECT_ROOT / "Library" / "SpaceshipPCGStaging" / FAMILY_ID
)
DEFAULT_REVIEW_ROOT = PROJECT_ROOT / "Library" / "SpaceshipPCGReview"


def parse_args() -> argparse.Namespace:
    argv = sys.argv
    argv = argv[argv.index("--") + 1 :] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--archetype",
        choices=("all", "saucer", "sailer", "fighter"),
        default="all",
    )
    parser.add_argument("--output-root", default=str(DEFAULT_OUTPUT_ROOT))
    parser.add_argument("--review-root", default=str(DEFAULT_REVIEW_ROOT))
    parser.add_argument("--report", default="")
    return parser.parse_args(argv)


def _apply_rotation(obj: bpy.types.Object) -> None:
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(
        location=False,
        rotation=True,
        scale=False,
    )
    obj.select_set(False)


def _finish_builder(
    parts: list[bpy.types.Object],
    collection: bpy.types.Collection,
) -> None:
    family.hide_sources(parts, collection)


def build_saucer(
    collection: bpy.types.Collection,
    spec: family.HullSpec,
) -> tuple[list[bpy.types.Object], list[bpy.types.Object]]:
    parts: list[bpy.types.Object] = []
    placement: list[bpy.types.Object] = []

    def add(
        obj: bpy.types.Object,
        role: str,
        group: str,
        placeable: bool = False,
    ) -> bpy.types.Object:
        family.tag_source(obj, spec, role, group)
        parts.append(obj)
        if placeable:
            placement.append(obj)
        return obj

    shell = add(
        base.create_smooth_loft(
            "SRC_SaucerContinuousLens",
            [
                (2.30, 0.06, 0.05, -0.02, 2.0),
                (2.15, 0.78, 0.12, -0.03, 2.6),
                (1.84, 1.60, 0.24, -0.02, 3.2),
                (1.34, 2.18, 0.33, 0.00, 3.8),
                (0.70, 2.49, 0.38, 0.00, 4.2),
                (0.00, 2.60, 0.40, 0.00, 4.5),
                (-0.72, 2.48, 0.38, 0.00, 4.2),
                (-1.34, 2.16, 0.32, 0.00, 3.8),
                (-1.82, 1.58, 0.24, 0.00, 3.3),
                (-2.06, 0.94, 0.17, 0.00, 2.8),
            ],
            collection,
            0,
            72,
            2,
            4,
        ),
        "continuous_lenticular_hull",
        "primary_structure",
        True,
    )
    shell["continuous_build_surface"] = True

    add(
        base.create_smooth_loft(
            "SRC_SaucerArmoredCommandCapsule",
            [
                (2.28, 0.07, 0.030, 0.24, 2.0),
                (2.04, 0.30, 0.075, 0.34, 2.7),
                (1.56, 0.48, 0.115, 0.42, 3.1),
                (0.82, 0.58, 0.140, 0.47, 3.4),
                (0.02, 0.55, 0.120, 0.45, 3.3),
                (-0.78, 0.45, 0.090, 0.38, 3.0),
                (-1.35, 0.28, 0.055, 0.28, 2.6),
            ],
            collection,
            1,
            56,
            2,
            3,
        ),
        "armored_command_capsule",
        "cockpit",
    )
    add(
        base.create_smooth_loft(
            "SRC_SaucerVentralPowerCore",
            [
                (1.02, 0.10, 0.035, -0.34, 2.0),
                (0.72, 0.54, 0.10, -0.44, 2.8),
                (0.12, 0.88, 0.15, -0.52, 3.4),
                (-0.58, 0.78, 0.13, -0.49, 3.2),
                (-1.02, 0.46, 0.08, -0.40, 2.7),
            ],
            collection,
            1,
            52,
            2,
            3,
        ),
        "ventral_power_core",
        "primary_structure",
    )

    add(
        base.create_torus(
            "SRC_SaucerPrimaryMaintenanceRing",
            (0.0, 0.0, 0.0),
            2.14,
            0.070,
            collection,
            1,
            "Z",
            96,
            18,
        ),
        "maintenance_ring",
        "primary_structure",
    )
    add(
        base.create_torus(
            "SRC_SaucerAccentRingUpper",
            (0.0, 0.0, 0.36),
            1.72,
            0.028,
            collection,
            3,
            "Z",
            96,
            14,
        ),
        "maintenance_ring",
        "surface_detail",
    )
    add(
        base.create_torus(
            "SRC_SaucerAccentRingLower",
            (0.0, 0.0, -0.36),
            1.72,
            0.028,
            collection,
            3,
            "Z",
            96,
            14,
        ),
        "maintenance_ring",
        "surface_detail",
    )

    for index in range(16):
        angle = (2.0 * math.pi * index) / 16.0
        x = math.cos(angle) * 2.23
        y = math.sin(angle) * 1.83
        segment = base.create_box(
            f"SRC_SaucerRimSegment_{index:02d}",
            (x, y, 0.0),
            (0.43, 0.14, 0.20),
            collection,
            3 if index in (2, 6, 10, 14) else 1,
            0.018,
        )
        segment.rotation_euler.z = angle + math.pi * 0.5
        _apply_rotation(segment)
        add(segment, "rim_armor_segment", "surface_detail")

    for index in range(12):
        angle = (2.0 * math.pi * index) / 12.0
        x = math.cos(angle) * 1.46
        y = math.sin(angle) * 1.12
        panel = base.create_box(
            f"SRC_SaucerDorsalRingPanel_{index:02d}",
            (x, y, 0.425),
            (0.40, 0.095, 0.034),
            collection,
            3 if index in (0, 3, 6, 9) else 1,
            0.007,
        )
        panel.rotation_euler.z = angle + math.pi * 0.5
        _apply_rotation(panel)
        add(panel, "dorsal_ring_panel", "surface_detail")

    # Large upper/lower armor fields remain broad enough for free placement.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        top_points = [
            (side * 0.72, 1.17, 0.39),
            (side * 1.72, 0.93, 0.34),
            (side * 1.92, 0.05, 0.35),
            (side * 0.88, 0.16, 0.43),
        ]
        bottom_points = [(x, y, -z) for x, y, z in reversed(top_points)]
        add(
            base.create_surface_panel(
                f"SRC_SaucerUpperArmorField_{label}",
                top_points,
                0.024,
                collection,
                0,
                0.006,
            ),
            "upper_armor_field",
            "primary_structure",
        )
        add(
            base.create_surface_panel(
                f"SRC_SaucerLowerArmorField_{label}",
                bottom_points,
                -0.024,
                collection,
                0,
                0.006,
            ),
            "lower_armor_field",
            "primary_structure",
        )
        for row, y in enumerate((0.56, 0.22, -0.12, -0.46)):
            add(
                base.create_box(
                    f"SRC_SaucerThermalSlat_{label}_{row:02d}",
                    (side * 1.75, y, 0.37),
                    (0.42, 0.08, 0.032),
                    collection,
                    1 if row not in (0, 3) else 3,
                    0.006,
                ),
                "thermal_slat",
                "thermal",
            )

    for index, (width, y, z) in enumerate(
        ((0.52, 1.48, 0.47), (0.78, 1.18, 0.57), (0.92, 0.82, 0.62))
    ):
        add(
            base.create_box(
                f"SRC_SaucerSensorSlit_{index:02d}",
                (0.0, y, z),
                (width, 0.060, 0.045),
                collection,
                2 if index == 1 else 1,
                0.010,
            ),
            "armored_sensor_slit",
            "cockpit",
        )

    # Three permanent cruise drives establish a clear rear direction.
    for index, x in enumerate((-0.92, 0.0, 0.92)):
        add(
            base.create_box(
                f"SRC_SaucerDriveHousing_{index:02d}",
                (x, -2.01, 0.0),
                (0.66, 0.30, 0.42),
                collection,
                1,
                0.055,
                6,
            ),
            "integrated_drive_housing",
            "propulsion",
        )
        add(
            base.create_torus(
                f"SRC_SaucerDriveRing_{index:02d}",
                (x, -2.20, 0.0),
                0.18,
                0.032,
                collection,
                3,
                "Y",
                56,
                14,
            ),
            "integrated_drive",
            "propulsion",
        )
        add(
            base.create_cylinder(
                f"SRC_SaucerDriveGlow_{index:02d}",
                (x, -2.23, 0.0),
                0.145,
                0.025,
                collection,
                2,
                56,
                "Y",
                0.003,
            ),
            "integrated_drive",
            "propulsion",
        )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        for y_index, y in enumerate((0.92, -0.92)):
            nozzle = base.create_cone(
                f"SRC_SaucerRCS_{label}_{y_index:02d}",
                (side * 2.43, y, 0.0),
                0.060,
                0.038,
                0.075,
                collection,
                1,
                28,
                "X",
                0.004,
            )
            if side < 0.0:
                nozzle.rotation_euler.y += math.pi
                _apply_rotation(nozzle)
            add(nozzle, "integrated_rcs", "propulsion")
            add(
                base.create_cylinder(
                    f"SRC_SaucerRCSGlow_{label}_{y_index:02d}",
                    (side * 2.48, y, 0.0),
                    0.030,
                    0.016,
                    collection,
                    2,
                    24,
                    "X",
                    0.002,
                ),
                "integrated_rcs",
                "propulsion",
            )

    _finish_builder(parts, collection)
    return parts, placement


def build_cutter(
    collection: bpy.types.Collection,
    spec: family.HullSpec,
) -> tuple[list[bpy.types.Object], list[bpy.types.Object]]:
    parts: list[bpy.types.Object] = []
    placement: list[bpy.types.Object] = []

    def add(
        obj: bpy.types.Object,
        role: str,
        group: str,
        placeable: bool = False,
    ) -> bpy.types.Object:
        family.tag_source(obj, spec, role, group)
        parts.append(obj)
        if placeable:
            placement.append(obj)
        return obj

    shell = add(
        base.create_smooth_loft(
            "SRC_CutterContinuousHull",
            [
                (3.50, 0.04, 0.05, -0.05, 2.0),
                (3.24, 0.30, 0.22, -0.08, 2.7),
                (2.78, 0.68, 0.43, -0.10, 3.2),
                (2.12, 1.06, 0.68, -0.09, 3.6),
                (1.28, 1.34, 0.91, -0.05, 4.0),
                (0.28, 1.48, 1.00, 0.00, 4.2),
                (-0.78, 1.45, 0.96, 0.00, 4.1),
                (-1.72, 1.30, 0.84, -0.02, 3.9),
                (-2.50, 1.06, 0.67, -0.03, 3.5),
                (-3.02, 0.82, 0.56, -0.04, 3.2),
            ],
            collection,
            0,
            68,
            2,
            4,
        ),
        "continuous_cutter_hull",
        "primary_structure",
        True,
    )
    shell["continuous_build_surface"] = True

    add(
        base.create_smooth_loft(
            "SRC_CutterArmoredBridge",
            [
                (1.84, 0.08, 0.05, 0.63, 2.0),
                (1.54, 0.42, 0.16, 0.77, 2.7),
                (1.05, 0.64, 0.22, 0.91, 3.2),
                (0.42, 0.69, 0.23, 0.96, 3.4),
                (-0.28, 0.61, 0.19, 0.91, 3.2),
                (-0.72, 0.44, 0.10, 0.78, 2.7),
            ],
            collection,
            1,
            56,
            2,
            3,
        ),
        "low_armored_bridge",
        "cockpit",
    )

    add(
        base.create_prism(
            "SRC_CutterVentralKeel",
            [
                (-0.24, 2.18),
                (-0.34, 0.85),
                (-0.36, -2.35),
                (-0.25, -3.02),
                (0.25, -3.02),
                (0.36, -2.35),
                (0.34, 0.85),
                (0.24, 2.18),
            ],
            -1.18,
            -0.93,
            collection,
            1,
            0.030,
        ),
        "deep_ventral_keel",
        "primary_structure",
        True,
    )

    # A continuous gunwale/service band gives the cutter a boat-like read.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            base.create_strut(
                f"SRC_CutterGunwaleUpper_{label}",
                (side * 1.14, 1.76, 0.42),
                (side * 1.25, -2.34, 0.31),
                0.065,
                collection,
                3,
            ),
            "gunwale_longeron",
            "primary_structure",
        )
        add(
            base.create_strut(
                f"SRC_CutterGunwaleLower_{label}",
                (side * 1.10, 1.68, -0.38),
                (side * 1.20, -2.34, -0.32),
                0.055,
                collection,
                3,
            ),
            "gunwale_longeron",
            "primary_structure",
        )
        add(
            base.create_box(
                f"SRC_CutterServiceBand_{label}",
                (side * 1.33, -0.28, 0.02),
                (0.10, 3.10, 0.44),
                collection,
                1,
                0.020,
            ),
            "service_band",
            "surface_detail",
        )
        add(
            base.create_cylinder(
                f"SRC_CutterPressureLine_{label}",
                (side * 1.39, -0.26, 0.10),
                0.025,
                2.82,
                collection,
                3,
                22,
                "Y",
                0.004,
            ),
            "pressure_line",
            "surface_detail",
        )
        for index, y in enumerate((1.08, 0.46, -0.18, -0.82, -1.46)):
            add(
                base.create_box(
                    f"SRC_CutterServiceCover_{label}_{index:02d}",
                    (side * 1.39, y, 0.0),
                    (0.035, 0.38, 0.30),
                    collection,
                    3 if index in (0, 4) else 0,
                    0.008,
                ),
                "service_cover",
                "surface_detail",
            )
            add(
                base.create_strut(
                    f"SRC_CutterServiceRib_{label}_{index:02d}",
                    (side * 1.41, y, -0.22),
                    (side * 1.41, y, 0.22),
                    0.028,
                    collection,
                    3,
                ),
                "service_rib",
                "surface_detail",
            )

    # Broad foredeck and aft-deck plates, not a stack of miniature warship decks.
    for index, (y, width, z) in enumerate(
        ((2.24, 0.84, 0.61), (1.68, 1.15, 0.80), (-0.78, 1.12, 0.91), (-1.42, 1.02, 0.80))
    ):
        add(
            base.create_box(
                f"SRC_CutterDeckPlate_{index:02d}",
                (0.0, y, z),
                (width, 0.46, 0.040),
                collection,
                0 if index not in (1, 3) else 3,
                0.012,
            ),
            "deck_armor",
            "primary_structure",
        )
        add(
            base.create_box(
                f"SRC_CutterDeckLatch_{index:02d}",
                (0.0, y, z + 0.035),
                (0.13, 0.10, 0.025),
                collection,
                1,
                0.006,
            ),
            "deck_latch",
            "surface_detail",
        )

    for index, (width, y, z) in enumerate(
        ((0.46, 1.63, 0.91), (0.68, 1.26, 1.08), (0.76, 0.82, 1.15))
    ):
        add(
            base.create_box(
                f"SRC_CutterBridgeSensorSlit_{index:02d}",
                (0.0, y, z),
                (width, 0.060, 0.048),
                collection,
                2 if index == 1 else 1,
                0.010,
            ),
            "armored_sensor_slit",
            "cockpit",
        )

    cutter_deck_fields = (
        (
            "Forward",
            [
                (-0.42, 2.18, 0.68),
                (-0.98, 1.62, 0.78),
                (-1.18, 0.52, 0.92),
                (-0.56, 0.62, 1.00),
            ],
        ),
        (
            "Aft",
            [
                (-0.56, 0.32, 1.00),
                (-1.22, 0.24, 0.92),
                (-1.12, -1.52, 0.74),
                (-0.50, -1.40, 0.88),
            ],
        ),
    )
    for side, label in ((-1.0, "L"), (1.0, "R")):
        for field_name, left_points in cutter_deck_fields:
            points = (
                left_points
                if side < 0.0
                else [(-x, y, z) for x, y, z in reversed(left_points)]
            )
            add(
                base.create_surface_panel(
                    f"SRC_CutterShoulderDeck_{label}_{field_name}",
                    points,
                    0.026,
                    collection,
                    0,
                    0.006,
                ),
                "shoulder_deck_armor",
                "primary_structure",
            )
        add(
            base.create_strut(
                f"SRC_CutterBowChine_{label}",
                (side * 0.30, 3.04, 0.12),
                (side * 1.18, 1.30, 0.36),
                0.052,
                collection,
                3,
            ),
            "bow_chine",
            "primary_structure",
        )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        for row, y in enumerate((-1.70, -1.94, -2.18)):
            add(
                base.create_box(
                    f"SRC_CutterAftVent_{label}_{row:02d}",
                    (side * 0.74, y, 0.67),
                    (0.38, 0.11, 0.042),
                    collection,
                    1 if row == 1 else 3,
                    0.007,
                ),
                "aft_thermal_vent",
                "thermal",
            )

    # Twin permanently installed drives.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            base.create_box(
                f"SRC_CutterDriveHousing_{label}",
                (side * 0.55, -3.05, 0.04),
                (0.72, 0.50, 0.62),
                collection,
                1,
                0.075,
                8,
            ),
            "integrated_drive_housing",
            "propulsion",
        )
        add(
            base.create_torus(
                f"SRC_CutterDriveRing_{label}",
                (side * 0.55, -3.35, 0.04),
                0.235,
                0.038,
                collection,
                3,
                "Y",
                64,
                16,
            ),
            "integrated_drive",
            "propulsion",
        )
        add(
            base.create_cylinder(
                f"SRC_CutterDriveGlow_{label}",
                (side * 0.55, -3.39, 0.04),
                0.198,
                0.026,
                collection,
                2,
                64,
                "Y",
                0.003,
            ),
            "integrated_drive",
            "propulsion",
        )
        for z_index, z in enumerate((-0.46, 0.48)):
            nozzle = base.create_cone(
                f"SRC_CutterRCS_{label}_{z_index:02d}",
                (side * 1.28, -1.96, z),
                0.050,
                0.030,
                0.060,
                collection,
                1,
                24,
                "X",
                0.003,
            )
            if side < 0.0:
                nozzle.rotation_euler.y += math.pi
                _apply_rotation(nozzle)
            add(nozzle, "integrated_rcs", "propulsion")

    _finish_builder(parts, collection)
    return parts, placement


def build_sailer(
    collection: bpy.types.Collection,
    spec: family.HullSpec,
) -> tuple[list[bpy.types.Object], list[bpy.types.Object]]:
    """Build a space sailboat with a deep hull and rigid solar sails."""
    parts: list[bpy.types.Object] = []
    placement: list[bpy.types.Object] = []

    def add(
        obj: bpy.types.Object,
        role: str,
        group: str,
        placeable: bool = False,
    ) -> bpy.types.Object:
        family.tag_source(obj, spec, role, group)
        parts.append(obj)
        if placeable:
            placement.append(obj)
        return obj

    hull = add(
        base.create_smooth_loft(
            "SRC_SailerContinuousBoatHull",
            [
                (3.58, 0.035, 0.045, -0.18, 2.0),
                (3.30, 0.28, 0.22, -0.22, 2.7),
                (2.82, 0.62, 0.43, -0.25, 3.2),
                (2.08, 1.02, 0.66, -0.26, 3.8),
                (1.12, 1.34, 0.82, -0.24, 4.1),
                (0.02, 1.48, 0.92, -0.20, 4.3),
                (-1.08, 1.44, 0.91, -0.16, 4.2),
                (-2.04, 1.30, 0.82, -0.10, 3.9),
                (-2.76, 1.05, 0.68, -0.04, 3.5),
                (-3.18, 0.82, 0.58, 0.00, 3.2),
            ],
            collection,
            0,
            68,
            2,
            4,
        ),
        "continuous_boat_hull",
        "primary_structure",
        True,
    )
    hull["continuous_build_surface"] = True

    deck_points = [
        (-0.22, 2.72),
        (-0.82, 2.20),
        (-1.20, 1.18),
        (-1.30, -2.34),
        (-0.88, -2.92),
        (0.88, -2.92),
        (1.30, -2.34),
        (1.20, 1.18),
        (0.82, 2.20),
        (0.22, 2.72),
    ]
    add(
        base.create_prism(
            "SRC_SailerContinuousDeck",
            deck_points,
            0.48,
            0.60,
            collection,
            0,
            0.035,
        ),
        "continuous_deck",
        "primary_structure",
        True,
    )
    add(
        base.create_prism(
            "SRC_SailerDeepKeel",
            [
                (-0.18, 2.20),
                (-0.30, 0.90),
                (-0.34, -2.64),
                (-0.22, -3.12),
                (0.22, -3.12),
                (0.34, -2.64),
                (0.30, 0.90),
                (0.18, 2.20),
            ],
            -1.36,
            -0.84,
            collection,
            1,
            0.035,
        ),
        "deep_keel",
        "primary_structure",
        True,
    )

    # Raised stern cabin and a low armored pilothouse retain the sailboat read.
    add(
        base.create_smooth_loft(
            "SRC_SailerRaisedSternCabin",
            [
                (-1.12, 0.38, 0.12, 0.58, 2.5),
                (-1.52, 0.72, 0.22, 0.74, 3.1),
                (-2.04, 0.86, 0.28, 0.86, 3.4),
                (-2.58, 0.78, 0.24, 0.88, 3.2),
                (-2.90, 0.55, 0.13, 0.72, 2.8),
            ],
            collection,
            1,
            52,
            2,
            3,
        ),
        "raised_stern_cabin",
        "cockpit",
    )
    for index, (width, y, z) in enumerate(
        ((0.52, -1.50, 0.92), (0.78, -1.88, 1.09), (0.86, -2.30, 1.15))
    ):
        add(
            base.create_box(
                f"SRC_SailerCabinSensorSlit_{index:02d}",
                (0.0, y, z),
                (width, 0.060, 0.046),
                collection,
                2 if index == 1 else 1,
                0.009,
            ),
            "armored_sensor_slit",
            "cockpit",
        )

    # Curved gunwales and bow rails make the base silhouette read as a vessel.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            base.create_strut(
                f"SRC_SailerGunwaleForward_{label}",
                (side * 0.22, 3.18, 0.48),
                (side * 1.18, 1.42, 0.66),
                0.070,
                collection,
                3,
            ),
            "gunwale",
            "primary_structure",
        )
        add(
            base.create_strut(
                f"SRC_SailerGunwaleAft_{label}",
                (side * 1.18, 1.42, 0.66),
                (side * 1.24, -2.62, 0.69),
                0.070,
                collection,
                3,
            ),
            "gunwale",
            "primary_structure",
        )
        add(
            base.create_box(
                f"SRC_SailerServiceGallery_{label}",
                (side * 1.30, -0.54, 0.02),
                (0.10, 3.18, 0.40),
                collection,
                1,
                0.020,
            ),
            "service_gallery",
            "surface_detail",
        )
        for index, y in enumerate((1.12, 0.48, -0.18, -0.84, -1.50)):
            add(
                base.create_box(
                    f"SRC_SailerGalleryCover_{label}_{index:02d}",
                    (side * 1.36, y, 0.02),
                    (0.035, 0.40, 0.27),
                    collection,
                    3 if index in (0, 4) else 0,
                    0.008,
                ),
                "gallery_cover",
                "surface_detail",
            )
            add(
                base.create_cylinder(
                    f"SRC_SailerGalleryFastener_{label}_{index:02d}",
                    (side * 1.39, y, 0.18),
                    0.027,
                    0.022,
                    collection,
                    3,
                    18,
                    "X",
                    0.003,
                ),
                "gallery_fastener",
                "surface_detail",
            )

    # Central mast, fore/aft booms, and two rigid triangular solar sails.
    add(
        base.create_cylinder(
            "SRC_SailerMainMast",
            (0.0, 0.15, 1.48),
            0.090,
            2.65,
            collection,
            3,
            32,
            "Z",
            0.010,
        ),
        "main_mast",
        "solar_rig",
    )
    add(
        base.create_strut(
            "SRC_SailerForeBoom",
            (0.0, 0.18, 0.86),
            (0.0, 2.72, 0.86),
            0.070,
            collection,
            3,
        ),
        "fore_boom",
        "solar_rig",
    )
    add(
        base.create_strut(
            "SRC_SailerAftBoom",
            (0.0, 0.10, 0.92),
            (0.0, -2.46, 0.92),
            0.070,
            collection,
            3,
        ),
        "aft_boom",
        "solar_rig",
    )
    fore_sail = add(
        base.create_yz_plate(
            "SRC_SailerForeSolarSail",
            [(0.32, 0.96), (0.32, 2.66), (2.70, 0.96)],
            0.026,
            collection,
            0,
            0.012,
        ),
        "rigid_solar_sail",
        "solar_rig",
    )
    aft_sail = add(
        base.create_yz_plate(
            "SRC_SailerAftSolarSail",
            [(-0.02, 1.00), (-0.02, 2.52), (-2.48, 1.00)],
            0.026,
            collection,
            1,
            0.012,
        ),
        "rigid_solar_sail",
        "solar_rig",
    )
    fore_sail["placement_excluded"] = True
    aft_sail["placement_excluded"] = True
    for name, start, end in (
        ("ForeLuff", (0.0, 0.32, 0.96), (0.0, 0.32, 2.66)),
        ("ForeLeech", (0.0, 0.32, 2.66), (0.0, 2.70, 0.96)),
        ("ForeFoot", (0.0, 0.32, 0.96), (0.0, 2.70, 0.96)),
        ("AftLuff", (0.0, -0.02, 1.00), (0.0, -0.02, 2.52)),
        ("AftLeech", (0.0, -0.02, 2.52), (0.0, -2.48, 1.00)),
        ("AftFoot", (0.0, -0.02, 1.00), (0.0, -2.48, 1.00)),
    ):
        add(
            base.create_strut(
                f"SRC_Sailer{name}Spar",
                start,
                end,
                0.045,
                collection,
                3,
            ),
            "solar_sail_spar",
            "solar_rig",
        )

    # Deck hatches and thermal grilles preserve the approved industrial detail.
    for index, (y, width) in enumerate(
        ((2.18, 0.62), (1.52, 0.88), (0.74, 0.96), (-0.30, 0.92), (-1.02, 0.82))
    ):
        add(
            base.create_box(
                f"SRC_SailerDeckHatch_{index:02d}",
                (0.0, y, 0.64),
                (width, 0.46, 0.040),
                collection,
                0 if index not in (1, 4) else 3,
                0.010,
            ),
            "deck_hatch",
            "surface_detail",
        )
        add(
            base.create_box(
                f"SRC_SailerDeckLatch_{index:02d}",
                (0.0, y, 0.676),
                (0.13, 0.09, 0.024),
                collection,
                1,
                0.005,
            ),
            "deck_latch",
            "surface_detail",
        )
    for side, label in ((-1.0, "L"), (1.0, "R")):
        for row, y in enumerate((-1.28, -1.52, -1.76)):
            add(
                base.create_box(
                    f"SRC_SailerAftThermalSlat_{label}_{row:02d}",
                    (side * 0.72, y, 0.72),
                    (0.38, 0.11, 0.038),
                    collection,
                    1 if row == 1 else 3,
                    0.006,
                ),
                "aft_thermal_slat",
                "thermal",
            )

    # Twin integral stern drives keep the sailer fully mobile without sails.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            base.create_box(
                f"SRC_SailerDriveHousing_{label}",
                (side * 0.56, -3.05, -0.12),
                (0.70, 0.48, 0.58),
                collection,
                1,
                0.070,
                8,
            ),
            "integrated_drive_housing",
            "propulsion",
        )
        add(
            base.create_torus(
                f"SRC_SailerDriveRing_{label}",
                (side * 0.56, -3.34, -0.12),
                0.215,
                0.036,
                collection,
                3,
                "Y",
                64,
                16,
            ),
            "integrated_drive",
            "propulsion",
        )
        add(
            base.create_cylinder(
                f"SRC_SailerDriveGlow_{label}",
                (side * 0.56, -3.38, -0.12),
                0.180,
                0.026,
                collection,
                2,
                64,
                "Y",
                0.003,
            ),
            "integrated_drive",
            "propulsion",
        )
        for y_index, y in enumerate((1.22, -1.62)):
            nozzle = base.create_cone(
                f"SRC_SailerRCS_{label}_{y_index:02d}",
                (side * 1.32, y, -0.24),
                0.050,
                0.030,
                0.060,
                collection,
                1,
                24,
                "X",
                0.003,
            )
            if side < 0.0:
                nozzle.rotation_euler.y += math.pi
                _apply_rotation(nozzle)
            add(nozzle, "integrated_rcs", "propulsion")

    _finish_builder(parts, collection)
    return parts, placement


def build_fighter(
    collection: bpy.types.Collection,
    spec: family.HullSpec,
) -> tuple[list[bpy.types.Object], list[bpy.types.Object]]:
    parts: list[bpy.types.Object] = []
    placement: list[bpy.types.Object] = []

    def add(
        obj: bpy.types.Object,
        role: str,
        group: str,
        placeable: bool = False,
    ) -> bpy.types.Object:
        family.tag_source(obj, spec, role, group)
        parts.append(obj)
        if placeable:
            placement.append(obj)
        return obj

    fuselage = add(
        base.create_smooth_loft(
            "SRC_FighterBlendedFuselage",
            [
                (3.20, 0.035, 0.045, -0.02, 2.0),
                (2.96, 0.22, 0.16, -0.03, 2.8),
                (2.54, 0.48, 0.32, -0.02, 3.3),
                (1.92, 0.72, 0.49, 0.00, 3.8),
                (1.12, 0.86, 0.62, 0.00, 4.2),
                (0.18, 0.92, 0.69, 0.00, 4.4),
                (-0.82, 0.88, 0.66, 0.00, 4.2),
                (-1.72, 0.78, 0.56, 0.00, 3.9),
                (-2.45, 0.67, 0.48, 0.00, 3.5),
                (-2.88, 0.58, 0.42, 0.00, 3.2),
            ],
            collection,
            0,
            68,
            2,
            4,
        ),
        "blended_fuselage",
        "primary_structure",
        True,
    )
    fuselage["continuous_build_surface"] = True

    left_wing_points = [
        (-0.54, 1.10),
        (-1.12, 0.92),
        (-2.70, -0.36),
        (-2.56, -1.42),
        (-0.70, -0.72),
    ]
    right_wing_points = [(-x, y) for x, y in reversed(left_wing_points)]
    wings: list[bpy.types.Object] = []
    for label, points in (("L", left_wing_points), ("R", right_wing_points)):
        wing = add(
            base.create_prism(
                f"SRC_FighterMainWing_{label}",
                points,
                -0.10,
                0.10,
                collection,
                0,
                0.045,
            ),
            "permanent_main_wing",
            "primary_structure",
            True,
        )
        wings.append(wing)
        inset_scale = 0.86
        inset = [(x * inset_scale, y * 0.92 - 0.04) for x, y in points]
        add(
            base.create_prism(
                f"SRC_FighterWingInset_{label}",
                inset,
                0.105,
                0.135,
                collection,
                1,
                0.006,
            ),
            "wing_armor_inset",
            "surface_detail",
        )
        outer = points[2]
        add(
            base.create_strut(
                f"SRC_FighterLeadingSpar_{label}",
                (points[1][0], points[1][1], 0.12),
                (outer[0], outer[1], 0.12),
                0.060,
                collection,
                3,
            ),
            "wing_leading_spar",
            "primary_structure",
        )

    left_tail_points = [
        (-0.50, -1.92),
        (-1.64, -2.28),
        (-1.52, -2.86),
        (-0.54, -2.54),
    ]
    right_tail_points = [(-x, y) for x, y in reversed(left_tail_points)]
    for label, points in (("L", left_tail_points), ("R", right_tail_points)):
        add(
            base.create_prism(
                f"SRC_FighterHorizontalTail_{label}",
                points,
                -0.065,
                0.065,
                collection,
                0,
                0.032,
            ),
            "permanent_horizontal_tail",
            "primary_structure",
            True,
        )

    for side, label in ((-1.0, "L"), (1.0, "R")):
        fin = base.create_yz_plate(
            f"SRC_FighterVerticalTail_{label}",
            [
                (-1.78, 0.18),
                (-2.12, 0.88),
                (-2.72, 0.74),
                (-2.88, 0.14),
            ],
            0.055,
            collection,
            0,
            0.025,
        )
        fin.location.x = side * 0.62
        add(
            fin,
            "permanent_vertical_tail",
            "primary_structure",
            True,
        )
        fin_inset = base.create_yz_plate(
            f"SRC_FighterVerticalTailInset_{label}",
            [
                (-2.04, 0.28),
                (-2.22, 0.70),
                (-2.58, 0.61),
                (-2.69, 0.25),
            ],
            0.060,
            collection,
            1,
            0.012,
        )
        fin_inset.location.x = side * 0.62
        add(fin_inset, "tail_armor_inset", "surface_detail")

    add(
        base.create_smooth_loft(
            "SRC_FighterArmoredCockpit",
            [
                (2.62, 0.08, 0.04, 0.32, 2.0),
                (2.34, 0.30, 0.11, 0.47, 2.7),
                (1.90, 0.46, 0.16, 0.60, 3.1),
                (1.38, 0.50, 0.17, 0.68, 3.3),
                (0.98, 0.42, 0.11, 0.64, 2.9),
            ],
            collection,
            1,
            52,
            2,
            3,
        ),
        "armored_cockpit",
        "cockpit",
    )
    for index, (width, y, z) in enumerate(
        ((0.34, 2.40, 0.47), (0.54, 2.08, 0.62), (0.63, 1.67, 0.75))
    ):
        add(
            base.create_box(
                f"SRC_FighterSensorSlit_{index:02d}",
                (0.0, y, z),
                (width, 0.055, 0.045),
                collection,
                2 if index == 1 else 1,
                0.009,
            ),
            "armored_sensor_slit",
            "cockpit",
        )

    # Wing-root heat exchangers and service covers supply mid-scale detail.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            base.create_box(
                f"SRC_FighterWingRootExchanger_{label}",
                (side * 0.86, -0.72, 0.18),
                (0.46, 0.92, 0.052),
                collection,
                1,
                0.016,
            ),
            "wing_root_exchanger",
            "thermal",
        )
        for index, y in enumerate((-1.02, -0.87, -0.72, -0.57, -0.42)):
            add(
                base.create_box(
                    f"SRC_FighterWingRootSlat_{label}_{index:02d}",
                    (side * 0.86, y, 0.218),
                    (0.37, 0.060, 0.025),
                    collection,
                    3 if index in (0, 4) else 0,
                    0.005,
                ),
                "wing_root_heat_slat",
                "thermal",
            )
        for index, y in enumerate((0.52, -0.02, -1.28)):
            add(
                base.create_box(
                    f"SRC_FighterWingAccessPanel_{label}_{index:02d}",
                    (side * (1.20 + index * 0.24), y, 0.145),
                    (0.46, 0.36, 0.032),
                    collection,
                    0 if index != 2 else 3,
                    0.009,
                ),
                "wing_access_panel",
                "surface_detail",
            )

    # Permanent twin engines remain integral; optional thrusters are boosters.
    for side, label in ((-1.0, "L"), (1.0, "R")):
        add(
            base.create_box(
                f"SRC_FighterEngineHousing_{label}",
                (side * 0.51, -2.64, 0.0),
                (0.62, 0.54, 0.62),
                collection,
                1,
                0.075,
                8,
            ),
            "integrated_engine_housing",
            "propulsion",
        )
        add(
            base.create_torus(
                f"SRC_FighterEngineRing_{label}",
                (side * 0.51, -3.00, 0.0),
                0.225,
                0.038,
                collection,
                3,
                "Y",
                64,
                16,
            ),
            "integrated_engine",
            "propulsion",
        )
        add(
            base.create_cylinder(
                f"SRC_FighterEngineGlow_{label}",
                (side * 0.51, -3.04, 0.0),
                0.188,
                0.026,
                collection,
                2,
                64,
                "Y",
                0.003,
            ),
            "integrated_engine",
            "propulsion",
        )
        for y_index, y in enumerate((0.56, -1.26)):
            nozzle = base.create_cone(
                f"SRC_FighterRCS_{label}_{y_index:02d}",
                (side * 0.90, y, -0.14),
                0.046,
                0.028,
                0.056,
                collection,
                1,
                24,
                "X",
                0.003,
            )
            if side < 0.0:
                nozzle.rotation_euler.y += math.pi
                _apply_rotation(nozzle)
            add(nozzle, "integrated_rcs", "propulsion")

    # Landing/maintenance keel gives the underside a structural centerline.
    add(
        base.create_prism(
            "SRC_FighterVentralKeel",
            [
                (-0.20, 1.20),
                (-0.27, 0.20),
                (-0.26, -2.30),
                (-0.17, -2.74),
                (0.17, -2.74),
                (0.26, -2.30),
                (0.27, 0.20),
                (0.20, 1.20),
            ],
            -0.86,
            -0.67,
            collection,
            1,
            0.025,
        ),
        "ventral_keel",
        "primary_structure",
        True,
    )

    _finish_builder(parts, collection)
    return parts, placement


SAUCER_SPEC = family.HullSpec(
    key="saucer",
    asset_id="flagship_lenticular_saucer_v1",
    display_name="Flagship Lenticular Saucer V1",
    seed="flagship-lenticular-saucer-v1-industrial",
    dimensions=(5.2, 1.4, 4.6),
    silhouette="lenticular-saucer",
    builder=build_saucer,
    balanced_loadout=(
        ("KineticRepeater", (-0.82, 0.74, 0.72), (0.0, 0.0, 1.0), 0.0, 0.72),
        ("EnergyPulse", (0.82, 0.74, 0.72), (0.0, 0.0, 1.0), 0.0, 0.72),
        ("SensorMast", (0.0, -0.20, 0.76), (0.0, 0.0, 1.0), 0.0, 0.70),
        ("Radiator", (0.0, -0.42, -0.70), (0.0, 0.0, -1.0), 0.0, 0.72),
        ("ThrusterMedium", (-1.42, -1.58, 0.06), (0.0, -1.0, 0.0), 0.0, 0.76),
        ("ThrusterMedium", (1.42, -1.58, 0.06), (0.0, -1.0, 0.0), 0.0, 0.76),
    ),
    asymmetric_loadout=(
        ("EngineNacelle", (-1.42, -1.55, 0.08), (0.0, -1.0, 0.0), 0.0, 0.78),
        ("Canard", (2.16, 0.58, 0.0), (1.0, 0.0, 0.0), 90.0, 0.82),
        ("ArmorFairing", (0.86, -0.30, 0.68), (0.0, 0.0, 1.0), -12.0, 0.82),
        ("Radiator", (-0.72, -0.36, -0.66), (0.0, 0.0, -1.0), 18.0, 0.70),
    ),
)

SAILER_SPEC = family.HullSpec(
    key="sailer",
    asset_id="flagship_solar_sailer_v1",
    display_name="Flagship Solar Sailer V1",
    seed="flagship-solar-sailer-v1-industrial",
    dimensions=(3.2, 3.6, 7.2),
    silhouette="rigid-solar-sailer",
    builder=build_sailer,
    balanced_loadout=(
        ("KineticRepeater", (-0.72, 1.34, 0.68), (0.0, 0.0, 1.0), 0.0, 0.68),
        ("EnergyPulse", (0.72, 1.34, 0.68), (0.0, 0.0, 1.0), 0.0, 0.68),
        ("SensorMast", (0.72, -0.72, 0.72), (0.0, 0.0, 1.0), 0.0, 0.64),
        ("Radiator", (0.0, -0.72, -1.18), (0.0, 0.0, -1.0), 0.0, 0.70),
        ("ThrusterLarge", (-0.76, -3.08, -0.10), (0.0, -1.0, 0.0), 0.0, 0.76),
        ("ThrusterLarge", (0.76, -3.08, -0.10), (0.0, -1.0, 0.0), 0.0, 0.76),
    ),
    asymmetric_loadout=(
        ("EngineNacelle", (-0.76, -3.02, -0.10), (0.0, -1.0, 0.0), 0.0, 0.78),
        ("Canard", (1.30, 1.12, -0.05), (1.0, 0.0, 0.0), 90.0, 0.78),
        ("ArmorFairing", (0.62, 0.12, 0.68), (0.0, 0.0, 1.0), -12.0, 0.76),
        ("Radiator", (-0.54, -0.82, -1.12), (0.0, 0.0, -1.0), 16.0, 0.68),
    ),
)

FIGHTER_SPEC = family.HullSpec(
    key="fighter",
    asset_id="flagship_conventional_fighter_v1",
    display_name="Flagship Conventional Fighter V1",
    seed="flagship-conventional-fighter-v1-industrial",
    dimensions=(5.4, 1.8, 6.4),
    silhouette="conventional-fighter",
    builder=build_fighter,
    balanced_loadout=(
        ("KineticRepeater", (-1.42, 0.28, 0.18), (0.0, 0.0, 1.0), 0.0, 0.68),
        ("EnergyPulse", (1.42, 0.28, 0.18), (0.0, 0.0, 1.0), 0.0, 0.68),
        ("SensorMast", (0.0, -0.42, 0.78), (0.0, 0.0, 1.0), 0.0, 0.66),
        ("Radiator", (0.0, -0.52, -0.78), (0.0, 0.0, -1.0), 0.0, 0.68),
        ("ThrusterMedium", (-1.88, -1.18, 0.12), (0.0, 0.0, 1.0), 0.0, 0.68),
        ("ThrusterMedium", (1.88, -1.18, 0.12), (0.0, 0.0, 1.0), 0.0, 0.68),
    ),
    asymmetric_loadout=(
        ("EngineNacelle", (-1.82, -1.02, 0.12), (0.0, 0.0, 1.0), 0.0, 0.72),
        ("ArmorFairing", (1.46, -0.08, 0.14), (0.0, 0.0, 1.0), -12.0, 0.78),
        ("Radiator", (-0.52, -0.54, -0.75), (0.0, 0.0, -1.0), 18.0, 0.66),
        ("Canard", (0.88, 1.42, 0.02), (1.0, 0.0, 0.0), 90.0, 0.72),
    ),
)

ALL_SPECS = (SAUCER_SPEC, SAILER_SPEC, FIGHTER_SPEC)


def _write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(payload, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )


def _individual_report(
    spec: family.HullSpec,
    hull_result: dict,
    module_results: dict[str, dict],
    module_metadata: dict[str, list[bpy.types.Object]],
    review_file: Path,
) -> dict:
    module_errors = [
        f"{module_id}: {error}"
        for module_id, result in module_results.items()
        for error in result["errors"]
    ]
    errors = list(hull_result["errors"]) + module_errors
    return {
        "success": not errors,
        "assetId": spec.asset_id,
        "seed": spec.seed,
        "blenderVersion": bpy.app.version_string,
        "dimensions": spec.dimensions,
        "forwardAxis": "+Y",
        "unityForwardAxis": "+Z",
        "artDirection": "industrial-hard-sci-fi",
        "silhouette": spec.silhouette,
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


def _build_gallery(
    specs: tuple[family.HullSpec, ...],
    review_root: Path,
) -> tuple[Path, Path]:
    base.pipeline.clean_scene()
    gallery = base.ensure_collection("10_Fleet_Unloaded_Hulls")
    source_root = base.ensure_collection("00_Fleet_Sources")
    x_positions = (-6.7, 0.0, 6.7)
    for spec, x in zip(specs, x_positions):
        source = base.ensure_collection(
            f"00_Source_{spec.key.title()}",
            source_root,
        )
        parts, _ = spec.builder(source, spec)
        root = base.create_group_root(
            f"{spec.asset_id}_Root",
            gallery,
            (x, 0.0, 0.0),
        )
        base.duplicate_group(
            parts,
            spec.key.upper(),
            gallery,
            root,
        )
        label = base.add_label(
            gallery,
            spec.display_name,
            (x, -4.10, -1.55),
            0.30,
        )
        label.rotation_euler.x = 0.0
        label.hide_render = True

    module_source = base.ensure_collection("01_Source_Modules")
    module_sources, _ = base.build_module_library(module_source)
    module_library = base.ensure_collection("20_Shared_Module_Library")
    for index, module_spec in enumerate(base.MODULE_SPECS):
        column = index % 5
        row = index // 5
        base.place_module(
            module_spec.module_id,
            module_sources,
            module_library,
            (-3.6 + column * 1.8, 1.2 - row * 2.0, 0.0),
            (0.0, 0.0, 1.0),
            0.0,
            0.88,
        )
    module_library.hide_viewport = True
    module_library.hide_render = True
    family.add_gallery_environment()

    review_file = review_root / f"{FAMILY_ID}_review.blend"
    overview = review_root / f"{FAMILY_ID}_overview.png"
    review_root.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.render.filepath = str(overview)
    bpy.ops.render.render(write_still=True)
    base.save_review(review_file)
    return review_file, overview


def main() -> None:
    args = parse_args()
    output_root = Path(args.output_root).resolve()
    review_root = Path(args.review_root).resolve()
    report_path = (
        Path(args.report).resolve()
        if args.report
        else output_root / "flagship_family_report.json"
    )
    specs = (
        ALL_SPECS
        if args.archetype == "all"
        else tuple(spec for spec in ALL_SPECS if spec.key == args.archetype)
    )

    family_results: dict[str, dict] = {}
    shared_modules: dict[str, dict] | None = None
    all_errors: list[str] = []
    for spec in specs:
        base.pipeline.clean_scene()
        source_collection = base.ensure_collection("00_Source_Hull")
        module_collection = base.ensure_collection("01_Source_Modules")
        hull_sources, placement_parts = spec.builder(source_collection, spec)
        module_sources, module_metadata = base.build_module_library(
            module_collection
        )
        hull_result, _ = family.build_hull_export(
            spec,
            hull_sources,
            placement_parts,
            output_root,
        )
        if shared_modules is None:
            shared_modules = base.build_module_exports(
                module_sources,
                output_root,
            )
        family.build_review_scene(
            spec,
            hull_sources,
            placement_parts,
            module_sources,
        )
        review_file = review_root / f"{spec.asset_id}_review.blend"
        report = _individual_report(
            spec,
            hull_result,
            shared_modules,
            module_metadata,
            review_file,
        )
        _write_json(
            output_root / "Reports" / f"{spec.asset_id}.json",
            report,
        )
        base.save_review(review_file)
        family_results[spec.asset_id] = report
        all_errors.extend(
            f"{spec.asset_id}: {error}" for error in report["errors"]
        )
        print(
            f"[FlagshipFamily] {spec.asset_id}: "
            f"{hull_result['triangles']} "
            f"hash={hull_result['geometryHash']}"
        )

    gallery_file = ""
    gallery_overview = ""
    if args.archetype == "all" and not all_errors:
        gallery, overview = _build_gallery(ALL_SPECS, review_root)
        gallery_file = str(gallery)
        gallery_overview = str(overview)

    aggregate = {
        "success": not all_errors,
        "familyId": FAMILY_ID,
        "blenderVersion": bpy.app.version_string,
        "artDirection": "industrial-hard-sci-fi",
        "forwardAxis": "+Y",
        "unityForwardAxis": "+Z",
        "fixedHardpoints": False,
        "freeSurfacePlacement": True,
        "materials": MATERIAL_NAMES,
        "hulls": family_results,
        "sharedModules": shared_modules or {},
        "galleryReviewFile": gallery_file,
        "galleryOverview": gallery_overview,
        "errors": all_errors,
    }
    _write_json(report_path, aggregate)
    if all_errors:
        raise RuntimeError(
            "Flagship family validation failed:\n" + "\n".join(all_errors)
        )
    print(f"[FlagshipFamily] review={gallery_file or next(iter(family_results.values()))['reviewFile']}")
    print(f"[FlagshipFamily] report={report_path}")


if __name__ == "__main__":
    main()
