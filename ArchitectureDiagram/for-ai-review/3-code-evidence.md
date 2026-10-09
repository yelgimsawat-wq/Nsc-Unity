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
