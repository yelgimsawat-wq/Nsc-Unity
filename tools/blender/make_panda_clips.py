"""
สร้างท่าให้แพนด้าไคจู (rig Mixamo "HumanBody:") จาก Mutant Jump Attack
  1) Kaiju Ground Pound  = กระโดดทุบ แต่ไม่ลอย ไม่พุ่ง: ยืนยกแขน (ช่วงลอยเดิม) แล้วทุบพื้นที่เดิม
  2) Kaiju Roll Attack   = ย่อ -> ขดเป็นลูกบอล -> กลิ้งไปหน้า 3 รอบ -> คลาย ลุกยืน
ส่งออก FBX (armature + animation) ไปที่ Assets/nok/Enemy/Animations/Custom/
ใช้: blender -b --python make_panda_clips.py -- <out_dir> <preview_dir>
"""
import bpy, math, sys
from mathutils import Matrix, Vector, Quaternion

SRC = r"D:\GitHub\Nsc-Unity\Assets\nok\Enemy\Animations\Mixamo\Mutant Jump Attack.fbx"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if len(argv) > 0 else r"D:\GitHub\Nsc-Unity\Assets\nok\Enemy\Animations\Custom"
PREVIEW = argv[1] if len(argv) > 1 else ""


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC, automatic_bone_orientation=False)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    return arm


def B(arm, name):
    return arm.pose.bones["HumanBody:" + name]


def sample(arm, frame):
    """ท่าที่เฟรมนี้: rotation local ทุกกระดูก + ตำแหน่ง/เมทริกซ์สะโพกในโลก"""
    bpy.context.scene.frame_set(frame)
    rots = {pb.name: pb.rotation_quaternion.copy() for pb in arm.pose.bones}
    hips = B(arm, "Hips")
    return rots, arm.matrix_world @ hips.matrix


def apply(arm, rots, hips_world):
    for pb in arm.pose.bones:
        pb.rotation_quaternion = rots[pb.name]
    hips = B(arm, "Hips")
    hips.matrix = arm.matrix_world.inverted() @ hips_world
    bpy.context.view_layer.update()


def key(arm, frame):
    for pb in arm.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=frame)
    B(arm, "Hips").keyframe_insert("location", frame=frame)


def new_action(arm, name):
    act = bpy.data.actions.new(name)
    arm.animation_data.action = act
    if hasattr(arm.animation_data, "action_slot") and act.slots:
        arm.animation_data.action_slot = act.slots[0]
    return act


def rotate_world(arm, pb, axis_world, deg):
    """หมุนกระดูกรอบหัวกระดูก ด้วยแกนในโลก (ลูกตามไปด้วย)"""
    mw = arm.matrix_world
    head_w = mw @ pb.head
    m_w = mw @ pb.matrix
    r = Matrix.Translation(head_w) @ Matrix.Rotation(math.radians(deg), 4, axis_world) @ Matrix.Translation(-head_w)
    loc, scl = pb.location.copy(), pb.scale.copy()
    pb.matrix = mw.inverted() @ (r @ m_w)
    # ตั้ง matrix แล้ว Blender แตก location/scale ใหม่ (สเกล armature 0.01 ทำให้คลาด) → หมุนอย่างเดียว คืนค่าเดิม
    pb.location, pb.scale = loc, scl
    bpy.context.view_layer.update()


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def blend(a, b, w):
    return {k: a[k].slerp(b[k], w) for k in a}


def export(arm, action, path):
    for o in bpy.data.objects:
        o.select_set(o == arm)
    bpy.context.view_layer.objects.active = arm
    arm.animation_data.action = action
    if hasattr(arm.animation_data, "action_slot") and action.slots:
        arm.animation_data.action_slot = action.slots[0]
    s = bpy.context.scene
    s.frame_start, s.frame_end = int(action.frame_range[0]), int(action.frame_range[1])
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE'},
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
                             bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                             bake_anim_simplify_factor=0.0)
    print("exported", path)


