from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build a normalized Blender review gallery from FBX assets.")
    parser.add_argument("--input-root", required=True)
    parser.add_argument("--output-blend", required=True)
    parser.add_argument("--output-report", required=True)
    parser.add_argument("--output-preview", required=True)
    parser.add_argument("--library-name", default="External Asset Library")
    parser.add_argument("--target-size", type=float, default=2.0)
    parser.add_argument("--columns", type=int, default=8)
    parser.add_argument("--camera-mode", choices=("perspective", "top"), default="perspective")
    parser.add_argument("--material-profile", choices=("imported", "fleet-i"), default="imported")
    parser.add_argument("--texture-root")
    parser.add_argument("--pack-resources", action="store_true")
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def ensure_collection(name: str, parent: bpy.types.Collection | None = None) -> bpy.types.Collection:
    collection = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    target = parent or bpy.context.scene.collection
    if collection.name not in {child.name for child in target.children}:
        target.children.link(collection)
    return collection


def move_object_to_collection(obj: bpy.types.Object, collection: bpy.types.Collection) -> None:
    for owner in list(obj.users_collection):
        owner.objects.unlink(obj)
    collection.objects.link(obj)


def triangulated_count(obj: bpy.types.Object) -> int:
    if obj.type != "MESH":
        return 0
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    points: list[Vector] = []
    for obj in objects:
        if obj.type != "MESH":
            continue
        points.extend(obj.matrix_world @ Vector(corner) for corner in obj.bound_box)
    if not points:
        return Vector((-0.5, -0.5, -0.5)), Vector((0.5, 0.5, 0.5))
    return (
        Vector(tuple(min(point[i] for point in points) for i in range(3))),
        Vector(tuple(max(point[i] for point in points) for i in range(3))),
    )


def category_for(path: Path) -> str:
    text = str(path).lower()
    name = path.stem.lower()
    if "missile" in text:
        return "Missiles"
    if "mine" in text:
        return "Mines"
    if "torpedo" in text:
        return "Torpedo Tubes"
    if "flak" in text:
        return "Flak"
    if "laser" in text:
        return "Energy Weapons"
    if "facility" in text or "hangar" in text or "platform" in text or "antenna" in text:
        return "Functional Modules"
    if "barrel" in name:
        return "Barrels"
    if "turret" in name or "gunport" in name or "gunport" in text:
        return "Turrets"
    return "Ships"


def fallback_material(category: str) -> bpy.types.Material:
    name = f"Review_{category}"
    existing = bpy.data.materials.get(name)
    if existing:
        return existing
    colors = {
        "Missiles": (0.55, 0.12, 0.05, 1.0),
        "Mines": (0.24, 0.07, 0.32, 1.0),
        "Torpedo Tubes": (0.42, 0.14, 0.04, 1.0),
        "Flak": (0.12, 0.28, 0.34, 1.0),
        "Energy Weapons": (0.03, 0.38, 0.48, 1.0),
        "Functional Modules": (0.26, 0.31, 0.12, 1.0),
        "Barrels": (0.13, 0.16, 0.21, 1.0),
        "Turrets": (0.21, 0.24, 0.3, 1.0),
        "Ships": (0.16, 0.2, 0.27, 1.0),
    }
    material = bpy.data.materials.new(name)
    material.diffuse_color = colors.get(category, (0.2, 0.22, 0.26, 1.0))
    material.metallic = 0.72
    material.roughness = 0.3
    return material


def image_texture(path: Path, non_color: bool = False) -> bpy.types.Image:
    image = bpy.data.images.load(str(path), check_existing=True)
    if non_color:
        try:
            image.colorspace_settings.name = "Non-Color"
        except TypeError:
            pass
    return image


def find_texture(texture_root: Path, filename: str) -> Path | None:
    matches = sorted(texture_root.rglob(filename))
    return matches[0] if matches else None


