# Edit Log

การแก้ไขล่าสุด 10 งาน (ใหม่สุดอยู่บน) งานละ 3 บรรทัด เขียนผ่าน `tools/context/log-edit.py` เท่านั้น ห้ามแก้มือ
รายการเก่ากว่าย้ายไป [archive/edit-log/](archive/edit-log/) อัตโนมัติ

<!-- entries: managed by tools/context/log-edit.py -->
- 2026-10-26 22:25 +07:00 · Q-20261026-robot-heavy-feel · files: Assets/_Game/Scripts/Limbs/ArmController.cs, Assets/_Game/Scripts/Limbs/ArmInput.cs, Assets/_Game/Scripts/Dev/ArmStepTest.cs, Assets/nok/scene/Test/TesItem/TestItem.unity, Context/quests/Q-20261026-robot-heavy-feel.md
  - แก้: หุ่น TestItem หนักแน่นขึ้น: เป้ามือยึดกับไหล่ + ท่าพักห้อย (แก้แขนกางตอนเทสคนเดียว), ArmInput ตั้งจุดเล็งใหม่ตอนสลับแขน, ในซีน: มวลลำตัว 50 ขา 5 แขน 2, angular damping 5/2, ศอกซ้ายเปิด limit, มือไล่เป้าช้าลง (0.2 วิ, 30 m/s), mouseReach 4->2, uprightSpring 15
  - ตรวจ: 2026-10-26 compile ผ่าน; Play TestItem ผ่าน MCP: ยืนเอียง 37->4.5 องศา, มือเหวี่ยง 8 ม. ลำตัวหมุน 1.46->0.31 rad/s, ศอกสั่น 0.10->0.014, ปุ่ม C สลับชิ้นได้; ยังไม่ตรวจเดินด้วยมือจริง/ต่อย/หลายเครื่อง; ซีนยังไม่ได้ save
- 2026-10-26 14:21 +07:00 · Q-20261026-nsc-robot-model · files: Assets/_Game/Art/Models/Robot/NSC/, Assets/nok/scene/Test/TesItem/TestItem.unity, Assets/_Game/Scripts/Camera/PlayerCam.cs, Context/quests/Q-20261026-nsc-robot-model.md
  - แก้: ใส่โมเดลหุ่น ROBOTNSC แทนหุ่นเดิมในฉาก TestItem: ย้าย body/joint/collider ตามข้อต่อโมเดล, เปิด Interpolate แก้ภาพกระตุก, PlayerCam ไม่นับหุ่นตัวเองเป็นสิ่งบังกล้อง
  - ตรวจ: 2026-10-26 Play ใน TestItem: compile ผ่าน, ไม่กระตุก, เดิน/ต่อยไม่ล้มหลังยืนนิ่ง แต่โยกกว่าสัดส่วนก่อน และล้ม 1 ครั้งตอนเดินทันทีหลังลุก; ยังไม่ทดสอบเตะ/ปีน/หลายเครื่อง
- 2026-10-13 17:35 +07:00 · Q-20261009-smooth-walk · files: Assets/_Game/Scripts/Limbs/LegController.cs, Assets/_Game/Scripts/Limbs/LegInput.cs, Assets/_Game/Scripts/Robot/TorsoBalance.cs, Context/Game-Design.md, Assets/_Game/Scripts/Camera/PlayerCam.cs, Assets/_Game/Scripts/Match/LimbSelection.cs, Assets/_Game/Scripts/Match/LimbControlBinder.cs, Assets/_Game/Scripts/Limbs/ArmInput.cs
  - แก้: คืนกล้องเป็นแบบล็อกมุม+คลิกขวาหมุนได้ (ผู้ใช้ไม่ชอบกล้องหันตามทิศ); เพิ่มปุ่มทดสอบ C สลับไปคุมชิ้นถัดไปกลางแมตช์ (Editor/Dev build)
  - ตรวจ: 2026-10-13 compile ผ่าน; Play TestArena: สลับ LeftLeg/RightLeg/แขน ผ่าน DebugCycleLimbRpc ได้ input ผูกชิ้นใหม่ กล้องมุมเดิม; ยังไม่ได้กดปุ่มจริง
