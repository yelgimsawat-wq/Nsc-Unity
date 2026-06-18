# SettingsManager - Unity Hierarchy Setup Guide

## 📋 โครงสร้าง Hierarchy ที่แนะนำ

```
Canvas (หรือ Canvas ใดๆ ที่มี Canvas Scaler)
└── SettingsPopup_Panel (GameObject + CanvasGroup)
    ├── Background_Overlay (Image - สีดำโปร่งแสง alpha 0.7)
    ├── Settings_Container (Panel)
    │   ├── Header
    │   │   ├── Title_Text (TextMeshPro - "Settings")
    │   │   └── CloseButton (Button + Image)
    │   │
    │   ├── TabButtons_Group (Horizontal Layout Group)
    │   │   ├── GraphicsTab_Button (Button + TextMeshPro)
    │   │   ├── AudioTab_Button (Button + TextMeshPro)
    │   │   └── GameplayTab_Button (Button + TextMeshPro)
    │   │
    │   └── TabContent_Container
    │       ├── Graphics_SubPanel (GameObject)
    │       │   ├── DisplayMode_Dropdown (TMP_Dropdown)
    │       │   ├── Resolution_Dropdown (TMP_Dropdown)
    │       │   ├── FrameRateLimit_Dropdown (TMP_Dropdown)
    │       │   ├── VSync_Toggle (Toggle + TextMeshPro)
    │       │   ├── GraphicsQuality_Dropdown (TMP_Dropdown)
    │       │   └── AntiAliasing_Dropdown (TMP_Dropdown)
    │       │
    │       ├── Audio_SubPanel (GameObject)
    │       │   ├── MasterVolume_Slider (Slider + Label)
    │       │   ├── MusicVolume_Slider (Slider + Label)
    │       │   ├── SfxVolume_Slider (Slider + Label)
    │       │   ├── UiVolume_Slider (Slider + Label)
    │       │   └── MuteOnFocusLoss_Toggle (Toggle + TextMeshPro)
    │       │
    │       └── Gameplay_SubPanel (GameObject)
    │           ├── PlayerName_InputField (TMP_InputField)
    │           └── ShowNetworkStats_Toggle (Toggle + TextMeshPro)
    │
    └── NetworkStats_Panel (GameObject - ลอยมุมบนซ้าย)
        ├── FPS_Label (TextMeshPro)
        └── Ping_Label (TextMeshPro)
```

---

## 🎨 ขั้นตอนการสร้างใน Unity Editor

### 1. สร้าง Main Popup Panel
1. Right-click ใน Hierarchy → UI → Panel
2. เปลี่ยนชื่อเป็น `SettingsPopup_Panel`
3. Add Component → Canvas Group
4. ปรับ Anchor เป็น **Stretch-Stretch** (เต็มจอ)

### 2. สร้าง Tab Buttons
1. สร้าง Empty GameObject ชื่อ `TabButtons_Group`
2. Add Component → Horizontal Layout Group
3. เพิ่ม 3 Buttons:
   - GraphicsTab_Button
   - AudioTab_Button  
   - GameplayTab_Button

### 3. สร้าง Sub-Panels (แท็บละอัน)
สร้าง 3 Panel GameObject:
- `Graphics_SubPanel`
- `Audio_SubPanel`
- `Gameplay_SubPanel`

**Graphics_SubPanel** ประกอบด้วย:
- 6 Dropdowns: DisplayMode, Resolution, FrameRate, Quality, AntiAliasing
- 1 Toggle: VSync

**Audio_SubPanel** ประกอบด้วย:
- 4 Sliders พร้อม Labels: Master, Music, SFX, UI
- 1 Toggle: Mute on Focus Loss

**Gameplay_SubPanel** ประกอบด้วย:
- 1 InputField: Player Name
- 1 Toggle: Show Network Stats

### 4. สร้าง Network Stats Panel
1. สร้าง Panel ชื่อ `NetworkStats_Panel`
2. ตั้งค่า Anchor: **Top-Left**
3. เพิ่ม 2 TextMeshPro:
   - FPS_Label
   - Ping_Label

---

## 🔌 การผูก Inspector Fields

สร้าง GameObject ว่าง ชื่อ `SettingsManager` และ Add Component → `SettingsManager.cs`

### Main Popup
- `Settings Popup Panel`: ลาก SettingsPopup_Panel
- `Popup Canvas Group`: ลาก CanvasGroup component
- `Close Button`: ลาก CloseButton

### Tab Buttons
- `Graphics Tab Button`: ลาก GraphicsTab_Button
- `Audio Tab Button`: ลาก AudioTab_Button
- `Gameplay Tab Button`: ลาก GameplayTab_Button

