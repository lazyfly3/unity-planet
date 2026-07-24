"""Convert selected local metal ZIPs into Unity-ready 2K metallic PBR sets.

The source library uses the specular workflow: its COL texture is intentionally
black for conductors and the visible metal colour lives in REFL. Unity's
Standard metallic workflow expects that conductor colour in BaseColor, so this
converter derives a legible, art-directed BaseColor from REFL while preserving
the source GLOSS and NRM surface character.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import zipfile
from pathlib import Path

from PIL import Image, ImageEnhance, ImageFilter, ImageOps


SETS = (
    {
        "id": "deep_space_blue",
        "archive": "MetalSteelBlueBrushed001.zip",
        "resolution": "3K",
        "metallic": 242,
        "shadow": (24, 55, 85),
        "highlight": (110, 170, 220),
    },
    {
        "id": "gunmetal",
        "archive": "MetalBlack002.zip",
        "resolution": "3K",
        "metallic": 235,
        "shadow": (45, 50, 55),
        "highlight": (135, 145, 155),
    },
    {
        "id": "industrial_copper",
        "archive": "MetalCopperBrushed001.zip",
        "resolution": "3K",
        "metallic": 250,
        "shadow": (90, 35, 18),
        "highlight": (235, 135, 65),
    },
    {
        "id": "ceramic_white",
        "archive": "MetalAluminum001.zip",
        "resolution": "4K",
        "glossToken": "_GLOSS_VAR1_",
        "metallic": 248,
        "shadow": (100, 110, 120),
        "highlight": (230, 235, 240),
    },
    {
        "id": "warning_red",
        "archive": "MetalStainlessSteelZincCoatedScratched001.zip",
        "resolution": "3K",
        "metallic": 245,
        "shadow": (85, 20, 25),
        "highlight": (220, 70, 60),
    },
    {
        "id": "explorer_green",
        "archive": "MetalSilverBrushed001.zip",
        "resolution": "3K",
        "metallic": 252,
        "shadow": (25, 65, 48),
        "highlight": (120, 170, 105),
    },
    {
        "id": "brushed_brass",
        "archive": "MetalBrassBrushed001.zip",
        "resolution": "3K",
        "metallic": 250,
        "shadow": (85, 55, 18),
        "highlight": (235, 185, 70),
    },
    {
        "id": "graphite_pitted",
        "archive": "MetalGraphitePitted001.zip",
        "resolution": "3K",
        "metallic": 225,
        "shadow": (35, 38, 42),
        "highlight": (115, 120, 125),
    },
)


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--destination", required=True)
    parser.add_argument("--size", type=int, default=2048)
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def find_entry(archive: zipfile.ZipFile, resolution: str, token: str) -> str:
    candidates = []
    for name in archive.namelist():
        upper = name.upper()
        if f"/{resolution.upper()}/" not in upper:
            continue
        if token not in upper or not upper.endswith((".JPG", ".JPEG", ".PNG")):
            continue
        if "16_" in upper:
            continue
        candidates.append(name)
    if not candidates:
        raise FileNotFoundError(
            f"No {resolution} {token} texture found in {Path(archive.filename).name}"
        )
    return sorted(candidates, key=len)[0]


def load_rgb(archive: zipfile.ZipFile, name: str, size: int) -> Image.Image:
    with archive.open(name) as stream:
        image = Image.open(io.BytesIO(stream.read())).convert("RGB")
    if image.size != (size, size):
        image = ImageOps.fit(image, (size, size), method=Image.Resampling.LANCZOS)
    return image


def save_png(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, "PNG", optimize=True)


def build_metallic_base_color(
    reflectance: Image.Image,
    shadow: tuple[int, int, int],
    highlight: tuple[int, int, int],
) -> Image.Image:
    """Map source conductor reflectance into a readable metallic albedo range."""
    detail = ImageOps.grayscale(reflectance)
    detail = ImageOps.autocontrast(detail, cutoff=1)
    detail = ImageEnhance.Contrast(detail).enhance(0.72)
    return ImageOps.colorize(detail, black=shadow, white=highlight)


def convert_set(source: Path, destination: Path, spec: dict, size: int) -> dict:
    archive_path = source / spec["archive"]
    if not archive_path.is_file():
        raise FileNotFoundError(archive_path)
    with zipfile.ZipFile(archive_path) as archive:
        legacy_color_name = find_entry(
            archive,
            spec["resolution"],
            spec.get("colorToken", "_COL_"),
        )
        reflectance_name = find_entry(
            archive,
            spec["resolution"],
            spec.get("reflectanceToken", "_REFL_"),
        )
        gloss_name = find_entry(
            archive,
            spec["resolution"],
            spec.get("glossToken", "_GLOSS_"),
        )
        normal_name = find_entry(
            archive,
            spec["resolution"],
            spec.get("normalToken", "_NRM_"),
        )
        reflectance = load_rgb(archive, reflectance_name, size)
        base_color = build_metallic_base_color(
            reflectance,
            spec["shadow"],
            spec["highlight"],
        )
        gloss_rgb = load_rgb(archive, gloss_name, size)
        normal = load_rgb(archive, normal_name, size)

    gloss = ImageOps.grayscale(gloss_rgb)
    roughness = ImageOps.invert(gloss)
    metallic = Image.new("L", (size, size), spec["metallic"])

    # The source sets do not include AO. Keep the effect restrained: only
    # broad, dark surface variation lowers occlusion, never baked lighting.
    gray = ImageOps.grayscale(base_color)
    low_frequency = gray.filter(ImageFilter.GaussianBlur(radius=max(8, size // 128)))
    low_frequency = ImageEnhance.Contrast(low_frequency).enhance(0.35)
    ao = Image.blend(Image.new("L", (size, size), 255), low_frequency, 0.16)

    packed = Image.merge(
        "RGBA",
        (
            metallic,
            Image.new("L", (size, size), 0),
            Image.new("L", (size, size), 0),
            gloss,
        ),
    )
    outputs = {
        "BaseColor": base_color,
        "Metallic": metallic.convert("RGB"),
        "Roughness": roughness.convert("RGB"),
        "Normal": normal,
        "AO": ao.convert("RGB"),
        "MetallicSmoothness": packed,
    }
    paths = {}
    for map_name, image in outputs.items():
        path = destination / f"{spec['id']}_{map_name}.png"
        save_png(image, path)
        paths[map_name] = {
            "path": path.name,
            "sha256": sha256(path),
        }
    return {
        "id": spec["id"],
        "archive": spec["archive"],
        "archiveSha256": sha256(archive_path),
        "sourceMaps": {
            "BaseColor": reflectance_name,
            "LegacyDiffuse": legacy_color_name,
            "Gloss": gloss_name,
            "Normal": normal_name,
        },
        "baseColorRange": {
            "shadow": spec["shadow"],
            "highlight": spec["highlight"],
        },
        "outputs": paths,
    }


def main():
    args = parse_args()
    source = Path(args.source).resolve()
    destination = Path(args.destination).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    converted = [
        convert_set(source, destination, spec, args.size)
        for spec in SETS
    ]
    manifest = {
        "pipeline": "local-metal-specular-to-metallic-pbr-v2",
        "resolution": args.size,
        "sourceNotice": (
            "User-supplied local texture library; personal-learning use only. "
            "Original authors retain copyright."
        ),
        "sets": converted,
    }
    (destination / "local_metal_pbr_manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
