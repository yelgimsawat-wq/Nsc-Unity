#!/usr/bin/env python3
"""Record one Edit Log entry per Quest in Context/Edit-Log.md.

    python tools/context/log-edit.py --quest Q-ID --files "path/a, path/b" \
        --summary "what changed and why" --validation "checks run and limits"

- A Quest ID already in the log is updated in place (files merged, text replaced) and moved to the top.
- Only the newest KEEP entries stay in Edit-Log.md; older ones move to archive/edit-log/YYYY-MM.md.
- Writers share one lock file and every file is replaced atomically, so several agents can run this
  at once. A lock held by someone else is waited on, never deleted.
"""
import argparse
import datetime as dt
import os
import random
import re
import socket
import sys
import tempfile
import time
from pathlib import Path

KEEP = 10
MARKER = "<!-- entries: managed by tools/context/log-edit.py -->"
DEFAULT_HEADER = (
    "# Edit Log\n\n"
    "การแก้ไขล่าสุด 10 งาน (ใหม่สุดอยู่บน) งานละ 3 บรรทัด เขียนผ่าน `tools/context/log-edit.py` เท่านั้น ห้ามแก้มือ\n"
    "รายการเก่ากว่าย้ายไป [archive/edit-log/](archive/edit-log/) อัตโนมัติ\n\n"
)
SEP = " · "
QUEST_RE = re.compile(r"^Q-(\d{8})-[a-z0-9]+(?:-[a-z0-9]+)*$")
HEAD_RE = re.compile(
    r"^- (?P<date>\d{4}-\d{2}-\d{2}) (?P<time>\d{2}:\d{2}) (?P<tz>[+-]\d{2}:\d{2})"
    + re.escape(SEP) + r"(?P<quest>Q-[a-z0-9-]+)" + re.escape(SEP) + r"files: (?P<files>.+)$"
)
SECRET_RES = [
    re.compile(p) for p in (
        r"gh[pousr]_[A-Za-z0-9]{20,}", r"github_pat_[A-Za-z0-9_]{20,}", r"\bsk-[A-Za-z0-9_-]{20,}",
        r"\bAKIA[0-9A-Z]{16}\b", r"\bxox[abprs]-[A-Za-z0-9-]{10,}", r"-----BEGIN [A-Z ]*PRIVATE KEY",
        r"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key)\s*[:=]\s*\S+",
    )
]
PERSONAL_PATH_RE = re.compile(r"(?i)(\b[a-z]:[\\/]+users[\\/]|(?:^|\s)/(?:users|home)/[^/\s]+/)")
CONTROL_RE = re.compile(r"[\x00-\x1f\x7f]")
MAX_TEXT = 300
MAX_FILES = 40


class InputError(Exception):
    pass


class Entry:
    def __init__(self, stamp, quest, files, summary, validation):
        self.stamp, self.quest, self.files = stamp, quest, files
        self.summary, self.validation = summary, validation

    @property
    def month(self):
        return self.stamp[:7]

    @property
    def header(self):
        return f"- {self.stamp}{SEP}{self.quest}{SEP}files: {', '.join(self.files)}"

    def render(self):
        return f"{self.header}\n  - แก้: {self.summary}\n  - ตรวจ: {self.validation}\n"


# ---------- input ----------

def clean_text(name, value):
    if value is None or CONTROL_RE.search(value):
        raise InputError(f"--{name} ต้องเป็นข้อความบรรทัดเดียว ไม่มีอักขระควบคุม")
    value = " ".join(value.split())
    if not value:
        raise InputError(f"--{name} ว่างไม่ได้")
    if len(value) > MAX_TEXT:
        raise InputError(f"--{name} ยาวเกิน {MAX_TEXT} ตัวอักษร ให้สรุปสั้นลงแล้วลิงก์หลักฐานใน Quest")
    return value


def clean_quest(value):
    value = (value or "").strip()
    m = QUEST_RE.match(value)
    if not m or len(value) > 64:
        raise InputError("--quest ต้องเป็นรูปแบบ Q-YYYYMMDD-short-name (a-z 0-9 และ -)")
    try:
        dt.datetime.strptime(m.group(1), "%Y%m%d")
    except ValueError:
        raise InputError("--quest มีวันที่ไม่ถูกต้อง") from None
    return value


