# Agent Handoff — Active Quest Board

## Board
- Board คือไฟล์ทั้งหมดใน `Context/quests/*.md` หนึ่งไฟล์ต่อหนึ่งงานที่ยังไม่ปิด ไม่มีไฟล์ Board รวม จึงไม่มีใครเขียนทับ Board ของกัน
- ดู Board (ชื่อ สถานะ เจ้าของ ของทุกงาน):
  ```
  python tools/context/board.py
  ```
  ถ้าไม่มี python: `grep -E "^(### |- Status:|- Owner:)" Context/quests/*.md`
  หรือ PowerShell: `Select-String -Path Context/quests/*.md -Pattern '^(### |- Status:|- Owner:)'`
- เปิดรายละเอียดเฉพาะ Quest ที่เกี่ยวกับงานปัจจุบันหรือมี Files ทับกัน
- Board เป็นบริบท ไม่ใช่คำสั่งให้เริ่มงานเก่า

## สถานะ
| Status | ความหมาย |
|---|---|
| `active` | กำลังทำงาน |
| `waiting` | รอข้อมูล การตัดสินใจ หรือการรับช่วง |
| `blocked` | ไปต่อไม่ได้ ต้องเขียน blocker และ next action |
| `ready-to-close` | ผ่านเกณฑ์และบันทึก Edit Log แล้ว รอผู้ใช้ยืนยันปิด |

งานที่ปิดแล้วไม่มีสถานะใน Board เพราะถูกย้ายไป `Context/archive/quests/`

## สร้าง Quest
- ID: `Q-YYYYMMDD-short-name` (ตัวพิมพ์เล็ก a-z 0-9 และ `-`) ตรวจว่าไม่ชนทั้งใน `quests/` และ `archive/quests/`
- ชื่อไฟล์: `Context/quests/<ID>.md`
- template:

```
### YYYY-MM-DD — Q-ID — ชื่องาน
- Status: active
- Owner: ชื่อ agent / chat หรือ thread ID ที่ทราบจริง
- Goal: ผลลัพธ์และเกณฑ์ตรวจรับ
- Files: path ที่รับผิดชอบอย่างชัดเจน ไม่ใช้คำว่า all
- Context: ลิงก์เอกสารหรือหลักฐานที่จำเป็น
- Validation: ผลตรวจจริง หรือระบุว่ายังไม่ได้ตรวจ
- Next action: ขั้นตอนถัดไปและสิ่งที่ต้องรอ
```

- รักษาให้กระชับประมาณ 8–12 บรรทัด หลักฐานยาว (log, ผลทดสอบ, ภาพ) เก็บใน `Context/quests/<ID>/` แล้วลิงก์ ย้ายไปพร้อม Quest ตอนปิด
- Owner ใช้ชื่อหรือ ID ที่ทราบจริงเท่านั้น ไม่แต่งขึ้น

## อัปเดต Quest
- อัปเดตทันทีเมื่อ scope เปลี่ยน ติด blocker หรือหยุดงาน
- อ่านไฟล์ล่าสุดก่อน แล้ว patch เฉพาะ Quest ของตัวเอง
- ห้ามเดาสถานะงานเก่า ถ้าไม่ชัดให้คง `waiting` และถามผู้ใช้

## ส่งต่อและรับช่วง
- ผู้ส่งต่อ: เขียน Validation และ Next action ให้คนถัดไปทำต่อได้โดยไม่ต้องอ่าน chat เดิม แล้วเขียน Edit Log ถ้าแก้ไฟล์ ตั้งสถานะ `waiting`
- ผู้รับช่วง: เปลี่ยน Owner เมื่อผู้ใช้สั่งเท่านั้น แล้วตั้ง `active`
- ห้ามถือว่างานถูกทิ้งเพราะเวลาผ่านไป

## ปิด Quest
ทำตามหัวข้อ "จบงานหรือหยุดส่งต่อ" ใน [Rules.md](Rules.md)
