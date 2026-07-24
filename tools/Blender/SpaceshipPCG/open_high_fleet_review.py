"""Open a visible Blender review scene containing several generated high-detail hulls."""

from __future__ import annotations

from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[3]
GENERATED_HULLS = PROJECT_ROOT / "Assets" / "SpacecraftEditor" / "Art" / "Generated" / "Hulls"
REVIEW_FILE = PROJECT_ROOT / "Library" / "SpaceshipPCGReview" / "high_fleet_review.blend"

HULLS = (
    ("balanced_00", (-7.0, 3.8, 0.0)),
    ("spindle_00", (0.0, 3.8, 0.0)),
    ("saucer_00", (8.0, 3.8, 0.0)),
    ("balanced_05", (-7.0, -4.0, 0.0)),
    ("spindle_07", (0.0, -4.0, 0.0)),
    ("saucer_05", (8.0, -4.0, 0.0)),
)


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in tuple(bpy.data.collections):
        if collection.users == 0:
            bpy.data.collections.remove(collection)


def import_hull(hull_id: str, offset: tuple[float, float, float]) -> list[bpy.types.Object]:
    source = GENERATED_HULLS / f"{hull_id}.fbx"
    if not source.is_file():
        raise FileNotFoundError(source)

    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(source))
    imported = [obj for obj in bpy.data.objects if obj not in before]
    roots = [obj for obj in imported if obj.parent is None]
    for root in roots:
        root.location.x += offset[0]
        root.location.y += offset[1]
        root.location.z += offset[2]

    lod0 = []
    for obj in imported:
        is_lod0 = obj.type == "MESH" and "Render_LOD0" in obj.name
        if obj.type == "MESH" and not is_lod0:
            obj.hide_set(True)
            obj.hide_render = True
        if is_lod0:
            obj.hide_set(False)
            obj.hide_render = False
            lod0.append(obj)

    label = bpy.data.curves.new(f"{hull_id}_Label", "FONT")
    label.body = hull_id
    label.align_x = "CENTER"
    label.size = 0.5
    label.extrude = 0.01
    label_object = bpy.data.objects.new(f"{hull_id}_Label", label)
    bpy.context.scene.collection.objects.link(label_object)
    label_object.location = (offset[0], offset[1] - 3.0, offset[2] + 0.1)
    label_object.rotation_euler = (0.0, 0.0, 0.0)
    return lod0


def configure_review_scene() -> None:
    scene = bpy.context.scene
    scene.name = "High Fleet Review"
    scene.world.color = (0.025, 0.025, 0.035)

    for obj in bpy.context.selected_objects:
        obj.select_set(False)

    all_lod0 = []
    for hull_id, offset in HULLS:
        all_lod0.extend(import_hull(hull_id, offset))

    for obj in all_lod0:
        obj.select_set(True)
    if all_lod0:
        bpy.context.view_layer.objects.active = all_lod0[0]

    for area in bpy.context.screen.areas:
        if area.type != "VIEW_3D":
            continue
        area.spaces.active.shading.type = "MATERIAL"
        area.spaces.active.clip_end = 10000.0
        area.spaces.active.overlay.show_floor = True
        area.spaces.active.overlay.show_axis_x = True
        area.spaces.active.overlay.show_axis_y = True
        region = next((item for item in area.regions if item.type == "WINDOW"), None)
        if region is not None:
            with bpy.context.temp_override(area=area, region=region):
                bpy.ops.view3d.view_selected(use_all_regions=False)

    REVIEW_FILE.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(REVIEW_FILE))


clear_scene()
configure_review_scene()
print(f"[SpaceshipPCG] opened high fleet review: {REVIEW_FILE}")
