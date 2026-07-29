from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


NUMBER = re.compile(r"[-+]?(?:\d+\.\d*|\d*\.\d+|\d+)(?:[eE][-+]?\d+)?")


def array_between(text: str, start: str, end: str) -> str:
    match = re.search(
        rf"(?m)^\s*{re.escape(start)}:\s*(.*?)(?=^\s*{re.escape(end)}:)",
        text,
        re.DOTALL,
    )
    if not match:
        raise ValueError(f"Missing {start} array")
    return match.group(1)


def array_between_any(text: str, start: str, ends: tuple[str, ...]) -> str:
    end_pattern = "|".join(re.escape(end) for end in ends)
    match = re.search(
        rf"(?m)^\s*{re.escape(start)}:\s*(.*?)(?=^\s*(?:{end_pattern}):)",
        text,
        re.DOTALL,
    )
    if not match:
        raise ValueError(f"Missing {start} array")
    return match.group(1)


def parse_float_array(value: str) -> list[float]:
    return [float(number) for number in NUMBER.findall(value)]


def parse_int_array(value: str) -> list[int]:
    return [int(number) for number in re.findall(r"-?\d+", value)]


def parse_faces(indices: list[int]) -> tuple[list[list[int]], list[list[int]]]:
    faces: list[list[int]] = []
    face_corners: list[list[int]] = []
    current: list[int] = []
    corners: list[int] = []
    corner_index = 0
    for index in indices:
        end = index < 0
        current.append((-index - 1) if end else index)
        corners.append(corner_index)
        corner_index += 1
        if end:
            faces.append(current)
            face_corners.append(corners)
            current = []
            corners = []
    if current:
        raise ValueError("PolygonVertexIndex did not terminate its final face")
    return faces, face_corners


def convert(source: Path, destination: Path) -> dict[str, object]:
    text = source.read_text(encoding="utf-8", errors="replace")
    if not text.startswith("; FBX 6.1.0 project file"):
        raise ValueError("Only the legacy ASCII FBX 6.1 files used by this asset pack are supported")

    model_match = re.search(r'Model:\s*"Model::([^"]+)",\s*"Mesh"', text)
    name = model_match.group(1) if model_match else source.stem
    vertices_raw = parse_float_array(array_between(text, "Vertices", "PolygonVertexIndex"))
    polygon_indices = parse_int_array(
        array_between_any(text, "PolygonVertexIndex", ("Edges", "GeometryVersion"))
    )
    if len(vertices_raw) % 3:
        raise ValueError("Vertex array length is not divisible by three")
    vertices = [vertices_raw[index : index + 3] for index in range(0, len(vertices_raw), 3)]
    faces, face_corners = parse_faces(polygon_indices)

    uvs: list[list[float]] = []
    uv_indices: list[int] = []
    try:
        uv_raw = parse_float_array(array_between(text, "UV", "UVIndex"))
        uv_indices = parse_int_array(
            array_between_any(
                text,
                "UVIndex",
                ("LayerElementTexture", "LayerElementMaterial", "Layer"),
            )
        )
        if len(uv_raw) % 2:
            raise ValueError("UV array length is not divisible by two")
        uvs = [uv_raw[index : index + 2] for index in range(0, len(uv_raw), 2)]
    except ValueError:
        pass

    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("w", encoding="utf-8", newline="\n") as output:
        output.write(f"# Converted from {source.name}\n")
        output.write(f"o {name}\n")
        for x, y, z in vertices:
            output.write(f"v {x:.9g} {y:.9g} {z:.9g}\n")
        for u, v in uvs:
            output.write(f"vt {u:.9g} {v:.9g}\n")
        for face, corners in zip(faces, face_corners):
            tokens: list[str] = []
            for vertex_index, corner in zip(face, corners):
                vertex = vertex_index + 1
                if corner < len(uv_indices) and 0 <= uv_indices[corner] < len(uvs):
                    tokens.append(f"{vertex}/{uv_indices[corner] + 1}")
                else:
                    tokens.append(str(vertex))
            output.write("f " + " ".join(tokens) + "\n")

    triangle_count = sum(max(0, len(face) - 2) for face in faces)
    return {
        "source": str(source),
        "destination": str(destination),
        "success": True,
        "vertices": len(vertices),
        "faces": len(faces),
        "triangles": triangle_count,
        "uvs": len(uvs),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input-root", required=True)
    parser.add_argument("--output-root", required=True)
    args = parser.parse_args()

    input_root = Path(args.input_root).resolve()
    output_root = Path(args.output_root).resolve()
    reports: list[dict[str, object]] = []
    for source in sorted(input_root.rglob("*.fbx"), key=lambda path: str(path).lower()):
        destination = output_root / source.relative_to(input_root).with_suffix(".obj")
        try:
            reports.append(convert(source, destination))
        except Exception as exc:
            reports.append(
                {
                    "source": str(source),
                    "destination": str(destination),
                    "success": False,
                    "error": repr(exc),
                }
            )

    report = {
        "success": all(item["success"] for item in reports),
        "inputRoot": str(input_root),
        "outputRoot": str(output_root),
        "convertedCount": sum(1 for item in reports if item["success"]),
        "failedCount": sum(1 for item in reports if not item["success"]),
        "assets": reports,
    }
    output_root.mkdir(parents=True, exist_ok=True)
    report_path = output_root / "conversion_report.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"report": str(report_path), **{key: report[key] for key in ("success", "convertedCount", "failedCount")}}))


if __name__ == "__main__":
    main()
