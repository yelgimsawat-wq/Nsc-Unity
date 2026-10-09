# ชุดเอกสารรีวิวสถาปัตยกรรม Nsc-Unity (ฉบับข้อความ)

Nsc-Unity เป็นเกมหุ่นยนต์ต่อสู้แบบ multiplayer บน Unity 6 และ Netcode for GameObjects ที่ผู้เล่น 4 คนคุมแขนขาของหุ่นคนละชิ้น ไฟล์นี้รวมเอกสาร 3 ส่วนไว้เป็นข้อความล้วน:

1. รีวิวสถาปัตยกรรม (หัวข้อ A-O) พร้อมแผนภาพ 15 ชุดเป็นโค้ด Mermaid
2. คำตอบข้อวิจารณ์ 40 ข้อต่อแผนภาพ Robot aggregate (แผนภาพ 3)
3. โค้ดทุกบรรทัดที่สองส่วนแรกอ้างเป็นหลักฐาน

คำว่า "แผนภาพ N" ในเอกสารหมายถึงแผนภาพลำดับที่ N ในส่วนที่ 1 แผนภาพ 1 คือโค้ดปัจจุบัน แผนภาพ 2 ถึง 14 คือข้อเสนอที่ยังไม่มีในโค้ด และแผนภาพ 15 คือพฤติกรรมที่ผู้เล่นเห็นซึ่งต้องเหมือนเดิมก่อนและหลังรีแฟกเตอร์ บรรทัด `%% สถานะ:` ในโค้ด Mermaid ทุกชุดบอกสถานะนี้ และ annotation ของคลาสบอกที่มา เช่น `<<NetworkBehaviour · เดิม TorsoMovement>>` คือมาจาก `TorsoMovement` ในโค้ดตอนนี้ ส่วน `ใหม่` คือยังไม่มีเลย

---

# ส่วนที่ 1: รีวิวสถาปัตยกรรม Nsc-Unity

ฉบับข้อความของ `ARCHITECTURE_REVIEW.th.md` แผนภาพ 15 ชุดเขียนเป็นโค้ด Mermaid แทนรูป

---

# รีวิวสถาปัตยกรรม Nsc-Unity

Branch `yelmee` ตามไฟล์ในเครื่อง ณ วันที่ 27 ก.ย. 2026 (รวมการแก้ที่ยังไม่ commit ในสคริปต์ไอเทมและ `ParkourFlowManager`)

**ขอบเขต** อ่านสคริปต์ runtime ของโปรเจกต์ครบทุกไฟล์ ประมาณ 24,000 บรรทัดใน ~110 ไฟล์ ภายใต้ `Assets/Scenes/TheBestFolder/Mynigga`, `Assets/nok`, `Assets/Yelmee`, `Assets/petong` และ `Assets/Scenes/Enemy AndInteractive Object` ส่วนสคริปต์ editor 19 ไฟล์ใน `Assets/Editor` (~5,000 บรรทัด), `OutDated/`, `Learn/` และ `_ArmFeelTest/` อ่านแบบไล่ดูโครง ไม่รวมโค้ด third-party (DOTween, JMO, Hierarchy Designer, Toon Shaders, UnityMCP, แพ็กฉากเมือง) และตรวจการใช้งานสคริปต์ด้วย GUID เทียบกับ 8 ซีนที่เปิดใน Build Settings และ prefab ทุกตัว คำว่า "ไม่ได้ใช้" ในเอกสารนี้จึงหมายถึง "ไม่ได้ติดอยู่ที่ไหนเลย" ไม่ใช่การเดา

**Severity** (ความเสี่ยงต่อบั๊กหรือต้นทุนการแก้): Critical, High, Medium, Low
**Priority** (ควรทำอะไรก่อน): Critical, Important, Improvement, Optional
จุดที่โค้ดตอบไม่ได้ จะเขียนว่า **ไม่สามารถสรุปได้จากโค้ดที่มี**

---

## A. ภาพรวมของโค้ดเบส

### เกมนี้คืออะไร (ดูจากโค้ด)

เกมหุ่นยนต์ต่อสู้ที่ขับด้วยฟิสิกส์ หุ่นหนึ่งตัวคือลำตัวกับแขนขาอีก 4 ชิ้น (แขนซ้าย/ขวา ขาซ้าย/ขวา) แขนขาแต่ละชิ้นเป็น `NetworkObject` ของตัวเอง และผู้เล่นคนละคนคุมคนละชิ้นได้ ฟิสิกส์ทั้งหมดรันบน server: client ที่เป็นเจ้าของชิ้นนั้นอ่านเมาส์แล้วส่งจุดเล็งกับสถานะปุ่มผ่าน RPC ส่วน server ขยับ rigidbody ที่ขับด้วยสปริง มีโหมด Boss (PvE, `MAPBOSS`), Parkour (`MAPPAKUAR`), PvP (หุ่นสองตัว แดง vs น้ำเงิน สูงสุด 8 คน) และเวอร์ชันที่มี Timeline intro ของ Boss กับ Parkour

### ระบบที่มีอยู่ตอนนี้

| ระบบ | คลาสหลัก (จำนวนบรรทัด) | ทำงานอย่างไร |
|---|---|---|
| การทรงตัวและการเคลื่อนที่ของหุ่น | `TorsoMovement` (517), `PlayerFootForRobot` (1128), `PlayerHandMovement` (995), `PlayerCam` (142) | ลำตัวลงทะเบียนเท้าและมือ อ่านสถานะของพวกมันทุก `FixedUpdate` แล้วตัดสินว่า `Standing / Falling / Ragdoll` แขนขาเป็นสปริงฝั่ง server ที่วิ่งเข้าหาเป้าที่เจ้าของส่งมา |
| การต่อสู้ของแขนขา | `PlayerHandCombat : PlayerHandMovement` (521), `PlayerLegCombat` (658), `PhysicsDamageSender` (258), `PvpDamageSender` (283) | ต่อย = อัดความเร่งขณะกดคลิกซ้ายค้าง เตะ = ชาร์จด้วย Shift เมื่อแขนขาชนอะไร จะแปลงความเร็วพีคเป็นดาเมจ |
| เลือดและการหลุดของแขนขา | `RobotHealth` (228), `JointPullAndReconnect` (445) | เลือดแยกต่อชิ้น เลือดหมด joint ถูกทำลายและเพดานเลือดลด กด R ค้างเพื่อดึงชิ้นกลับแล้วสร้าง joint ใหม่ |
| บอส (`NscGame.Enemy`) | `EnemyController` (653), `EnemyCombat` (650), `EnemyHealth` (376), `EnemyUltimate` (641), `BlackHoleProjectile`, `EnemyRagdoll`, `EnemyBodySway`, `EnemyHealthUI`, `EnemyStateData` | ลูปตัดสินใจเป็น coroutine บน server ใช้ NetworkVariable ขับ Animator ฝั่ง client `IHittable` กับ `AttackType` ถูกประกาศไว้ในโมดูลนี้ |
| ไอเทม (`NscUnity.Items`) | `ItemDefinition`/`ItemDatabase` (ScriptableObject), `PlayerInventory`, `HandItemHolder`, `HeldItem` + `ChargeGunHeldItem`/`GunHeldItem`/`SwordHeldItem`, `Projectile`, `WorldItem`, `ItemPickupInteractor`, `LocalItemOwner`, `InputCompat`, `ItemWheelUI`, `PickupPromptUI` | กระเป๋าที่ server เป็นเจ้าของ sync เป็นเลข index ในฐานข้อมูลไอเทม |
| session และลำดับของแมตช์ | `OnlineNetworkUI` (1430), `ReturnToMenuOnHostLost` (328), `LobbyManager` (1533), `PvpTeamManager` (608), `PvpRobotTeam` (321), `GameFlowManager` (319), `ParkourFlowManager` (208), `RespawnManager`, `CheckpointZone`, `FallDeathZone`, `TutorialManager`, `TimelineSkipController` | เมนูสร้างหรือเข้าห้องผ่าน Unity Gaming Services, host โหลดแมพ, ลอบบี้ในฉากแจกแขนขา, ตัวจัดการโหมดตัดสินแพ้ชนะ |
| HUD และเมนู | `LocalRobotBinder` + `HullRingUI`, `LimbStatusUI`, `PunchForceUI`, `KickChargeUI`, `StatusPromptUI`; `HudVisibilityGate`, `PvpPlayerHudGate`, `PvpHudRobotBinder`, `PvpTeamSelectUI` (760), `PvpResultUI`, `InMatchMenu` (767), `SettingsManager` (1113) | widget ของ HUD ผูกกับ "หุ่นของเรา" ผ่าน `LocalRobotBinder` |
| service ระดับแอป | `VoiceChat`, `GraphicsQualityManager` (สร้างตัวเองและ `DontDestroyOnLoad` ทั้งคู่), `GameAudio`/`AudioCategory`, `MouseSettings`, `MouseWheelFocus`, `UiFocus` | helper แบบ static หรือ singleton ที่อยู่ข้ามฉาก |
| เครื่องมือใน Editor | `PlayerHudBuilder`, `SettingsUIBuilder`, `PvpUIBuilder`, `InMatchMenuBuilder`, `GameFlowPanelBuilder`, ตัว validate ต่างๆ, `ItemAuthorityTests` | UI ส่วนใหญ่ถูกสร้างจากโค้ดใน editor |

### สิ่งที่ทำได้ดีแล้วและควรเก็บไว้

- **ยึด server authority อย่างสม่ำเสมอ** client ส่งแค่เจตนา server เป็นคน clamp และตรวจค่า (`ValidateAndSetFootTarget`, `ValidateAndSetHandTarget`, การจำกัดรัศมีใน `TorsoMovement.ApplyRecoveryForceRpc`, `Vector3Extensions.IsValid`)
- **`PlayerInventory` คือแบบที่ควรลอกไปใช้ทั้งโปรเจกต์** มีแหล่งความจริงเดียว (`NetworkList` + `NetworkVariable`) แก้ได้เฉพาะ server มี API `Request*` ให้ client มีอีเวนต์ให้ UI และใส่ `RpcInvokePermission.Owner` บน RPC
- **ใช้ ScriptableObject ถูกที่** `ItemDefinition` กับ `ItemDatabase` เก็บค่าตั้งและให้ไอเทมมี index สำหรับเครือข่ายที่คงที่
- **widget ของ HUD ใช้การฟังอีเวนต์ ไม่ poll กันเอง** เช่น `LocalRobotBinder.OnBound`, `NetworkVariable.OnValueChanged`, `JointPullAndReconnect.OnConnectionStateChanged`
- **`UiFocus`** เป็นคำตอบที่เล็กและชัดสำหรับคำถาม "ตอนนี้มี UI ครองเมาส์อยู่ไหม"
- **โมดูลบอส** มี namespace ของตัวเอง ความเป็นเจ้าของในตัว prefab ไหลทางเดียว และแคช hash ของ Animator
- คอมเมนต์อธิบาย *เหตุผล* ของการแก้แต่ละจุดไว้ ซึ่งทำให้รีวิวนี้ทำได้

### ปัญหาในประโยคเดียว

โค้ดแต่ละจุดเขียนอย่างตั้งใจ แต่โปรเจกต์ไม่มี "แกนกลาง" ที่ทุกระบบใช้ร่วมกัน (ไม่มี `Robot` ที่เป็นเจ้าของแขนขา ไม่มีทางเดียวในการทำดาเมจ ไม่มีเจ้าของเฟสของแมตช์เพียงคนเดียว) ทุกฟีเจอร์ใหม่จึงต้องหาความหมายเหล่านี้ใหม่เอง ด้วยการค้นทั้งฉาก เทียบชื่อ GameObject และอ่านค่าจาก singleton ของ UI

---

## B. ปัญหาเชิงสถาปัตยกรรม

| # | ไฟล์ / คลาส | ปัญหา | Severity | หลักฐาน / เหตุผล |
|---|---|---|---|---|
| 1 | จุดที่ทำดาเมจ (7 ที่) | ไม่มีเส้นทางดาเมจเส้นเดียว ผู้โจมตีทุกตัวเขียนการหาเป้า กติกาทีม และสูตรดาเมจเอง | Critical | `PhysicsDamageSender.OnCollisionEnter` (EnemyHealth แล้วค่อย RobotHealth), `PvpDamageSender.OnCollisionEnter` (PvpRobotTeam แล้วค่อย RobotHealth หรือลำตัว), `explobuilding.OnCollisionEnter` (อ่านค่าจูนของ `PhysicsDamageSender` และ `PlayerHandCombat` เอง), `GunHeldItem.ApplyDamage` (EnemyHealth, เช็ค PvP, IHittable, ลำตัว), `Projectile.ApplyDamage` (PvP, EnemyHealth, IHittable), `BlackHoleProjectile.DamageAlong` (`hittable is explobuilding`, `hittable is RobotHealth`), `EnemyCombat.ProcessHitDetection` (`p is RobotHealth rh`) โดย `EnemyHealth` ไม่ได้ implement `IHittable` แต่มี `ServerTakeHit` ของตัวเอง สูตรหมัด `min(peak * speedToDamage, maxDamagePerHit)` ถูกเขียนซ้ำ 3 ไฟล์ |
| 2 | แขนขาของหุ่นใน `PVP.unity` | มีคอมโพเนนต์ดาเมจสองตัวบน rigidbody เดียวกันที่กติกาต่างกัน ตัวไหนได้ทำดาเมจขึ้นกับลำดับคอมโพเนนต์ | Critical (ต้องยืนยันใน Play Mode) | prefab ฐาน `Yelmee/Robotc DDD/RobotContainer.prefab` ติด `PhysicsDamageSender` บนแขนขาทั้ง 4 ส่วน `PVP.unity` เพิ่ม `PvpDamageSender` ให้หุ่นทั้งสองตัวโดยไม่ลบของเดิม `PhysicsDamageSender` ไม่เช็คเฟสของแมตช์ และการเช็คทีม `RobotTeam.AreEnemies` คืนค่า true เสมอเพราะ `RobotTeam` ไม่ได้ติดอยู่ที่ prefab หรือซีนไหนเลย ตัวส่งที่ทำงานก่อนจะเรียก `NotifyPunchImpact()` ซึ่งทำให้ `CanDealDamage` ของอีกตัวเป็น false กติกา PvP (`IsFighting`, `friendlyFire`, `limbDamageMultiplier`) จึงอาจถูกข้าม |
| 3 | `LobbyManager` | God class และโค้ดเกมเพลย์ต้องพึ่งมัน | Critical | 1533 บรรทัด ราวสิบหน้าที่ (ดูข้อ C) โค้ดเกมเพลย์อ่านค่าจากมัน: `PlayerHandCombat` และ `PlayerLegCombat` (`CanReadCombatInputForThisLimb` ผ่าน `Instance` หรือ `FindFirstObjectByType`), `HandItemHolder.CanUseWeapon`, `GameFlowManager` และ `ParkourFlowManager` (เริ่มจับเวลา), `EnemyHealthUI`, `HudVisibilityGate`, `LocalRobotBinder` |
| 4 | โครงสร้างหุ่น (≥ 9 ที่) | ไม่มี aggregate `Robot` หาแขนขาด้วยชื่อ GameObject และการค้น hierarchy และ index ของแขนขาถูกนิยาม 4 แบบ | Critical | เทียบชื่อ: `LobbyManager.ResolveActiveRobot` (`"_R"` ไปลงช่องแขน**ซ้าย** กลับด้านเพื่อ UI), `PvpTeamManager.AssignByName` (`"_L"` ไปลงช่องแขนซ้าย ไม่กลับด้าน), `LocalRobotBinder.IsLeftName/IsRightName` ค้น hierarchy: `PvpRobotTeam.ResolveRobotRoot`, `RobotTeam.Find`, `LocalRobotBinder.ResolveRobotRoot`, `RobotBodyCheck`, `EnemyController.FindRobotTarget`, `GameFlowManager.CheckDefeatCondition` (เท้าและมือทุกตัวในฉาก), `RespawnManager.limbRigidbodies` index ของแขนขา: ค่าคงที่ใน `PvpLimb`, `LobbyManager.GetLimbByIndex`, `LimbStatusUI.LimbSlot`, `TutorialManager.LimbRole` |
| 5 | เฟสของแมตช์ | คำถาม "ผู้เล่นทำอะไรได้หรือทำดาเมจได้หรือยัง" ถูกตอบด้วยแฟล็ก 4 ตัวที่ไม่เกี่ยวกัน | High | `GameFlowManager.GameEnded` แบบ static ถูกเขียนโดย `GameFlowManager`, `ParkourFlowManager` และ `PvpResultUI` และถูกอ่านโดยมือ เท้า การเตะ และ `HandItemHolder` นอกจากนี้ยังมี `LobbyManager.GameStarted`, `PvpTeamManager.IsFighting`, `ParkourFlowManager.netEnded` โดย `HandItemHolder.CanUseWeapon` เช็ค singleton สามตัว และ `PvpPlayerHudGate` ต้องปิด `HudVisibilityGate` ทิ้งเพื่อไม่ให้สองตัวแย่งคุม `CanvasGroup` อันเดียวกัน |
| 6 | การหลุดของแขนขา | มีแหล่งความจริงสองแห่งสำหรับ "ชิ้นนี้หลุดอยู่ไหม" | High | `JointPullAndReconnect.netIsConnected` กับ `PlayerFootForRobot.currentState` / `PlayerHandMovement.currentState` (`Attached/Detached`) โดย joint เขียนทั้งสองที่ `GameFlowManager` นับการแพ้จากสถานะมือ/เท้า ส่วน `PvpRobotTeam` นับจาก `joint.IsConnected` |
| 7 | `PlayerFootForRobot`, `PlayerHandMovement` | input ของเจ้าของ การล็อกเคอร์เซอร์ โมเดลการเล็ง crosshair แบบ IMGUI โปรโตคอล RPC ฟิสิกส์ฝั่ง server และการรีเซ็ตตอน respawn อยู่ในคลาสเดียว และการเล็งเป็น state แบบ static ที่ใช้ร่วมกัน | High | 1128 และ 995 บรรทัด `HandleCursorLock` กับ `HandleFootCursorLock` แทบเหมือนกัน `protected static sharedVirtualCursor` / `sharedAimOffsetWorld` ถูกอ่านโดย `GunHeldItem` และเท้าผ่าน `PlayerHandMovement.AimNormalized` |
| 8 | เคอร์เซอร์และปุ่ม ESC | มี 10 คลาสที่เขียน `Cursor.lockState` และ 4 คลาสที่จัดการ ESC | High | ผู้เขียน: `PlayerHandMovement`, `PlayerFootForRobot`, `PlayerCam`, `GameFlowManager` และ `ParkourFlowManager` (ทุกเฟรมหลังจบเกม), `PvpResultUI`, `InMatchMenu`, `ItemWheelUI`, `SettingsManager`, `ReturnToMenuOnHostLost.LeaveMatchAsync` ส่วน ESC: มือ, เท้า, `InMatchMenu`, `ItemWheelUI` |
| 9 | `LobbyManager` กับ `PvpTeamManager` | มีโค้ดสองชุดสำหรับเรื่องเดียวกัน คือ "ผู้เล่นจองแขนขา host กดเริ่ม ผูกกล้องเข้ากับชิ้นที่จอง" | High | `LobbyPlayer` กับ `PvpPlayerEntry`, `SubmitPlayerNameServerRpc` กับ `SubmitNameRpc`, `TryAssignAllLimbs` กับ `TryAssignLocalLimb`, `GetPlayerObject` สองชุด, ลูปโอน ownership สองชุด หัวไฟล์ของ `PvpTeamManager` บอกว่าฉาก PvP ห้ามมี `LobbyManager` เพราะลอบบี้จะปิดหุ่นตัวเกินทิ้ง |
| 10 | เครื่องมือดีบักในของที่ปล่อยจริง | client ไหนก็ทำให้แขนขาหลุดได้ | High | `DebugLimbBreaker` อยู่บน `PlayerHUD.prefab` โดย `enableDebugKeys: 1` ปุ่ม 1-4 เรียก `JointPullAndReconnect.DebugRequestBreakRpc` ซึ่งเป็น server RPC ที่ใช้ค่า default ของ NGO คือ `InvokePermission = Everyone` และ `AutoStartHost` อยู่ใน 5 ซีนที่อยู่ใน build |
| 11 | โฟลเดอร์และ namespace | จัดตามชื่อคน ไม่ใช่ตามฟีเจอร์ และมีชื่อโฟลเดอร์หนึ่งที่เป็นคำเหยียดเชื้อชาติ | High | สคริปต์อยู่ใน `Assets/Scenes/TheBestFolder/Mynigga`, `Assets/nok`, `Assets/petong`, `Assets/Yelmee`, `Assets/Scenes/Enemy AndInteractive Object` ชื่อ `Mynigga` เป็นคำเหยียดเชื้อชาติ และโผล่ในทุก path ของสคริปต์ ทุก log และทุก stack trace มี prefab `RobotContainer` สามตัว (`Yelmee/`, `Yelmee/Robotc DDD/`, `เก็บไว้กัน/`) namespace มี `NscGame.Enemy`, `NscGame.Pvp`, `NscUnity.Items`, global และ `Unity.Multiplayer.Samples.Utilities.ClientAuthority` ของคลาสที่ก๊อปมาจากตัวอย่าง แผนผังโฟลเดอร์ใน CLAUDE.md ไม่ตรงของจริง (`player/` และ `Ui/` ไม่ใช่ `Player/` และ `UI/`; `PhysicsDamageSender.cs` กับ `PlayerCam.cs` ไม่ได้อยู่ใน `Combat/`) |
| 12 | `PlayerHandCombat : PlayerHandMovement` | ใช้ inheritance เพื่อเขียนทับฟิลด์ของคลาสแม่ชั่วคราว | Medium | `PerformArmMovement` ตั้งค่า `handDamper`, `brakeDamping`, `smoothedHandTarget`, `velocityCapOverride`, `extraReach` เรียก `base` แล้วคืนค่าเดิม ขณะที่ขาใช้ composition (`PlayerLegCombat` เป็นคอมโพเนนต์แยก) แนวคิดเดียวกันจึงมีสองแพทเทิร์น |
| 13 | `GameFlowManager`, `ParkourFlowManager`, `PvpResultUI` | กติกาของโหมด UI ผลลัพธ์ และการเปลี่ยนฉากปนกันและถูกก๊อปซ้ำ ชื่อฉากเมนูถูกนิยาม 4 แบบ | Medium | `FormatTime`, `RestartLevel`, `ExitToMenu`, `HidePanelInstant` การเริ่มจับเวลา และลูปปลดล็อกเคอร์เซอร์ เป็นโค้ดก๊อปกัน ชื่อฉากเมนู: `"-Menu"` (ค่า default ของ GameFlowManager), `"Assets/nok/scene/Game/-MenuNOk.unity"` (ParkourFlowManager), `"-MenuNOk"` (ค่าคงที่ใน `ReturnToMenuOnHostLost` และ `InMatchMenu`) ค่าไหนถูกใช้จริงขึ้นกับข้อมูลที่ serialize ในซีน: **ไม่สามารถสรุปได้จากโค้ดที่มี** |
| 14 | `OnlineNetworkUI` + `ReturnToMenuOnHostLost` | วงจรชีวิตของ session อยู่ในเมนู UI และสถานะ reconnect ถูกแชร์ผ่าน static ที่ใครก็แก้ได้ | Medium | `OnlineNetworkUI` คุม `CreateSessionAsync`, `JoinSessionByCodeAsync`, การออกและรีเซ็ต พร้อมแผง UI สี่แผงและแอนิเมชัน `LastRoomCode`, `LastSessionId`, `LeavingIntentionally` เป็น static และถูกเขียนโดยทั้งสองคลาส |
| 15 | `EnemyHealth` | บอสรู้จักโหมดของเกม | Medium | `ServerDie` เรียก `GameFlowManager.Instance.TriggerVictory()` |
| 16 | `IHittable`, `AttackType` | สัญญาเรื่องดาเมจที่ใช้ร่วมกันอยู่ในโมดูลบอส และผู้เรียกต้อง downcast | Medium | `IHittable` ประกาศไว้ท้ายไฟล์ `EnemyCombat.cs` ส่วน `AttackType` เป็นรายชื่อท่าของบอส แต่ถูกใช้เป็นแหล่งดาเมจของหมัด เตะ PvP และปืน (`GunHeldItem` กับ `Projectile` รายงานเป็น `AttackType.LightPunch`) และ overload สองตัวบังคับให้ทุกคลาสที่ implement ต้องเขียนเมธอดเกือบเหมือนกันสองตัว |
| 17 | `HandItemHolder` | "มือ" แบบทั่วไปที่รู้จักอาวุธชนิดเดียวและ singleton ของโหมดสามตัว | Medium | `Current as ChargeGunHeldItem` ใน `Update` และ `FireRpc`, ฟิลด์ `serverChargingGun`, RPC สำหรับชาร์จ และ `CanUseWeapon()` อ่าน `GameFlowManager`, `PvpTeamManager`, `LobbyManager` |
| 18 | การบันทึกค่าตั้ง | คีย์ PlayerPrefs กระจายอยู่หลายไฟล์ และเสียงหลักถูกเขียนโดย UI สองตัว | Medium | คีย์แบบตัวอักษรตรงๆ อยู่ใน `SettingsManager`, `InMatchMenu`, `LobbyManager`, `PvpTeamManager`, `VoiceChat`, `GameAudio`, `MouseSettings`, `GraphicsQualityManager` คีย์ `"PlayerName"` ถูกอ่าน 3 ไฟล์ และ `"MasterVolume"` ถูกเขียนโดย `SettingsManager.OnMasterVolumeChanged` และ `InMatchMenu.OnMasterChanged` |
| 19 | helper แอนิเมชัน UI | helper ของ DOTween ชุดเดียวกันถูกก๊อปไป 4 คลาส | Medium | `SetVisibleAnimated`, `SetVisibleInstant`, `KillUiTween`, `GetOriginalScale`, `PlayButtonClickFeedback`, `UpdateMenuFeel` อยู่ใน `OnlineNetworkUI`, `LobbyManager` ("ported from OnlineNetworkUI"), `PvpTeamSelectUI`, `InMatchMenu` และ CLAUDE.md ยังแนะนำให้ก๊อปแพทเทิร์นนี้ |
| 20 | Input | มี input API สองแบบ ปุ่มถูก hard-code ในราว 12 คลาส และข้อความสอนเล่นไม่ตรงกับโค้ด | Medium | `InputCompat` (ไอเทม, `InMatchMenu`) กับ `Input.*` (แขนขา, กล้อง, `JointPullAndReconnect`, `TutorialManager`, `DebugLimbBreaker`) `TutorialManager` บอกว่า "กด Left Shift เพื่อต่อย", "W/S ปรับความสูง", "กด F อีกครั้งเพื่อปล่อย" แต่โค้ดต่อยด้วยคลิกซ้าย เตะด้วย Shift และจับของเฉพาะตอนกด F ค้าง |
| 21 | state ที่ public และแก้ได้ | คลาสอื่นเขียนฟิลด์ที่เจ้าของควรคุมเอง | Medium | `torso.currentState.Value` ถูกเขียนโดย `JointPullAndReconnect.HandleDisconnection` และ `BlackHoleProjectile.KnockDownRobot`, `torso.armPullIntensity` ถูกเขียนโดย `PlayerHandMovement`, ฟิลด์ `PlayerFootForRobot.isStepping/isJumping/isPushingRecovery/plantedPosition` เป็น public, `RobotHealth.MaxHp` และ `currentHp` เป็น public, `LobbyManager.Instance` และ `leftArm`… เป็นฟิลด์ public, `PlayerCam.followTarget` ถูกตั้งโดย `LobbyManager` และ `PvpTeamManager` |
| 22 | singleton และการค้นฉาก | singleton แบบ `Instance` 9 ตัว static ที่แก้ได้ราว 10 ตัว และการเรียก `FindObject*ByType` 20 จุดในโค้ด runtime | Medium | ดูข้อ F |
| 23 | โค้ดที่ไม่ได้ใช้ | คลาสและฟิลด์ที่ไม่ได้ใช้ยังคอมไพล์อยู่และทำให้คนอ่านเข้าใจผิด | Low | `RobotTeam` (ไม่ได้ติดที่ไหน แต่ `PhysicsDamageSender` ยังเรียกอยู่), `RobotManager` (อ้าง `OutDated.Following`), `TestNetwork`, `ClientNetworkTransform`, `PlayerPartsHitbox` (ว่างเปล่า), `OutDated/*`, `ItemWheelUI.IsAnyOpen`, `PlayerHandMovement.detachedMoveSpeed` (และไม่มีฟิสิกส์ของมือตอนหลุดเลย), `PlayerLegCombat.localLiftArmed` / `kickNeedsMouseRelease` (เขียนแต่ไม่เคยอ่าน), `OnlineNetworkUI.nextSceneName`, `PlayerLegCombat.IsFinite` (ซ้ำกับ `Vector3Extensions.IsValid`) |
| 24 | `TutorialManager` | เนื้อหาถูก hard-code ในคลาส และไม่ตรงกับปุ่มจริงแล้ว | Low | ข้อความแต่ละขั้นเป็น static array ดูแถวที่ 20 |
| 25 | ลำตัวกับขาที่หลุด | ลำตัวยังนับขาที่หลุดเป็นเท้าในกฎการล้มและในแรงดึงเข้ากลาง | High (ต้องยืนยันใน Play Mode) | เท้าลงทะเบียนตอน spawn (`PlayerFootForRobot.cs:219`) และถอนตอน despawn เท่านั้น (`PlayerFootForRobot.cs:340`) ลูปใน `TorsoMovement.HandleFakeHoverAndPosture` นับทุกเท้าที่ลงทะเบียน (`TorsoMovement.cs:196-213`) และ `IsGrounded()` ไม่เช็คว่าต่ออยู่ (`PlayerFootForRobot.cs:1074-1080`) ขาที่หลุดนอนบนพื้นจึงนับว่าแตะพื้น ลำตัวถูกดึงไปทางมัน และกฎ "ลอยนานเกิน" ไม่ทำงาน ถ้าหลุดตอนผู้เล่นกดคลิกซ้ายค้าง `isStepping` ของขานั้นค้างเป็น true บน server เพราะถูกล้างเฉพาะในสาขาที่ต่ออยู่ของ `HandleInput` (`PlayerFootForRobot.cs:710-715`, `PlayerFootForRobot.cs:800-805`) หลังลุกขึ้น ขาที่เหลือก้าวเมื่อไหร่ กฎสองขาก้าวพร้อมกัน (`TorsoMovement.cs:249-258`) จะนับครบสองขาแล้วสั่งล้ม |
| 26 | `PlayerHandMovement` → `TorsoMovement.armPullIntensity` | มือทุกข้างเขียนทับค่าเดียวกันทุก tick | Medium | `PlayerHandMovement.cs:805` (มือที่ปีน) และ `PlayerHandMovement.cs:809` (มือที่ไม่ได้จับ เขียน 0) รันทุก `FixedUpdate` ของแขนที่ต่ออยู่ทั้งสองข้าง ค่าที่ลำตัวใช้ลดแรงดึงเข้ากลางตอนปีน (`TorsoMovement.cs:308`) จึงเป็นของแขนที่รันทีหลัง ไม่ใช่ของแขนที่ปีน |
| 27 | `RespawnManager` | แคช joint ทั้งตัวไว้ตอน `Awake` แต่ joint ของแขนขาถูกสร้างใหม่ทุกครั้งที่ต่อกลับ และต้องรู้จักคลาสของหุ่นทุกตัว | Medium | `RespawnManager.cs:109-125` เก็บ `GetComponentsInChildren<Joint>` ครั้งเดียว ส่วน `JointPullAndReconnect.ReconnectSystem` สร้าง `ConfigurableJoint` ตัวใหม่ (`JointPullAndReconnect.cs:258`) ที่ไม่อยู่ในแคช จึงไม่ถูกปลดระหว่าง teleport และ `RespawnManager` อ้าง `TorsoMovement`, `PlayerFootForRobot`, `PlayerHandMovement` ตรงๆ (`RespawnManager.cs:53-55`, `RespawnManager.cs:217-225`) |
| 28 | RPC ของแขนขาและลำตัว | ไม่ได้จำกัดให้เฉพาะเจ้าของเรียก | Medium | `[Rpc(SendTo.Server)]` ใน `PlayerHandMovement.cs:585-586, 607, 663`, `PlayerFootForRobot.cs:1008-1015` และ `TorsoMovement.cs:465` ใช้ค่าเริ่มต้น `InvokePermission = Everyone` (NGO 2.12, `RpcAttributes.cs:91-93`) client ไหนก็ส่งเป้าหมายให้แขนขาของคนอื่นได้ ใน PvP คือขับแขนขาของฝ่ายตรงข้ามได้ถ้า client ถูกแก้ (`PlayerInventory` ทำถูกแล้วด้วย `RpcInvokePermission.Owner`) ส่วน `TorsoMovement.ApplyRecoveryForceRpc` ไม่มีโค้ดไหนเรียกเลย ลบได้ |

---

## C. การวิเคราะห์หน้าที่ของคลาส

### ความเสี่ยงของ God class และ Manager

| คลาส | บรรทัด | เหตุผลอิสระที่ทำให้ต้องแก้ | Risk | ชื่อสะท้อนหน้าที่จริงไหม |
|---|---|---|---|---|
| `LobbyManager` | 1533 | ~10 | Critical | ไม่ เป็นแค่ที่เก็บของ: รายชื่อผู้เล่น ที่นั่งรอ reconnect การจองแขนขา การเริ่มแมตช์ การผูกผู้เล่นกับแขนขา การหาหุ่น การแช่แข็งฟิสิกส์ การเริ่มสอนเล่น UI ลอบบี้ทั้งหมด และตัวตนของเครื่อง |
| `OnlineNetworkUI` | 1430 | ~8 | High | บางส่วน เป็นเมนู UI บวกวงจรชีวิตของ session UGS ทั้งหมด |
| `PlayerFootForRobot` | 1128 | ~7 | High | ใช่ในส่วนฟิสิกส์ แต่ input เคอร์เซอร์ และ IMGUI เกินหน้าที่ |
| `SettingsManager` | 1113 | ~6 | High | บางส่วน เป็น UI ตั้งค่า บวกการบันทึก การใช้ค่าจอ UI เสียงพูด ตัวโชว์ FPS/ping และการย้ายตัวเองไปอยู่ใน canvas ข้ามฉาก |
| `PlayerHandMovement` | 995 | ~6 | High | ใช่ในส่วนฟิสิกส์ แต่ input เคอร์เซอร์ การเล็งแบบ static และ IMGUI เกินหน้าที่ |
| `PvpTeamManager` | 608 | ~5 | Medium | ส่วนใหญ่ใช่ คือรายชื่อและผู้ชนะของ PvP แต่การผูกแขนขาและการหาแขนขาด้วยชื่อควรอยู่ที่อื่น |
| `PvpRobotTeam` | 321 | 4 | Medium | บางส่วน ป้ายทีม registry แบบ static การเฝ้าดูการแพ้ และการกระจายดาเมจที่ลำตัว |
| `GameFlowManager` | 319 | 5 | Medium | บางส่วน ใช้กับโหมด Boss เท่านั้น และยังดูแล UI ผลลัพธ์ การเปลี่ยนฉาก และเคอร์เซอร์ |
| `TorsoMovement` | 517 | 3 | Medium | ใช่ แต่ชื่อบอกว่า "เคลื่อนที่" ขณะที่คลาสตัดสินการทรงตัวและการล้ม |
| `PlayerHandCombat` | 521 | 2 | Medium | ใช่ ปัญหาอยู่ที่กลไก inheritance (แถว 12) |
| `HandItemHolder` | 265 | 3 | Medium | ส่วนใหญ่ใช่ แต่โปรโตคอลของปืนชาร์จรั่วเข้ามา |
| `EnemyController`, `EnemyCombat`, `EnemyUltimate` | ~650 ต่อไฟล์ | 2-3 | Low | ใช่ ใหญ่แต่เกาะกลุ่มกันดีสำหรับบอสตัวเดียว |
| `RespawnManager`, `GraphicsQualityManager`, `TutorialManager` | 150-440 | 1-2 | Low | ใช่ |
| `RobotManager` | 13 | 0 | Low | ไม่ ว่างเปล่า ลบได้ |

### จุดที่เป็น spaghetti

- **ดาเมจที่ขึ้นกับลำดับ** ตัวส่งดาเมจสองตัวบน rigidbody เดียวกันสื่อสารกันผ่านผลข้างเคียง `NotifyPunchImpact()` → `hasHitThisPunch` (แถว 2)
- **ผูกกันด้วยลำดับเวลาผ่านการแก้ฟิลด์** `PlayerHandCombat.PerformArmMovement` เขียนฟิลด์ที่สืบทอดมา 5 ตัว เรียก `base` แล้วคืนค่า ถ้าลืมคืนค่าตัวไหน ฟีลของแขนจะเปลี่ยนแบบเงียบๆ
- **`TorsoMovement.HandleFakeHoverAndPosture`** (~150 บรรทัด) ประเมินกฎการล้ม 4 ข้อด้วยตัวจับเวลา 4 ตัว แล้วค่อยใส่แรงดึงเข้ากลาง แรงลอย และแรงตั้งตรง โดยมี `return` กลางทาง
- **แฟล็กข้ามฉาก** `intentionalLeave` (ของอินสแตนซ์) กับ `ReturnToMenuOnHostLost.LeavingIntentionally` (static) ต้องถูกตั้งตามลำดับที่ถูกโดยสองคลาส ไม่งั้นจะวนพยายามต่อกลับ
- **หาของด้วยการ poll** `HudVisibilityGate`, `EnemyHealthUI` และ `LocalRobotBinder` ค้นหา `LobbyManager` ทุกวินาที

### ขอบเขตหน้าที่ที่แนะนำสำหรับคลาสสำคัญ

**`LobbyManager`**
- หน้าที่ปัจจุบัน: (1) รายชื่อ client พร้อมชื่อ `playerId` และสถานะการเชื่อมต่อ (2) กันที่นั่งไว้รอ reconnect (3) จองแขนขา (`limbOwners`, `RequestLimbServerRpc`) (4) เริ่มแมตช์ (`netGameStarted`, โอน ownership) (5) ผูกผู้เล่นเครื่องนี้กับแขนขา (กล้องตาม เปิดตัวควบคุม และลูป retry) (6) หาหุ่นและปิดหุ่นตัวเกิน (7) แช่แข็งและปลดฟิสิกส์ (8) เริ่มสอนเล่น (9) UI ลอบบี้ทั้งหมด รวม hover สี tween และ parallax (10) `LocalPlayerId` แบบ static
- ปัญหา: การออกแบบ UI ใหม่ การเปลี่ยนนโยบาย reconnect และการเปลี่ยน prefab หุ่น ล้วนต้องแก้ไฟล์เดียวกัน และโค้ดเกมเพลย์ต้องหาอ็อบเจกต์ UI ตัวนี้เพื่อรู้ว่ากดปุ่มได้หรือยัง
- หน้าที่ที่ควรเป็น: "ใครจองแขนขาชิ้นไหน และแมตช์เริ่มเมื่อไหร่"
- คลาสที่ควรแยกออกมา:
  - `LimbSelection` (NetworkBehaviour): รายชื่อ ที่นั่ง การจอง คำขอเริ่ม
  - `LimbControlBinder` (MonoBehaviour ฝั่ง client ใช้ร่วมกับ PvP): ให้กล้องตามและเปิดคอมโพเนนต์ input ของแขนขาที่ได้รับ
  - `LimbSelectionPanel` (UI): รูปแขนขา สี รายชื่อผู้เล่น ข้อความสถานะ
  - การแช่แข็งฟิสิกส์ย้ายไปเป็น `Robot.SetSimulationEnabled(bool)` โดยให้เฟสของแมตช์เป็นตัวสั่ง
  - `LocalPlayerId` และชื่อผู้เล่นย้ายไป `GameSettings`/`PlayerIdentity`

**`OnlineNetworkUI`**
- ปัจจุบัน: เริ่ม UGS และล็อกอิน สร้าง/เข้า/ออก/รีเซ็ต session sync จำนวนผู้เล่นและความจุห้อง เลือกแมพและโหลดฉาก state machine ของแผงเชื่อมต่อ แผง reconnect tween ฟีดแบ็กปุ่ม parallax
- แนะนำ: `SessionService` (อยู่ตลอดอายุแอป และรวม `ReturnToMenuOnHostLost` เข้ามา) มี `HostAsync`, `JoinAsync`, `LeaveAsync`, การต่อกลับ และอีเวนต์ `StateChanged` / `MainMenuUI` สำหรับแผงต่างๆ / `RoomPanel` สำหรับห้องรอ จำนวนผู้เล่น และการเลือกแมพ

**`PlayerFootForRobot` และ `PlayerHandMovement`**
- ปัจจุบัน: input ของเจ้าของ (เล็งเมาส์ ล้อ ปุ่ม Q Space F) ล็อกเคอร์เซอร์และ ESC โมเดลการเล็ง ตัวชี้แบบ IMGUI โปรโตคอล RPC และการตรวจค่า ฟิสิกส์ฝั่ง server การตั้งค่า joint chain การรีเซ็ตตอน respawn
- แนะนำ: ย้ายโค้ดฝั่งเจ้าของ (input การเล็ง เคอร์เซอร์ crosshair ราว 300 บรรทัดต่อไฟล์) ไปไว้ใน `LegInput` / `ArmInput` และแทนฟิลด์เล็งแบบ static ด้วย `LocalAim` หนึ่งตัวต่อผู้เล่นในเครื่อง ฝั่ง server เป็น `LegController` / `ArmController` ที่สืบทอดจาก `LimbController` ตัวเดียวกัน (rigidbody จุดหมุน สปริง การทำ joint chain ให้ไม่ขาด และ `ServerResetForRespawn`) เพราะสองไฟล์นี้มีโค้ดเดินโซ่ joint และโค้ดรีเซ็ตที่ซ้ำกันอยู่แล้ว แยกการจับของออกมาเป็น `ArmGrip` เพราะมี state และโปรโตคอลขอ ยืนยัน และบังคับปล่อยของตัวเอง และลำตัวต้องอ่านแค่ส่วนนี้ ขาใช้ `LegMode` ค่าเดียวต่อ tick แทนแฟล็ก bool 4 ตัวที่ต้องอ่านรวมกัน แล้วส่ง `FootContact` ที่คำนวณจากโหมดให้ลำตัว ขาที่หลุดจึงไม่ถูกนับเป็นเท้าและไม่มีแฟล็ก stepping ค้าง (แถว 25) ไม่แยกย่อยกว่านี้ เพราะการยืน การก้าว และการกระโดดใช้ rigidbody เป้า และจุดปักเดียวกันทุก tick (แผนภาพ 5)

**`PlayerHandCombat`**
- แนะนำ: เป็นคอมโพเนนต์ `PunchAbility` แยกออกมาเหมือน `PlayerLegCombat` แล้วให้ `ArmController` รับ struct `ArmMotionOverride` (เป้า เพดานความเร็ว damper สเกลเบรก ระยะยืดพิเศษ) สำหรับ physics tick นั้น จะได้ไม่ต้องแก้ฟิลด์แล้วคืนค่า

**`TorsoMovement`**
- ปัจจุบัน: state ของลำตัว กฎการล้ม 5 ข้อพร้อมตัวจับเวลา 4 ตัว แรงพยุงตัว การกระโดดสองคน การลุก การรีเซ็ตตอน respawn และรายชื่อเท้ากับมือที่ลงทะเบียนตอน spawn และถอนตอน despawn เท่านั้น ส่วน state ถูกเขียนจากภายนอกอีก 2 คลาส (`JointPullAndReconnect.cs:183`, `BlackHoleProjectile.cs:202`)
- แนะนำ: เปลี่ยนชื่อเป็น `TorsoBalance` แยกกฎการล้มเป็นคลาส C# ธรรมดา `FallRules` ที่รับ `BalanceInput` แล้วคืน `FallReason` เลิกใช้รายชื่อที่ลงทะเบียนไว้ โดยให้ลำตัวอ่านแขนขาทั้ง 4 ชิ้นจาก `Robot` ทุก tick (ขาที่หลุดยังนับในกฎล้มแบบเดิมเป็นค่าเริ่มต้น `FallLimits.countDetachedLegs = true` จนกว่าทีมจะลองเล่นแล้วตัดสิน เพราะการแก้แถว 25 เปลี่ยนความรู้สึกตอนเหลือขาเดียว) และให้คลาสอื่นสั่งล้มได้ทางเดียวคือ `ServerKnockDown(FallReason)` แขนขาเรียกลำตัวได้แค่ `ServerAddStress`, `ServerApplyJump` และ `ServerApplyRecoveryPush` ส่วนแรงพยุง การกระโดด และการลุก อยู่ใน `TorsoBalance` ต่อ เพราะใช้ rigidbody และ state ตัวเดียวกันทุก tick (แผนภาพ 4)
- เปลี่ยนจากเวอร์ชันก่อน: เดิมรีวิวนี้บอกว่า `FallRules` ไม่บังคับ หลังตรวจโค้ดรอบสองพบว่ารายชื่อที่ลงทะเบียนไว้ทำให้ขาที่หลุดยังถูกนับ (แถว 25) และตอบไม่ได้ว่าหุ่นล้มเพราะกฎข้อไหน จึงเปลี่ยนเป็นแนะนำให้ทำ เพราะนี่คือกติกาหลักของเกมที่ต้องเทสได้

**`JointPullAndReconnect`**
- ปัจจุบัน: ทำลายและสร้าง joint ใหม่ เก็บค่าตั้งของ joint รับปุ่ม R ฟิสิกส์การดึงกลับ sync สถานะการต่อ เขียนสถานะของตัวควบคุม สั่งลำตัวล้ม และปุ่มดีบัก
- แนะนำ: `LimbAttachment` เก็บแค่สถานะ ค่าตั้งของ joint ฟิสิกส์การดึงกลับ และอีเวนต์ `AttachmentChanged` ปุ่ม R ย้ายไปอยู่กับ input ของแขนขา ผลที่ตามมาของการหลุดย้ายออกไปที่อื่น: `RobotLimb` ใช้กติกาของชิ้น (เลือดหมดแล้วหลุด ต่อกลับแล้วเติมเลือด) `Robot` สั่งลำตัวล้มพร้อมเหตุผล และตัวควบคุมอ่าน `limb.IsAttached` แทนการเก็บสำเนาไว้เอง (แผนภาพ 3 และ 11)

**`PhysicsDamageSender` + `PvpDamageSender`**
- หน้าที่เดียวกัน ("แปลงการชนของแขนขาเป็นคำขอทำดาเมจ") แต่กติกาเป้าต่างกัน รวมเป็น `LimbStrike` ที่สร้าง `DamageInfo` แล้วเรียก `DamageRouter` (ดูข้อ H)

**`PvpRobotTeam`**
- ทีมย้ายไป `Robot.Team` / registry ย้ายไป `Robot.All` / `Robot.FromCollider` / ดาเมจที่ลำตัวย้ายไป `RobotBodyDamageRelay` (`IDamageable` บนลำตัว) / การเฝ้าดูการแพ้ย้ายไป `PvpModeRules`

**`GameFlowManager`, `ParkourFlowManager`, `PvpResultUI`**
- แนะนำ: กติกาฝั่ง server (`BossModeRules`, `ParkourModeRules`, `PvpModeRules`) + `MatchResultPanel` ตัวเดียว + `SceneNavigator` ที่ใช้ร่วมกันและมีที่เดียวสำหรับชื่อฉาก

**`SettingsManager` และ `InMatchMenu`**
- แนะนำ: `GameSettings` เป็นเจ้าของคีย์ การโหลด การบันทึก และการนำค่าไปใช้ ส่วน `SettingsPanel` กับ `InMatchMenu` ทำแค่แสดงผลและส่งต่อการเปลี่ยนค่า ย้ายตัวโชว์ FPS/ping ไปเป็น `NetStatsOverlay`

**`HandItemHolder`**
- แนะนำ: เพิ่ม hook ฝั่ง server ให้ `HeldItem` (`ServerOnUseStart`, `ServerOnUseRelease(float heldSeconds)`) เพื่อให้โปรโตคอลการชาร์จอยู่ใน `ChargeGunHeldItem` และอ่าน `GameplayGate` แทน singleton สามตัว

**`LocalRobotBinder`**
- เมื่อมี `LimbSelection` แล้ว คลาสนี้จะเหลือแค่ `LocalPlayerRobot` บางๆ ที่บอกว่าเราได้ `(Robot, LimbSlot)` ไหน widget ของ HUD ไม่ต้องแก้

**ไม่ต้องแก้:** `PlayerInventory`, `WorldItem`, `ItemDatabase`, `ItemDefinition`, `ItemPickupInteractor`, `UiFocus`, `MouseWheelFocus`, `MouseSettings`, `GameAudio`/`AudioCategory`, `EnemyRagdoll`, `EnemyBodySway`, `CheckpointZone`, `FallDeathZone`, widget ของ HUD, `VoiceLevelMeter`, `Vector3Extensions`

---

## D. การวิเคราะห์การตั้งชื่อ

### คำศัพท์กลาง

ตอนนี้โปรเจกต์ใช้ `Manager`, `Movement`, `Combat`, `ForRobot`, `Binder`, `Gate`, `Check` และ `UI` กับบทบาทที่ทับซ้อนกัน เสนอให้ใช้คำศัพท์ชุดนี้:

| คำ | ความหมาย | ตัวอย่าง |
|---|---|---|
| `Robot` | หุ่นทั้งตัว (aggregate) | `Robot`, `RobotLimb` |
| `Limb`, `LimbSlot` | แขนหรือขาหนึ่งชิ้น และเป็นชิ้นไหนในสี่ชิ้น | `LimbSlot.LeftLeg` |
| `*Controller` | ตัวขับฟิสิกส์ของอวัยวะฝั่ง server | `ArmController`, `LegController` |
| `*Input` | ตัวอ่าน input ฝั่งเจ้าของที่ส่ง RPC | `ArmInput`, `LegInput` |
| `*Ability` | ท่าที่เข้าไปคุมตัวควบคุมชั่วคราว | `PunchAbility`, `KickAbility` |
| `*Health` | เจ้าของเลือด | `LimbHealth`, `EnemyHealth` |
| `*ModeRules` | กติกาของโหมดฝั่ง server | `BossModeRules` |
| `*Rules` (คลาส C# ธรรมดา) | ชุดกติกาที่เป็นลอจิกล้วน เทสได้โดยไม่ต้องเปิด Unity | `FallRules` |
| `*Service` | ระบบที่อยู่ตลอดอายุแอป (`DontDestroyOnLoad`) | `SessionService` |
| `*UI` / `*Panel` | แสดงผลอย่างเดียว | `SettingsPanel`, `HullRingUI` |
| เมธอด `Server*` | ทำงานบน server เท่านั้น | `ServerApplyDamage` |
| เมธอด `Request*` | client ขอให้ server ทำ | `RequestEquip` (มีใช้แล้ว) |
| ชื่ออีเวนต์ | รูปอดีต ไม่ขึ้นต้นด้วย `On` | `Died`, `PhaseChanged` |
| ชื่อตัวรับอีเวนต์ | `On` + ชื่ออีเวนต์ | `OnPhaseChanged` |

### โฟลเดอร์และ namespace

```text
Assets/Scenes/TheBestFolder/Mynigga/ → Assets/_Game/Scripts/{Core, Robot, Enemy, Items, Match, Services, UI}/
เหตุผล: ชื่อปัจจุบันเป็นคำเหยียดเชื้อชาติ และสคริปต์ไม่ควรอยู่ใต้ Scenes/

Assets/nok/, Assets/petong/, Assets/Yelmee/ (ส่วนที่เป็นสคริปต์) → โฟลเดอร์ตามฟีเจอร์ข้างบน
เหตุผล: โฟลเดอร์ตามชื่อคนไม่ได้บอกคนอ่านว่า "ไอเทม" หรือ "PvP" อยู่ตรงไหน เก็บโฟลเดอร์ส่วนตัวไว้สำหรับซีนทดลองเท่านั้น

Assets/nok/Timeline'/ → Assets/_Game/Scripts/Match/Timeline/
เหตุผล: เครื่องหมาย ' ทำให้คำสั่ง shell และเครื่องมือบางตัวพัง

Assets/เก็บไว้กัน/, Assets/_Recovery/ → เอาออกจาก Assets (git เก็บประวัติให้อยู่แล้ว)
เหตุผล: prefab สำรองมีชื่อซ้ำและอ้างสคริปต์ตัวเดียวกับของจริงผ่าน GUID

namespace NscGame.Enemy, NscGame.Pvp, NscUnity.Items, global → Nsc.Core, Nsc.Robot, Nsc.Enemy, Nsc.Items, Nsc.Match, Nsc.Services, Nsc.UI
เหตุผล: มี root เดียว และเพิ่ม namespace ตอนย้ายไฟล์ ไม่ต้องไล่แก้ทั้งโปรเจกต์รอบเดียว
```

การย้ายและเปลี่ยนชื่อต้องทำใน Unity Editor เท่านั้น เพื่อให้ GUID ในไฟล์ `.meta` และการอ้างอิงจากซีนกับ prefab ไม่หลุด

### คลาส

```text
TorsoMovement → TorsoBalance
เหตุผล: คลาสนี้ตัดสินว่ายืนหรือล้ม ไม่ได้ขยับหุ่น

PlayerHandMovement → ArmController + ArmGrip (+ ArmInput)
PlayerFootForRobot → LegController (+ LegInput)
เหตุผล: ให้เป็นคู่ที่สอดคล้องกัน "ForRobot" ไม่ได้เพิ่มความหมาย และ "Player" ทำให้เข้าใจผิด (แขนขาเป็นของหุ่น ผู้เล่นแค่คุม)
คำว่า Controller ในที่นี้หมายถึงตัวควบคุมแบบป้อนกลับ (สปริง PD ที่ขับ rigidbody ไปหาเป้า) ไม่ใช่ตัวรับ input

(ใหม่) LimbController ← โค้ดที่ซ้ำกันใน PlayerHandMovement และ PlayerFootForRobot
เหตุผล: คลาสฐานของ ArmController และ LegController ให้ Robot รีเซ็ตทุกชิ้นได้โดยไม่ต้องรู้ว่าเป็นแขนหรือขา

(ใหม่) FallRules, BalanceInput, FallReason, FallLimits ← กฎการล้มและตัวจับเวลาใน TorsoMovement.HandleFakeHoverAndPosture
(ใหม่) FootContact, LegMode ← แฟล็ก isStepping, isJumping, isPushingRecovery, _releasedForClimb และ IsGrounded ของเท้า
เหตุผล: แยกกติกาหลักของเกมออกจากฟิสิกส์ ให้เทสได้และบอกเหตุผลของการล้มได้

PlayerHandCombat → PunchAbility
PlayerLegCombat → KickAbility
เหตุผล: บอกว่าคอมโพเนนต์ทำอะไร และเข้ากับโมเดล composition

JointPullAndReconnect → LimbAttachment
เหตุผล: ตั้งชื่อตามแนวคิด (ชิ้นนี้ต่ออยู่ไหม) ไม่ใช่ตามกลไก

RobotHealth → LimbHealth
เหตุผล: เลือดเป็นของแต่ละชิ้น หุ่นทั้งตัวไม่มีเลือดของตัวเอง

PhysicsDamageSender + PvpDamageSender → LimbStrike
เหตุผล: คอมโพเนนต์เดียวสำหรับ "แขนขาชิ้นนี้ชนอะไรบางอย่าง"

IHittable → IDamageable
เหตุผล: สัญญานี้เกี่ยวกับการรับดาเมจ และควรอยู่ใน Core ไม่ใช่ใน EnemyCombat.cs

explobuilding (ไฟล์ "explo building.cs") → DestructibleBuilding (DestructibleBuilding.cs)
เหตุผล: เป็นตัวพิมพ์เล็กและย่อคำ และชื่อไฟล์ควรตรงกับชื่อคลาส

LobbyManager → LimbSelection + LimbSelectionPanel + LimbControlBinder
เหตุผล: มันคือการเลือกแขนขาในฉาก ไม่ใช่ลอบบี้ของ UGS และมีสามหน้าที่

OnlineNetworkUI → MainMenuUI + SessionService
ReturnToMenuOnHostLost → รวมเข้า SessionService
เหตุผล: ชื่อปัจจุบันอธิบายแค่กรณีสำรองกรณีเดียว ทั้งที่คลาสทำการต่อกลับและออกจากแมตช์

GameFlowManager → BossModeRules + MatchResultPanel
ParkourFlowManager → ParkourModeRules
PvpTeamManager → PvpMatch
PvpRobotTeam → Robot.Team + PvpModeRules + RobotBodyDamageRelay
RobotTeam → ลบ
เหตุผล: คำว่า "Manager" ปิดบังว่าแต่ละคลาสใช้กับโหมดเดียว และควรแยกกติกาออกจากการแสดงผล

LocalRobotBinder → LocalPlayerRobot
HudVisibilityGate + PvpPlayerHudGate → HudVisibility
SettingsManager → SettingsPanel (+ GameSettings สำหรับข้อมูล)
TutorialManager → TutorialPanel (+ asset TutorialContent)

NetworkCheck.IsServerOrHost() → NetworkAuthority.IsServerOrOffline()
เหตุผล: เมธอดนี้คืน true ตอนไม่มีเครือข่ายเลย ชื่อปัจจุบันซ่อนข้อนี้ไว้

RobotBodyCheck.IsRobotBodyPart(c) → Robot.FromCollider(c) != null
UiTest, PlayerPartsHitbox, RobotManager, TestNetwork, ClientNetworkTransform → ลบ
```

### เมธอด

```text
HandleFakeHoverAndPosture → FallRules.Evaluate + ApplyStandingSupport
เหตุผล: ตอนนี้เมธอดเดียวทำสองงาน การแยกชื่อตรงกับสองงานนั้น

ApplyContinuousRecoveryForce → ServerApplyRecoveryPush
NotifyFootJump(foot) → ServerApplyJump(LimbSlot)
เหตุผล: เมธอดที่รันบน server เท่านั้นใช้คำนำหน้า Server และชื่อที่รีวิวนี้เคยเสนอ (ServerRegisterFootJump) ถูกอ่านเป็น "ลงทะเบียนเท้า" มาแล้ว จึงใช้กริยาที่ตรงกับสิ่งที่ทำ คือใส่แรงกระโดด

RegisterFoot / UnregisterFoot / RegisterHand / UnregisterHand → ลบ
เหตุผล: ลำตัวอ่านแขนขาจาก Robot ทุก tick จึงไม่มีรายชื่อที่ต้องคอยถอนตอนหลุด (จะนับขาที่หลุดหรือไม่ ตั้งได้ที่ FallLimits.countDetachedLegs ค่าเริ่มต้นนับแบบเดิม)

UpdateHandTargetRpc / UpdateFootTargetRpc → SetHandTargetRpc / SetFootTargetRpc
เหตุผล: Set คือแทนค่าเดิม ตรงกับ SetSteppingStateRpc ที่มีอยู่

SetPunchingRpc(bool) → RequestPunchStartRpc() / RequestPunchReleaseRpc()
เหตุผล: พารามิเตอร์ bool ซ่อนคำสั่งสองอย่างที่ต่างกัน

HandleInput (PlayerHandMovement, PlayerFootForRobot) และส่วนที่อ่าน input ใน Update() ของ HandItemHolder → ReadOwnerInput
เหตุผล: "Handle" ไม่ได้บอกอะไร เมธอดนี้อ่าน input ในเครื่องของเจ้าของ

ProcessHitDetection → ApplyHitToNearestPart
เหตุผล: บอกกติกาที่ทำจริง

ServerTakeHit (EnemyHealth) และ ServerTakeDamage สามตัว (RobotHealth, explobuilding) → ServerApplyDamage(in DamageInfo)
เหตุผล: ซิกเนเจอร์เดียวสำหรับทุกอย่างที่รับดาเมจได้

TriggerVictory / TriggerDefeat / TriggerRunEnd → MatchSession.ServerEnd(MatchResult)
ForceBreakJoint → ServerDetach
StartPullingExternal / StopPullingExternal → BeginReattach / CancelReattach
CheckDefeat / CheckDefeatCondition → EvaluateDefeat
UiTest.OnHelthchanged → ลบ (สะกดผิดและไม่ได้ใช้)
```

### ฟิลด์และ property

```text
public NetworkVariable currentState (ลำตัว มือ เท้า) → private NetworkVariable + public State { get; } + เมธอด Server*
เหตุผล: ตอนนี้คลาสอื่นเขียน .Value ได้ตรงๆ

RobotHealth.MaxHp (ฟิลด์ public), currentHp → [SerializeField] maxHp + property MaxHp; NetworkVariable hp แบบ private + property Hp
Jpar → attachment
Joinobject → jointHost
RobotManager.limps → (ลบ; พิมพ์ผิดจาก "limbs")
PlayerCam.playercam / playeral → playerCamera / audioListener
RobotHealth.regening → isRegenerating
TorsoMovement.armPullIntensity (ฟิลด์ public ที่มือทุกข้างเขียนทับทุก tick) → ArmController.ClimbPull ที่ลำตัวอ่านเองแล้วใช้ค่ามากสุด (ต่างจากตอนนี้เล็กน้อย ต้องลองปีนเทียบ)
TorsoMovement.currentStress (public) → stress แบบ private บน server + ServerAddStress(amount)
TorsoMovement.testSingleFootRecovery (ค่า default true) → allowSingleFootRecovery
เหตุผล: เป็นพฤติกรรมที่ปล่อยจริงแล้ว ไม่ใช่การทดสอบ

debugLog / debugPunchLog / debugKickLog (ค่า default true) → ค่า default false
ulong.MaxValue ที่แปลว่า "ไม่มีเจ้าของ" (LobbyManager) และ PvpTeamManager.NoOwner → ค่าคงที่ ClientIds.None ตัวเดียว
```

รูปแบบการตั้งชื่อฟิลด์ private ปนกันในไฟล์เดียว (`_tiltTimer` อยู่ข้าง `currentStress` ใน `TorsoMovement`) เลือกแบบเดียวแล้วใช้ตามนั้น เรื่องนี้ Priority ต่ำ ทำเฉพาะตอนแตะไฟล์นั้นอยู่แล้ว

### อินเทอร์เฟซ

มีอินเทอร์เฟซตัวเดียวคือ `IHittable` เป็นแนวคิดที่ดีแต่อยู่ผิดที่: อยู่ในโมดูลบอส ใช้ `AttackType` ของบอส มี overload สองตัว และผู้เรียกต้อง downcast (`is RobotHealth`, `is explobuilding`) ให้แทนด้วย `IDamageable` ใน Core ที่มีเมธอดเดียวรับ `DamageInfo` และเพิ่ม `IStrikeSource` สำหรับมือ ขา และดาบ ไม่ต้องมีอินเทอร์เฟซอื่นอีก รวมถึงอินเทอร์เฟซแบบ `ISupportProvider` ระหว่างลำตัวกับแขนขา (เหตุผลอยู่ในข้อ N)

### อีเวนต์

อีเวนต์ทุกตัวขึ้นต้นด้วย `On` (`OnConnectionStateChanged`, `OnBound`, `OnFocusChanged`, `OnRosterChanged`, `OnMatchStateChanged`, `OnWinnerDecided`, `OnRegistryChanged`, `OnSlotsChanged`, `OnEquippedChanged`, `OnReconnectStateChanged`, `OnVoiceStateChanged`) และตัวรับอีเวนต์ก็ขึ้นต้นด้วย `On` เหมือนกัน ทำให้บรรทัดอย่าง `joint.OnConnectionStateChanged += OnPartConnectionChanged` อ่านยาก เปลี่ยนเป็น `AttachmentChanged`, `Bound`, `FocusChanged`, `RosterChanged`, `PhaseChanged`, `WinnerDecided`, `SlotsChanged`, `EquippedChanged`, `ReconnectStateChanged` ตอนที่แตะคลาสนั้น และเปลี่ยน `Action<bool, int, int, string>` เป็น `Action<ReconnectStatus>` เพื่อให้ค่าทั้งสี่มีชื่อ

### Enum

- `EnemyState`, `UltimatePhase`, `CombatState`, `LegActionState`: เป็น enum สถานะที่ชัดเจน เก็บไว้
- `AttackType` ปนท่าของบอสกับแหล่งดาเมจทั่วไป แยกเป็น `EnemyAttack` (ท่าประชิดของบอสเท่านั้น: LightPunch, BarragePunch, Kick) และ `DamageSource` (ใน Core) ค่า `BlackHole` ไม่อยู่ใน `EnemyAttack` เพราะหลุมดำยิงจาก `EnemyUltimate` ที่เดียว ตอนนี้ค่านี้ใช้แค่เป็นป้ายแหล่งดาเมจใน `BlackHoleProjectile` (`BlackHoleProjectile.cs:166`, `BlackHoleProjectile.cs:171`) ซึ่งย้ายไปเป็น `DamageSource.EnemyUltimate`
- `TorsoState.Falling` อยู่ไม่เกินหนึ่ง `FixedUpdate` (`TorsoMovement.cs:174-176`) และทั้ง 8 จุดนอก `TorsoMovement` ที่อ่านค่านี้ถือว่าเท่ากับ `Ragdoll` ให้เหลือ `TorsoState {Standing, Ragdoll}` แล้วเก็บเหตุผลไว้ใน `FallReason` คลาสภายนอกสั่งล้มผ่าน `ServerKnockDown(reason)` ไม่ต้องเพิ่ม state อย่าง Staggering หรือ Recovering จนกว่าจะมีพฤติกรรมที่ต่างกันจริง การลุกด้วยปุ่ม Q เป็นการกระทำระหว่าง `Ragdoll` ไม่ใช่ state
- `LegMode {Planted, Stepping, Kicking, Limp, PushingUp, Detached}` (ใหม่) แทนแฟล็ก bool 4 ตัวของเท้าที่ต้องอ่านรวมกับสถานะลำตัวและท่าเตะ
- `FallReason` (ใหม่) บอกว่าหุ่นล้มเพราะกฎข้อไหน ใช้ใน log และในเทส
- `HandState` / `FootState` (`Attached/Detached`) ซ้ำกับ `LimbAttachment` ให้ลบออก
- `GameState {Playing, Victory, Defeat}`, `PvpMatchState {TeamSelect, Fighting, Finished}`, `bool netGameStarted`, `bool netEnded` อธิบายเรื่องเดียวกัน ใช้ `MatchPhase {Preparing, Playing, Ended}` คู่กับ `MatchResult`
- ค่าคงที่ใน `PvpLimb`, `LimbStatusUI.LimbSlot`, `TutorialManager.LimbRole` และการเช็ค `index >= 2` ใน `LobbyManager`: รวมเป็น enum `LimbSlot` ตัวเดียว พร้อม extension `IsArm()`, `IsLeg()`, `DisplayName()`

### คอมเมนต์

คอมเมนต์มีทั้งภาษาไทยและอังกฤษ และ `PlayerFootMovementAudio` เป็นภาษาญี่ปุ่น ส่วนชื่อในโค้ดเป็นอังกฤษอยู่แล้ว การเลือกภาษาคอมเมนต์ภาษาเดียวเป็นเรื่องที่ทีมตัดสินเอง (Optional) แต่การผสมสามภาษาในฟีเจอร์เดียวทำให้คนที่ไม่ใช่ผู้เขียนเดิมรีวิวได้ยากขึ้น

---

## E. การวิเคราะห์ OOP

### Encapsulation

แพทเทิร์นที่เจอบ่อยที่สุดคือ "ฟิลด์ public ที่คลาสอื่นเข้ามาเขียน" เช่น `TorsoMovement.currentState`, `torsoRb`, `armPullIntensity`, `currentStress`; `PlayerFootForRobot.isStepping`, `isJumping`, `isPushingRecovery`, `plantedPosition`; `RobotHealth.MaxHp`, `currentHp`; `LobbyManager.Instance` (เป็นฟิลด์ public ไม่ใช่ property), `leftArm`, `rightArm`, `leftLeg`, `rightLeg`; `PlayerCam.followTarget` ค่าจูนแบบ public เพื่อให้ปรับใน Inspector ไม่เป็นไร แต่ state ระหว่างเล่นไม่ควร public

กติกา: `NetworkVariable` แบบ private + property อ่านอย่างเดียว + เมธอด `Server*` สำหรับการเปลี่ยนค่า ซึ่ง `PlayerInventory` และ `EnemyController` ทำแบบนี้อยู่แล้ว

### Abstraction

- ที่ดีอยู่แล้ว: `UiFocus`, `InputCompat` (adapter ครอบ input สองระบบ), `ItemDatabase`, `LocalRobotBinder` ในมุมของ widget
- ที่ขาด: aggregate `Robot`, สัญญาเรื่องดาเมจ (`DamageInfo` + `IDamageable`), เจ้าของเฟสของแมตช์เพียงหนึ่งเดียว
- ที่รั่ว: ผู้เรียก `IHittable` ต้อง downcast เป็นคลาสจริง, `HandItemHolder` cast เป็น `ChargeGunHeldItem`, `AttackType` เปิดท่าของบอสให้โค้ดผู้เล่นเห็น

### Inheritance

| ลำดับชั้น | เป็น IS-A จริงไหม | ข้อสรุป |
|---|---|---|
| `PlayerHandCombat : PlayerHandMovement` | พอได้ (แขนที่ต่อยได้ก็เป็นแขน) แต่ทำงานด้วยการเขียนทับฟิลด์ของคลาสแม่ | เปลี่ยนเป็น composition (`ArmController` + `PunchAbility`) ให้เหมือนขา |
| `HeldItem` → `ChargeGunHeldItem`, `GunHeldItem`, `SwordHeldItem` | ใช่ และ virtual hook เหมาะกับงานนี้ | เก็บไว้ เพิ่ม hook ฝั่ง server เพื่อให้ตัวถือเลิก cast |
| `ArmController`, `LegController` : `LimbController` (ใหม่) | ใช่ ทั้งคู่เป็นตัวขับ rigidbody ของอวัยวะไปหาเป้าฝั่ง server | ใช้รวมโค้ดที่ซ้ำ (สปริง การเดินโซ่ joint การรีเซ็ต) และให้ `RobotLimb` กับ `Robot` เรียก `ServerResetForRespawn()` ได้โดยไม่ต้องรู้ว่าเป็นแขนหรือขา ลึกแค่ระดับเดียว |
| `ClientNetworkTransform : NetworkTransform` | ใช่ | ไม่ได้ใช้ ลบ |
| `NetworkBehaviour` ทุกตัว | framework บังคับ | ไม่มีปัญหา ไม่มีลำดับชั้นที่ลึก |

### Polymorphism

เงื่อนไขที่คุ้มจะแทน:
- `if (combat != null) … else if (legCombat != null) … else …` ใน `PhysicsDamageSender`, `PvpDamageSender` และ `explobuilding`: แทนด้วย `IStrikeSource` (มือ ขา ดาบ implement ของตัวเอง)
- `hittable is explobuilding` / `is RobotHealth` ใน `BlackHoleProjectile` และ `EnemyCombat`: แทนด้วย `IDamageable.Kind` (Structure, RobotLimb, Enemy) และแฟล็ก `DetachLimb` ใน `DamageInfo`
- `switch (AttackType)` สี่ชุดขนานกันใน `EnemyCombat` (จุดเริ่ม มุมหมุน VFX การเรียกท่า): อันนี้คือข้อมูล ไม่ใช่พฤติกรรม ใช้อาร์เรย์ `EnemyAttackDefinition[]` ที่ serialize ได้ (จุดเริ่ม รัศมี ดาเมจ ดีเลย์ VFX SFX trigger) แทนฟิลด์ 12 ตัวกับ switch 4 ชุด Priority Improvement และคุ้มเฉพาะถ้าจะเพิ่มท่าหรือเพิ่มบอสตัวที่สอง

เงื่อนไขที่ควรเก็บไว้: `switch` บน `TorsoState`, `EnemyState`, `CombatState`, `LegActionState` เพราะเล็ก และ enum ต้อง serialize ใน `NetworkVariable` ได้

### Composition

ขาใช้ composition ได้ดีอยู่แล้ว (`PlayerFootForRobot` + `PlayerLegCombat` บนอ็อบเจกต์เดียวกัน โดยเท้ายอมปล่อยการควบคุมผ่าน `IsKickControllingFoot`) HUD ก็ประกอบ widget รอบ `LocalRobotBinder` ข้อเสนอนี้ขยายแนวคิดเดียวกัน: `Robot` ประกอบด้วยลำตัวและ `RobotLimb` สี่ชิ้น และแขนขาแต่ละชิ้นประกอบด้วย attachment, health, strike, controller, input และ ability

---

## F. การวิเคราะห์ Dependency

### Dependency ปัจจุบัน (ตรวจจากการอ้างในโค้ดจริง)

เกมเพลย์ชี้ไปหา UI และคลาสของโหมด (ทิศที่ผิด):

```text
PlayerHandCombat   → LobbyManager                      (CanReadCombatInputForThisLimb)
PlayerLegCombat    → LobbyManager, GameFlowManager
PlayerHandMovement → GameFlowManager.GameEnded, UiFocus
PlayerFootForRobot → GameFlowManager.GameEnded, UiFocus
HandItemHolder     → GameFlowManager, PvpTeamManager, LobbyManager, UiFocus
EnemyHealth        → GameFlowManager                   (TriggerVictory)
GunHeldItem, Projectile, ChargeGunHeldItem → PvpTeamManager, PvpRobotTeam
RobotHealth        → IHittable, AttackType             (ประกาศอยู่ในโมดูลบอส)
```

ลำดับเกมและ UI ชี้ไปหาเกมเพลย์ (ทิศถูก แต่ผ่านการค้นฉากและชื่อ):

```text
LobbyManager     → TorsoMovement, PlayerHandMovement, PlayerFootForRobot, PlayerCam, TutorialManager, SettingsManager
PvpTeamManager   → PlayerHandMovement, PlayerFootForRobot, PlayerCam, PvpRobotTeam
GameFlowManager  → PlayerFootForRobot, PlayerHandMovement, LobbyManager
ParkourFlowManager → TorsoMovement, LobbyManager, GameFlowManager
LocalRobotBinder → LobbyManager, TorsoMovement, JointPullAndReconnect, PlayerHandCombat, PlayerLegCombat, RobotHealth
```

การต่อสู้:

```text
PhysicsDamageSender → PlayerHandCombat, PlayerLegCombat, EnemyHealth, RobotHealth, RobotTeam, AttackType
PvpDamageSender     → PlayerHandCombat, PlayerLegCombat, PvpRobotTeam, PvpTeamManager, RobotHealth
explobuilding       → PhysicsDamageSender, PlayerHandCombat, NetworkCheck, IHittable
EnemyCombat         → EnemyController, IHittable, RobotHealth
BlackHoleProjectile → IHittable, explobuilding, RobotHealth, TorsoMovement
```

### Dependency วนกัน

1. `PlayerHandCombat ⇄ PhysicsDamageSender` และ `PlayerLegCombat ⇄ PhysicsDamageSender`: ตัวส่งดาเมจอ่าน `PeakPunchSpeed`/`CanDealDamage` และเรียก `Notify*` ส่วนคลาสต่อสู้อ่านค่าจูนของตัวส่งไปใช้ใน debug log
2. `PlayerFootForRobot ⇄ PlayerLegCombat`
3. `TorsoMovement ⇄ PlayerFootForRobot / PlayerHandMovement`: การลงทะเบียน และแขนขาเขียนฟิลด์ของลำตัว
4. `JointPullAndReconnect ⇄ PlayerFootForRobot / PlayerHandMovement`: joint เขียนสถานะของตัวควบคุม ส่วนเท้าอ่าน `JointPull.IsBeingPulled`
5. `OnlineNetworkUI ⇄ ReturnToMenuOnHostLost` ผ่านฟิลด์ static และอีเวนต์ static
6. ระดับโมดูล: แขนขา → `LobbyManager` → แขนขา; `RobotHealth` → โมดูลบอส (`IHittable`) → `RobotHealth`; บอส → `GameFlowManager` → แขนขา

ข้อ 2 และ 3 อยู่ภายในหุ่นตัวเดียว ยอมรับได้ถ้าผ่านสมาชิกแบบอ่านอย่างเดียวที่แคบ ส่วนข้อ 1, 5 และ 6 ข้ามขอบเขตโมดูลและควรตัดออก

### Fan-in และ fan-out

| คลาส | Fan-in (จำนวนไฟล์ที่ใช้ในโค้ด) | หมายเหตุ |
|---|---|---|
| `TorsoMovement` | 14 | ศูนย์กลางตามธรรมชาติของหุ่น แต่ควรเปิดให้เห็นน้อยกว่านี้ |
| `PlayerHandMovement`, `PlayerFootForRobot` | ~12 ต่อตัว | กลายเป็นศูนย์กลางเพราะไม่มีอะไรแทน "หุ่น" |
| `RobotHealth`, `JointPullAndReconnect` | 11 ต่อตัว | |
| `PvpTeamManager` | 10 | ไอเทมและกระสุนพึ่งโหมด PvP |
| `LocalRobotBinder`, `UiFocus` | 10 ต่อตัว | ไม่เป็นไร เป็นศูนย์กลางฝั่ง UI |
| `LobbyManager` | 8 | ปัญหา: เกมเพลย์พึ่งคลาส UI |
| `GameFlowManager` | 7 | ปัญหา: ใช้แฟล็ก static เป็นประตูของทั้งเกม |

Fan-out สูงสุด: `LobbyManager` (คลาสในโปรเจกต์ 9+ ตัว บวก NGO, UGS, DOTween), `OnlineNetworkUI`, `PvpTeamManager`, `PhysicsDamageSender` (6), `GunHeldItem` (6)

### Dependency ที่ซ่อนอยู่

- **เรียก `FindObject*ByType` 20 จุด** ในโค้ด runtime: `LocalRobotBinder` (4), `LobbyManager` (2), `GameFlowManager` (2), `EnemyHealthUI` (2), `LocalItemOwner` (2) และอย่างละหนึ่งจุดใน `PlayerHandCombat`, `PlayerLegCombat`, `HudVisibilityGate`, `ParkourFlowManager`, `EnemyController`, `GunHeldItem`, `PlayerCam`, `GraphicsQualityManager`
- **singleton 9 ตัว**: `LobbyManager`, `SettingsManager`, `VoiceChat`, `GraphicsQualityManager`, `TutorialManager`, `PvpTeamManager`, `GameFlowManager`, `ParkourFlowManager`, `RespawnManager`
- **static ที่แก้ได้**: `GameFlowManager.GameEnded`; `ReturnToMenuOnHostLost.LastRoomCode`, `LastSessionId`, `LeavingIntentionally`, `IsReconnecting`; การเล็งที่แชร์ใน `PlayerHandMovement`; registry ของ `PvpRobotTeam`; `WorldItem.Active`; `ItemWheelUI.openCount`; แคชของ `UiFocus`, `MouseWheelFocus`, `MouseSettings`, `GameAudio` ซึ่งส่วนใหญ่รีเซ็ตถูกต้องตอน domain reload
- **ชื่อ GameObject** ถูกใช้เป็นตัวตน (แถว 4)

### ใครควรรู้จักใคร

| Dependency | กลไก | เหตุผล |
|---|---|---|
| ลำตัว → แขนขา | อ่าน `FootContact`, `ArmGrip.IsSupporting`, `ArmController.ClimbPull` ของแขนขาทั้ง 4 ชิ้นผ่าน `Robot` ทุก tick | หุ่นตัวเดียวกัน และไม่มีรายชื่อที่ต้องคอยถอนตอนหลุด |
| แขนขา → ลำตัว | `ServerAddStress`, `ServerApplyJump`, `ServerApplyRecoveryPush` | มีสามทางเท่านั้น ห้ามเขียนฟิลด์ของลำตัวตรงๆ |
| แขนขา ⇄ ability ของมัน | อ้างอิงตรงแบบ serialize | อ็อบเจกต์เดียวกัน อายุเท่ากัน |
| อะไรก็ตาม → ชิ้นส่วนหุ่น | `Robot` / `RobotLimb` / `LimbSlot` | แทนชื่อและการค้นฉาก |
| ผู้โจมตี → สิ่งที่โดนดาเมจ | `DamageRouter` + `IDamageable` | ผู้รับห้าชนิดที่ไม่เกี่ยวกัน |
| การชน → ข้อมูลการกระแทก | `IStrikeSource` | มือ ขา และดาบต่างกัน |
| บอสตาย แขนขาหลุด เฟสของแมตช์ | อีเวนต์ C# / callback ของ `NetworkVariable` | ผู้ส่งไม่ต้องรู้จักผู้รับ |
| เกมเพลย์ → "แมตช์กำลังเล่นอยู่ไหม" | `GameplayGate` (อ่านอย่างเดียว มีผู้เขียนคนเดียว) | ตัดการพึ่ง UI และคลาสของโหมด |
| อ็อบเจกต์ในฉาก → อ็อบเจกต์ในฉาก | อ้างอิงผ่าน Inspector | ชัดเจน ไม่ต้องค้นตอนรัน |
| UI → เกมเพลย์ | อ่านข้อมูล และส่งคำขอ `Request*` | ไม่มีใครพึ่ง UI |

---

## G. การออกแบบการสื่อสาร

| ผู้ส่ง | กลไก | ผู้รับ | ทำไมเลือกกลไกนี้ |
|---|---|---|---|
| input ของ client เจ้าของ | RPC (`InvokePermission.Owner`) | `ArmController` / `LegController` | เป็นคำสั่งที่ต้องตรวจสิทธิ์ และ owner-only ตรงกับที่ `PlayerInventory` ทำ |
| `LimbStrike`, กระสุน, ท่าโจมตีของบอส | เรียก static `DamageRouter.TryApply(collider, info)` | `IDamageable` | มีที่เดียวสำหรับกติกาทีมและเฟส ผู้รับหลายชนิด |
| `LimbHealth.Hp`, `EnemyHealth.Hp` | `NetworkVariable.OnValueChanged` (มีอยู่แล้ว) | widget HUD, หลอดเลือดบอส | ถูกต้องอยู่แล้ว |
| `LimbHealth`, `LimbAttachment` | อีเวนต์ `Depleted`, `AttachmentChanged` | `RobotLimb` (กติกาของชิ้น) → อีเวนต์ `StateChanged(limb)` → `Robot` → `TorsoBalance.ServerKnockDown(LegDetached)` | ชิ้นส่วนย่อยไม่รู้จักกันเองและไม่รู้จักลำตัว หุ่นเป็นเจ้าของแขนขา |
| `EnemyHealth` | อีเวนต์ `Died` | `BossModeRules` → `MatchSession.ServerEnd(Victory)` | บอสไม่ควรรู้ว่าอยู่ในโหมดไหน |
| จำนวนชิ้นที่หลุดของ `Robot` | query `DetachedCount()` + อีเวนต์ `Robot.LimbStateChanged(limb)` | `BossModeRules`, `PvpModeRules`, HUD | การแพ้เป็นกติกาของโหมด |
| `MatchSession.Phase` | `NetworkVariable` + `PhaseChanged` และเขียน `GameplayGate` | input, อาวุธ, HUD, หน้าผลลัพธ์ | เฟสมีเจ้าของคนเดียว และผู้อ่านพึ่งแค่ Core |
| การแจกแขนขาใน `LimbSelection` | การเปลี่ยนของ `NetworkList` | `LimbControlBinder` → `LocalPlayerRobot.Bound` → HUD | ใช้ร่วมกันทั้ง co-op และ PvP |
| แผง UI | `UiFocus.Push/Pop` | input ของแขนขา (ผ่านนโยบายเคอร์เซอร์) | `UiFocus` ใช้ได้ดีอยู่แล้ว ให้มันเป็นเจ้าของเคอร์เซอร์ด้วย |
| `SessionService` | อีเวนต์ `StateChanged` | `MainMenuUI`, `InMatchMenu` | อยู่รอดข้ามการโหลดฉาก และแทนฟิลด์ static |
| UI ตั้งค่า | เรียก setter ของ `GameSettings` ตรงๆ | `AudioListener`, `GameAudio`, `MouseSettings`, กราฟิก | ง่ายและ synchronous ไม่ต้องมี event bus |
| `FallDeathZone` | `RespawnManager.Instance.RespawnBody()` → `Robot.ServerResetForRespawn()` | แขนขา ลำตัว | service ประจำฉากตัวเดียว แล้วหุ่นกระจายคำสั่งไปยังชิ้นส่วนของตัวเอง |

ไม่ต้องมี event bus เพราะอีเวนต์ข้ามโมดูลมีไม่ถึงสิบตัว และแต่ละตัวมีเจ้าของชัดเจน

### ใครเป็น authority ของอะไร

| เรื่อง | ใครตัดสิน | ถึงเครื่องอื่นอย่างไร | ตอนนี้ในโค้ด |
|---|---|---|---|
| input ของแขนขาแต่ละชิ้น | client ที่เป็นเจ้าของชิ้นนั้น (ได้มาจากการแจกแขนขา) | RPC ไป server แบบ owner-only | `Update()` ของแขนขาทำงานเฉพาะ `IsOwner` แต่ RPC ยังไม่ได้จำกัดเจ้าของ (ข้อ B แถว 28) |
| การตรวจคำสั่ง (ระยะเป้า, ค่า NaN) | server | ไม่ต้องส่ง | `ValidateAndSetFootTarget`, `ValidateAndSetHandTarget` |
| ฟิสิกส์ของหุ่นทั้งตัว | server เท่านั้น | NetworkTransform แบบ AuthorityMode = Server | `FixedUpdate` ของลำตัวและแขนขาขึ้นต้นด้วย `if (!IsServer) return` และ NetworkTransform ทั้ง 15 ตัวใน `RobotContainer.prefab` เป็น Server |
| การล้ม การหลุด เลือด | server | NetworkVariable ที่ server เขียนคนเดียว | `currentState`, `netIsConnected`, `currentHp` |
| stress และตัวจับเวลาของกฎการล้ม | server | ไม่ sync (client ไม่ได้ตัดสินการล้ม) | `TorsoMovement.currentStress` |
| เอฟเฟกต์ตอนโดนตี | server สั่ง | ClientRpc | `RobotHealth.PlayHitEffectsClientRpc` |
| การทำนายผลฝั่ง client | ไม่มี | ไม่เกี่ยว | หุ่นต่อกันด้วย joint และฟิสิกส์ต่อเนื่อง การ rollback ฟิสิกส์แบบนี้ไม่คุ้ม และเกม co-op ทนดีเลย์ได้ |

ownership ของ NetworkObject แต่ละชิ้นบอกแค่ว่าใครเป็นคนส่ง input ของชิ้นนั้น ไม่ได้ย้ายการจำลองไปที่ client (คอมเมนต์ใน `RespawnManager.cs:186-190` ยืนยันเรื่องนี้) ลำดับตั้งแต่กดปุ่มจนถึงจอทุกคนอยู่ในแผนภาพ 12

---

## H. สถาปัตยกรรมที่แนะนำ

เจ็ดโมดูล ขนาดพอดีกับโปรเจกต์ ลูกศรชี้จากผู้ใช้ไปยังโมดูลที่ถูกใช้

| โมดูล | หน้าที่ | สิ่งที่อยู่ในนี้ | สิ่งที่ไม่ควรอยู่ในนี้ | พึ่งพา | สื่อสารผ่าน |
|---|---|---|---|---|---|
| **Core** | คำศัพท์ที่ใช้ร่วมกัน | `DamageInfo`, `DamageSource`, `IDamageable`, `DamageRouter`, `Team`, `LimbSlot`, `GameplayGate`, `UiFocus`, `MouseWheelFocus`, `InputCompat`, `Vector3Extensions`, `NetworkAuthority` | ลอจิกของฉาก และ MonoBehaviour ที่มี state | Unity, NGO | helper แบบ static, struct |
| **Robot** | หุ่นและแขนขา | `Robot`, `RobotLimb`, `TorsoBalance`, `FallRules`, `LimbController` (`ArmController`, `LegController`), `ArmGrip`, `ArmInput`, `LegInput`, `LocalAim`, `PunchAbility`, `KickAbility`, `LimbAttachment`, `LimbHealth`, `LimbStrike`, `RobotBodyDamageRelay`, `OrbitCamera` | ลอบบี้ โหมด UI และทีม PvP (ยกเว้นค่า `Team`) | Core | RPC, อีเวนต์, `DamageRouter` |
| **Enemy** | บอส | คลาสบอสปัจจุบัน + อีเวนต์ `Died` | ลอจิกการชนะ | Core, Robot (หาเป้าผ่าน `Robot.All`) | `DamageRouter`, อีเวนต์ |
| **Items** | กระเป๋าและของที่ถือ | คลาสไอเทมปัจจุบัน | การเช็คโหมด (ใช้ `GameplayGate`) | Core, Robot | `DamageRouter`, RPC |
| **Match** | แมตช์หนึ่งแมตช์ในฉากหนึ่งฉาก | `MatchSession`, `LimbSelection`, `PvpMatch`, `LimbControlBinder`, `BossModeRules`, `ParkourModeRules`, `PvpModeRules`, `RespawnManager`, checkpoint, `SceneNavigator`, `GameScenes` | เลย์เอาต์ของ UI | Core, Robot, Enemy, Services | NetworkVariable, อีเวนต์ |
| **Services** | ระบบที่อยู่ตลอดอายุแอป | `SessionService`, `GameSettings`, `VoiceChat`, `GraphicsQualityService`, `GameAudio` | เกมเพลย์ | NGO, UGS | อีเวนต์, เรียกตรง |
| **UI** | ทุกอย่างที่ผู้เล่นเห็น | เมนู แผงลอบบี้ หน้าเลือกทีม PvP HUD หน้าผลลัพธ์ เมนูระหว่างเล่น แผงตั้งค่า แผงสอนเล่น วงล้อไอเทม `UiPanelAnimator`, `ButtonFeedback` | กติกา การบันทึกค่า ลอจิกของ session | ทุกโมดูลข้างบน (อ่าน + ส่งคำขอ) | ฟังข้อมูล เรียก `Request*` |

ทางเลือกหลังย้ายโฟลเดอร์แล้ว: ใช้ Assembly Definition หนึ่งตัวต่อโมดูล ถ้าสคริปต์ใน Robot อ้างคลาส UI จะคอมไพล์ไม่ผ่าน ทิศของ dependency จึงถูกบังคับไปในตัว และคอมไพล์ได้เร็วขึ้นด้วย

### การวางโค้ดแบบ Unity

| เรื่อง | ตอนนี้ | ที่แนะนำ |
|---|---|---|
| Input | `Update()` ใน NetworkBehaviour ของแขนขา ปน `Input.*` กับ `InputCompat` | คอมโพเนนต์ `*Input` ที่ทำงานเฉพาะเจ้าของ และมีแหล่งกำหนดปุ่มที่เดียว (asset ของ Input System ซึ่งติดตั้งอยู่แล้ว หรือ asset `KeyBindings` ที่อ่านผ่าน `InputCompat`) |
| กติกาเกม | ฟิสิกส์แขนขาใน `FixedUpdate` ฝั่ง server (ดีแล้ว) แต่กติกาของโหมดอยู่ในตัวจัดการ UI | คอมโพเนนต์ `*Rules` ในโมดูล Match |
| state ระหว่างเล่น | `NetworkVariable` (ดีแล้ว) แต่เป็น public | `NetworkVariable` แบบ private + property |
| การแสดงผล | widget HUD (ดีแล้ว) แต่ crosshair แบบ IMGUI อยู่ในคลาสแขนขา | crosshair เป็น widget ของ HUD ที่อ่าน `LocalAim` (Optional) |
| ฟิสิกส์ | จำลองบน server อย่างเดียว (ดีแล้ว) แต่การสลับ kinematic ฝั่ง client อยู่ใน `LobbyManager` | `Robot.SetSimulationEnabled` สั่งโดยเฟสของแมตช์ |
| ข้อมูล | ไอเทมเป็น ScriptableObject (ดีแล้ว) แต่ค่าจูนการกระแทกซ้ำกันบนคอมโพเนนต์ และข้อความสอนเล่นอยู่ในโค้ด | asset `StrikeTuning`, `GameScenes`, `TutorialContent` |
| การบันทึกค่า | คีย์ PlayerPrefs ใน 8 ไฟล์ | `GameSettings` |
| เครือข่าย | โค้ด session อยู่ในเมนู UI | `SessionService` |
| วงจรชีวิต | bootstrap ด้วย `RuntimeInitializeOnLoadMethod` พร้อมรีเซ็ต static (ดีแล้ว) | เก็บไว้สำหรับ service ระดับแอป |

การเลือกชนิด: ใช้ `MonoBehaviour` กับของที่มี transform ฟิสิกส์ หรือการผูกผ่าน Inspector ใช้ `NetworkBehaviour` เฉพาะที่ต้อง sync state หรือใช้ RPC ใช้คลาส C# ธรรมดาหรือ static กับลอจิกล้วน (`DamageRouter`, `DamageInfo`, `GameSettings`, `SceneNavigator`, `FallRules`) และใช้ ScriptableObject กับค่าตั้งและเนื้อหาที่ใช้ร่วมกัน coroutine ยังเหมาะกับลำดับเหตุการณ์ตามเวลาบน server (ท่าโจมตีบอส ท่าไม้ตาย) ส่วน `async`/`await` ใช้กับการเรียก UGS ต่อไป โดยให้ `async void` อยู่เฉพาะใน event handler ของ UI และครอบด้วย `try/catch`

---

## I. UML Class Diagram

แผนภาพในหัวข้อ I ถึง K ในไฟล์นี้เขียนเป็นโค้ด Mermaid เพื่อให้อ่านเป็นข้อความได้ (ฉบับรูปวาดด้วยสัญลักษณ์ UML มาตรฐาน) ไฟล์ที่เปิดแก้ใน draw.io ได้คือ `uml/nsc-architecture.drawio` (หนึ่งหน้าต่อหนึ่งแผนภาพ) รูป SVG อยู่ในโฟลเดอร์ `uml/` และรูป PNG ขนาด 2 เท่าอยู่ใน `uml/png/`

กล่อง class แบ่งเป็น 3 ช่อง ได้แก่ ชื่อคลาส ฟิลด์ และเมธอด ด้านบนชื่อมี stereotype บอกชนิด: `<<NetworkBehaviour>>`, `<<Interface>>`, `<<Struct>>`, `<<Enumeration>>`, `<<ScriptableObject>>`, `<<Static>>` และ `<<Plain C#>>` คือคลาส C# ธรรมดาที่เทสได้โดยไม่ต้องเปิด Unity กล่องที่ไม่มี stereotype คือ MonoBehaviour ชื่อคลาสตัวเอียงคือ abstract class เครื่องหมายหน้าสมาชิกคือ `+` public, `-` private, `#` protected ส่วน `{static}`, `{abstract}`, `{override}` และ `{server only}` (ค่าที่อยู่บน server เท่านั้นและไม่ sync) เขียนต่อท้าย กล่องมุมพับคือโน้ตอธิบาย

ทุกแผนภาพมีป้ายสถานะที่มุมซ้ายบน เพื่อไม่ให้ใครเข้าใจผิดว่าของที่เสนอมีอยู่แล้วในโค้ด:

- **โค้ดปัจจุบัน** (ป้ายสีขาว, แผนภาพ 1) วาดจากโค้ดตอนนี้
- **ข้อเสนอ** (ป้ายสีฟ้า, แผนภาพ 2 ถึง 14) ยังไม่มีในโค้ด ในกล่องคลาสมีป้ายใต้ชื่อบอกที่มา: `{เดิม: X}` แปลว่าส่วนนี้มาจากคลาส X ในโค้ดตอนนี้, `{ใหม่}` ยังไม่มีเลย, `{มีอยู่แล้ว}` ใช้ของเดิม และ `{มีอยู่แล้ว · ปรับ}` ของเดิมที่แก้เล็กน้อย ชื่อเดิมละเอียดกว่านี้ดูได้ในหัวข้อ D
- **พฤติกรรมเกม** (ป้ายสีเขียว, แผนภาพ 15 ในหัวข้อ L) สิ่งที่ผู้เล่นเห็น ซึ่งต้องเหมือนเดิมทั้งก่อนและหลังรีแฟกเตอร์

ข้อเสนอเปลี่ยนโครงสร้างข้างในได้ แต่ต้องได้ผลที่ผู้เล่นเห็นเหมือนเดิม โน้ตจึงมีสองสี **โน้ตสีเขียว** คือกติกาและตัวเลขที่คงไว้เหมือนเกมตอนนี้ ตัวเลขมาจาก prefab ที่ฉากเกมใช้จริง (`Yelmee/RobotContainer.prefab` ซึ่งเป็น variant ของ `Yelmee/Robotc DDD/RobotContainer.prefab`) ไม่ใช่ค่า default ในโค้ด เพราะหลายค่าต่างกันมาก เช่น `maxTorsoStress` เป็น 50 ไม่ใช่ 500 **โน้ตสีส้ม** คือจุดที่โค้ดตอนนี้น่าจะมีบั๊ก แต่ถ้าแก้ผู้เล่นจะรู้สึกต่าง ข้อเสนอจึงเก็บแบบเดิมไว้เป็นค่าเริ่มต้น และทีมต้องลองเล่นเทียบก่อนเปลี่ยน

| เส้น | ชื่อ | ความหมาย |
|---|---|---|
| เส้นทึบ หัวสามเหลี่ยมโปร่ง | Inheritance | สืบทอดคลาส (IS-A) |
| เส้นประ หัวสามเหลี่ยมโปร่ง | Realization | implement อินเทอร์เฟซ |
| เส้นทึบ หัวลูกศรเปิด | Association | ถือ reference หรือเรียกใช้ประจำ |
| เส้นประ หัวลูกศรเปิด | Dependency | ใช้ชั่วคราว เช่น เรียก static หรือฟังอีเวนต์ |
| เส้นทึบ ข้าวหลามตัดทึบที่ฝั่งเจ้าของ | Composition | เป็นเจ้าของ ส่วนย่อยอยู่และหายไปพร้อมเจ้าของ |
| เส้นทึบ ข้าวหลามตัดโปร่งที่ฝั่งเจ้าของ | Aggregation | รวมไว้ แต่แต่ละชิ้นมีอายุของตัวเอง |
| เส้นประสีเทา ไม่มีหัวลูกศร | Note | ต่อโน้ตอธิบายเข้ากับคลาส |
| เส้นประสีแดง | ทิศที่ผิด | dependency ที่ไม่ควรมี (เฉพาะแผนภาพ 1) |
| เส้นโค้งข้ามเส้นอื่น | Line hop | สองเส้นแค่ตัดผ่านกัน ไม่ได้เชื่อมกัน |
| กรอบโน้ตสีเขียว | คงเดิม | กติกาและตัวเลขที่ต้องเหมือนเกมตอนนี้ |
| กรอบโน้ตสีส้ม | ต้องตัดสิน | ถ้าทำตามข้อเสนอ ผู้เล่นจะรู้สึกต่าง ค่าเริ่มต้นยังเป็นแบบเดิม |

### แผนภาพ 1 — ภาพรวมโมดูล: โครงสร้างปัจจุบัน

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/hl-current.png`)

```mermaid
flowchart TB
  %% สถานะ: โค้ดปัจจุบัน (สิ่งที่มีในโปรเจกต์ตอนนี้)
  flow["<b>Flow & UI</b><br/>LobbyManager<br/>GameFlowManager (static GameEnded)<br/>ParkourFlowManager<br/>LocalRobotBinder + HUD"]
  items["<b>Items</b><br/>HandItemHolder<br/>GunHeldItem<br/>ChargeGunHeldItem<br/>Projectile"]
  robot["<b>Robot limbs</b><br/>TorsoMovement<br/>PlayerHandMovement<br/>PlayerHandCombat<br/>PlayerFootForRobot<br/>PlayerLegCombat"]
  pvp["<b>PvP</b><br/>PvpTeamManager<br/>PvpRobotTeam<br/>PvpDamageSender"]
  limb["<b>Limb health & damage</b><br/>RobotHealth<br/>JointPullAndReconnect<br/>PhysicsDamageSender"]
  enemy["<b>Enemy</b><br/>EnemyController<br/>EnemyCombat (+ IHittable, AttackType)<br/>EnemyHealth<br/>EnemyUltimate"]
  robot -.->|"LobbyManager, GameEnded"| flow
  flow -.->|"หาแขนขาด้วยชื่อ GameObject"| robot
  items -.->|"LobbyManager, GameEnded"| flow
  enemy -.->|"TriggerVictory()"| flow
  items -.->|"IsFighting, FindByPart"| pvp
  items -.->|"EnemyHealth"| enemy
  enemy -.->|"FindFirstObjectByType"| robot
  enemy -.->|"RobotHealth"| limb
  limb -.->|"IHittable, AttackType"| enemy
  pvp -.->|"RobotHealth"| limb
  pvp -.->|"ผูกกล้องและ controller"| robot
  robot -.->|"อ่าน JointPull, RobotHealth"| limb
  limb -.->|"JointPull เขียนสถานะ controller"| robot
  linkStyle 0,2,3,4,8,12 stroke:#e5534b,color:#e5534b
```
แต่ละกล่องคือกลุ่มสคริปต์ตามโฟลเดอร์ในโปรเจกต์ตอนนี้ เส้นสีแดงคือ dependency ทิศที่ไม่ควรมี คือเกมเพลย์ชี้ไปหา UI หรือคลาสของโหมด หรือวนกลับกัน (รายการเต็มอยู่ในหัวข้อ F)

### แผนภาพ 2 — ภาพรวมโมดูล: โครงสร้างที่เสนอ

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/hl-proposed.png`)

```mermaid
flowchart TB
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  ui["<b>UI</b><br/>MainMenuUI, RoomPanel<br/>LimbSelectionPanel, PvpTeamSelectUI<br/>HUD widgets, MatchResultPanel<br/>InMatchMenu, SettingsPanel, TutorialPanel"]
  match["<b>Match</b><br/>MatchSession<br/>LimbSelection, PvpMatch<br/>LimbControlBinder<br/>Boss / Parkour / PvpModeRules<br/>RespawnManager, SceneNavigator"]
  services["<b>Services</b><br/>SessionService<br/>GameSettings<br/>VoiceChat<br/>GraphicsQualityService, GameAudio"]
  enemy["<b>Enemy</b><br/>EnemyController<br/>EnemyCombat<br/>EnemyHealth (+ Died)<br/>EnemyUltimate"]
  robot["<b>Robot</b><br/>Robot, RobotLimb<br/>TorsoBalance + FallRules<br/>LimbController: Arm, Leg (+ ArmGrip)<br/>Arm/LegInput, LocalAim<br/>Punch/KickAbility, LimbStrike<br/>LimbAttachment, LimbHealth"]
  items["<b>Items</b><br/>PlayerInventory<br/>HandItemHolder<br/>HeldItem + subclasses<br/>Projectile"]
  core["<b>Core</b><br/>DamageInfo, IDamageable, DamageRouter<br/>GameplayGate<br/>LimbSlot, Team<br/>UiFocus, InputCompat"]
  ui -.-> match
  ui -.-> services
  ui -.->|"อ่านสถานะ Robot, Enemy, Items"| robot
  match -.-> robot
  match -.-> enemy
  match -.-> services
  match -.-> core
  items -.-> robot
  enemy -.-> robot
  robot -.-> core
  enemy -.-> core
  items -.-> core
```
dependency ไหลทางเดียวจากบนลงล่าง UI อ่านได้ทุกอย่างแต่ไม่มีใครพึ่ง UI และ Core ไม่พึ่งใครเลย

### แผนภาพ 3 — Robot aggregate และวงจรชีวิตของแขนขา

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/robot-core.png`)

```mermaid
classDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  direction LR
  class Robot {
    <<ใหม่>>
    -Team team
    -TorsoBalance torso
    -RobotLimb[4] limbs
    +event LimbStateChanged
    +GetLimb(LimbSlot slot) RobotLimb
    +Limbs() IEnumerable~RobotLimb~
    +DetachedCount() int
    +SetSimulationEnabled(bool on) void
    +ServerResetForRespawn(Pose spawn) void
    +FromCollider(Collider hit)$ Robot
    -OnLimbStateChanged(RobotLimb limb) void
  }
  class LimbSlot {
    <<enumeration · เดิม LimbStatusUI.LimbSlot>>
    LeftArm
    RightArm
    LeftLeg
    RightLeg
  }
  class RobotLimb {
    <<ใหม่>>
    -LimbSlot slot
    -Robot owner
    -LimbAttachment attachment
    -LimbHealth health
    -LimbController controller
    +bool IsAttached
    +event StateChanged
    +ServerApplyDamage(DamageInfo info) bool
    +ServerForceDetach() void
    -OnHealthDepleted() void
    -OnAttachmentChanged(bool attached) void
  }
  class LimbAttachment {
    <<NetworkBehaviour · เดิม JointPullAndReconnect>>
    -NetworkVariable~bool~ isAttached
    -Rigidbody socket
    -SavedJointSettings savedJoint
    -float = 3 reconnectCooldown
    -float = 2 reconnectDistance
    +bool IsAttached
    +event AttachmentChanged
    +ServerDetach() void
    +RequestPullBackRpc(bool held) void
    -ServerReconnect() void
  }
  class SavedJointSettings {
    <<struct · เดิม SavedConfigurableJointSettings>>
    +Vector3 anchor, connectedAnchor
    +Vector3 axis, secondaryAxis
    +motions, limits, drives
  }
  class LimbHealth {
    <<NetworkBehaviour · เดิม RobotHealth>>
    -NetworkVariable~float~ hp
    -NetworkVariable~float~ maxHp
    +event Depleted
    +ServerTakeDamage(float amount) void
    +ServerDeplete() void
    +ServerRefill() void
    -Regenerate() void
  }
  class IDamageable {
    <<interface · เดิม IHittable>>
    +Kind() DamageTargetKind
    +Team() Team
    +ServerApplyDamage(DamageInfo info) bool
  }
  class RobotBodyDamageRelay {
    <<เดิม PvpRobotTeam>>
    -Robot robot
    +ServerApplyDamage(DamageInfo info) bool
    -NearestAttachedLimb(Vector3 p) RobotLimb
  }
  class TorsoBalance {
    <<NetworkBehaviour · เดิม TorsoMovement>>
    -NetworkVariable~TorsoState~ state
    +ServerKnockDown(FallReason reason) void
    +ServerResetForRespawn() void
  }
  class LimbController {
    <<abstract NetworkBehaviour · ใหม่>>
    #RobotLimb limb
    #Rigidbody body
    +ServerResetForRespawn()* void
  }
  Robot *-- "1" TorsoBalance
  Robot *-- "4" RobotLimb
  RobotLimb --> LimbSlot
  RobotLimb *-- LimbController
  RobotLimb *-- LimbAttachment
  RobotLimb *-- LimbHealth
  LimbAttachment *-- SavedJointSettings
  IDamageable <|.. RobotLimb
  IDamageable <|.. RobotBodyDamageRelay
  RobotBodyDamageRelay --> Robot : หาชิ้นที่ใกล้สุด
  note for RobotLimb "กติการะดับชิ้น เหมือนโค้ดตอนนี้ (server):\nชิ้นที่หลุดไม่รับดาเมจ และไม่มีเอฟเฟคโดนตี\nHp = 0 หรือโดนท่าไม้ตายบอส → หลุด + เพดานเลือดลด\nต่อกลับ → เลือดเต็มตามเพดานปัจจุบัน\nทุกเครื่อง: ยิง StateChanged(this)"
  note for RobotBodyDamageRelay "เหมือน PvP ตอนนี้:\nลำตัวไม่มีเลือดของตัวเอง\nดาเมจที่ลำตัวลงชิ้นที่ใกล้จุดปะทะที่สุด\nและยังต่ออยู่ (ไม่หารให้ทุกชิ้น)"
  note for LimbAttachment "หลุดและดึงกลับแบบเดิม:\nหลุด = ทำลาย joint อย่างเดียว ตัวชิ้นและฟิสิกส์ยังอยู่\nขาที่หลุดยังคลานตามจุดเล็ง แขนที่หลุดนิ่ง\nกด R ค้างได้หลังหลุด 3 วิ ต่อเมื่อห่าง socket ≤ 2 ม."
  note for LimbHealth "ค่าจาก prefab ที่ฉากเกมใช้จริง:\nเลือดเริ่ม 500 หลุดแต่ละครั้งเพดานลด 125 (ต่ำสุด 50)\nไม่โดนตี 7.5 วิ แล้ว regen 2 ต่อวิ ไม่เกินเพดาน\nโดนตีแล้วชิ้นนั้นกระเด็น (แรง = ดาเมจ × 2)"
```
หุ่นเป็นเจ้าของลำตัวและแขนขา 4 ชิ้น `RobotLimb` เป็นตัวแทนของชิ้นนั้นและเป็นที่อยู่ของกติการะดับชิ้น (รับดาเมจเฉพาะตอนต่ออยู่ เลือดหมดแล้วหลุด ต่อกลับแล้วเติมเลือด) `LimbHealth` กับ `LimbAttachment` จึงไม่ต้องรู้จักกัน สถานะ "ต่ออยู่ไหม" มีเจ้าของคนเดียวคือ `LimbAttachment` ส่วนดาเมจที่ลำตัวลงชิ้นที่ใกล้สุดผ่าน `RobotBodyDamageRelay` ผู้ที่ implement `IDamageable` ฝั่งหุ่นคือ `RobotLimb` ไม่ใช่ `LimbHealth` เพราะชิ้นที่หลุดต้องปัดดาเมจทิ้งเหมือนที่ `RobotHealth.CanTakeDamage` ทำตอนนี้ (`RobotHealth.cs:173-182`) ถ้าให้ `LimbHealth` รับดาเมจตรง ชิ้นที่หลุดจะโดนตีจนเพดานเลือดลดซ้ำ ซึ่งเป็นบั๊กที่โค้ดตอนนี้แก้ไปแล้ว

### แผนภาพ 4 — การทรงตัวของลำตัว

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/balance.png`)

```mermaid
classDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  direction LR
  class TorsoBalance {
    <<NetworkBehaviour · เดิม TorsoMovement>>
    -NetworkVariable~TorsoState~ state
    -float stress «server only»
    -FallReason lastFall «server only»
    -Robot robot
    -FallRules rules
    -FallLimits limits
    +TorsoState State
    +ServerKnockDown(FallReason reason) void
    +ServerAddStress(float amount) void
    +ServerApplyJump(LimbSlot from) void
    +ServerApplyRecoveryPush(Vector3 point) void
    +ServerResetForRespawn() void
    -ReadBalanceInput() BalanceInput
    -ApplyStandingSupport(BalanceInput input) void
  }
  class TorsoState {
    <<enumeration · เดิม TorsoMovement.TorsoState>>
    Standing
    Ragdoll
  }
  class FallRules {
    <<plain C# · ใหม่>>
    -FallLimits limits
    -float steppingTimer
    -float offBalanceTimer
    -float airborneTimer
    -float tiltTimer
    +Evaluate(BalanceInput input, float dt) FallReason
    +Reset() void
  }
  class FallLimits {
    <<struct · ใหม่>>
    +float = 50 maxStress
    +float = 45 maxTiltAngle
    +float = 0.25 tiltGrace
    +float = 0.15 bothSteppingGrace
    +float = 0.15 offBalanceGrace
    +float = 0.5 airborneGrace
    +bool = true countDetachedLegs
  }
  class BalanceInput {
    <<struct · ใหม่>>
    +int feet, groundedFeet, balancedFeet
    +int walkSteppingFeet
    +bool hasSupportFoot, kickActive
    +bool handSupport, jumpProtected
    +float stress, tiltAngle
  }
  class FallReason {
    <<enumeration · ใหม่>>
    None
    StressOverload
    BothFeetStepping
    BothFeetOffBalance
    AirborneTooLong
    TiltedInAir
    LegDetached
    HitByBlackHole
  }
  class FootContact {
    <<struct · ใหม่>>
    +bool attached
    +bool grounded, balanced
    +bool walkStepping, kicking
    +bool canSupport
    +Vector3 position
  }
  class Robot {
    <<ใหม่>>
    +Limbs() IEnumerable~RobotLimb~
  }
  class LegController {
    <<NetworkBehaviour · เดิม PlayerFootForRobot>>
    +FootContact Contact
  }
  class ArmController {
    <<NetworkBehaviour · เดิม PlayerHandMovement>>
    +float ClimbPull
  }
  class ArmGrip {
    <<NetworkBehaviour · เดิม PlayerHandMovement>>
    +bool IsSupporting
  }
  TorsoBalance --> TorsoState
  note for TorsoState "ผู้เล่นไม่เห็นความต่าง:\nตัด Falling ออก เพราะในโค้ดตอนนี้มันอยู่แค่ 1 FixedUpdate\nและทุกจุดที่อ่านถือว่าเท่ากับ Ragdoll\nลุกได้ทางเดียวเหมือนเดิม: กด Q ระหว่าง Ragdoll"
  note for TorsoBalance "stress และ lastFall อยู่บน server เท่านั้น\nclient ไม่ได้ตัดสินการล้ม จึงอ่านแค่ state\nแขนขาเรียกลำตัวได้ 3 ทาง: ServerAddStress,\nServerApplyJump, ServerApplyRecoveryPush"
  note for TorsoBalance "กระโดดและลุกเหมือนเดิม:\nSpace ขาแรก = hop แรง 250 ขาที่สองกดภายใน 0.3 วิ = +450\nความเร็วขาขึ้นไม่เกิน 10 ม./วิ ไม่ว่ากี่ขากด\nQ: แรงลุกคิดครั้งเดียวต่อ tick ไม่ว่ากี่คนกดพร้อมกัน"
  TorsoBalance *-- FallRules
  note for FallRules "กฎล้มเหมือนเกมตอนนี้ (ตัวเลขจาก prefab อยู่ใน FallLimits):\nสองขาก้าวพร้อมกันเกิน 0.15 วิ = ล้ม (กฎหลักของเกม)\nมือที่จับของนิ่ง: กันได้ทุกกฎ รวม stress ≥ 50 (ลดลง 50 ต่อวิ)\nช่วงกระโดด 1.2 วิ และพยุงหลังลงพื้น 0.4 วิ: กันทุกกฎยกเว้น stress\nขาที่ยันพื้นหรือกำลังเตะ: กันเฉพาะกฎทรงตัว\nกฎทรงตัว: เอียงเกิน 45° ตอนไม่มีเท้าแตะพื้น 0.25 วิ,\nสองเท้าลอยและเสียสมดุล 0.15 วิ, ลอยทั้งตัว 0.5 วิ"
  TorsoBalance --> Robot : แขนขาทั้ง 4 ชิ้น
  TorsoBalance ..> LegController : อ่าน Contact
  LegController --> FootContact
  TorsoBalance ..> ArmController : อ่าน ClimbPull
  TorsoBalance ..> ArmGrip : อ่าน IsSupporting
  TorsoBalance ..> BalanceInput
  FallRules ..> BalanceInput : อ่าน
  FallRules --> FallLimits
  FallRules ..> FallReason : คืนค่า
  note for FallLimits "ต้องตัดสินก่อนเปลี่ยน: ขาที่หลุด\nตอนนี้ขาที่หลุดยังถูกนับในกฎล้ม และในแรงดึงลำตัวเข้าหากลางเท้า\nมีผลกับความรู้สึกตอนเหลือขาข้างเดียว\ncountDetachedLegs = true คือแบบเดิม (ค่าเริ่มต้น)\nfalse นับเฉพาะขาที่ต่ออยู่ ต้องลองเล่นเทียบก่อน"
  note for ArmController "ต้องลองเล่นเทียบ: แรงดึงตอนปีน\nตอนนี้แขนสองข้างเขียนค่าเดียวกันทุก tick\nลำตัวใช้ค่าของแขนที่รันทีหลัง (ขึ้นกับลำดับสคริปต์)\nที่เสนอ: ใช้ ClimbPull ที่มากกว่าของสองแขน"
```
`TorsoBalance` ไม่เก็บรายชื่อแขนขาเอง ทุก physics tick มันอ่านแขนขาทั้ง 4 ชิ้นผ่าน `Robot` มาเป็น `BalanceInput` แล้วให้ `FallRules` (คลาส C# ธรรมดา) ตัดสินว่าล้มไหมและเพราะอะไร กติกาการล้มจึงเทสได้โดยไม่ต้องเปิด Unity และตอบได้เสมอว่าหุ่นล้มเพราะกฎข้อไหน ตัวเลขใน `FallLimits` เป็นค่าจาก prefab ที่ใช้จริง (stress 50, เอียง 45° นาน 0.25 วินาที, ลอย 0.5 วินาที, สองขาก้าวพร้อมกัน 0.15 วินาที) โน้ตสีส้มสองอันคือบั๊กที่น่าจะไม่ได้ตั้งใจแต่มีผลกับความรู้สึก: ขาที่หลุดยังถูกนับ (ข้อ B แถว 25) และแรงดึงตอนปีนเป็นของแขนที่รันทีหลัง (แถว 26) ข้อเสนอเก็บข้อแรกไว้เหมือนเดิม (`countDetachedLegs = true`) ส่วนข้อหลังเก็บแบบเดิมไม่ได้เพราะผลขึ้นกับลำดับสคริปต์ จึงเสนอให้ใช้ค่ามากสุดและลองปีนเทียบ

### แผนภาพ 5 — การควบคุมแขนขา

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/limb-control.png`)

```mermaid
classDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  direction LR
  class ArmInput {
    <<NetworkBehaviour · เดิม PlayerHandMovement>>
    -ArmController controller
    -ArmGrip grip
    -LocalAim aim
    -ReadOwnerInput() void
  }
  class LocalAim {
    <<เดิม PlayerHandMovement>>
    -Vector3 aimOffset
    -Vector2 virtualCursor
    +Accumulate(Vector2 delta) void
    +AimNormalized() Vector2
  }
  class LegInput {
    <<NetworkBehaviour · เดิม PlayerFootForRobot>>
    -LegController controller
    -LocalAim aim
    -ReadOwnerInput() void
  }
  class LimbController {
    <<abstract NetworkBehaviour · ใหม่>>
    #RobotLimb limb
    #Rigidbody body
    #Transform pivot
    +ServerResetForRespawn()* void
    #DriveToward(Vector3 target, float speed, float damper) void
    #ProtectJointChain() void
  }
  class ArmGrip {
    <<NetworkBehaviour · เดิม PlayerHandMovement>>
    -FixedJoint joint
    -Rigidbody held
    +bool IsSupporting
    +RequestGrabRpc() void
    +RequestReleaseRpc() void
    +ServerRelease() void
    -IgnoreTorsoCollision(bool on) void
  }
  class ArmController {
    <<NetworkBehaviour · เดิม PlayerHandMovement>>
    -Vector3 target
    -ArmMotionOverride motionOverride
    -ArmGrip grip
    +float ClimbPull
    +SetHandTargetRpc(Vector3 target) void
    +RequestRecoveryPushRpc() void
    +SetMotionOverride(ArmMotionOverride o) void
    +ServerResetForRespawn() void
    -DriveHand() void
    -PullTorsoTowardGrip() void
  }
  class LegController {
    <<NetworkBehaviour · เดิม PlayerFootForRobot>>
    -LegMode mode
    -Vector3 target
    -Vector3 balanceShift
    -bool stepping
    -bool recoveryHeld
    +FootContact Contact
    +SetFootTargetRpc(Vector3 target) void
    +SetBalanceShiftRpc(Vector3 point) void
    +SetSteppingRpc(bool on) void
    +SetRecoveryHeldRpc(bool on) void
    +JumpRpc() void
    +ServerResetForRespawn() void
    -ResolveMode() LegMode
    -DriveForMode(LegMode mode) void
  }
  class LegMode {
    <<enumeration · ใหม่>>
    Planted
    Stepping
    Kicking
    Limp
    PushingUp
    Detached
  }
  class ArmMotionOverride {
    <<struct · ใหม่>>
    +Vector3 target
    +float velocityCap
    +float damper
    +float brakeScale
    +float extraReach
  }
  class PunchAbility {
    <<NetworkBehaviour · เดิม PlayerHandCombat>>
    -NetworkVariable~CombatState~ state
    -float peakSpeed
    +RequestPunchStartRpc() void
    +RequestPunchReleaseRpc() void
    +CanDealDamage() bool
    +PeakSpeed() float
  }
  class KickAbility {
    <<NetworkBehaviour · เดิม PlayerLegCombat>>
    -NetworkVariable~LegActionState~ action
    -float peakSpeed
    +bool ControlsFoot
    +RequestChargeRpc(Vector3 aim) void
    +RequestReleaseRpc(Vector3 aim) void
    +CanDealDamage() bool
    +PeakSpeed() float
  }
  class IStrikeSource {
    <<interface · ใหม่>>
    +CanDealDamage() bool
    +PeakSpeed() float
    +Source() DamageSource
    +OnStrikeLanded() void
    +OnStrikeBlocked() void
  }
  class LimbStrike {
    <<เดิม PhysicsDamageSender + PvpDamageSender>>
    -IStrikeSource source
    -StrikeTuning tuning
    -OnCollisionEnter(Collision c) void
    -BuildDamageInfo(Collision c) DamageInfo
  }
  ArmInput --> LocalAim
  LegInput --> LocalAim
  note for LegInput "ผู้เล่นปกติไม่เห็นความต่าง:\nRPC ทุกตัวเป็น owner-only ตัวรับแค่ตรวจค่าแล้วเก็บคำสั่ง\nส่งเมื่อเป้าขยับเกินเกณฑ์ ฟิสิกส์รันบน server ใน FixedUpdate\nไม่มี client prediction เหมือนตอนนี้"
  ArmInput --> ArmGrip : RPC
  ArmInput --> ArmController : RPC
  LegInput --> LegController : RPC
  LimbController <|-- ArmController
  LimbController <|-- LegController
  ArmController --> ArmGrip
  ArmController --> ArmMotionOverride
  LegController --> LegMode
  note for LegController "ขาที่หลุดคิดแบบเดิม:\nResolveMode() ครั้งเดียวต่อ tick จาก attachment, ท่าเตะ,\nลำตัวล้มไหม และคำสั่งล่าสุด\nขาที่หลุด: canSupport = false\nแต่ grounded และ balanced ยังคิดตามฟิสิกส์"
  note for LegMode "ต้องตัดสินก่อนเปลี่ยน: แฟล็กก้าวค้าง\nตอนนี้ถ้าขาหลุดตอนผู้เล่นกดคลิกซ้ายค้างอยู่\nstepping ของขานั้นค้าง และยังนับในกฎสองขาก้าวพร้อมกัน\nที่เสนอเก็บแบบเดิมไว้ก่อน ล้างตอนหลุดเมื่อทีมยืนยันว่าเป็นบั๊ก"
  PunchAbility --> ArmController : ตั้ง override
  PunchAbility ..> ArmMotionOverride : สร้าง
  KickAbility --> LegController : ขับเท้าตอนเตะ
  IStrikeSource <|.. PunchAbility
  IStrikeSource <|.. KickAbility
  LimbStrike --> IStrikeSource
```
ฝั่งเจ้าของอ่าน input แล้วส่ง RPC แบบ owner-only ฝั่ง server ตรวจค่าแล้วขับฟิสิกส์ `ArmController` กับ `LegController` ใช้ฐานเดียวกัน (`LimbController`) ขามีโหมดเดียวต่อ tick การจับแยกเป็น `ArmGrip` ส่วนท่าต่อยกับเตะเป็น ability ที่ประกอบเข้ากับตัวควบคุม และ implement `IStrikeSource` ให้ `LimbStrike` อ่าน ปุ่มทุกปุ่มและจังหวะส่งคำสั่งเหมือนเดิม (โน้ตสีเขียว) ขาที่หลุดยังคิด grounded และ balanced ตามฟิสิกส์เหมือนตอนนี้ ส่วนแฟล็ก stepping ที่ค้างเมื่อขาหลุดตอนกดคลิกซ้าย (แถว 25) เก็บไว้ก่อนจนกว่าทีมยืนยันว่าเป็นบั๊ก

### แผนภาพ 6 — เส้นทางดาเมจ

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/damage.png`)

```mermaid
classDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  direction LR
  class DamageRouter {
    <<static · ใหม่>>
    +TryApply(Collider hit, DamageInfo info) bool
    -IsAllowed(IDamageable target, DamageInfo info) bool
  }
  class DamageInfo {
    <<struct · ใหม่>>
    +float amount
    +float knockback
    +Vector3 direction
    +Vector3 point
    +DamageSource source
    +Team attackerTeam
    +ulong attackerRobotId
    +bool detachLimb
  }
  class IDamageable {
    <<interface · เดิม IHittable>>
    +Kind() DamageTargetKind
    +Team() Team
    +ServerApplyDamage(DamageInfo info) bool
  }
  class GameplayGate {
    <<static · ใหม่>>
    +bool CanAct
    +bool CanDamage
  }
  class DamageSource {
    <<enumeration · เดิม AttackType>>
    Punch
    Kick
    MeleeWeapon
    Projectile
    EnemyMelee
    EnemyUltimate
  }
  class DamageTargetKind {
    <<enumeration · ใหม่>>
    RobotLimb
    Enemy
    Structure
  }
  class Team {
    <<enumeration · เดิม PvpTeam>>
    None
    Red
    Blue
    Enemy
  }
  class StrikeTuning {
    <<ScriptableObject · ใหม่>>
    +float = 8 minVelocity
    +float = 0.6 punchSpeedToDamage
    +float = 1.2 kickSpeedToDamage
    +float = 60 maxDamagePerHit
    +AnimationCurve knockbackBySpeed
  }
  class LimbStrike {
    <<เดิม PhysicsDamageSender + PvpDamageSender>>
    -IStrikeSource source
    -StrikeTuning tuning
    -OnCollisionEnter(Collision c) void
  }
  class Projectile {
    <<มีอยู่แล้ว ปรับ>>
    -float speed
    -float damage
    -Team shooterTeam
    -Update() void
    -HandleHit(RaycastHit hit) void
  }
  class EnemyCombat {
    <<NetworkBehaviour · มีอยู่แล้ว ปรับ>>
    -EnemyAttackDefinition[] attacks «ใหม่»
    +ServerExecuteAttack(EnemyAttack type) float
    -ApplyHitToNearestPart() void «เดิม ProcessHitDetection»
  }
  class BlackHoleProjectile {
    <<มีอยู่แล้ว ปรับ>>
    -float damageRadius
    -float buildingDamage
    -float playerDamage
    -DamageAlong(Vector3 from, Vector3 to) void
  }
  class RobotLimb {
    <<ใหม่>>
    -LimbHealth health
    +bool IsAttached
    +ServerApplyDamage(DamageInfo info) bool
  }
  class EnemyHealth {
    <<NetworkBehaviour · มีอยู่แล้ว ปรับ>>
    +NetworkVariable~float~ CurrentHp
    +event Died «ใหม่»
    +ServerApplyDamage(DamageInfo info) bool «เดิม ServerTakeHit»
    -ServerKnockbackRoutine() IEnumerator
  }
  class DestructibleBuilding {
    <<NetworkBehaviour · เดิม explobuilding>>
    -float health
    -bool isDestroyed
    +ServerApplyDamage(DamageInfo info) bool
    -Collapse() void
  }
  class RobotBodyDamageRelay {
    <<เดิม PvpRobotTeam>>
    -Robot robot
    +ServerApplyDamage(DamageInfo info) bool
    -NearestAttachedLimb(Vector3 p) RobotLimb
  }
  class PunchBag {
    <<NetworkBehaviour · มีอยู่แล้ว ปรับ>>
    -Rigidbody rb
    +ServerApplyDamage(DamageInfo info) bool «เดิม ServerTakeDamage»
  }
  LimbStrike --> StrikeTuning
  LimbStrike ..> DamageRouter
  Projectile ..> DamageRouter
  EnemyCombat ..> DamageRouter
  BlackHoleProjectile ..> DamageRouter
  DamageRouter ..> DamageInfo
  DamageRouter ..> GameplayGate : เช็คเฟส
  DamageInfo --> DamageSource
  DamageInfo --> Team
  DamageRouter ..> IDamageable
  IDamageable --> DamageTargetKind
  IDamageable --> Team
  IDamageable <|.. RobotLimb
  IDamageable <|.. RobotBodyDamageRelay
  IDamageable <|.. EnemyHealth
  IDamageable <|.. DestructibleBuilding
  IDamageable <|.. PunchBag
  RobotBodyDamageRelay --> RobotLimb
```
ผู้โจมตีทุกตัวสร้าง `DamageInfo` แล้วเรียก `DamageRouter` ที่เดียว ผู้รับทุกชนิด implement `IDamageable` กติกาทีมและเฟสของแมตช์จึงอยู่ที่เดียว สูตรดาเมจเดิมย้ายค่าเข้า `StrikeTuning` (ความเร็วพีคขั้นต่ำ 8 ม./วินาที ตัวคูณหมัด 0.6 เตะ 1.2 เพดาน 60 ต่อครั้ง) โน้ตสีส้มคือสิ่งที่ต้องวัดใน Play Mode ก่อนย้าย เพราะแขนขาใน PvP ตอนนี้มีตัวส่งดาเมจสองตัว (ข้อ B แถว 2) จึงยังไม่รู้แน่ว่าหมัดหนึ่งลดเลือดเท่าไหร่

### แผนภาพ 7 — แมตช์ ลอบบี้ และ session

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/match.png`)

```mermaid
classDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  direction LR
  class MatchSession {
    <<NetworkBehaviour · ใหม่>>
    -NetworkVariable~MatchPhase~ phase
    -NetworkVariable~MatchResult~ result
    -float startTime
    +event PhaseChanged
    +MatchPhase Phase
    +ElapsedTime() float
    +ServerBeginPlaying() void
    +ServerEnd(MatchResult result) void
  }
  class MatchPhase {
    <<enumeration · ใหม่>>
    Preparing
    Playing
    Ended
  }
  class MatchResult {
    <<struct · ใหม่>>
    +MatchOutcome outcome
    +Team winningTeam
    +float time
    +float height
  }
  class GameplayGate {
    <<static · ใหม่>>
    +bool CanAct
    +bool CanDamage
  }
  class LimbSelection {
    <<NetworkBehaviour · เดิม LobbyManager>>
    -NetworkList~LimbAssignment~ assignments
    -Dictionary seatExpiry
    +event AssignmentsChanged
    +RequestLimbRpc(ulong robotId, LimbSlot slot) void
    +RequestStartRpc() void
    +GetControllerClientId(ulong robotId, LimbSlot slot) ulong
  }
  class LimbAssignment {
    <<struct · เดิม LobbyManager>>
    +ulong robotId
    +LimbSlot slot
    +ulong clientId
  }
  class PvpMatch {
    <<NetworkBehaviour · เดิม PvpTeamManager>>
    -NetworkList~PvpPlayerEntry~ players
    +RequestTeamRpc(Team team) void
    +GetTeam(ulong clientId) Team
  }
  class LimbControlBinder {
    <<เดิม LobbyManager>>
    -LimbSelection selection
    -OnAssignmentsChanged() void
    -Bind(Robot robot, LimbSlot slot) void
  }
  class LocalPlayerRobot {
    <<เดิม LocalRobotBinder>>
    +Robot Robot
    +LimbSlot Slot
    +event Bound
  }
  class BossModeRules {
    <<NetworkBehaviour · เดิม GameFlowManager>>
    -EnemyHealth boss
    -Robot robot
    -OnBossDied() void
    -EvaluateDefeat() void
  }
  class ParkourModeRules {
    <<NetworkBehaviour · เดิม ParkourFlowManager>>
    -Robot robot
    -float peakHeight
    +ServerReachGoal() void
    -TrackHeight() void
  }
  class PvpModeRules {
    <<NetworkBehaviour · เดิม PvpRobotTeam>>
    -Robot red
    -Robot blue
    -EvaluateDefeat() void
  }
  class MatchResultPanel {
    <<เดิม GameFlowManager>>
    -MatchSession session
    -OnPhaseChanged(MatchPhase phase) void
    -Show(MatchResult result) void
  }
  class SceneNavigator {
    <<static · ใหม่>>
    +RestartCurrent() void
    +ExitToMenu() void
  }
  class GameScenes {
    <<ScriptableObject · ใหม่>>
    +string menu
    +string boss
    +string parkour
    +string pvp
  }
  class SessionService {
    <<เดิม OnlineNetworkUI + ReturnToMenuOnHostLost>>
    -ISession session
    +event StateChanged
    +HostAsync(SessionOptions options) Task
    +JoinAsync(string code) Task
    +LeaveAsync() Task
  }
  BossModeRules --> MatchSession
  ParkourModeRules --> MatchSession
  PvpModeRules --> MatchSession
  MatchSession --> MatchPhase
  MatchSession --> MatchResult
  MatchSession ..> GameplayGate : เขียนคนเดียว
  LimbSelection --> MatchSession : เริ่มแมตช์
  LimbSelection *-- "*" LimbAssignment
  PvpMatch --> LimbSelection : ช่องของทีม
  LimbControlBinder ..> LimbSelection : ติดตาม
  LimbControlBinder --> LocalPlayerRobot : ผูก
  MatchResultPanel ..> MatchSession : ติดตาม
  MatchResultPanel ..> SceneNavigator
  SceneNavigator --> GameScenes
  SceneNavigator ..> SessionService : ออกจากห้อง
  note for PvpModeRules "ชนะแพ้เหมือนเดิม:\nบอส: บอสตาย = ชนะ, แขนขาหลุดพร้อมกันครบ 4 = แพ้ (เช็คทุก 1 วิ)\nPvP: หุ่นที่แขนขาหลุดครบ 4 แพ้ (เช็คทุก 0.25 วิ)\nพาร์คัวร์: ชิ้นไหนของหุ่นแตะเส้นชัย = จบ แสดงเวลาและความสูงสูงสุด\nต่อชิ้นกลับทันก่อนรอบเช็ค = ยังไม่แพ้"
  note for MatchResultPanel "เมื่อจบแมตช์ เหมือนเดิม:\nแขนขาเลิกรับ input และปลดเคอร์เซอร์\nหน้าผลลัพธ์ขึ้นทุกเครื่อง\nปุ่มเล่นใหม่และออกเป็นของ host"
```
`MatchSession` เป็นเจ้าของเฟสของแมตช์คนเดียว กติกาของแต่ละโหมดเป็นคอมโพเนนต์แยก และ co-op กับ PvP ใช้ระบบแจกแขนขาชุดเดียวกัน เงื่อนไขชนะแพ้และจังหวะการเช็คเหมือนเดิม (บอสเช็คทุก 1 วินาที PvP ทุก 0.25 วินาที)

### แผนภาพ 8 — ไอเทม (แทบไม่เปลี่ยน)

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/items.png`)

```mermaid
classDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  direction LR
  class ItemDefinition {
    <<ScriptableObject · มีอยู่แล้ว>>
    +string displayName
    +Sprite icon
    +GameObject heldPrefab
    +GameObject worldPrefab
  }
  class ItemDatabase {
    <<ScriptableObject · มีอยู่แล้ว>>
    -List~ItemDefinition~ items
    +GetIndex(ItemDefinition item) int
    +GetByIndex(int index) ItemDefinition
  }
  class PlayerInventory {
    <<NetworkBehaviour · มีอยู่แล้ว>>
    -NetworkList~int~ slotItemIndices
    -NetworkVariable~int~ equippedIndex
    +event OnSlotsChanged
    +event OnEquippedChanged
    +RequestEquip(int index) void
    +RequestDrop(Vector3 position, Vector3 direction) void
    +TryAddServerSide(ItemDefinition item, int out index) bool
  }
  class HandItemHolder {
    <<NetworkBehaviour · มีอยู่แล้ว ปรับ>>
    -Transform holdPoint
    +HeldItem Current
    +Hold(ItemDefinition definition) void
    -ReadOwnerInput() void «ใหม่»
  }
  class HeldItem {
    <<มีอยู่แล้ว ปรับ>>
    +ItemDefinition Definition
    +HandItemHolder Holder
    +OnEquipped() void
    +OnUseStart() void
    +OnUseHold(float dt) void
    +OnUseEnd() void
    +ServerOnUseStart() void «ใหม่»
    +ServerOnUseRelease(float heldSeconds) void «ใหม่»
  }
  class ChargeGunHeldItem {
    <<มีอยู่แล้ว ปรับ>>
    -float maxChargeTime
    -float minDamage
    -float maxDamage
    +ServerOnUseRelease(float heldSeconds) void «ใหม่»
    +ExecuteFire(FireData data) void
  }
  class SwordHeldItem {
    <<มีอยู่แล้ว ปรับ>>
    -float damageMultiplier
    -FixedJoint joint
    +OnEquipped() void
    +CanDealDamage() bool «ใหม่»
    +PeakSpeed() float «ใหม่»
  }
  class GunHeldItem {
    <<มีอยู่แล้ว ปรับ>>
    -float damage
    -float fireRate
    +ServerOnUseStart() void «ใหม่»
  }
  class Projectile {
    <<มีอยู่แล้ว ปรับ>>
    -float speed
    -float damage
    -Team shooterTeam
    -HandleHit(RaycastHit hit) void
  }
  class IStrikeSource {
    <<interface · ใหม่>>
    +CanDealDamage() bool
    +PeakSpeed() float
    +Source() DamageSource
    +OnStrikeLanded() void
    +OnStrikeBlocked() void
  }
  class WorldItem {
    <<มีอยู่แล้ว>>
    -ItemDefinition definition
    +List~WorldItem~ Active$
    +ConsumeFromWorld() void
  }
  class ItemPickupInteractor {
    <<NetworkBehaviour · มีอยู่แล้ว>>
    -PlayerInventory inventory
    +WorldItem Focused
    -RefreshFocus() void
    -PickupRpc(NetworkObjectReference item) void
  }
  class DamageRouter {
    <<static · ใหม่>>
    +TryApply(Collider hit, DamageInfo info) bool
  }
  ItemPickupInteractor --> PlayerInventory
  PlayerInventory --> ItemDatabase
  ItemDatabase o-- "*" ItemDefinition
  ItemPickupInteractor ..> WorldItem : เล็งอยู่
  PlayerInventory --> HandItemHolder : ของที่ถือ
  HandItemHolder *-- HeldItem : 0..1
  HeldItem --> ItemDefinition
  WorldItem --> ItemDefinition
  HeldItem <|-- ChargeGunHeldItem
  HeldItem <|-- SwordHeldItem
  HeldItem <|-- GunHeldItem
  IStrikeSource <|.. SwordHeldItem
  ChargeGunHeldItem ..> Projectile : สร้าง
  Projectile ..> DamageRouter
  GunHeldItem ..> DamageRouter
```
โครงเดิมใช้ได้ดีอยู่แล้ว สิ่งที่เปลี่ยนคือ `HeldItem` มี hook ฝั่ง server (`HandItemHolder` จะได้เลิก cast เป็นปืนชาร์จ) และอาวุธทำดาเมจผ่าน `DamageRouter`

---

## J. แผนภาพการสื่อสาร

sequence diagram อ่านจากบนลงล่างตามเวลา เส้นทึบหัวทึบคือการเรียกเมธอดหรือ RPC เส้นทึบหัวเปิดคืออีเวนต์หรือค่าที่ sync และเส้นประคือค่าที่ส่งกลับ ป้ายใต้ชื่อผู้เข้าร่วมบอกว่าโค้ดส่วนนั้นรันที่ไหน (server, owner client, ทุกเครื่อง หรือ static ซึ่งคือคลาสล้วนที่ไม่มีอินสแตนซ์)

### แผนภาพ 9 — หมัดโดนบอส บอสตาย แมตช์จบ

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/seq-punch.png`)

```mermaid
sequenceDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  autonumber
  participant owner as เจ้าของแขน (client)
  participant arm as PunchAbility (server)
  participant strike as LimbStrike (server)
  participant router as DamageRouter (static)
  participant boss as EnemyHealth (server)
  participant rules as BossModeRules (server)
  participant match as MatchSession (server)
  participant ui as MatchResultPanel (ทุกเครื่อง)
  owner->>arm: RequestPunchStartRpc()
  arm->>arm: ตั้ง ArmMotionOverride ทุก FixedUpdate
  Note over strike: OnCollisionEnter
  strike->>arm: CanDealDamage, PeakSpeed
  strike->>router: TryApply(collider, info)
  router->>router: เช็ค GameplayGate และกติกาทีม
  router->>boss: ServerApplyDamage(info)
  router-->>strike: true
  strike->>arm: OnStrikeLanded()
  boss-)rules: Died
  rules->>match: ServerEnd(Victory)
  match-)ui: Phase = Ended
```
ดาเมจเดินผ่าน `DamageRouter` จุดเดียว บอสแค่ประกาศอีเวนต์ `Died` โดยไม่รู้ว่าอยู่ในโหมดไหน กติกาของโหมดเป็นคนจบแมตช์

### แผนภาพ 10 — เลือกแขนขา เริ่มแมตช์ ผูกผู้เล่นในเครื่อง

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/seq-lobby.png`)

```mermaid
sequenceDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  autonumber
  participant player as ผู้เล่น
  participant panel as LimbSelectionPanel (client)
  participant sel as LimbSelection (server)
  participant match as MatchSession (server)
  participant binder as LimbControlBinder (ทุกเครื่อง)
  participant hud as LocalPlayerRobot + HUD (client)
  player->>panel: คลิกขาซ้าย
  panel->>sel: RequestLimbRpc(robotId, LeftLeg)
  sel-)panel: assignments เปลี่ยน
  Note over sel: host กด Start
  sel->>sel: ChangeOwnership ให้ชิ้นที่ถูกจอง
  sel->>match: ServerBeginPlaying()
  match-)binder: PhaseChanged(Playing)
  binder->>binder: ให้กล้องตาม และเปิด LegInput
  binder-)hud: Bound(robot, LeftLeg)
```
co-op และ PvP ใช้เส้นทางเดียวกัน `LimbSelection` เก็บว่าใครคุมชิ้นไหน `MatchSession` ประกาศเฟส แล้ว binder ในแต่ละเครื่องผูกกล้องกับ input เอง

### แผนภาพ 11 — เลือดหมด ขาหลุด หุ่นล้ม แล้วดึงกลับมาต่อ

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/seq-limb-break.png`)

```mermaid
sequenceDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  autonumber
  participant router as DamageRouter (static)
  participant limb as RobotLimb (ทุกเครื่อง)
  participant hp as LimbHealth (server)
  participant att as LimbAttachment (server)
  participant robot as Robot (ทุกเครื่อง)
  participant torso as TorsoBalance (server)
  participant leg as LegController (server)
  participant rules as Mode rules (server)
  router->>limb: ServerApplyDamage(info)
  limb->>hp: ServerTakeDamage(amount) ถ้ายังต่ออยู่
  hp->>hp: Hp = 0 และเพดานเลือดลด 125
  hp-)limb: Depleted
  limb->>att: ServerDetach()
  att-)limb: AttachmentChanged(false) ทุกเครื่อง
  limb-)robot: StateChanged(limb)
  robot->>torso: ServerKnockDown(LegDetached) ถ้าเป็นขา
  robot-)rules: LimbStateChanged(limb)
  rules->>rules: EvaluateDefeat() ในรอบเช็คถัดไป
  Note over leg: tick ถัดไป: mode = Detached
  Note over att: หลุดครบ 3 วิ แล้วผู้เล่นกด R ค้าง: RequestPullBackRpc(true)
  att->>att: ดึงเข้า socket (≤ 2 ม.) แล้ว ServerReconnect()
  att-)limb: AttachmentChanged(true)
  limb->>hp: ServerRefill() เต็มตามเพดานใหม่
```
`RobotLimb` เป็นผู้รับดาเมจของชิ้นและเป็นคนใช้กติกาของชิ้น `LimbHealth` กับ `LimbAttachment` ไม่รู้จักกัน `Robot` สั่งลำตัวล้มพร้อมเหตุผล ลำตัวไม่ต้องถอนทะเบียนขา เพราะ tick ถัดไปมันอ่านแขนขาจาก `Robot` เอง ส่วนการแพ้เป็นหน้าที่ของกติกาโหมด ตารางนี้สรุปว่าแต่ละส่วนของชิ้นเป็นอย่างไรเมื่อหลุดและเมื่อต่อกลับตามโครงที่เสนอ พฤติกรรมและตัวเลขเหมือนเกมตอนนี้ทุกแถว รวมถึงแถวลำตัวที่เก็บพฤติกรรมเดิมของข้อ B แถว 25 ไว้เป็นค่าเริ่มต้น

| ส่วนของชิ้น | เมื่อหลุด | เมื่อต่อกลับ |
|---|---|---|
| joint (`ConfigurableJoint`) | ถูกทำลาย | สร้างใหม่ที่ socket เดิมด้วย `SavedJointSettings` |
| GameObject และ NetworkObject | อยู่ต่อ ไม่ despawn | ไม่เปลี่ยน |
| ฟิสิกส์ | ยังจำลองบน server ชิ้นนั้นกลายเป็นวัตถุอิสระ | กลับไปขับตามโหมดปกติ |
| input ของเจ้าของ | ขาที่หลุดยังคลานตามจุดเล็ง (`LegMode.Detached`) แขนที่หลุดนิ่ง | ไม่เปลี่ยน |
| ลำตัว | ถ้าเป็นขา สั่งล้มด้วย `LegDetached` ขาที่หลุดยันพื้นแทนขาไม่ได้ แต่ยังถูกนับในกฎล้มและในแรงดึงเข้ากลางแบบเดิม (`countDetachedLegs = true`) | ไม่เปลี่ยน |
| เลือด | `RobotLimb` ปัดดาเมจทิ้งและไม่มีเอฟเฟคโดนตี ถ้าหลุดเพราะเลือดหมดหรือโดนท่าที่สั่งให้หลุด เพดานเลือดลด 125 (ต่ำสุด 50) | เติมเต็มตามเพดานใหม่ |
| การดึงกลับ | กด R ค้างได้หลังคูลดาวน์ 3 วินาที ต่อเมื่อห่าง socket ไม่เกิน 2 เมตร | ไม่เกี่ยว |
| กติกาของโหมด | ตรวจการแพ้ เช่น PvP แพ้เมื่อหลุดครบ 4 ชิ้น | ไม่เกี่ยว |

### แผนภาพ 12 — ก้าวเท้าหนึ่งครั้ง จาก input ถึงจอทุกคน

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/seq-step.png`)

```mermaid
sequenceDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  autonumber
  participant player as ผู้เล่นขาซ้าย
  participant input as LegInput (owner client)
  participant leg as LegController (server)
  participant torso as TorsoBalance (server)
  participant rules as FallRules (server)
  participant net as NetworkTransform (server authority)
  participant all as จอของทุกคน (ทุกเครื่อง)
  player->>input: กดคลิกซ้ายค้าง และเล็ง
  input->>leg: SetSteppingRpc(true)
  input->>leg: SetFootTargetRpc(target)
  leg->>leg: NGO รับเฉพาะเจ้าของ แล้ว clamp เป้าในระยะขา
  Note over leg: FixedUpdate
  leg->>leg: mode = Stepping, ขับเท้าด้วยสปริง
  torso->>leg: อ่าน Contact
  torso->>rules: Evaluate(input, dt)
  rules-->>torso: None หรือ BothFeetStepping
  net-)all: ตำแหน่งเท้าและลำตัว
  torso-)all: State (เมื่อเปลี่ยน)
```
เจ้าของขาส่งแค่คำสั่ง server ตรวจคำสั่ง จำลองฟิสิกส์ และตัดสินการล้มคนเดียว ทุกเครื่องได้ผลผ่าน NetworkTransform (AuthorityMode = Server) และ NetworkVariable ไม่มีการทำนายผลฝั่ง client (ตาราง authority อยู่ในหัวข้อ G)

### แผนภาพ 13 — ตกแมพแล้วเกิดใหม่ที่เช็คพอยต์

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/seq-respawn.png`)

```mermaid
sequenceDiagram
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  autonumber
  participant zone as FallDeathZone (server)
  participant mgr as RespawnManager (server)
  participant robot as Robot (server)
  participant ctrl as LimbController ×4 (server)
  participant grip as ArmGrip ×2 (server)
  participant torso as TorsoBalance (server)
  participant owner as เจ้าของแต่ละชิ้น (owner client)
  zone->>mgr: RespawnBody() (กันเรียกซ้ำด้วย cooldown)
  mgr->>robot: ServerResetForRespawn(checkpoint)
  robot->>ctrl: ServerPrepareRespawn()
  ctrl->>grip: ServerRelease() (แขนปล่อยของที่จับเอง)
  robot->>robot: ปลด joint ที่ต่ออยู่ตอนนี้ แล้ว teleport ทุกชิ้น
  robot->>robot: ต่อ joint กลับ
  robot->>ctrl: ServerResetForRespawn()
  ctrl->>owner: ResetAimRpc()
  robot->>torso: ServerResetForRespawn()
  torso->>torso: state = Standing, ล้าง stress และตัวจับเวลา
  Note over robot: แขนขาที่หลุดยังหลุดอยู่ และเลือดคงเดิม (ตามโค้ดตอนนี้)
```
`RespawnManager` ตัดสินว่าเมื่อไหร่และที่ไหน `Robot` ตัดสินว่ารีเซ็ตตัวเองอย่างไรและตามลำดับไหน ชิ้นส่วนแต่ละชิ้นล้าง state ส่วนตัวของตัวเอง `Robot` อ่าน joint ที่ต่ออยู่ตอนนั้นจากแขนขา ไม่ใช้ลิสต์ที่แคชไว้ตอนเริ่มฉาก (ข้อ B แถว 27) การต่อแขนขาที่หลุดคืนและการเติมเลือดไม่ได้ทำตอน respawn ตามโค้ดตอนนี้ ถ้าจะเปลี่ยนเป็นเรื่องที่ทีมต้องเลือก

---

## K. การไหลของข้อมูล

### แผนภาพ 14 — ค่าตั้ง → state → ลอจิก → การแสดงผล

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/dataflow.png`)

```mermaid
flowchart LR
  %% สถานะ: ข้อเสนอ (ยังไม่มีในโค้ด)
  subgraph g_config["ค่าตั้ง (asset และ PlayerPrefs)"]
    strike["StrikeTuning"]
    itemdb["ItemDefinition<br/>ItemDatabase"]
    enemyData["ข้อมูลท่าโจมตีของบอส"]
    scenes["GameScenes"]
    tutorial["TutorialContent"]
    settings["GameSettings<br/>(PlayerPrefs)"]
  end
  subgraph g_logic["ลอจิกบน server"]
    ctrl["Controllers<br/>+ abilities"]
    ai["AI ของบอส"]
    router["DamageRouter"]
    rules["กติกาของโหมด"]
  end
  subgraph g_state["state ที่ sync (server เป็นเจ้าของ)"]
    assign["LimbSelection<br/>assignments"]
    hp["LimbHealth.Hp"]
    att["LimbAttachment<br/>.IsAttached"]
    torso["TorsoBalance.State"]
    ehp["EnemyHealth.Hp"]
    inv["PlayerInventory<br/>slots"]
    phase["MatchSession<br/>Phase / Result"]
  end
  subgraph g_view["แสดงผลบนทุกเครื่อง"]
    hud["widget ของ HUD"]
    panels["เมนูและหน้าผลลัพธ์"]
    fx["VFX / SFX<br/>แอนิเมชัน"]
  end
  itemdb --> inv
  settings -->|"ความไวเมาส์"| ctrl
  strike --> ctrl
  assign -->|"ใครคุมชิ้นไหน"| ctrl
  ctrl --> router
  enemyData --> ai
  ai --> router
  router --> hp
  router --> ehp
  hp --> att
  att --> torso
  att --> rules
  ehp --> rules
  rules --> phase
  inv --> hud
  hp --> hud
  att --> hud
  torso --> hud
  ehp --> hud
  ehp --> fx
  phase --> panels
  scenes --> panels
  tutorial --> panels
```
ค่าตั้งอยู่ใน asset ลอจิกรันบน server state ที่ sync มีเจ้าของคนเดียว และการแสดงผลแค่อ่าน state ไม่แก้เอง

### จุดที่ข้อมูล state และลอจิกปนกันอยู่ตอนนี้

| ประเภท | ตัวอย่างตอนนี้ | คำแนะนำ |
|---|---|---|
| ค่าตั้งที่ซ้ำกัน | `minVelocityThreshold`, `speedToDamage`, `kickSpeedToDamage`, `maxDamagePerHit` อยู่ทั้งใน `PhysicsDamageSender` และ `PvpDamageSender` และ `SwordHeldItem` เขียนทับฟิลด์เหล่านั้นตอนรัน | asset `StrikeTuning` ตัวเดียว ดาบส่งตัวคูณมาแทน |
| ค่าตั้งบนคอมโพเนนต์ที่ใช้ได้อยู่แล้ว | ระยะและน้ำหนักการสุ่มท่าของบอส ค่าคงที่ของสปริงแขนขา | เก็บเป็นฟิลด์ serialize ต่อไป ย้ายค่าจูนแขนขาไปเป็น asset เฉพาะเมื่อมีหุ่นหลายแบบที่ต้องใช้ตัวเลขชุดเดียวกัน |
| เนื้อหาในโค้ด | ข้อความสอนเล่น ชื่อฉากใน 4 ที่ | asset `TutorialContent` และ `GameScenes` |
| เลขมหัศจรรย์ในลอจิก | การ regen ของ `RobotHealth` ที่ `2f` ต่อวินาทีและดีเลย์ `7.5f` วินาที | ทำเป็นฟิลด์ serialize |
| state ที่เก็บสองที่ | การต่ออยู่ของแขนขา (joint และตัวควบคุม), "เกมเริ่มแล้ว" (ลอบบี้, PvP, ตัวจัดการโหมด), รายชื่อเท้าและมือในลำตัวที่ไม่ตามสถานะการหลุด | เจ้าของอย่างละหนึ่ง และอ่านจากเจ้าของแทนการเก็บสำเนา |
| ค่าที่หลายคนเขียนทับ | `armPullIntensity` ที่มือทุกข้างเขียนทุก tick, `currentState` ของลำตัวที่ 3 คลาสเขียน | ผู้เขียนคนเดียว หรือให้ผู้ใช้ค่าอ่านจากทุกแหล่งแล้วรวมเอง (`ClimbPull` ค่ามากสุด) |
| state แบบ static | การเล็งที่แชร์, `GameEnded`, static ของการ reconnect | state ของอินสแตนซ์บนเจ้าของ |
| การบันทึกค่ากระจายอยู่ใน UI | PlayerPrefs ใน 8 ไฟล์ | `GameSettings` |

---

## L. ก่อนและหลัง

| ปัญหาตอนนี้ | ต้นเหตุ | การรีแฟกเตอร์ | ผลลัพธ์ |
|---|---|---|---|
| ลอจิกดาเมจอยู่ 7 ที่ และ PvP มีตัวส่งดาเมจสองตัวแย่งกัน | ไม่มีสัญญาเรื่องดาเมจ | `DamageInfo` + `IDamageable` + `DamageRouter` + `LimbStrike` | กติกาดาเมจ ทีม และเฟสอยู่ที่เดียว บั๊กตัวส่งซ้อนของ PvP หายไปทั้งกลุ่ม |
| `LobbyManager` เป็น God class และเกมเพลย์ต้องอ่านมัน | ลอบบี้ถือทั้ง state เครือข่าย UI และการเตรียมหุ่น | `LimbSelection`, `LimbSelectionPanel`, `LimbControlBinder`, `Robot.SetSimulationEnabled` | input ของแขนขาไม่ต้องพึ่งอ็อบเจกต์ UI อีก |
| หาชิ้นส่วนหุ่นด้วยชื่อใน 9+ ที่ | ไม่มี aggregate `Robot` | `Robot`, `RobotLimb`, `LimbSlot` | อ้างอิงชัดเจน เปลี่ยนชื่อ GameObject แล้ว PvP ไม่พัง |
| แฟล็ก "เริ่ม/จบแมตช์" 4 ตัว | ไม่มีเจ้าของเฟสของแมตช์ | `MatchSession` + `GameplayGate` | มีคำตอบเดียวว่าผู้เล่นทำอะไรหรือทำดาเมจได้หรือยัง |
| สถานะการต่ออยู่ถูกเก็บสองที่ | joint เขียนสถานะของตัวควบคุม | ตัวควบคุมอ่าน `LimbAttachment` | HUD การเช็คแพ้ และฟิสิกส์ไม่หลุด sync กัน |
| หุ่นล้มโดยไม่รู้ว่าเพราะกฎข้อไหน และลำตัวยังนับขาที่หลุด | กฎการล้มปนกับแรงพยุง และใช้รายชื่อเท้าที่ลงทะเบียนตอน spawn | `FallRules` + `FallReason` + อ่านแขนขาจาก `Robot` ทุก tick | เทสกฎการล้มได้ใน EditMode และ log บอกเหตุผลทุกครั้งที่ล้ม การนับขาที่หลุดยังเหมือนเดิม (`countDetachedLegs = true`) จนกว่าทีมจะลองเล่นแล้วตัดสิน |
| respawn พึ่ง joint ที่แคชไว้ตอนเริ่มฉาก | `RespawnManager` รู้จักทุกคลาสของหุ่นและแคช joint ตอน `Awake` | `Robot.ServerResetForRespawn` อ่าน joint ปัจจุบันจากแขนขา และเรียก `LimbController` ทีละชิ้น | แขนขาที่เคยหลุดแล้วต่อกลับ respawn ได้ถูกต้อง และ `RespawnManager` ไม่ต้องรู้จักคลาสของหุ่น |
| การแจกแขนขาของ co-op และ PvP ถูกเขียนสองรอบ | สร้างแยกกันตามโหมด | `LimbSelection` + `LimbControlBinder` ที่ใช้ร่วมกัน | แก้การผูกกล้องและการควบคุมครั้งเดียวได้ทั้งสองโหมด |
| ตัวจัดการโหมดที่ก๊อปกัน และชื่อฉากเมนู 4 แบบ | กติกา UI และการเปลี่ยนฉากอยู่ในคลาสเดียว | `*ModeRules`, `MatchResultPanel`, `SceneNavigator`, `GameScenes` | เพิ่มโหมดใหม่ = เพิ่มคอมโพเนนต์กติกาหนึ่งตัว |
| สิบคลาสแย่งกันตั้งเคอร์เซอร์ | ไม่มีเจ้าของ | `UiFocus` เป็นเจ้าของนโยบายเคอร์เซอร์ | เคอร์เซอร์ในเมนู วงล้อ และหน้าผลลัพธ์คาดเดาได้ |
| แขนใช้ inheritance แบบแก้ฟิลด์ ขาใช้ composition | สองแพทเทิร์นสำหรับแนวคิดเดียว | `PunchAbility` + `ArmMotionOverride` | แขนกับขาอ่านแบบเดียวกัน |
| helper ของ DOTween ที่ก๊อปกัน | ไม่มีคอมโพเนนต์ UI ที่ใช้ร่วมกัน | `UiPanelAnimator`, `ButtonFeedback`, `MenuParallax` | โค้ดลดลงราว 400 บรรทัด และ CLAUDE.md เลิกแนะนำให้ก๊อป |
| คีย์ PlayerPrefs ใน 8 ไฟล์ | UI เป็นเจ้าของการบันทึก | `GameSettings` | รายการคีย์อยู่ที่เดียว |
| ข้อความสอนเล่นผิด | เนื้อหาอยู่ในโค้ด ปุ่มกระจายใน 12 คลาส | `TutorialContent` + แหล่งกำหนดปุ่มที่เดียว | ข้อความสร้างจากปุ่มจริง |
| ปุ่มดีบักทำให้แขนขาหลุดไปถึงผู้เล่นจริง | ไม่มีตัวกั้นให้ใช้ได้เฉพาะตอนพัฒนา | `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` และ RPC แบบ owner-only | build ที่ปล่อยจริงไม่มีช่องโกง |
| โฟลเดอร์ตามชื่อคน และชื่อโฟลเดอร์ที่ไม่เหมาะสม | โตมาแบบไม่ได้วางแผน | โฟลเดอร์ตามฟีเจอร์ และ namespace root เดียว | หาของง่าย path ใน log สะอาด |
| คลาสและฟิลด์ที่ไม่ได้ใช้ | ไม่เคยเก็บกวาด | ลบ | อ่านน้อยลงและเข้าใจผิดน้อยลง |

### แผนภาพ 15 — พฤติกรรมที่ผู้เล่นเห็น: ลำตัว แขนขา และแมตช์

แผนภาพนี้เขียนเป็นโค้ด Mermaid (มาจากโมเดลเดียวกับรูป `png/gameplay.png`)

```mermaid
flowchart TB
  %% สถานะ: พฤติกรรมที่ผู้เล่นเห็น (ต้องเหมือนเดิมก่อนและหลังรีแฟกเตอร์)
  subgraph g_torso["ลำตัว (ทั้งหุ่น)"]
    direction LR
    t0((" "))
    tStand("<b>Standing</b><br/>ลอยพยุงตัว ดึงลำตัวเข้าหากลางเท้า<br/>เดิน กระโดด จับ ต่อย เตะ")
    tRag("<b>Ragdoll</b><br/>ล้มตามฟิสิกส์ ลุกเองไม่ได้<br/>ทุกคนกด Q ช่วยกันดันตัวขึ้นได้")
    nFall>"Standing → Ragdoll เมื่อ:<br/>สองขาก้าวพร้อมกันเกิน 0.15 วิ (กฎหลักของเกม)<br/>stress ≥ 50 (ชน โดนตี) เว้นแต่มีมือจับของนิ่ง<br/>เอียงเกิน 45° ตอนไม่มีเท้าแตะพื้น 0.25 วิ<br/>สองเท้าลอยและเสียสมดุล 0.15 วิ หรือลอยทั้งตัว 0.5 วิ<br/>(กฎทรงตัวพักไว้ถ้ามีขายัน กำลังเตะ หรือจับของนิ่ง)<br/>ขาหลุด หรือโดนหลุมดำของบอส"]
    nJump>"กระโดด (อยู่ใน Standing):<br/>Space ขาแรก = hop, ขาที่สองกดภายใน 0.3 วิ = co-op jump<br/>ความเร็วขึ้นไม่เกิน 10 ม./วิ พักกฎล้มจากเท้า 1.2 วิ"]
  end
  subgraph g_limb["แขนขาแต่ละชิ้น"]
    direction LR
    l0((" "))
    lAtt("<b>ต่ออยู่</b><br/>รับดาเมจ และ regen 2 ต่อวิ<br/>หลังไม่โดนตี 7.5 วิ")
    lDet("<b>หลุด</b><br/>ไม่รับดาเมจ ขาคลานตามจุดเล็ง แขนนิ่ง<br/>ดึงกลับได้หลังหลุด 3 วิ")
    lPull("<b>กำลังดึงกลับ</b><br/>สปริงดึงเข้า socket เร็วสุด 25 ม./วิ")
    nLimb>"ทุกครั้งที่หลุด เพดานเลือดของชิ้นลด 125 (ต่ำสุด 50)<br/>ขาหลุด → ลำตัวเป็น Ragdoll ทันที"]
  end
  subgraph g_match["แมตช์"]
    direction LR
    m0((" "))
    mPrep("<b>เตรียม</b><br/>จองแขนขา (co-op) หรือเลือกทีม (PvP)")
    mPlay("<b>กำลังเล่น</b><br/>พาร์คัวร์: ตกแมพ = เกิดที่เช็คพอยต์<br/>ชิ้นที่หลุดยังหลุด เลือดเท่าเดิม")
    mEnd("<b>จบแมตช์</b><br/>หน้าผลลัพธ์ขึ้นทุกเครื่อง แขนขาหยุดรับ input<br/>host กดเล่นใหม่หรือออก")
    nMatch>"เช็คแพ้เป็นรอบ: บอสทุก 1 วิ PvP ทุก 0.25 วิ<br/>ต่อชิ้นกลับทันก่อนรอบเช็ค = ยังไม่แพ้<br/>พาร์คัวร์ไม่มีแพ้ จบที่เส้นชัยพร้อมเวลาและความสูง"]
  end
  t0 --> tStand
  tStand -->|"ล้ม (เงื่อนไขในโน้ต)"| tRag
  tRag -->|"กด Q จนลำตัวเจอพื้น หรือ respawn"| tStand
  tRag -.- nFall
  tStand -.- nJump
  l0 --> lAtt
  lAtt -->|"เลือดหมด หรือท่าไม้ตายบอส"| lDet
  lDet -->|"เจ้าของชิ้นกด R ค้าง"| lPull
  lPull -->|"ปล่อย R"| lDet
  lPull -->|"ถึง socket ≤ 2 ม. แล้วเลือดเต็มตามเพดาน"| lAtt
  lDet -.- nLimb
  m0 --> mPrep
  mPrep -->|"host กด Start"| mPlay
  mPlay -->|"ชนะ: บอสตาย / ถึงเส้นชัย / อีกทีมหลุดครบ 4"| mEnd
  mPlay -->|"แพ้: แขนขาหลุดพร้อมกันครบ 4"| mEnd
  mEnd -.- nMatch
```
รีแฟกเตอร์ทั้งหมดในรีวิวนี้เปลี่ยนแค่โครงสร้างข้างใน ไม่เปลี่ยนสิ่งที่ผู้เล่นเห็น แผนภาพนี้เป็น state machine ของสิ่งที่ผู้เล่นรู้สึกได้ วาดจากสคริปต์และค่าใน prefab ที่ฉากเกมใช้จริง ไม่ขึ้นกับโครงสร้างคลาส จึงใช้เป็นสัญญาได้ว่าหลังรีแฟกเตอร์ทุกสถานะ ทุกเส้น และทุกตัวเลขต้องเหมือนเดิม และใช้เป็น checklist ของขั้น 0 ในหัวข้อ M ได้ทันที

### สิ่งที่ผู้เล่นรู้สึกได้ ตอนนี้และในข้อเสนอ

| สิ่งที่ผู้เล่นรู้สึก | ตอนนี้อยู่ที่ | ในข้อเสนออยู่ที่ | ผล |
|---|---|---|---|
| ปุ่มและความไวของการบังคับ | input ใน `Update()` ของ `PlayerHandMovement` และ `PlayerFootForRobot` ส่ง RPC เมื่อเป้าขยับเกินเกณฑ์ แขนขาในเครื่องเดียวกันใช้จุดเล็งร่วม | `ArmInput`, `LegInput`, `LocalAim` ส่ง RPC แบบเดิม เพิ่มแค่ owner-only | เหมือนเดิม |
| การทรงตัวและการล้ม | กฎใน `TorsoMovement` ค่าจาก prefab: stress 50, เอียงเกิน 45° นาน 0.25 วินาที, ลอย 0.5 วินาที, สองขาก้าวพร้อมกัน 0.15 วินาที | `FallRules` + `FallLimits` ค่าเดียวกัน | เหมือนเดิม |
| ขาที่หลุดในกฎล้ม | ยังถูกนับในกฎล้มและในแรงดึงลำตัวเข้ากลาง (ข้อ B แถว 25) | `countDetachedLegs = true` | ต้องตัดสิน ค่าเริ่มต้นเหมือนเดิม |
| แฟล็กก้าวค้างเมื่อขาหลุดตอนกดคลิกซ้าย | ค้าง และนับในกฎสองขาก้าวพร้อมกัน (แถว 25) | เก็บไว้แบบเดิม | ต้องตัดสิน ค่าเริ่มต้นเหมือนเดิม |
| กระโดดและ co-op jump | `TorsoMovement.NotifyFootJump` hop แรง 250 ขาที่สองกดภายใน 0.3 วินาทีได้ +450 ความเร็วขึ้นไม่เกิน 10 ม./วินาที | `TorsoBalance.ServerApplyJump` | เหมือนเดิม |
| ลุกจาก ragdoll ด้วย Q | แรงลุกคิดครั้งเดียวต่อ tick ไม่ว่ากี่คนกด | `TorsoBalance.ServerApplyRecoveryPush` | เหมือนเดิม |
| จับด้วย F ค้าง | มือที่จับของนิ่งกันการล้มได้ทุกกฎ | `ArmGrip.IsSupporting` | เหมือนเดิม |
| แรงดึงลำตัวตอนปีน | `armPullIntensity` เป็นค่าของแขนที่รันทีหลัง (แถว 26) | `ArmController.ClimbPull` ค่าที่มากกว่าของสองแขน | ต่าง ต้องลองปีนเทียบ |
| ต่อยและเตะ | `PlayerHandCombat`, `PlayerLegCombat` + `PhysicsDamageSender` ความเร็วพีค ≥ 8 ตัวคูณ 0.6 และ 1.2 เพดาน 60 หนึ่งหมัดหนึ่งครั้ง | `PunchAbility`, `KickAbility`, `LimbStrike` + `StrikeTuning` ค่าเดียวกัน | เหมือนเดิม |
| ดาเมจต่อหมัดใน PvP | ตัวส่งดาเมจสองตัวบนชิ้นเดียว (ข้อ B แถว 2) | `LimbStrike` ตัวเดียว | ต้องยืนยันใน Play Mode ก่อนตั้งค่า |
| ดาเมจนอกช่วงเล่น | `PhysicsDamageSender` ไม่เช็คเฟส ส่วน `PvpDamageSender` เช็ค `IsFighting` | `GameplayGate.CanDamage` | ต่าง เฉพาะกรณีขอบ เช่นตีกันตอนเลือกทีม |
| เลือดของชิ้น | `RobotHealth` 500 หลุดแล้วเพดานลด 125 (ต่ำสุด 50) regen 2 ต่อวินาทีหลังไม่โดนตี 7.5 วินาที | `LimbHealth` | เหมือนเดิม |
| ชิ้นที่หลุดไม่รับดาเมจ | `RobotHealth.CanTakeDamage` | `RobotLimb.ServerApplyDamage` | เหมือนเดิม |
| หลุดและดึงกลับ | `JointPullAndReconnect` กด R ค้างหลังหลุด 3 วินาที ต่อเมื่อห่าง socket ไม่เกิน 2 เมตร แล้วเลือดเต็มตามเพดาน | `LimbAttachment` + `RobotLimb` | เหมือนเดิม |
| ขาหลุดแล้วล้ม | `JointPullAndReconnect.HandleDisconnection` ตั้งลำตัวเป็น Falling | `Robot` เรียก `TorsoBalance.ServerKnockDown(LegDetached)` | เหมือนเดิม |
| ตีลำตัวใน PvP | `PvpRobotTeam.ServerApplyBodyDamage` ลงชิ้นที่ใกล้สุดที่ยังต่ออยู่ | `RobotBodyDamageRelay` | เหมือนเดิม |
| ฟิสิกส์และเครือข่าย | server จำลองคนเดียว `NetworkTransform` แบบ Server ไม่มี client prediction | ไม่เปลี่ยน | เหมือนเดิม |
| ชนะและแพ้ | `GameFlowManager` (แขนขาหลุดพร้อมกันครบ 4 เช็คทุก 1 วินาที), `PvpRobotTeam` (หลุดครบ 4 เช็คทุก 0.25 วินาที), `ParkourFlowManager` (แตะเส้นชัย) | `BossModeRules`, `PvpModeRules`, `ParkourModeRules` + `MatchSession` | เหมือนเดิม |
| respawn ในพาร์คัวร์ | `RespawnManager` ชิ้นที่หลุดยังหลุด เลือดเท่าเดิม | `Robot.ServerResetForRespawn` | เหมือนเดิม |

---

## M. แผนการรีแฟกเตอร์

ทุกขั้นทำให้เกมยังเล่นได้อยู่ ทำทีละขั้นต่อหนึ่ง branch และทดสอบเล่นก่อน merge

| ขั้น | อะไรเปลี่ยน | ทำไม | ต้องทำอะไรก่อน | Risk | ทดสอบอะไรหลังทำ |
|---|---|---|---|---|---|
| **0. เก็บพฤติกรรมปัจจุบัน** | ใช้แผนภาพ 15 และตารางในหัวข้อ L เป็นฐาน เขียน checklist ทดสอบเล่นของแต่ละโหมด (เดิน กระโดดสองคน ต่อย เตะ จับและปีน แขนขาหลุดและกด R ดึง ตกแมพแล้ว respawn เก็บของและยิง หน้าชนะและแพ้ การต่อกลับ) เพิ่ม EditMode test ให้ฟังก์ชันบริสุทธิ์ที่จะแตะ โดยดู `Assets/Editor/ItemAuthorityTests.cs` เป็นตัวอย่าง และยืนยันปัญหาข้อ 2 ด้วยการใส่ log ในตัวส่งดาเมจทั้งสองตัว แล้วต่อยหุ่นอีกตัวระหว่างเลือกทีม | รู้พฤติกรรมเดิมก่อนเปลี่ยน | ไม่มี | None | ตัว checklist เอง |
| **1. เก็บกวาด** | เปลี่ยนชื่อโฟลเดอร์ที่ไม่เหมาะสม (ทำใน Editor) ลบโค้ดที่ไม่ได้ใช้ในแถว 23 และลบการเรียก `RobotTeam` ใน `PhysicsDamageSender` (มันคืน true เสมอ) ครอบ `DebugLimbBreaker` และ `DebugRequestBreakRpc` ด้วย `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` และตั้งแฟล็กดีบักเป็น false รวมชื่อฉากไว้ในคลาส `GameScenes` ที่เดียว อัปเดต CLAUDE.md | ลดสิ่งรบกวน ปิดช่องโกง แก้ชื่อฉากเมนูที่ไม่ตรงกัน | ขั้น 0 | Low | คอมไพล์ผ่าน ทุกซีนใน build โหลดได้ ไม่มี missing script บน prefab |
| **2. aggregate ของ Robot** | เพิ่ม `Robot` และ `RobotLimb` ที่อ้างอิงแบบ serialize (ปุ่ม "Auto-fill" ใน editor ใช้กติกาชื่อเดิมได้ครั้งเดียวตอนแก้ไข) แทนการค้นใน `LocalRobotBinder`, `LobbyManager`, `PvpTeamManager`, `PvpRobotTeam`, `RobotBodyCheck`, `GameFlowManager`, `EnemyController`, `RespawnManager` เพิ่ม `LimbSlot` และบันทึกเรื่องซ้าย/ขวาที่กลับด้านในลอบบี้ไว้ที่เดียว ย้าย "ขาหลุด → ลำตัวล้ม" ไปไว้ใน `Robot` และเพิ่ม `TorsoBalance.ServerKnockDown(reason)` ลบ `HandState`/`FootState` | ทุกขั้นต่อจากนี้ต้องมีทางเข้าถึงแขนขาที่เชื่อถือได้ | ขั้น 1 | Medium | co-op: ทั้ง 4 ช่องคุมแขนขาถูกชิ้น PvP: ทั้งสองทีมทุกช่อง HUD โชว์ชิ้นของเรา แพ้เมื่อหลุดครบ 4 ชิ้น หลุมดำทำให้ล้ม |
| **2b. การทรงตัวและวงจรชีวิตของแขนขา** | แยก `FallRules`, `BalanceInput`, `FallReason` ออกจาก `TorsoMovement` โดยย้ายตัวเลขและลำดับของกฎทั้ง 5 ข้อมาเป๊ะๆ และเขียน EditMode test ให้ทุกกฎ ลำตัวอ่านแขนขาจาก `Robot` แทน `Register*`/`Unregister*` โดยยังนับขาที่หลุดแบบเดิม (`countDetachedLegs = true`) คลาสอื่นสั่งล้มผ่าน `ServerKnockDown(reason)` เท่านั้น แทน `armPullIntensity` ด้วย `ClimbPull` ที่ลำตัวอ่านเอง ย้ายกติกา "เลือดหมด → หลุด → ต่อกลับ → เติมเลือด" ไปที่ `RobotLimb` และให้ `Robot.ServerResetForRespawn` อ่าน joint ปัจจุบันแทนแคชของ `RespawnManager` ใส่ `RpcInvokePermission.Owner` ให้ RPC ของแขนขาที่ส่งไป server ทุกตัว และลบ `TorsoMovement.ApplyRecoveryForceRpc` ที่ไม่มีโค้ดไหนเรียก | แก้แถว 25-28 และทำให้กติกาหลักของเกมเทสได้ | ขั้น 2 | Medium-High (ฟีลการทรงตัว) | EditMode: ทุกกฎล้มที่เงื่อนไขเดิมและไม่ล้มเมื่อมีขายันหรือมือจับ Play Mode: สองคนก้าวพร้อมกันแล้วล้มพร้อม log `BothFeetStepping` ขาหลุดตอนกดคลิกค้างแล้วลุกด้วยขาที่เหลือ ได้ผลเหมือนก่อนรีแฟกเตอร์ ปีนด้วยมือเดียวขณะอีกมือว่าง ต่อแขนกลับแล้วตกแมพให้ respawn client ที่ไม่ใช่เจ้าของส่ง RPC ไปที่ขาไม่ได้ |
| **3. เส้นทางดาเมจ** | เพิ่ม `DamageInfo`, `DamageSource`, `Team`, `IDamageable`, `DamageRouter`, `GameplayGate` (ค่าเริ่มต้นเปิด) ให้ `RobotHealth`, `EnemyHealth` (adapter ครอบ `ServerTakeHit`), `explobuilding`, `PunchBag`, `RobotBodyDamageRelay` implement `IDamageable` เพิ่ม `IStrikeSource` ให้มือ ขา และดาบ รวมตัวส่งดาเมจสองตัวเป็น `LimbStrike` พร้อม asset `StrikeTuning` ที่ก๊อปตัวเลขปัจจุบันมาเป๊ะๆ เอา `PvpDamageSender` ออกจาก `PVP.unity` และ `Sword.prefab` ให้กระสุน ปืน การโจมตีของบอส และหลุมดำ เรียก `DamageRouter` แล้วลบ `IHittable` | แก้ปัญหาข้อ 2 และทำให้กติกาดาเมจแก้ได้ที่เดียว | ขั้น 2 | Medium-High (ฟีลการต่อสู้) | ดาเมจต่อครั้งเท่าเดิม (เทียบ log ของหมัดเดียวกัน) ไม่มี friendly fire ไม่มีดาเมจตอนเลือกทีม หลุมดำพังตึกได้ ตัวคูณของดาบ |
| **4. เฟสของแมตช์** | เพิ่ม `MatchSession` ในทุกฉากเกมเพลย์ โดยเป็นผู้เขียน `GameplayGate` เพียงคนเดียว การเริ่มของลอบบี้และ PvP เรียก `ServerBeginPlaying()` ส่วนแพ้/ชนะเรียก `ServerEnd(result)` แทนการอ่าน `GameEnded`, `GameStarted`, `IsFighting` ในแขนขา ไอเทม และ HUD รวมประตู HUD สองตัวเป็นตัวเดียว และให้ `EnemyHealth` ส่งอีเวนต์ `Died` | มีเจ้าของคำตอบ "แมตช์กำลังเล่นอยู่ไหม" คนเดียว | ขั้น 3 | Medium | จับเวลาเริ่มตอนกด Start HUD ซ่อนจนกด Start อาวุธใช้ไม่ได้ก่อน Start หน้าผลลัพธ์ทุกโหมด เคอร์เซอร์ถูกปล่อยตอนจบ ปุ่มเริ่มใหม่และออก |
| **5. แยก `LobbyManager` และใช้ร่วมกับ PvP** | แยก `LimbSelection` ออกมา (คงเลย์เอาต์ NetworkList เดิม) ตามด้วย `LimbSelectionPanel` และ `LimbControlBinder` การแช่แข็งฟิสิกส์ย้ายไป `Robot.SetSimulationEnabled` ส่วน `PvpTeamManager` เก็บเรื่องทีมไว้และใช้ `LimbSelection` สำหรับช่อง เอา `CanReadCombatInputForThisLimb` ออก เพราะ binder เปิดเฉพาะ input ของแขนขาที่ได้รับ | ปลด God class และตัดความซ้ำซ้อนระหว่าง co-op กับ PvP | ขั้น 4 | High (เครือข่าย) | 2-4 client ด้วย Multiplayer Play Mode (ติดตั้งแพ็กเกจแล้ว) ต่อกลับภายในเวลากันที่นั่งแล้วได้ชิ้นเดิม เข้าห้องช้า host ออก |
| **6. กติกาของโหมดและหน้าผลลัพธ์** | `GameFlowManager` → `BossModeRules` + `MatchResultPanel`; `ParkourFlowManager` → `ParkourModeRules` (วัดความสูง); `PvpResultUI` อ่าน `MatchResult` และใช้ `SceneNavigator` ตัวเดียว | เพิ่มโหมดใหม่ได้ด้วยคอมโพเนนต์เดียว | ขั้น 4 | Low-Medium | ข้อความ เวลา และความสูงบนหน้าผลลัพธ์ของแต่ละโหมด การเริ่มใหม่ของทุก client |
| **7. Input และเคอร์เซอร์** | ย้ายโค้ดฝั่งเจ้าของจากมือและเท้าไปไว้ใน `ArmInput`/`LegInput` โดยไม่แก้เนื้อโค้ด RPC ยังอยู่ที่ตัวควบคุม ใช้ `LocalAim` หนึ่งอินสแตนซ์แทนการเล็งแบบ static ให้ `UiFocus` เป็นโค้ดเดียวที่ตั้ง `Cursor.lockState` รวมการกำหนดปุ่มไว้ที่เดียวและสร้างข้อความสอนเล่นจากตรงนั้น (ถ้าพร้อม) แปลง `PlayerHandCombat` เป็น `PunchAbility` + `ArmMotionOverride` ย้ายโค้ดที่ซ้ำระหว่างแขนกับขาไปที่ `LimbController` แยก `ArmGrip` ออกจากแขน และเปลี่ยนแฟล็กของเท้าเป็น `LegMode` | ขอบเขตระหว่างเจ้าของกับ server ชัดเจน และเลิกแย่งเคอร์เซอร์ | ขั้น 2 | Medium (ฟีลการเล่น) | ฟีลการเล็งเหมือนเดิม (ใช้ harness `_ArmFeelTest`) สถานะเคอร์เซอร์ตอน ESC เมนู วงล้อ และหน้าผลลัพธ์ มือสองข้างยังแชร์จุดเล็ง |
| **8. ค่าตั้ง helper ของ UI และ session** | ใช้ `GameSettings` กับ PlayerPrefs ทั้งหมด ใช้ `UiPanelAnimator`, `ButtonFeedback`, `MenuParallax` แทน helper ที่ก๊อปกัน (อัปเดต CLAUDE.md) ย้ายการเรียก UGS จาก `OnlineNetworkUI` ไป `SessionService` รวม `ReturnToMenuOnHostLost` เข้าไป และแทนฟิลด์ static ด้วย state ของอินสแตนซ์กับอีเวนต์ | ลดความซ้ำ session อยู่รอดข้ามการโหลดฉากได้สะอาด | ขั้น 1 | Low-Medium | ค่าตั้งยังอยู่หลังเปิดเกมใหม่ เมนูเคลื่อนไหวเหมือนเดิม host, join, leave, reconnect |
| **9. โฟลเดอร์ namespace และ asmdef** | ย้ายสคริปต์ไปโฟลเดอร์ตามฟีเจอร์ใน Editor เพิ่ม namespace ตอนย้าย และ (ถ้าต้องการ) เพิ่ม asmdef หนึ่งตัวต่อโมดูล | ทำให้ทิศของ dependency ที่ผิดกลายเป็น compile error | ขั้น 2-8 | Low (ถ้าทำใน Editor) | คอมไพล์ผ่าน ไม่มี missing script ทุกซีนโหลดได้ |

ขั้น 7 และ 8 ทำคู่ขนานกับขั้น 5 และ 6 ได้ ถ้าเวลาน้อย ขั้น 1-4 (รวม 2b) ให้คุณค่าส่วนใหญ่แล้ว เพราะตัดความซ้ำที่ยืนยันแล้ว บั๊กดาเมจ PvP และบั๊กขาที่หลุดที่น่าจะเกิด และการที่เกมเพลย์พึ่ง UI

---

## N. การใช้ Design Pattern

| Pattern | ตำแหน่ง | เหตุผล | ข้อแลก |
|---|---|---|---|
| Aggregate root (composition) | `Robot` + `RobotLimb` | แทนการหาด้วยชื่อและ hierarchy 9+ ที่ | มีคอมโพเนนต์ต้องผูกเพิ่มหนึ่งตัว ปุ่ม auto-fill ใน editor ช่วยได้ |
| ทางเข้าเดียว (facade) | `DamageRouter` | จุดทำดาเมจ 7 ที่ใช้นโยบายเดียวกัน | เป็นคลาส static ต้องเก็บให้เล็กและไม่รู้เรื่องโหมด |
| อินเทอร์เฟซสำหรับผู้รับที่ไม่เกี่ยวกัน | `IDamageable` | ผู้รับห้าชนิด ตัดการ downcast | ผู้รับทุกตัวต้องปรับให้เข้ากับซิกเนเจอร์เดียว |
| Strategy | `IStrikeSource` (หมัด เตะ ดาบ) | ตัด `if/else` ใน 3 ไฟล์ | อินเทอร์เฟซเล็กๆ หนึ่งตัว |
| Observer | `EnemyHealth.Died`, `LimbAttachment.AttachmentChanged`, `MatchSession.PhaseChanged` และ callback ของ `NetworkVariable` ที่มีอยู่ | ผู้ส่งไม่ต้องรู้จักผู้รับ | ผู้ฟังทุกตัวต้องเลิกฟังใน `OnDestroy` หรือ `OnNetworkDespawn` |
| ที่เก็บ state แบบผู้เขียนคนเดียว | `GameplayGate` เขียนโดย `MatchSession` เท่านั้น | แทนแฟล็กที่กระจาย 4 ตัว โดยเกมเพลย์ไม่ต้องพึ่งโมดูล Match | ทุกคนอ่านได้ ต้องเก็บ setter ไว้ภายในโค้ด Match และคอยรีวิวผู้เขียน |
| ค่าตั้งแบบ data-driven | `StrikeTuning`, `GameScenes`, `TutorialContent` และ (ถ้าต้องการ) ค่าจูนแขนขาและ `EnemyAttackDefinition[]` | ตัดค่าจูนที่ซ้ำและข้อความที่ล้าสมัย | มี asset ต้องดูแลเพิ่ม |
| Parameter object | `ArmMotionOverride`, `BalanceInput`, `FootContact` | แทนการแก้ฟิลด์ชั่วคราวในแขน และส่งสถานะของแขนขาให้ลำตัวเป็นค่า | ไม่มีข้อแลกที่น่ากังวล |
| ลอจิกล้วนแยกจาก Unity (functional core) | `FallRules` | กติกาหลักของเกมเทสได้ใน EditMode และคืนเหตุผลของการล้ม | ต้องสร้าง struct ข้อมูลเข้าทุก tick ซึ่งถูกมาก |
| คลาสฐาน (template method) | `LimbController` | โค้ดที่ซ้ำระหว่างแขนกับขา และการรีเซ็ตแบบเดียวกัน | ลำดับชั้นลึกหนึ่งระดับ ห้ามลึกกว่านี้ |
| Adapter | `InputCompat` (มีอยู่แล้ว) | ซ่อน input ระบบเก่ากับใหม่ | ใช้ไปจนกว่าจะย้ายไปใช้ actions |
| Registry | `Robot.All`, `WorldItem.Active` (มีอยู่แล้ว) | หาของได้โดยไม่ต้องค้นฉาก | ต้องล้างตอน domain reload (`WorldItem` ทำแล้ว) |
| State pattern | `TorsoState`, `EnemyState`, `CombatState`, `LegActionState` | **ไม่ต้องใช้ Pattern** enum กับ `switch` เล็กพอ และต้อง serialize ใน `NetworkVariable` ได้ | — |
| Command | การกระทำของผู้เล่น | **ไม่ต้องใช้ Pattern** RPC เป็นคำสั่งอยู่แล้ว | — |
| Factory | การสร้างไอเทมและ VFX | **ไม่ต้องใช้ Pattern** `Instantiate` กับ `ItemDefinition` พอแล้ว | — |
| DI container / service locator | การผูกอ็อบเจกต์ | **ไม่ต้องใช้ Pattern** อ้างอิงผ่าน Inspector และ singleton ไม่กี่ตัวที่มีเหตุผล เหมาะกับขนาดนี้ | — |
| Event bus | ข้อความข้ามโมดูล | **ไม่ต้องใช้ Pattern** อีเวนต์ไม่ถึงสิบตัว และแต่ละตัวมีเจ้าของชัด | — |
| Repository | ค่าตั้ง | **ไม่ต้องใช้ Pattern** `GameSettings` ครอบ PlayerPrefs พอแล้ว | — |
| อินเทอร์เฟซระหว่างลำตัวกับแขนขา (`ISupportProvider`) | การอ่านค่าทรงตัว | **ไม่ต้องใช้ Pattern** กฎการล้มแยกมือจับกับขายันคนละแบบ (มือจับกันได้ทุกกฎรวม stress ขายันไม่กัน stress) และกฎหลักเป็นของขาโดยเฉพาะ (สองขาก้าวพร้อมกัน) อินเทอร์เฟซรวมจะลบความต่างนี้ทิ้ง และแต่ละอินเทอร์เฟซจะมีผู้ implement ตัวเดียว ใช้ struct `FootContact` กับ property อ่านอย่างเดียวแทน | — |
| Registry ที่ต้องลงทะเบียนและถอนเอง | ชิ้นส่วนของหุ่น | **ไม่ต้องใช้ Pattern** อ่านจาก `Robot` ณ tick นั้น รายชื่อที่ต้องถอนเองคือที่มาของแถว 25 | — |

---

## O. กฎสถาปัตยกรรมสำหรับการพัฒนาต่อ

1. โค้ดใน Robot, Enemy และ Items ห้ามอ้างคลาส UI ลอบบี้ หรือโหมด ถ้าอยากรู้ว่าแมตช์กำลังเล่นอยู่ไหม ให้อ่าน `GameplayGate`
2. ดาเมจมีทางเดียว: สร้าง `DamageInfo` แล้วเรียก `DamageRouter.TryApply` ผู้โจมตีห้ามหาคอมโพเนนต์เลือดเอง
3. ชิ้นส่วนหุ่นมีทางเดียว: `Robot`, `RobotLimb`, `LimbSlot` ห้ามเทียบชื่อ GameObject หรือค้นฉากตอนรัน
4. state ที่ server เป็นเจ้าของต้อง private: ใช้ `NetworkVariable` แบบ private, property อ่านอย่างเดียว และเมธอด `Server*` คลาสอื่นเรียกเมธอด ห้ามเขียน `.Value`
5. state ที่ใช้ร่วมกันแต่ละชิ้นมีเจ้าของคนเดียว: เคอร์เซอร์ (`UiFocus`), เฟสของแมตช์ (`MatchSession`), ค่าตั้ง (`GameSettings`), การแจกแขนขา (`LimbSelection`), การต่ออยู่ของแขนขา (`LimbAttachment`)
6. input ของเจ้าของกับการจำลองบน server อยู่คนละคอมโพเนนต์ (`*Input` กับ `*Controller`) มีแค่ RPC เป็นสะพาน และ RPC ที่มาจาก input ของเจ้าของต้องประกาศ `InvokePermission.Owner`
7. เลือก composition ก่อน: ให้ `*Ability` ขับตัวควบคุมผ่าน struct พารามิเตอร์ ห้าม subclass ตัวควบคุมเพื่อเปลี่ยนค่าจูนชั่วคราว
8. เพิ่มอินเทอร์เฟซเฉพาะเมื่อมีคลาสที่ไม่เกี่ยวกันตั้งแต่สองตัวขึ้นไปที่ผู้เรียกต้องปฏิบัติแบบเดียวกัน ห้ามมีอินเทอร์เฟซที่มี implementation เดียว
9. ใช้อีเวนต์ C# สำหรับ "มีบางอย่างเกิดขึ้น" ข้ามโมดูล ตั้งชื่อเป็นรูปอดีตไม่ขึ้นต้นด้วย `On` และเลิกฟังใน `OnDestroy` หรือ `OnNetworkDespawn`
10. ค่าจูนที่ใช้ร่วมกันและข้อความที่ผู้เล่นเห็นให้อยู่ใน ScriptableObject ส่วนค่าเฉพาะอินสแตนซ์ให้ serialize บนคอมโพเนนต์
11. ห้ามเพิ่มคลาส `*Manager` ใหม่ ตั้งชื่อตามหน้าที่โดยใช้คำศัพท์ในข้อ D
12. singleton ใหม่ต้องมีเหตุผลหนึ่งบรรทัด: เป็น service ที่อยู่ตลอดอายุแอป (session เสียงพูด กราฟิก) หรือเป็นผู้ประสานงานหนึ่งตัวต่อฉาก (`MatchSession`, `RespawnManager`) คอมโพเนนต์เกมเพลย์ห้ามเป็น singleton
13. เครื่องมือดีบักคอมไพล์เฉพาะใน Editor และ development build และห้ามเปิด server RPC ที่ใครก็เรียกได้
14. สคริปต์อยู่ในโฟลเดอร์ตามฟีเจอร์ภายใต้ root เดียวและ namespace root เดียว โฟลเดอร์ส่วนตัวใช้กับซีนทดลองเท่านั้น
15. ชิ้นส่วนของหุ่นไม่ลงทะเบียนกันเอง ใครอยากรู้ว่าแขนขาชิ้นไหนต่ออยู่ให้ถาม `Robot` ณ tick นั้น
16. state ที่มีเหตุผลเบื้องหลังเปลี่ยนผ่านเมธอดที่รับเหตุผล เช่น `ServerKnockDown(FallReason)` เพื่อให้ log ตอบได้ว่าเกิดเพราะอะไร
17. ก่อนเพิ่มชั้นใหม่ ให้บอกได้ว่ามันตัดความซ้ำหรือบั๊กอะไรที่มีอยู่จริง เก็บสถาปัตยกรรมให้เล็กเท่ากับตัวเกม

---

## คำถามที่ยังเปิดอยู่ (สรุปจากโค้ดที่มีไม่ได้)

- ปัญหาข้อ 2 จะทำดาเมจซ้ำหรือข้ามกติกา PvP จริงหรือไม่ ขึ้นกับลำดับที่ `OnCollisionEnter` ถูกเรียกระหว่างสองคอมโพเนนต์บน GameObject เดียวกัน ต้องยืนยันใน Play Mode (ขั้น 0)
- ชื่อฉากเมนูและแมพที่ใช้จริงขึ้นกับค่าที่ serialize ในซีน ซึ่งอาจทับค่า default ในโค้ด
- ป้ายของ `PvpTeamSelectUI` ชดเชยการกลับซ้าย/ขวาของลอบบี้หรือไม่
- `GunHeldItem` (บน `Pistol.prefab`) อยู่ในฐานข้อมูลไอเทมที่ build ใช้จริงหรือไม่
- แถว 25-27 มาจากการอ่านโค้ด ยังไม่ได้ลองใน Play Mode วิธีลอง: ทำให้ขาหลุดตอนกดคลิกซ้ายค้าง (ปุ่มดีบัก) กด Q ลุกด้วยขาที่เหลือแล้วก้าว / ปีนด้วยมือเดียวขณะอีกมือว่างแล้วดูค่า `armPullIntensity` / ต่อแขนกลับแล้วตกแมพให้ respawn
- หุ่นขาเดียวควรยืนและเดินได้แค่ไหน พฤติกรรมตอนนี้ขึ้นกับการนับขาที่หลุด (แถว 25) พอแก้แล้วหุ่นขาเดียวที่ยกขาจะไม่มีเท้าแตะพื้น และล้มตามกฎ "ลอยนานเกิน" ใน 0.5 วินาที (`fallGracePeriod` ใน `RobotContainer.prefab`) ทีมต้องเลือกว่าต้องการแบบไหน ข้อเสนอเก็บแบบเดิมไว้เป็นค่าเริ่มต้น (`FallLimits.countDetachedLegs = true`) เปลี่ยนได้ที่เดียวหลังลองเล่นเทียบ
- respawn ควรต่อแขนขาที่หลุดคืนและเติมเลือดหรือไม่ ตอนนี้ไม่ทำ


---

# ส่วนที่ 2: คำตอบข้อวิจารณ์แผนภาพ Robot aggregate

ฉบับเดียวกับ `CRITIQUE_RESPONSE.th.md` ตอบข้อวิจารณ์ 40 ข้อที่มีต่อแผนภาพ 3 ในส่วนที่ 1 หลักฐานจากโค้ดอยู่ในส่วนที่ 3

---

# ตอบข้อวิจารณ์แผนภาพ Robot aggregate

เอกสารนี้ตอบข้อวิจารณ์ 40 ข้อที่มีต่อแผนภาพ Robot aggregate (แผนภาพ 3 ในรีวิวสถาปัตยกรรม Nsc-Unity) ทุกข้อถูกเทียบกับโค้ดจริงใน branch `yelmee` ก่อนตัดสิน ข้อที่ถูกได้แก้แล้วในรีวิว (`ARCHITECTURE_REVIEW.th.md`) และในแผนภาพ (`uml/`) ข้อที่เห็นต่างมีเหตุผลและหลักฐานกำกับ

ตัวเลขแบบ `ไฟล์.cs:บรรทัด` อ้างถึงโค้ดใน branch นี้ ส่วนคำว่า "แผนภาพ N" หมายถึงลำดับใหม่ในรีวิวที่ปรับแล้ว (มีทั้งหมด 14 แผนภาพ)

---

## A. สรุป

| ผล | จำนวน | ข้อ |
|---|---|---|
| ยอมรับ และแก้แล้ว | 9 | 6, 7, 11, 13, 25, 27, 29, 33, 34 |
| ยอมรับบางส่วน | 11 | 1, 2, 3, 8, 10, 14, 19, 26, 30, 31, 40 |
| เห็นต่าง | 13 | 4, 5, 9, 12, 16, 17, 18, 20, 21, 23, 37, 38, 39 |
| ชี้แจง (แผนภาพอื่นในชุดตอบไว้แล้ว หรือแก้แค่การวาด) | 6 | 15, 22, 24, 32, 35, 36 |
| อ่านแผนภาพผิด แต่ประเด็นจริงในโค้ด | 1 | 28 |

ข้อวิจารณ์ถูกในเรื่องที่สำคัญที่สุด คือวงจรชีวิตของแขนขาที่หลุดและที่อยู่ของกฎการล้ม ตอนตรวจโค้ดเพื่อตอบข้อวิจารณ์นี้เจอปัญหาที่น่าจะเป็นบั๊กจริง 4 จุด (หัวข้อ F) ซึ่งสนับสนุนข้อวิจารณ์ และทำให้รีวิวเปลี่ยนความเห็นเดิมหนึ่งเรื่อง คือจากเดิมที่บอกว่าการแยก `FallRules` ไม่บังคับ ตอนนี้แนะนำให้ทำ

ส่วนข้อเสนอที่เพิ่มความทั่วไป ได้แก่ อินเทอร์เฟซ support แบบรวม state ของลำตัวที่มากขึ้น ชั้น command แยก ชั้นฟิสิกส์แยก topology แบบยืดหยุ่น และระบบ capability ไม่เข้ากับเกมนี้ และมีหลักฐานในโค้ดว่าบางข้อจะทำให้กฎของเกมแสดงออกได้แย่ลง รายละเอียดอยู่ในหัวข้อ E

---

## B. บริบทที่ใช้ตัดสิน

- **แผนภาพ 3 เป็นหนึ่งในชุด** ข้อวิจารณ์ดูแผนภาพนี้ภาพเดียว แต่หลายข้อถูกตอบไว้แล้วในภาพอื่น: การไหลของดาเมจ (แผนภาพ 6 และ 9) การควบคุมและ RPC (แผนภาพ 5) การหลุดของแขนขา (แผนภาพ 11) และ data flow (แผนภาพ 14) รอบนี้เพิ่มภาพที่ขาดจริงอีก 3 ภาพ (แผนภาพ 4, 12, 13)
- **เกณฑ์ของรีวิว** สถาปัตยกรรมต้องพอดีกับขนาดของเกม อินเทอร์เฟซใหม่ต้องมีผู้ใช้ที่ไม่เกี่ยวกันอย่างน้อยสองแบบ (รีวิวข้อ O กฎ 8) และชั้นใหม่ต้องตัดความซ้ำหรือบั๊กที่มีอยู่จริง (กฎ 17)
- **ข้อเท็จจริงของเกมที่ส่งผลต่อการตัดสิน**
  - ผู้เล่น 4 คนคุมแขนขาคนละชิ้น topology 4 ชิ้นจึงเป็นแก่นของเกม ไม่ใช่ข้อจำกัดที่บังเอิญ
  - ฟิสิกส์ของหุ่นรันบน server เท่านั้น NetworkTransform ทั้ง 15 ตัวใน `RobotContainer.prefab` เป็น AuthorityMode Server และ ownership บอกแค่ว่าใครส่ง input (`RespawnManager.cs:186-190`)
  - กติกาหลักของการล้มคือ "สองคนเผลอก้าวพร้อมกัน" (`TorsoMovement.cs:245-258`) ซึ่งเป็นกฎของขาโดยเฉพาะ

---

## C. สิ่งที่แก้แล้ว

**แผนภาพ**

- แผนภาพ 3 (วาดใหม่) Robot aggregate และวงจรชีวิตของแขนขา: `RobotLimb` ถือ `LimbController` และเป็นที่อยู่ของกติกาของชิ้น อีเวนต์บอกว่าชิ้นไหน (`StateChanged(RobotLimb)`) เพิ่ม `RobotBodyDamageRelay` สำหรับดาเมจที่ลำตัว `SavedJointSettings` เป็น struct และมีโน้ตบอกว่าหลุดแล้วอะไรยังอยู่
- แผนภาพ 4 (ใหม่) การทรงตัวของลำตัว: `TorsoBalance` ไม่มีรายชื่อแขนขา อ่านแขนขาทั้ง 4 ชิ้นจาก `Robot` มาเป็น `BalanceInput` (ขาที่หลุดยังนับแบบเดิมเป็นค่าเริ่มต้น) แล้วให้ `FallRules` คืน `FallReason` `TorsoState` เหลือ `{Standing, Ragdoll}` และ `stress` ถูกระบุว่า `{server only}`
- แผนภาพ 5 (วาดใหม่) การควบคุมแขนขา: `LimbController` เป็นคลาสฐานของแขนและขา แยก `ArmGrip` ขามี `LegMode` และ RPC ทุกตัวเป็น owner-only
- แผนภาพ 11 (เขียนใหม่) เลือดหมด ขาหลุด หุ่นล้ม แล้วดึงกลับมาต่อ พร้อมตารางวงจรชีวิตของชิ้นที่หลุด
- แผนภาพ 12 (ใหม่) ก้าวเท้าหนึ่งครั้ง จาก input ถึงจอทุกคน (ใครเป็น authority)
- แผนภาพ 13 (ใหม่) respawn และลำดับการรีเซ็ต
- แผนภาพ 15 (ใหม่) พฤติกรรมที่ผู้เล่นเห็น: state machine ของลำตัว แขนขา และแมตช์ พร้อมตัวเลขจาก prefab ที่ใช้จริง เป็นสัญญาว่ารีแฟกเตอร์ต้องไม่เปลี่ยนสิ่งที่ผู้เล่นรู้สึก ทุกแผนภาพมีป้ายบอกว่าเป็นโค้ดปัจจุบันหรือข้อเสนอ และกล่องคลาสที่เสนอมีป้าย `{เดิม: X}` บอกว่ามาจากคลาสไหน

**ข้อความในรีวิว**

- ข้อ B เพิ่มแถว 25-28 (ปัญหาที่เจอในหัวข้อ F ของเอกสารนี้)
- ข้อ C ปรับคำแนะนำของ `TorsoMovement`, `PlayerFootForRobot`/`PlayerHandMovement` และ `JointPullAndReconnect`
- ข้อ D เพิ่มชื่อใหม่ (`LimbController`, `ArmGrip`, `FallRules`, `FootContact`, `LegMode`, `FallReason`) เปลี่ยน `ServerRegisterFootJump` เป็น `ServerApplyJump` และลด `TorsoState` เหลือสองค่า
- ข้อ E, F, H, K, L, N ปรับให้ตรงกับโครงใหม่
- ข้อ G เพิ่มตาราง "ใครเป็น authority ของอะไร"
- ข้อ M เพิ่มขั้น 2b (การทรงตัวและวงจรชีวิตของแขนขา) พร้อมเทสที่ต้องผ่าน
- ข้อ O เพิ่มกฎ 15 (ชิ้นส่วนไม่ลงทะเบียนกันเอง) และกฎ 16 (state ที่มีเหตุผลเปลี่ยนผ่านเมธอดที่รับเหตุผล)

---

## D. ตอบทีละข้อ

| # | ข้อวิจารณ์ | ผล | คำตอบและสิ่งที่แก้ |
|---|---|---|---|
| 1 | `TorsoBalance` เป็น God Object | ยอมรับบางส่วน | ถูกว่ากฎการล้มไม่ควรปนกับแรงพยุง `TorsoMovement` ตอนนี้มีกฎ 5 ข้อ ตัวจับเวลา 4 ตัว แรงพยุง การกระโดดสองคน และการลุก และ state ถูกเขียนจากภายนอกอีก 2 คลาส (`JointPullAndReconnect.cs:183`, `BlackHoleProjectile.cs:202`) แก้: แยก `FallRules` (คลาส C# ธรรมดา) รับ `BalanceInput` คืน `FallReason` และให้ `ServerKnockDown(reason)` เป็นทางเดียวที่คลาสอื่นสั่งล้มได้ แรงพยุง การกระโดด และการลุกอยู่ที่ `TorsoBalance` ต่อ เพราะใช้ rigidbody และ state ตัวเดียวกันทุก tick แยกต่อก็ได้แค่คลาสที่ต้องคุยกันทุก tick (แผนภาพ 4) |
| 2 | `List<LegController>` / `List<ArmController>` ผูกแน่นเกิน ควรใช้ `ISupportProvider` / `IGrabProvider` | ยอมรับบางส่วน | เอารายชื่อออกแล้ว ลำตัวอ่านแขนขาจาก `Robot` ทุก tick (ดูข้อ 28-29) แต่ไม่ใช้อินเทอร์เฟซ support แบบรวม เพราะกฎแยกมือจับกับขายันคนละแบบ: มือที่จับของนิ่งกันได้ทุกกฎรวม stress ส่วนขายันไม่กัน stress (`TorsoMovement.cs:157` เทียบกับ `TorsoMovement.cs:224`) และกฎหลักเป็นของขาโดยเฉพาะ (`TorsoMovement.cs:249`) อินเทอร์เฟซรวมจะลบความต่างนี้ทิ้ง และแต่ละอินเทอร์เฟซจะมีผู้ implement ตัวเดียว ใช้ struct `FootContact` กับ property อ่านอย่างเดียวแทน (หัวข้อ E1) |
| 3 | `TorsoBalance` มีทั้ง network state, physics, rules และ transition | ยอมรับบางส่วน | กฎและการตัดสินย้ายไป `FallRules` ซึ่งคืนเหตุผลมาด้วย คำถาม "ทำไมล้ม" จึงตอบได้จาก log ไม่ต้องไล่ stress, support, physics เอง ส่วน `NetworkVariable` ต้องอยู่บน NetworkBehaviour อยู่แล้ว การให้มันอยู่กับคอมโพเนนต์ที่เป็นเจ้าของ rigidbody ของลำตัวคือแบบปกติของ NGO |
| 4 | `Robot` รับงานเยอะ และ `FromCollider` ทำให้ Unity physics รั่วเข้า domain | เห็นต่าง | โปรเจกต์นี้ไม่มีและไม่ควรมีชั้น domain ที่แยกจาก Unity หุ่นเป็น MonoBehaviour ที่เป็น aggregate root และเมธอดทั้ง 5 เป็นงานของหุ่นทั้งตัว `FromCollider` มาแทนการหาหุ่นหลายแบบที่มีอยู่ เช่น `RobotBodyCheck` ที่ใช้ `transform.root` (`NetworkCheck.cs:41`) ส่วนดาเมจไม่ผ่าน `Robot` อยู่แล้ว เส้นทางคือ Collision → `LimbStrike` → `DamageRouter` → `IDamageable` ตรงตามที่ข้อวิจารณ์เสนอ (แผนภาพ 6) (หัวข้อ E6) |
| 5 | `Robot` ไม่ควรเป็นที่รวม event ทุกตัว ควรมีระบบกลาง | เห็นต่าง | `Robot` คือระบบกลางของชิ้นส่วนตัวเอง คลาสใหม่ที่รับอีเวนต์แล้วอัปเดต aggregate คือของเดิมที่เปลี่ยนชื่อและเพิ่มอีกหนึ่งทอด `Robot` ฟังแค่ `StateChanged` ของแขนขา 4 ชิ้น แล้วประกาศต่อเป็น `LimbStateChanged(limb)` ให้ HUD และกติกาของโหมด |
| 6 | `RobotLimb` ยังไม่เป็นตัวแทนของ limb | ยอมรับ | เพิ่ม `IsAttached`, `controller`, อีเวนต์ `StateChanged` และให้ `RobotLimb` เป็นที่อยู่ของกติกาของชิ้น (เลือดหมดแล้วหลุด ต่อกลับแล้วเติมเลือด) คำถาม "แขนซ้ายเป็นอย่างไร" ถามที่ `robot.GetLimb(LeftArm)` ที่เดียว ไม่เพิ่ม `CanStrike` / `CanSupport` ที่ `RobotLimb` เพราะเป็นค่าที่เปลี่ยนทุก tick ตามโหมดของตัวควบคุม และอยู่ใน `FootContact` กับ `IStrikeSource` แล้ว ในเกมนี้ "ใช้งานได้" เท่ากับ "ต่ออยู่" เพราะเลือดหมดแล้วหลุดทันที (`RobotHealth.cs:192-195`) |
| 7 | ความสัมพันธ์ระหว่าง `RobotLimb` กับ controller และวงจรชีวิตตอนหลุดไม่ชัด | ยอมรับ | `RobotLimb` ถือ `controller : LimbController` หนึ่งตัว และเพิ่มตารางวงจรชีวิตในรีวิวข้อ J: หลุดคือทำลาย joint อย่างเดียว (`JointPullAndReconnect.cs:158`) GameObject และ NetworkObject ยังอยู่ (แต่ละท่อนมี NetworkObject ของตัวเอง 15 ตัวใน `RobotContainer.prefab`) ฟิสิกส์ยังรันบน server ขาที่หลุดยังรับ input และคลานตามจุดเล็ง (`PlayerFootForRobot.cs:689-698, 822-835`) แขนที่หลุดไม่ถูกขับและไม่รับ input (`PlayerHandMovement.cs:281-284, 425`) ลำตัวต้องเลิกนับเป็นเท้า ซึ่งโค้ดตอนนี้ยังนับอยู่ (หัวข้อ F1) |
| 8 | `ArmController` ทำทั้ง input, physics, grab และ network | ยอมรับบางส่วน | input ย้ายไป `ArmInput` อยู่แล้วในข้อเสนอเดิม รอบนี้แยก `ArmGrip` เพิ่ม เพราะการจับมี state และโปรโตคอลขอ ยืนยัน และบังคับปล่อยของตัวเอง (`PlayerHandMovement.cs:552-583`, `PlayerHandMovement.cs:607-761`) และลำตัวต้องอ่านแค่ส่วนนี้ ไม่แยก "ชั้น network" ออกจากฟิสิกส์ เพราะใน NGO RPC ต้องเป็นเมธอดบน NetworkBehaviour ตัวรับเป็นบรรทัดเดียวที่ตรวจค่าแล้วเก็บเป้า (`PlayerHandMovement.cs:585-605`) และฟิสิกส์อ่านเป้าใน `FixedUpdate` ซึ่งคือ input → command → simulation อยู่แล้ว |
| 9 | ชื่อ `Controller` หลอกความหมาย ควรเป็น `ArmMotor` | เห็นต่าง | หลังย้าย input ออก คลาสนี้คือตัวควบคุมแบบป้อนกลับ (สปริง PD ที่ขับ rigidbody ไปหาเป้า) คำว่า Controller จึงตรงความหมาย และรีวิวข้อ D นิยามคำศัพท์ไว้แล้ว (`*Controller` = ตัวขับฟิสิกส์ฝั่ง server, `*Input` = ตัวอ่าน input ของเจ้าของ) คำว่า Motor ชนกับ `JointMotor` ของ Unity ซึ่งเป็นอีกอย่างหนึ่ง (หัวข้อ E4) |
| 10 | `LegController` ทำทุกอย่างของการเคลื่อนที่ | ยอมรับบางส่วน | ถูกว่าขาต้องมีโครง ตอนนี้โหมดของเท้าถูกตัดสินจากแฟล็ก bool 4 ตัว บวกสถานะลำตัวและท่าเตะ (`PlayerFootForRobot.cs:422-491`) ซึ่งเป็นที่มาของบั๊ก F1 แก้เป็น `LegMode` ค่าเดียวต่อ tick แต่ละโหมดมีเมธอดขับของตัวเอง และ `FootContact` คำนวณจากโหมด input ราว 300 บรรทัด (`PlayerFootForRobot.cs:700-1002`) ย้ายไป `LegInput` ไม่แยกเป็นคลาสเดิน กระโดด และยืน (หัวข้อ E7) |
| 11 | แขนกับขามีแพทเทิร์นซ้ำแต่ไม่มี abstraction กลาง | ยอมรับ | เพิ่ม `LimbController` (abstract) ที่มี rigidbody จุดหมุน สปริง การทำ joint chain ให้ไม่ขาด และ `ServerResetForRespawn()` โค้ดที่ซ้ำจริงคือการเดินโซ่ joint (`PlayerHandMovement.cs:891-969` กับ `PlayerFootForRobot.cs:289-307, 658-687`) และการรีเซ็ตกับ RPC ล้างจุดเล็ง (`PlayerHandMovement.cs:694-722` กับ `PlayerFootForRobot.cs:1088-1127`) และ `Robot` รีเซ็ตแขนขาทุกชิ้นได้ในลูปเดียว |
| 12 | `savedJoint` ทำให้ `LimbAttachment` รู้รายละเอียดมากไป | เห็นต่าง | การต่อกลับต้องสร้าง `ConfigurableJoint` ใหม่ด้วยค่าเดิมทุกตัว ทั้ง anchor แกน drive และ limit (`JointPullAndReconnect.cs:382-445`) นี่คือหน้าที่หลักของคอมโพเนนต์นี้ และเป็นค่า private เห็นด้วยกับคำเตือนว่าอย่าให้มันโตเป็นระบบหลุดทั้งระบบ ซึ่งตอนนี้มันโตไปแล้ว (เขียนสถานะของตัวควบคุมและสั่งลำตัวล้ม `JointPullAndReconnect.cs:173-184`) ข้อเสนอย้ายสองอย่างนี้ไปที่ `RobotLimb` และ `Robot` |
| 13 | ไม่ชัดว่า HP = 0 แล้วเกิดอะไร | ยอมรับ | แผนภาพเดิมขาดจริง กติกาจากโค้ดคือ HP = 0 → เพดานเลือดลด 125 (ต่ำสุด 50) → ชิ้นหลุด (`RobotHealth.cs:218-228`) ชิ้นที่หลุดไม่รับดาเมจ (`RobotHealth.cs:177`) และต่อกลับแล้วเลือดเต็มตามเพดานใหม่ (`RobotHealth.cs:72-81`) ตอนนี้วาดไว้ในแผนภาพ 3 และ 11 โดย `RobotLimb` เป็นผู้ implement `IDamageable` แทน `LimbHealth` เพื่อให้ชิ้นที่หลุดปัดดาเมจทิ้งได้เหมือนเดิม |
| 14 | `LimbStrike` ผูกกับ Collision และ `IStrikeSource` ลอยอยู่ | ยอมรับบางส่วน | `OnCollisionEnter` ต้องอยู่บนอ็อบเจกต์ที่มี collider `LimbStrike` คือตัวแปลงการชนของ Unity เป็น `DamageInfo` การพึ่ง Collision จึงอยู่ถูกที่แล้ว ผู้ implement `IStrikeSource` อยู่ในแผนภาพ 5 (`PunchAbility`, `KickAbility`) และแผนภาพ 8 (`SwordHeldItem`) และสายเต็มอยู่ในแผนภาพ 6 และ 9 แผนภาพ 3 ใหม่ไม่วาด `LimbStrike` ซ้ำ จะได้ไม่มีอินเทอร์เฟซที่ไม่เห็นผู้ implement ในภาพ |
| 15 | ไม่ชัดว่าตีลำตัวหรือข้อต่อแล้วดาเมจไปไหน | ชี้แจง และเพิ่มในแผนภาพ | ลำตัวไม่มีเลือดโดยตั้งใจ เพราะไม่มี joint ให้หลุด ดาเมจที่ลำตัวลงชิ้นที่ใกล้จุดปะทะที่สุดที่ยังต่ออยู่ เพื่อไม่ให้ทั้ง 4 ชิ้นเลือดหมดพร้อมกัน (`PvpRobotTeam.cs:161-196`) ในข้อเสนอคือ `RobotBodyDamageRelay` ซึ่งตอนนี้อยู่ในแผนภาพ 3 ด้วย ตีข้อต่อก็คือโดน collider ของชิ้นนั้น |
| 16 | `LimbSlot` เป็น enum อาจไม่พอเมื่อระบบโต | เห็นต่าง | topology 4 ชิ้นคือแก่นของเกม (หัวข้อ E5) และโค้ดนิยามช่องแขนขาซ้ำกันอยู่แล้วหลายที่ (`LimbStatusUI.LimbSlot` กับ `PvpLimb` แบบ 4 ช่อง และ `TutorialManager.LimbRole` แบบแขนหรือขา) ซึ่งรีวิวรวมเป็น enum ตัวเดียว ข้อวิจารณ์เองก็บอกว่า enum พอสำหรับหุ่นแบบตายตัว |
| 17 | `RobotLimb[4]` เป็น topology แบบ hard-code | เห็นต่าง | array ที่ index ด้วย `LimbSlot` คือชนิดที่ตรงที่สุดสำหรับ topology ตายตัว ส่วน `List<RobotLimb>` ทำให้ "หุ่นไม่มีขาซ้าย" เขียนได้ในชนิดข้อมูล ทั้งที่ชิ้นที่หลุดยังเป็นของหุ่น การหลุดเป็นสถานะ ไม่ใช่การถอดชิ้นออกจากหุ่น |
| 18 | `owner : Robot` ทำให้อ้างอิงสองทาง | เห็นต่าง | ทั้งสองทางถูกตั้งใน prefab และไม่เปลี่ยนตอนรัน ชิ้นต่อกลับได้เฉพาะ socket เดิมของหุ่นตัวเอง (`JointPullAndReconnect.cs:123-139`) หุ่นกับแขนขาเกิดและหายพร้อมกันเพราะเป็น prefab เดียว จึงไม่มีการผูกตอนรันที่ต้องห่วงเรื่องลำดับ วงจรชีวิตเดียวที่มีคือสถานะต่ออยู่ ซึ่ง `LimbAttachment` เป็นเจ้าของ |
| 19 | ยังไม่ได้ model ว่าใครเป็น authority | ยอมรับบางส่วน | มีในแผนภาพ 5 และรีวิวข้อ G อยู่แล้ว แต่แผนภาพ 3 ไม่บอก และไม่มีภาพตั้งแต่ input ถึงจอ ข้อเท็จจริงคือฟิสิกส์รันบน server เท่านั้น (`FixedUpdate` ของลำตัวและแขนขาขึ้นต้นด้วย `if (!IsServer) return`) NetworkTransform ใน prefab หุ่นเป็น Server ทั้งหมด และไม่มีการทำนายผลฝั่ง client เพิ่มตาราง authority ในรีวิวข้อ G และแผนภาพ 12 และเจอช่องโหว่จริงหนึ่งจุด: RPC ของแขนขาใช้สิทธิ์ค่าเริ่มต้นที่ใครก็เรียกได้ (หัวข้อ F4) |
| 20 | RPC เยอะจนชั้น network ฝังในทุก component ควรเป็น Input → Command → Simulation → State | เห็นต่าง | RPC ของแขนขาคือชั้น command อยู่แล้ว เป็นคำสั่งที่มีชื่อ server ตรวจค่า (`ValidateAndSetFootTarget`) แล้วเก็บไว้ ฟิสิกส์อ่านใน `FixedUpdate` และผลกลับไปทุกเครื่องผ่าน NetworkTransform กับ NetworkVariable ส่วนสิ่งที่ยอมรับคือตัวรับ RPC ต้องบางและเป็น owner-only (หัวข้อ E3) |
| 21 | เมธอด `Server...` เต็มไปหมด ทุก object กลายเป็น network endpoint | เห็นต่าง | เมธอด `Server*` ไม่ใช่ network endpoint แต่เป็นเมธอด C# ธรรมดาที่ตกลงกันว่าเรียกบน server เท่านั้น (รีวิวข้อ D) endpoint จริงมีแค่เมธอด `*Rpc` เรื่องการเทส ลอจิกที่ควรเทสย้ายไปคลาสธรรมดาแล้ว (`FallRules` และกติกาใน `DamageRouter`) เทสใน EditMode ได้แบบเดียวกับ `Assets/Editor/ItemAuthorityTests.cs` |
| 22 | data กับ logic ยังปนกัน | ชี้แจง และปรับแผนภาพ | แยกตามชนิดไว้แล้ว `StrikeTuning` เป็น ScriptableObject (ค่าตั้ง) `DamageInfo` เป็น struct (ค่าต่อการโดนหนึ่งครั้ง) `ArmMotionOverride` เป็น struct พารามิเตอร์ต่อ tick และ `SavedJointSettings` เป็น struct ภายในตอนรัน ภาพรวมอยู่ในแผนภาพ 14 แผนภาพใหม่ใส่ stereotype ให้ครบ และแยก `FallLimits` (ค่าตั้ง) ออกจากตัวจับเวลาของ `FallRules` |
| 23 | `TorsoState` หยาบไป ควรมี Unstable, Staggering, Recovering, Disabled | เห็นต่าง (ในทิศตรงข้าม) | โค้ดมีพฤติกรรมแค่สองแบบคือยืนกับล้ม `Falling` อยู่ไม่เกิน 1 `FixedUpdate` (`TorsoMovement.cs:174-176`) และทั้ง 8 จุดนอก `TorsoMovement` ที่อ่านค่านี้ถือว่าเท่ากับ `Ragdoll` เช่น `PlayerHandMovement.cs:555`, `PlayerFootForRobot.cs:425`, `StatusPromptUI.cs:87-88` จึงลดเหลือ `{Standing, Ragdoll}` บวก `FallReason` (หัวข้อ E2) |
| 24 | `stress` ไม่ใช่ NetworkVariable อาจ desync | ชี้แจง และปรับแผนภาพ | stress ถูกเขียนและอ่านใน `FixedUpdate` ฝั่ง server เท่านั้น (`TorsoMovement.cs:127, 157, 479` และ `PlayerHandMovement.cs:803, 859` ซึ่งรันใต้ `IsServer`) client ไม่ได้ตัดสินการล้ม แค่อ่าน state จึง desync ไม่ได้ แผนภาพใส่ `{server only}` แล้ว |
| 25 | `OnLimbAttachmentChanged(bool)` ไม่บอกว่าชิ้นไหน | ยอมรับ | อีเวนต์เดิมเป็น `Action<bool>` (`JointPullAndReconnect.cs:41`) ซึ่งพอสำหรับภายในชิ้น แต่ `Robot` ฟัง 4 ชิ้น เปลี่ยนเป็น `RobotLimb.StateChanged(RobotLimb)` และ `Robot.OnLimbStateChanged(limb : RobotLimb)` |
| 26 | naming ปนกันหลายแบบ | ยอมรับบางส่วน | คำนำหน้าเป็นแบบแผนที่ความหมายต่างกัน (`Server*` = เรียกบน server, `*Rpc` = NGO บังคับ, `On*` = ตัวรับอีเวนต์, `Request*` = client ขอ) กริยาต่างกันเพราะการกระทำต่างกัน ที่แก้คือจุดที่ไม่สม่ำเสมอจริง: ลบ `ReportArmPull`, เปลี่ยน `ServerRegisterFootJump` เป็น `ServerApplyJump` และ `UpdateHandTargetRpc` / `UpdateFootTargetRpc` เป็น `SetHandTargetRpc` / `SetFootTargetRpc` |
| 27 | `ReportArmPullIntensity` เฉพาะแขนเกินไป | ยอมรับ | และเจอบั๊ก: `armPullIntensity` เป็นฟิลด์ public ที่มือทุกข้างเขียนทุก tick (`PlayerHandMovement.cs:805, 809`) มือที่ไม่ได้จับเขียน 0 ทับมือที่ปีนอยู่ (หัวข้อ F2) แก้: ไม่มี API ให้แขนส่งค่าเข้ามา ลำตัวอ่าน `ClimbPull` ของแขนที่ต่ออยู่แล้วใช้ค่ามากสุด (ต่างจากตอนนี้เล็กน้อย ต้องลองปีนเทียบ) ไม่ใช้ชื่อ `ReportSupportForce` เพราะแรงปีนไม่ใช่การพยุง แต่เป็นการลดแรงดึงเข้ากลาง (`TorsoMovement.cs:308`) |
| 28 | `RegisterFoot` ทำให้ลำตัวเป็น registry และไม่มี Unregister | อ่านแผนภาพผิด แต่ประเด็นจริงในโค้ด | เมธอดในแผนภาพคือ `ServerRegisterFootJump` แปลว่าเท้าเพิ่งกระโดด (หน้าต่างกระโดดสองคน `TorsoMovement.cs:395-418`) ไม่ใช่การลงทะเบียน แต่ชื่อนี้ชวนอ่านผิดจริง จึงเปลี่ยนเป็น `ServerApplyJump(LimbSlot)` ส่วนในโค้ดมี registry จริง (`TorsoMovement.cs:105-108`) ที่ลงทะเบียนตอน spawn และถอนตอน despawn เท่านั้น (`PlayerFootForRobot.cs:219, 340`) ไม่ถอนตอนหลุด ข้อเสนอเอา registry ออกทั้งหมด |
| 29 | หลุดแล้วการลงทะเบียนเป็นอย่างไร | ยอมรับ | ไม่มีการลงทะเบียนให้แก้แล้ว ลำตัวสร้าง `BalanceInput` จากแขนขาทั้ง 4 ชิ้นทุก tick ส่วนจะนับขาที่หลุดหรือไม่เป็นค่าใน `FallLimits` ค่าเริ่มต้นนับแบบเดิม เพื่อไม่ให้ความรู้สึกตอนเหลือขาเดียวเปลี่ยนโดยไม่มีใครตัดสิน (หัวข้อ F1) ลำดับเต็มอยู่ในแผนภาพ 11 |
| 30 | respawn กระจายไปทุก component ไม่มี RespawnSystem | ยอมรับบางส่วน | ถูกว่าต้องเห็นลำดับ เพิ่มแผนภาพ 13 และเจอปัญหาจริง: `RespawnManager` รู้จักทุกคลาสของหุ่นและแคช joint ไว้ตอน `Awake` (`RespawnManager.cs:109-125`) ขณะที่การต่อกลับสร้าง joint ใหม่ (`JointPullAndReconnect.cs:258`) (หัวข้อ F3) ข้อเสนอ: `RespawnManager` ตัดสินว่าเมื่อไหร่และที่ไหน ส่วน `Robot.ServerResetForRespawn` ตัดสินว่าอย่างไร ไม่เห็นด้วยที่ว่าชิ้นส่วนไม่ควรมีเมธอดรีเซ็ตของตัวเอง เพราะแต่ละชิ้นมี state ส่วนตัว (จุดปักเท้า joint ที่จับ ตัวจับเวลา) ถ้าให้คนอื่นล้างแทนจะทำลาย encapsulation การต่อแขนขาคืนและเติมเลือดตอน respawn ไม่ได้ทำในโค้ดตอนนี้ เป็นเรื่องที่ทีมต้องเลือก (หัวข้อ H) |
| 31 | `FromCollider` มีปัญหาเมื่อ hierarchy ซับซ้อน | ยอมรับบางส่วน | ถูกสำหรับวิธีที่ใช้ตอนนี้ `RobotBodyCheck` ใช้ `transform.root` (`NetworkCheck.cs:41`) ซึ่งพังถ้าหุ่นถูกวางใต้อ็อบเจกต์อื่น กำหนดให้ `FromCollider` คือ `hit.attachedRigidbody.GetComponentInParent<Robot>()` โดย `Robot` อยู่บน root ของ prefab หุ่น (แขนขาเป็นลูกพี่น้องกันใต้ root `NetworkCheck.cs:29-30`) ชิ้นที่หลุดยังอยู่ใต้ root เดิม จึงยังหาเจ้าของเจอ ซึ่งถูกสำหรับการเช็คทีม ส่วนดาเมจ `LimbHealth` ปฏิเสธชิ้นที่หลุดเอง |
| 32 | composition กับ aggregation ไม่ชัดเรื่องอายุของอ็อบเจกต์ | ชี้แจง และแก้การวาด | legend ในรีวิวนิยามไว้แล้ว `Robot` กับ `RobotLimb` อยู่ใน prefab เดียวกัน เกิดและหายพร้อมกัน และหลุดไม่เท่ากับทำลาย ส่วนเส้น `RobotLimb` → ตัวควบคุมที่เคยวาดเป็น aggregation (ข้าวหลามตัดโปร่ง) ผิดจริง เพราะอายุเท่ากัน แก้เป็น composition แล้ว |
| 33 | `LimbAttachment` กับ `RobotLimb` ใครเป็นเจ้าของความจริง | ยอมรับ | เจ้าของคือ `LimbAttachment.isAttached` (NetworkVariable) ตัวเดียว `RobotLimb.IsAttached` แค่อ่านต่อ และไม่เขียนสถานะนี้นอกจากสั่ง `ServerDetach()` ตามกติกา โค้ดตอนนี้มีสองแหล่งจริง (`HandState`/`FootState` กับ `netIsConnected`) ซึ่งรีวิวเสนอให้ลบ `HandState`/`FootState` (รีวิวข้อ B แถว 6) |
| 34 | ความสัมพันธ์ระหว่าง `LimbHealth` กับ `LimbAttachment` ไม่แสดง | ยอมรับ | เหมือนข้อ 13 ตอนนี้สองตัวไม่รู้จักกัน `RobotLimb` เป็นคนเชื่อม (แผนภาพ 3 และ 11) |
| 35 | damage pipeline ไม่ครบ | ชี้แจง | อยู่ในแผนภาพ 6 และ 9 `DamageRouter` เป็นตัวตัดสิน (เฟสของแมตช์และกติกาทีม) `DamageInfo` เป็นข้อมูล และผู้รับ 5 ชนิด implement `IDamageable` ไม่เพิ่ม Processor หรือ Resolver อีก เพราะตัวตัดสินตัวเดียวพอสำหรับผู้รับ 5 ชนิด |
| 36 | `Team` ไม่มีใครใช้ในแผนภาพ | ชี้แจง | ใช้ใน `DamageRouter` ผ่าน `IDamageable.Team()` กับ `DamageInfo.attackerTeam` (แผนภาพ 6) และใน `PvpModeRules` (แผนภาพ 7) ทีมอยู่ที่ `Robot` เพราะใน PvP ทีมเป็นของหุ่นทั้งตัว |
| 37 | ไม่แยก physical limb กับ logical limb | เห็นต่าง | เกมนี้ฟิสิกส์คือเกมเพลย์ การแยกที่มีประโยชน์มีแล้ว คือ `RobotLimb` (ตัวตนและกติกา) `LimbAttachment` (joint) และ `LimbController` (ขับ rigidbody) (หัวข้อ E4) |
| 38 | controller ผูกกับ `Rigidbody` ตรงเกินไป | เห็นต่าง | การขับ `Rigidbody` คือหน้าที่ของตัวควบคุม ดูข้อ 9 และ 37 |
| 39 | ไม่มี `LimbRole` หรือ capability | เห็นต่าง | ข้อวิจารณ์เองก็บอกว่าไม่จำเป็นสำหรับหุ่นแบบตายตัว ความสามารถของชิ้นบอกด้วยคอมโพเนนต์ที่ติดอยู่ (`ArmGrip`, `PunchAbility`, `KickAbility`) และ `LimbSlot.IsArm()` / `IsLeg()` สำหรับ UI |
| 40 | แผนภาพขาด flow | ยอมรับบางส่วน | ชุดแผนภาพมี sequence และ data flow อยู่แล้ว (แผนภาพ 9, 10, 14) แต่ขาดสามเรื่องที่ข้อวิจารณ์ยกมาจริง ตอนนี้เพิ่มแล้ว: เลือดหมด → หลุด → ล้ม → ต่อกลับ (แผนภาพ 11) ก้าวเท้าจาก input ถึงจอทุกคน (แผนภาพ 12) และ respawn (แผนภาพ 13) |

---

## E. ข้อที่เห็นต่าง อธิบายเต็ม

### E1. อินเทอร์เฟซ support แบบรวม (ข้อ 2)

ข้อวิจารณ์เสนอ `ISupportProvider` ให้ขาและแขน implement เพื่อให้ลำตัวไม่ต้องรู้ว่าข้างหลังเป็นอะไร และรองรับหาง ล้อ หรือส่วนพยุงอื่นในอนาคต

กฎในโค้ดแยกการพยุงสองแบบออกจากกันโดยตั้งใจ:

- มือที่จับของนิ่ง (kinematic) กันการล้มได้ทุกกฎ รวมกฎ stress (`TorsoMovement.cs:157` เช็ค `!supportedByHand`)
- ขาที่ยันพื้นกันได้เฉพาะกฎการทรงตัว (เอียง ลอย เสียสมดุล) ผ่าน `balanceProtected` (`TorsoMovement.cs:224`) แต่ไม่กันกฎ stress
- กฎหลักของเกมคือ "สองขาก้าวพร้อมกัน" (`TorsoMovement.cs:249`) ซึ่งเป็นเรื่องของขาโดยเฉพาะ และคอมเมนต์ในโค้ดบอกว่านี่คือความยากที่ตั้งใจ (`TorsoMovement.cs:245-248`)

ถ้ารวมเป็น `ISupportProvider` ตัวเดียว ลำตัวจะต้องถามกลับว่า support ชิ้นนี้เป็นแบบไหน ซึ่งก็คือการรู้จักชนิดเดิมผ่านอีกชั้นหนึ่ง ถ้าแยกเป็นสองอินเทอร์เฟซ แต่ละตัวจะมีผู้ implement ตัวเดียว ซึ่งขัดกฎของรีวิว ส่วนหางหรือล้อไม่อยู่ในการออกแบบของเกมที่ผู้เล่น 4 คนคุม 4 ชิ้น

สิ่งที่รับจากข้อนี้คือปัญหาที่อยู่ข้างใต้ คือรายชื่อที่ลงทะเบียนไว้และไม่ตามสถานะการหลุด ซึ่งแก้ด้วยการอ่านจาก `Robot` ทุก tick และส่งสถานะเป็นค่า (`FootContact`) ให้ `FallRules` เทสได้โดยไม่ต้องมี MonoBehaviour

### E2. state ของลำตัวควรมีมากขึ้น (ข้อ 23)

state machine ควรมี state เท่ากับจำนวนพฤติกรรมที่ต่างกันจริง โค้ดตอนนี้มีพฤติกรรมสองแบบ คือยืน (มีแรงพยุง ขาปักพื้น มือดึงลำตัวได้) กับล้ม (แรงพยุงหาย มือห้อย ขาอ่อน กด Q เพื่อลุก) `Falling` ไม่มีพฤติกรรมของตัวเอง มันอยู่ไม่เกินหนึ่ง `FixedUpdate` แล้วกลายเป็น `Ragdoll` (`TorsoMovement.cs:174-176`) และทุกจุดที่อ่านค่าถือว่ามันเท่ากับ `Ragdoll`

การเพิ่ม Unstable, Staggering หรือ Recovering ตอนนี้จะสร้าง state ที่ไม่มีโค้ดไหนทำอะไรต่างออกไป และเพิ่มจุดที่ต้องเช็คในแปดที่ที่อ่านค่านี้ ข้อเสนอจึงไปทางตรงข้าม คือเหลือสองค่า แล้วเก็บ "ล้มเพราะอะไร" ไว้ใน `FallReason` ถ้าวันหนึ่งนักออกแบบเพิ่มท่าเซที่มีแอนิเมชันหรือกติกาของตัวเอง ค่อยเพิ่ม state ตอนนั้น

### E3. ชั้น command แยก และเมธอด Server* (ข้อ 20, 21)

ลำดับที่ข้อวิจารณ์เสนอ (Input → Command → Server Simulation → Network State) คือสิ่งที่โค้ดทำอยู่แล้ว เจ้าของอ่าน input ใน `Update()` ส่งคำสั่งที่มีชื่อเป็น RPC server ตรวจค่าแล้วเก็บ ฟิสิกส์อ่านคำสั่งล่าสุดใน `FixedUpdate` และผลกลับไปทุกเครื่องผ่าน NetworkTransform กับ NetworkVariable (แผนภาพ 12)

ชั้น command ที่แยกเป็นระบบ (บัฟเฟอร์คำสั่ง หมายเลข tick การยืนยัน) คุ้มเมื่อต้องทำนายผลฝั่ง client แล้ว rollback ซึ่งกับหุ่นที่ต่อด้วย joint และฟิสิกส์ต่อเนื่องทำได้ยากมาก และเกม co-op ที่ทุกคนเล่นหุ่นตัวเดียวกันก็ทนดีเลย์ได้ จึงไม่ใช่ปัญหาที่เกมนี้มี

ส่วนเมธอด `Server*` ไม่ใช่ network endpoint เป็นแค่แบบแผนการตั้งชื่อว่าเรียกบน server เท่านั้น endpoint จริงคือเมธอด `*Rpc` ซึ่งจะเป็น owner-only ทุกตัว

### E4. ชั้นฟิสิกส์แยก และชื่อ Controller (ข้อ 9, 37, 38)

`ILimbPhysics` จะมีผู้ implement ตัวเดียวคือ PhysX ของ Unity ไม่มีระบบฟิสิกส์อื่นให้สลับ และพฤติกรรมของแขนขาก็เทสโดยไม่ใช้ฟิสิกส์ไม่ได้อยู่ดี โปรเจกต์ใช้ harness `_ArmFeelTest` สำหรับวัดฟีลของแขนในฉากจริง ซึ่งเป็นวิธีที่ถูกสำหรับเรื่องนี้

การแยกที่มีประโยชน์ในเกมนี้มีแล้ว คือตัวตนและกติกาของชิ้น (`RobotLimb`) joint (`LimbAttachment`) และการขับ rigidbody (`LimbController`) ส่วนลอจิกที่ไม่ใช่ฟิสิกส์และควรเทส (กฎการล้ม กติกาดาเมจ) แยกเป็นคลาสธรรมดาแล้ว

ชื่อ Controller ถูกความหมายหลังย้าย input ออก เพราะคลาสนี้คือตัวควบคุมแบบป้อนกลับที่คำนวณแรงเพื่อให้ rigidbody ไปถึงเป้า ถ้าเปลี่ยนเป็น Motor จะชนกับ `JointMotor` ของ Unity ซึ่งเป็นอีกกลไกหนึ่ง

### E5. topology แบบยืดหยุ่น และ capability (ข้อ 16, 17, 39)

เกมนี้ออกแบบให้ผู้เล่น 4 คนคุมแขนขาคนละชิ้น กฎการล้มนับสองขา การกระโดดสองคนต้องการสองขาจากคนละคน (`TorsoMovement.cs:403-404`) ลอบบี้ HUD และ PvP ใช้ 4 ช่อง และกติกา PvP แพ้เมื่อหลุดครบ 4 ชิ้น หุ่นที่มีหางหรือแขนที่สามไม่ใช่การต่อยอดระบบนี้ แต่เป็นเกมอีกแบบที่ต้องออกแบบกติกาใหม่หมด การทำให้โค้ดรองรับไว้ก่อนจะทำให้ทุกที่ต้องรับมือกับกรณีที่ไม่มีวันเกิด เช่น "หุ่นมีขาเดียวตั้งแต่เกิด"

### E6. Robot รับงานเยอะ และ FromCollider (ข้อ 4, 5)

ทุกเมธอดของ `Robot` เป็นงานของหุ่นทั้งตัว: หาชิ้นส่วน เปิดปิดการจำลอง รีเซ็ตตอน respawn และตอบสนองเมื่อชิ้นหลุด ถ้าย้ายออกไปคลาสอื่น คลาสนั้นก็ต้องรู้จักชิ้นส่วนทั้งหมดของหุ่นอยู่ดี ซึ่งคือสิ่งที่ `RespawnManager` ทำอยู่ตอนนี้และเป็นที่มาของปัญหา F3

`FromCollider` เป็นเมธอด static ที่ตอบคำถาม "collider นี้เป็นของหุ่นตัวไหน" ซึ่งระบบทีม ไอเทม บอส และหลุมดำต้องถาม และตอนนี้แต่ละที่ถามด้วยวิธีของตัวเอง การที่มันอ้าง `Collider` ไม่ใช่การรั่ว เพราะหุ่นเป็นคอมโพเนนต์ของ Unity อยู่แล้ว ส่วนเส้นทางดาเมจไม่ผ่าน `Robot` เลย ตรงกับที่ข้อวิจารณ์ต้องการ

### E7. แยก LegController เป็นหลายคลาส (ข้อ 10)

การยืน การก้าว การกระโดด และการยันตัวลุก ใช้ rigidbody เป้า และจุดปักเดียวกันทุก tick และสลับกันตามสถานะของลำตัวกับท่าเตะ ถ้าแยกเป็นคลาสต่อพฤติกรรม คลาสเหล่านั้นต้องแย่งกันคุม rigidbody ตัวเดียว ซึ่งโค้ดเจอปัญหานี้อยู่แล้วระหว่างเท้ากับท่าเตะ ต้องมี `IsKickControllingFoot` ไว้บอกว่าใครคุม (`PlayerFootForRobot.cs:207-213`) ทางที่ดีกว่าคือทำให้โหมดชัดเจนเป็นค่าเดียวต่อ tick (`LegMode`) แล้วให้แต่ละโหมดมีเมธอดขับของตัวเอง

---

## F. ปัญหาที่เจอระหว่างตรวจ

ทั้งสี่ข้อมาจากการอ่านโค้ด ยังไม่ได้ลองใน Play Mode และถูกเพิ่มในรีวิวข้อ B แถว 25-28 แล้ว

### F1. ลำตัวยังนับขาที่หลุดเป็นเท้า (High)

- เท้าลงทะเบียนกับลำตัวตอน spawn (`PlayerFootForRobot.cs:219`) และถอนตอน despawn เท่านั้น (`PlayerFootForRobot.cs:340`) ไม่ถอนตอนหลุด
- ลูปของลำตัวนับทุกเท้าที่ลงทะเบียน (`TorsoMovement.cs:196-213`) และ `IsGrounded()` เช็คแค่ capsule จากสะโพกถึงเท้า ไม่เช็คว่าต่ออยู่ (`PlayerFootForRobot.cs:1074-1080`)
- ผลที่หนึ่ง: ขาที่หลุดนอนบนพื้นถูกนับว่าแตะพื้นและสมดุล แรงดึงลำตัวเข้ากลาง (`TorsoMovement.cs:304-309`) จึงดึงไปทางขาที่หลุด และกฎ "ลอยนานเกิน" ไม่ทำงาน
- ผลที่สอง: ถ้าขาหลุดตอนผู้เล่นกดคลิกซ้ายค้าง `isStepping` ของขานั้นค้างเป็น true บน server เพราะถูกล้างเฉพาะในสาขาที่ต่ออยู่ของ `PlayerFootForRobot.HandleInput` (`PlayerFootForRobot.cs:710-715`, `PlayerFootForRobot.cs:800-805`) พอลุกขึ้นแล้วขาที่เหลือก้าวเมื่อไหร่ กฎสองขาก้าวพร้อมกันจะนับครบสองขาแล้วสั่งล้มใน 0.15 วินาที
- วิธีลอง: ใช้ปุ่มดีบักทำให้ขาหลุดขณะกดคลิกซ้ายค้าง กด Q ลุกด้วยขาที่เหลือ แล้วก้าว
- ในข้อเสนอ: ไม่มีรายชื่อ ลำตัวอ่านแขนขาจาก `Robot` แต่ผลทั้งสองข้อข้างบนเปลี่ยนความรู้สึกของเกม ข้อเสนอจึงเก็บแบบเดิมไว้เป็นค่าเริ่มต้น (`FallLimits.countDetachedLegs = true` และแฟล็ก stepping ไม่ถูกล้างตอนหลุด) ขาที่หลุดยันพื้นแทนขาไม่ได้เหมือนตอนนี้ ถ้าทีมลองเล่นแล้วตัดสินว่าเป็นบั๊ก ปิดได้ที่ `FallLimits` กับ `LegController` ที่เดียว (โน้ตสีส้มในแผนภาพ 4 และ 5)

### F2. ค่าแรงปีนถูกเขียนทับ (Medium)

- `armPullIntensity` เป็นฟิลด์ public ของลำตัว มือที่ปีนเขียนค่าจริง (`PlayerHandMovement.cs:805`) มือที่ไม่ได้จับเขียน 0 (`PlayerHandMovement.cs:809`) และทั้งสองมือรันทุก `FixedUpdate`
- ค่าที่ลำตัวใช้ลดแรงดึงเข้ากลางตอนปีน (`TorsoMovement.cs:308`) จึงเป็นของแขนที่รันทีหลัง ไม่ใช่ของแขนที่ปีน
- วิธีลอง: จับขอบนิ่งด้วยแขนข้างเดียวขณะอีกข้างว่าง แล้ว log ค่านี้ใน `TorsoMovement.FixedUpdate`
- ในข้อเสนอ: ลำตัวอ่าน `ClimbPull` ของแขนที่ต่ออยู่ทุกข้างแล้วใช้ค่ามากสุด แบบเดิมเก็บไว้ไม่ได้เพราะผลขึ้นกับลำดับสคริปต์ จึงต้องลองปีนเทียบก่อนใช้

### F3. respawn พึ่ง joint ที่แคชไว้ตอนเริ่มฉาก (Medium)

- `RespawnManager` เก็บ joint ทั้งหมดของหุ่นครั้งเดียวตอน `Awake` (`RespawnManager.cs:109-125`) เพื่อปลดชั่วคราวระหว่าง teleport
- การต่อแขนขากลับสร้าง `ConfigurableJoint` ตัวใหม่ (`JointPullAndReconnect.cs:258`) ที่ไม่อยู่ในแคช แขนขาที่เคยหลุดแล้วต่อกลับจึงไม่ถูกปลดระหว่าง teleport
- วิธีลอง: ทำให้แขนหลุด ดึงกลับมาต่อ แล้วเดินตกแมพ ดูว่าแขนข้างนั้นกระตุกหรือยืดตอนเกิดใหม่ต่างจากข้างที่ไม่เคยหลุดไหม
- ในข้อเสนอ: `Robot.ServerResetForRespawn` อ่าน joint ที่ต่ออยู่ตอนนั้นจากแขนขา (แผนภาพ 13)

### F4. RPC ของแขนขาไม่ได้จำกัดเจ้าของ (Medium)

- RPC ของมือ เท้า และลำตัวประกาศเป็น `[Rpc(SendTo.Server)]` โดยไม่ระบุสิทธิ์ (`PlayerHandMovement.cs:585-586, 607, 663`, `PlayerFootForRobot.cs:1008-1015`, `TorsoMovement.cs:465`)
- ใน NGO 2.12 ค่าเริ่มต้นของ `InvokePermission` คือ `Everyone` (`RpcAttributes.cs:91-93` ในแพ็กเกจ) client ไหนก็ส่งเป้าหมายให้แขนขาของคนอื่นได้ ใน PvP คือขับแขนขาของฝ่ายตรงข้ามได้ถ้า client ถูกแก้
- `PlayerInventory` ทำถูกแล้วด้วย `RpcInvokePermission.Owner` แก้แบบเดียวกัน

---

## G. ลำดับความสำคัญหลังตรวจโค้ด

| ลำดับ | เรื่อง | ข้อ | เหตุผล |
|---|---|---|---|
| 1 | วงจรชีวิตของขาที่หลุด และการเลิกใช้ registry | 7, 28, 29 | มีบั๊กที่น่าจะเกิดจริงกับกติกาหลักของเกม (F1) |
| 2 | สิทธิ์ของ RPC | 19 | ช่องโกงใน PvP แก้ง่าย (F4) |
| 3 | `FallRules` + `FallReason` และผู้สั่งล้มคนเดียว | 1, 3, 23 | กติกาหลักต้องเทสได้ และต้องรู้ว่าล้มเพราะอะไร |
| 4 | ค่าที่หลายคนเขียนทับ | 27 | บั๊ก F2 |
| 5 | respawn ที่พึ่งแคช | 30 | บั๊ก F3 และ `RespawnManager` รู้จักทุกคลาสของหุ่น |
| 6 | กติกาเลือดกับการหลุดผ่าน `RobotLimb` | 13, 34 | ตัดการที่ health กับ attachment อ้างกันเอง |
| 7 | โครงของตัวควบคุม: `LimbController`, `ArmGrip`, `LegMode` | 8, 10, 11 | ลดโค้ดซ้ำ และทำให้โหมดของขาชัด |
| 8 | ความชัดของแผนภาพ | 14, 22, 24, 25, 32 | signature, stereotype, server only และชนิดของเส้น |

ที่ไม่ทำ: อินเทอร์เฟซ support แบบรวม, state ของลำตัวที่มากขึ้น, ชั้น command แยก, ชั้นฟิสิกส์แยก, topology แบบยืดหยุ่น, ระบบ capability, การเปลี่ยนชื่อ Controller และการย้าย `FromCollider` ออกจาก `Robot`

เทียบกับลำดับในข้อวิจารณ์ ข้อที่ตรงกันคือเรื่องลำตัว วงจรชีวิตตอนหลุด ความสัมพันธ์ของเลือดกับการหลุด และ respawn ข้อที่ต่างคือ "Collision รั่วเข้า Robot" กับ "damage pipeline ไม่ครบ" ซึ่งไม่ใช่ปัญหาเมื่อดูทั้งชุดแผนภาพ และมีสองเรื่องที่ข้อวิจารณ์ไม่ได้พูดถึงแต่ขึ้นมาอยู่อันดับต้น คือสิทธิ์ของ RPC และค่าที่หลายคนเขียนทับ

---

## H. เรื่องที่ทีมต้องตัดสิน

- **หุ่นขาเดียวควรยืนและเดินได้แค่ไหน** พฤติกรรมตอนนี้ขึ้นกับการนับขาที่หลุด (F1) พอแก้แล้วหุ่นขาเดียวที่ยกขาจะไม่มีเท้าแตะพื้น และล้มตามกฎ "ลอยนานเกิน" ใน 0.5 วินาที (`fallGracePeriod` ใน `RobotContainer.prefab`) ถ้าต้องการให้เดินขาเดียวได้ ต้องเพิ่มกติกานี้ใน `FallRules` อย่างตั้งใจ
- **respawn ควรต่อแขนขาที่หลุดคืนและเติมเลือดไหม** ตอนนี้ไม่ทำ ถ้าจะทำก็เพิ่มใน `Robot.ServerResetForRespawn` ที่เดียว
- **RPC ที่ต้องให้คนที่ไม่ใช่เจ้าของเรียก** จากที่ตรวจมีแค่ปุ่มดีบัก ซึ่งควรคอมไพล์เฉพาะใน Editor และ development build อยู่แล้ว (รีวิวข้อ B แถว 10)


---

# ส่วนที่ 3: โค้ดที่เอกสารอ้างถึง

ทุกบรรทัดที่รีวิวและคำตอบข้อวิจารณ์อ้างเป็น `ไฟล์.cs:บรรทัด` ตัดมาจาก branch `yelmee` ของโปรเจกต์ (บวกบรรทัดรอบข้างอย่างละ 3 บรรทัด) ตัวเลขหน้าแต่ละบรรทัดคือเลขบรรทัดจริงในไฟล์ `RpcAttributes.cs` มาจากแพ็กเกจ Netcode for GameObjects 2.12.0

ข้อเท็จจริงที่ไม่อยู่ในไฟล์ .cs: `RobotContainer.prefab` (prefab ของหุ่น) มี NetworkObject 15 ตัว และ NetworkTransform ทั้ง 15 ตัวตั้ง AuthorityMode = 0 (Server) ค่า `fallGracePeriod` ใน prefab คือ 0.5 ส่วน `bothFeetSteppingGrace` ไม่ได้ serialize ไว้ จึงใช้ค่าในโค้ดคือ 0.15

ฉากเกมที่อยู่ใน build (`MAPBOSS`, `MAPPAKUAR`, `PVP`) ใช้ `Yelmee/RobotContainer.prefab` ซึ่งเป็น variant ของ `Yelmee/Robotc DDD/RobotContainer.prefab` ตัวเลขเกมเพลย์ในโน้ตของแผนภาพมาจากค่าที่ serialize ใน prefab สองตัวนี้ ไม่ใช่ค่า default ในโค้ด: `maxTorsoStress` 50, `stressDecayRate` 50, `maxBalanceAngle` 45, `fallGracePeriod` 0.5 (กฎเอียงใช้ครึ่งหนึ่งคือ 0.25 วินาที), `bothFeetUnbalancedGrace` 0.15, `jumpGraceDuration` 1.2, `coopJumpWindow` 0.3, `soloJumpForce` 250, `coopJumpBonusForce` 450, `maxJumpUpVelocity` 10, `landingAssistDuration` 0.4, `testSingleFootRecovery` true, `MaxHp` 500, `hpLossPerBreak` 125, `minMaxHp` 50, `reconnectCooldown` 3, `reconnectDistance` 2, `maxPullSpeed` 25, `minVelocityThreshold` 8, `speedToDamage` 0.6, `kickSpeedToDamage` 1.2, `maxDamagePerHit` 60 ส่วนเกณฑ์แพ้: `BossGameFlowUI.prefab` ตั้ง `defeatDetachedLimbs` 4 และเช็คทุก 1 วินาที `PVP.unity` ตั้ง `limbsLostToLose` 4 และเช็คทุก 0.25 วินาที

## BlackHoleProjectile.cs

`Assets/nok/Enemy/Scripts/EnemyAI/BlackHoleProjectile.cs` (226 บรรทัด)

```csharp
163: 
164:                 if (hittable is explobuilding)
165:                 {
166:                     hittable.ServerTakeDamage(buildingDamage, AttackType.BlackHole, direction);
167:                     buildingsDestroyed++;
168:                 }
169:                 else
170:                 {
171:                     hittable.ServerTakeDamage(playerDamage, AttackType.BlackHole, direction);
172:                     partsHit++;
173: 
174:                     // This is the ultimate: whatever it touches comes off, and the robot
```

```csharp
199:             torsoKnockedDown = true;   // found it — don't search the hierarchy again this shot
200: 
201:             if (torso.currentState.Value == TorsoMovement.TorsoState.Standing)
202:                 torso.currentState.Value = TorsoMovement.TorsoState.Falling;
203:         }
204: 
205:         /// <summary>Stop emitting and fade out, then remove the object.</summary>
```

## JointPullAndReconnect.cs

`Assets/Scenes/Enemy AndInteractive Object/JointPullAndReconnect.cs` (446 บรรทัด)

```csharp
38:     public bool IsBeingPulled => isPulling;
39: 
40:     /// <summary>ยิงบนทุกเครื่องเมื่อสถานะหลุด/ต่อกลับเปลี่ยน (true = ต่ออยู่)</summary>
41:     public event Action<bool> OnConnectionStateChanged;
42: 
43:     // ✅ สถานะเชื่อมต่อถูกตัดสินบน Server เท่านั้น แล้ว sync ให้ทุกเครื่องผ่านตัวนี้
44:     // (เดิมเป็น bool ธรรมดา เครื่อง Client ไม่มีทางรู้ว่าชิ้นส่วนหลุด
```

```csharp
120:         }
121:     }
122: 
123:     public void CaptureJointSetup(ConfigurableJoint joint)
124:     {
125:         if (joint == null || joint.connectedBody == null) return;
126: 
127:         targetBody = joint.connectedBody;
128: 
129:         // 🚨 อิงตำแหน่งจาก Joinobject.transform เท่านั้น เพื่อความเป๊ะ
130:         localPositionOffset = targetBody.transform.InverseTransformPoint(Joinobject.transform.position);
131:         localRotationOffset = Quaternion.Inverse(targetBody.transform.rotation) * Joinobject.transform.rotation;
132: 
133:         savedSettings = new SavedConfigurableJointSettings(joint);
134: 
135:         SetConnectionState(true);
136:         isPulling = false;
137:         currentCooldown = 0f;
138:         currentJoint = joint;
139:     }
140: 
141:     private void OnJointBreak(float breakForce)
142:     {
```

```csharp
155: 
156:         if (!isConnected || currentJoint == null) return;
157: 
158:         Destroy(currentJoint);
159:         currentJoint = null;
160:         HandleDisconnection();
161:     }
```

```csharp
170:         if (!isConnected)
171:             return;
172: 
173:         SetConnectionState(false);
174:         SetControllerStateServer(false);
175: 
176:         // ✅ ขาหลุด = ยืนต่อไม่ได้ — สั่งล้ม (Falling → Ragdoll) ทันที
177:         // ไม่งั้นระบบ hover ยังพยุงตัวลอยค้างกลางอากาศ แล้วขาที่หลุด
178:         // เอื้อมกลับมาต่อไม่ถึงตำแหน่ง socket ที่ลอยอยู่
179:         if (footController != null && footController.torso != null)
180:         {
181:             TorsoMovement torso = footController.torso;
182:             if (torso.currentState.Value == TorsoMovement.TorsoState.Standing)
183:                 torso.currentState.Value = TorsoMovement.TorsoState.Falling;
184:         }
185:     }
186: 
187:     private void Update()
```

```csharp
255: 
256:         myRb.isKinematic = false;
257: 
258:         currentJoint = Joinobject.AddComponent<ConfigurableJoint>();
259:         savedSettings.ApplyTo(currentJoint, targetBody);
260: 
261:         SetConnectionState(true);
```

```csharp
379: // ==========================================
380: // 🚨 อัปเดต Struct: เพิ่ม Axis และ AutoAnchor ป้องกันข้อต่อเบี้ยว
381: // ==========================================
382: public struct SavedConfigurableJointSettings
383: {
384:     public Vector3 anchor;
385:     public Vector3 connectedAnchor;
386:     public bool autoConfigureConnectedAnchor; // ✅ โคตรสำคัญ ป้องกัน Unity คาดเดา Anchor เอง
387:     public Vector3 axis;                      // ✅ แกนหมุนหลัก
388:     public Vector3 secondaryAxis;             // ✅ แกนหมุนรอง
389:     public float massScale;
390:     public float connectedMassScale;
391: 
392:     public ConfigurableJointMotion xMotion, yMotion, zMotion, angularXMotion, angularYMotion, angularZMotion;
393:     public SoftJointLimit linearLimit, lowAngularXLimit, highAngularXLimit, angularYLimit, angularZLimit;
394:     public JointDrive xDrive, yDrive, zDrive, angularXDrive, angularYZDrive, slerpDrive;
395:     public RotationDriveMode rotationDriveMode;
396:     public float breakForce, breakTorque;
397:     public bool enableCollision;
398: 
399:     public SavedConfigurableJointSettings(ConfigurableJoint source)
400:     {
401:         anchor = source.anchor;
402:         connectedAnchor = source.connectedAnchor;
403:         autoConfigureConnectedAnchor = source.autoConfigureConnectedAnchor;
404:         axis = source.axis;
405:         secondaryAxis = source.secondaryAxis;
406:         massScale = source.massScale;
407:         connectedMassScale = source.connectedMassScale;
408: 
409:         xMotion = source.xMotion; yMotion = source.yMotion; zMotion = source.zMotion;
410:         angularXMotion = source.angularXMotion; angularYMotion = source.angularYMotion; angularZMotion = source.angularZMotion;
411:         linearLimit = source.linearLimit; lowAngularXLimit = source.lowAngularXLimit; highAngularXLimit = source.highAngularXLimit;
412:         angularYLimit = source.angularYLimit; angularZLimit = source.angularZLimit;
413:         xDrive = source.xDrive; yDrive = source.yDrive; zDrive = source.zDrive;
414:         angularXDrive = source.angularXDrive; angularYZDrive = source.angularYZDrive; slerpDrive = source.slerpDrive;
415:         rotationDriveMode = source.rotationDriveMode; breakForce = source.breakForce; breakTorque = source.breakTorque;
416:         enableCollision = source.enableCollision;
417:     }
418: 
419:     public void ApplyTo(ConfigurableJoint target, Rigidbody connectedBody)
420:     {
421:         target.connectedBody = connectedBody;
422: 
423:         // 🚨 ต้องปิด Auto Configure ก่อนยัด Anchor ไม่งั้น Unity จะเขียนทับ
424:         target.autoConfigureConnectedAnchor = false;
425: 
426:         target.anchor = anchor;
427:         target.connectedAnchor = connectedAnchor;
428:         target.axis = axis;
429:         target.secondaryAxis = secondaryAxis;
430:         target.massScale = massScale;
431:         target.connectedMassScale = connectedMassScale;
432: 
433:         // คืนค่า Auto ให้เหมือนต้นฉบับ
434:         target.autoConfigureConnectedAnchor = autoConfigureConnectedAnchor;
435: 
436:         target.xMotion = xMotion; target.yMotion = yMotion; target.zMotion = zMotion;
437:         target.angularXMotion = angularXMotion; target.angularYMotion = angularYMotion; target.angularZMotion = angularZMotion;
438:         target.linearLimit = linearLimit; target.lowAngularXLimit = lowAngularXLimit; target.highAngularXLimit = highAngularXLimit;
439:         target.angularYLimit = angularYLimit; target.angularZLimit = angularZLimit;
440:         target.xDrive = xDrive; target.yDrive = yDrive; target.zDrive = zDrive;
441:         target.angularXDrive = angularXDrive; target.angularYZDrive = angularYZDrive; target.slerpDrive = slerpDrive;
442:         target.rotationDriveMode = rotationDriveMode; target.breakForce = breakForce; target.breakTorque = breakTorque;
443:         target.enableCollision = enableCollision;
444:     }
445: }
446: 
```

## NetworkCheck.cs

`Assets/nok/SaveScript/NetworkCheck.cs` (43 บรรทัด)

```csharp
26: /// <summary>
27: /// เช็กว่า Collider เป็นชิ้นส่วนของหุ่นผู้เล่นไหม — ดูจาก component จริง ไม่พึ่ง tag
28: /// (tag ลืมตั้งแม้ชิ้นเดียว = ระบบตายเงียบ / ส่วน component ติดมากับ prefab เสมอ)
29: /// ใช้ root ของ Rigidbody เพราะโครงหุ่นมี limb เป็น sibling กัน — GetComponentInParent
30: /// ตรงๆ จาก collider ของท่อนขาจะหา TorsoMovement ไม่เจอ
31: /// </summary>
32: public static class RobotBodyCheck
33: {
```

```csharp
38:         Rigidbody rb = other.attachedRigidbody;
39:         if (rb == null) return false;
40: 
41:         return rb.transform.root.GetComponentInChildren<TorsoMovement>() != null;
42:     }
43: }
```

## PlayerFootForRobot.cs

`Assets/Scenes/TheBestFolder/Mynigga/player/PlayerFootForRobot.cs` (1129 บรรทัด)

```csharp
204:     private static GUIStyle _footReachStyle;
205:     private PlayerLegCombat _legCombat;
206: 
207:     public bool IsKickMotionActive =>
208:         _legCombat != null && _legCombat.IsKickMotionActive;
209: 
210:     // รวมช่วงง้างขาด้วย — ตอนง้าง PlayerLegCombat เป็นคนขับเท้าเอง
211:     // ถ้าปล่อยให้สปริงก้าวเดินทำงานต่อ มันจะลากเท้ากลับไปหาจุดที่เมาส์ชี้ ท่าง้างเลยไม่เกิด
212:     public bool IsKickControllingFoot =>
213:         _legCombat != null && _legCombat.IsKickControllingFoot;
214: 
215:     public override void OnNetworkSpawn()
216:     {
217:         base.OnNetworkSpawn();
218: 
219:         if (IsServer && torso != null) torso.RegisterFoot(this);
220:         _footNetworkTransform = GetComponent<NetworkTransform>();
221:         _legCombat = GetComponent<PlayerLegCombat>();
222: 
```

```csharp
286:     }
287: 
288:     // เก็บ Collider ทั้งโซ่ขา (เท้า → ท่อนขา จนถึงก่อนถึงลำตัว)
289:     private List<Collider> GetLegChainColliders()
290:     {
291:         var cols = new List<Collider>();
292:         Rigidbody currentBody = footRb;
293:         int safety = 0;
294:         while (currentBody != null &&
295:                (torso == null || currentBody != torso.torsoRb) &&
296:                safety++ < 8)
297:         {
298:             cols.AddRange(currentBody.GetComponents<Collider>());
299: 
300:             Rigidbody next = null;
301:             foreach (Joint j in currentBody.GetComponents<Joint>())
302:                 if (j != null && next == null && j.connectedBody != null)
303:                     next = j.connectedBody;
304:             currentBody = next;
305:         }
306:         return cols;
307:     }
308: 
309:     // ✅ [Selective Self-Collision] ปิดการชนเฉพาะ "ขา ↔ ขาอีกข้าง"
310:     // เวอร์ชันก่อนปิดชนกับทั้งตัว → ตอน Ragdoll ลำตัวทะลุขาลงไปกองกับพื้น ล้มดูหนักผิดปกติ
```

```csharp
337: 
338:     public override void OnNetworkDespawn()
339:     {
340:         if (IsServer && torso != null) torso.UnregisterFoot(this);
341:         // คืนเคอร์เซอร์ตอนหุ่นหายจากเกม (กลับเมนู/ตาย) — ไม่งั้นคลิกเมนูไม่ได้
342:         ReleaseCursorIfOwner();
343: 
```

```csharp
419:         }
420:     }
421: 
422:     private void HandleAttachedState()
423:     {
424:         bool isRagdoll = torso != null &&
425:             (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll ||
426:              torso.currentState.Value == TorsoMovement.TorsoState.Falling);
427: 
428:         if (isRagdoll)
429:         {
430:             _releasedForClimb = false;
431:             if (isPushingRecovery)
432:             {
433:                 footRb.isKinematic = false; // 🔓 ลุกยืน: ปล่อยให้ฟิสิกส์/ข้อต่อทำงาน กัน Solver รวน
434:                 // 🧊 แช่แข็งเท้าติดกับพื้นทันทีที่กด Q ยันตัวลุก!
435:                 ApplyFootFreeze(true);
436: 
437:                 // 🦵 ส่งแรงดึงสะโพกเข้าหาศูนย์กลางเต็ม 100% (1f) เสมอ ไม่ต้องสนใจว่าขากางไกลแค่ไหนแล้ว
438:                 torso.ApplyContinuousRecoveryForce(pivotPoint.position, 1f);
439: 
440:                 // 🚀 แรงงัดขึ้น — ✅ [4-Player Fix] หยุดอัดเมื่อตัวพุ่งขึ้นเร็วพอแล้ว
441:                 // เดิม 2 เท้าอัดพร้อมกันไม่มีเพดาน → หุ่นพุ่งขึ้นฟ้าตอนหลายคนช่วยกันกด Q
442:                 if (torso.torsoRb != null && torso.torsoRb.linearVelocity.y < torso.maxRecoveryUpVelocity)
443:                     torso.torsoRb.AddForce(Vector3.up * upwardRecoveryBoost, ForceMode.Acceleration);
444:             }
445:             else
446:             {
447:                 _isPlantedSet = false;
448:                 PerformRagdollFootPhysics();
449:             }
450:         }
451:         else
452:         {
453:             // Charge Kick owns the foot Rigidbody during the wind-up/launch/recovery window.
454:             // Do not let the standing lock or step spring cancel the kick velocity.
455:             if (IsKickControllingFoot)
456:             {
457:                 _isPlantedSet = false;
458:                 footRb.isKinematic = false;
459:                 return;
460:             }
461: 
462:             bool supportingClimb = torso != null && torso.HasSupportingHandGrab;
463: 
464:             // Keep a planted foot fixed while the leg can physically reach it. Once
465:             // the torso climbs beyond that reach, release the plant without detaching
466:             // the foot/leg joint chain, so the solver is not forced to tear the model apart.
467:             if (supportingClimb && _isPlantedSet)
468:             {
469:                 Vector3 plantedWorldPosition = plantedPosition + Vector3.up * footThicknessOffset;
470:                 if (Vector3.Distance(pivotPoint.position, plantedWorldPosition) > maxLegLength * 0.95f)
471:                 {
472:                     _releasedForClimb = true;
473:                     _isPlantedSet = false;
474:                 }
475:             }
476:             else if (!supportingClimb && _releasedForClimb && IsGrounded())
477:             {
478:                 _releasedForClimb = false;
479:             }
480: 
481:             if (_releasedForClimb || isStepping || isJumping)
482:             {
483:                 _isPlantedSet = false;
484:                 PerformSteppingPhysics();
485:             }
486:             else
487:             {
488:                 PerformStandingPhysics();
489:             }
490:         }
491:     }
492: 
493:     private void ApplyFootFreeze(bool isRecovering = false)
494:     {
```

```csharp
 655: 
 656:     // คืน true เมื่อเจอ joint อย่างน้อย 1 ตัว (สำเร็จ → เลิกเรียกซ้ำ)
 657:     // คืน false ถ้าโซ่ยังไม่พร้อม (เช่นยัง spawn ไม่ครบ) → FixedUpdate หน้าลองใหม่
 658:     private bool MakeLegJointChainUnbreakable()
 659:     {
 660:         Rigidbody currentBody = footRb;
 661:         int safety = 0;
 662:         bool foundAnyJoint = false;
 663: 
 664:         while (currentBody != null &&
 665:                (torso == null || currentBody != torso.torsoRb) &&
 666:                safety++ < 8)
 667:         {
 668:             Joint[] joints = currentBody.GetComponents<Joint>();
 669:             if (joints.Length == 0) break;
 670: 
 671:             Rigidbody nextBody = null;
 672:             foreach (Joint joint in joints)
 673:             {
 674:                 if (joint == null) continue;
 675:                 joint.breakForce = Mathf.Infinity;
 676:                 joint.breakTorque = Mathf.Infinity;
 677:                 foundAnyJoint = true;
 678: 
 679:                 if (nextBody == null && joint.connectedBody != null)
 680:                     nextBody = joint.connectedBody;
 681:             }
 682: 
 683:             currentBody = nextBody;
 684:         }
 685: 
 686:         return foundAnyJoint;
 687:     }
 688: 
 689:     private void PerformDetachedPhysics()
 690:     {
 691:         // + footThicknessOffset — เป้า detached ถูกส่งมาเป็นจุดบนพื้นตรงๆ (hit.point)
 692:         // ไม่ยกขึ้นเท้าจะพยายามเอาศูนย์กลางตัวเองมุดลงไปอยู่ระดับพื้น
 693:         Vector3 target = _detachedTargetPos + Vector3.up * footThicknessOffset;
 694:         Vector3 velTarget = (target - footRb.position) * detachedMoveSpeed;
 695:         // เพดานความเร็ว — สปริงไล่เป้าไกลๆ เคยคำนวณความเร็วมหาศาลจนเท้าปลิวหาย
 696:         velTarget = Vector3.ClampMagnitude(velTarget, maxDetachedSpeed);
 697:         footRb.AddForce((velTarget - footRb.linearVelocity) * legDamper, ForceMode.Acceleration);
 698:     }
 699: 
 700:     private void HandleInput()
 701:     {
 702:         // ✅ [Camera-Independent Aim] ล็อกเคอร์เซอร์ + คำนวณจุดเล็งเท้าในแกนโลก (เหมือนมือ)
 703:         if (useVirtualCursor) HandleFootCursorLock();
 704: 
 705:         // เคอร์เซอร์ปลดอยู่ (กด Esc ไปเมนู) → หยุดรับ input เท้าทั้งหมด
 706:         // ✅ [Stuck-State Fix] ต้องเคลียร์ state ที่ค้างอยู่ก่อนหยุด — ไม่งั้นถ้ากด Esc
 707:         // กลางก้าวเดิน/กลาง Q ค้าง server จะไม่มีวันได้รับคำสั่งหยุด เท้าเดินค้างตลอดที่อยู่ในเมนู
 708:         if (useVirtualCursor && Cursor.lockState != CursorLockMode.Locked)
 709:         {
 710:             if (isStepping)
 711:             {
 712:                 isStepping = false;
 713:                 SetSteppingStateRpc(false);
 714:                 _currentLiftOffset = 0f;
 715:             }
 716:             if (isPushingRecovery)
 717:             {
 718:                 isPushingRecovery = false;
 719:                 SetRecoveryInputRpc(false);
 720:             }
 721:             return;
 722:         }
 723: 
 724:         // 🎯 อ่านทิศทาง (เมาส์) + ระยะ (ล้อ) ครั้งเดียวต่อเฟรม แล้วรวมเป็น offset แนวราบ
 725:         UpdateFootAim();
 726:         Vector3 aimOffset = PlanarAimOffset();
 727: 
 728:         if (currentState.Value == FootState.Attached)
 729:         {
 730:             bool isRagdoll = torso != null && (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling);
 731:             // marker: ยิงลงพื้นให้ไปเกาะพื้นจริง (default = ระดับสะโพกถ้าไม่เจอพื้น)
 732:             _footMarkerWorld = Physics.Raycast(pivotPoint.position + aimOffset + Vector3.up * 5f,
 733:                     Vector3.down, out RaycastHit markerHit, 60f, groundLayer)
 734:                 ? markerHit.point
 735:                 : pivotPoint.position + aimOffset;
 736: 
 737:             // ⚖️ จุดถ่ายน้ำหนักตามทิศที่เล็ง แต่ยังคุมด้วยรัศมี mouseReach เดิม
 738:             // ระยะก้าวไกลขึ้นได้ถึง maxFootReach แต่แรงเอนลำตัวต้องไม่โตตามไปด้วย
 739:             // (ค่านี้ถูกคูณเป็นแรงดันลำตัวใน PerformStandingPhysics — โตขึ้นเมื่อไหร่ท่ายืนเพี้ยนทันที)
 740:             Vector3 balanceOffset = Vector3.ClampMagnitude(aimOffset, Mathf.Max(mouseReachX, mouseReachY));
 741:             Vector3 newBalance = pivotPoint.position + balanceOffset;
 742:             if ((newBalance - _lastSentBalance).sqrMagnitude > RPC_SEND_THRESHOLD_SQR)
 743:             {
 744:                 _lastSentBalance = newBalance;
 745:                 UpdateBalanceShiftRpc(newBalance);
 746:             }
 747: 
 748:             if (!isRagdoll)
 749:             {
 750:                 if (!_localJumpLock && Input.GetKeyDown(KeyCode.Space) && IsGrounded() && !isJumping)
 751:                 {
 752:                     _localJumpLock = true;
 753:                     ApplyJumpRpc();
 754:                 }
 755:                 if (_localJumpLock && IsGrounded() && !isJumping) _localJumpLock = false;
 756: 
 757:                 bool holdingClick = Input.GetMouseButton(0);
 758:                 // คลิกที่ใช้ดึงเมาส์กลับ ไม่นับเป็นก้าวเดิน จนกว่าจะปล่อยแล้วกดใหม่
 759:                 if (_ignoreStepUntilRelease)
 760:                 {
 761:                     if (!holdingClick) _ignoreStepUntilRelease = false;
 762:                     holdingClick = false;
 763:                 }
 764:                 // 🖱️ คลิกซ้ายค้าง = ยกขาแล้วเคลื่อนเท้าไปยังเป้า | ปล่อย = ปักเท้าลง
 765:                 if (holdingClick && !isStepping)
 766:                 {
 767:                     isStepping = true;
 768:                     SetSteppingStateRpc(true);
 769:                     // เริ่มจากความสูงปัจจุบัน (0 = ระดับพื้น) แล้วไต่ขึ้นเอง — ไม่ snap ทันที
 770:                     _currentLiftOffset = 0f;
 771:                 }
 772:                 else if (!holdingClick && isStepping) { isStepping = false; SetSteppingStateRpc(false); _currentLiftOffset = 0f; }
 773: 
 774:                 if (isStepping)
 775:                 {
 776:                     // ⬆️ [Auto Lift] ยกเท้าให้เองระหว่างก้าว — ตัด W/S ทิ้งแล้ว
 777:                     // ผู้เล่นเลือกแค่ "ทิศ" กับ "ระยะ" ส่วนแกน Y เป็นหน้าที่ของระบบ:
 778:                     // พื้น → ยกขึ้น clickLiftHeight → เคลื่อนไปเป้า → ปล่อยคลิกแล้วค่อยลงพื้นจริง
 779:                     float liftTarget = Mathf.Clamp(clickLiftHeight, 0f, maxLegLength);
 780:                     // อัตราคิดเป็นสัดส่วนของความสูงเป้า → เวลายกเท่ากันทั้งหุ่นเล็กและหุ่นสเกลยักษ์
 781:                     _currentLiftOffset = Mathf.MoveTowards(
 782:                         _currentLiftOffset, liftTarget, liftTarget * stepLiftSpeed * Time.deltaTime);
 783: 
 784:                     Vector3 planarTarget = pivotPoint.position + aimOffset;
 785:                     Vector3 newTarget;
 786:                     // 🦿 ยิง Raycast หาระดับพื้น ณ จุดที่เลือก (50m เผื่อหุ่นสเกลยักษ์สะโพกสูง)
 787:                     // แล้วยกเป้าขึ้นตามระยะยกอัตโนมัติ + ชดเชยความหนาเท้ากันจมดิน
 788:                     if (Physics.Raycast(planarTarget + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 50f, groundLayer))
 789:                         newTarget = new Vector3(planarTarget.x, hit.point.y + _currentLiftOffset + footThicknessOffset, planarTarget.z);
 790:                     else
 791:                         newTarget = planarTarget + Vector3.down * maxLegLength;
 792: 
 793:                     if ((newTarget - _lastSentTarget).sqrMagnitude > RPC_SEND_THRESHOLD_SQR) { _lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }
 794:                 }
 795: 
 796:                 if (isPushingRecovery) { isPushingRecovery = false; SetRecoveryInputRpc(false); }
 797:             }
 798:             else
 799:             {
 800:                 if (isStepping)
 801:                 {
 802:                     isStepping = false;
 803:                     SetSteppingStateRpc(false);
 804:                     _currentLiftOffset = 0f;
 805:                 }
 806: 
 807:                 Vector3 newTarget;
 808:                 // + footThicknessOffset ด้วย — เดิมโหมด Ragdoll ใช้ hit.point ดิบๆ เท้าเลยมุดพื้น
 809:                 if (Physics.Raycast(pivotPoint.position + aimOffset + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, groundLayer)) newTarget = hit.point + Vector3.up * footThicknessOffset;
 810:                 else newTarget = pivotPoint.position + aimOffset;
 811: 
 812:                 if ((newTarget - _lastSentTarget).sqrMagnitude > RPC_SEND_THRESHOLD_SQR) { _lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }
 813: 
 814:                 bool pressingQ = Input.GetKey(KeyCode.Q);
 815:                 
 816:                 // ถอดเงื่อนไขระยะห่างทิ้งไปเลย! ขอแค่กด Q และเท้าเหยียบพื้นอยู่ ก็ลุกได้ทันที
 817:                 bool validPush = pressingQ && IsGrounded();
 818:                                  
 819:                 if (validPush != isPushingRecovery) { isPushingRecovery = validPush; SetRecoveryInputRpc(validPush); }
 820:             }
 821:         }
 822:         else
 823:         {
 824:             // เท้าหลุด (Detached): ใช้ทิศ/ระยะชุดเดียวกัน แค่ขยายระยะเอื้อมออก
 825:             // ⚠️ ฐานจุดเล็งต้องเป็นสะโพก (จุดอ้างอิงนิ่ง) ห้ามใช้ตำแหน่งเท้าเอง —
 826:             // เดิมเป้า = เท้า + offset ทำให้เป้าวิ่งหนีตามเท้าไปเรื่อยๆ (feedback loop)
 827:             // ขยับเมาส์นิดเดียวเท้าเลยไล่เป้าด้วยความเร็วคงที่ไม่มีวันถึง = ปลิวหาย
 828:             Vector3 offset = aimOffset * detachedReachMultiplier;
 829:             Vector3 aimBase = pivotPoint != null ? pivotPoint.position : footRb.position;
 830:             if (Physics.Raycast(aimBase + offset + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, groundLayer))
 831:             {
 832:                 _footMarkerWorld = hit.point; // อัปเดต marker ตอน Detached ด้วย — เดิมค้างที่จุดสุดท้ายก่อนหลุด
 833:                 if ((hit.point - _lastSentDetached).sqrMagnitude > RPC_SEND_THRESHOLD_SQR) { _lastSentDetached = hit.point; UpdateDetachedTargetRpc(hit.point); }
 834:             }
 835:         }
 836:     }
 837: 
 838:     // ✅ [Pointer Lock Fix] fallback เมื่อไม่ได้ล็อกเคอร์เซอร์ — อ่านตำแหน่งเมาส์จริงบนจอ
 839:     private Vector2 GetNormalizedMousePosition() => PlayerHandMovement.AimNormalized;
 840: 
 841:     // ── Camera-Independent Foot Aim (ยกระบบมาจากมือ) ──────────────────
 842: 
 843:     private void HandleFootCursorLock()
 844:     {
 845:         // 🏁 เกมจบแล้ว (Win/GameOver panel ขึ้น) — ห้ามล็อกเมาส์กลับ ผู้เล่นต้องคลิกปุ่มบน panel
 846:         if (GameFlowManager.GameEnded) return;
 847: 
 848:         // 🖱️ มี UI เปิดอยู่ (วงล้อไอเทม/เมนู) — ห้ามแย่งเมาส์กลับ (ดู UiFocus)
 849:         if (UiFocus.IsCaptured) return;
 850: 
 851:         // Esc = ปลดล็อก เมาส์โผล่ (ไปกดเมนู)
 852:         if (Input.GetKeyDown(KeyCode.Escape))
 853:         {
 854:             Cursor.lockState = CursorLockMode.None;
 855:             Cursor.visible = true;
 856:             return;
 857:         }
 858: 
 859:         // เคอร์เซอร์ปลดอยู่ → ครั้งแรกที่เข้าเกม หรือ คลิกซ้าย = ล็อกกลับ เมาส์หาย
 860:         if (Cursor.lockState != CursorLockMode.Locked)
 861:         {
 862:             if (!_footEverLocked || Input.GetMouseButtonDown(0))
 863:             {
 864:                 LockFootCursor();
 865:                 _ignoreStepUntilRelease = true; // คลิกนี้ใช้ดึงเมาส์กลับ ห้ามไปเริ่มก้าวเดิน
 866:             }
 867:         }
 868:     }
 869: 
 870:     private void LockFootCursor()
 871:     {
 872:         // เริ่มทิศ+ระยะจากท่าเท้าจริง ณ ตอนล็อก — เท้าไม่กระโดดตำแหน่ง
 873:         Vector3 footOffset = footRb.position - pivotPoint.position;
 874:         Vector3 planar = new Vector3(footOffset.x, 0f, footOffset.z);
 875:         if (planar.sqrMagnitude > 0.0001f)
 876:         {
 877:             _footDirection = planar.normalized;
 878:             _footAimStick  = _footDirection; // ก้านชี้ทิศเดิม เมาส์ครั้งต่อไป = หมุนทิศต่อจากนี้
 879:             _currentFootDistance = Mathf.Clamp(planar.magnitude, minFootReach, maxFootReach);
 880:         }
 881: 
 882:         Cursor.lockState = CursorLockMode.Locked;
 883:         Cursor.visible = false;
 884:         _footEverLocked = true;
 885:     }
 886: 
 887:     // ── Direction (เมาส์) + Distance (ล้อ) ─────────────────────────────
 888:     //
 889:     // 🎯 หลักการ: เมาส์เลือก "ไปทางไหน" ล้อเลือก "ไปไกลแค่ไหน" — อิสระจากกันสนิท
 890:     //
 891:     // ทิศทางเก็บเป็นเวกเตอร์ในแกนโลก ไม่ใช่แกนกล้อง (World-Anchored Aim ของเดิม)
 892:     // → หันกล้องแล้วเท้าไม่กวาดตาม | คลิกขวาค้าง = เมาส์และล้อเป็นของกล้องล้วน
 893:     //   ทิศ/ระยะค้างไว้เป๊ะ ปล่อยคลิกขวาแล้วคุมต่อจากค่าเดิมทันที ไม่มี reset ไม่มีกระโดด
 894:     private void UpdateFootAim()
 895:     {
 896:         Vector3 camFwd   = playerCamera.transform.forward; camFwd.y = 0f;
 897:         Vector3 camRight = playerCamera.transform.right;   camRight.y = 0f;
 898:         // กล้องก้มดิ่ง 90° → forward แบนราบเหลือ ~0 ใช้ up มาแทนทิศ "หน้าจอด้านบน"
 899:         if (camFwd.sqrMagnitude < 0.0001f)
 900:         {
 901:             camFwd = playerCamera.transform.up; camFwd.y = 0f;
 902:             if (camFwd.sqrMagnitude < 0.0001f) camFwd = Vector3.forward;
 903:         }
 904:         camFwd.Normalize();
 905:         camRight.Normalize();
 906: 
 907:         bool cameraMode = Input.GetMouseButton(1); // คลิกขวาค้าง = โหมดกล้อง
 908: 
 909:         // ── ทิศทาง ──────────────────────────────────────────────────
 910:         if (useVirtualCursor && Cursor.lockState == CursorLockMode.Locked)
 911:         {
 912:             if (!cameraMode)
 913:             {
 914:                 Vector2 md = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) *
 915:                              (mouseSensitivity * MouseSettings.Multiplier * 0.1f);
 916:                 _footAimStick += camRight * md.x + camFwd * md.y;
 917:                 _footAimStick.y = 0f;
 918:                 // ก้านถูก clamp ที่รัศมี 1 — ความยาวไม่มีผลกับระยะก้าวเลย ใช้แค่ทิศของมัน
 919:                 // ผลคือพอดันเมาส์จนสุดขอบก้าน การขยับต่อ = "หมุนทิศรอบตัว" ซึ่งคือสิ่งที่ต้องการ
 920:                 if (_footAimStick.sqrMagnitude > 1f) _footAimStick.Normalize();
 921:             }
 922:         }
 923:         else
 924:         {
 925:             // fallback: ไม่ได้ใช้ virtual cursor → ตำแหน่งเมาส์บนจอทำหน้าที่เป็นก้านทิศทาง
 926:             Vector2 mouseNorm = GetNormalizedMousePosition();
 927:             Vector3 stick = camRight * mouseNorm.x + camFwd * mouseNorm.y;
 928:             stick.y = 0f;
 929:             _footAimStick = stick.sqrMagnitude > 1f ? stick.normalized : stick;
 930:         }
 931: 
 932:         // ก้านอยู่กลาง (แทบไม่มีทิศ) = คงทิศเดิมไว้ ห้ามให้ทิศสะบัดมั่วตอนผู้เล่นหยุดมือ
 933:         if (_footAimStick.sqrMagnitude > AIM_STICK_DEADZONE_SQR)
 934:             _footDirection = _footAimStick.normalized;
 935: 
 936:         // ── ระยะ ────────────────────────────────────────────────────
 937:         if (!cameraMode)
 938:         {
 939:             float scroll = Input.GetAxis("Mouse ScrollWheel");
 940:             if (Mathf.Abs(scroll) > 0.0001f)
 941:             {
 942:                 float notches = scroll / SCROLL_NOTCH;
 943:                 _currentFootDistance += notches * footReachScrollSpeed * (maxFootReach - minFootReach);
 944:             }
 945: 
 946:             // จองล้อทุกเฟรมที่ขาถือสิทธิ์อยู่ (ไม่ใช่เฉพาะเฟรมที่หมุนจริง) — ธงจะได้นิ่ง
 947:             // ไม่กะพริบจนกล้องแอบซูมแทรกระหว่างคลิกล้อสองครั้ง
 948:             MouseWheelFocus.Claim();
 949:         }
 950: 
 951:         _currentFootDistance = Mathf.Clamp(_currentFootDistance, minFootReach, maxFootReach);
 952:     }
 953: 
 954:     /// <summary>เป้าหมายแนวราบ = ทิศทาง × ระยะ (offset จากสะโพก)</summary>
 955:     private Vector3 PlanarAimOffset() => _footDirection * _currentFootDistance;
 956: 
 957:     public Vector3 GetKickAimDirection()
 958:     {
 959:         // ทิศเตะ = ทิศที่ผู้เล่นเล็งไว้ตรงๆ ไม่ต้องคำนวณย้อนจากจุด marker อีกแล้ว
 960:         // (ระบบเก่าอนุมานทิศจาก marker ลบสะโพก ซึ่งยุบเป็นศูนย์เวลาเท้าลอยอยู่เหนือ marker พอดี)
 961:         if (_footDirection.sqrMagnitude > 0.001f)
 962:             return _footDirection.normalized;
 963: 
 964:         Vector3 direction = pivotPoint != null ? pivotPoint.forward : transform.forward;
 965:         direction.y = 0f;
 966:         if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
 967: 
 968:         // A push kick travels horizontally. Upward bias made the whole robot hop
 969:         // and read visually as a jump instead of a thrust.
 970:         return direction.normalized;
 971:     }
 972: 
 973:     private void OnGUI()
 974:     {
 975:         if (!IsOwner || !useVirtualCursor || !showCrosshair) return;
 976:         if (Cursor.lockState != CursorLockMode.Locked) return;
 977:         if (playerCamera == null) return;
 978: 
 979:         if (_footCrosshairStyle == null)
 980:             _footCrosshairStyle = new GUIStyle(GUI.skin.label)
 981:             { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
 982: 
 983:         Vector3 screenPos = playerCamera.WorldToScreenPoint(_footMarkerWorld);
 984:         if (screenPos.z <= 0f) return;
 985:         float sx = screenPos.x, sy = Screen.height - screenPos.y;
 986: 
 987:         GUI.color = new Color(0f, 0f, 0f, 0.6f);
 988:         GUI.Label(new Rect(sx - 13f, sy - 13f, 30f, 30f), "◈", _footCrosshairStyle);
 989:         GUI.color = new Color(0.5f, 0.85f, 1f);
 990:         GUI.Label(new Rect(sx - 14f, sy - 14f, 30f, 30f), "◈", _footCrosshairStyle);
 991: 
 992:         // ระยะที่เลือกด้วยล้อเมาส์ — ไม่มีตัวเลขนี้ผู้เล่นจะไม่รู้เลยว่าหมุนล้อไปถึงไหนแล้ว
 993:         if (_footReachStyle == null)
 994:             _footReachStyle = new GUIStyle(GUI.skin.label)
 995:             { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
 996: 
 997:         string reachText = $"{Mathf.RoundToInt(NormalizedFootReach * 100f)}%";
 998:         GUI.color = new Color(0f, 0f, 0f, 0.6f);
 999:         GUI.Label(new Rect(sx - 29f, sy + 13f, 60f, 20f), reachText, _footReachStyle);
1000:         GUI.color = new Color(0.5f, 0.85f, 1f);
1001:         GUI.Label(new Rect(sx - 30f, sy + 12f, 60f, 20f), reachText, _footReachStyle);
1002:     }
1003: 
1004:     // ✅ [Reliability Fix] เปลี่ยน footTarget/balanceShift จาก Unreliable → Reliable
1005:     // เดิมถ้าแพ็กเก็ตหลุดกลางอากาศตอนเดิน Server จะค้างใช้เป้าหมายเก่า พอแพ็กเก็ตใหม่มาถึงทีหลัง
1006:     // ตำแหน่งกระโดดข้ามแบบกระแทก ดูเหมือนเท้ากระตุก/หลุดจากพื้น — อัตราส่งถูกจำกัดด้วย
1007:     // RPC_SEND_THRESHOLD อยู่แล้ว ต้นทุน bandwidth ของ Reliable จึงต่ำมาก
1008:     [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void UpdateFootTargetRpc(Vector3 v) { ValidateAndSetFootTarget(v); }
1009:     [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void UpdateBalanceShiftRpc(Vector3 v) { ValidateAndSetBalanceShift(v); }
1010:     [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)] private void UpdateDetachedTargetRpc(Vector3 v) { if (v.IsValid()) _detachedTargetPos = v; }
1011:     [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void SetSteppingStateRpc(bool v) { isStepping = v; }
1012:     [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void SetRecoveryInputRpc(bool v) { isPushingRecovery = v; }
1013: 
1014:     [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
1015:     private void ApplyJumpRpc()
1016:     {
1017:         // 🛡️ [Server Validation] เช็กซ้ำฝั่ง server เสมอ — เงื่อนไขฝั่ง client เชื่อไม่ได้
1018:         // กัน client ยิง RPC รัวๆ ให้หุ่นลอยขึ้นฟ้า (เงื่อนไขเดียวกับที่ HandleInput เช็กก่อนส่ง)
```

```csharp
1071:         _balanceShiftPos = target;
1072:     }
1073: 
1074:     public bool IsGrounded()
1075:     {
1076:         if (footRb == null || pivotPoint == null) return false;
1077:         Vector3 point1 = pivotPoint.position;
1078:         Vector3 point2 = footRb.position;
1079:         return Physics.CheckCapsule(point1, point2, groundCheckDistance, groundLayer);
1080:     }
1081: 
1082:     /// <summary>
1083:     /// เรียกจาก RespawnManager (ฝั่ง server) หลัง teleport ร่างกลับเช็คพอยต์
1084:     /// สำคัญสุดคือ _isPlantedSet — plantedPosition เก่ายังชี้จุดตกเหว ถ้าไม่ล้าง
1085:     /// PerformStandingPhysics จะ MovePosition ลากเท้ากลับไปก้นเหวในเฟรมถัดไปทันที
1086:     /// และรีเซ็ตเป้าทุกตัวเป็นตำแหน่งปัจจุบัน (หลัง teleport แล้ว) กันสปริงไล่เป้าเก่า
1087:     /// </summary>
1088:     public void ResetForRespawn()
1089:     {
1090:         if (!IsServer) return;
1091: 
1092:         _isPlantedSet     = false;
1093:         _releasedForClimb = false;
1094:         isStepping        = false;
1095:         isJumping         = false;
1096:         isPushingRecovery = false;
1097:         _jumpCooldownTimer = 0f;
1098:         if (_legCombat == null)
1099:             _legCombat = GetComponent<PlayerLegCombat>();
1100:         _legCombat?.ResetForRespawn();
1101: 
1102:         if (footRb != null)
1103:         {
1104:             _targetFootPos     = footRb.position;
1105:             _detachedTargetPos = footRb.position;
1106:             _balanceShiftPos   = pivotPoint != null ? pivotPoint.position : footRb.position;
1107:         }
1108: 
1109:         // Owner ถือทิศ/ระยะ/สถานะปุ่มไว้ในเครื่องตัวเอง — ถ้าไม่สั่งล้างด้วย มันจะไม่ตรงกับ server
1110:         // ที่เพิ่งถูกรีเซ็ตไป (โดยเฉพาะ isStepping: owner ที่ยังกดคลิกซ้ายค้างอยู่จะไม่ส่ง
1111:         // SetSteppingStateRpc ซ้ำ เพราะฝั่งมันไม่เห็นว่าค่าเปลี่ยน → เท้าค้างไม่ยอมก้าวอีกเลย)
1112:         if (IsSpawned) ResetAimOnOwnerRpc();
1113:     }
1114: 
1115:     [Rpc(SendTo.Owner, Delivery = RpcDelivery.Reliable)]
1116:     private void ResetAimOnOwnerRpc()
1117:     {
1118:         isStepping = false;
1119:         isPushingRecovery = false;
1120:         _ignoreStepUntilRelease = true; // ต้องปล่อยคลิกซ้ายก่อน ถึงจะเริ่มก้าวใหม่ได้
1121: 
1122:         ApplyAutoFootReach();
1123:         ResetFootAim();
1124: 
1125:         // ล้างแคชกันส่งซ้ำ — เป้าใหม่หลัง teleport ต้องถูกส่งทันทีแม้จะบังเอิญใกล้ค่าเดิม
1126:         _lastSentTarget = _lastSentBalance = _lastSentDetached = Vector3.positiveInfinity;
1127:     }
1128: }
1129: 
```

## PlayerHandMovement.cs

`Assets/Scenes/TheBestFolder/Mynigga/player/PlayerHandMovement.cs` (996 บรรทัด)

```csharp
278:         if (handRb == null) return; // 🛡️ กัน NRE ถ้า ref หลุด/ถูก despawn (แบบเดียวกับเท้า)
279:         SmoothHandTarget();
280: 
281:         if (currentState.Value == HandState.Attached)
282:         {
283:             PerformArmMovement();
284:         }
285:     }
286: 
287:     private void SmoothHandTarget()
```

```csharp
422: 
423:     protected virtual void HandleInput()
424:     {
425:         if (currentState.Value != HandState.Attached) return;
426: 
427:         // ✅ [Pointer Lock] จัดการล็อก/ปลดล็อกเคอร์เซอร์ (เฉพาะโหมด Virtual Cursor)
428:         if (useVirtualCursor) HandleCursorLock();
```

```csharp
549:     /// ถ้ากดแล้วยังไม่มีอะไรให้จับ จะลองใหม่เป็นระยะ (grabRetryInterval) ไม่ใช่ทุกเฟรม
550:     /// ผู้เล่นจึงค้าง F ไว้แล้วขยับมือเข้าไปหาวัตถุให้มันจับติดเองได้ โดยไม่สแปม RPC
551:     /// </summary>
552:     private void HandleGrabInput()
553:     {
554:         bool torsoDown = torso != null &&
555:             (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll ||
556:              torso.currentState.Value == TorsoMovement.TorsoState.Falling);
557: 
558:         if (Input.GetKeyDown(KeyCode.F))
559:         {
560:             grabHeld = true;
561:             nextGrabRetryTime = 0f; // ขอทันทีในเฟรมนี้เลย
562:         }
563: 
564:         if (Input.GetKeyUp(KeyCode.F) && grabHeld)
565:         {
566:             grabHeld = false;
567:             grabActive = false;
568:             // ส่งเสมอ (ไม่ใช่เฉพาะตอน grabActive) — กันเคสที่ RPC ตอบกลับยังไม่ถึงเรา
569:             // แต่ server จับติดไปแล้ว | ServerReleaseGrab ปลอดภัยแม้ไม่มีอะไรถูกจับอยู่
570:             ReleaseGrabRpc();
571:             return;
572:         }
573: 
574:         if (!grabHeld || grabActive) return;
575: 
576:         // ตัวล้มอยู่ = เริ่มจับใหม่ไม่ได้ (กฎเดิมของ TryGrabRpc ฝั่ง server)
577:         // เช็คซ้ำฝั่ง client เพื่อไม่ให้ยิง RPC ที่รู้ผลอยู่แล้วว่าถูกปฏิเสธ
578:         if (torsoDown) return;
579: 
580:         if (Time.time < nextGrabRetryTime) return;
581:         nextGrabRetryTime = Time.time + grabRetryInterval;
582:         TryGrabRpc();
583:     }
584:     
585:     [Rpc(SendTo.Server)] private void UpdateHandTargetRpc(Vector3 target) { ValidateAndSetHandTarget(target); }
586:     [Rpc(SendTo.Server)] private void ApplyHandRecoveryRpc() { if (torso != null) torso.ApplyContinuousRecoveryForce(PivotPosition); }
587: 
588:     // 🛡️ [Server Validation] แบบเดียวกับ ValidateAndSetFootTarget ฝั่งขา —
589:     // NaN หลุดเข้ามาพังทั้งโซ่ฟิสิกส์แขน / พิกัดไกลเกินก็ถูกดึงกลับเข้าระยะเล็งจริง
590:     private const float SERVER_REACH_MARGIN = 1.5f;
591:     private void ValidateAndSetHandTarget(Vector3 target)
592:     {
593:         if (!target.IsValid()) return;
594: 
595:         // 🛡️ เพดานอิงระยะที่แขนเอื้อมถึงจริง ไม่ใช่ค่าความไวเมาส์อีกต่อไป
596:         // (เดิมใช้ max ของ mouseReachX/Y/Depth ซึ่งเป็นสเกลความไว ไม่ได้แปลว่าระยะแขน
597:         //  ปรับความไวขึ้นเมื่อไหร่เพดาน validate ก็หลวมตามไปด้วยโดยไม่มีใครตั้งใจ)
598:         float limit = Mathf.Max(maxHandDepth, maxArmLength) * SERVER_REACH_MARGIN;
599: 
600:         Vector3 pivot = PivotPosition;
601:         Vector3 dir = target - pivot;
602:         if (dir.magnitude > limit) target = pivot + dir.normalized * limit;
603: 
604:         targetHandPosition = target;
605:     }
606: 
607:     [Rpc(SendTo.Server)]
608:     private void TryGrabRpc()
609:     {
610:         // Server authority: do not allow a client to start a new grab while ragdolled.
611:         if (torso != null &&
612:             (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll ||
613:              torso.currentState.Value == TorsoMovement.TorsoState.Falling))
614:         {
615:             ForceReleaseGrabClientRpc();
616:             return;
617:         }
618: 
619:         // 🛡️ กันจับซ้อน — ถ้า RPC เข้ามาซ้ำตอนมี joint อยู่แล้ว FixedJoint ตัวใหม่จะทับ field
620:         // แต่ตัวเก่ายังเกาะ handRb ถาวร = ของที่เคยจับติดมือตลอดกาล
621:         if (isGrabbing || grabJoint != null || handRb == null) return;
622: 
623:         Collider[] hits = Physics.OverlapSphere(GrabPosition, grabRadius, grabLayer);
624:         bool grabbedSomething = false;
625: 
626:         foreach (var h in hits)
627:         {
628:             Rigidbody rb = h.attachedRigidbody;
629:             if (rb == null) continue;
630:             isGrabbing = true;
631:             grabbedObject = rb;
632:             grabbedSomething = true;
633: 
634:             // ✅ จับได้ทั้ง Kinematic และ Dynamic
635:             grabJoint = handRb.gameObject.AddComponent<FixedJoint>();
636:             grabJoint.connectedBody = rb;
637:             float jointBreakLimit = preventGrabBreakWhileRagdoll
638:                 ? Mathf.Infinity
639:                 : grabBreakForce;
640:             grabJoint.breakForce = jointBreakLimit;
641:             grabJoint.breakTorque = jointBreakLimit;
642: 
643:             // ปิดการชนระหว่างของที่ถูกจับกับลำตัวหุ่น เพื่อป้องกันบั๊กบินขึ้นฟ้า
644:             IgnoreCollisionWithTorso(grabbedObject, true);
645: 
646:             break;
647:         }
648: 
649:         // แจ้งผล owner เสมอ — สำเร็จ = หยุด retry / พลาด = ปล่อยให้ retry ต่อขณะยังกด F ค้าง
650:         // (เดิมแจ้งเฉพาะตอนพลาด เพราะระบบ toggle ไม่ต้องรู้ว่าสำเร็จเมื่อไหร่)
651:         if (grabbedSomething)
652:             GrabSucceededClientRpc();
653:         else
654:             ForceReleaseGrabClientRpc();
655:     }
656: 
657:     [Rpc(SendTo.Owner)]
658:     private void GrabSucceededClientRpc()
659:     {
660:         grabActive = true;
661:     }
662: 
663:     [Rpc(SendTo.Server)]
664:     private void ReleaseGrabRpc() => ServerReleaseGrab();
665: 
666:     /// <summary>
667:     /// ปล่อย grab ฝั่ง server — แยกออกจาก RPC เพื่อให้ระบบอื่น (เช่น RespawnManager)
668:     /// สั่งปล่อยได้โดยตรง พร้อมแจ้ง owner ให้รีเซ็ต toggle F ด้วยเสมอ
669:     /// </summary>
670:     public void ServerReleaseGrab()
671:     {
672:         if (!IsServer) return;
673: 
674:         isGrabbing = false;
675:         if (grabJoint != null)
676:         {
677:             Destroy(grabJoint);
678:             grabJoint = null;
679:         }
680: 
681:         if (grabbedObject != null)
682:         {
683:             IgnoreCollisionWithTorso(grabbedObject, false);
684:             grabbedObject = null;
685:         }
686: 
687:         if (IsSpawned) ForceReleaseGrabClientRpc();
688:     }
689: 
690:     /// <summary>
691:     /// เรียกจาก RespawnManager (ฝั่ง server) หลัง teleport ร่างกลับเช็คพอยต์
692:     /// ปล่อย grab + รีเซ็ตเป้ามือเป็นตำแหน่งปัจจุบัน กันสปริงไล่เป้าเก่าที่จุดตกเหว
693:     /// </summary>
694:     public void ResetForRespawn()
695:     {
696:         if (!IsServer) return;
697: 
698:         ServerReleaseGrab();
699: 
700:         Vector3 rest = handRb != null ? handRb.position : PivotPosition;
701:         targetHandPosition = rest;
702:         smoothedHandTarget = rest;
703:         _smoothVelocityRef = Vector3.zero;
704: 
705:         // Owner ถือจุดเล็ง/สถานะปุ่มไว้ในเครื่องตัวเอง ต้องสั่งล้างด้วย ไม่งั้นเป้าเก่า
706:         // (ที่จุดตกเหว) จะถูกส่งกลับมาทับทันทีในเฟรมถัดไป
707:         if (IsSpawned) ResetAimOnOwnerRpc();
708:     }
709: 
710:     [Rpc(SendTo.Owner, Delivery = RpcDelivery.Reliable)]
711:     private void ResetAimOnOwnerRpc()
712:     {
713:         grabHeld = false;
714:         grabActive = false;
715:         ignoreClickUntilRelease = true; // ต้องปล่อยคลิกซ้ายก่อน ถึงจะต่อยครั้งใหม่ได้
716: 
717:         ApplyAutoHandDepth();
718:         SeedAimFromCurrentHandPose();
719: 
720:         // ล้างแคชกันส่งซ้ำ — เป้าใหม่หลัง teleport ต้องถูกส่งทันทีแม้บังเอิญใกล้ค่าเดิม
721:         lastSentTarget = Vector3.positiveInfinity;
722:     }
723: 
724:     private void IgnoreCollisionWithTorso(Rigidbody targetRb, bool ignore)
725:     {
726:         if (targetRb == null || torso == null) return;
727:         Collider[] targetCols = targetRb.GetComponentsInChildren<Collider>();
728:         Collider[] torsoCols = torso.GetComponentsInChildren<Collider>();
729:         foreach (var tc in torsoCols)
730:         {
731:             foreach (var gc in targetCols)
732:             {
733:                 Physics.IgnoreCollision(tc, gc, ignore);
734:             }
735:         }
736:     }
737: 
738:     void OnJointBreak(float breakForce)
739:     {
740:         Debug.Log($"Hand joint broke due to massive force: {breakForce}");
741:         isGrabbing = false;
742:         
743:         if (grabbedObject != null)
744:         {
745:             IgnoreCollisionWithTorso(grabbedObject, false);
746:             grabbedObject = null;
747:         }
748:         
749:         if (IsServer)
750:         {
751:             ForceReleaseGrabClientRpc();
752:         }
753:     }
754: 
755:     [Rpc(SendTo.Owner)]
756:     private void ForceReleaseGrabClientRpc()
757:     {
758:         // จับไม่ติด / ถูกสั่งปล่อยจากฝั่ง server (respawn, joint แตก, ล้ม)
759:         // ⚠️ ไม่แตะ grabHeld — ผู้เล่นยังกด F ค้างอยู่ก็ให้ retry ต่อได้ตามข้อกำหนด
760:         grabActive = false;
761:     }
762: 
763:     protected virtual void PerformArmMovement()
764:     {
```

```csharp
800:             if (kinematicGrabAddsStress)
801:             {
802:                 float stressThisFrame = kinematicPullForce * Time.fixedDeltaTime * Mathf.Clamp01(currentDistance / maxArmLength);
803:                 torso.AddStress(stressThisFrame);
804:             }
805:             torso.armPullIntensity = Mathf.Clamp01(currentDistance / maxArmLength);
806:         }
807:         else
808:         {
809:             torso.armPullIntensity = 0f;
810: 
811:             if (currentDistance < 0.05f)
812:             {
```

```csharp
856:                 // ──────────────────────────────────────────────────────────────
857: 
858:                 float stressThisFrame = torsoPullForce * Time.fixedDeltaTime * 0.5f;
859:                 torso.AddStress(stressThisFrame);
860:                 } // end standing or holding a pre-ragdoll grab
861:             }
862: 
```

```csharp
888:         }
889:     }
890: 
891:     private void ProtectArmJointChain(bool protect)
892:     {
893:         if (!protect)
894:         {
895:             foreach (var savedLimit in protectedArmJointLimits)
896:             {
897:                 if (savedLimit.Key == null) continue;
898:                 savedLimit.Key.breakForce = savedLimit.Value.x;
899:                 savedLimit.Key.breakTorque = savedLimit.Value.y;
900:             }
901:             protectedArmJointLimits.Clear();
902:             _armJointsProtected = false;
903:             RestoreArmMasses();
904:             return;
905:         }
906: 
907:         bool stabilizing = stabilizeArmChainWhileGrabbing && HasSupportingGrab;
908: 
909:         // ✅ เลิกจับแล้ว → คืนมวลแขนเป็นค่าดั้งเดิม (เดิมมวลถูกเขียนทับถาวรตั้งแต่ปีนครั้งแรก
910:         // ฟิสิกส์แขน/น้ำหนักหมัดเปลี่ยนไปตลอดชีวิตโดยไม่มีใครรู้)
911:         if (!stabilizing)
912:         {
913:             RestoreArmMasses();
914:             _armChainStabilized = false;
915:         }
916: 
917:         // ⚡ ทุกอย่างถูกตั้งครบแล้วและไม่มีอะไรเปลี่ยน → ไม่ต้องเดินโซ่ซ้ำทุก tick
918:         if (_armJointsProtected && (!stabilizing || _armChainStabilized))
919:             return;
920: 
921:         Rigidbody currentBody = handRb;
922:         int safety = 0;
923:         bool foundAnyJoint = false;
924: 
925:         while (currentBody != null &&
926:                (torso == null || currentBody != torso.torsoRb) &&
927:                safety++ < 8)
928:         {
929:             if (stabilizing)
930:             {
931:                 if (!originalArmMasses.ContainsKey(currentBody))
932:                     originalArmMasses.Add(currentBody, currentBody.mass);
933: 
934:                 currentBody.mass = Mathf.Max(0.01f, stabilizedArmMass);
935:                 currentBody.solverIterations = Mathf.Max(currentBody.solverIterations, stabilizedArmSolverIterations);
936:                 currentBody.solverVelocityIterations = Mathf.Max(currentBody.solverVelocityIterations, stabilizedArmSolverVelocityIterations);
937:             }
938: 
939:             Joint[] armJoints = currentBody.GetComponents<Joint>();
940:             if (armJoints.Length == 0) break;
941: 
942:             Rigidbody nextBody = null;
943:             foreach (Joint armJoint in armJoints)
944:             {
945:                 if (armJoint == null) continue;
946: 
947:                 if (!protectedArmJointLimits.ContainsKey(armJoint))
948:                 {
949:                     protectedArmJointLimits.Add(
950:                         armJoint,
951:                         new Vector2(armJoint.breakForce, armJoint.breakTorque));
952:                 }
953: 
954:                 armJoint.breakForce = Mathf.Infinity;
955:                 armJoint.breakTorque = Mathf.Infinity;
956:                 foundAnyJoint = true;
957: 
958:                 // The runtime grab FixedJoint points toward the grabbed object, not
959:                 // toward the torso, so never use it to continue the limb traversal.
960:                 if (nextBody == null && armJoint != grabJoint && armJoint.connectedBody != null)
961:                     nextBody = armJoint.connectedBody;
962:             }
963: 
964:             currentBody = nextBody;
965:         }
966: 
967:         if (foundAnyJoint) _armJointsProtected = true;
968:         if (stabilizing) _armChainStabilized = true;
969:     }
970: 
971:     private void RestoreArmMasses()
972:     {
```

## PvpRobotTeam.cs

`Assets/nok/PVP/PvpRobotTeam.cs` (322 บรรทัด)

```csharp
158:         }
159: 
160:         /// <summary>
161:         /// [SERVER] ดาเมจที่ลงลำตัว (หรือชิ้นที่ไม่มี RobotHealth ของตัวเอง)
162:         ///
163:         /// ทำไมต้องมี: ลำตัวหุ่นไม่มี RobotHealth เพราะมันไม่มี joint ให้หลุด
164:         /// แต่ผู้เล่นย่อมเล็งลำตัวเพราะเป็นเป้าใหญ่สุด ถ้าไม่นับดาเมจเลยเกมจะรู้สึกพัง
165:         ///
166:         /// วิธีคิด: โยนดาเมจเต็มจำนวนให้ชิ้นส่วนที่ "ใกล้จุดปะทะที่สุด" ชิ้นเดียว
167:         ///
168:         /// ⚠️ ห้ามหารกระจายให้ทุกชิ้นเท่าๆ กัน — ทุกชิ้นเลือดเท่ากัน (500) ถ้าโดนเท่ากันทุกหมัด
169:         ///    มันจะถึง 0 พร้อมกันเป๊ะ แล้ว "หลุดหมดทั้ง 4 ชิ้นพร้อมกัน" ซึ่งพังทั้งเกมเพลย์
170:         ///    (เงื่อนไขชนะคือหลุดครบ 4 → หมัดเดียวจบเกม) และดูไม่เป็นธรรมชาติ
171:         /// </summary>
172:         /// <returns>true ถ้ามีชิ้นส่วนรับดาเมจได้จริง</returns>
173:         public bool ServerApplyBodyDamage(float damage, AttackType source, Vector3 direction, Vector3 hitPoint)
174:         {
175:             if (!IsServer || damage <= 0f) return false;
176: 
177:             RobotHealth nearest = null;
178:             float nearestSqr = float.MaxValue;
179: 
180:             for (int i = 0; i < _limbHealths.Count; i++)
181:             {
182:                 RobotHealth h = _limbHealths[i];
183:                 if (!IsLimbDamageable(h)) continue; // ชิ้นที่หลุด/พังแล้วไม่รับดาเมจซ้ำ
184: 
185:                 float sqr = (h.transform.position - hitPoint).sqrMagnitude;
186:                 if (sqr < nearestSqr)
187:                 {
188:                     nearestSqr = sqr;
189:                     nearest = h;
190:                 }
191:             }
192: 
193:             if (nearest == null) return false;
194: 
195:             float hpBefore = nearest.currentHp.Value;
196:             nearest.ServerTakeDamage(damage, source, direction);
197: 
198:             if (debugLog)
199:                 Debug.Log($"[PVP] 🫀 ลำตัวทีม {team.DisplayName()} รับ {damage:F1} " +
```

## RespawnManager.cs

`Assets/nok/SaveScript/RespawnManager.cs` (283 บรรทัด)

```csharp
50: 
51:     // ✅ [Gameplay Reset] สคริปต์เกมเพลย์ที่ต้องรีเซ็ต state หลัง teleport
52:     // ไม่งั้นเท้าจะ MovePosition กลับไปจุดปักเก่าที่ก้นเหว / มือลาก joint ที่ยังจับกำแพงเดิม
53:     private TorsoMovement torsoMovement;
54:     private PlayerFootForRobot[] feet;
55:     private PlayerHandMovement[] hands;
56: 
57:     // เก็บตำแหน่ง/หมุนของแต่ละ limb แบบ "สัมพัทธ์กับ bodyRoot" ตอนเริ่มเกม
58:     // ใช้คำนวณตำแหน่งเป้าหมายตอน teleport แทนที่จะปล่อยให้ joint ดึงเอง (ซึ่งพังถ้าระยะไกล)
```

```csharp
106:             }
107:         }
108: 
109:         // เก็บ joint ทุกตัวใต้ bodyRoot (รวมลูกทั้งหมด) เพื่อปลด/ต่อ connectedBody ตอน teleport
110:         // (Joint สืบทอดจาก Component ตรงๆ ไม่ใช่ Behaviour เลยไม่มี .enabled ให้ปิด/เปิดแบบ Collider/Renderer)
111:         if (bodyRoot != null)
112:         {
113:             allJoints = bodyRoot.GetComponentsInChildren<Joint>(includeInactive: true);
114:         }
115:         else
116:         {
117:             allJoints = new Joint[0];
118:         }
119: 
120:         jointConnectedBodies = new Rigidbody[allJoints.Length];
121:         for (int i = 0; i < allJoints.Length; i++)
122:         {
123:             if (allJoints[i] != null)
124:                 jointConnectedBodies[i] = allJoints[i].connectedBody;
125:         }
126:     }
127: 
128:     public override void OnNetworkSpawn()
```

```csharp
183:         //    การกระโดดตำแหน่งทันที ไม่ใช่การเคลื่อนที่ปกติที่ต้อง interpolate
184:         TeleportTransform(bodyRoot, bodyRootNetTransform, pos, rot);
185: 
186:         // 3) Server teleport ทุก limb เองหมด — โปรเจกต์นี้ limb เป็น server-authoritative
187:         //    (ฟิสิกส์รันบน server, NetworkTransform sync ลง client / ChangeOwnership แค่บอกว่า
188:         //    ใครคุม input) เวอร์ชันก่อนส่ง RPC ให้ owner teleport เอง ซึ่งใช้ไม่ได้:
189:         //    client ไม่มี authority บน NetworkTransform → Teleport ฝั่งนั้นถูกปัดทิ้ง
190:         //    ผลคือแขนขาผู้เล่นค้างที่จุดตายในขณะที่ลำตัววาร์ปไปแล้ว
191:         for (int i = 0; i < limbRigidbodies.Length; i++)
192:         {
193:             var rb = limbRigidbodies[i];
```

```csharp
214:         //    เท้า: plantedPosition เก่าชี้จุดตกเหว ถ้าไม่ล้าง PerformStandingPhysics
215:         //          จะ MovePosition ลากเท้ากลับไปก้นเหวในเฟรมถัดไปทันที
216:         //    torso: ล้าง stress/timer ทั้งหมด แล้วตั้งกลับเป็นท่ายืน (limb ถูกจัดท่ายืนให้แล้ว)
217:         if (feet != null)
218:             foreach (var foot in feet)
219:                 if (foot != null) foot.ResetForRespawn();
220: 
221:         if (hands != null)
222:             foreach (var hand in hands)
223:                 if (hand != null) hand.ResetForRespawn();
224: 
225:         if (torsoMovement != null) torsoMovement.ResetForRespawn();
226: 
227:         RespawnFeedbackClientRpc();
228:     }
```

## RobotHealth.cs

`Assets/Scenes/Enemy AndInteractive Object/RobotHealth.cs` (229 บรรทัด)

```csharp
69:         base.OnNetworkDespawn();
70:     }
71: 
72:     private void OnPartConnectionChanged(bool connected)
73:     {
74:         if (!IsServer) return;
75:         if (connected)
76:         {
77:             currentHp.Value = currentMaxHp.Value;
78:             regening = true;
79:             timer = 0;
80:         }
81:     }
82: 
83:     private void Update()
84:     {
```

```csharp
170:     /// ชิ้นนี้ยังกินดาเมจได้อยู่ไหม — แยกออกมาเป็นฟังก์ชันเพื่อให้เอฟเฟคตอนโดนต่อย
171:     /// ใช้เงื่อนไขชุดเดียวกับดาเมจเป๊ะๆ (ชิ้นที่หลุด/พังแล้วจะได้ไม่เด้งเอฟเฟคลอยๆ)
172:     /// </summary>
173:     private bool CanTakeDamage()
174:     {
175:         // ✅ ชิ้นที่หลุดไปแล้วไม่รับดาเมจ — ไม่งั้นเลือดที่ regen ระหว่างหลุด
176:         // จะโดนตีจนหมดซ้ำ → Break() ซ้ำ → เพดานเลือดโดนหักรัวๆ ทั้งที่หลุดไปแล้ว
177:         if (Jpar != null && !Jpar.IsConnected) return false;
178: 
179:         if (currentHp.Value <= 0) return false; // พังไปแล้ว ไม่ต้องลบเลือดซ้ำ
180: 
181:         return true;
182:     }
183: 
184:     private void ApplyDamage(float amount)
185:     {
186:         if (!CanTakeDamage()) return;
187: 
188:         currentHp.Value -= amount;
189:         regening = false;
190:         timer = 0;
191: 
192:         if (currentHp.Value <= 0)
193:         {
194:             currentHp.Value = 0;
195:             Break();
196:         }
197:     }
198: 
```

```csharp
215:         Break();
216:     }
217: 
218:     void Break()
219:     {
220:         // ทุกครั้งที่หลุด เพดานเลือดของชิ้นนี้ลดถาวร (ต่อกลับก็ได้แค่เพดานใหม่)
221:         currentMaxHp.Value = Mathf.Max(minMaxHp, currentMaxHp.Value - hpLossPerBreak);
222:         Debug.Log($"[RobotHealth] 💔 {gameObject.name} หลุด! เพดานเลือดเหลือ {currentMaxHp.Value}");
223: 
224:         if (Jpar != null)
225:         {
226:             Jpar.ForceBreakJoint();
227:         }
228:     }
229: }
```

## RpcAttributes.cs

`Library/PackageCache/com.unity.netcode.gameobjects@aaabf07f880c/Runtime/Messaging/RpcAttributes.cs` (181 บรรทัด)

```csharp
88:         public RpcDelivery Delivery = RpcDelivery.Reliable;
89: 
90:         /// <summary>
91:         /// Controls who has permission to invoke this RPC. The default setting is <see cref="RpcInvokePermission.Everyone"/>
92:         /// </summary>
93:         public RpcInvokePermission InvokePermission;
94: 
95:         /// <summary>
96:         /// When true, only the owner of the object can execute this RPC
```

## StatusPromptUI.cs

`Assets/Yelmee/PlayerUI/StatusPromptUI.cs` (123 บรรทัด)

```csharp
84:         if (torso != null)
85:         {
86:             TorsoMovement.TorsoState state = torso.currentState.Value;
87:             if (state == TorsoMovement.TorsoState.Ragdoll ||
88:                 state == TorsoMovement.TorsoState.Falling)
89:             {
90:                 return PromptMode.Recovery;
91:             }
```

## TorsoMovement.cs

`Assets/Scenes/TheBestFolder/Mynigga/player/TorsoMovement.cs` (518 บรรทัด)

```csharp
102:     // ให้เท้าแต่ละข้างมองเห็นกันได้ (ใช้ตั้งค่า ignore การชนระหว่างสองขา)
103:     public IReadOnlyCollection<PlayerFootForRobot> AttachedFeet => _attachedFeet;
104: 
105:     public void RegisterFoot(PlayerFootForRobot foot) { if (foot != null) _attachedFeet.Add(foot); }
106:     public void UnregisterFoot(PlayerFootForRobot foot) { if (foot != null) _attachedFeet.Remove(foot); }
107:     public void RegisterHand(PlayerHandMovement hand) { if (hand != null) _attachedHands.Add(hand); }
108:     public void UnregisterHand(PlayerHandMovement hand) { if (hand != null) _attachedHands.Remove(hand); }
109: 
110:     public bool HasSupportingHandGrab
111:     {
```

```csharp
124: 
125:         _attachedFeet.RemoveWhere(f => f == null);
126:         _attachedHands.RemoveWhere(h => h == null);
127:         currentStress = Mathf.Max(0f, currentStress - stressDecayRate * Time.fixedDeltaTime);
128: 
129:         // 🦘 [Jump System] นับถอยหลัง grace + ตรวจจับ "ลงพื้นแล้ว" เพื่อสลับเข้าช่วงพยุง
130:         if (_jumpGraceTimer > 0f)
```

```csharp
154:         // คำนวณครั้งเดียวต่อ tick — property นี้วนลิสต์มือ + RemoveWhere ทุกครั้งที่ถูกเรียก
155:         bool supportedByHand = HasSupportingHandGrab;
156: 
157:         if (!supportedByHand && currentStress >= maxTorsoStress && currentState.Value == TorsoState.Standing)
158:             currentState.Value = TorsoState.Falling;
159: 
160:         // [Gameplay Fix G10] Set hipJoint เฉพาะตอน state เปลี่ยน ไม่ใช่ทุก FixedUpdate
```

```csharp
171:             case TorsoState.Standing:
172:                 HandleFakeHoverAndPosture(supportedByHand);
173:                 break;
174:             case TorsoState.Falling:
175:                 currentState.Value = TorsoState.Ragdoll;
176:                 break;
177:             case TorsoState.Ragdoll:
178:                 // ✅ [ตามดีไซน์] ล้มแล้วต้องกด Q เท่านั้นถึงลุกได้ — ไม่มีลุกอัตโนมัติ
179:                 // (Auto Recovery เดิมอัดแรงทุก tick จนหุ่นลอยค้างฟ้าเป็นบอลลูน → ถอดทิ้งแล้ว)
```

```csharp
193:         bool kickInProgress = false;
194:         Vector3 avgFootPos = Vector3.zero;
195: 
196:         foreach (var foot in _attachedFeet)
197:         {
198:             if (foot == null) continue;
199:             if (foot.IsWalkStepping) walkSteppingCount++;
200:             // เฉพาะช่วงเตะจริง (Kicking/Recovering) ซึ่งจบเองใน ~0.5 วิ
201:             // จงใจไม่รวมช่วงง้าง (Charging) เพราะผู้เล่นกด Shift ค้างได้ไม่จำกัดเวลา
202:             // ถ้ารวมเข้าไปจะกลายเป็นเกราะกันล้มถาวรที่เปิดได้ด้วยการกดปุ่มค้าง
203:             if (foot.IsKickMotionActive) kickInProgress = true;
204:             if (foot.IsBalanced) balancedCount++;
205:             if (!foot.IsGrounded()) continue;
206:             // ✅ [Bug Fix] เท้าที่ footRb หายห้ามนับ — เดิมบวก Vector3.zero เข้า average
207:             // ทำให้จุดศูนย์ถ่วงถูกลากไปหา (0,0,0) ของโลก ตัวหุ่นไหลผิดทิศ
208:             if (foot.footRb == null) continue;
209:             groundedCount++;
210:             avgFootPos += foot.footRb.position;
211:             // ⭐ Support Foot = เท้าที่ยันพื้นอยู่และไม่ได้กำลังก้าว/กระโดด/ลุก
212:             if (foot.CanActAsSupport) hasSupportFoot = true;
213:         }
214: 
215:         // 🦘 [Jump Grace] ช่วงกระโดด/เพิ่งลงพื้น: พักการตัดสินล้มจาก "สถานะเท้า" ไว้ก่อน
216:         // (เท้าลอยเพราะกระโดด ≠ กำลังล้ม)
```

```csharp
221:         // ตราบใดที่ขาอีกข้างยันพื้นอยู่ ระบบ Plant + Tether (legStretchSpring ในไฟล์ขา)
222:         // เป็นคนรั้งลำตัวไว้ ไม่ใช่การจับล้มทิ้ง
223:         // ความยากของเกมต้องมาจาก "สองคนต้องไม่ก้าวพร้อมกัน" ไม่ใช่จากการทรงตัวรายเฟรม
224:         bool balanceProtected = supportedByHand || jumpProtected || kickInProgress || hasSupportFoot;
225: 
226:         // เช็กเอียงเกินองศาทำงานเฉพาะตอนไม่มีอะไรพยุงเลย (เช่นคะมำกลางอากาศ)
227:         float tiltAngle = Vector3.Angle(torsoRb.transform.up, Vector3.up);
```

```csharp
242: 
243:         int footCount = _attachedFeet.Count;
244: 
245:         // ⭐ [Walk Fall Rule] สาเหตุหลักที่ทำให้ล้มจากการเดิน: สองขาก้าวพร้อมกัน
246:         // ไม่ผูกกับ groundedCount/IsBalanced เลย — ผู้เล่นสองคนเผลอกดคลิกซ้ายพร้อมกัน
247:         // ค้างเกิน grace = ไม่มีขายัน = ล้ม (นี่คือความยากที่ตั้งใจให้เป็นหัวใจของเกม)
248:         // IsWalkStepping ตัดกระโดด/เตะออกไปแล้วในฝั่ง PlayerFootForRobot
249:         if (!supportedByHand && !jumpProtected && footCount >= 2 && walkSteppingCount >= 2)
250:         {
251:             _bothSteppingTimer += Time.fixedDeltaTime;
252:             if (_bothSteppingTimer >= bothFeetSteppingGrace)
253:             {
254:                 _bothSteppingTimer = 0f;
255:                 currentState.Value = TorsoState.Falling;
256:                 return;
257:             }
258:         }
259:         else
260:         {
261:             _bothSteppingTimer = Mathf.Max(0f, _bothSteppingTimer - Time.fixedDeltaTime * 2f);
```

```csharp
301:         // ⚖️ ดึงลำตัวให้อยู่เหนือฐานเท้า — ต้องทำงานทุกครั้งที่มีเท้าแตะพื้น
302:         // (แยกออกมาจาก else ด้านบนแล้ว: สาขานั้นเข้าได้ก็ต่อเมื่อ groundedCount == 0 อยู่ดี
303:         //  ถ้าคาไว้ในนั้น พอเพิ่ม balanceProtected แรงจัดศูนย์จะหายไปเงียบๆ ตอนมีขายัน)
304:         if (groundedCount > 0)
305:         {
306:             avgFootPos /= groundedCount;
307:             Vector3 flatError = new Vector3(avgFootPos.x - torsoRb.position.x, 0f, avgFootPos.z - torsoRb.position.z);
308:             torsoRb.AddForce(flatError * (autoCenterGravityForce * Mathf.Lerp(1f, minCenterForceMultiplier, armPullIntensity)), ForceMode.Acceleration);
309:         }
310: 
311:         // ✅ [Fly-away Fix] ชดเชยแรงโน้มถ่วง "เฉพาะตอนมีพื้นในระยะ" เท่านั้น
312:         // เดิมใส่ -gravity ตลอดเวลา → หุ่นที่ถูกดีดพ้นระยะ raycast จะลอยค้างฟ้าไม่ตกลงมา
```

```csharp
392:     /// หรือ Co-op Jump (สองขากดภายใน coopJumpWindow = ได้แรงโบนัสเพิ่ม)
393:     /// แรงทั้งหมดถูกคุมด้วยเพดาน maxJumpUpVelocity — ไม่มีทางซ้อนสะสมจนพุ่งฟ้า
394:     /// </summary>
395:     public void NotifyFootJump(PlayerFootForRobot foot)
396:     {
397:         if (torsoRb == null || currentState.Value != TorsoState.Standing) return;
398: 
399:         // เข้าช่วง grace: การตัดสินล้มจากสถานะเท้าถูกพักไว้จนกว่าจะลงพื้น/หมดเวลา
400:         _jumpGraceTimer = jumpGraceDuration;
401:         _landingAssistTimer = 0f;
402: 
403:         bool isCoopJump = _pendingJumpFoot != null && _pendingJumpFoot != foot &&
404:                           (Time.fixedTime - _pendingJumpTime) <= coopJumpWindow;
405: 
406:         if (isCoopJump)
407:         {
408:             _pendingJumpFoot = null; // ใช้คู่นี้ไปแล้ว กันขาที่สามมาต่อคอมโบ (เผื่ออนาคต)
409:             ApplyJumpBoost(coopJumpBonusForce);
410:             Debug.Log("[Server] 🚀 Co-op Jump! สองขากดพร้อมกัน — ได้แรงโบนัส");
411:         }
412:         else
413:         {
414:             _pendingJumpFoot = foot;
415:             _pendingJumpTime = Time.fixedTime;
416:             ApplyJumpBoost(soloJumpForce);
417:         }
418:     }
419: 
420:     private void ApplyJumpBoost(float force)
421:     {
```

```csharp
462:         currentState.Value = TorsoState.Standing;
463:     }
464: 
465:     [Rpc(SendTo.Server)]
466:     public void ApplyRecoveryForceRpc(Vector3 forcePosition)
467:     {
468:         // 🛡️ [Server Validation] ห้ามเชื่อพิกัดจาก client — จุดออกแรงยิ่งไกลตัว
```

```csharp
476: 
477:         ApplyContinuousRecoveryForce(forcePosition);
478:     }
479:     public void AddStress(float amount) => currentStress = Mathf.Min(currentStress + amount, maxTorsoStress * 1.5f);
480:     public int RegisteredFootCount => _attachedFeet.Count;
481: 
482:     // [Audit Fix] ปลดล็อก hipJoints เป็น Free เสมอ ไม่ใช้ Locked เพื่อไม่ให้แช่แข็งผิดท่า
```
