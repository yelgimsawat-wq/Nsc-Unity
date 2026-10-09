# Edit Log

การแก้ไขล่าสุด 10 งาน (ใหม่สุดอยู่บน) งานละ 3 บรรทัด เขียนผ่าน `tools/context/log-edit.py` เท่านั้น ห้ามแก้มือ
รายการเก่ากว่าย้ายไป [archive/edit-log/](archive/edit-log/) อัตโนมัติ

<!-- entries: managed by tools/context/log-edit.py -->
- 2026-10-09 14:50 +07:00 · Q-20261009-folder-cleanup · files: Assets/_Game/, Assets/Plugins/Demigiant/, Assets/TextMesh Pro/, Assets/Settings/, Assets/nok/, Assets/Yelmee/, Assets/petong/, Assets/Scenes/, ProjectSettings/EditorBuildSettings.asset
  - แก้: ย้ายของที่เกมใช้จริงเข้า Assets/_Game ตามประเภท (คง GUID), ย้าย DOTween/TMP/URP settings ไปที่มาตรฐาน, ลบไฟล์ขยะ, ล้าง build list, แก้ path ใน Editor tools
  - ตรวจ: 2026-10-09: compile ผ่าน, EditMode 101/101, prefab 55 ตัวไม่มี missing script, Play MAPBOSS ผ่าน; Parkour/PVP ยังไม่ได้เล่นหลังย้าย
- 2026-10-09 14:22 +07:00 · Q-20261009-ai-setup · files: README.md, AGENTS.md, CLAUDE.md, .gitignore, Context/README.md, Context/Rules.md, Context/Status.md, Context/Agent-Handoff.md, Context/Edit-Log.md, Context/quests/Q-20261009-ai-setup.md, Context/archive/, tools/context/log-edit.py, tools/context/board.py
  - แก้: ติดตั้งระบบ AI Setup: README เป็นทางเข้า, AGENTS.md, Context/ (Rules, Status, Handoff, quests, Edit Log, archive), log-edit.py และ board.py; CLAUDE.md import AGENTS.md
  - ตรวจ: 2026-10-09 ลิงก์ครบ (ยกเว้น Assets/Something/ ใน CLAUDE.md ที่หายมาก่อน), log-edit.py ผ่าน 34 กรณีในโฟลเดอร์ชั่วคราว, ไม่ได้รัน build เกม
