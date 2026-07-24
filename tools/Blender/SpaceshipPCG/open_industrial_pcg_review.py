"""Open an interactive Blender scene with the industrial hull and player greebles."""

from __future__ import annotations

from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[3]
HULL_SOURCE = (
    PROJECT_ROOT
    / "Library"
    / "SpaceshipPCGStaging"
    / "industrial-sample"
    / "Hulls"
    / "balanced_00.fbx"
)
MODULE_ROOT = (
    PROJECT_ROOT
    / "Library"
    / "SpaceshipPCGStaging"
    / "industrial-modules"
    / "Modules"
)
REVIEW_FILE = (
    PROJECT_ROOT
    / "Library"
    / "SpaceshipPCGReview"
    / "industrial_8metal_review.blend"
)
PBR_LIBRARY = (
    PROJECT_ROOT
    / "Assets"
    / "SpacecraftEditor"
    / "Art"
    / "Materials"
    / "Paints"
    / "PBRLibrary"
)

MODULES = (
    ("SweptWing", (-5.2, 3.2, 0.0), "deep_space_blue"),
    ("DeltaWing", (-3.1, 3.2, 0.0), "gunmetal"),
    ("Canard", (-1.0, 3.2, 0.0), "ceramic_white"),
    ("VerticalFin", (1.1, 3.2, 0.0), "warning_red"),
    ("Radiator", (3.2, 3.2, 0.0), "industrial_copper"),
    ("SensorMast", (5.3, 3.2, 0.0), "explorer_green"),
    ("EngineNacelle", (-3.3, -3.3, 0.0), "brushed_brass"),
    ("ArmorFairing", (3.3, -3.3, 0.0), "graphite_pitted"),
)


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def texture_node(nodes, path: Path, non_color: bool):
    image = bpy.data.images.load(str(path), check_existing=True)
    if non_color:
        image.colorspace_settings.name = "Non-Color"
    node = nodes.new("ShaderNodeTexImage")
    node.image = image
    node.interpolation = "Linear"
    node.extension = "REPEAT"
    return node


def create_library_material(material_id: str):
    material = bpy.data.materials.get("Library_" + material_id)
    if material is None:
        material = bpy.data.materials.new("Library_" + material_id)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    output.location = (820, 0)
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    principled.location = (560, 0)
    links.new(principled.outputs["BSDF"], output.inputs["Surface"])

    base = texture_node(
        nodes,
        PBR_LIBRARY / f"{material_id}_BaseColor.png",
        False,
    )
    base.location = (-620, 240)
    ao = texture_node(nodes, PBR_LIBRARY / f"{material_id}_AO.png", True)
    ao.location = (-620, 20)
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1.0
    multiply.location = (-120, 220)
    links.new(base.outputs["Color"], multiply.inputs[1])
    links.new(ao.outputs["Color"], multiply.inputs[2])
    links.new(multiply.outputs["Color"], principled.inputs["Base Color"])

    metallic = texture_node(
        nodes,
        PBR_LIBRARY / f"{material_id}_Metallic.png",
        True,
    )
    metallic.location = (-620, -180)
    links.new(metallic.outputs["Color"], principled.inputs["Metallic"])
    roughness = texture_node(
        nodes,
        PBR_LIBRARY / f"{material_id}_Roughness.png",
        True,
    )
    roughness.location = (-620, -360)
    links.new(roughness.outputs["Color"], principled.inputs["Roughness"])

    normal_texture = texture_node(
        nodes,
        PBR_LIBRARY / f"{material_id}_Normal.png",
        True,
    )
    normal_texture.location = (-620, -560)
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.location = (-120, -500)
    normal_map.inputs["Strength"].default_value = 0.65
    links.new(normal_texture.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])
    return material


def apply_library_materials(objects) -> None:
    library = {
        "HullPrimary": create_library_material("deep_space_blue"),
        "HullDark": create_library_material("gunmetal"),
        "Accent": create_library_material("industrial_copper"),
    }
    for obj in objects:
        for slot in obj.material_slots:
            if slot.material is None:
                continue
            source_name = slot.material.name.split(".")[0]
            if source_name in library:
                slot.material = library[source_name]


def apply_module_material(objects, material_id: str) -> None:
    material = create_library_material(material_id)
    for obj in objects:
        for slot in obj.material_slots:
            if slot.material is None:
                continue
            if slot.material.name.split(".")[0] != "HullEmission":
                slot.material = material


def import_lod0(source: Path, offset, label_text: str, scale: float = 1.0):
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
        root.scale = (scale, scale, scale)

    visible = []
    for obj in imported:
        is_lod0 = obj.type == "MESH" and "Render_LOD0" in obj.name
        if obj.type == "MESH":
            obj.hide_set(not is_lod0)
            obj.hide_render = not is_lod0
        if is_lod0:
            visible.append(obj)

    label = bpy.data.curves.new(label_text + "_Label", "FONT")
    label.body = label_text
    label.align_x = "CENTER"
    label.size = 0.28 if scale <= 1.0 else 0.42
    label.extrude = 0.008
    label_obj = bpy.data.objects.new(label_text + "_Label", label)
    bpy.context.scene.collection.objects.link(label_obj)
    label_obj.location = (offset[0], offset[1] - (1.15 if scale <= 1.0 else 3.5), 0.02)
    return visible


def configure_scene() -> None:
    scene = bpy.context.scene
    scene.name = "Industrial PCG + Local Metal PBR Library"
    scene.world.color = (0.025, 0.04, 0.065)
    scene.view_settings.look = "AgX - Medium High Contrast"

    visible = import_lod0(HULL_SOURCE, (0.0, -0.2, 1.5), "Industrial Hull", 1.35)
    apply_library_materials(visible)
    for module_id, offset, material_id in MODULES:
        module_objects = import_lod0(
            MODULE_ROOT / f"{module_id}.fbx",
            offset,
            f"{module_id}: {material_id}",
        )
        apply_module_material(module_objects, material_id)
        visible.extend(module_objects)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in visible:
        obj.select_set(True)
    if visible:
        bpy.context.view_layer.objects.active = visible[0]

    for area in bpy.context.screen.areas:
        if area.type != "VIEW_3D":
            continue
        space = area.spaces.active
        space.shading.type = "MATERIAL"
        space.shading.light = "STUDIO"
        space.shading.studiolight_rotate_z = 0.55
        space.shading.studiolight_background_alpha = 0.45
        space.clip_end = 1000.0
        region = next((item for item in area.regions if item.type == "WINDOW"), None)
        if region is not None:
            with bpy.context.temp_override(area=area, region=region):
                bpy.ops.view3d.view_selected(use_all_regions=False)

    REVIEW_FILE.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(REVIEW_FILE))


clear_scene()
configure_scene()
print(f"[SpaceshipPCG] opened industrial review: {REVIEW_FILE}")