- 2026-10-09 15:59 +07:00 · Q-20261009-giant-feel · files: Assets/_Game/Scripts/Feel/, Assets/_Game/Scripts/Combat/IImpactFeedback.cs, Assets/_Game/Scripts/Combat/LimbStrike.cs, Assets/_Game/Scripts/Limbs/LimbController.cs, Assets/_Game/Scripts/Limbs/LegController.cs, Assets/_Game/Scripts/Robot/LimbHealth.cs, Assets/_Game/Scripts/Camera/PlayerCam.cs, Assets/_Game/Resources/FeelConfig.asset, Assets/_Game/Art/VFX/Feel/, Assets/_Game/Audio/SFX/
  - แก้: ระบบ Feel: เท้าปักพื้น/หมัดเข้า/โดนบอส ส่ง RPC ให้ทุกเครื่องเล่นกล้องสั่น กล้องค้าง FOV kick ฝุ่น ประกายไฟ เสียง ค่าจูนอยู่ใน FeelConfig
  - ตรวจ: 2026-10-09 compile ผ่าน, Play TestArena เอฟเฟกต์ทำงาน, ยังไม่ได้จูนด้วยตาและยังไม่ได้ทดสอบ client
- 2026-10-09 15:34 +07:00 · Q-20261009-test-scene · files: Assets/_Game/Scenes/Test/, ProjectSettings/EditorBuildSettings.asset
  - แก้: สร้างฉากทดสอบ TestArena จากสำเนา MAPBOSS: ตัดแมพทิ้ง ใส่พื้นเรียบ เสาทดสอบมือจับ ดาบ ย้ายปืนใกล้หุ่น bake NavMesh ใหม่ เพิ่มเข้า build list
  - ตรวจ: 2026-10-09 Play: เริ่มเกม/ลุก/บอสเดินมาตี/ต่อย/จับเสา/ฆ่าบอสขึ้น WIN ผ่าน ไม่มี error ใหม่
- 2026-10-09 15:17 +07:00 · Q-20261009-game-design · files: Context/Game-Design.md, Context/README.md
  - แก้: จดทิศทางเกมจาก chat: ฟีล Pacific Rim, ทำต่อบนระบบเดิม, 3 เรื่องที่ต้องพิสูจน์, ไอเดียความสนุกและทางไปขาย แยกตัดสินใจแล้ว/ยังเป็นไอเดีย
  - ตรวจ: 2026-10-09: ลิงก์ relative ตรวจแล้วมีไฟล์จริง
- 2026-10-09 15:09 +07:00 · Q-20261009-folder-cleanup · files: Assets/_Game/, Assets/Plugins/Demigiant/, Assets/TextMesh Pro/, Assets/Settings/, Assets/nok/, Assets/Yelmee/, Assets/petong/, Assets/Scenes/, ProjectSettings/EditorBuildSettings.asset, CLAUDE.md
  - แก้: ย้ายของที่เกมใช้จริงเข้า Assets/_Game ตามประเภท (คง GUID), ย้าย DOTween/TMP/URP settings ไปที่มาตรฐาน, ลบไฟล์ขยะ, ล้าง build list, แก้ path ใน Editor tools; แก้บรรทัดฉากเมนูใน CLAUDE.md
  - ตรวจ: 2026-10-09: compile ผ่าน, EditMode 101/101, prefab 55 ตัวไม่มี missing script, Play MAPBOSS ผ่าน; Parkour/PVP ยังไม่ได้เล่นหลังย้าย
- 2026-10-09 15:00 +07:00 · Q-20261009-ai-setup · files: README.md, AGENTS.md, CLAUDE.md, .gitignore, Context/README.md, Context/Rules.md, Context/Status.md, Context/Agent-Handoff.md, Context/Edit-Log.md, Context/quests/Q-20261009-ai-setup.md, Context/archive/, tools/context/log-edit.py, tools/context/board.py, .claude/settings.local.json
  - แก้: ปรับ path หลังย้ายโฟลเดอร์ (59762f00) ใน CLAUDE.md และ Context, เพิ่ม SessionStart hook แสดง Board (เฉพาะเครื่องนี้), ปิด Quest ตามที่ผู้ใช้สั่ง
  - ตรวจ: 2026-10-09 ลิงก์/path 74 จุดครบ, path ใหม่ตรวจกับ git ls-files, hook รันได้ exit 0 (มีผลเมื่อเปิด session ใหม่), ไม่ได้รัน build เกม
