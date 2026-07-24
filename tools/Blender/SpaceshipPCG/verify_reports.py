"""Compare deterministic PCG reports produced with the same pinned Blender build."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("first")
    parser.add_argument("second")
    args = parser.parse_args()
    first = json.loads(Path(args.first).read_text(encoding="utf-8"))
    second = json.loads(Path(args.second).read_text(encoding="utf-8"))
    if not first.get("success") or not second.get("success"):
        raise SystemExit("Both reports must be successful.")
    if first.get("blenderVersion") != second.get("blenderVersion"):
        raise SystemExit("Reports use different Blender versions.")
    left = {item["id"]: item["geometryHash"] for item in first["results"]}
    right = {item["id"]: item["geometryHash"] for item in second["results"]}
    if left != right:
        missing = sorted(set(left) ^ set(right))
        changed = sorted(key for key in set(left) & set(right) if left[key] != right[key])
        raise SystemExit(f"Determinism failure; missing={missing}, changed={changed}")
    print(f"Deterministic: {len(left)} generated assets match.")


if __name__ == "__main__":
    main()
