# Status — บริบทปัจจุบัน

ไฟล์นี้เก็บสภาพโปรเจกต์ ณ วันที่ตรวจ ไม่ใช่ backlog งานที่ต้องทำอยู่ใน Board ([Agent-Handoff.md](Agent-Handoff.md))
แก้เฉพาะบรรทัดที่ตรวจใหม่ พร้อมวันที่และแหล่งหลักฐาน

ตรวจล่าสุด: 2026-10-09 (Q-20261009-ai-setup)

## โปรเจกต์
| เรื่อง | สภาพ | หลักฐาน (วันที่ตรวจ) |
|---|---|---|
| Unity | 6000.3.6f1, URP | `ProjectSettings/ProjectVersion.txt` (2026-10-09) |
| Branch ที่ทำงาน | `refactor/class-diagram` HEAD `2f7a6d6f` "แก้Code" ย้ายโค้ด gameplay เข้า `Assets/_Game/Scripts` ตาม class diagram (267 ไฟล์) | `git log`, `git show --stat` (2026-10-09) |
| Working tree | `Nsc-Unity.slnx` ถูกแก้ (ไฟล์ IDE generate ไม่อยู่ใน Quest ใด) | `git status` (2026-10-09) |
| โครงสร้างโค้ด | ดู [../CLAUDE.md](../CLAUDE.md) หัวข้อ Code Structure | — |
| EditMode tests | อยู่ใน `Assets/Editor/` เช่น `FallRulesTests.cs` ผลล่าสุดที่ทราบ 101/101 | บันทึก play-test ของ session Claude 2026-10-09 (ไม่ได้รันซ้ำ) |

## ปัญหาที่ทราบ (snapshot ยังไม่ได้ตรวจซ้ำ ไม่ใช่ Quest)
ที่มา: บันทึก play-test ของ session Claude 2026-10-09 ซึ่งระบุว่าเป็นปัญหาข้อมูล scene ที่มีอยู่ก่อน refactor จะเปิดเป็น Quest เมื่อผู้ใช้สั่งเท่านั้น
- `Assets/nok/scene/Game/PVP.unity`: PistolW (`busternscP`) 4 ตัวใช้ GlobalObjectIdHash ซ้ำกัน ทำให้ StartHost ในโหมด PVP ล้ม
- `Assets/nok/scene/Game/MAPPAKUAR.unity`: RespawnManager ไม่มี defaultSpawnPoint (respawn ไปที่ world origin) และ checkpoint `save1` มี respawnPoint ลอยเหนือที่ว่าง

## ข้อจำกัดของเครื่องที่ตรวจ (2026-10-09)
- มี Python 3.14 (`python`, `py`) แต่ไม่มี `rg` ให้ค้นด้วย `git grep` / `grep` / `Select-String`
- repo อยู่ใน OneDrive ซึ่งอาจล็อกไฟล์ชั่วครู่ระหว่างซิงค์ `log-edit.py` จึงลองเขียนซ้ำเอง
