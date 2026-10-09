# Status — บริบทปัจจุบัน

ไฟล์นี้เก็บสภาพโปรเจกต์ ณ วันที่ตรวจ ไม่ใช่ backlog งานที่ต้องทำอยู่ใน Board ([Agent-Handoff.md](Agent-Handoff.md))
แก้เฉพาะบรรทัดที่ตรวจใหม่ พร้อมวันที่และแหล่งหลักฐาน

ตรวจล่าสุด: 2026-10-09 (Q-20261009-ai-setup)

## โปรเจกต์
| เรื่อง | สภาพ | หลักฐาน (วันที่ตรวจ) |
|---|---|---|
| Unity | 6000.3.6f1, URP | `ProjectSettings/ProjectVersion.txt` (2026-10-09) |
| Branch ที่ทำงาน | `refactor/class-diagram` HEAD `59762f00` "จัดโฟลเดอร์" ต่อจาก `2f7a6d6f` "แก้Code" (refactor ตาม class diagram) | `git log` (2026-10-09) |
| โครงโฟลเดอร์ | ของที่เกมใช้อยู่ใน `Assets/_Game/` แยกตามประเภท โฟลเดอร์ชื่อคนเป็น sandbox รายละเอียดใน [../CLAUDE.md](../CLAUDE.md) หัวข้อ Code Structure และ Assets Organization | `git ls-files` (2026-10-09) |
| EditMode tests | `Assets/_Game/Editor/Tests/` (`FallRulesTests.cs`, `ItemAuthorityTests.cs`) ผลล่าสุดที่ทราบ 101/101 | Validation ของ Q-20261009-folder-cleanup 2026-10-09 (ไม่ได้รันซ้ำใน Quest นี้) |
| Play Mode หลังย้ายโฟลเดอร์ | MAPBOSS ผ่าน, Parkour และ PVP ยังไม่ได้เล่น | Validation ของ Q-20261009-folder-cleanup 2026-10-09 |
| `dotnet build` | ใช้ไม่ได้จนกว่า Unity จะ regenerate `.csproj` (DOTween ย้ายเข้า `Assets/Plugins`) | Validation ของ Q-20261009-folder-cleanup 2026-10-09 |

## ปัญหาที่ทราบ (snapshot ยังไม่ได้ตรวจซ้ำ ไม่ใช่ Quest)
ที่มา: บันทึก play-test ของ session Claude 2026-10-09 (ก่อนย้ายโฟลเดอร์) ระบุว่าเป็นปัญหาข้อมูล scene ที่มีอยู่ก่อน refactor จะเปิดเป็น Quest เมื่อผู้ใช้สั่งเท่านั้น
- `Assets/_Game/Scenes/PVP.unity`: PistolW (`busternscP`) 4 ตัวใช้ GlobalObjectIdHash ซ้ำกัน ทำให้ StartHost ในโหมด PVP ล้ม
- `Assets/_Game/Scenes/MAPPAKUAR.unity`: RespawnManager ไม่มี defaultSpawnPoint (respawn ไปที่ world origin) และ checkpoint `save1` มี respawnPoint ลอยเหนือที่ว่าง

## ข้อจำกัดของเครื่องที่ตรวจ (2026-10-09)
- มี Python 3.14 (`python`, `py`) แต่ไม่มี `rg` ให้ค้นด้วย `git grep` / `grep` / `Select-String`
- repo อยู่ใน OneDrive ซึ่งอาจล็อกไฟล์ชั่วครู่ระหว่างซิงค์ `log-edit.py` จึงลองเขียนซ้ำเอง
- Claude Code เครื่องนี้มี SessionStart hook ใน `.claude/settings.local.json` (ไม่อยู่ใน git) แสดง Board ตอนเริ่ม session
