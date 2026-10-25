### 2026-10-09 — Q-20261009-test-scene — ฉากทดสอบใหม่ (มีบอส + ระบบที่ผู้เล่นต้องใช้ครบ ไม่มีแมพ)
- Status: ready-to-close
- Owner: Claude Code (Opus 5.5) / session e4f42754-d497-482e-bee8-7411aa092957
- Goal: ฉากใหม่ใน `Assets/_Game/Scenes/Test/` ที่กด Play แล้วเล่นได้ทันที: หุ่น, ล็อบบี้เลือกแขนขา, MatchSession, HUD, กล้อง, เมนูในเกม, บอสที่เดินหาและตีผู้เล่นได้ (NavMesh), หน้าแพ้/ชนะ; พื้นเรียบแทนแมพ
- Files: Assets/_Game/Scenes/Test/, ProjectSettings/EditorBuildSettings.asset (เพิ่ม TestArena ท้ายรายการ ให้ปุ่ม RESTART โหลดฉากซ้ำได้)
- Context: ผู้ใช้สั่งใน chat 2026-10-09 "ไม่ต้องจัดแมพ เอาเป็น Scene ทดสอบอันใหม่"
- Validation: 2026-10-09 Play ใน Unity: เริ่มแมตช์ได้, หุ่นลุกยืนบนพื้นใหม่, บอสเดินตาม NavMesh ใหม่มาตีหุ่น, ต่อยโดนบอส, มือจับ GrabWall ได้, ไอเทม 3 ชิ้น spawn, ฆ่าบอสแล้วหน้า WIN ขึ้น, ไม่มี error ใหม่ใน log; NetworkObject 21 ตัว hash ไม่ซ้ำ
- Next action: รอผู้ใช้ยืนยันปิดและสั่ง commit; ฉากสร้างจากสำเนา MAPBOSS (ลบ CityBoss, MAP, Obby4, Cube, Canvas, ParticleTimeline2, Spline) + พื้น 400x400 layer Ground ที่ y=51.42 + GrabWall (layer grabLayer, kinematic Rigidbody) + SwordW
