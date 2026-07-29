from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


SHIPS = (
    ("hull.a30_thunderbolt", "A 30 Thunderbolt"),
    ("hull.sf_stealth_fighter", "SF Stealth Fighter"),
    ("hull.sf_modular_pirate", "SF Modular Pirate fighter"),
    ("hull.sf_dropship_r35", "SF Dropship R35"),
    ("hull.sf_fighter_gr2", "SF Fighter GR2"),
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", required=True)
    parser.add_argument("--output-root", required=True)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    points = [
        obj.matrix_world @ Vector(corner)
        for obj in objects
        if obj.type == "MESH"
        for corner in obj.bound_box
    ]
    return (
        Vector(tuple(min(point[index] for point in points) for index in range(3))),
        Vector(tuple(max(point[index] for point in points) for index in range(3))),
    )


def tri_count(obj: bpy.types.Object) -> int:
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def clean_name(value: str) -> str:
    return "".join(character if character.isalnum() else "_" for character in value).strip("_").lower()


def bake_normalized_meshes(objects: list[bpy.types.Object]) -> list[bpy.types.Object]:
    minimum, maximum = mesh_bounds(objects)
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    # Source gallery review establishes that all five authored ships point toward -Y.
    # Flip once so Blender +Y exports as Unity +Z under the project FBX convention.
    scale = 6.0 / max(size.x, size.y)
    transform = Matrix.Scale(scale, 4) @ Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Translation(-center)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    baked: list[tuple[str, bpy.types.Mesh]] = []
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = bpy.data.meshes.new_from_object(
            evaluated, preserve_all_data_layers=True, depsgraph=depsgraph
        )
        mesh.transform(transform @ evaluated.matrix_world)
        baked.append((obj.name, mesh))
    for obj in list(bpy.context.scene.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    result: list[bpy.types.Object] = []
    for name, mesh in baked:
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        result.append(obj)
    return result


def join_meshes(objects: list[bpy.types.Object], name: str) -> bpy.types.Object:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.hide_render = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    result.data.name = name
    return result


def make_placement(render: bpy.types.Object, target_triangles: int = 18000) -> bpy.types.Object:
    placement = render.copy()
    placement.data = render.data.copy()
    bpy.context.scene.collection.objects.link(placement)
    placement.name = "PlacementSurface"
    placement.data.name = "PlacementSurface"
    placement.data.materials.clear()
    triangles = tri_count(placement)
    if triangles > target_triangles:
        modifier = placement.modifiers.new("Placement_Decimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max(0.01, target_triangles / triangles)
        modifier.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = placement
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return placement


def make_collision(render: bpy.types.Object, target_triangles: int = 190) -> bpy.types.Object:
    collision = render.copy()
    collision.data = render.data.copy()
    bpy.context.scene.collection.objects.link(collision)
    collision.name = "Collision"
    collision.data.name = "Collision"
    collision.data.materials.clear()
    bpy.ops.object.select_all(action="DESELECT")
    collision.select_set(True)
    bpy.context.view_layer.objects.active = collision
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.convex_hull()
    bpy.ops.object.mode_set(mode="OBJECT")
    if tri_count(collision) > target_triangles:
        modifier = collision.modifiers.new("Collision_Decimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max(0.01, target_triangles / tri_count(collision))
        modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return collision


def texture_roles(material: bpy.types.Material) -> list[dict[str, object]]:
    if not material.use_nodes or material.node_tree is None:
        return []
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    result: list[dict[str, object]] = []
    for node in nodes:
        if node.type != "TEX_IMAGE" or node.image is None:
            continue
        frontier = [link.to_socket for output in node.outputs for link in links if link.from_socket == output]
        visited: set[int] = set()
        roles: set[str] = set()
        while frontier:
            socket = frontier.pop()
            owner = socket.node
            key = owner.as_pointer()
            if owner.type == "BSDF_PRINCIPLED":
                roles.add(socket.name)
                continue
            if key in visited:
                continue
            visited.add(key)
            frontier.extend(
                link.to_socket
                for output in owner.outputs
                for link in links
                if link.from_socket == output
            )
        result.append(
            {
                "file": Path(bpy.path.abspath(node.image.filepath)).name or node.image.name,
                "roles": sorted(roles),
                "nonColor": node.image.colorspace_settings.name == "Non-Color",
            }
        )
    return result


def material_manifest(render: bpy.types.Object) -> list[dict[str, object]]:
    return [
        {
            "name": slot.material.name,
            "textures": texture_roles(slot.material),
            "glass": "glass" in slot.material.name.lower(),
            "emission": any(
                role in {"Emission", "Emission Color"}
                for texture in texture_roles(slot.material)
                for role in texture["roles"]
            ) or "hud" in slot.material.name.lower(),
        }
        for slot in render.material_slots
        if slot.material is not None
    ]


def export_fbx(objects: list[bpy.types.Object], output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        object_types={"MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=False,
        use_triangles=True,
        add_leaf_bones=False,
        path_mode="AUTO",
    )


def look_at(obj: bpy.types.Object, target: Vector) -> None:
    obj.rotation_euler = (target - obj.location).to_track_quat("-Z", "Y").to_euler()


def render_thumbnail(render: bpy.types.Object, output: Path) -> None:
    collision = bpy.data.objects.get("Collision")
    placement = bpy.data.objects.get("PlacementSurface")
    if collision:
        collision.hide_render = True
    if placement:
        placement.hide_render = True
    scene = bpy.context.scene
    world = bpy.data.worlds.new("ThumbnailWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.006, 0.01, 0.018, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.24
    scene.world = world
    for name, location, energy, size, color in (
        ("Key", (5.5, 7.5, 6.0), 1050.0, 5.0, (0.62, 0.78, 1.0)),
        ("Rim", (-5.0, -3.5, 3.2), 900.0, 4.0, (1.0, 0.34, 0.12)),
    ):
        light_data = bpy.data.lights.new(name, "AREA")
        light_data.energy = energy
        light_data.shape = "DISK"
        light_data.size = size
        light_data.color = color
        light = bpy.data.objects.new(name, light_data)
        light.location = location
        scene.collection.objects.link(light)
        look_at(light, Vector((0.0, 0.0, 0.0)))
    camera_data = bpy.data.cameras.new("ThumbnailCamera")
    camera = bpy.data.objects.new("ThumbnailCamera", camera_data)
    camera.location = (7.2, 8.6, 6.0)
    camera_data.lens = 58.0
    scene.collection.objects.link(camera)
    look_at(camera, Vector((0.0, 0.15, 0.0)))
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = True
    scene.render.filepath = str(output)
    scene.view_settings.look = "AgX - Medium High Contrast"
    bpy.ops.render.render(write_still=True)


def process_ship(source_root: Path, output_root: Path, hull_id: str, name: str) -> dict[str, object]:
    reset_scene()
    source = source_root / name / f"{name}.fbx"
    if not source.exists():
        raise FileNotFoundError(source)
    bpy.ops.import_scene.fbx(filepath=str(source), use_image_search=True)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"No meshes imported from {source}")
    meshes = bake_normalized_meshes(meshes)
    render = join_meshes(meshes, "Render_LOD0")
    placement = make_placement(render)
    collision = make_collision(render)
    ship_key = hull_id.replace("hull.", "").replace(".", "_")
    ship_root = output_root / ship_key
    ship_root.mkdir(parents=True, exist_ok=True)
    export_fbx([render, placement, collision], ship_root / f"{ship_key}.fbx")
    render_thumbnail(render, ship_root / f"{ship_key}_thumbnail.png")
    minimum, maximum = mesh_bounds([render])
    dimensions = maximum - minimum
    result = {
        "hullId": hull_id,
        "displayName": name,
        "key": ship_key,
        "source": str(source),
        "fbx": str(ship_root / f"{ship_key}.fbx"),
        "thumbnail": str(ship_root / f"{ship_key}_thumbnail.png"),
        "dimensions": [round(dimensions.x, 6), round(dimensions.z, 6), round(dimensions.y, 6)],
        "triangles": {
            "render": tri_count(render),
            "placement": tri_count(placement),
            "collision": tri_count(collision),
        },
        "materials": material_manifest(render),
        "sourceTextures": sorted(path.name for path in (source.parent / "textures").glob("*") if path.is_file()),
    }
    (ship_root / "manifest.json").write_text(
        json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    return result


def main() -> None:
    args = parse_args()
    source_root = Path(args.source_root).resolve()
    output_root = Path(args.output_root).resolve()
    output_root.mkdir(parents=True, exist_ok=True)
    reports = [process_ship(source_root, output_root, hull_id, name) for hull_id, name in SHIPS]
    (output_root / "external_fleet_manifest.json").write_text(
        json.dumps({"ships": reports}, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    print(json.dumps({"ships": len(reports), "output": str(output_root)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
