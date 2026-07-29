"""Open the high-detail flagship review file in Blender 5.1."""

from __future__ import annotations

from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[3]
REVIEW_FILE = (
    PROJECT_ROOT
    / "Library"
    / "SpaceshipPCGReview"
    / "flagship_arrowhead_v2_review.blend"
)

if not REVIEW_FILE.is_file():
    raise FileNotFoundError(
        f"Generate the flagship v2 review first: {REVIEW_FILE}"
    )

bpy.ops.wm.open_mainfile(filepath=str(REVIEW_FILE))
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type != "VIEW_3D":
            continue
        space = area.spaces.active
        space.shading.type = "MATERIAL"
        space.shading.light = "STUDIO"
        space.shading.studiolight_rotate_z = 0.55
        space.clip_end = 1000.0
        space.overlay.show_floor = True
        space.region_3d.view_perspective = "CAMERA"

print(f"[SpaceshipPCG] opened flagship v2 review: {REVIEW_FILE}")
