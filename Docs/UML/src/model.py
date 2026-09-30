# Nsc-Unity class diagram model.
# Member prefix "_" = static (underlined), suffix "{abstract}" = abstract (italic).

NB = "NetworkBehaviour"

CLASSES = {}
ORDER = []


def C(cid, stereo=None, attrs=(), ops=(), abstract=False, enum=None, pkg=None):
    CLASSES[cid] = dict(id=cid, stereo=stereo, attrs=list(attrs), ops=list(ops),
                        abstract=abstract, enum=enum, pkg=pkg)
    ORDER.append(cid)


# ---------------- Robot ----------------
P = "robot"
C("Robot", NB, [
    "- team : NetworkVariable<Team>",
    "- torso : TorsoBalance",
    "- limbs : RobotLimb[4]",
    "+ LimbStateChanged : Action<RobotLimb> {event}",
], [
    "+ GetLimb(slot : LimbSlot) : RobotLimb",
    "+ AttachedLimbs() : IEnumerable<RobotLimb>",
    "+ ServerResetForRespawn(spawn : Pose) : void",
    "_+ FromCollider(hit : Collider) : Robot",
    "- OnLimbStateChanged(limb : RobotLimb) : void",
], pkg=P)
C("RobotLimb", None, [
    "- slot : LimbSlot",
    "- owner : Robot",
    "- attachment : LimbAttachment",
    "- health : LimbHealth",
    "- controller : LimbController",
    "+ IsAttached : bool",
    "+ StateChanged : Action<RobotLimb> {event}",
], [
    "- OnHealthDepleted() : void",
    "- OnAttachmentChanged(attached : bool) : void",
], pkg=P)
C("LimbSlot", "Enumeration", enum=["LeftArm", "RightArm", "LeftLeg", "RightLeg"], pkg=P)
C("LimbAttachment", NB, [
    "- isAttached : NetworkVariable<bool>",
    "- joint : ConfigurableJoint",
    "- socket : Rigidbody",
    "+ AttachmentChanged : Action<bool> {event}",
], [
    "+ ServerDetach() : void",
    "+ RequestPullBackRpc(held : bool) : void",
    "- ServerReconnect() : void",
], pkg=P)
C("LimbHealth", NB, [
    "- hp : NetworkVariable<float>",
    "- maxHp : NetworkVariable<float>",
    "+ Depleted : Action {event}",
], [
    "+ GetTeam() : Team",
    "+ ServerApplyDamage(info : DamageInfo) : bool",
    "+ ServerRefill() : void",
], pkg=P)
C("TorsoBalance", NB, [
    "- isRagdoll : NetworkVariable<bool>",
    "- stress : float",
    "- robot : Robot",
    "- rules : FallRules",
], [
    "+ ServerAddStress(amount : float) : void",
    "+ ServerApplyJump(from : LimbSlot) : void",
    "+ ServerKnockDown(reason : FallReason) : void",
    "+ ServerResetForRespawn() : void",
    "- ReadBalanceInput() : BalanceInput",
], pkg=P)
C("FallRules", "Plain C#", [
    "- maxStress : float",
    "- bothSteppingGrace : float",
    "- airborneGrace : float",
    "- steppingTimer : float",
    "- airborneTimer : float",
], [
    "+ Evaluate(input : BalanceInput, dt : float) : FallReason",
    "+ Reset() : void",
], pkg=P)
C("BalanceInput", "Struct", [
    "+ groundedFeet : int",
    "+ walkSteppingFeet : int",
    "+ handSupport : bool",
    "+ jumpProtected : bool",
    "+ stress : float",
    "+ tiltAngle : float",
], pkg=P)
C("FallReason", "Enumeration", enum=[
    "None", "StressOverload", "BothFeetStepping", "BothFeetOffBalance",
    "AirborneTooLong", "TiltedInAir", "LegDetached", "KnockedDown"], pkg=P)