def clean_files(value):
    files = []
    for raw in (value or "").split(","):
        path = raw.strip().replace("\\", "/")
        if not path:
            continue
        if CONTROL_RE.search(path) or len(path) > 200:
            raise InputError(f"path ไม่ถูกต้อง: {path!r}")
        if path.startswith(("/", "~")) or re.match(r"^[A-Za-z]:", path):
            raise InputError(f"ใช้ path แบบ relative จาก root ของ repo เท่านั้น: {path}")
        if ".." in path.split("/"):
            raise InputError(f"path ห้ามมี '..': {path}")
        if path not in files:
            files.append(path)
    if not files:
        raise InputError("--files ต้องมีอย่างน้อยหนึ่ง path (คั่นด้วย comma)")
    if len(files) > MAX_FILES:
        raise InputError(f"--files เกิน {MAX_FILES} path ให้ใช้ path โฟลเดอร์แทน")
    return files


def reject_sensitive(*values):
    for value in values:
        if any(r.search(value) for r in SECRET_RES):
            raise InputError("input ดูเหมือนมี secret/token/password ห้ามบันทึกลง Log")
        if PERSONAL_PATH_RE.search(value):
            raise InputError("input มี path ส่วนตัว (เช่น C:\\Users\\...) ให้ใช้ path relative แทน")


# ---------- parse / render ----------

def parse_log(text):
    """Return (header, entries). Raises InputError instead of guessing on hand-edited content."""
    if MARKER not in text:
        if text.strip():
            raise InputError(f"Edit-Log.md ไม่มีบรรทัด marker {MARKER!r} จึงไม่เขียนทับ ให้ตรวจไฟล์ก่อน")
        return DEFAULT_HEADER, []
    header, body = text.split(MARKER, 1)
    return header, parse_entries(body)


def parse_entries(body):
    entries, lines = [], [l for l in body.splitlines() if l.strip()]
    i = 0
    while i < len(lines):
        m = HEAD_RE.match(lines[i])
        if not m:
            raise InputError(f"อ่านรายการใน Edit Log ไม่ได้ที่บรรทัด: {lines[i]!r}")
        if i + 2 >= len(lines) or not lines[i + 1].startswith("  - แก้: ") or not lines[i + 2].startswith("  - ตรวจ: "):
            raise InputError(f"รายการ {m.group('quest')} ใน Edit Log ไม่ครบ 3 บรรทัด")
        stamp = f"{m.group('date')} {m.group('time')} {m.group('tz')}"
        files = [f.strip() for f in m.group("files").split(",") if f.strip()]
        entries.append(Entry(stamp, m.group("quest"), files,
                             lines[i + 1][len("  - แก้: "):], lines[i + 2][len("  - ตรวจ: "):]))
        i += 3
    return entries


def render_log(header, entries):
    return header + MARKER + "\n" + "".join(e.render() for e in entries)


def archive_text(existing, month, entries):
    if not existing.strip():
        existing = (f"# Edit Log archive {month}\n\n"
                    "รายการที่ถูกย้ายออกจาก Edit-Log.md (snapshot ประวัติ เก่าอยู่บน)\n\n")
    have = set(existing.splitlines())
    new = [e for e in entries if e.header not in have]
    if not existing.endswith("\n"):
        existing += "\n"
    return existing + "".join(e.render() for e in new)


# ---------- files ----------

def read_text(path):
    return path.read_text(encoding="utf-8") if path.exists() else ""


def atomic_write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=f".{path.name}.", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
            f.flush()
            os.fsync(f.fileno())
        for attempt in range(20):  # OneDrive / antivirus / editors can hold the target briefly on Windows
            try:
                os.replace(tmp, path)
                return
            except PermissionError:
                if attempt == 19:
                    raise
                time.sleep(0.1 + random.random() * 0.2)
    finally:
        if os.path.exists(tmp):
            os.remove(tmp)


