#!/usr/bin/env python3
"""LogTrack 日志解析工具 — Phase + depth + Class::Method 格式。"""

from __future__ import annotations

import argparse
import json
import re
import time
from pathlib import Path

PHASE_ORDER = ["FixedUpdate", "Update", "LateUpdate"]
PHASE_MARKER = re.compile(r"^-- \[Phase: (\w+)\] --\s*$")
ENTER_FRAME = re.compile(r"^#(\d+) \[H\] \[EnterFrame\]\s*$")
CALL_LINE = re.compile(
    r"^\s*0?\[(-*)(\d+)\]([^\(]+)\((.*)\)\s*$"
)


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
        phase_buckets: dict[str, list[dict]] = {p: [] for p in PHASE_ORDER}
        arg_index = 0
        for j, raw_item in enumerate(frame.get("items", [])):
            hash_id, arg_count = decode_item(int(raw_item))
            args = frame.get("args", [])[arg_index : arg_index + arg_count]
            arg_index += arg_count
            pdb_item = pdb_map.get(hash_id, {})
            depth = frame.get("depths", [])[j] if j < len(frame.get("depths", [])) else 1
            phase_id = frame.get("phases", [])[j] if j < len(frame.get("phases", [])) else 1
            phase_name = PHASE_ORDER[phase_id] if phase_id < len(PHASE_ORDER) else "Update"
            call_name = pdb_item.get("className", "") + "::" + pdb_item.get("funcName", "")
            if not pdb_item.get("className"):
                call_name = pdb_item.get("dbgStr", f"hash_{hash_id}")
            args_str = ",".join(str(x) for x in args)
            call_text = f"{call_name}({args_str})"
            entry = {"depth": depth, "call": call_text}
            if keyword and keyword.lower() not in json.dumps(entry, ensure_ascii=False).lower():
                continue
            phase_buckets.setdefault(phase_name, []).append(entry)

        phases = [{"phase": p, "calls": phase_buckets.get(p, [])} for p in PHASE_ORDER]
        parsed_frames.append({"frameIndex": frame_index, "phases": phases})
    return parsed_frames


def _parse_call_line(line: str) -> dict | None:
    m = CALL_LINE.match(line)
    if m:
        dashes, depth_str, call_name, args = m.groups()
        depth = int(depth_str)
        if depth <= 0 and dashes:
            depth = len(dashes)
        call_name = call_name.strip()
        return {"depth": depth, "call": f"{call_name}({args})"}
    return None


def parse_text_log(path: Path, last_n: int | None, keyword: str | None):
    frames: list[dict] = []
    current: dict | None = None
    current_phase: str | None = None

    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("===="):
            if current is not None:
                frames.append(current)
            current = {
                "frameIndex": None,
                "phases": {p: [] for p in PHASE_ORDER},
            }
            current_phase = None
            continue

        m_enter = ENTER_FRAME.match(line)
        if m_enter and current is not None:
            current["frameIndex"] = int(m_enter.group(1))
            continue

        m_phase = PHASE_MARKER.match(line)
        if m_phase and current is not None:
            current_phase = m_phase.group(1)
            continue

        call = _parse_call_line(line)
        if call and current is not None and current_phase:
            if keyword:
                blob = json.dumps(call, ensure_ascii=False)
                if keyword.lower() not in blob.lower():
                    continue
            current["phases"].setdefault(current_phase, []).append(call)

    if current is not None:
        frames.append(current)

    parsed = []
    for frame in frames:
        if frame.get("frameIndex") is None:
            continue
        phases = [{"phase": p, "calls": frame["phases"].get(p, [])} for p in PHASE_ORDER]
        parsed.append({"frameIndex": frame["frameIndex"], "phases": phases})

    if last_n is not None and last_n > 0:
        parsed = parsed[-last_n:]
    return parsed


def depth_from_log_line(line: str) -> int | None:
    m = re.match(r"\s*#?\d+\[(?P<depth>[^\]]*)\]", line)
    if not m:
        return None
    raw = m.group("depth") or ""
    number = re.search(r"(\d+)\s*$", raw)
    if number:
        return int(number.group(1))
    return raw.count("-")


def extract_subtree(frame: dict, phase: str = "FixedUpdate", root: str | None = None) -> list[dict]:
    """Extract calls from a phase segment, optionally from root call downward."""
    phase_calls = []
    for seg in frame.get("phases", []):
        if seg.get("phase") == phase:
            phase_calls = list(seg.get("calls", []))
            break

    if not root:
        return phase_calls

    start = next((i for i, c in enumerate(phase_calls) if root in c.get("call", "")), None)
    if start is None:
        return []
    root_depth = phase_calls[start].get("depth", 1)
    out = [phase_calls[start]]
    for call in phase_calls[start + 1 :]:
        if call.get("depth", 0) <= root_depth:
            break
        out.append(call)
    return out


def to_markdown(frames: list[dict]) -> str:
    lines = []
    for frame in frames:
        lines.append(f"## Frame {frame['frameIndex']}")
        for seg in frame.get("phases", []):
            lines.append(f"### {seg.get('phase', '?')}")
            for call in seg.get("calls", []):
                lines.append(f"- [d{call.get('depth')}] {call.get('call')}")
        lines.append("")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Parse LogTrack logs for Agent analysis")
    parser.add_argument("--log", required=True, help="Log file (.json/.bin.json/.log)")
    parser.add_argument("--pdb", help="Pdb json file")
    parser.add_argument("--last", type=int, default=100, help="Only keep last N frames")
    parser.add_argument("--filter", help="Keyword filter")
    parser.add_argument("--out", help="Output json path")
    parser.add_argument("--md", help="Output markdown path")
    parser.add_argument("--benchmark-out", help="Write parse timing to benchmark json")
    args = parser.parse_args()

    t0 = time.perf_counter()
    log_path = Path(args.log)
    if log_path.suffix == ".log" and not log_path.name.endswith(".bin.json"):
        frames = parse_text_log(log_path, args.last, args.filter)
    else:
        if not args.pdb:
            raise SystemExit("二进制/JSON 日志需要提供 --pdb")
        frames = parse_binary_log(load_json(log_path), load_json(Path(args.pdb)), args.last, args.filter)

    parse_ms = (time.perf_counter() - t0) * 1000.0

    result = {
        "frameCount": len(frames),
        "lastN": args.last,
        "filter": args.filter,
        "parse_ms": round(parse_ms, 2),
        "frames": frames,
    }

    if args.out:
        Path(args.out).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.md:
        Path(args.md).write_text(to_markdown(frames), encoding="utf-8")
    if args.benchmark_out:
        bench_path = Path(args.benchmark_out)
        bench = {}
        if bench_path.exists():
            bench = json.loads(bench_path.read_text(encoding="utf-8"))
        bench["parse_ms"] = round(parse_ms, 2)
        bench_path.write_text(json.dumps(bench, ensure_ascii=False, indent=2), encoding="utf-8")

    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
