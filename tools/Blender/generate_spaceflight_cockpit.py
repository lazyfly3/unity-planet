import bpy
import math
import os
from mathutils import Euler, Vector


PROJECT_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SOURCE_DIR = os.path.join(PROJECT_ROOT, "Tools", "Blender", "Source")
ASSET_DIR = os.path.join(
    PROJECT_ROOT, "Assets", "SpacecraftEditor", "Art", "Models", "Cockpit"
)
TEMP_DIR = os.path.join(PROJECT_ROOT, "Temp")
BLEND_PATH = os.path.join(SOURCE_DIR, "UniversalCockpit.blend")
FBX_PATH = os.path.join(ASSET_DIR, "UniversalCockpit.fbx")
PREVIEW_PATH = os.path.join(TEMP_DIR, "UniversalCockpit_preview.png")


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def material(name, base, metallic=0.0, roughness=0.5, emission=None, strength=0.0):
    value = bpy.data.materials.new(name)
    value.use_nodes = True
    bsdf = value.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*base, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        emission_input = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
        strength_input = bsdf.inputs.get("Emission Strength")
        if emission_input is not None:
            emission_input.default_value = (*emission, 1.0)
        if strength_input is not None:
            strength_input.default_value = strength
    return value


def set_material(obj, value):
    obj.data.materials.clear()
    obj.data.materials.append(value)


