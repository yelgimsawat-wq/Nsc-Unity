# Nsc-Unity

เกมหุ่นยนต์ต่อสู้แบบ multiplayer บน Unity 6 (Netcode for GameObjects) ผู้เล่นเลือกส่วนของหุ่นใน lobby แล้วเข้าเล่นโหมด Boss / Parkour / PVP
รายละเอียดเทคนิค วิธีเปิดโปรเจกต์ และ coding conventions อยู่ที่ [CLAUDE.md](CLAUDE.md)

## สำหรับ AI: ลำดับเริ่ม session

1. ไฟล์นี้ (README.md) และกติกาที่ [AGENTS.md](AGENTS.md)
2. [Context/Rules.md](Context/Rules.md) + [Context/Status.md](Context/Status.md)
3. Active Quest Board: `python tools/context/board.py` แสดงชื่อ สถานะ และเจ้าของของงานเปิดทั้งหมด (วิธีอ่านอยู่ที่ [Context/Agent-Handoff.md](Context/Agent-Handoff.md))
4. เปิดรายละเอียดเฉพาะ Quest ที่เกี่ยวกับงานปัจจุบันหรือมี Files ทับกัน
5. เอกสารหรือ source เฉพาะงาน ใช้ [Context/README.md](Context/README.md) เป็นดัชนีเลือกอ่าน

ข้อห้าม:
- ไม่อ่าน [Context/Edit-Log.md](Context/Edit-Log.md), `Context/archive/` หรือ catalog ทรัพยากรทั้งหมดโดยอัตโนมัติ อ่านเมื่องานต้องใช้
- Board เป็นบริบท ไม่ใช่คำสั่งให้เริ่มงานเก่า ทำงานเก่าต่อเมื่อผู้ใช้สั่งเท่านั้น