# ---------------- Limb control ----------------
P = "control"
C("LimbController", NB, [
    "# limb : RobotLimb",
    "# body : Rigidbody",
    "# pivot : Transform",
], [
    "+ ServerResetForRespawn() : void {abstract}",
    "# DriveToward(target : Vector3, speed : float) : void",
    "# ResetAimRpc() : void",
], abstract=True, pkg=P)
C("ArmController", NB, [
    "- target : Vector3",
    "- grip : ArmGrip",
    "- punch : PunchSkill",
    "+ ClimbPull : float",
], [
    "+ SetHandTargetRpc(target : Vector3) : void",
    "+ RequestPunchRpc(held : bool) : void",
    "+ ServerResetForRespawn() : void {override}",
    "- DriveHand() : void",
    "- PullTorsoTowardGrip() : void",
], pkg=P)
C("LegController", NB, [
    "- mode : LegMode",
    "- target : Vector3",
    "- kick : KickSkill",
    "+ IsGrounded : bool",
    "+ IsWalkStepping : bool",
], [
    "+ SetFootTargetRpc(target : Vector3) : void",
    "+ SetSteppingRpc(on : bool) : void",
    "+ JumpRpc() : void",
    "+ StartKickRpc() : void",
    "+ ReleaseKickRpc(aim : Vector3) : void",
    "+ ServerResetForRespawn() : void {override}",
    "- ResolveMode() : LegMode",
], pkg=P)
C("LegMode", "Enumeration", enum=["Planted", "Stepping", "Kicking", "Limp", "PushingUp", "Detached"], pkg=P)
C("ArmGrip", NB, [
    "- joint : FixedJoint",
    "- held : Rigidbody",
    "+ IsSupporting : bool",
], [
    "+ RequestGrabRpc() : void",
    "+ RequestReleaseRpc() : void",
    "+ ServerRelease() : void",
], pkg=P)
C("ArmInput", NB, [
    "- arm : ArmController",
    "- grip : ArmGrip",
], [
    "- ReadOwnerInput() : void",
], pkg=P)
C("LegInput", NB, [
    "- leg : LegController",
], [
    "- ReadOwnerInput() : void",
], pkg=P)
# Skills are modules the controller owns and calls; they never reference the controller.
C("PunchSkill", NB, [
    "- isPunching : NetworkVariable<bool>",
    "- acceleration : float",
    "- peakSpeed : float",
    "+ IsActive : bool",
], [
    "+ ServerStart(aim : Vector3) : void",
    "+ ServerRelease() : void",
    "+ DriveTarget(current : Vector3) : Vector3",
    "+ CanDealDamage() : bool",
    "+ PeakSpeed() : float",
    "+ Source() : DamageSource",
], pkg=P)
C("KickSkill", NB, [
    "- isKicking : NetworkVariable<bool>",
    "- chargeTime : float",
    "- peakSpeed : float",
    "+ IsActive : bool",
], [
    "+ ServerStartCharge() : void",
    "+ ServerRelease(aim : Vector3) : void",
    "+ DriveTarget(current : Vector3) : Vector3",
    "+ CanDealDamage() : bool",
    "+ PeakSpeed() : float",
    "+ Source() : DamageSource",
], pkg=P)

# ---------------- Combat / damage ----------------
P = "combat"
C("IStrikeSource", "Interface", [], [
    "+ CanDealDamage() : bool",
    "+ PeakSpeed() : float",
    "+ Source() : DamageSource",
], pkg=P)
C("LimbStrike", None, [
    "- source : IStrikeSource",
    "- speedToDamage : float",
], [
    "- OnCollisionEnter(c : Collision) : void",
    "- BuildDamageInfo(c : Collision) : DamageInfo",
], pkg=P)
C("DamageRouter", "Static", [], [
    "_+ TryApply(hit : Collider, info : DamageInfo) : bool",
    "_- IsAllowed(target : IDamageable, info : DamageInfo) : bool",
], pkg=P)
C("DamageInfo", "Struct", [
    "+ amount : float",
    "+ knockback : float",
    "+ point : Vector3",
    "+ direction : Vector3",
    "+ source : DamageSource",
    "+ attackerTeam : Team",
    "+ detachLimb : bool",
], pkg=P)
C("DamageSource", "Enumeration", enum=["Punch", "Kick", "MeleeWeapon", "Projectile", "EnemyMelee", "EnemyUltimate"], pkg=P)
C("Team", "Enumeration", enum=["None", "Red", "Blue", "Enemy"], pkg=P)
C("IDamageable", "Interface", [], [
    "+ GetTeam() : Team",
    "+ ServerApplyDamage(info : DamageInfo) : bool",
], pkg=P)
C("RobotBodyDamageRelay", None, [
    "- robot : Robot",
], [
    "+ GetTeam() : Team",
    "+ ServerApplyDamage(info : DamageInfo) : bool",
    "- NearestAttachedLimb(point : Vector3) : LimbHealth",
], pkg="robot")
C("DestructibleBuilding", NB, [
    "- health : NetworkVariable<float>",
], [
    "+ GetTeam() : Team",
    "+ ServerApplyDamage(info : DamageInfo) : bool",
    "- Collapse() : void",
], pkg=P)

