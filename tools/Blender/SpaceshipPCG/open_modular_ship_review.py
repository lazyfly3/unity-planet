"""Open the generated modular ship review file in an interactive Blender."""

from __future__ import annotations

from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[3]
REVIEW_FILE = (
    PROJECT_ROOT
    / "Library"
    / "SpaceshipPCGReview"
    / "modular_ship_v1_review.blend"
)

if not REVIEW_FILE.is_file():
    raise FileNotFoundError(
        f"Generate the modular prototype before opening review: {REVIEW_FILE}"
    )

bpy.ops.wm.open_mainfile(filepath=str(REVIEW_FILE))
for area in bpy.context.screen.areas:
    if area.type != "VIEW_3D":
        continue
    space = area.spaces.active
    space.shading.type = "MATERIAL"
    space.shading.light = "STUDIO"
    space.shading.studiolight_rotate_z = 0.55
    space.shading.studiolight_background_alpha = 0.35
    space.clip_end = 1000.0
    space.overlay.show_floor = True
    region = next(
        (candidate for candidate in area.regions if candidate.type == "WINDOW"),
        None,
    )
    if region is not None:
        with bpy.context.temp_override(area=area, region=region):
            bpy.ops.view3d.view_camera()

print(f"[SpaceshipPCG] opened modular prototype review: {REVIEW_FILE}")