### Sub-Panels
- `Graphics Sub Panel`: ลาก Graphics_SubPanel
- `Audio Sub Panel`: ลาก Audio_SubPanel
- `Gameplay Sub Panel`: ลาก Gameplay_SubPanel

### Graphics Settings (จาก Graphics_SubPanel)
- `Display Mode Dropdown`
- `Resolution Dropdown`
- `Frame Rate Limit Dropdown`
- `V Sync Toggle`
- `Graphics Quality Dropdown`
- `Anti Aliasing Dropdown`

### Audio Settings (จาก Audio_SubPanel)
- `Master Volume Slider`
- `Music Volume Slider`
- `Sfx Volume Slider`
- `Ui Volume Slider`
- `Mute On Focus Loss Toggle`
- ผูก Labels ทั้ง 4 ตัว

### Gameplay Settings (จาก Gameplay_SubPanel)
- `Player Name Input Field`
- `Show Network Stats Toggle`

### Network Stats Display
- `Network Stats Panel`: ลาก NetworkStats_Panel
- `Fps Label`
- `Ping Label`

---

## 💻 การเรียกใช้งานจาก Script อื่น

### วิธีที่ 1: เปิด Settings จาก Script ใดๆ
```csharp
using UnityEngine;

public class PauseMenu : MonoBehaviour
{
    public void OnSettingsButtonClicked()
    {
        // เรียก Singleton
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OpenSettings();
        }
    }
}
```

### วิธีที่ 2: เปิด Settings จาก OnlineNetworkUI
เพิ่มปุ่ม Settings ใน OnlineNetworkUI:

```csharp
[Header("--- Settings Button ---")]
[SerializeField] private Button settingsButton;

private void Start()
{
    // ... โค้ดเดิม ...
    
    if (settingsButton != null)
    {
        settingsButton.onClick.AddListener(OpenSettingsPanel);
    }
}

private void OpenSettingsPanel()
{
    if (SettingsManager.Instance != null)
    {
        SettingsManager.Instance.OpenSettings();
    }
}
```

### วิธีที่ 3: เปิดด้วย Keyboard Shortcut
```csharp
private void Update()
{
    // กด ESC เพื่อเปิด Settings
    if (Input.GetKeyDown(KeyCode.Escape))
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OpenSettings();
        }
    }
}
```

---

## ⚙️ Dropdown Options ที่ต้องตั้งค่า

### Display Mode Dropdown
```
Options:
0. Windowed
1. Fullscreen
2. Borderless Fullscreen
```

### Frame Rate Limit Dropdown
```
Options:
0. Uncapped
1. 60 FPS
2. 120 FPS
3. 144 FPS
4. 240 FPS
```

### Graphics Quality Dropdown
```
Options:
0. Low
1. Medium
2. High
3. Ultra
```

### Anti-Aliasing Dropdown
```
Options:
0. Off
1. FXAA
2. SMAA
3. TAA
```

---

## 🎯 Features ที่ใช้งานได้

✅ **Tabbed UI System** - สลับแท็บได้ราบรื่น  
✅ **DOTween Animation** - Fade + Scale pop-up เหมือน OnlineNetworkUI  
✅ **Auto-Save** - บันทึกอัตโนมัติทุกครั้งที่เปลี่ยนค่า  
✅ **FPS Counter** - แสดง FPS แบบ Real-time พร้อมเปลี่ยนสี  
✅ **Ping Display** - แสดงค่า Ping เมื่อเชื่อม Netcode  
✅ **Singleton Pattern** - เรียกใช้ได้จากทุกที่ด้วย `.Instance`  
✅ **Event Cleanup** - ลบ Listeners ใน OnDestroy อย่างรัดกุม  

---

## 🐛 Troubleshooting

**ปัญหา: Dropdown ไม่แสดงตัวเลือก**
- ตรวจสอบว่า Dropdown มี TextMeshPro component
- เปิด Dropdown Inspector → Template ต้องมี Item object

**ปัญหา: Network Stats ไม่แสดงค่า**
- ตรวจสอบว่า NetworkManager.Singleton มีค่า (ต้องเชื่อม Network ก่อน)
- Toggle "Show Network Stats" ต้องเปิดอยู่

**ปัญหา: Animation ไม่เล่น**
- ตรวจสอบว่าติดตั้ง DOTween แล้ว (Package Manager)
- CanvasGroup ต้องผูกกับ SettingsPopup_Panel

---

สร้างโดย: Claude Code (Opus 4.8)  
สไตล์อ้างอิง: OnlineNetworkUI.cs
