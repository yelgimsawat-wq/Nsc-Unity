# Context — ดัชนีเอกสาร

เลือกอ่านตามหัวข้อของงาน ไม่ต้องอ่านทุกไฟล์ แต่ละเรื่องมีเอกสารเจ้าของหลักแห่งเดียว ที่อื่นให้ลิงก์มาแทนการคัดลอก

| หัวข้อ | เอกสารเจ้าของ | อ่านเมื่อ |
|---|---|---|
| ลำดับเริ่ม session | [../README.md](../README.md) | ทุก session |
| กติกา AI (ภาษา การอ่าน การแก้ การรายงาน) | [../AGENTS.md](../AGENTS.md) | ทุก session |
| workflow, ขอบเขต, หลาย agent, เกณฑ์จบ, Edit Log, โน้ตหัวข้อล่าสุด | [Rules.md](Rules.md) | ทุก session |
| บริบทปัจจุบันของโปรเจกต์และข้อจำกัด | [Status.md](Status.md) | ทุก session |
| Board, สถานะ Quest, template, การส่งต่องาน | [Agent-Handoff.md](Agent-Handoff.md) | เมื่อสร้าง อัปเดต รับช่วง หรือปิด Quest |
| งานที่ยังไม่ปิด | [quests/](quests/) | อ่านหัว (ชื่อ สถานะ เจ้าของ) ทุกไฟล์ผ่าน Board, รายละเอียดเฉพาะที่เกี่ยวข้อง |
| การแก้ไขล่าสุด | [Edit-Log.md](Edit-Log.md) | เมื่องานต้องรู้ว่าใครเพิ่งแก้อะไร ไม่อ่านอัตโนมัติ |
| งานปิดแล้ว, Log เก่า, snapshot | [archive/](archive/) | เมื่อค้นประวัติเท่านั้น |
| ภาพรวมโปรเจกต์, สถาปัตยกรรมโค้ด, conventions, คำสั่ง dev, pitfalls, MCP | [../CLAUDE.md](../CLAUDE.md) | ก่อนแก้โค้ดหรือ scene |
| class diagram ปัจจุบัน (โค้ดใน `Assets/_Game/Scripts` ยึดตามนี้) | [../ArchitectureDiagram/uml/class-packages.drawio](../ArchitectureDiagram/uml/class-packages.drawio) | งานที่กระทบโครงสร้าง class |
| ระบบ PVP | [../Assets/nok/PVP/README_PVP.md](../Assets/nok/PVP/README_PVP.md) | งาน PVP |
| Settings UI | [../Assets/_Game/Scripts/UI/SettingsManager_README.md](../Assets/_Game/Scripts/UI/SettingsManager_README.md) | งานหน้า Settings |
| เครื่องมือ Context | [../tools/context/](../tools/context/) | `log-edit.py` (Edit Log), `board.py` (Board) |

## Snapshot (ห้ามใช้ยืนยัน runtime ปัจจุบัน)

| เอกสาร | เป็น snapshot ของ |
|---|---|
| [../ArchitectureDiagram/ARCHITECTURE_REVIEW.th.md](../ArchitectureDiagram/ARCHITECTURE_REVIEW.th.md), [CRITIQUE_RESPONSE.th.md](../ArchitectureDiagram/CRITIQUE_RESPONSE.th.md), ไฟล์ `.docx` / `.pdf` และ [for-ai-review/](../ArchitectureDiagram/for-ai-review/) | architecture review 2026-09-27 ก่อน refactor ตาม class diagram |
| [../ArchitectureDiagram/ARCHITECTURE_REVIEW.md](../ArchitectureDiagram/ARCHITECTURE_REVIEW.md) | review ภาษาอังกฤษรุ่นเก่ากว่า ไม่ตรงกับฉบับภาษาไทย |
| `Context/archive/` ทั้งหมด | ประวัติ ณ วันที่ระบุในแต่ละไฟล์ |