def cube(name, location, scale, mat, parent, bevel=0.04, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0.0:
        modifier = obj.modifiers.new("EdgeBevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    set_material(obj, mat)
    obj.parent = parent
    return obj


def cylinder(name, location, radius, depth, mat, parent, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=12,
        radius=radius,
        depth=depth,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    set_material(obj, mat)
    obj.parent = parent
    bevel = obj.modifiers.new("EdgeBevel", "BEVEL")
    bevel.width = min(radius * 0.18, 0.035)
    bevel.segments = 2
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return obj


def beam(name, start, end, width, mat, parent):
    start_value = Vector(start)
    end_value = Vector(end)
    delta = end_value - start_value
    obj = cube(
        name,
        (start_value + end_value) * 0.5,
        (width, width, delta.length * 0.5),
        mat,
        parent,
        bevel=width * 0.45,
    )
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = delta.to_track_quat("Z", "Y")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return obj


def empty(name, location, parent, rotation=(0.0, 0.0, 0.0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = rotation
    obj.parent = parent
    return obj


def offset_along_local_y(location, rotation, distance):
    normal = Euler(rotation, "XYZ").to_matrix() @ Vector((0.0, 1.0, 0.0))
    return Vector(location) + normal * distance


def build_cockpit():
    shell = material("CockpitShell", (0.015, 0.035, 0.055), 0.78, 0.24)
    trim = material("CockpitTrim", (0.035, 0.12, 0.17), 0.62, 0.2)
    cyan = material(
        "CockpitCyan",
        (0.01, 0.16, 0.22),
        0.18,
        0.28,
        emission=(0.02, 0.75, 1.0),
        strength=3.4,
    )
    amber = material(
        "CockpitAmber",
        (0.24, 0.06, 0.01),
        0.22,
        0.3,
        emission=(1.0, 0.25, 0.025),
        strength=4.0,
    )
    rubber = material("CockpitRubber", (0.012, 0.014, 0.018), 0.05, 0.72)

    root = empty("UniversalCockpit", (0.0, 0.0, 0.0), None)

    # Camera origin is (0, 0, 0), looking toward Blender -Y. The FBX exporter
    # converts this into Unity +Z forward.
    # Keep the eye point at the origin. The dashboard is deliberately lower
    # and farther forward than the first version so it leaves roughly the
    # upper two thirds of a 16:9 frame unobstructed.
    cube("Dashboard", (0.0, -1.30, -0.82), (1.42, 0.48, 0.18), shell, root, 0.09)
    cube("DashboardLip", (0.0, -0.96, -0.70), (1.38, 0.12, 0.06), trim, root, 0.035)
    cube("LeftConsole", (-1.18, -0.85, -0.82), (0.27, 0.66, 0.17), shell, root, 0.06)
    cube("RightConsole", (1.18, -0.85, -0.82), (0.27, 0.66, 0.17), shell, root, 0.06)

    # The first-person view model deliberately omits canopy/window frames.
    # The external ship still keeps its hull and canopy geometry, while the
    # pilot view gets an unobstructed forward field of view.

    panel_pitch = math.radians(17)
    side_yaw = math.radians(18)
    left_panel_rotation = (panel_pitch, 0.0, -side_yaw)
    center_panel_rotation = (panel_pitch, 0.0, 0.0)
    right_panel_rotation = (panel_pitch, 0.0, side_yaw)
    left_panel_position = (-0.86, -1.15, -0.50)
    center_panel_position = (0.0, -1.25, -0.48)
    right_panel_position = (0.86, -1.15, -0.50)

    left_bezel = cube(
        "LeftMFD_Bezel",
        left_panel_position,
        (0.39, 0.07, 0.27),
        trim,
        root,
        0.04,
        rotation=left_panel_rotation,
    )
    left_surface_position = offset_along_local_y(
        left_panel_position, left_panel_rotation, 0.086
    )
    left_surface = cube(
        "LeftMFD_Surface",
        left_surface_position,
        (0.34, 0.014, 0.22),
        cyan,
        root,
        0.015,
        rotation=left_panel_rotation,
    )
    center_bezel = cube(
        "CenterRadar_Bezel",
        center_panel_position,
        (0.38, 0.07, 0.30),
        trim,
        root,
        0.05,
        rotation=center_panel_rotation,
    )
    center_surface_position = offset_along_local_y(
        center_panel_position, center_panel_rotation, 0.086
    )
    center_surface = cube(
        "CenterRadar_Surface",
        center_surface_position,
        (0.32, 0.014, 0.245),
        cyan,
        root,
        0.015,
        rotation=center_panel_rotation,
    )
    right_bezel = cube(
        "RightMFD_Bezel",
        right_panel_position,
        (0.39, 0.07, 0.27),
        trim,
        root,
        0.04,
        rotation=right_panel_rotation,
    )
    right_surface_position = offset_along_local_y(
        right_panel_position, right_panel_rotation, 0.086
    )
    right_surface = cube(
        "RightMFD_Surface",
        right_surface_position,
        (0.34, 0.014, 0.22),
        cyan,
        root,
        0.015,
        rotation=right_panel_rotation,
    )

    for surface in (left_surface, center_surface, right_surface):
        surface["cockpit_screen"] = True
    left_bezel["cockpit_module"] = "left_mfd"
    center_bezel["cockpit_module"] = "radar"
    right_bezel["cockpit_module"] = "right_mfd"

    stick_pivot = empty("StickPivot", (0.46, -0.74, -0.82), root)
    cylinder("StickBase", (0.46, -0.74, -0.82), 0.14, 0.09, rubber, root)
    cylinder(
        "StickShaft",
        (0.0, 0.0, 0.19),
        0.035,
        0.38,
        trim,
        stick_pivot,
    )
    grip = cube("StickGrip", (0.0, 0.0, 0.40), (0.075, 0.055, 0.11), rubber, stick_pivot, 0.035)
    cube("StickAccent", (0.0, -0.056, 0.41), (0.044, 0.012, 0.055), cyan, stick_pivot, 0.012)
    grip["cockpit_control"] = "stick"

    throttle_pivot = empty("ThrottlePivot", (-0.84, -0.58, -0.85), root)
    cube("ThrottleBase", (-0.84, -0.58, -0.85), (0.20, 0.27, 0.06), rubber, root, 0.035)
    beam("ThrottleLever", (0.0, 0.0, 0.0), (0.0, -0.22, 0.28), 0.035, trim, throttle_pivot)
    cube("ThrottleGrip", (0.0, -0.24, 0.30), (0.13, 0.065, 0.055), rubber, throttle_pivot, 0.025)
    throttle_pivot["cockpit_control"] = "throttle"

    for index, x in enumerate((-1.22, -1.02, 1.02, 1.22)):
        value = cyan if index % 2 == 0 else amber
        cube(
            f"WarningLight_{index + 1:02}",
            (x, -0.93, -0.72),
            (0.055, 0.018, 0.022),
            value,
            root,
            0.012,
        )

    empty(
        "LeftMFD_Anchor",
        left_surface_position,
        root,
        left_panel_rotation,
    )
    empty(
        "CenterRadar_Anchor",
        center_surface_position,
        root,
        center_panel_rotation,
    )
    empty(
        "RightMFD_Anchor",
        right_surface_position,
        root,
        right_panel_rotation,
    )

    return root


def add_preview_scene(root):
    world = bpy.context.scene.world
    world.color = (0.002, 0.004, 0.008)
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.001, 0.003, 0.008, 1.0)
    background.inputs["Strength"].default_value = 0.12

    bpy.ops.object.light_add(type="AREA", location=(0.0, 0.7, 1.9))
    key = bpy.context.object
    key.name = "Preview_Key"
    key.data.energy = 950.0
    key.data.shape = "RECTANGLE"
    key.data.size = 4.0
    key.rotation_euler = (math.radians(20), 0.0, math.radians(180))

    bpy.ops.object.light_add(type="AREA", location=(-2.3, -1.0, 0.5))
    fill = bpy.context.object
    fill.name = "Preview_Fill"
    fill.data.energy = 700.0
    fill.data.color = (0.03, 0.48, 1.0)
    fill.data.size = 3.0
    fill.rotation_euler = (math.radians(80), 0.0, math.radians(-75))

    # Render the same eye point used by Unity so the generated preview catches
    # dashboard occlusion and excessive screen perspective before export.
    bpy.ops.object.camera_add(location=(0.0, 0.0, 0.0))
    camera = bpy.context.object
    camera.name = "Preview_Camera"
    direction = Vector((0.0, -1.0, 0.0))
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    # Unity's Camera.fieldOfView is vertical. At 16:9, 66 degrees vertical is
    # roughly 98 degrees horizontal, which corresponds to about 15.5 mm on
    # Blender's default 36 mm sensor.
    camera.data.lens = 15.5
    bpy.context.scene.camera = camera

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = PREVIEW_PATH
    scene.render.film_transparent = False


def descendants(root):
    values = [root]
    for child in root.children:
        values.extend(descendants(child))
    return values


def save_and_export(root):
    os.makedirs(SOURCE_DIR, exist_ok=True)
    os.makedirs(ASSET_DIR, exist_ok=True)
    os.makedirs(TEMP_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    bpy.ops.render.render(write_still=True)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in descendants(root):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        global_scale=1.0,
        apply_unit_scale=True,
        bake_space_transform=True,
        path_mode="AUTO",
        add_leaf_bones=False,
    )


def main():
    clear_scene()
    root = build_cockpit()
    add_preview_scene(root)
    save_and_export(root)
    print(f"Saved Blender source: {BLEND_PATH}")
    print(f"Exported Unity FBX: {FBX_PATH}")
    print(f"Rendered preview: {PREVIEW_PATH}")


if __name__ == "__main__":
    main()