# ---------------- Boss ----------------
P = "boss"
C("EnemyController", NB, [
    "- state : NetworkVariable<EnemyState>",
    "- agent : NavMeshAgent",
    "- combat : EnemyCombat",
    "- target : Transform",
], [
    "- UpdateState() : void",
    "- ChooseAttack() : AttackType",
], pkg=P)
C("EnemyState", "Enumeration", enum=["Idle", "Walk", "Roll", "Attack", "Ultimate", "Dead"], pkg=P)
C("EnemyCombat", NB, [
    "- attackRange : float",
], [
    "+ ServerExecuteAttack(type : AttackType) : float",
    "- HitTargetsInRange(type : AttackType) : void",
], pkg=P)
C("AttackType", "Enumeration", enum=["LightPunch", "BarragePunch", "Kick", "BlackHole"], pkg=P)
C("EnemyHealth", NB, [
    "- hp : NetworkVariable<float>",
    "- maxHp : float",
    "- ultimate : EnemyUltimate",
    "+ Died : Action {event}",
], [
    "+ GetTeam() : Team",
    "+ ServerApplyDamage(info : DamageInfo) : bool",
    "+ HpPercent() : float",
], pkg=P)
C("EnemyUltimate", NB, [
    "- triggerHpPercent : float",
    "- usesLeft : int",
], [
    "+ ServerTryTrigger() : void",
    "- FireBlackHole() : void",
], pkg=P)
C("BlackHoleProjectile", None, [
    "- speed : float",
    "- damageRadius : float",
], [
    "- DamageAlong(from : Vector3, to : Vector3) : void",
], pkg=P)

# ---------------- Match & session ----------------
P = "match"
C("MatchSession", NB, [
    "- phase : NetworkVariable<MatchPhase>",
    "- result : NetworkVariable<MatchResult>",
    "- startTime : float",
    "+ PhaseChanged : Action<MatchPhase> {event}",
], [
    "+ ServerBeginPlaying() : void",
    "+ ServerEnd(result : MatchResult) : void",
    "+ ElapsedTime() : float",
], pkg=P)
C("MatchPhase", "Enumeration", enum=["Preparing", "Playing", "Ended"], pkg=P)
C("MatchResult", "Struct", [
    "+ victory : bool",
    "+ winningTeam : Team",
    "+ time : float",
    "+ height : float",
], pkg=P)
C("GameplayGate", "Static", [
    "_+ CanAct : bool",
    "_+ CanDamage : bool",
], [], pkg=P)
C("ModeRules", NB, [
    "# session : MatchSession",
    "# robots : List<Robot>",
], [
    "# EvaluateDefeat() : void {abstract}",
    "# EndMatch(victory : bool) : void",
], abstract=True, pkg=P)
C("BossModeRules", NB, [
    "- boss : EnemyHealth",
], [
    "# EvaluateDefeat() : void {override}",
    "- OnBossDied() : void",
], pkg=P)
C("ParkourModeRules", NB, [
    "- peakHeight : float",
], [
    "# EvaluateDefeat() : void {override}",
    "+ ServerReachGoal() : void",
], pkg=P)
C("PvpModeRules", NB, [], [
    "# EvaluateDefeat() : void {override}",
    "- WinningTeam() : Team",
], pkg=P)
C("LimbSelection", NB, [
    "- assignments : NetworkList<LimbAssignment>",
    "- session : MatchSession",
    "+ AssignmentsChanged : Action {event}",
], [
    "+ RequestLimbRpc(robotId : ulong, slot : LimbSlot) : void",
    "+ RequestStartRpc() : void",
    "+ GetController(robotId : ulong, slot : LimbSlot) : ulong",
], pkg=P)
C("LimbAssignment", "Struct", [
    "+ robotId : ulong",
    "+ slot : LimbSlot",
    "+ clientId : ulong",
], pkg=P)
C("LimbControlBinder", None, [
    "- selection : LimbSelection",
    "- session : MatchSession",
    "+ LocalRobot : Robot",
    "+ LocalSlot : LimbSlot",
    "+ Bound : Action<Robot, LimbSlot> {event}",
], [
    "- OnPhaseChanged(phase : MatchPhase) : void",
    "- Bind(robot : Robot, slot : LimbSlot) : void",
], pkg=P)
C("MatchResultPanel", None, [
    "- session : MatchSession",
], [
    "- Show(result : MatchResult) : void",
    "+ Retry() : void",
    "+ ExitToMenu() : void",
], pkg=P)
C("SessionService", None, [
    "- joinCode : string",
    "+ StateChanged : Action {event}",
], [
    "+ HostAsync(maxPlayers : int) : Task",
    "+ JoinAsync(code : string) : Task",
    "+ LeaveAsync() : Task",
], pkg=P)

