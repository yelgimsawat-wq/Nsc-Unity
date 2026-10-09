#!/usr/bin/env python3
"""Print the Active Quest Board: ID, status, owner, title and files of every open Quest.

    python tools/context/board.py

Reads only the header lines of Context/quests/*.md, so it is cheap to run at the start of every session.
"""
import argparse
import re
import sys
from pathlib import Path

STATUSES = {"active", "waiting", "blocked", "ready-to-close"}
TITLE_RE = re.compile(r"^###\s+(\S+)\s+—\s+(\S+)\s+—\s+(.+)$")


def field(lines, name):
    prefix = f"- {name}:"
    return next((l[len(prefix):].strip() for l in lines if l.startswith(prefix)), "")


def main(argv=None):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass
    ap = argparse.ArgumentParser(description="แสดง Active Quest Board")
    ap.add_argument("--context-dir", type=Path, default=Path(__file__).resolve().parents[2] / "Context")
    args = ap.parse_args(argv)

    files = sorted((args.context_dir / "quests").glob("*.md"))
    if not files:
        print("Board ว่าง: ไม่มี Quest ที่เปิดอยู่")
        return 0
    for path in files:
        lines = path.read_text(encoding="utf-8").splitlines()[:15]
        m = next((TITLE_RE.match(l) for l in lines if TITLE_RE.match(l)), None)
        quest_id, title = (m.group(2), m.group(3)) if m else (path.stem, "(ไม่พบบรรทัดหัว ### ตาม template)")
        status = field(lines, "Status") or "?"
        warn = []
        if quest_id != path.stem:
            warn.append(f"ชื่อไฟล์ไม่ตรง ID ({path.name})")
        if status not in STATUSES:
            warn.append(f"สถานะไม่อยู่ในรายการ: {status}")
        print(f"{quest_id} [{status}] {title}")
        print(f"    Owner: {field(lines, 'Owner') or '?'}")
        print(f"    Files: {field(lines, 'Files') or '?'}")
        for w in warn:
            print(f"    ! {w}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