def fleet_i_family(source: Path) -> tuple[str, str] | None:
    normalized = str(source).replace("\\", "/").lower()
    for marker, prefix in (
        ("corvettes/corvetteweapons/", "CorvetteWeapons"),
        ("corvettes/corvettemissiles/", "CorvetteMissiles"),
        ("corvettes/corvettemines/", "CorvetteMines"),
        ("frigates/frigateweapons/", "FrigateWeapons"),
        ("destroyers/destroyerweapons/", "DestroyerWeapons"),
        ("mothership1/mothershipweaponequipment/", "MothershipWeaponsEquipment"),
    ):
        if marker in normalized:
            return marker.rstrip("/").replace("/", "_"), prefix
    return None


def build_fleet_i_material(
    source: Path, texture_root: Path
) -> tuple[bpy.types.Material, list[str], str | None]:
    family = fleet_i_family(source)
    if family is None:
        return fallback_material(category_for(source)), [], None
    family_key, prefix = family
    name = f"FleetI_{prefix}_Grey_PBR"
    existing = bpy.data.materials.get(name)
    if existing:
        return (
            existing,
            [
                node.image.filepath
                for node in existing.node_tree.nodes
                if node.type == "TEX_IMAGE" and node.image
            ],
            existing.get("unity_material_file"),
        )

    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = (0.24, 0.27, 0.31, 1.0)
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    output.location = (850, 0)
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.location = (520, 0)
    shader.inputs["Metallic"].default_value = 0.0
    shader.inputs["Roughness"].default_value = 0.33
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])

    base = (
        find_texture(texture_root, f"{prefix}GreyAlbedoAO.png")
        or find_texture(texture_root, f"{prefix}GreyAlbedo.png")
        or find_texture(texture_root, f"{prefix}AlbedoAO.png")
        or find_texture(texture_root, f"{prefix}Albedo.png")
    )
    ao = find_texture(texture_root, f"{prefix}AO.png")
    normal = find_texture(texture_root, f"{prefix}Normal.png")
    specular = (
        find_texture(texture_root, f"{prefix}PBRSpecular.tga")
        or find_texture(texture_root, f"{prefix}PBRSpecular1.tga")
        or find_texture(texture_root, f"{prefix}PBRSpecular2.tga")
    )
    illumination = find_texture(texture_root, f"{prefix}Illumination.tga")
    unity_material = (
        find_texture(texture_root, f"{prefix}Grey.mat")
        or find_texture(texture_root, f"{prefix}.mat")
        or (
            find_texture(texture_root, "CorvetteMissile.mat")
            if prefix == "CorvetteMissiles"
            else None
        )
    )
    used: list[str] = []

    if base:
        base_node = nodes.new("ShaderNodeTexImage")
        base_node.name = "BaseColor"
        base_node.label = base.name
        base_node.location = (-700, 180)
        base_node.image = image_texture(base)
        used.append(str(base))
        if ao and "albedoao" not in base.name.lower():
            ao_node = nodes.new("ShaderNodeTexImage")
            ao_node.name = "AmbientOcclusion"
            ao_node.label = ao.name
            ao_node.location = (-700, -20)
            ao_node.image = image_texture(ao, non_color=True)
            multiply = nodes.new("ShaderNodeMixRGB")
            multiply.blend_type = "MULTIPLY"
            multiply.inputs["Fac"].default_value = 1.0
            multiply.location = (-280, 150)
            links.new(base_node.outputs["Color"], multiply.inputs[1])
            links.new(ao_node.outputs["Color"], multiply.inputs[2])
            links.new(multiply.outputs["Color"], shader.inputs["Base Color"])
            used.append(str(ao))
        else:
            links.new(base_node.outputs["Color"], shader.inputs["Base Color"])

    if normal:
        normal_node = nodes.new("ShaderNodeTexImage")
        normal_node.name = "Normal"
        normal_node.label = normal.name
        normal_node.location = (-700, -260)
        normal_node.image = image_texture(normal, non_color=True)
        normal_map = nodes.new("ShaderNodeNormalMap")
        normal_map.inputs["Strength"].default_value = 0.75
        normal_map.location = (-280, -250)
        links.new(normal_node.outputs["Color"], normal_map.inputs["Color"])
        links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
        used.append(str(normal))

    if specular:
        specular_node = nodes.new("ShaderNodeTexImage")
        specular_node.name = "SpecularSmoothness"
        specular_node.label = f"{specular.name} (RGB=Specular A=Smoothness)"
        specular_node.location = (-700, -520)
        specular_node.image = image_texture(specular, non_color=True)
        rgb_to_bw = nodes.new("ShaderNodeRGBToBW")
        rgb_to_bw.location = (-420, -580)
        invert = nodes.new("ShaderNodeMath")
        invert.operation = "SUBTRACT"
        invert.inputs[0].default_value = 1.0
        invert.location = (-40, -480)
        links.new(specular_node.outputs["Color"], shader.inputs["Specular Tint"])
        links.new(specular_node.outputs["Color"], rgb_to_bw.inputs["Color"])
        links.new(rgb_to_bw.outputs["Val"], shader.inputs["Specular IOR Level"])
        links.new(specular_node.outputs["Alpha"], invert.inputs[1])
        links.new(invert.outputs[0], shader.inputs["Roughness"])
        used.append(str(specular))

    if illumination:
        emission_node = nodes.new("ShaderNodeTexImage")
        emission_node.name = "Emission"
        emission_node.label = illumination.name
        emission_node.location = (-250, 420)
        emission_node.image = image_texture(illumination, non_color=True)
        emission_input = shader.inputs.get("Emission Color") or shader.inputs.get("Emission")
        if emission_input:
            links.new(emission_node.outputs["Color"], emission_input)
        if shader.inputs.get("Emission Strength"):
            shader.inputs["Emission Strength"].default_value = 2.5
        used.append(str(illumination))

    material["source_family"] = family_key
    material["source_textures"] = json.dumps(used, ensure_ascii=False)
    if unity_material:
        material["unity_material_file"] = str(unity_material)
    return material, used, str(unity_material) if unity_material else None


