from __future__ import annotations

import argparse
import gzip
import json
import sys
from pathlib import Path

from PIL import Image


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Decode gzip-compressed ASTC textures to PNG.")
    parser.add_argument("--input-root", required=True)
    parser.add_argument("--dependency-root")
    parser.add_argument("--report", required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    if args.dependency_root:
        sys.path.insert(0, str(Path(args.dependency_root).resolve()))
    import texture2ddecoder

    input_root = Path(args.input_root).resolve()
    decoded: list[dict[str, object]] = []
    for source in sorted(input_root.rglob("*.astc.gz")):
        astc = gzip.open(source, "rb").read()
        if astc[:4] != bytes.fromhex("13aba15c"):
            raise RuntimeError(f"Invalid ASTC header: {source}")
        block_width = astc[4]
        block_height = astc[5]
        width = int.from_bytes(astc[7:10], "little")
        height = int.from_bytes(astc[10:13], "little")
        rgba = texture2ddecoder.decode_astc(
            astc[16:], width, height, block_width, block_height
        )
        output = source.with_suffix("").with_suffix(".png")
        image = Image.frombytes("RGBA", (width, height), rgba)
        image.save(output, optimize=True)
        decoded.append(
            {
                "source": str(source),
                "output": str(output),
                "width": width,
                "height": height,
                "block": [block_width, block_height],
                "bytes": output.stat().st_size,
            }
        )

    report = {
        "success": True,
        "inputRoot": str(input_root),
        "decodedCount": len(decoded),
        "textures": decoded,
    }
    report_path = Path(args.report).resolve()
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"success": True, "decodedCount": len(decoded), "report": str(report_path)}))


if __name__ == "__main__":
    main()
