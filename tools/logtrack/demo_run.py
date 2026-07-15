#!/usr/bin/env python3
"""LogTrack 端到端演示 — 新格式 Phase + depth + Class::Method。"""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PARSE = ROOT / "tools" / "parse_logtrack.py"
DEMO_PROJ = ROOT / "Demo" / "LogTrackDemo.csproj"


def _dotnet_available() -> bool:
    proc = subprocess.run(["dotnet", "--version"], capture_output=True, text=True)
    return proc.returncode == 0


def main():
    print("=== LogTrack smoke (dotnet demo + parse) ===")
    log_path = None
    if _dotnet_available():
        proc = subprocess.run(
            ["dotnet", "run", "--project", str(DEMO_PROJ), "-c", "Release"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            cwd=str(ROOT),
        )
        print(proc.stdout)
        if proc.returncode != 0:
            print(proc.stderr, file=sys.stderr)
            sys.exit(1)
        logtrack_dir = Path(tempfile.gettempdir()) / "LogTrack"
        logs = sorted(logtrack_dir.glob("*_LogTrack_Normal.log"), key=lambda p: p.stat().st_mtime, reverse=True)
        if logs:
            log_path = logs[0]

    if log_path is None:
        fallback = ROOT / "samples" / "review_20260714" / "sample_LogTrack_Normal.log"
        if fallback.exists():
            print(f"dotnet unavailable — using review sample: {fallback}")
            log_path = fallback
        else:
            print("FAIL: no exported .log and no review sample")
            sys.exit(1)
    out_path = log_path.with_suffix(".analysis.json")
    parse_proc = subprocess.run(
        [sys.executable, str(PARSE), "--log", str(log_path), "--out", str(out_path)],
        capture_output=True,
        text=True,
    )
    if parse_proc.returncode != 0:
        print(parse_proc.stderr, file=sys.stderr)
        sys.exit(1)

    data = json.loads(out_path.read_text(encoding="utf-8"))
    frame = data["frames"][-1]
    print(f"\nFrame {frame['frameIndex']} phases:")
    for seg in frame["phases"]:
        print(f"  {seg['phase']}: {len(seg['calls'])} calls")
        for call in seg["calls"][:3]:
            print(f"    d{call['depth']} {call['call']}")

    print("\nSMOKE PASS")
    sys.exit(0)


if __name__ == "__main__":
    main()