def repair_imported_images(source: Path) -> list[dict[str, str]]:
    repairs: list[dict[str, str]] = []
    texture_directory = source.parent / "textures"
    if not texture_directory.exists():
        return repairs
    for image in bpy.data.images:
        raw_name = Path(bpy.path.abspath(image.filepath)).name or image.name
        if not raw_name.lower().endswith(".astc.gz"):
            continue
        decoded_name = Path(raw_name).with_suffix("").with_suffix(".png").name
        decoded = texture_directory / decoded_name
        if not decoded.exists():
            continue
        original = image.filepath
        image.filepath = str(decoded)
        image.source = "FILE"
        image.reload()
        try:
            image.colorspace_settings.name = "Non-Color"
        except TypeError:
            pass
        repairs.append({"image": image.name, "original": original, "replacement": str(decoded)})
    return repairs


def add_label(text: str, location: Vector, size: float, collection: bpy.types.Collection) -> None:
    curve = bpy.data.curves.new(f"Label_{text}", "FONT")
    curve.body = text
    curve.align_x = "CENTER"
    curve.size = size
    curve.extrude = size * 0.015
    obj = bpy.data.objects.new(f"Label_{text}", curve)
    obj.location = location
    obj.rotation_euler = (math.radians(70.0), 0.0, 0.0)
    collection.objects.link(obj)


