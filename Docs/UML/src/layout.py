CANVAS = (3560, 2600)
TITLE_AT = (60, 72)
TITLE = "Nsc-Unity — Class Diagram"
SUBTITLE = "โครงสร้างคลาสของเกม (ปรับจากโค้ดในเกม) · 61 คลาส ใน 7 แพ็กเกจ"
LEGEND_AT = None
SIMPLE_LINES = False    # standard UML arrows (association, inheritance, realization, dependency, composition, aggregation)
SHOW_LINE_TEXT = False  # no labels or multiplicities on lines
LEGEND_SIZE = (1100, 290)

PKG_PAD = {
    "robot": (28, 46, 28, 20),
}

POS = {
    # Limb control
    "LimbController": (500, 530),
    "ArmGrip": (60, 780), "ArmController": (290, 780), "LegController": (700, 780), "LegMode": (1010, 780),
    "ArmInput": (60, 1090), "PunchSkill": (290, 1090), "KickSkill": (620, 1090), "LegInput": (990, 1090),
    # Respawn
    "CheckpointZone": (1270, 222), "RespawnManager": (1680, 222), "FallDeathZone": (2050, 222),
    # Robot
    "RobotLimb": (1270, 522), "Robot": (1660, 522), "LimbSlot": (2190, 522),
    "LimbAttachment": (1270, 800), "TorsoBalance": (1660, 800), "FallRules": (2030, 800),
    "LimbHealth": (1270, 1080), "RobotBodyDamageRelay": (1660, 1080), "BalanceInput": (2030, 1080), "FallReason": (2200, 1080),
    # Combat
    "IStrikeSource": (1270, 1476), "IDamageable": (1560, 1476), "DestructibleBuilding": (2100, 1476),
    "LimbStrike": (1270, 1656), "DamageRouter": (1610, 1656),
    "DamageInfo": (1800, 1860), "DamageSource": (2010, 1860), "Team": (2210, 1860),
    # Match
    "LimbSelection": (2840, 222), "LimbAssignment": (3230, 222),
    "LimbControlBinder": (2500, 522), "MatchSession": (2860, 522), "MatchPhase": (3230, 522), "MatchResult": (3230, 660),
    "ModeRules": (2500, 800), "GameplayGate": (2930, 800), "MatchResultPanel": (3230, 830),
    "BossModeRules": (2500, 1030), "ParkourModeRules": (2740, 1030), "PvpModeRules": (3000, 1030), "SessionService": (3250, 1000),
    # Boss
    "EnemyHealth": (2530, 1476), "EnemyUltimate": (2890, 1476),
    "BlackHoleProjectile": (2530, 1710),
    "EnemyCombat": (2530, 1890), "EnemyController": (2890, 1890), "EnemyState": (3190, 1890),
    "AttackType": (2530, 2070),
    # Items
    "ItemPickupInteractor": (60, 1476), "PlayerInventory": (430, 1476), "ItemDatabase": (780, 1476),
    "WorldItem": (60, 1740), "HandItemHolder": (440, 1740), "ItemDefinition": (820, 1740),
    "HeldItem": (410, 1940),
    "ChargeGunHeldItem": (60, 2190), "GunHeldItem": (470, 2190), "SwordHeldItem": (760, 2190),
    "Projectile": (110, 2400),
}


def E(k, a, b, sa, sb, **kw):
    d = dict(k=k, a=a, b=b, sa=sa, sb=sb)
    d.update(kw)
    return d


