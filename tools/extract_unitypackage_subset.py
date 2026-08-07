#!/usr/bin/env python3
"""Extract a dependency-closed subset of a Unity .unitypackage.

Unity packages store each asset under a directory named after its GUID.  This
utility keeps the original asset paths and .meta files, follows GUID references
from YAML assets/importer metadata, and refuses to overwrite different files.
It is intentionally conservative so a curated art import cannot replace project
settings, scenes, or files that already belong to the project.
"""

from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path, PurePosixPath
import re
import tarfile
from typing import Dict, Iterable, Optional, Set, Tuple


GUID_DIRECTORY = re.compile(r"^(?:\./)?([0-9a-f]{32})/(asset|asset\.meta|pathname)$")
GUID_REFERENCE = re.compile(rb"guid:\s*([0-9a-f]{32})")
TEXT_ASSET_SUFFIXES = {
    ".anim",
    ".asset",
    ".compute",
    ".controller",
    ".cs",
    ".guiskin",
    ".mat",
    ".mask",
    ".overridecontroller",
    ".physicmaterial",
    ".playable",
    ".prefab",
    ".rendertexture",
    ".shader",
    ".shadergraph",
    ".shadersubgraph",
    ".terrainlayer",
    ".unity",
}
MAX_TEXT_ASSET_BYTES = 16 * 1024 * 1024


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("package", type=Path)
    parser.add_argument("project_root", type=Path)
    parser.add_argument(
        "--select",
        action="append",
        default=[],
        help="Exact package pathname to include. May be supplied repeatedly.",
    )
    parser.add_argument(
        "--select-file",
        type=Path,
        help="UTF-8 text file containing one exact package pathname per line.",
    )
    parser.add_argument(
        "--report",
        type=Path,
        help="Optional dependency/import report written as UTF-8 text.",
    )
    return parser.parse_args()


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def normalize_package_path(value: str) -> str:
    normalized = str(PurePosixPath(value.strip().replace("\\", "/")))
    if normalized.startswith("/") or normalized == ".." or normalized.startswith("../"):
        raise ValueError(f"Unsafe package pathname: {value!r}")
    if not normalized.startswith("Assets/"):
        raise ValueError(f"Only Assets/ paths may be imported: {value!r}")
    return normalized


def selected_paths(args: argparse.Namespace) -> Set[str]:
    values = list(args.select)
    if args.select_file:
        values.extend(
            line.strip()
            for line in args.select_file.read_text(encoding="utf-8-sig").splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        )
    if not values:
        raise ValueError("At least one --select or --select-file entry is required")
    return {normalize_package_path(value) for value in values}


def scan_package(
    package: Path,
) -> Tuple[Dict[str, str], Dict[str, str], Dict[str, Set[str]]]:
    """Return path->guid, guid->path, and guid dependency graph."""
    path_to_guid: Dict[str, str] = {}
    guid_to_path: Dict[str, str] = {}
    graph: Dict[str, Set[str]] = {}
    pending: Dict[str, Dict[str, bytes]] = {}

    with tarfile.open(package, mode="r|gz") as archive:
        for member in archive:
            match = GUID_DIRECTORY.match(member.name)
            if not match or not member.isfile():
                continue
            guid, kind = match.groups()
            source = archive.extractfile(member)
            if source is None:
                continue
            if kind == "asset" and member.size > MAX_TEXT_ASSET_BYTES:
                # Large binary content has no YAML dependency references. FBX
                # external material bindings live in asset.meta, which we scan.
                continue
            data = source.read()
            group = pending.setdefault(guid, {})
            group[kind] = data
            if kind != "pathname":
                continue

            pathname = normalize_package_path(data.decode("utf-8-sig").strip())
            path_to_guid[pathname] = guid
            guid_to_path[guid] = pathname
            references = graph.setdefault(guid, set())
            meta = group.get("asset.meta", b"")
            references.update(value.decode("ascii") for value in GUID_REFERENCE.findall(meta))
            asset = group.get("asset", b"")
            if Path(pathname).suffix.lower() in TEXT_ASSET_SUFFIXES:
                references.update(value.decode("ascii") for value in GUID_REFERENCE.findall(asset))
            pending.pop(guid, None)

    return path_to_guid, guid_to_path, graph


