# Edit Log

การแก้ไขล่าสุด 10 งาน (ใหม่สุดอยู่บน) งานละ 3 บรรทัด เขียนผ่าน `tools/context/log-edit.py` เท่านั้น ห้ามแก้มือ
รายการเก่ากว่าย้ายไป [archive/edit-log/](archive/edit-log/) อัตโนมัติ

<!-- entries: managed by tools/context/log-edit.py -->
- 2026-10-28 18:24 +07:00 · Q-20261028-kaiju-enemy-feel · files: Assets/nok/Enemy/model/Newpanda.fbx.meta, Context/quests/Q-20261028-kaiju-enemy-feel.md, Assets/nok/Enemy/anim/, Assets/nok/Enemy/anim/mixamo/, Assets/nok/Enemy/KaijuRootMotion.cs, Assets/nok/scene/Test/TesItem/TestItem.unity, Assets/nok/Enemy/KaijuPoseFix.cs, Assets/nok/Enemy/KaijuFeel.cs, Assets/_Game/Scripts/Enemy/EnemyCombat.cs, Assets/nok/Enemy/vfx/Kaiju Dust Cloud.prefab
  - แก้: แพนด้าไคจูใน TestItem: Humanoid+Mixamo, พุ่งจริง, ragdoll, ฟีล, VFX + ฝุ่นหนัก (ก้าวตรงฝ่าเท้าตอนแตะพื้นจริง), แก้ลอย (scale root 9.57) + ปรับระยะ/VFX ×2.17, ตัวย้วยตอนเดิน; EnemyCombat เพิ่ม vfxOnAttackStart/vfxScale
  - ตรวจ: 2026-10-28 Play ทีละเฟรม: ฝุ่นก้าวห่างกลางฝ่าเท้า 0.00 เกิดหลังเท้าถึงพื้น 1 เฟรม, เท้าติดพื้น, กระโดดทุบที่ระยะใหม่, ตัวเอียง ±6° ตอนเดิน; ยังไม่ได้ดูด้วยตา, ซีนยังไม่เซฟ
- 2026-10-27 23:30 +07:00 · Q-20261027-robot-bone-rig · files: Assets/nok/robot/newROBOT/RobotNSC_Rigged.fbx, Context/quests/Q-20261027-robot-bone-rig.md, Assets/nok/scene/Test/TesItem/TestItem.unity, Assets/nok/BoneLimb/RobotIKControl.cs, Assets/nok/BoneLimb/IKTest.unity, Assets/nok/BoneLimb/README_BoneLimb.md, Assets/nok/BoneLimb/ (ลบ RobotBoneRig/BoneChainSolver/Editor), ProjectSettings/EditorBuildSettings.asset, Assets/nok/BoneLimb/IKLimb.cs, Assets/nok/BoneLimb/JointLimit.cs, Assets/nok/BoneLimb/OrbitCamera.cs, Assets/nok/BoneLimb/IKInput.cs, Assets/nok/BoneLimb/LimbNetState.cs
  - แก้: ระบบ IK ใหม่บน RobotNSC_Rigged ใน IKTest (IK บานพับ, เคอร์เซอร์, ข้อต่อคน, หน้าเลือกชิ้นส่วนเดิม); จัดโค้ดเป็นคลาส OOP: IKLimb, JointLimit/ShoulderLimit/HipLimit, OrbitCamera, IKInput, LimbNetState; ลบ RobotBoneRig เก่า
  - ตรวจ: 2026-10-27 ก่อนจัด OOP: Play IKTest ศอก/เข่าพับตรงแกน ไม่ทะลุ มือตรงเคอร์เซอร์; หลังจัด OOP ยังไม่ได้คอมไพล์/Play เพราะ UnityMCP ต่อไม่ได้
- 2026-10-27 13:10 +00:00 · Q-20261027-bone-rig-arena · files: Assets/nok/BoneLimb/, Assets/nok/scene/RobotArena.unity, Assets/nok/scene/RobotArena/, Context/quests/Q-20261027-bone-rig-arena.md
  - แก้: เพิ่มระบบกระดูก RobotBoneRig (แขนขาเป็นกระดูกแข็ง IK ตามมือ/เท้าฟิสิกส์ ไม่แตะโค้ดเดิม; ใช้ mesh เดิมหรือโมเดลมีกระดูก Mixamo ก็ได้) + เมนูเซ็ทอัพ + EditMode tests และแมพ RobotArena ในโฟลเดอร์ซีนของ nok
  - ตรวจ: 2026-10-27 compile+test ด้วย dotnet กับ stub ผ่าน (4 EditMode + 35 ข้อ ทั้งสองโหมด), ซีน YAML ตรวจโครงสร้างผ่าน; ยังไม่ได้เปิด/Play ใน Unity
- 2026-10-26 22:46 +07:00 · Q-20261026-robot-heavy-feel · files: Assets/_Game/Scripts/Limbs/ArmController.cs, Assets/_Game/Scripts/Limbs/ArmInput.cs, Assets/_Game/Scripts/Dev/ArmStepTest.cs, Assets/nok/scene/Test/TesItem/TestItem.unity, Context/quests/Q-20261026-robot-heavy-feel.md
  - แก้: หุ่น TestItem หนักแน่นขึ้น (เป้ามือยึดไหล่, มวล/damping ใหม่, มือช้าลง, เมาส์ไวน้อยลง) + ลบ Torso (1) ที่ duplicate ซ้อนใน RobotContainer ซึ่งแย่งผูกแขนขาจนกด Q ลุกไม่ได้
  - ตรวจ: 2026-10-26 compile ผ่าน; Play TestItem: ยืนเอียง 37->4.5 องศา, มือเหวี่ยงลำตัวหมุน 1.46->0.31 rad/s, ปุ่ม C สลับชิ้นได้, ขาซ้ายกด Q ค้างลุกใน 0.5 วิ ยืนเอียง 15-23; ยังไม่ตรวจ Q จากแขน/เดินด้วยมือจริง/ต่อย/หลายเครื่อง
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