# ---------------------------------------------------------------- 1) ทุบพื้นอยู่กับที่
def make_ground_pound(arm):
    src = arm.animation_data.action
    f0, f1 = int(src.frame_range[0]), int(src.frame_range[1])
    frames = {f: sample(arm, f) for f in range(f0, f1 + 1)}
    stand_rots, stand_hips = frames[f0]
    start_xy = stand_hips.translation.xy.copy()
    stand_z = stand_hips.translation.z
    legs = [n for n in stand_rots if any(s in n for s in ("UpLeg", "Leg", "Foot", "ToeBase"))]

    act = new_action(arm, "Kaiju Ground Pound")
    for f in range(f0, f1 + 1):
        rots, hips_w = frames[f]
        z = hips_w.translation.z
        # ช่วงลอยเดิม: ตัวยืดตรงแค่สูงกว่ายืนนิดหน่อย ขากลับเป็นท่ายืน (ไม่หุบขากลางอากาศ)
        air = smooth((z - stand_z) / 0.45)
        rots = dict(rots)
        for n in legs:
            rots[n] = rots[n].slerp(stand_rots[n], air)
        m = hips_w.copy()
        m.translation = Vector((start_xy.x, start_xy.y, min(z, stand_z + 0.08)))
        apply(arm, rots, m)
        key(arm, f)
    arm.animation_data.action = src
    return act