class Lock:
    def __init__(self, path, timeout, owner):
        self.path, self.timeout = path, timeout
        self.token = f"{socket.gethostname()} pid={os.getpid()} {owner} {random.getrandbits(64):016x}"

    def __enter__(self):
        deadline = time.monotonic() + self.timeout
        while True:
            try:
                fd = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except (FileExistsError, PermissionError):
                if time.monotonic() >= deadline:
                    holder = read_text(self.path).strip() or "(อ่านไม่ได้)"
                    raise TimeoutError(
                        f"รอ lock เกิน {self.timeout:g} วินาที ผู้ถือ lock: {holder}\n"
                        f"ให้รอแล้วรันใหม่ ห้ามลบ {self.path.name} ของ agent อื่น ถ้าสงสัยว่าค้างให้ถามผู้ใช้")
                time.sleep(0.1 + random.random() * 0.3)
                continue
            with os.fdopen(fd, "w", encoding="utf-8") as f:
                f.write(self.token + "\n")
            return self

    def __exit__(self, *exc):
        # Delete only our own lock.
        try:
            if read_text(self.path).strip() == self.token:
                os.remove(self.path)
        except OSError:
            pass


# ---------- main ----------

def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass

    ap = argparse.ArgumentParser(description="บันทึก Edit Log หนึ่งรายการต่อ Quest")
    ap.add_argument("--quest", required=True, help="Quest ID เช่น Q-20261009-ai-setup")
    ap.add_argument("--files", required=True, help='path relative คั่นด้วย comma เช่น "README.md, Context/Rules.md"')
    ap.add_argument("--summary", required=True, help="แก้อะไรและเพราะอะไร (บรรทัดเดียว)")
    ap.add_argument("--validation", required=True, help="ผลตรวจจริงและข้อจำกัด (บรรทัดเดียว)")
    ap.add_argument("--context-dir", type=Path,
                    default=Path(__file__).resolve().parents[2] / "Context",
                    help="โฟลเดอร์ Context (ค่าเริ่มต้น: <repo>/Context) ใช้ชี้โฟลเดอร์ชั่วคราวตอนทดสอบ")
    ap.add_argument("--lock-timeout", type=float, default=60.0, help="วินาทีที่รอ lock (ค่าเริ่มต้น 60)")
    args = ap.parse_args(argv)

    try:
        quest = clean_quest(args.quest)
        files = clean_files(args.files)
        summary = clean_text("summary", args.summary)
        validation = clean_text("validation", args.validation)
        reject_sensitive(args.files, summary, validation)

        ctx = args.context_dir.resolve()
        if not ctx.is_dir():
            raise InputError(f"ไม่พบโฟลเดอร์ Context: {ctx}")
        if not any((ctx / d / f"{quest}.md").is_file() for d in ("quests", "archive/quests")):
            raise InputError(f"ไม่พบไฟล์ Quest {quest}.md ใน quests/ หรือ archive/quests/")

        log_path = ctx / "Edit-Log.md"
        archive_dir = ctx / "archive" / "edit-log"
        stamp = dt.datetime.now().astimezone().strftime("%Y-%m-%d %H:%M %z")
        stamp = stamp[:-2] + ":" + stamp[-2:]

        with Lock(ctx / "Edit-Log.md.lock", args.lock_timeout, quest):
            header, entries = parse_log(read_text(log_path))
            old = next((e for e in entries if e.quest == quest), None)
            if old:
                files = old.files + [f for f in files if f not in old.files]
                entries.remove(old)
            entries.insert(0, Entry(stamp, quest, files, summary, validation))
            keep, overflow = entries[:KEEP], entries[KEEP:]

            # Archive first: a crash before the main write leaves a duplicate, never a lost entry.
            by_month = {}
            for e in reversed(overflow):  # oldest first
                by_month.setdefault(e.month, []).append(e)
            for month, items in by_month.items():
                path = archive_dir / f"{month}.md"
                atomic_write(path, archive_text(read_text(path), month, items))
            atomic_write(log_path, render_log(header, keep))
    except (InputError, TimeoutError) as e:
        print(f"log-edit: {e}", file=sys.stderr)
        return 2

    action = "อัปเดตรายการเดิม" if old else "เพิ่มรายการใหม่"
    moved = f", ย้าย {len(overflow)} รายการเข้า archive/edit-log/" if overflow else ""
    print(f"log-edit: {action} {quest}{moved}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