EDGES = [
    # ---- limb control
    E("inh", "ArmController", "LimbController", ("t", 455), ("b", 645), via=[("y", 740), ("x", 645)], group="lc"),
    E("inh", "LegController", "LimbController", ("t", 830), ("b", 645), via=[("y", 740), ("x", 645)], group="lc", shared_tip=True),
    E("assoc", "LegController", "LegMode", ("r", 850), ("l", 850)),
    E("assoc", "ArmController", "ArmGrip", ("l", 850), ("r", 850)),
    E("assoc", "ArmInput", "ArmGrip", ("t", 110), ("b", 110)),
    E("assoc", "ArmInput", "ArmController", ("t", 190), ("b", 320), via=[("y", 1030), ("x", 320)]),
    E("comp", "ArmController", "PunchSkill", ("b", 440), ("t", 440)),
    E("comp", "LegController", "KickSkill", ("b", 800), ("t", 800)),
    E("assoc", "LegInput", "LegController", ("t", 1040), ("b", 920), via=[("y", 1055), ("x", 920)]),
    E("real", "PunchSkill", "IStrikeSource", ("b", 400), ("t", 1355), via=[("y", 1388), ("x", 1355)], group="iss"),
    E("real", "KickSkill", "IStrikeSource", ("b", 745), ("t", 1355), via=[("y", 1388), ("x", 1355)], group="iss", shared_tip=True),
    E("real", "SwordHeldItem", "IStrikeSource", ("r", 2230), ("t", 1355), via=[("x", 1110), ("y", 1388), ("x", 1355)], group="iss", shared_tip=True),
    E("comp", "RobotLimb", "LimbController", ("l", 615), ("r", 615), mb=("1", 14, -8)),
    # ---- respawn
    E("dep", "CheckpointZone", "RespawnManager", ("r", 270), ("l", 270)),
    E("dep", "FallDeathZone", "RespawnManager", ("l", 254), ("r", 254)),
    E("assoc", "RespawnManager", "Robot", ("b", 1800), ("t", 1800), label="เกิดใหม่ที่เช็คพอยต์", lpos=(1800, 455)),
    # ---- robot
    E("comp", "Robot", "RobotLimb", ("l", 600), ("r", 600), mb=("4", 14, -8)),
    E("comp", "Robot", "TorsoBalance", ("b", 1805), ("t", 1805), mb=("1", 12, -8)),
    E("comp", "RobotLimb", "LimbAttachment", ("b", 1400), ("t", 1400), mb=("1", 12, -8)),
    E("comp", "RobotLimb", "LimbHealth", ("r", 680), ("r", 1160), via=[("x", 1600), ("y", 1160)], mb=("1", 14, -8)),
    E("comp", "TorsoBalance", "FallRules", ("r", 880), ("l", 880), mb=("1", -12, -8)),
    E("dep", "Robot", "LimbSlot", ("r", 580), ("l", 580)),
    E("dep", "FallRules", "BalanceInput", ("b", 2100), ("t", 2100)),
    E("dep", "FallRules", "FallReason", ("b", 2275), ("t", 2275), label="คืนค่า", lpos=(2275, 1030)),
    E("dep", "TorsoBalance", "BalanceInput", ("r", 990), ("l", 1150), via=[("x", 1995), ("y", 1150)], label="สร้าง", lpos=(1995, 1060)),
    E("assoc", "RobotBodyDamageRelay", "Robot", ("l", 1110), ("b", 1700), via=[("x", 1630), ("y", 760), ("x", 1700)]),
    E("dep", "RobotBodyDamageRelay", "LimbHealth", ("b", 1680), ("b", 1520), via=[("y", 1262), ("x", 1520)], label="หาชิ้นที่ใกล้สุด", lpos=(1600, 1266)),
    E("real", "LimbHealth", "IDamageable", ("b", 1400), ("t", 1760), via=[("y", 1370), ("x", 1760)], group="idm"),
    E("real", "RobotBodyDamageRelay", "IDamageable", ("b", 1860), ("t", 1760), via=[("y", 1370), ("x", 1760)], group="idm", shared_tip=True),
    E("real", "DestructibleBuilding", "IDamageable", ("t", 2240), ("t", 1760), via=[("y", 1370), ("x", 1760)], group="idm", shared_tip=True),
    E("real", "EnemyHealth", "IDamageable", ("l", 1510), ("t", 1760), via=[("x", 2485), ("y", 1370), ("x", 1760)], group="idm", shared_tip=True),
    # ---- combat
    E("assoc", "LimbStrike", "IStrikeSource", ("t", 1355), ("b", 1355)),
    E("dep", "LimbStrike", "DamageRouter", ("r", 1700), ("l", 1700)),
    E("dep", "DamageRouter", "IDamageable", ("t", 1700), ("b", 1700)),
    E("dep", "DamageRouter", "DamageInfo", ("b", 1880), ("t", 1880)),
    E("assoc", "DamageInfo", "DamageSource", ("r", 1910), ("l", 1910)),
    E("assoc", "DamageInfo", "Team", ("r", 2020), ("b", 2285), via=[("x", 2285)]),
    E("dep", "DamageRouter", "GameplayGate", ("t", 1930), ("b", 2975), via=[("y", 1352), ("x", 2975)],
      label="เช็คเฟสแมตช์", lpos=(2200, 1356)),
    E("dep", "Projectile", "DamageRouter", ("r", 2455), ("b", 1700), via=[("x", 1170), ("y", 1815), ("x", 1700)]),
    E("dep", "GunHeldItem", "DamageRouter", ("b", 585), ("b", 1650), via=[("y", 2430), ("x", 1140), ("y", 1795), ("x", 1650)]),
    E("dep", "BlackHoleProjectile", "DamageRouter", ("l", 1760), ("r", 1675), via=[("x", 2460), ("y", 1675)]),
    E("dep", "EnemyCombat", "DamageRouter", ("l", 1940), ("r", 1725), via=[("x", 2440), ("y", 1725)]),
    # ---- boss
    E("assoc", "EnemyHealth", "EnemyUltimate", ("r", 1530), ("l", 1530)),
    E("dep", "EnemyUltimate", "BlackHoleProjectile", ("b", 2970), ("t", 2680), via=[("y", 1660), ("x", 2680)], label="ยิง", lpos=(2830, 1664)),
    E("assoc", "EnemyController", "EnemyCombat", ("l", 1940), ("r", 1940)),
    E("assoc", "EnemyController", "EnemyState", ("r", 1940), ("l", 1940)),
    E("dep", "EnemyCombat", "AttackType", ("b", 2605), ("t", 2605)),
    E("dep", "EnemyController", "AttackType", ("b", 3005), ("r", 2127), via=[("y", 2127)]),
    E("assoc", "BossModeRules", "EnemyHealth", ("b", 2690), ("t", 2690), label="บอส", lpos=(2690, 1250)),
    # ---- match
    E("assoc", "LimbControlBinder", "Robot", ("l", 660), ("r", 660), label="หุ่นของผู้เล่นเครื่องนี้", lpos=(2215, 664)),
    E("assoc", "ModeRules", "Robot", ("l", 870), ("r", 706), via=[("x", 2420), ("y", 706)], mb=("1..*", 22, -8)),
    E("assoc", "LimbControlBinder", "LimbSelection", ("t", 2760), ("l", 300), via=[("y", 300)]),
    E("assoc", "LimbControlBinder", "MatchSession", ("r", 600), ("l", 600)),
    E("assoc", "LimbSelection", "MatchSession", ("b", 3000), ("t", 3000), label="เริ่มแมตช์", lpos=(3000, 454)),
    E("comp", "LimbSelection", "LimbAssignment", ("r", 270), ("l", 270), mb=("*", -10, -8)),
    E("assoc", "MatchSession", "MatchPhase", ("r", 560), ("l", 560)),
    E("assoc", "MatchSession", "MatchResult", ("r", 680), ("l", 680)),
    E("dep", "MatchSession", "GameplayGate", ("b", 2960), ("t", 2960), label="ผู้เขียนคนเดียว", lpos=(2960, 752)),
    E("assoc", "ModeRules", "MatchSession", ("t", 2690), ("b", 2880), via=[("y", 740), ("x", 2880)]),
    E("inh", "BossModeRules", "ModeRules", ("t", 2605), ("b", 2605), group="mr"),
    E("inh", "ParkourModeRules", "ModeRules", ("t", 2845), ("b", 2605), via=[("y", 985), ("x", 2605)], group="mr", shared_tip=True),
    E("inh", "PvpModeRules", "ModeRules", ("t", 3105), ("b", 2605), via=[("y", 985), ("x", 2605)], group="mr", shared_tip=True),
    E("assoc", "MatchResultPanel", "MatchSession", ("l", 870), ("b", 3110), via=[("x", 3110)]),
    E("dep", "MatchResultPanel", "SessionService", ("b", 3340), ("t", 3340), label="ออกจากห้อง", lpos=(3340, 974)),
    # ---- items
    E("assoc", "ItemPickupInteractor", "PlayerInventory", ("r", 1530), ("l", 1530)),
    E("assoc", "ItemPickupInteractor", "WorldItem", ("b", 150), ("t", 150), label="focus", lpos=(150, 1706)),
    E("assoc", "PlayerInventory", "ItemDatabase", ("r", 1530), ("l", 1530)),
    E("assoc", "PlayerInventory", "HandItemHolder", ("b", 555), ("t", 555)),
    E("aggr", "ItemDatabase", "ItemDefinition", ("b", 900), ("t", 900), mb=("*", 12, -8)),
    E("assoc", "WorldItem", "ItemDefinition", ("t", 210), ("t", 860), via=[("y", 1700), ("x", 860)]),
    E("comp", "HandItemHolder", "HeldItem", ("b", 555), ("t", 555), mb=("0..1", 22, -8)),
    E("assoc", "HeldItem", "ItemDefinition", ("r", 1980), ("b", 905), via=[("x", 905)]),
    E("inh", "ChargeGunHeldItem", "HeldItem", ("t", 235), ("b", 555), via=[("y", 2150), ("x", 555)], group="hi"),
    E("inh", "GunHeldItem", "HeldItem", ("t", 585), ("b", 555), via=[("y", 2150), ("x", 555)], group="hi", shared_tip=True),
    E("inh", "SwordHeldItem", "HeldItem", ("t", 855), ("b", 555), via=[("y", 2150), ("x", 555)], group="hi", shared_tip=True),
    E("dep", "ChargeGunHeldItem", "Projectile", ("b", 210), ("t", 210), label="ยิง", lpos=(210, 2362)),
]

NOTES = []