def import_asset(
    source: Path,
    collection: bpy.types.Collection,
    target_size: float,
    position: Vector,
    material_profile: str,
    texture_root: Path | None,
) -> dict[str, object]:
    before = set(bpy.data.objects)
    if source.suffix.lower() == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(source), use_image_search=True)
    elif source.suffix.lower() in {".glb", ".gltf"}:
        bpy.ops.import_scene.gltf(filepath=str(source))
    elif source.suffix.lower() == ".obj":
        bpy.ops.wm.obj_import(filepath=str(source))
    else:
        raise RuntimeError(f"Unsupported model format: {source.suffix}")
    imported = [obj for obj in bpy.data.objects if obj not in before]
    if not imported:
        raise RuntimeError(f"No objects imported from {source}")

    for obj in imported:
        move_object_to_collection(obj, collection)

    roots = [obj for obj in imported if obj.parent not in imported]
    holder = bpy.data.objects.new(f"AssetRoot_{source.stem}", None)
    holder["source_asset"] = str(source)
    collection.objects.link(holder)
    for obj in roots:
        world = obj.matrix_world.copy()
        obj.parent = holder
        obj.matrix_world = world

    minimum, maximum = bounds(imported)
    dimensions = maximum - minimum
    center = (minimum + maximum) * 0.5
    longest = max(dimensions)
    scale = target_size / longest if longest > 1.0e-8 else 1.0
    holder.scale = (scale, scale, scale)
    holder.location = position - center * scale

    mesh_objects = [obj for obj in imported if obj.type == "MESH"]
    category = category_for(source)
    texture_paths: list[str] = []
    unity_material: str | None = None
    image_repairs: list[dict[str, str]] = []
    if material_profile == "fleet-i" and texture_root is not None:
        restored, texture_paths, unity_material = build_fleet_i_material(source, texture_root)
        for obj in mesh_objects:
            obj.data.materials.clear()
            obj.data.materials.append(restored)
    else:
        image_repairs = repair_imported_images(source)
        for obj in mesh_objects:
            if not obj.material_slots:
                obj.data.materials.append(fallback_material(category))
    materials = sorted(
        {
            slot.material.name
            for obj in mesh_objects
            for slot in obj.material_slots
            if slot.material is not None
        }
    )
    return {
        "name": source.stem,
        "source": str(source),
        "category": category,
        "objectCount": len(imported),
        "meshCount": len(mesh_objects),
        "meshNames": sorted(obj.name for obj in mesh_objects),
        "rootObjectNames": sorted(obj.name for obj in roots),
        "triangles": sum(triangulated_count(obj) for obj in mesh_objects),
        "vertices": sum(len(obj.data.vertices) for obj in mesh_objects),
        "materials": materials,
        "materialCount": len(materials),
        "texturePaths": sorted(set(texture_paths)),
        "unityMaterial": unity_material,
        "imageRepairs": image_repairs,
        "sourceDimensions": [round(value, 6) for value in dimensions],
        "normalizedScale": scale,
    }


