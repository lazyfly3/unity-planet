#!/usr/bin/env python3
"""LogTrack 日志解析工具（复现版）。

用法:
  python parse_logtrack.py --log track.bin.json --pdb LogPdb.pdb.json --last 100
  python parse_logtrack.py --log track.log --out analysis.json
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


def load_json(path: Path):
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def decode_item(item: int) -> tuple[int, int]:
    return item >> 3, item & 7


def parse_binary_log(log_data: dict, pdb_data: dict, last_n: int | None, keyword: str | None):
    pdb_map = {entry["hash"]: entry for entry in pdb_data.get("items", [])}
    frames = log_data.get("frames", [])
    if last_n is not None and last_n > 0:
        frames = frames[-last_n:]

    parsed_frames = []
    for frame in frames:
        frame_index = frame.get("frameIndex", 0)
        calls = []
        arg_index = 0
        for raw_item in frame.get("items", []):
            hash_id, arg_count = decode_item(int(raw_item))
            args = frame.get("args", [])[arg_index : arg_index + arg_count]
            arg_index += arg_count
            pdb_item = pdb_map.get(hash_id, {})
            call = {
                "frameIndex": frame_index,
                "hash": hash_id,
                "function": pdb_item.get("dbgStr", f"hash_{hash_id}"),
                "file": pdb_item.get("file", ""),
                "line": pdb_item.get("line", 0),
                "args": args,
            }
            if keyword:
                blob = json.dumps(call, ensure_ascii=False)
                if keyword.lower() not in blob.lower():
                    continue
            calls.append(call)
        if calls:
            parsed_frames.append({"frameIndex": frame_index, "calls": calls})
    return parsed_frames


def parse_text_log(path: Path, last_n: int | None, keyword: str | None):
    text = path.read_text(encoding="utf-8")
    blocks = re.split(r"=+\s*\n", text)
    frames = []
    current = None
    for line in text.splitlines():
        if line.startswith("===="):
            if current:
                frames.append(current)
            current = {"frameIndex": None, "calls": []}
            continue
        m_enter = re.match(r"#(\d+) \[H\] \[EnterFrame\]", line)
        if m_enter and current is not None:
            current["frameIndex"] = int(m_enter.group(1))
            continue
        m_call = re.match(r"#(\d+) \[H\] (.+),line:(\d+),(.+)\((.*)\)", line)
        if m_call and current is not None:
            args = [int(x) for x in m_call.group(5).split(",") if x]
            call = {
                "frameIndex": int(m_call.group(1)),
                "file": m_call.group(2),
                "line": int(m_call.group(3)),
                "function": m_call.group(4),
                "args": args,
            }
            if keyword:
                blob = json.dumps(call, ensure_ascii=False)
                if keyword.lower() not in blob.lower():
                    continue
            current["calls"].append(call)
    if current:
        frames.append(current)

    frames = [f for f in frames if f.get("frameIndex") is not None]
    if last_n is not None and last_n > 0:
        frames = frames[-last_n:]
    return frames


def to_markdown(frames: list[dict]) -> str:
    lines = []
    for frame in frames:
        lines.append(f"## Frame {frame['frameIndex']}")
        for call in frame.get("calls", []):
            fn = call.get("function", "unknown")
            args = ",".join(str(x) for x in call.get("args", []))
            file_ = call.get("file", "")
            line = call.get("line", 0)
            lines.append(f"- {fn}({args}) @ {file_}:{line}")
        lines.append("")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Parse LogTrack logs for Agent analysis")
    parser.add_argument("--log", required=True, help="Log file (.json/.bin.json/.log)")
    parser.add_argument("--pdb", help="Pdb json file")
    parser.add_argument("--last", type=int, default=100, help="Only keep last N frames")
    parser.add_argument("--filter", help="Keyword filter, e.g. Send")
    parser.add_argument("--out", help="Output json path")
    parser.add_argument("--md", help="Output markdown path")
    args = parser.parse_args()

    log_path = Path(args.log)
    if log_path.suffix == ".log" and not log_path.name.endswith(".bin.json"):
        frames = parse_text_log(log_path, args.last, args.filter)
    else:
        if not args.pdb:
            raise SystemExit("二进制/JSON 日志需要提供 --pdb")
        frames = parse_binary_log(load_json(log_path), load_json(Path(args.pdb)), args.last, args.filter)

    result = {
        "frameCount": len(frames),
        "lastN": args.last,
        "filter": args.filter,
        "frames": frames,
    }

    if args.out:
        Path(args.out).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.md:
        Path(args.md).write_text(to_markdown(frames), encoding="utf-8")

    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
