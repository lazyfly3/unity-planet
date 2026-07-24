"""Creates deterministic Blender review renders without exporting runtime assets."""

from __future__ import annotations

from pathlib import Path

import bpy
from mathutils import Vector


def scene_bounds(meshes):
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    minimum = Vector(tuple(min(point[i] for point in points) for i in range(3)))
    maximum = Vector(tuple(max(point[i] for point in points) for i in range(3)))
    return minimum, maximum


def ensure_camera_and_lights(center, size):
    scene = bpy.context.scene
    camera_data = bpy.data.cameras.get("CreatureReviewCamera") or bpy.data.cameras.new("CreatureReviewCamera")
    camera = bpy.data.objects.get("CreatureReviewCamera") or bpy.data.objects.new("CreatureReviewCamera", camera_data)
    if camera.name not in scene.collection.objects:
        scene.collection.objects.link(camera)
    scene.camera = camera
    distance = max(22.0, size.y * 1.8)
    camera.location = (center.x, center.y - distance, center.z + max(8.0, size.z * 2.5))
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = max(12.0, size.x / 1.2, size.y * 1.9, size.z * 4.0)
    for name, offset, energy, area_size in (
        ("CreatureReviewKey", Vector((4.0, -12.0, 18.0)), 1800.0, 12.0),
        ("CreatureReviewFill", Vector((-12.0, -2.0, 10.0)), 1000.0, 10.0),
        ("CreatureReviewRim", Vector((10.0, 8.0, 14.0)), 1400.0, 8.0),
    ):
        light_data = bpy.data.lights.get(name) or bpy.data.lights.new(name, "AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = area_size
        light = bpy.data.objects.get(name) or bpy.data.objects.new(name, light_data)
        if light.name not in scene.collection.objects:
            scene.collection.objects.link(light)
        light.location = center + offset
        light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
    return camera


def render_six_views(scene, camera, center, size, output):
    distance = max(size.length * 1.4, 12.0)
    views = (
        ("front", Vector((0.0, -1.0, 0.0)), Vector((0.0, 0.0, 1.0))),
        ("back", Vector((0.0, 1.0, 0.0)), Vector((0.0, 0.0, 1.0))),
        ("left", Vector((-1.0, 0.0, 0.0)), Vector((0.0, 0.0, 1.0))),
        ("right", Vector((1.0, 0.0, 0.0)), Vector((0.0, 0.0, 1.0))),
        ("top", Vector((0.0, 0.0, 1.0)), Vector((0.0, 1.0, 0.0))),
        ("bottom", Vector((0.0, 0.0, -1.0)), Vector((0.0, -1.0, 0.0))),
    )
    rendered = []
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = max(size.x, size.y, size.z) * 1.18
    for name, direction, up in views:
        camera.location = center + direction * distance
        camera.rotation_euler = (
            center - camera.location
        ).to_track_quat("-Z", "Y" if abs(up.z) > 0.5 else "X").to_euler()
        path = output / f"material_{name}.png"
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        rendered.append(str(path))
    return rendered


def review_stage(family, action_records):
    showcase = family["outputRoot"] / f"{family['familyId']}SpeciesShowcase.blend"
    bpy.ops.wm.open_mainfile(filepath=str(showcase))
    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.hide_get() and not obj.hide_render
    ]
    if not meshes:
        raise RuntimeError("Showcase contains no renderable meshes")
    minimum, maximum = scene_bounds(meshes)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    camera = ensure_camera_and_lights(center, size)
    scene = bpy.context.scene
    scene.world.color = (0.035, 0.045, 0.06)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    output = family["outputRoot"] / "Reviews" / family["familyId"]
    output.mkdir(parents=True, exist_ok=True)
    last = int(action_records["walk"]["frames"] - 1)
    rendered = []
    for frame in sorted({0, last // 2, last}):
        scene.frame_set(frame)
        path = output / f"showcase_walk_{frame:03d}.png"
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        rendered.append(str(path))
    scene.frame_set(0)
    rendered.extend(render_six_views(scene, camera, center, size, output))
    scene.frame_set(0)
    for armature in (obj for obj in scene.objects if obj.type == "ARMATURE"):
        armature.hide_set(False)
        armature.hide_viewport = False
        armature.hide_render = True
        armature.show_in_front = False
        armature.data.display_type = "STICK"
    if bpy.context.screen is not None:
        for area in bpy.context.screen.areas:
            if area.type == "VIEW_3D":
                area.spaces.active.shading.type = "MATERIAL"
                area.spaces.active.overlay.show_relationship_lines = False
                area.spaces.active.overlay.show_extras = False
    bpy.ops.wm.save_as_mainfile(filepath=str(showcase))
    return rendered