def add_parent_directories(
    paths: Iterable[str],
    path_to_guid: Dict[str, str],
    closure: Set[str],
) -> None:
    for value in list(paths):
        parent = PurePosixPath(value).parent
        while str(parent) not in (".", "Assets"):
            guid = path_to_guid.get(str(parent))
            if guid:
                closure.add(guid)
            parent = parent.parent


def dependency_closure(
    requested: Set[str],
    path_to_guid: Dict[str, str],
    guid_to_path: Dict[str, str],
    graph: Dict[str, Set[str]],
) -> Set[str]:
    missing = sorted(path for path in requested if path not in path_to_guid)
    if missing:
        raise ValueError("Package paths were not found:\n  " + "\n  ".join(missing))

    closure = {path_to_guid[path] for path in requested}
    queue = list(closure)
    while queue:
        guid = queue.pop()
        for dependency in graph.get(guid, ()):
            if dependency in guid_to_path and dependency not in closure:
                closure.add(dependency)
                queue.append(dependency)

    add_parent_directories(
        (guid_to_path[guid] for guid in closure if guid in guid_to_path),
        path_to_guid,
        closure,
    )
    return closure


def resolve_output(project_root: Path, pathname: str, is_meta: bool) -> Path:
    relative = Path(*PurePosixPath(pathname).parts)
    output = (project_root / relative).resolve()
    assets_root = (project_root / "Assets").resolve()
    if output != assets_root and assets_root not in output.parents:
        raise ValueError(f"Resolved path escapes Assets/: {pathname}")
    return output.with_name(output.name + ".meta") if is_meta else output


def write_if_safe(path: Path, data: bytes) -> str:
    if path.exists():
        existing = path.read_bytes()
        if existing != data:
            raise FileExistsError(
                f"Refusing to overwrite different file: {path}\n"
                f"existing sha256={sha256(existing)} package sha256={sha256(data)}"
            )
        return "unchanged"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return "created"


def extract_assets(
    package: Path,
    project_root: Path,
    closure: Set[str],
    guid_to_path: Dict[str, str],
) -> Tuple[int, int]:
    created = 0
    unchanged = 0
    with tarfile.open(package, mode="r|gz") as archive:
        for member in archive:
            match = GUID_DIRECTORY.match(member.name)
            if not match or not member.isfile():
                continue
            guid, kind = match.groups()
            if guid not in closure or kind == "pathname":
                continue
            pathname = guid_to_path[guid]
            source = archive.extractfile(member)
            if source is None:
                continue
            is_meta = kind == "asset.meta"
            output = resolve_output(project_root, pathname, is_meta)
            result = write_if_safe(output, source.read())
            if result == "created":
                created += 1
            else:
                unchanged += 1
    return created, unchanged


def main() -> int:
    args = parse_args()
    package = args.package.resolve()
    project_root = args.project_root.resolve()
    requested = selected_paths(args)
    path_to_guid, guid_to_path, graph = scan_package(package)
    closure = dependency_closure(
        requested,
        path_to_guid,
        guid_to_path,
        graph,
    )
    created, unchanged = extract_assets(
        package,
        project_root,
        closure,
        guid_to_path,
    )

    imported_paths = sorted(guid_to_path[guid] for guid in closure)
    lines = [
        f"package={package}",
        f"requested={len(requested)}",
        f"dependency_closed_assets={len(closure)}",
        f"created_files={created}",
        f"unchanged_files={unchanged}",
        "",
        *imported_paths,
    ]
    report = "\n".join(lines) + "\n"
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(report, encoding="utf-8")
    print(report, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