# ---------------------------------------------------------------- 2) กลิ้งเป็นลูกบอล
def make_roll(arm):
    src = arm.animation_data.action
    idle_rots, idle_hips = sample(arm, int(src.frame_range[0]))
    crouch_rots, crouch_hips = sample(arm, 13)  # ย่อตัวก่อนกระโดดของท่าเดิม

    # ท่าขดตัว: เริ่มจากท่ายืนแล้วงอทุกข้อไปข้างหน้า (แกน X โลก บวก = ก้มไปทาง -Y = หน้า)
    apply(arm, idle_rots, idle_hips)
    X = Vector((1, 0, 0))
    for n, d in (("Spine", 30), ("Spine1", 30), ("Spine2", 30), ("Neck", 25), ("Head", 30)):
        rotate_world(arm, B(arm, n), X, d)
    for side in ("Left", "Right"):
        rotate_world(arm, B(arm, side + "UpLeg"), X, -115)   # เข่าขึ้นมาชิดอก
        rotate_world(arm, B(arm, side + "Leg"), X, 140)      # พับเข่า ส้นเท้าชิดก้น
        rotate_world(arm, B(arm, side + "Foot"), X, 30)
        rotate_world(arm, B(arm, side + "Arm"), X, -55)      # แขนกอดหน้าแข้ง
        rotate_world(arm, B(arm, side + "ForeArm"), X, -70)
    ball_rots = {pb.name: pb.rotation_quaternion.copy() for pb in arm.pose.bones}
    hips_ball = arm.matrix_world @ B(arm, "Hips").matrix

    # ศูนย์กลางลูกบอล = เฉลี่ยข้อหลักตอนขด, รัศมี = ข้อที่ไกลศูนย์สุด (ลูกแตะพื้นพอดี)
    joints = [arm.matrix_world @ B(arm, n).head for n in
              ("Hips", "Head", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot", "LeftHand", "RightHand", "Spine2")]
    c0 = sum(joints, Vector()) / len(joints)
    radius = max((j - c0).length for j in joints) * 0.9
    fwd = Vector((0, -1, 0))
    start = Vector((idle_hips.translation.x, idle_hips.translation.y, radius))

    def ball_at(center, deg):
        """ท่าขด หมุนรอบศูนย์ deg องศา (แกน X บวก = ม้วนไปหน้า) แล้วย้ายศูนย์ไปที่ center"""
        return Matrix.Translation(center) @ Matrix.Rotation(math.radians(deg), 4, 'X') @ Matrix.Translation(-c0) @ hips_ball

    act = new_action(arm, "Kaiju Roll Attack")
    TUCK0, TUCK1, ROLL1, END = 1, 16, 64, 84          # 24 fps
    revs = 3.0
    total = math.radians(360.0 * revs) * radius
    end_stand = Matrix.Translation(fwd * total) @ idle_hips
    for f in range(TUCK0, END + 1):
        if f <= TUCK1:                                  # ย่อ -> ขด อยู่กับที่
            w = smooth((f - TUCK0) / (TUCK1 - TUCK0))
            rots = blend(idle_rots, ball_rots, w)
            m = lerp_matrix(idle_hips, ball_at(start, 0.0), w)
        elif f <= ROLL1:                                # กลิ้ง: ระยะ = มุม × รัศมี (ไม่ไถล)
            t = (f - TUCK1) / (ROLL1 - TUCK1)
            ease = smooth(t) * 0.35 + t * 0.65          # ออกตัวกับเบรกนุ่มนิดหน่อย
            deg = 360.0 * revs * ease
            rots = ball_rots
            m = ball_at(start + fwd * (math.radians(deg) * radius), deg)
        else:                                           # คลายตัว ลุกยืน
            w = smooth((f - ROLL1) / (END - ROLL1))
            rots = blend(ball_rots, idle_rots, w)
            m = lerp_matrix(ball_at(start + fwd * total, 360.0 * revs), end_stand, w)
        apply(arm, rots, m)
        key(arm, f)
    print("roll radius", round(radius, 3), "distance", round(total, 3))
    arm.animation_data.action = src
    return act


def lerp_matrix(a, b, w):
    la, ra, sa = a.decompose()
    lb, rb, sb = b.decompose()
    # ต้องเก็บสเกลด้วย — เมทริกซ์โลกของสะโพกมีสเกล 0.01 ของ armature ทิ้งไปแล้วโครงขยาย 100 เท่า
    return Matrix.LocRotScale(la.lerp(lb, w), ra.slerp(rb, w), sa.lerp(sb, w))


def render_preview(arm, action, out_png, frames):
    """ต่อแท่งตามกระดูกแล้ว render ด้านข้าง (Workbench) เพื่อดูท่า"""
    if not out_png:
        return
    arm.animation_data.action = action
    if hasattr(arm.animation_data, "action_slot") and action.slots:
        arm.animation_data.action_slot = action.slots[0]
    scn = bpy.context.scene
    bones = [pb for pb in arm.pose.bones
             if not any(k in pb.name for k in ("Thumb", "Index", "Middle", "Ring", "Pinky", "_End"))]
    sticks = []
    for pb in bones:
        bpy.ops.mesh.primitive_cylinder_add(radius=0.04, depth=1.0)
        sticks.append(bpy.context.active_object)

    def place():
        # วางแท่งในโลกตรงหัว→หางกระดูกทุกเฟรม (ไม่ parent จะได้ไม่ติดสเกล 0.01 ของ armature)
        for pb, cyl in zip(bones, sticks):
            h = arm.matrix_world @ pb.head
            t = arm.matrix_world @ pb.tail
            d = t - h
            cyl.location = (h + t) * 0.5
            cyl.rotation_mode = 'QUATERNION'
            cyl.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized()) if d.length > 1e-5 else Quaternion()
            cyl.scale = (1, 1, max(d.length, 1e-3))

    bpy.ops.object.camera_add(location=(6, -1.0, 1.2), rotation=(math.radians(90), 0, math.radians(90)))
    scn.camera = bpy.context.active_object
    scn.camera.data.type = 'ORTHO'
    scn.camera.data.ortho_scale = 3.2
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.render.resolution_x, scn.render.resolution_y = 360, 300
    import os
    for f in frames:
        scn.frame_set(f)
        place()
        bpy.context.view_layer.update()
        # กล้องตามตัวไปด้านข้าง (ท่ากลิ้งเคลื่อนไปไกล)
        hips = arm.matrix_world @ B(arm, "Hips").head
        scn.camera.location = (6, hips.y, 0.95)
        scn.render.filepath = os.path.join(out_png, f"{action.name.replace(' ', '_')}_{f:03d}.png")
        bpy.ops.render.render(write_still=True)
    for o in sticks + [scn.camera]:
        bpy.data.objects.remove(o, do_unlink=True)


import os
os.makedirs(OUT, exist_ok=True)
arm = load()
gp = make_ground_pound(arm)
roll = make_roll(arm)
export(arm, gp, os.path.join(OUT, "Kaiju Ground Pound.fbx"))
export(arm, roll, os.path.join(OUT, "Kaiju Roll Attack.fbx"))
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    render_preview(arm, gp, PREVIEW, [1, 13, 25, 37, 43, 55, 75])
    render_preview(arm, roll, PREVIEW, [1, 10, 16, 22, 28, 34, 64, 76, 84])
