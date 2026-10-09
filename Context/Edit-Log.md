# Edit Log

การแก้ไขล่าสุด 10 งาน (ใหม่สุดอยู่บน) งานละ 3 บรรทัด เขียนผ่าน `tools/context/log-edit.py` เท่านั้น ห้ามแก้มือ
รายการเก่ากว่าย้ายไป [archive/edit-log/](archive/edit-log/) อัตโนมัติ

<!-- entries: managed by tools/context/log-edit.py -->
- 2026-10-09 15:09 +07:00 · Q-20261009-folder-cleanup · files: Assets/_Game/, Assets/Plugins/Demigiant/, Assets/TextMesh Pro/, Assets/Settings/, Assets/nok/, Assets/Yelmee/, Assets/petong/, Assets/Scenes/, ProjectSettings/EditorBuildSettings.asset, CLAUDE.md
  - แก้: ย้ายของที่เกมใช้จริงเข้า Assets/_Game ตามประเภท (คง GUID), ย้าย DOTween/TMP/URP settings ไปที่มาตรฐาน, ลบไฟล์ขยะ, ล้าง build list, แก้ path ใน Editor tools; แก้บรรทัดฉากเมนูใน CLAUDE.md
  - ตรวจ: 2026-10-09: compile ผ่าน, EditMode 101/101, prefab 55 ตัวไม่มี missing script, Play MAPBOSS ผ่าน; Parkour/PVP ยังไม่ได้เล่นหลังย้าย
- 2026-10-09 15:00 +07:00 · Q-20261009-ai-setup · files: README.md, AGENTS.md, CLAUDE.md, .gitignore, Context/README.md, Context/Rules.md, Context/Status.md, Context/Agent-Handoff.md, Context/Edit-Log.md, Context/quests/Q-20261009-ai-setup.md, Context/archive/, tools/context/log-edit.py, tools/context/board.py, .claude/settings.local.json
  - แก้: ปรับ path หลังย้ายโฟลเดอร์ (59762f00) ใน CLAUDE.md และ Context, เพิ่ม SessionStart hook แสดง Board (เฉพาะเครื่องนี้), ปิด Quest ตามที่ผู้ใช้สั่ง
  - ตรวจ: 2026-10-09 ลิงก์/path 74 จุดครบ, path ใหม่ตรวจกับ git ls-files, hook รันได้ exit 0 (มีผลเมื่อเปิด session ใหม่), ไม่ได้รัน build เกม
