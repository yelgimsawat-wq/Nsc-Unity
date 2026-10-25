### 2026-10-09 — Q-20261009-giant-feel — ฟีลหุ่นยักษ์: ก้าวสะเทือน + หมัดหนัก
- Status: waiting
- Owner: Claude Code (Opus 5.5) / session e4f42754-d497-482e-bee8-7411aa092957
- Goal: ทุกก้าวที่เท้าปักพื้นมีกล้องสั่น/ฝุ่น/เสียงหนัก, หมัด-เตะที่เข้าเป้ามี hit-stop/กล้องสั่น/ประกายไฟ/เสียงเหล็ก เห็นและได้ยินทุกเครื่องใน multiplayer; ไม่เปลี่ยนกติกาหรือดาเมจ; ทดสอบใน TestArena
- Files: Assets/_Game/Scripts/Feel/ (ใหม่), Assets/_Game/Scripts/Limbs/LimbController.cs, Assets/_Game/Scripts/Limbs/LegController.cs, Assets/_Game/Scripts/Combat/LimbStrike.cs, Assets/_Game/Scripts/Camera/PlayerCam.cs, Assets/_Game/Audio/SFX/
- Context: [../Game-Design.md](../Game-Design.md) หัวข้อ "ขนาดและน้ำหนัก"; ผู้ใช้สั่งใน chat 2026-10-09
- Validation: 2026-10-09 compile ผ่าน; Play TestArena: ก้าวเท้ามี shake/เสียง/ฝุ่น, หมัดเข้ามีกล้องค้าง/FOV kick 2.7°/ประกายไฟ/เสียง; ภาพจาก screenshot กล้องอยู่ใกล้บอสเกินจนประกายไฟเห็นไม่ชัด ยังไม่ได้จูนด้วยตาจริง
- Next action: ผู้ใช้บอกเสียงต่อยฟังตลก → เปลี่ยนเป็น iron.mp3 ลด pitch + ซ้อนเสียงระเบิดทุ้มตอนหมัดหนัก (แก้ตอน Unity ปิด ยังไม่ได้คอมไพล์/ฟังจริง); ผู้ใช้ลองเล่นแล้วจูนค่าใน Assets/_Game/Resources/FeelConfig.asset (ขนาดประกายไฟ/ฝุ่น/แรงสั่น); ยังไม่ได้ทดสอบ client เครื่องที่สอง; ยังไม่ commit
