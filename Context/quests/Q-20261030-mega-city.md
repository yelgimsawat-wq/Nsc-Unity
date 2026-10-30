### 2026-10-30 — Q-20261030-mega-city — แมพเมืองใหญ่ระดับโปรดักชัน (ตึก ถนนใหญ่ จัดแสง) สำหรับหุ่นยักษ์
- Status: waiting
- Owner: Claude Code (Opus 5.5) / session 01FCuosT4TvRKUM6vGi84K8P
- Goal: ซีนใหม่เป็นเมืองใหญ่ มีตึกหลากแบบ ถนนใหญ่ จัดแสงได้หลายแบบ คุณภาพระดับเดียวกับแมพอารีน่า (Colosseum) ไม่ใช้ asset เมืองเดิม/เดโม | ตรวจรับ: ผู้ใช้กด Build ใน Unity แล้วพอใจหน้าตา
- Files: Assets/nok/scene/MegaCity/ (ใหม่ทั้งหมด: Editor/CityBuilder.cs, Editor/CityTextures.cs, README_MegaCity.md; Generated/ กับ MegaCity.unity จะเกิดตอนกด Build)
- Context: ผู้ใช้สั่งใน chat 2026-10-30 "ไม่เอาแบบเดิม ไม่เอาเดโม ดูแมพอารีน่าเป็นต้นแบบ" → ทำเป็นเครื่องมือสร้างแบบเดียวกับ `Assets/nok/scene/RobotArena/Colosseum/Editor/ColosseumBuilder.cs` (ใช้ `MeshB` ตัวเดียวกัน อ่านอย่างเดียว ไม่แก้ไฟล์อารีน่า). ทำบน branch `claude/laughing-rubin-o69qie` (จาก `Nok`) ตามที่ผู้ใช้อนุญาต
- Validation: 2026-10-30 เครื่อง cloud ไม่มี Unity — compile ผ่านกับ UnityEngine 2021.3 + UnityEditor 2018.1 reference (NuGet) + stub ของ URP/Toon Shaders Pro (ไม่ใช่ Unity 6 จริง); รันส่วนสร้าง geometry นอก Unity ด้วย stub คณิต แล้วเรนเดอร์พรีวิวด้วย three.js: 355 ชิ้น ~490k สามเหลี่ยม ไฟถนนจริง 200 ดวง ผัง/ถนน/ลาน/ตึก/สวนถูกตำแหน่ง; รันตัวสร้าง texture นอก Unity ได้ PNG ครบ 26 ไฟล์ ดูด้วยตาแล้ว. **ยังไม่ได้ตรวจ:** กด Build ใน Unity จริง, หน้าตา shader toon/แสง/bloom จริง, ความเร็ว (FPS), Play กับหุ่น
- Next action: ผู้ใช้ pull branch → Tools ▸ NSC ▸ City ▸ Build City Scene → ลอง Lighting ▸ Day/Sunset/Night แล้วบอกส่วนที่ไม่ชอบ; ก่อน commit ไฟล์ใน Generated/ ดูขนาดก่อน (mesh ~350 ชิ้น)
