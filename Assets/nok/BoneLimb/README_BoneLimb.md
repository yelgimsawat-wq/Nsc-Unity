# Bone Limb (ระบบแขนขาด้วยกระดูก)

ใช้กระดูก (IK) ขยับแขน/ขาไปหาเป้า แทนการเอาชิ้นฟิสิกส์มาต่อกันด้วย Joint
โค้ดเก่าไม่ถูกแก้เลย: `LobbyManager`, `EZMovement`, `EZFootMovement`, `Following` ใช้ได้เหมือนเดิม

## ไฟล์
- `BoneLimbIK.cs`: ตัวคำนวณ IK (FABRIK) ใช้กับกระดูกกี่ท่อนก็ได้ มี Pole กันเข่า/ศอกพับผิดด้าน
- `Editor/BoneLimbSetupWindow.cs`: หน้าต่างเซ็ทอัพ เปิดที่เมนู **Tools > Bone Limb > Setup Wizard**

## เซ็ทอัพกับเกมจริง (ผูกกับ LobbyManager)
1. ลากโมเดลที่มีกระดูก (เช่น `robot/leg/leggggggggggggggg.fbx`, `arm/Model/แขน111.prefab`)
   ไปเป็น **ลูกของ Torso** ใน `RobotContainer` วางตรงตำแหน่งสะโพก/ไหล่
2. ใน Hierarchy เลือกกระดูกโคน (เช่น `Armature/Bone`)
3. เปิด **Tools > Bone Limb > Setup Wizard**
   - Mode = `LinkToLobby`
   - Limb Slot = แขน/ขาข้างที่ต้องการ (เช่น `LeftLeg`)
   - กด **Setup**
4. Wizard จะทำให้อัตโนมัติ:
   - ใส่ `BoneLimbIK` ที่กระดูกโคน + สร้าง Pole
   - ใส่ `Rigidbody` (Kinematic) + `SphereCollider` + `Following` ที่ปลายกระดูก
   - เปลี่ยนช่องใน `LobbyManager` ให้ชี้ไปที่ `Following` ตัวใหม่
5. ปิด (SetActive false) แขน/ขาฟิสิกส์เดิม เช่น `UpperLeftLeg / LowerLeftLeg / LeftFoot`
6. ตั้ง `Range` ใน `EZMovement` / `EZFootMovement` ของ Player ให้ใกล้เคียงความยาวแขน/ขา (ดูค่าที่ Console)

## ทดสอบเร็วๆ (ไม่ต้องเปิดเน็ตเวิร์ค)
เลือก Mode = `StandaloneTest` แล้วกด Setup → กด Play → ลาก `*_IKTarget` ในหน้า Scene

## การตั้งค่าที่ควรรู้ (ใน BoneLimbIK)
| ช่อง | ใช้ทำอะไร |
|---|---|
| Weight | 0 = ปิด IK, 1 = IK เต็มที่ (ใช้ผสมกับอนิเมชั่นต่อยได้) |
| Follow Sharpness | ความไวในการตามเป้า (ค่าน้อย = หน่วงเหมือนหุ่นหนัก, 0 = ตามทันที) |
| Pole | ขยับเพื่อเปลี่ยนทิศเข่า/ศอก |
| Reset Pose Each Frame | ปิดไว้ถ้ามี Animator เล่นท่ากับกระดูกชุดนี้ |
| Keep Chain Kinematic | กัน `LobbyManager` ปลด Kinematic ตอนเริ่มเกมแล้วแขนร่วง |
| Ignore Collision With Body | ไม่ให้แขน/ขาชนตัวหุ่นเอง |

## หมายเหตุเน็ตเวิร์ค
IK รันบนทุกเครื่องเอง ไม่ต้อง sync กระดูก เพราะตำแหน่งเป้า (ตัว Player) ถูก sync ด้วย
`ClientNetworkTransform` อยู่แล้ว ตัวหุ่น (Torso) ยังเป็นฟิสิกส์เหมือนเดิม
แรงดันจาก `EZMovement` / `EZFootMovement` จึงยังผลักตัวหุ่นได้ตามปกติ
