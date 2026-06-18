# Nsc-Unity Project

โปรเจกต์ Unity นี้ได้ตั้งค่าการเชื่อมต่อ MCP (Model Context Protocol) เพื่อทำงานร่วมกับ Claude Code อย่างราบรื่น

## MCP Servers ที่ติดตั้ง

### 1. 🗂️ **Filesystem Server**
- เข้าถึงไฟล์และโฟลเดอร์ในโปรเจกต์
- อ่าน/เขียนไฟล์ได้โดยตรง
- เหมาะสำหรับการจัดการ Assets, Scripts, Configs

### 2. 🐙 **GitHub Server**  
- ดึงข้อมูล Issues, Pull Requests
- ตรวจสอบประวัติ commits
- จัดการ branches และ tags
- **ต้องตั้งค่า:** `GITHUB_TOKEN` environment variable

### 3. 🧠 **Memory Server**
- จำบริบทของโปรเจกต์
- เก็บข้อมูลที่สำคัญระหว่าง sessions
- ช่วยให้ Claude จำรายละเอียดโปรเจกต์ได้

## การใช้งาน

เมื่อเปิด Claude Code ใน session นี้ คุณสามารถ:

```
"ช่วยดูไฟล์ในโฟลเดอร์ Assets/Scripts"
"เช็ค GitHub issues ที่เปิดอยู่"
"จำไว้ว่าโปรเจกต์นี้ใช้ Unity 2022.x"
```

## การตั้งค่า GitHub Token

1. ไปที่: https://github.com/settings/tokens
2. สร้าง Personal Access Token (classic) 
3. เลือก scopes: `repo`, `read:org`
4. เพิ่ม token ใน environment variables:
   ```powershell
   $env:GITHUB_TOKEN = "ghp_your_token_here"
   ```

## โครงสร้างโปรเจกต์

```
Nsc-Unity/
├── Assets/              # Unity assets และ scripts
├── ProjectSettings/     # การตั้งค่า Unity
├── Packages/           # Unity packages
├── .mcp.json          # MCP server configuration
└── .claude/           # Claude Code settings
    └── settings.json
```

---

**เคล็ดลับ:** ใช้ `/mcp` command ใน Claude Code เพื่อดู MCP servers ที่เชื่อมต่ออยู่
