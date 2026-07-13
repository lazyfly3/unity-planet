#!/usr/bin/env python3
"""无需 Unity / .NET 的 LogTrack 端到端演示。

模拟：插桩概念 → 记录 → 导出 JSON → 解析最近 N 帧。
"""

from __future__ import annotations

import json
import tempfile
from pathlib import Path


def encode_item(hash_id: int, arg_count: int) -> int:
    return (hash_id << 3) | (arg_count & 7)


def build_demo():
    pdb = {
        "items": [
            {"hash": 1, "argCount": 2, "file": "TestGameplay.cs", "line": 5, "dbgStr": "MovePlayer"},
            {"hash": 2, "argCount": 2, "file": "TestGameplay.cs", "line": 12, "dbgStr": "SendMessage"},
            {"hash": 3, "argCount": 2, "file": "TestGameplay.cs", "line": 18, "dbgStr": "Dispatch"},
        ]
    }

    frames = []
    for frame in range(1, 151):
        items = []
        args = []
        items.append(encode_item(1, 2))
        args.extend([3, frame % 4])
        if frame % 10 == 0:
            items.append(encode_item(2, 2))
            args.extend([1001, frame])
            items.append(encode_item(3, 2))
            args.extend([1001, frame])
        frames.append({"frameIndex": frame, "items": items, "args": args})

    log = {
        "errorFrameIndex": 0,
        "saveDateTime": "demo",
        "frames": frames[-100:],
    }
    return log, pdb


def to_text(frames, pdb_map, last_n=100):
    selected = frames[-last_n:]
    lines = []
    for frame in selected:
        lines.append("======================================================")
        lines.append(f"#{frame['frameIndex']} [H] [EnterFrame]")
        lines.append("------------------------------------------------------")
        arg_index = 0
        for item in frame["items"]:
            hash_id = item >> 3
            arg_count = item & 7
            meta = pdb_map[hash_id]
            call_args = frame["args"][arg_index : arg_index + arg_count]
            arg_index += arg_count
            lines.append(
                f"#{frame['frameIndex']} [H] {meta['file']},line:{meta['line']},{meta['dbgStr']}({','.join(map(str, call_args))})"
            )
        lines.append("======================================================")
    return "\n".join(lines)


def main():
    work = Path(tempfile.mkdtemp(prefix="logtrack_demo_"))
    log, pdb = build_demo()
    log_path = work / "track.bin.json"
    pdb_path = work / "LogPdb.pdb.json"
    text_path = work / "track.log"
    analysis_path = work / "analysis.json"

    log_path.write_text(json.dumps(log, ensure_ascii=False, indent=2), encoding="utf-8")
    pdb_path.write_text(json.dumps(pdb, ensure_ascii=False, indent=2), encoding="utf-8")
    pdb_map = {item["hash"]: item for item in pdb["items"]}
    text_path.write_text(to_text(log["frames"], pdb_map, 100), encoding="utf-8")

    print("=== LogTrack Python Demo ===")
    print("工作目录:", work)
    print("日志:", log_path)
    print("Pdb:", pdb_path)
    print("文本:", text_path)
    print()
    print("最近100帧中，含 SendMessage 的帧：")
    for frame in log["frames"]:
        if frame["frameIndex"] % 10 != 0:
            continue
        print(f"  Frame {frame['frameIndex']}: SendMessage(1001,{frame['frameIndex']})")

    print()
    print("可执行解析：")
    print(f'  python tools/parse_logtrack.py --log "{log_path}" --pdb "{pdb_path}" --last 100 --out "{analysis_path}"')


if __name__ == "__main__":
    main()
