"""Idempotent family-scoped extraction for the Blender creature pipeline."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path


TEXTURE_PATTERN = re.compile(
    r"(textures[\\/][^\"'<>\s]+?\.(?:dds|png|tga))",
    re.IGNORECASE,
)
MATERIAL_PATTERN = re.compile(
    rb"(models[\\/][ -~]+?\.material\.mbin)",
    re.IGNORECASE,
)


def _sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _normalise(value: str) -> str:
    return value.replace("\\", "/").lower()


def _load_hgpak(family):
    tool_root = family["hgpakTool"]
    if str(tool_root) not in sys.path:
        sys.path.insert(0, str(tool_root))
    from hgpaktool.api import HGPAKFile
    return HGPAKFile


def _extract_filters(family, pak_name, filters, records, warnings):
    pak_path = family["gamePakRoot"] / pak_name
    if not pak_path.is_file():
        warnings.append(f"Archive is missing: {pak_path}")
        return 0
    HGPAKFile = _load_hgpak(family)
    count = 0
    with HGPAKFile(pak_path) as pak:
        for relative, data in pak.extract(filters=filters):
            normalised = _normalise(relative)
            target = family["extractedRoot"] / normalised
            target.parent.mkdir(parents=True, exist_ok=True)
            digest = _sha256_bytes(data)
            reused = target.is_file() and target.stat().st_size == len(data)
            if reused:
                reused = _sha256_file(target) == digest
            if not reused:
                target.write_bytes(data)
            records[normalised] = {
                "archive": pak_name,
                "size": len(data),
                "sha256": digest,
                "reused": reused,
            }
            count += 1
    return count


def _convert_mbin(family, source: Path) -> Path:
    output = source.with_suffix(".MXML")
    source_digest = _sha256_file(source)
    stamp = output.with_suffix(output.suffix + ".source.sha256")
    if output.is_file() and stamp.is_file():
        if stamp.read_text(encoding="ascii").strip() == source_digest:
            return output
    result = subprocess.run(
        [str(family["mbinCompiler"]), "-y", "-f", "-Q", str(source)],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode != 0 or not output.is_file():
        details = (result.stdout + result.stderr).strip()
        raise RuntimeError(f"MBINCompiler failed for {source}: {details}")
    stamp.write_text(source_digest, encoding="ascii")
    return output


def _convert_metadata(family, warnings):
    converted = []
    descriptor_source_relative = family.get("descriptorSource")
    if descriptor_source_relative:
        descriptor_source = family["extractedRoot"] / descriptor_source_relative
        if not descriptor_source.is_file():
            raise RuntimeError(f"Descriptor source is missing: {descriptor_source}")
        converted.append(_convert_mbin(family, descriptor_source))
    for material in family["extractedRoot"].rglob("*.material.mbin"):
        try:
            converted.append(_convert_mbin(family, material))
        except RuntimeError as exc:
            warnings.append(str(exc))
    descriptor_relative = family.get("descriptor")
    if descriptor_relative:
        descriptor = family["extractedRoot"] / descriptor_relative
        if not descriptor.is_file():
            raise RuntimeError(f"Descriptor MXML was not generated: {descriptor}")
    return converted


def _material_references(root: Path):
    references = set()
    for scene in root.rglob("*.scene.mbin"):
        data = scene.read_bytes()
        for match in MATERIAL_PATTERN.finditer(data):
            references.add(_normalise(match.group(1).decode("ascii", errors="ignore")))
    return references


def _extract_referenced_materials(family, references, records, warnings):
    if not references:
        warnings.append("No material references were found in extracted Scene MBIN files")
        return
    pak_name = family.get("materialPak", "NMSARC.MetadataEtc.pak")
    pak_path = family["gamePakRoot"] / pak_name
    if not pak_path.is_file():
        warnings.append(f"Material archive is missing: {pak_path}")
        return
    unresolved = set(references)
    HGPAKFile = _load_hgpak(family)
    with HGPAKFile(pak_path) as pak:
        available = unresolved.intersection(pak.files.keys())
        for relative, data in pak.extract(filters=sorted(available)):
            normalised = _normalise(relative)
            target = family["extractedRoot"] / normalised
            target.parent.mkdir(parents=True, exist_ok=True)
            digest = _sha256_bytes(data)
            reused = target.is_file() and target.stat().st_size == len(data)
            if reused:
                reused = _sha256_file(target) == digest
            if not reused:
                target.write_bytes(data)
            records[normalised] = {
                "archive": pak_name,
                "size": len(data),
                "sha256": digest,
                "reused": reused,
            }
            unresolved.discard(normalised)
    for relative in sorted(unresolved):
        warnings.append(f"Missing material: {relative}")


def _texture_references(mxml_paths):
    references = set()
    for path in mxml_paths:
        text = path.read_text(encoding="utf-8", errors="ignore")
        for match in TEXTURE_PATTERN.finditer(text):
            references.add(_normalise(match.group(1)))
    return references


def _extract_referenced_textures(family, references, records, warnings):
    if not references:
        return
    unresolved = set(references)
    HGPAKFile = _load_hgpak(family)
    configured = list(family.get("texturePaks", []))
    discovered = [
        path.name for path in sorted(family["gamePakRoot"].glob("NMSARC.TexCreature*.pak"))
        if path.name not in configured
    ]
    shared = []
    normalised_references = tuple(sorted(unresolved))
    shared_rules = (
        ("/planets/biomes/common/", "NMSARC.TexBiomesCOMMON.pak"),
        ("/planets/biomes/weird/", "NMSARC.TexBiomesWEIRD.pak"),
        ("/planets/npcs/", "NMSARC.TexPlanetNPCS.pak"),
        ("/common/player/", "NMSARC.TexPlayer.pak"),
        ("/textures/effects/", "NMSARC.TexMisc.pak"),
        ("/space/spaceship/", "NMSARC.TexSpacecraft.pak"),
        ("/space/", "NMSARC.TexSpace.pak"),
        ("/robots/", "NMSARC.TexRobots.pak"),
    )
    for path_fragment, pak_name in shared_rules:
        if any(path_fragment in f"/{reference}" for reference in normalised_references):
            if pak_name not in configured and pak_name not in discovered and pak_name not in shared:
                shared.append(pak_name)

    # Biome-specific references follow TexBiomes<BIOME>.pak naming. Keep this
    # data driven so new creature materials can reference other biomes without
    # adding family-specific configuration.
    for reference in normalised_references:
        match = re.search(r"(?:^|/)textures/planets/biomes/([^/]+)/", f"/{reference}")
        if not match:
            continue
        biome = re.sub(r"[^a-z0-9]", "", match.group(1).lower()).upper()
        pak_name = f"NMSARC.TexBiomes{biome}.pak"
        if pak_name not in configured and pak_name not in discovered and pak_name not in shared:
            shared.append(pak_name)

    for pak_name in configured + discovered + shared:
        pak_path = family["gamePakRoot"] / pak_name
        if not pak_path.is_file():
            warnings.append(f"Texture archive is missing: {pak_path}")
            continue
        with HGPAKFile(pak_path) as pak:
            available = unresolved.intersection(pak.files.keys())
            if not available:
                continue
            for relative, data in pak.extract(filters=sorted(available)):
                normalised = _normalise(relative)
                target = family["extractedRoot"] / normalised
                target.parent.mkdir(parents=True, exist_ok=True)
                digest = _sha256_bytes(data)
                reused = target.is_file() and target.stat().st_size == len(data)
                if reused:
                    reused = _sha256_file(target) == digest
                if not reused:
                    target.write_bytes(data)
                records[normalised] = {
                    "archive": pak_name,
                    "size": len(data),
                    "sha256": digest,
                    "reused": reused,
                }
                unresolved.discard(normalised)
    for relative in sorted(unresolved):
        kind = "optional legacy normal texture" if "normal" in relative or relative.endswith(".normal.dds") else "texture"
        warnings.append(f"Missing {kind}: {relative}")


def _validate_required_sources(family):
    required = [family["sourceScene"], *family["actions"].values()]
    if family.get("descriptorSource"):
        required.append(family["descriptorSource"])
    missing = [
        str(family["extractedRoot"] / relative)
        for relative in required
        if not (family["extractedRoot"] / relative).is_file()
    ]
    scene_relative = Path(family["sourceScene"])
    try:
        creature_index = [part.lower() for part in scene_relative.parts].index("creatures")
        family_relative = Path(*scene_relative.parts[:creature_index + 2])
    except (ValueError, IndexError):
        family_relative = scene_relative.parent
    family_root = family["extractedRoot"] / family_relative
    geometries = list(family_root.rglob("*.geometry.mbin.pc"))
    geometry_data = list(family_root.rglob("*.geometry.data.mbin.pc"))
    if not geometries:
        missing.append(str(family_root / "*.geometry.mbin.pc"))
    if not geometry_data:
        missing.append(str(family_root / "*.geometry.data.mbin.pc"))
    if missing:
        raise RuntimeError(
            f"Required {family['familyId']} sources are missing: " + ", ".join(missing)
        )


def extraction_stage(family):
    for key in ("gamePakRoot", "hgpakTool", "mbinCompiler", "extractedRoot", "outputRoot"):
        if key not in family:
            raise RuntimeError(f"Extraction configuration is missing {key}")
    family["extractedRoot"].mkdir(parents=True, exist_ok=True)
    records = {}
    warnings = []
    family_filters = family.get("familyFilters")
    if family_filters is None:
        family_filter = family.get("familyFilter")
        family_filters = [family_filter] if family_filter else []
    if not family_filters:
        raise RuntimeError("Extraction configuration is missing familyFilter(s)")
    texture_filters = family.get("textureFilters", [])
    extracted_by_archive = {}
    for pak_name in family.get("extractionPaks", []):
        filters = list(family_filters)
        if pak_name.lower().startswith("nmsarc.tex"):
            filters = texture_filters or filters
        extracted_by_archive[pak_name] = _extract_filters(
            family, pak_name, filters, records, warnings
        )
    _validate_required_sources(family)
    material_references = _material_references(family["extractedRoot"])
    _extract_referenced_materials(
        family, material_references, records, warnings
    )
    converted = _convert_metadata(family, warnings)
    references = _texture_references(converted)
    _extract_referenced_textures(family, references, records, warnings)

    manifest = {
        "pipelineVersion": family["pipelineVersion"],
        "familyId": family["familyId"],
        "gamePakRoot": str(family["gamePakRoot"]),
        "extractedRoot": str(family["extractedRoot"]),
        "extractedByArchive": extracted_by_archive,
        "fileCount": len(records),
        "files": records,
        "convertedMxml": [str(path) for path in converted],
        "materialReferenceCount": len(material_references),
        "textureReferenceCount": len(references),
        "warnings": warnings,
    }
    path = family["outputRoot"] / "Manifests" / f"{family['familyId']}_extraction.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return str(path), manifest
