"""Blender 5.1 offline generator for the Unity spacecraft PCG pipeline.

The upstream generator is imported as a library and left unmodified. This
wrapper replaces its Blender 2.x material path, normalizes its output to the
Unity contract, creates deterministic LOD/collision meshes, and exports FBX.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import os
import random
import sys
import traceback
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parents[2]
UPSTREAM_PATH = PROJECT_ROOT / "Tools" / "ThirdParty" / "SpaceshipGenerator" / "spaceship_generator.py"
PBR_IMAGES = {}


def parse_args() -> argparse.Namespace:
    argv = sys.argv
    argv = argv[argv.index("--") + 1 :] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--scope", choices=("all", "hull", "module"), default="all")
    parser.add_argument("--id", default="")
    parser.add_argument("--staging", required=True)
    parser.add_argument("--report", required=True)
    return parser.parse_args(argv)


def load_upstream():
    spec = importlib.util.spec_from_file_location("pcg_upstream_spaceship_generator", UPSTREAM_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot import upstream generator: {UPSTREAM_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)

    native_create_cone = bmesh.ops.create_cone
    native_create_icosphere = bmesh.ops.create_icosphere

    def compatible_create_cone(target_bmesh, **kwargs):
        # Blender 5 renamed the diameter arguments to radius arguments.
        if "diameter1" in kwargs:
            kwargs["radius1"] = kwargs.pop("diameter1") * 0.5
        if "diameter2" in kwargs:
            kwargs["radius2"] = kwargs.pop("diameter2") * 0.5
        return native_create_cone(target_bmesh, **kwargs)

    def compatible_create_icosphere(target_bmesh, **kwargs):
        if "diameter" in kwargs:
            kwargs["radius"] = kwargs.pop("diameter") * 0.5
        return native_create_icosphere(target_bmesh, **kwargs)

    module.bmesh.ops.create_cone = compatible_create_cone
    module.bmesh.ops.create_icosphere = compatible_create_icosphere

    def compatible_materials():
        # Upstream geometry expects five material indices. They are collapsed
        # into the four stable Unity material slots after generation.
        return [new_material(f"_UpstreamSlot{index}", (0.2, 0.25, 0.3, 1.0)) for index in range(5)]

    def compatible_surface_antenna(bm, face):
        if not face.is_valid or len(face.verts[:]) < 4:
            return
        horizontal_step = module.randint(4, 10)
        vertical_step = module.randint(4, 10)
        for horizontal in range(horizontal_step):
            top = face.verts[0].co.lerp(
                face.verts[1].co, (horizontal + 1) / float(horizontal_step + 1)
            )
            bottom = face.verts[3].co.lerp(
                face.verts[2].co, (horizontal + 1) / float(horizontal_step + 1)
            )
            for vertical in range(vertical_step):
                if module.random() <= 0.9:
                    continue
                pos = top.lerp(bottom, (vertical + 1) / float(vertical_step + 1))
                face_size = math.sqrt(face.calc_area())
                depth = module.uniform(0.1, 1.5) * face_size
                depth_short = depth * module.uniform(0.02, 0.15)
                base_diameter = module.uniform(0.005, 0.05)
                material_index = (
                    module.Material.hull
                    if module.random() > 0.5
                    else module.Material.hull_dark
                )
                # Blender 5 requires an integer segment count. Upstream 1.1.3
                # accidentally supplies the float returned by random.uniform.
                segments = module.randint(3, 6)
                for result in (
                    bmesh.ops.create_cone(
                        bm,
                        cap_ends=False,
                        cap_tris=False,
                        segments=segments,
                        diameter1=0,
                        diameter2=base_diameter,
                        depth=depth,
                        matrix=module.get_face_matrix(
                            face, pos + face.normal * depth * 0.5
                        ),
                    ),
                    bmesh.ops.create_cone(
                        bm,
                        cap_ends=True,
                        cap_tris=False,
                        segments=segments,
                        diameter1=base_diameter * module.uniform(1, 1.5),
                        diameter2=base_diameter * module.uniform(1.5, 2),
                        depth=depth_short,
                        matrix=module.get_face_matrix(
                            face, pos + face.normal * depth_short * 0.45
                        ),
                    ),
                ):
                    for vert in result["verts"]:
                        for linked_face in vert.link_faces:
                            linked_face.material_index = material_index

    module.create_materials = compatible_materials
    module.add_surface_antenna_to_face = compatible_surface_antenna
    return module


def _texture_node(nodes, image_key: str, label: str, non_color: bool = False):
    image = PBR_IMAGES.get(image_key)
    if image is None:
        return None
    node = nodes.new("ShaderNodeTexImage")
    node.name = label
    node.label = label
    node.image = image
    node.interpolation = "Linear"
    node.extension = "REPEAT"
    if non_color:
        image.colorspace_settings.name = "Non-Color"
    return node


def new_material(
    name: str,
    color,
    emission: float = 0.0,
    metallic: float = 0.75,
    roughness_value: float = 0.38,
):
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.diffuse_color = color
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    output.location = (900, 0)
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    principled.location = (620, 0)
    links.new(principled.outputs["BSDF"], output.inputs["Surface"])

    base = principled.inputs.get("Base Color")
    if base is not None:
        base.default_value = color
    roughness = principled.inputs.get("Roughness")
    if roughness is not None:
        roughness.default_value = roughness_value
    metallic_input = principled.inputs.get("Metallic")
    if metallic_input is not None:
        metallic_input.default_value = metallic

    base_texture = _texture_node(nodes, "BaseColor", "PBR BaseColor")
    ao_texture = _texture_node(nodes, "AO", "PBR AO", True)
    if base_texture is not None:
        tint = nodes.new("ShaderNodeMixRGB")
        tint.blend_type = "MULTIPLY"
        tint.inputs[0].default_value = 1.0
        tint.inputs[2].default_value = color
        tint.location = (170, 180)
        links.new(base_texture.outputs["Color"], tint.inputs[1])
        if ao_texture is not None:
            ao_mix = nodes.new("ShaderNodeMixRGB")
            ao_mix.blend_type = "MULTIPLY"
            ao_mix.inputs[0].default_value = 1.0
            ao_mix.location = (400, 180)
            links.new(tint.outputs["Color"], ao_mix.inputs[1])
            links.new(ao_texture.outputs["Color"], ao_mix.inputs[2])
            if base is not None:
                links.new(ao_mix.outputs["Color"], base)
        elif base is not None:
            links.new(tint.outputs["Color"], base)

    metallic_texture = _texture_node(nodes, "Metallic", "PBR Metallic", True)
    if metallic_texture is not None and metallic_input is not None and emission <= 0:
        multiply = nodes.new("ShaderNodeMath")
        multiply.operation = "MULTIPLY"
        multiply.inputs[1].default_value = metallic
        links.new(metallic_texture.outputs["Color"], multiply.inputs[0])
        links.new(multiply.outputs[0], metallic_input)

    roughness_texture = _texture_node(nodes, "Roughness", "PBR Roughness", True)
    if roughness_texture is not None and roughness is not None:
        links.new(roughness_texture.outputs["Color"], roughness)

    normal_texture = _texture_node(nodes, "Normal", "PBR Normal", True)
    normal_input = principled.inputs.get("Normal")
    if normal_texture is not None and normal_input is not None:
        normal_map = nodes.new("ShaderNodeNormalMap")
        normal_map.inputs["Strength"].default_value = 0.55
        links.new(normal_texture.outputs["Color"], normal_map.inputs["Color"])
        links.new(normal_map.outputs["Normal"], normal_input)

    emission_color = principled.inputs.get("Emission Color") or principled.inputs.get("Emission")
    emission_strength = principled.inputs.get("Emission Strength")
    if emission_color is not None:
        emission_color.default_value = color
    if emission_strength is not None:
        emission_strength.default_value = emission
    emission_texture = _texture_node(nodes, "Emission", "PBR Emission")
    if emission > 0 and emission_texture is not None and emission_color is not None:
        links.new(emission_texture.outputs["Color"], emission_color)
    return material


def stable_materials():
    return [
        new_material("HullPrimary", (0.16, 0.29, 0.34, 1.0), metallic=0.78, roughness_value=0.34),
        new_material("HullDark", (0.025, 0.04, 0.05, 1.0), metallic=0.88, roughness_value=0.52),
        new_material(
            "HullEmission",
            (0.02, 0.62, 1.0, 1.0),
            5.0,
            metallic=0.05,
            roughness_value=0.2,
        ),
        new_material("Accent", (0.38, 0.42, 0.44, 1.0), metallic=0.96, roughness_value=0.24),
    ]


def _hash01(x: int, y: int, salt: int = 0) -> float:
    value = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    value = ((value ^ (value >> 13)) * 1274126177) & 0xFFFFFFFF
    return (value & 0x00FFFFFF) / float(0x00FFFFFF)


def _save_pbr_image(name: str, path: Path, pixels, size: int, non_color: bool):
    image = bpy.data.images.get(name) or bpy.data.images.new(
        name,
        width=size,
        height=size,
        alpha=True,
        float_buffer=False,
    )
    if image.size[0] != size or image.size[1] != size:
        image.scale(size, size)
    # Blender 5.1 rebuilds the generated-image pixel buffer when the color
    # space changes, so this must happen before writing pixels.
    image.colorspace_settings.name = "Non-Color" if non_color else "sRGB"
    image.pixels.foreach_set(pixels)
    image.update()
    image.file_format = "PNG"
    image.filepath_raw = str(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save()
    return image


def generate_pbr_textures(staging: Path, size: int = 256):
    """Create deterministic, tileable industrial PBR maps shared by the fleet."""
    pbr_root = staging / "PBR"
    height = [0.0] * (size * size)
    base = []
    metallic = []
    roughness = []
    ao = []
    emission = []
    packed = []
    for y in range(size):
        for x in range(size):
            u = x / float(size)
            v = y / float(size)
            noise = _hash01(x, y, 7)
            fine = math.sin(v * math.pi * 128.0) * 0.018
            grime = max(0.0, (_hash01(x // 8, y // 8, 23) - 0.68) * 2.4)
            scratch = 1.0 if (_hash01(x // 2, y, 41) > 0.992 and (x + y) % 11 < 7) else 0.0
            chip = 1.0 if _hash01(x // 3, y // 3, 59) > 0.965 else 0.0
            panel_line = 1.0 if x % 64 in (0, 1) or y % 64 in (0, 1) else 0.0
            h = (noise - 0.5) * 0.16 + fine + chip * 0.22 - grime * 0.08
            height[y * size + x] = h

            base_value = max(0.22, min(1.0, 0.88 + fine + (noise - 0.5) * 0.055))
            dirt_scale = 1.0 - grime * 0.38 - panel_line * 0.22
            chip_scale = 1.0 + chip * 0.12 + scratch * 0.18
            value = max(0.08, min(1.0, base_value * dirt_scale * chip_scale))
            base.extend((value * 0.92, value * 0.97, value, 1.0))

            metal_value = max(0.0, min(1.0, 0.82 + chip * 0.16 - grime * 0.35))
            rough_value = max(
                0.08,
                min(0.96, 0.34 + grime * 0.44 + panel_line * 0.18 - scratch * 0.16 + noise * 0.06),
            )
            ao_value = max(0.32, min(1.0, 0.96 - grime * 0.42 - panel_line * 0.26))
            metallic.extend((metal_value, metal_value, metal_value, 1.0))
            roughness.extend((rough_value, rough_value, rough_value, 1.0))
            ao.extend((ao_value, ao_value, ao_value, 1.0))
            packed.extend((metal_value, 0.0, 0.0, 1.0 - rough_value))

            marker = (
                (x % 96 in range(6, 10) and y % 96 in range(8, 48))
                or (y % 96 in range(52, 56) and x % 96 in range(12, 42))
            )
            emission.extend(
                (0.02, 0.72, 1.0, 1.0)
                if marker
                else (0.002, 0.025, 0.04, 1.0)
            )

    normal = []
    for y in range(size):
        for x in range(size):
            left = height[y * size + ((x - 1) % size)]
            right = height[y * size + ((x + 1) % size)]
            down = height[((y - 1) % size) * size + x]
            up = height[((y + 1) % size) * size + x]
            vector = Vector((-(right - left) * 2.2, -(up - down) * 2.2, 1.0)).normalized()
            normal.extend(
                (
                    vector.x * 0.5 + 0.5,
                    vector.y * 0.5 + 0.5,
                    vector.z * 0.5 + 0.5,
                    1.0,
                )
            )

    outputs = {
        "BaseColor": (base, False),
        "Metallic": (metallic, True),
        "Roughness": (roughness, True),
        "Normal": (normal, True),
        "AO": (ao, True),
        "Emission": (emission, False),
        "MetallicSmoothness": (packed, True),
    }
    PBR_IMAGES.clear()
    for key, (pixels, non_color) in outputs.items():
        path = pbr_root / f"IndustrialHull_{key}.png"
        PBR_IMAGES[key] = _save_pbr_image(
            f"IndustrialHull_{key}",
            path,
            pixels,
            size,
            non_color,
        )
    return {key: str((pbr_root / f"IndustrialHull_{key}.png").relative_to(staging)).replace("\\", "/") for key in outputs}


def clean_scene():
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_modifiers(obj):
    activate(obj)
    for modifier in list(obj.modifiers):
        try:
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        except RuntimeError:
            obj.modifiers.remove(modifier)


def apply_hard_surface_shading(obj):
    """Bake bevels and area-weighted normals for crisp industrial edge highlights."""
    activate(obj)
    try:
        weighted = obj.modifiers.new("WeightedNormals", "WEIGHTED_NORMAL")
        if hasattr(weighted, "keep_sharp"):
            weighted.keep_sharp = True
        if hasattr(weighted, "weight"):
            weighted.weight = 70
    except (RuntimeError, TypeError):
        weighted = None
    apply_modifiers(obj)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True


def normalize_hull(obj, dimensions):
    # Upstream ships point along Blender +X. Blender -Y exports as Unity +Z.
    obj.rotation_euler = (0.0, 0.0, math.radians(-90.0))
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    for modifier in obj.modifiers:
        if modifier.type == "BEVEL":
            modifier.segments = 4
            modifier.profile = 0.32
            if hasattr(modifier, "harden_normals"):
                modifier.harden_normals = True
    apply_modifiers(obj)
    fit_hull_bounds(obj, dimensions)


def fit_hull_bounds(obj, dimensions):
    activate(obj)

    bounds = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    size = Vector(
        (
            max(v.x for v in bounds) - min(v.x for v in bounds),
            max(v.y for v in bounds) - min(v.y for v in bounds),
            max(v.z for v in bounds) - min(v.z for v in bounds),
        )
    )
    target = Vector((dimensions[0], dimensions[2], dimensions[1]))
    obj.scale = Vector(
        (
            target.x / max(size.x, 1e-5),
            target.y / max(size.y, 1e-5),
            target.z / max(size.z, 1e-5),
        )
    )
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    obj.location = (0.0, 0.0, 0.0)


def remap_hull_materials(obj):
    mapping = {0: 0, 1: 2, 2: 1, 3: 3, 4: 2}
    for polygon in obj.data.polygons:
        polygon.material_index = mapping.get(polygon.material_index, 0)
    obj.data.materials.clear()
    for material in stable_materials():
        obj.data.materials.append(material)


def cube_uv(obj):
    activate(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.0, correct_aspect=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def triangle_count(obj) -> int:
    return sum(max(0, len(poly.vertices) - 2) for poly in obj.data.polygons)


def decimate_to(obj, target: int):
    if any(len(polygon.vertices) > 3 for polygon in obj.data.polygons):
        triangulate = obj.modifiers.new("PCG_Triangulate", "TRIANGULATE")
        apply_modifiers(obj)
    for _ in range(12):
        current = triangle_count(obj)
        if current <= target:
            return
        modifier = obj.modifiers.new("PCG_Decimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max(0.005, min(1.0, target / float(current) * 0.90))
        modifier.use_collapse_triangulate = True
        apply_modifiers(obj)


def duplicate_mesh(source, name):
    copy = source.copy()
    copy.data = source.data.copy()
    copy.name = name
    copy.data.name = name
    bpy.context.collection.objects.link(copy)
    return copy


def convex_collision(source, target: int):
    vertices = [vertex.co.copy() for vertex in source.data.vertices]
    if not vertices:
        raise RuntimeError("Cannot create collision hull from an empty mesh.")
    # A convex polyhedron made from N input points has at most 2N-4 triangle
    # faces. Sample at most 96 deterministic support points to stay below the
    # 200-triangle PhysX budget without relying on Decimate convergence.
    point_budget = min(96, max(16, (target + 4) // 2))
    directions = [
        Vector((x, y, z)).normalized()
        for x in (-1, 0, 1)
        for y in (-1, 0, 1)
        for z in (-1, 0, 1)
        if x != 0 or y != 0 or z != 0
    ]
    sampled = []
    seen = set()

    def append_point(point):
        key = (round(point.x, 6), round(point.y, 6), round(point.z, 6))
        if key not in seen and len(sampled) < point_budget:
            seen.add(key)
            sampled.append(point.copy())

    for direction in directions:
        append_point(max(vertices, key=lambda point: point.dot(direction)))
    step = max(1, len(vertices) // max(1, point_budget - len(sampled)))
    for index in range(0, len(vertices), step):
        append_point(vertices[index])
        if len(sampled) >= point_budget:
            break

    mesh = bpy.data.meshes.new("Collision")
    mesh.from_pydata(sampled, [], [])
    mesh.update()
    collision = bpy.data.objects.new("Collision", mesh)
    bpy.context.collection.objects.link(collision)
    bm = bmesh.new()
    bm.from_mesh(collision.data)
    result = bmesh.ops.convex_hull(bm, input=list(bm.verts), use_existing_faces=False)
    removable = []
    for key in ("geom_interior", "geom_unused", "geom_holes"):
        removable.extend(element for element in result.get(key, []) if getattr(element, "is_valid", False))
    if removable:
        verts = []
        seen_verts = set()
        for element in removable:
            if isinstance(element, bmesh.types.BMVert) and id(element) not in seen_verts:
                seen_verts.add(id(element))
                verts.append(element)
        if verts:
            bmesh.ops.delete(bm, geom=verts, context="VERTS")
    bm.to_mesh(collision.data)
    bm.free()
    collision.data.materials.clear()
    return collision


def geometry_hash(objects) -> str:
    digest = hashlib.sha256()
    for obj in sorted(objects, key=lambda item: item.name):
        digest.update(obj.name.encode("utf-8"))
        for vertex in obj.data.vertices:
            digest.update(f"{vertex.co.x:.6f},{vertex.co.y:.6f},{vertex.co.z:.6f};".encode("ascii"))
        for polygon in obj.data.polygons:
            digest.update((",".join(str(index) for index in polygon.vertices) + "|").encode("ascii"))
    return digest.hexdigest()


def mesh_bounds_unity(obj):
    bounds = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    blender_size = (
        max(v.x for v in bounds) - min(v.x for v in bounds),
        max(v.y for v in bounds) - min(v.y for v in bounds),
        max(v.z for v in bounds) - min(v.z for v in bounds),
    )
    return [blender_size[0], blender_size[2], blender_size[1]]


def export_fbx(objects, path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=True,
        use_triangles=True,
        use_custom_props=True,
        bake_anim=False,
        path_mode="AUTO",
    )


def render_preview(obj, path: Path, resolution: int):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = True
    scene.render.filepath = str(path)
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = 1.0

    camera_data = bpy.data.cameras.new("_PCGPreviewCamera")
    camera = bpy.data.objects.new("_PCGPreviewCamera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    size = max(obj.dimensions)
    camera.location = Vector((size * 1.35, size * -1.35, size * 0.9))
    direction = -camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = size * 1.55

    world = scene.world or bpy.data.worlds.new("_PCGWorld")
    scene.world = world
    world.color = (0.035, 0.05, 0.075)

    lights = []
    energy_scale = max(0.08, min(1.5, (size / 6.0) ** 2))
    for name, location, energy, color, area_size in (
        ("_PCGKey", (size * 1.2, -size * 0.9, size * 1.6), 1800, (0.80, 0.90, 1.0), size * 0.9),
        ("_PCGFill", (-size * 1.4, -size * 0.2, size * 0.65), 950, (0.32, 0.52, 1.0), size * 1.25),
        ("_PCGRim", (size * 0.2, size * 1.5, size * 1.1), 1500, (1.0, 0.48, 0.22), size * 0.7),
    ):
        light_data = bpy.data.lights.new(name, "AREA")
        light_data.energy = energy * energy_scale
        light_data.color = color
        light_data.shape = "DISK"
        light_data.size = area_size
        light = bpy.data.objects.new(name, light_data)
        scene.collection.objects.link(light)
        light.location = location
        light.rotation_euler = (-Vector(location)).to_track_quat("-Z", "Y").to_euler()
        lights.append(light)

    for candidate in bpy.context.scene.objects:
        candidate.hide_render = candidate.type == "MESH" and candidate != obj
    obj.hide_render = False
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera, do_unlink=True)
    for light in lights:
        bpy.data.objects.remove(light, do_unlink=True)


def validate_mesh_set(lod0, lod1, lod2, collision, dimensions, budgets):
    errors = []
    counts = {
        "lod0": triangle_count(lod0),
        "lod1": triangle_count(lod1),
        "lod2": triangle_count(lod2),
        "collision": triangle_count(collision),
    }
    limits = {
        "lod0": budgets["lod0Triangles"],
        "lod1": budgets["lod1Triangles"],
        "lod2": budgets["lod2Triangles"],
        "collision": budgets["collisionTriangles"],
    }
    for key, value in counts.items():
        if value > limits[key]:
            errors.append(f"{key} triangles {value} exceed {limits[key]}")
    actual = mesh_bounds_unity(lod0)
    tolerance = float(budgets.get("dimensionTolerance", 0.01))
    for index, label in enumerate(("width", "height", "length")):
        if abs(actual[index] - dimensions[index]) > dimensions[index] * tolerance + 1e-4:
            errors.append(f"{label} {actual[index]:.4f} differs from {dimensions[index]:.4f}")
    if not lod0.data.uv_layers:
        errors.append("LOD0 has no UV layer")
    if [slot.material.name for slot in lod0.material_slots] != [
        "HullPrimary",
        "HullDark",
        "HullEmission",
        "Accent",
    ]:
        errors.append("material slots do not match Unity contract")
    return errors, counts, actual


def generate_hull(upstream, archetype, seed_index: int, manifest, staging: Path):
    clean_scene()
    seed = f"{archetype['id']}-{seed_index:02d}"
    random.seed(seed)
    hull_segments = archetype["hullSegments"]
    asymmetry = archetype["asymmetrySegments"]
    lod0 = upstream.generate_spaceship(
        random_seed=seed,
        num_hull_segments_min=hull_segments[0],
        num_hull_segments_max=hull_segments[1],
        create_asymmetry_segments=True,
        num_asymmetry_segments_min=asymmetry[0],
        num_asymmetry_segments_max=asymmetry[1],
        create_face_detail=True,
        allow_horizontal_symmetry=archetype["allowHorizontalSymmetry"],
        allow_vertical_symmetry=archetype["allowVerticalSymmetry"],
        apply_bevel_modifier=True,
        assign_materials=True,
    )
    lod0.name = "Render_LOD0"
    lod0.data.name = "Render_LOD0"
    normalize_hull(lod0, archetype["dimensions"])
    remap_hull_materials(lod0)
    lod0 = industrialize_hull(lod0, archetype, seed_index)
    decimate_to(lod0, manifest["budgets"]["lod0Triangles"])

    lod1 = duplicate_mesh(lod0, "Render_LOD1")
    decimate_to(lod1, manifest["budgets"]["lod1Triangles"])
    lod2 = duplicate_mesh(lod0, "Render_LOD2")
    decimate_to(lod2, manifest["budgets"]["lod2Triangles"])
    collision = convex_collision(lod2, manifest["budgets"]["collisionTriangles"])
    objects = [lod0, lod1, lod2, collision]

    errors, counts, actual = validate_mesh_set(
        lod0, lod1, lod2, collision, archetype["dimensions"], manifest["budgets"]
    )
    output_id = f"{archetype['id']}_{seed_index:02d}"
    fbx_path = staging / "Hulls" / f"{output_id}.fbx"
    preview_path = staging / "Thumbnails" / f"{output_id}.png"
    if not errors:
        export_fbx(objects, fbx_path)
        render_preview(lod0, preview_path, 512)
    return {
        "kind": "hull",
        "id": output_id,
        "archetype": archetype["id"],
        "seedIndex": seed_index,
        "seed": seed,
        "hullId": f"hull.{archetype['id']}" if seed_index == 0 else f"hull.{archetype['id']}.pcg.{seed_index:02d}",
        "dimensions": archetype["dimensions"],
        "triangles": counts,
        "actualDimensions": actual,
        "geometryHash": geometry_hash(objects),
        "fbx": str(fbx_path.relative_to(staging)).replace("\\", "/"),
        "thumbnail": str(preview_path.relative_to(staging)).replace("\\", "/"),
        "sha256": sha256_file(fbx_path) if fbx_path.exists() else "",
        "errors": errors,
    }


def add_box(name, location, scale, bevel=0.08):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    modifier = obj.modifiers.new("Bevel", "BEVEL")
    modifier.width = bevel
    modifier.segments = 4
    modifier.profile = 0.35
    if hasattr(modifier, "harden_normals"):
        modifier.harden_normals = True
    return obj


def add_cylinder(name, location, radius, depth, rotation=(math.pi / 2, 0, 0), vertices=32):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=depth,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    modifier = obj.modifiers.new("Bevel", "BEVEL")
    modifier.width = radius * 0.08
    modifier.segments = 3
    modifier.profile = 0.4
    if hasattr(modifier, "harden_normals"):
        modifier.harden_normals = True
    return obj


def assign_material_index(obj, material_index: int):
    materials = stable_materials()
    obj.data.materials.clear()
    for material in materials:
        obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.material_index = material_index


def consolidate_material_slots(obj):
    canonical = stable_materials()
    names = [material.name if material is not None else "" for material in obj.data.materials]
    canonical_names = [material.name for material in canonical]
    for polygon in obj.data.polygons:
        source_name = names[polygon.material_index] if polygon.material_index < len(names) else ""
        polygon.material_index = (
            canonical_names.index(source_name) if source_name in canonical_names else 0
        )
    obj.data.materials.clear()
    for material in canonical:
        obj.data.materials.append(material)


def join_geometry(objects, name: str):
    for obj in objects:
        apply_hard_surface_shading(obj)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    result.data.name = name
    consolidate_material_slots(result)
    return result


def _industrial_box(parts, name, location, scale, material_index, bevel):
    obj = add_box(name, location, scale, bevel)
    assign_material_index(obj, material_index)
    parts.append(obj)
    return obj


def _industrial_cylinder(
    parts,
    name,
    location,
    radius,
    depth,
    material_index,
    vertices=20,
    rotation=(math.pi / 2, 0, 0),
):
    obj = add_cylinder(name, location, radius, depth, rotation=rotation, vertices=vertices)
    assign_material_index(obj, material_index)
    parts.append(obj)
    return obj


def industrialize_hull(base, archetype, seed_index: int):
    """Wrap the upstream hull in a deterministic modular industrial hard-surface kit."""
    width, height, length = (float(value) for value in archetype["dimensions"])
    rng = random.Random(f"{archetype['id']}-{seed_index:02d}-industrial-v2")
    base.scale = (0.72, 0.82, 0.72)
    activate(base)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    parts = [base]

    block_count = 6 if archetype["id"] == "spindle" else 5
    if archetype["id"] == "saucer":
        block_count = 4
    segment_length = length / block_count
    for index in range(block_count):
        y = -length * 0.42 + segment_length * (index + 0.5)
        taper = 1.0 - abs(index - (block_count - 1) * 0.5) / max(1.0, block_count) * 0.35
        half_width = width * rng.uniform(0.27, 0.40) * taper
        half_height = height * rng.uniform(0.24, 0.37)
        _industrial_box(
            parts,
            f"ChassisBlock_{index:02d}",
            (0.0, y, rng.uniform(-0.04, 0.05) * height),
            (half_width, segment_length * rng.uniform(0.38, 0.52), half_height),
            0,
            min(width, height) * 0.035,
        )
        _industrial_box(
            parts,
            f"TopArmor_{index:02d}",
            (0.0, y, half_height + height * 0.055),
            (
                half_width * rng.uniform(0.65, 0.92),
                segment_length * rng.uniform(0.27, 0.45),
                height * 0.035,
            ),
            3 if index % 3 == 0 else 0,
            height * 0.018,
        )

        for side in (-1.0, 1.0):
            pod_width = width * rng.uniform(0.075, 0.13)
            pod_height = height * rng.uniform(0.13, 0.24)
            pod_x = side * (half_width + pod_width * 0.68)
            _industrial_box(
                parts,
                f"SidePod_{index:02d}_{'L' if side < 0 else 'R'}",
                (pod_x, y, -height * rng.uniform(0.02, 0.12)),
                (pod_width, segment_length * rng.uniform(0.24, 0.43), pod_height),
                1 if index % 2 else 0,
                min(width, height) * 0.028,
            )
            _industrial_box(
                parts,
                f"SideArmor_{index:02d}_{'L' if side < 0 else 'R'}",
                (pod_x + side * pod_width * 0.82, y, pod_height * 0.18),
                (width * 0.018, segment_length * 0.30, pod_height * 0.72),
                3,
                width * 0.008,
            )

    rear_y = length * 0.42
    engine_x = width * (0.22 if archetype["id"] != "spindle" else 0.16)
    for side in (-1.0, 1.0):
        x = side * engine_x
        _industrial_box(
            parts,
            f"EngineBay_{'L' if side < 0 else 'R'}",
            (x, rear_y, -height * 0.04),
            (width * 0.17, length * 0.11, height * 0.30),
            1,
            min(width, height) * 0.045,
        )
        _industrial_cylinder(
            parts,
            f"EngineNozzle_{'L' if side < 0 else 'R'}",
            (x, length * 0.515, -height * 0.04),
            min(width, height) * 0.13,
            length * 0.10,
            3,
            vertices=24,
        )

    rail_count = 7 if archetype["id"] == "spindle" else 5
    for index in range(rail_count):
        y = -length * 0.34 + index * (length * 0.68 / max(1, rail_count - 1))
        for side in (-1.0, 1.0):
            _industrial_box(
                parts,
                f"HullRib_{index:02d}_{'L' if side < 0 else 'R'}",
                (side * width * 0.41, y, 0.0),
                (width * 0.018, length * 0.018, height * 0.34),
                3,
                min(width, height) * 0.008,
            )

    for index in range(4):
        y = -length * 0.28 + index * length * 0.18
        _industrial_box(
            parts,
            f"EmissionMark_{index:02d}",
            (width * 0.22 * (-1 if index % 2 else 1), y, height * 0.405),
            (width * 0.055, length * 0.035, height * 0.012),
            2,
            height * 0.006,
        )

    result = join_geometry(parts, "Render_LOD0")
    fit_hull_bounds(result, archetype["dimensions"])
    cube_uv(result)
    return result


def build_module_parts(module):
    kind = module["kind"]
    scale = float(module.get("scale", 1.0))
    parts = []
    if kind == "thruster":
        parts += [
            add_cylinder("Housing", (0, 0, 0.18 * scale), 0.42 * scale, 0.8 * scale),
            add_cylinder("Nozzle", (0, 0, -0.36 * scale), 0.34 * scale, 0.45 * scale),
            add_cylinder("Core", (0, 0, 0.64 * scale), 0.22 * scale, 0.3 * scale),
        ]
    elif kind == "armor_plate":
        parts += [
            add_box("ArmorPrimary", (0, 0, 0.10 * scale), (0.9 * scale, 0.62 * scale, 0.10 * scale), 0.05),
            add_box("ArmorInset", (0, 0, 0.22 * scale), (0.68 * scale, 0.42 * scale, 0.035 * scale), 0.018),
        ]
        for x in (-0.72, 0.72):
            for y in (-0.44, 0.44):
                parts.append(
                    add_cylinder(
                        "ArmorBolt",
                        (x * scale, y * scale, 0.24 * scale),
                        0.045 * scale,
                        0.035 * scale,
                        rotation=(0, 0, 0),
                        vertices=12,
                    )
                )
    elif kind == "side_pod":
        parts += [
            add_box("PodPrimary", (0, 0, 0.28 * scale), (0.72 * scale, 1.05 * scale, 0.28 * scale), 0.12),
            add_box("PodArmor", (0, 0.05 * scale, 0.60 * scale), (0.58 * scale, 0.72 * scale, 0.06 * scale), 0.03),
            add_box("PodFrame", (0, -0.82 * scale, 0.34 * scale), (0.62 * scale, 0.10 * scale, 0.34 * scale), 0.04),
        ]
        for side in (-1, 1):
            parts.append(
                add_box(
                    "PodRail",
                    (side * 0.70 * scale, 0, 0.32 * scale),
                    (0.045 * scale, 0.86 * scale, 0.28 * scale),
                    0.02,
                )
            )
    elif kind == "pipe_cluster":
        parts.append(add_box("PipeMount", (0, 0, 0.08 * scale), (0.65 * scale, 0.85 * scale, 0.08 * scale), 0.04))
        for index, x in enumerate((-0.38, -0.13, 0.13, 0.38)):
            parts.append(
                add_cylinder(
                    f"Pipe_{index}",
                    (x * scale, 0, 0.24 * scale),
                    0.07 * scale,
                    1.55 * scale,
                    vertices=16,
                )
            )
            for y in (-0.55, 0.55):
                parts.append(
                    add_cylinder(
                        "PipeCoupler",
                        (x * scale, y * scale, 0.24 * scale),
                        0.095 * scale,
                        0.12 * scale,
                        vertices=16,
                    )
                )
    elif kind == "antenna_array":
        parts.append(add_box("AntennaMount", (0, 0, 0.08 * scale), (0.62 * scale, 0.48 * scale, 0.08 * scale), 0.05))
        for index, x in enumerate((-0.42, -0.14, 0.14, 0.42)):
            height = (0.75 + index * 0.16) * scale
            parts.append(
                add_cylinder(
                    f"AntennaMast_{index}",
                    (x * scale, 0, height * 0.5 + 0.16 * scale),
                    0.035 * scale,
                    height,
                    rotation=(0, 0, 0),
                    vertices=10,
                )
            )
            parts.append(
                add_cylinder(
                    "AntennaTip",
                    (x * scale, 0, height + 0.18 * scale),
                    0.075 * scale,
                    0.10 * scale,
                    rotation=(0, 0, 0),
                    vertices=12,
                )
            )
    elif kind == "radiator":
        parts += [add_box("RadiatorFrame", (0, 0, 0.08 * scale), (0.9 * scale, 0.52 * scale, 0.08 * scale), 0.04)]
        for index in range(7):
            parts.append(
                add_box(
                    f"RadiatorPanel_{index}",
                    ((index - 3) * 0.25 * scale, 0, 0.20 * scale),
                    (0.095 * scale, 0.46 * scale, 0.025 * scale),
                    0.008,
                )
            )
    elif kind == "sensor":
        parts += [
            add_box("SensorMount", (0, 0, 0.08 * scale), (0.48 * scale, 0.42 * scale, 0.08 * scale), 0.04),
            add_cylinder("SensorMast", (0, 0, 0.62 * scale), 0.11 * scale, 1.1 * scale, rotation=(0, 0, 0), vertices=12),
            add_cylinder("SensorDish", (0, 0, 1.18 * scale), 0.34 * scale, 0.12 * scale, rotation=(0, 0, 0), vertices=24),
        ]
    elif kind == "engine_bay":
        parts += [
            add_box("EngineBayPrimary", (0, 0, 0.30 * scale), (0.62 * scale, 1.05 * scale, 0.30 * scale), 0.13),
            add_box("EngineBayArmor", (0, 0.12 * scale, 0.64 * scale), (0.48 * scale, 0.66 * scale, 0.06 * scale), 0.03),
            add_cylinder("EngineCore", (-0.24 * scale, -0.88 * scale, 0.30 * scale), 0.19 * scale, 0.42 * scale),
            add_cylinder("EngineCore", (0.24 * scale, -0.88 * scale, 0.30 * scale), 0.19 * scale, 0.42 * scale),
        ]
    elif kind == "turret_mount":
        parts += [
            add_box("TurretFoundation", (0, 0, 0.10 * scale), (0.72 * scale, 0.72 * scale, 0.10 * scale), 0.06),
            add_cylinder("TurretRing", (0, 0, 0.24 * scale), 0.52 * scale, 0.18 * scale, rotation=(0, 0, 0), vertices=32),
            add_cylinder("TurretBearing", (0, 0, 0.38 * scale), 0.37 * scale, 0.14 * scale, rotation=(0, 0, 0), vertices=24),
            add_box("TurretHardpoint", (0, 0.12 * scale, 0.52 * scale), (0.26 * scale, 0.38 * scale, 0.12 * scale), 0.06),
        ]
    elif kind in ("energy_weapon", "kinetic_weapon"):
        parts += [
            add_box("WeaponMount", (0, 0, 0.16), (0.38, 0.42, 0.16), 0.08),
            add_box("WeaponHousing", (0, 0.18, 0.38), (0.28, 0.55, 0.2), 0.08),
        ]
        barrel_count = 2 if kind == "energy_weapon" else 4
        for index in range(barrel_count):
            x = (index - (barrel_count - 1) * 0.5) * 0.13
            parts.append(add_cylinder(f"Barrel{index}", (x, 0.9, 0.42), 0.045, 1.0, vertices=12))
    else:
        raise ValueError(f"Unsupported module kind: {kind}")
    return parts


def join_parts(parts, name):
    for part in parts:
        lower_name = part.name.lower()
        if any(token in lower_name for token in ("core", "tip", "emission", "marker")):
            material_index = 2
        elif any(
            token in lower_name
            for token in (
                "armor",
                "frame",
                "rail",
                "ring",
                "bearing",
                "bolt",
                "coupler",
                "barrel",
                "nozzle",
                "hardpoint",
                "panel",
            )
        ):
            material_index = 3
        elif any(token in lower_name for token in ("mount", "housing", "foundation", "mast")):
            material_index = 1
        else:
            material_index = 0
        assign_material_index(part, material_index)
    result = join_geometry(parts, name)
    cube_uv(result)
    activate(result)
    bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
    result.location = (0, 0, 0)
    return result


def generate_module(module, manifest, staging: Path):
    clean_scene()
    random.seed(module["id"])
    lod0 = join_parts(build_module_parts(module), "Render_LOD0")
    lod1 = duplicate_mesh(lod0, "Render_LOD1")
    decimate_to(lod1, max(200, triangle_count(lod0) // 2))
    lod2 = duplicate_mesh(lod0, "Render_LOD2")
    decimate_to(lod2, max(80, triangle_count(lod0) // 4))
    collision = convex_collision(lod2, min(100, manifest["budgets"]["collisionTriangles"]))
    objects = [lod0, lod1, lod2, collision]
    fbx_path = staging / "Modules" / f"{module['id']}.fbx"
    preview_path = staging / "Thumbnails" / f"{module['id']}.png"
    export_fbx(objects, fbx_path)
    render_preview(lod0, preview_path, 256)
    return {
        "kind": "module",
        "id": module["id"],
        "moduleKind": module["kind"],
        "triangles": {
            "lod0": triangle_count(lod0),
            "lod1": triangle_count(lod1),
            "lod2": triangle_count(lod2),
            "collision": triangle_count(collision),
        },
        "geometryHash": geometry_hash(objects),
        "fbx": str(fbx_path.relative_to(staging)).replace("\\", "/"),
        "thumbnail": str(preview_path.relative_to(staging)).replace("\\", "/"),
        "sha256": sha256_file(fbx_path),
        "errors": [],
    }


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main():
    args = parse_args()
    manifest_path = Path(args.manifest).resolve()
    staging = Path(args.staging).resolve()
    report_path = Path(args.report).resolve()
    staging.mkdir(parents=True, exist_ok=True)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    pbr_textures = generate_pbr_textures(staging)
    upstream = load_upstream()
    results = []
    errors = []

    try:
        if args.scope in ("all", "hull"):
            for archetype in manifest["archetypes"]:
                for seed_index in range(int(manifest["hullSeedCount"])):
                    output_id = f"{archetype['id']}_{seed_index:02d}"
                    if args.id and args.id not in (output_id, archetype["id"]):
                        continue
                    item = generate_hull(upstream, archetype, seed_index, manifest, staging)
                    results.append(item)
                    errors.extend(f"{output_id}: {message}" for message in item["errors"])
                    print(f"[SpaceshipPCG] hull {output_id}: {item['triangles']}", flush=True)
        if args.scope in ("all", "module"):
            for module in manifest["modules"]:
                if args.id and args.id != module["id"]:
                    continue
                item = generate_module(module, manifest, staging)
                results.append(item)
                errors.extend(f"{module['id']}: {message}" for message in item["errors"])
                print(f"[SpaceshipPCG] module {module['id']}: {item['triangles']}", flush=True)
    except Exception as exc:
        errors.append(f"{type(exc).__name__}: {exc}")
        traceback.print_exc()

    report = {
        "success": not errors,
        "pipelineVersion": manifest["pipelineVersion"],
        "upstreamRevision": manifest["upstreamRevision"],
        "compatibilityRevision": manifest["compatibilityRevision"],
        "blenderVersion": bpy.app.version_string,
        "manifest": str(manifest_path),
        "pbrTextures": pbr_textures,
        "results": results,
        "errors": errors,
    }
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    if errors:
        print(json.dumps(errors, ensure_ascii=False, indent=2), file=sys.stderr)
        raise SystemExit(2)
    print(f"[SpaceshipPCG] generated {len(results)} assets", flush=True)


if __name__ == "__main__":
    main()
