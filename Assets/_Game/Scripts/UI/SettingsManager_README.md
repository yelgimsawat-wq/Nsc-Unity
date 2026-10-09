# SettingsManager - Unity Hierarchy Setup Guide (Design.png)

## 🎨 Design Overview

Based on **Design.png** mockup:
- **Color Scheme:** Black background (#000000) + White borders (#FFFFFF) + Gold accent (#D4AF37)
- **Style:** Minimal dark theme with rounded corners
- **Tab System:** GRAPHICS | AUDIO | GAMEPLAY (gold when active)
- **Bottom Buttons:** "SAVE & CLOSE" (gold) and "CLOSE X" (white)

---

## 📋 โครงสร้าง Hierarchy ที่แนะนำ

```
Canvas
└── SettingsPopup_Panel (Image - Black with white border, rounded)
    ├── Background_Overlay (Image - Dim overlay, optional)
    │
    ├── Header_Section
    │   ├── Title_Text (TextMeshPro - "SETTINGS", white, bold)
    │   └── Separator_Line (Image - horizontal white line)
    │
    ├── TabButtons_Group (Horizontal Layout Group)
    │   ├── GraphicsTab_Button (Button + Image + TextMeshPro)
    │   │   ├── Background (Image - changes color: gold=active, gray=inactive)
    │   │   └── Text (TextMeshPro - "GRAPHICS")
    │   │
    │   ├── AudioTab_Button (Button + Image + TextMeshPro)
    │   │   ├── Background (Image)
    │   │   └── Text (TextMeshPro - "AUDIO")
    │   │
    │   └── GameplayTab_Button (Button + Image + TextMeshPro)
    │       ├── Background (Image)
    │       └── Text (TextMeshPro - "GAMEPLAY")
    │
    ├── TabContent_Container
    │   ├── Graphics_SubPanel (GameObject)
    │   │   ├── DisplayMode_Row
    │   │   │   ├── Label (TextMeshPro - "DISPLAY MODE")
    │   │   │   └── Dropdown (TMP_Dropdown)
    │   │   │
    │   │   ├── Resolution_Row
    │   │   │   ├── Label (TextMeshPro - "RESOLUTION")
    │   │   │   ├── Slider (Slider - gold handle)
    │   │   │   └── ValueLabel (TextMeshPro - "70")
    │   │   │
    │   │   ├── VSync_Row
    │   │   │   ├── Label (TextMeshPro - "V-SYNC")
    │   │   │   └── Toggle (Toggle - gold checkmark)
    │   │   │
    │   │   └── QualityPreset_Row
    │   │       ├── Label (TextMeshPro - "QUALITY PRESET")
    │   │       ├── Slider (Slider - gold handle)
    │   │       └── ValueLabel (TextMeshPro - "70")
    │   │
    │   ├── Audio_SubPanel (GameObject)
    │   │   ├── MasterVolume_Row
    │   │   │   ├── Label (TextMeshPro - "MASTER VOLUME")
    │   │   │   ├── Slider (Slider - gold handle)
    │   │   │   └── ValueLabel (TextMeshPro - "100%")
    │   │   │
    │   │   ├── MusicVolume_Row
    │   │   ├── SfxVolume_Row
    │   │   └── ...
    │   │
    │   └── Gameplay_SubPanel (GameObject)
    │       ├── PlayerName_InputField (TMP_InputField)
    │       └── ShowNetworkStats_Toggle (Toggle)
    │
    └── BottomButtons_Group (Horizontal Layout Group)
        ├── SaveAndClose_Button (Button - Gold background)
        │   ├── Background (Image - Gold #D4AF37)
        │   └── Text (TextMeshPro - "SAVE & CLOSE", black)
        │
        └── Close_Button (Button - White/Gray background)
            ├── Background (Image - White/Gray)
            └── Text (TextMeshPro - "CLOSE ✕", white)
```

---

## 🎨 ขั้นตอนการสร้างใน Unity Editor

### 1. สร้าง Main Panel (ตาม SettingPanel.png)
1. Right-click ใน Hierarchy → UI → Image
2. เปลี่ยนชื่อเป็น `SettingsPopup_Panel`
3. Add Component → Canvas Group
4. ตั้งค่า Image:
   - Color: Black (R:0, G:0, B:0, A:255)
   - Sprite: ใช้ ScollAndButton.png (ปุ่มแบบยาว) หรือสร้าง sprite rounded rectangle
   - Image Type: Sliced (สำหรับ rounded corners)

### 2. สร้าง Header
1. เพิ่ม TextMeshPro - Text ชื่อ "Title_Text"
2. ตั้งค่า:
   - Text: "SETTINGS"
   - Font: Bold
   - Color: White
   - Alignment: Center
   - Font Size: 24-32

### 3. สร้าง Tab Buttons (3 ปุ่ม)

**สำหรับแต่ละปุ่ม:**
1. Right-click → UI → Button - TextMeshPro
2. เปลี่ยนชื่อเป็น `GraphicsTab_Button`, `AudioTab_Button`, `GameplayTab_Button`
3. ตั้งค่า Button Image:
   - Source Image: ScollAndButton.png (ตัวบน - สั้นกว่า)
   - Color: **Dark Gray (inactive)** หรือ **Gold (active)**
4. ตั้งค่า Text:
   - Text: "GRAPHICS" / "AUDIO" / "GAMEPLAY"
   - Font: Bold
   - Color: White (active) / Light Gray (inactive)

### 4. สร้าง Content Sub-Panels

**Graphics SubPanel:**
- DISPLAY MODE: TMP_Dropdown
  - Options: Windowed, Fullscreen, Borderless Fullscreen
- RESOLUTION: Slider (horizontal) + Value Label
  - Slider Handle: ใช้สี Gold (#D4AF37)
  - Fill Area: Gold
- V-SYNC: Toggle
  - Checkmark: Gold
- QUALITY PRESET: Slider + Value Label
  - ตั้งค่าเหมือน Resolution slider

**สไตล์ Slider:**
- Background: Dark gray rounded bar
- Fill: Gold (#D4AF37)
- Handle: Gold circle

**สไตล์ Toggle:**
- Background: Black with white border
- Checkmark: Gold when ON

### 5. สร้าง Bottom Buttons

**SAVE & CLOSE Button:**
1. Button → Background Image Color: **Gold (#D4AF37)**
2. Text: "SAVE & CLOSE" (สีดำ)
3. Font: Bold

**CLOSE X Button:**
1. Button → Background Image Color: White/Gray
2. Text: "CLOSE ✕" (สีขาว)

---

## 🔌 การผูก Inspector Fields

### Main Popup
- `Settings Popup Panel`: ลาก SettingsPopup_Panel
- `Popup Canvas Group`: ลาก CanvasGroup component
- `Save And Close Button`: ลาก SAVE & CLOSE button
- `Close Button`: ลาก CLOSE X button

### Tab Buttons + Visuals
- `Graphics Tab Button`: ลาก GraphicsTab_Button
- `Audio Tab Button`: ลาก AudioTab_Button
- `Gameplay Tab Button`: ลาก GameplayTab_Button
- `Graphics Tab Image`: ลาก Background Image ของ GraphicsTab
- `Audio Tab Image`: ลาก Background Image ของ AudioTab
- `Gameplay Tab Image`: ลาก Background Image ของ GameplayTab
- `Graphics Tab Text`: ลาก TextMeshPro ของ GraphicsTab
- `Audio Tab Text`: ลาก TextMeshPro ของ AudioTab
- `Gameplay Tab Text`: ลาก TextMeshPro ของ GameplayTab

### Graphics Settings
- `Display Mode Dropdown`: TMP_Dropdown
- `Resolution Slider`: Slider component
- `Resolution Value Label`: TextMeshPro (แสดงค่า)
- `V Sync Toggle`: Toggle
- `Quality Preset Slider`: Slider
- `Quality Preset Value Label`: TextMeshPro

### Audio Settings
- `Master Volume Slider`: Slider
- `Music Volume Slider`: Slider
- `Sfx Volume Slider`: Slider
- `Master Volume Label`: TextMeshPro (แสดง %)
- `Music Volume Label`: TextMeshPro
- `Sfx Volume Label`: TextMeshPro

### Gameplay Settings
- `Player Name Input Field`: TMP_InputField
- `Show Network Stats Toggle`: Toggle
- `Mouse Sensitivity Slider` / `Mouse Sensitivity Value Label`: **ไม่ต้องสร้างเอง** —
  สั่ง `Tools ▸ NSC ▸ UI ▸ Add Mouse Sensitivity Row` มันจะก๊อปหน้าตาจากแถว MASTER VOLUME
  มาวางในแท็บ Gameplay แล้วต่อสายให้เสร็จ (สั่งซ้ำได้ ของเดิมจะถูกแทนที่)

### Network Stats Display
- `Network Stats Panel`: GameObject (ลอยมุมจอ)
- `Fps Label`: TextMeshPro
- `Ping Label`: TextMeshPro

### Theme Colors (ปรับตามต้องการ)
- `Active Tab Color`: Gold (#D4AF37) - RGB(212, 175, 55)
- `Inactive Tab Color`: Dark Gray - RGB(77, 77, 77)
- `Text Active Color`: White
- `Text Inactive Color`: Light Gray - RGB(179, 179, 179)

---

## 💻 การเรียกใช้งาน

### เปิด Settings
```csharp
SettingsManager.Instance.OpenSettings();
```

### ปิด Settings
```csharp
SettingsManager.Instance.CloseSettings();
```

### บันทึกและปิด
```csharp
SettingsManager.Instance.SaveAndClose();
```

### เปิดจากในเกม (ระหว่างแมตช์)
กด **ESC** ระหว่างเล่น → เมนูระหว่างเล่น (`InMatchMenu`) → ปุ่ม **SETTINGS**

แผงตั้งค่าเป็นตัวเดียวกับในเมนูหลัก ไม่ได้สร้างซ้ำ — ตอน `Awake()` มันจะสร้าง Canvas ของตัวเอง
(`[SettingsUI]`, sortingOrder 100) แล้วย้ายตัวเองไปอยู่ใต้ Canvas นั้นพร้อม `DontDestroyOnLoad`

> ⚠️ ห้ามเปลี่ยนกลับไปเรียก `DontDestroyOnLoad(gameObject)` ตรงๆ — แผงนี้เป็น **ลูก** ของ Canvas ในฉากเมนู
> Unity ย้ายข้ามฉากให้เฉพาะ object ที่เป็น root เท่านั้น เรียกกับลูกจะแค่ขึ้น warning แล้วไม่ทำอะไร
> ผลคือพอเข้าเกมแล้วแผงหายทั้งอัน (เป็นบั๊กเดิมก่อนแก้)

### ความไวเมาส์ (Mouse Sensitivity)
ค่าจริงเก็บที่ `MouseSettings` (static + PlayerPrefs) ไม่ได้เก็บใน `SettingsManager`
เพราะสคริปต์มือ/เท้า/กล้อง ต้องอ่านค่าได้แม้แผงตั้งค่ายังไม่เกิด (เช่นกด Play จากฉากเกมตรงๆ)

```csharp
// ฝั่งที่รับอินพุตเมาส์
delta * (mouseSensitivity * MouseSettings.Multiplier * 0.1f);
```

ตอนนี้ผูกไว้แล้วที่ `PlayerHandMovement`, `PlayerFootForRobot`, `PlayerCam` —
ค่าใน Inspector ของแต่ละตัวยังเป็น "ความไวพื้นฐาน" เหมือนเดิม สไลเดอร์เป็นแค่ตัวคูณทับ (0.2x - 3.0x)

---

## 🎨 Sprites ที่ต้องใช้

จากโฟลเดอร์ `Assets/MenuUI/Setting/`:
1. **SettingPanel.png** - ใช้เป็น Panel background (ขอบโค้ง)
2. **ScollAndButton.png** - มี 2 shapes:
   - **ปุ่มสั้น** (บน) - ใช้สำหรับ Tab Buttons
   - **แถบยาว** (ล่าง) - ใช้สำหรับ Slider background
3. **Design.png** - Reference สำหรับ layout ทั้งหมด

### Import Settings สำหรับ Sprites:
1. เลือก sprite ใน Project
2. Inspector → Texture Type: **Sprite (2D and UI)**
3. Sprite Mode: **Single** หรือ **Multiple** (ถ้าต้องการแยก)
4. Pixels Per Unit: 100
5. Mesh Type: **Tight** (สำหรับ rounded corners)
6. Click **Apply**

---

## ⚙️ Slider Configuration

### Resolution Slider & Quality Preset Slider:
```
Min Value: 0
Max Value: 100 (หรือ availableResolutions.Length - 1 สำหรับ Resolution)
Whole Numbers: Yes
Direction: Left to Right
```

**Handle Sprite:** วงกลมสี Gold  
**Fill Area:** สี Gold (#D4AF37)  
**Background:** Dark gray bar

---

## 🎯 Features ที่ใช้งานได้

✅ **Tab System** - เปลี่ยน tab ด้วยสี Gold (active) และ Gray (inactive)  
✅ **Visual Feedback** - Tab ที่เลือกจะเปลี่ยนสีทันที  
✅ **DOTween Animation** - Fade + Scale pop-up  
✅ **Auto-Save** - ทุกค่าบันทึกอัตโนมัติเมื่อเปลี่ยน  
✅ **SAVE & CLOSE** - บันทึกทุกอย่างแล้วปิด  
✅ **Sliders with Values** - แสดงค่าแบบ realtime (70, 100%, etc.)  
✅ **Gold Accent Theme** - ตรงตาม Design.png  

---

## 🐛 Troubleshooting

**ปัญหา: Tab ไม่เปลี่ยนสี**
- ตรวจสอบว่าผูก `Tab Image` และ `Tab Text` ครบ
- ตรวจสอบค่า `Active Tab Color` และ `Inactive Tab Color`

**ปัญหา: Slider ไม่แสดงค่า**
- ตรวจสอบว่าผูก `Value Label` แล้ว
- ตรวจสอบว่า Slider มี `onValueChanged` listener

**ปัญหา: Sprites ไม่โค้งมุม**
- ตั้ง Image Type เป็น **Sliced**
- ตรวจสอบว่า sprite มี 9-slice borders

**ปัญหา: ปุ่มไม่ทำงาน**
- ตรวจสอบว่า Button มี `onClick` event
- ตรวจสอบว่า SettingsManager อยู่ใน Scene

---

## 📸 Design Reference

**จาก Design.png:**
- Header: "SETTINGS" กลางบน
- Tab: GRAPHICS (Gold) | AUDIO | GAMEPLAY
- Content: DISPLAY MODE, RESOLUTION: 70, V-SYNC (ON), QUALITY PRESET: 70
- Bottom: SAVE & CLOSE (Gold) | CLOSE X (White)

**Color Palette:**
- Background: #000000 (Black)
- Border: #FFFFFF (White)
- Active/Accent: #D4AF37 (Gold)
- Text: #FFFFFF (White)
- Inactive: #4D4D4D (Dark Gray)

---

สร้างโดย: Claude Code (Opus 4.8)  
ออกแบบตาม: Design.png mockup  
สไตล์อ้างอิง: OnlineNetworkUI.cs
