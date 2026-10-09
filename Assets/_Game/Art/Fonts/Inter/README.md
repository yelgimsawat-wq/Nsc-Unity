# Inter Font for Unity

ดาวน์โหลดจาก: https://github.com/rsms/inter
Version: 4.1

## 📁 ไฟล์ที่รวมอยู่:

- **Inter-Regular.ttf** - ใช้สำหรับข้อความปกติ
- **Inter-Bold.ttf** - ใช้สำหรับหัวข้อและปุ่ม
- **Inter-Medium.ttf** - ใช้สำหรับข้อความเน้น
- **Inter-SemiBold.ttf** - ใช้สำหรับ Sub-headings
- **Inter-Light.ttf** - ใช้สำหรับข้อความเบาๆ

## 🎨 วิธีใช้ใน Unity:

### 1. สำหรับ TextMeshPro:
1. เปิด Unity Editor
2. ไปที่ `Window → TextMeshPro → Font Asset Creator`
3. Source Font File: เลือก `Inter-Regular.ttf` (หรือ weight อื่นๆ)
4. กด **Generate Font Atlas**
5. บันทึก Font Asset

### 2. นำไปใช้กับ UI:
```csharp
// ใน TextMeshProUGUI component
Font Asset: เลือก Inter Font Asset ที่สร้างไว้
Font Style: Regular, Bold, Medium, etc.
```

### 3. ใช้ใน SettingsManager:
อัปเดต SettingsUIBuilder.cs เพื่อใช้ Inter font อัตโนมัติ

---

## ✨ คุณสมบัติของ Inter:

- ✅ ออกแบบมาสำหรับหน้าจอ UI
- ✅ อ่านง่ายในขนาดเล็ก
- ✅ รองรับตัวอักษรหลากหลาย
- ✅ Open Source (SIL Open Font License)

---

**License:** SIL Open Font License 1.1  
**Website:** https://rsms.me/inter/
