from __future__ import annotations

import argparse
import json
import shutil
import tarfile
from pathlib import Path, PurePosixPath


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract selected Unity material assets and their metadata from a .unitypackage."
    )
    parser.add_argument("--package", required=True)
    parser.add_argument("--output-root", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument(
        "--contains",
        action="append",
        default=[],
        help="Case-insensitive logical-path substring. Repeat for multiple asset families.",
    )
    return parser.parse_args()


def scan_paths(package: Path) -> dict[str, str]:
    paths: dict[str, str] = {}
    with tarfile.open(package, "r|*") as archive:
        for member in archive:
            if not member.isfile() or not member.name.endswith("/pathname"):
                continue
            stream = archive.extractfile(member)
            if stream is None:
                continue
            decoded = stream.read().decode("utf-8", errors="replace")
            # Older Unity packages terminate pathname records with a second
            # line containing "00" instead of a NUL byte.
            logical_path = decoded.splitlines()[0].strip()
            guid = member.name.split("/", 1)[0]
            paths[guid] = logical_path
    return paths


def safe_output_path(output_root: Path, logical_path: str) -> Path:
    logical = PurePosixPath(logical_path)
    parts = logical.parts[1:] if logical.parts and logical.parts[0] == "Assets" else logical.parts
    output = output_root.joinpath(*parts)
    resolved_root = output_root.resolve()
    resolved_output = output.resolve()
    if resolved_output != resolved_root and resolved_root not in resolved_output.parents:
        raise RuntimeError(f"Refusing path outside output root: {logical_path}")
    return output


def main() -> None:
    args = parse_args()
    package = Path(args.package).resolve()
    output_root = Path(args.output_root).resolve()
    report_path = Path(args.report).resolve()
    filters = tuple(value.lower() for value in args.contains)

    logical_paths = scan_paths(package)
    selected = {
        guid: path
        for guid, path in logical_paths.items()
        if path.lower().endswith(".mat")
        and (not filters or any(value in path.lower() for value in filters))
    }
    extracted: list[dict[str, object]] = []
    pending_assets = set(selected)

    output_root.mkdir(parents=True, exist_ok=True)
    with tarfile.open(package, "r|*") as archive:
        for member in archive:
            if not member.isfile() or "/" not in member.name:
                continue
            guid, leaf = member.name.split("/", 1)
            if guid not in pending_assets or leaf not in {"asset", "asset.meta", "preview.png"}:
                continue
            source = archive.extractfile(member)
            if source is None:
                continue
            logical_path = selected[guid]
            destination = safe_output_path(output_root, logical_path)
            if leaf == "asset.meta":
                destination = destination.with_name(destination.name + ".meta")
            elif leaf == "preview.png":
                destination = destination.with_name(destination.stem + "_preview.png")
            destination.parent.mkdir(parents=True, exist_ok=True)
            with destination.open("wb") as target:
                shutil.copyfileobj(source, target)
            extracted.append(
                {
                    "guid": guid,
                    "logicalPath": logical_path,
                    "member": leaf,
                    "output": str(destination),
                    "bytes": destination.stat().st_size,
                }
            )

    report = {
        "success": True,
        "package": str(package),
        "outputRoot": str(output_root),
        "filters": list(args.contains),
        "materialCount": len(selected),
        "materials": [
            {"guid": guid, "logicalPath": path}
            for guid, path in sorted(selected.items(), key=lambda item: item[1].lower())
        ],
        "extractedFiles": extracted,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"success": True, "materialCount": len(selected), "report": str(report_path)}))


if __name__ == "__main__":
    main()