# ---------------- Respawn ----------------
P = "respawn"
C("RespawnManager", NB, [
    "_+ Instance : RespawnManager",
    "- checkpoint : NetworkVariable<Vector3>",
    "- robot : Robot",
    "- cooldown : float",
], [
    "+ SetCheckpoint(point : Vector3) : void",
    "+ RespawnBody() : void",
], pkg=P)
C("CheckpointZone", None, [
    "- respawnPoint : Transform",
    "- triggered : bool",
], [
    "- OnTriggerEnter(other : Collider) : void",
], pkg=P)
C("FallDeathZone", None, [], [
    "- OnTriggerEnter(other : Collider) : void",
], pkg=P)

# ---------------- Items ----------------
P = "items"
C("ItemPickupInteractor", NB, [
    "- inventory : PlayerInventory",
    "+ Focused : WorldItem",
], [
    "- RefreshFocus() : void",
    "- PickupRpc(item : NetworkObjectReference) : void",
], pkg=P)
C("PlayerInventory", NB, [
    "- slots : NetworkList<int>",
    "- equippedIndex : NetworkVariable<int>",
    "- database : ItemDatabase",
    "- hand : HandItemHolder",
    "+ SlotsChanged : Action {event}",
], [
    "+ RequestEquip(index : int) : void",
    "+ RequestDrop() : void",
    "+ ServerTryAdd(item : ItemDefinition) : bool",
], pkg=P)
C("ItemDatabase", "ScriptableObject", [
    "- items : List<ItemDefinition>",
], [
    "+ GetIndex(item : ItemDefinition) : int",
    "+ GetByIndex(index : int) : ItemDefinition",
], pkg=P)
C("ItemDefinition", "ScriptableObject", [
    "+ displayName : string",
    "+ icon : Sprite",
    "+ heldPrefab : GameObject",
    "+ worldPrefab : GameObject",
], pkg=P)
C("WorldItem", None, [
    "_+ Active : List<WorldItem>",
    "- definition : ItemDefinition",
], [
    "+ ConsumeFromWorld() : void",
], pkg=P)
C("HandItemHolder", NB, [
    "- holdPoint : Transform",
    "+ Current : HeldItem",
], [
    "+ Hold(definition : ItemDefinition) : void",
    "- ReadOwnerInput() : void",
], pkg=P)
C("HeldItem", None, [
    "+ Definition : ItemDefinition",
    "+ Holder : HandItemHolder",
], [
    "+ OnEquipped() : void",
    "+ OnUnequipped() : void",
    "+ ServerOnUseStart() : void",
    "+ ServerOnUseRelease(heldSeconds : float) : void",
], abstract=True, pkg=P)
C("ChargeGunHeldItem", None, [
    "- maxChargeTime : float",
    "- minDamage : float",
    "- maxDamage : float",
], [
    "+ ServerOnUseRelease(heldSeconds : float) : void {override}",
    "- SpawnProjectile(charge : float) : void",
], pkg=P)
C("SwordHeldItem", None, [
    "- joint : FixedJoint",
    "- maxDamagePerHit : float",
], [
    "+ OnEquipped() : void {override}",
    "+ CanDealDamage() : bool",
    "+ PeakSpeed() : float",
    "+ Source() : DamageSource",
], pkg=P)
C("GunHeldItem", None, [
    "- damage : float",
    "- fireRate : float",
], [
    "+ ServerOnUseStart() : void {override}",
], pkg=P)
C("Projectile", None, [
    "- speed : float",
    "- damage : float",
    "- shooterTeam : Team",
], [
    "- HandleHit(hit : RaycastHit) : void",
], pkg=P)

PACKAGES = {
    "control": ("Limb Control", "ควบคุมแขนขา (input ฝั่งเจ้าของ → RPC → ฟิสิกส์บน server)"),
    "robot": ("Robot", "หุ่นยนต์ แขนขา และการทรงตัว"),
    "respawn": ("Respawn", "เช็คพอยต์และการเกิดใหม่"),
    "match": ("Match & Session", "แมตช์ โหมดเกม ลอบบี้ และการเลือกแขนขา"),
    "items": ("Items", "ไอเทมและอาวุธ"),
    "combat": ("Combat", "เส้นทางดาเมจ"),
    "boss": ("Boss", "AI และท่าไม้ตายของบอส"),
}
