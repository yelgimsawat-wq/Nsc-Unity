### 2026-10-31 — Q-20261031-camera-feel — กล้องหุ่น IK หมุนไว ซูมง่ายขึ้น
- Status: active
- Owner: Claude Code (Opus 5) / session 665a1d1c-91d4-4e5c-9937-52c4b030daed
- Goal: หมุนกล้อง (คลิกขวาลาก) ได้ระยะพอใช้ในหนึ่งลาก ไม่ต้องลากซ้ำหลายรอบ และล้อเมาส์ซูมเข้า/ออกถึงสุดในจำนวนคลิกที่ไม่น่ารำคาญ — ตรวจรับ: ผู้ใช้ลองเองแล้วบอกว่าคุมง่ายขึ้น
- Files: Assets/nok/BoneLimb/OrbitCamera.cs (ค่า default), Assets/nok/scene/Test/TesItem/TestItem.unity (IKRobot ไม่ใช่ prefab instance), Assets/nok/BoneLimb/IKRobotRig.prefab (ซีน RobotArena ใช้ prefab นี้ ไม่ override ค่ากล้อง)
- Context: ผู้ใช้บอก "ซูมเข้าซูมออกง่ายขึ้น หมุนตั้งนานขยับนิดเดียว" | ค่าเดิมที่วัดได้จากซีน: lookSensitivity 0.2 (ลาก 100px = 20°), zoomStep 0.1 ต่อคลิกล้อ, zoomRange 0.4–3 (ซูมสุดฝั่งเดียวต้อง ~9 คลิก เต็มช่วง ~19 คลิก) | ค่าถูก serialize บน `RobotIKControl.orbit` ของ GameObject `IKRobot` แก้แต่ default ในโค้ดอย่างเดียวไม่มีผลกับซีนที่มีอยู่
- Validation: 2026-10-31 ตั้งค่าใหม่ทั้ง 3 ที่ (โค้ด default / TestItem / IKRobotRig.prefab): lookSensitivity 0.2 → 0.45 (ลาก 200px ได้ 90°), zoomStep 0.1 → 0.2, zoomRange 0.4–3 → 0.25–4 (ซูมได้ใกล้กว่าเดิมและถอยไกลกว่าเดิม ทั้งช่วงเหลือ ~12 คลิกล้อ จากเดิม ~19) | diff ไฟล์ตรงตามที่ตั้งใจ | **ยังไม่ได้ลองหมุน/ซูมจริง** ตัวเลขเป็นค่าเริ่มต้นที่เดาจากอาการที่ผู้ใช้บอก
- Next action: ผู้ใช้ลองหมุน/ซูมจริงแล้วบอกว่าเร็วไป/ช้าไป — ปรับ `lookSensitivity` (หมุน) กับ `zoomStep`/`zoomRange` (ซูม) ต่อได้ทันทีใน Inspector ของ IKRobot ไม่ต้องแก้โค้ด