def look_at(obj: bpy.types.Object, target: Vector) -> None:
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def create_environment(width: float, height: float, camera_mode: str) -> None:
    scene = bpy.context.scene
    world = bpy.data.worlds.new("ReviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.008, 0.012, 0.02, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.18
    scene.world = world

    bpy.ops.mesh.primitive_plane_add(size=max(width, height) * 2.4, location=(width * 0.5, -height * 0.5, -0.72))
    floor = bpy.context.object
    floor.name = "ReviewFloor"
    floor_material = bpy.data.materials.new("ReviewFloorMaterial")
    floor_material.diffuse_color = (0.015, 0.02, 0.03, 1.0)
    floor_material.use_nodes = True
    floor_bsdf = floor_material.node_tree.nodes.get("Principled BSDF")
    floor_bsdf.inputs["Base Color"].default_value = (0.07, 0.085, 0.12, 1.0)
    floor_bsdf.inputs["Roughness"].default_value = 0.78
    floor.data.materials.append(floor_material)

    for name, location, energy, size, color in (
        ("Key", (width * 0.2, -height * 0.15, max(width, height) * 0.85), 2600.0, 12.0, (0.72, 0.84, 1.0)),
        ("Fill", (width * 0.9, -height * 0.7, max(width, height) * 0.55), 1800.0, 10.0, (1.0, 0.42, 0.2)),
    ):
        data = bpy.data.lights.new(name, "AREA")
        data.energy = energy
        data.shape = "DISK"
        data.size = size
        data.color = color
        light = bpy.data.objects.new(name, data)
        light.location = location
        bpy.context.scene.collection.objects.link(light)
        look_at(light, Vector((width * 0.5, -height * 0.5, 0.0)))

    camera_data = bpy.data.cameras.new("AssetReviewCamera")
    camera = bpy.data.objects.new("AssetReviewCamera", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    if camera_mode == "top":
        camera.location = (width * 0.5, -height * 0.5, max(width, height) * 1.3)
        camera_data.type = "ORTHO"
        camera_data.ortho_scale = max(height * 1.08, width * 0.56)
        look_at(camera, Vector((width * 0.5, -height * 0.5, 0.0)))
    else:
        camera.location = (width * 0.5, -height, max(width, height) * 1.8)
        camera_data.lens = 55.0
        look_at(camera, Vector((width * 0.5, -height * 0.5, 0.0)))
    scene.camera = camera

    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 2600
    scene.render.resolution_y = max(1400, int(2600 * height / max(width, 1.0)))
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = 1.0


def main() -> None:
    args = parse_args()
    input_root = Path(args.input_root).resolve()
    output_blend = Path(args.output_blend).resolve()
    output_report = Path(args.output_report).resolve()
    output_preview = Path(args.output_preview).resolve()
    texture_root = Path(args.texture_root).resolve() if args.texture_root else None
    if args.material_profile == "fleet-i" and texture_root is None:
        raise RuntimeError("--texture-root is required for --material-profile fleet-i")
    sources = sorted(
        (
            path
            for path in input_root.rglob("*")
            if path.is_file() and path.suffix.lower() in {".fbx", ".glb", ".gltf", ".obj"}
        ),
        key=lambda path: str(path).lower(),
    )
    if not sources:
        raise RuntimeError(f"No FBX files found below {input_root}")

    reset_scene()
    gallery = ensure_collection("00_Asset_Gallery")
    categories: dict[str, bpy.types.Collection] = {}
    spacing_x = args.target_size * 1.65
    spacing_y = args.target_size * 1.55
    rows = math.ceil(len(sources) / args.columns)
    reports: list[dict[str, object]] = []

    for index, source in enumerate(sources):
        category = category_for(source)
        category_collection = categories.get(category)
        if category_collection is None:
            category_collection = ensure_collection(f"10_{category}", gallery)
            categories[category] = category_collection
        column = index % args.columns
        row = index // args.columns
        position = Vector((column * spacing_x, -row * spacing_y, 0.0))
        try:
            report = import_asset(
                source,
                category_collection,
                args.target_size,
                position,
                args.material_profile,
                texture_root,
            )
            report["success"] = True
        except Exception as exc:
            report = {
                "name": source.stem,
                "source": str(source),
                "category": category,
                "success": False,
                "error": repr(exc),
            }
        reports.append(report)
        add_label(source.stem, position + Vector((0.0, -args.target_size * 0.72, -0.55)), args.target_size * 0.12, category_collection)

    width = max(1, min(args.columns, len(sources))) * spacing_x
    height = max(1, rows) * spacing_y
    create_environment(width, height, args.camera_mode)

    scene = bpy.context.scene
    scene["library_name"] = args.library_name
    scene["asset_count"] = len(sources)
    scene["source_root"] = str(input_root)
    output_blend.parent.mkdir(parents=True, exist_ok=True)
    output_report.parent.mkdir(parents=True, exist_ok=True)
    output_preview.parent.mkdir(parents=True, exist_ok=True)
    if args.pack_resources:
        bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(output_blend))
    scene.render.filepath = str(output_preview)
    bpy.ops.render.render(write_still=True)
    report = {
        "success": all(item.get("success") for item in reports),
        "libraryName": args.library_name,
        "inputRoot": str(input_root),
        "blend": str(output_blend),
        "preview": str(output_preview),
        "assetCount": len(reports),
        "materialProfile": args.material_profile,
        "resourcesPacked": bool(args.pack_resources),
        "imageRepairCount": sum(len(item.get("imageRepairs", [])) for item in reports),
        "categoryCounts": {
            category: sum(1 for item in reports if item["category"] == category)
            for category in sorted(categories)
        },
        "assets": reports,
    }
    output_report.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"success": report["success"], "assetCount": len(reports), "report": str(output_report)}))


if __name__ == "__main__":
    main()
