#!/usr/bin/env python3
"""Remove legacy LogTrack source-level instrumentation (try/finally + FSPDebuger hooks)."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

# Matches the old source patch wrapper injected by FSPDebugerTool.
_SOURCE_PATCH_RE = re.compile(
    r"\{bool __logTrackDepthEntered = FSPDebuger\.EnableLogTrackInternal;\s*"
    r"if\(__logTrackDepthEntered\)\{FSPDebuger\.PushDepth\(\);FSPDebuger\.LogTrack\([^;]*\);\}\s*"
    r"try\s*\{\s*"
    r"(.*?)"
    r"\}\s*finally\s*\{\s*"
    r"if\(__logTrackDepthEntered\)FSPDebuger\.PopDepth\(\);\s*"
    r"\}\}",
    re.DOTALL,
)

_MULTILINE_SOURCE_PATCH_RE = re.compile(
    r"bool __logTrackDepthEntered = FSPDebuger\.EnableLogTrackInternal;\s*"
    r"if\(__logTrackDepthEntered\)\{FSPDebuger\.PushDepth\(\);FSPDebuger\.LogTrack\([^;]*\);\}\s*"
    r"try\s*\{\s*"
    r"(.*?)"
    r"\}\s*finally\s*\{\s*"
    r"if\(__logTrackDepthEntered\)FSPDebuger\.PopDepth\(\);\s*"
    r"\}\s*\}",
    re.DOTALL,
)

_INLINE_SOURCE_PATCH_RE = re.compile(
    r"\{if\(FSPDebuger\.EnableLogTrackInternal\)\{FSPDebuger\.PushDepth\(\);FSPDebuger\.LogTrack\([^;]*\);\}\s*"
    r"(.*?)"
    r"\s*if\(FSPDebuger\.EnableLogTrackInternal\)FSPDebuger\.PopDepth\(\);\}",
    re.DOTALL,
)


def _wrap_body(body: str) -> str:
    if not body:
        return "{\n}"
    if not body.startswith("\n"):
        body = "\n" + body
    body = body.rstrip()
    if not body.endswith("\n"):
        body += "\n"
    return "{" + body + "}"


def strip_content(text: str) -> tuple[str, int]:
    count = 0

    def _apply(pattern: re.Pattern[str], content: str) -> str:
        nonlocal count

        def _repl(match: re.Match[str]) -> str:
            nonlocal count
            count += 1
            return _wrap_body(match.group(1))

        return pattern.sub(_repl, content)

    text = _apply(_SOURCE_PATCH_RE, text)
    text = _apply(_MULTILINE_SOURCE_PATCH_RE, text)
    text = _apply(_INLINE_SOURCE_PATCH_RE, text)
    return text, count


def strip_file(path: Path, dry_run: bool) -> int:
    original = path.read_text(encoding="utf-8")
    stripped, count = strip_content(original)
    if count == 0:
        return 0
    if not dry_run:
        path.write_text(stripped, encoding="utf-8", newline="\n")
    return count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path, help="Directory to scan (e.g. Assets/Scripts)")
    parser.add_argument("--dry-run", action="store_true", help="Report only, do not write")
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.is_dir():
        raise SystemExit(f"Not a directory: {root}")

    total_files = 0
    total_patches = 0
    for path in sorted(root.rglob("*.cs")):
        count = strip_file(path, args.dry_run)
        if count:
            total_files += 1
            total_patches += count
            action = "would strip" if args.dry_run else "stripped"
            print(f"{action} {count} block(s): {path}")

    print(f"Done: {total_patches} block(s) in {total_files} file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
