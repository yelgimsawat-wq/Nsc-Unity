using System;
using System.Collections.Generic;
using Nsc.BoneLimb;
using Nsc.Robots;
using UnityEngine;

namespace Nsc.Limbs.Bones
{
    /// <summary>
    /// แขนขาแบบ "กระดูก" — แขน/ขาที่เห็นเป็นกระดูกแข็งติดลำตัว แล้วใช้ IK เอื้อมไปหามือ/เท้าฟิสิกส์
    /// แทนการโชว์ท่อนฟิสิกส์ที่ต่อกันด้วย joint (ซึ่งแกว่ง/ยืดเหมือนยาง)
    ///
    /// ไม่แตะเกมเพลย์: ฟิสิกส์ทุกชิ้น (มือ เท้า ลำตัว joint collider) ทำงานเหมือนเดิมบน server
    /// คอมโพเนนต์นี้เปลี่ยนแค่สิ่งที่มองเห็น — รันเองในทุกเครื่อง ไม่มี RPC
    ///
    /// สองโหมด:
    ///   1) ไม่ใส่ Rigged Model — ใช้ mesh เดิมของหุ่น: ท่อนบน/ท่อนล่างถูกสำเนาไปไว้บนกระดูก มือ/เท้ายังเป็นชิ้นฟิสิกส์
    ///   2) ใส่ Rigged Model (โมเดลมีกระดูก เช่น Mixamo) เป็นลูกของ Torso — IK ขยับกระดูกของโมเดลไปหามือ/เท้าฟิสิกส์
    ///      แล้วซ่อน mesh ฟิสิกส์ของหุ่นทั้งตัว
    ///
    /// ใส่บน GameObject เดียวกับ Robot (Torso) — เมนู Tools ▸ NSC ▸ Bone Rig ▸ Add To Selected Robot
    /// ชิ้นที่หลุด (LimbAttachment) หรือตอนลำตัวล้ม (ragdoll) กลับไปโชว์ชิ้นฟิสิกส์อัตโนมัติ
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)] // หลังทุกอย่างขยับ transform เสร็จ (รวม Animator)
    public class RobotBoneRig : MonoBehaviour
    {
        public enum PoleMode
        {
            /// <summary>ศอก/เข่าชี้ทิศคงที่เทียบลำตัว — นิ่งที่สุด ดูเป็นหุ่น</summary>
            TorsoDirection,
            /// <summary>ศอก/เข่าชี้ตามข้อต่อฟิสิกส์ตัวจริง — ตามท่าฟิสิกส์มากกว่า แต่สั่นตามด้วย</summary>
            PhysicsJoint,
        }

        [Serializable]
        public struct RiggedLimb
        {
            public LimbSlot slot;
            [Tooltip("กระดูกจากโคนไปปลาย เช่น LeftArm, LeftForeArm, LeftHand")]
            public Transform[] bones;
        }

        [Header("ใช้กับชิ้นไหน")]
        [SerializeField] private bool arms = true;
        [SerializeField] private bool legs = true;

        [Header("Rigged Model (ไม่บังคับ)")]
        [Tooltip("โมเดลที่มีกระดูก วางเป็นลูกของ Torso ให้ทับตัวหุ่น — ว่าง = ใช้ mesh เดิมของหุ่น")]
        [SerializeField] private Transform riggedModel;
        [Tooltip("ปล่อยว่างได้ — หากระดูกชื่อแบบ Mixamo ให้เอง (LeftArm/LeftForeArm/LeftHand, LeftUpLeg/LeftLeg/LeftFoot)")]
        [SerializeField] private RiggedLimb[] riggedLimbs = Array.Empty<RiggedLimb>();
        [Tooltip("หมุนมือ/เท้าของโมเดลตามชิ้นฟิสิกส์")]
        [SerializeField] private bool alignHandsAndFeet = true;
        [Tooltip("ใช้ IK บานพับของ nok (IKLimb): ศอก/เข่าพับรอบแกนที่โมเดลออกแบบไว้ ไม่บิดทะลุโมเดล | ปิด = FABRIK เดิม")]
        [SerializeField] private bool hingeIK = true;
        [Tooltip("ย่อ/ขยายแขนขาของโมเดลตอนเริ่มให้ยาวเท่าโซ่ฟิสิกส์ — ไม่งั้นขาโมเดลที่ยาวกว่าต้องงอเข่ามากจนดูขาโก่ง")]
        [SerializeField] private bool matchLimbLength = true;
        [Tooltip("ท่ากำมือจากโมเดล (เฟรมแรก = แบ, เฟรมท้าย = กำ) เช่น Hand_Grip_L / Hand_Grip_R ใน RobotNSC_Rigged — ว่าง = นิ้วตามท่าในซีน")]
        [SerializeField] private AnimationClip leftGripClip, rightGripClip;

        [Header("ทิศศอก / เข่า")]
        [SerializeField] private PoleMode poleMode = PoleMode.TorsoDirection;
        [Tooltip("ทิศศอกในแกนของลำตัว — ใช้เมื่อท่าเริ่มต้นของโมเดลแขนเหยียดตรง (T-pose)")]
        [SerializeField] private Vector3 armPoleDirection = new Vector3(0f, -1f, -1f);
        [Tooltip("ทิศเข่าในแกนของลำตัว — ใช้เมื่อท่าเริ่มต้นของโมเดลขาเหยียดตรง")]
        [SerializeField] private Vector3 legPoleDirection = new Vector3(0f, 0f, 1f);

        [Header("Behaviour")]
        [Tooltip("ตอนลำตัวล้ม (ragdoll) กลับไปโชว์ชิ้นฟิสิกส์ ให้ดูล้มแบบตุ๊กตาเหมือนเดิม")]
        [SerializeField] private bool physicsLookWhenRagdoll = true;
        [Range(1, 20)] [SerializeField] private int iterations = 10;

        [Header("Debug")]
        [Tooltip("โชว์ชิ้นฟิสิกส์ซ้อนไว้ด้วย (ดูว่ากระดูกตามฟิสิกส์ถูกไหม)")]
        [SerializeField] private bool showPhysicsParts;
        [SerializeField] private bool drawGizmos = true;

        private static readonly Dictionary<LimbSlot, string[]> MixamoNames = new Dictionary<LimbSlot, string[]>
        {
            { LimbSlot.LeftArm, new[] { "LeftArm", "LeftForeArm", "LeftHand" } },
            { LimbSlot.RightArm, new[] { "RightArm", "RightForeArm", "RightHand" } },
            { LimbSlot.LeftLeg, new[] { "LeftUpLeg", "LeftLeg", "LeftFoot" } },
            { LimbSlot.RightLeg, new[] { "RightUpLeg", "RightLeg", "RightFoot" } },
        };

        private Robot robot;
        private Transform torso;
        private readonly List<LimbRig> rigs = new List<LimbRig>();

        // โหมด Rigged Model: ชิ้นฟิสิกส์ที่ไม่ใช่แขนขา (ลำตัว หัว) + renderer ของโมเดล
        private readonly List<Renderer> bodyPhysicsRenderers = new List<Renderer>();
        private Renderer[] modelRenderers = Array.Empty<Renderer>();
        private bool resetModelPose;
        private bool? modelShown;

        public int RigCount => rigs.Count;
        public bool UsesRiggedModel => riggedModel != null;

        private void Start()
        {
            robot = GetComponent<Robot>();
            if (robot == null) robot = Robot.FromTransform(transform);
            if (robot == null || robot.Torso == null || robot.Torso.Body == null)
            {
                Debug.LogWarning("[RobotBoneRig] ต้องวางบน Torso ที่มี Robot + TorsoBalance", this);
                enabled = false;
                return;
            }

            torso = robot.Torso.Body.transform;
            Rigidbody torsoBody = robot.Torso.Body;

            if (UsesRiggedModel)
            {
                if (!riggedModel.IsChildOf(torso))
                    Debug.LogWarning("[RobotBoneRig] Rigged Model ควรเป็นลูกของ Torso ไม่งั้นโมเดลจะไม่ตามตัวหุ่น", riggedModel);

                Animator animator = riggedModel.GetComponentInChildren<Animator>();
                resetModelPose = animator == null || animator.runtimeAnimatorController == null;
                modelRenderers = riggedModel.GetComponentsInChildren<Renderer>(true);
            }

            var limbBodies = new HashSet<Rigidbody>();
            foreach (LimbSlot slot in LimbSlots.All)
            {
                if (slot.IsArm() ? !arms : !legs) continue;
                RobotLimb limb = robot.GetLimb(slot);
                if (limb == null) continue;

                PhysicsChain chain = PhysicsChain.TryFind(limb, torsoBody);
                if (chain == null)
                {
                    Debug.LogWarning($"[RobotBoneRig] {slot}: หาโซ่ joint จากปลายไปถึงลำตัวไม่เจอ ข้ามชิ้นนี้", limb);
                    continue;
                }

                Vector3 pole = slot.IsArm() ? armPoleDirection : legPoleDirection;
                LimbRig rig;
                if (UsesRiggedModel)
                {
                    Transform[] bones = RiggedBones(slot);
                    if (bones == null)
                    {
                        Debug.LogWarning($"[RobotBoneRig] {slot}: หากระดูกในโมเดลไม่ครบ ข้ามชิ้นนี้ (ใส่เองในช่อง Rigged Limbs ได้)", riggedModel);
                        continue;
                    }
                    AnimationClip grip = slot == LimbSlot.LeftArm ? leftGripClip : slot == LimbSlot.RightArm ? rightGripClip : null;
                    rig = new SkinnedRig(limb, chain, bones, torso, pole, riggedModel, hingeIK, grip, matchLimbLength);
                }
                else
                {
                    rig = new SegmentRig(limb, chain, torso, pole);
                }

                rigs.Add(rig);
                limbBodies.UnionWith(chain.Bodies);
            }

            if (UsesRiggedModel) CollectBodyRenderers(torsoBody, limbBodies);
        }

        private void LateUpdate()
        {
            bool ragdoll = physicsLookWhenRagdoll && robot.Torso.IsRagdoll;

            if (UsesRiggedModel) SetModelShown(!ragdoll);

            foreach (LimbRig rig in rigs)
            {
                bool useBones = !ragdoll && rig.Limb != null && rig.Limb.IsAttached;
                rig.SetBonesShown(useBones, showPhysicsParts);
                if (useBones) rig.Solve(torso, poleMode, iterations, resetModelPose, alignHandsAndFeet);
                else rig.UpdateDetached();
            }
        }

        private void OnDisable()
        {
            foreach (LimbRig rig in rigs)
            {
                rig.ClearDetached();
                rig.SetBonesShown(false, true);
            }
            if (UsesRiggedModel && robot != null) SetModelShown(false);
        }

        private void OnDestroy()
        {
            foreach (LimbRig rig in rigs) rig.Destroy();
            rigs.Clear();
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            foreach (LimbRig rig in rigs) rig.DrawGizmos();
        }

        // ---------------------------------------------------------------- rigged model

        private Transform[] RiggedBones(LimbSlot slot)
        {
            foreach (RiggedLimb entry in riggedLimbs)
                if (entry.slot == slot && entry.bones != null && entry.bones.Length >= 2 &&
                    Array.TrueForAll(entry.bones, b => b != null))
                    return entry.bones;

            string[] names = MixamoNames[slot];
            var found = new Transform[names.Length];
            foreach (Transform t in riggedModel.GetComponentsInChildren<Transform>(true))
            {
                string bare = t.name.Substring(t.name.LastIndexOf(':') + 1);
                for (int i = 0; i < names.Length; i++)
                    if (found[i] == null && bare == names[i]) found[i] = t;
            }
            return Array.TrueForAll(found, b => b != null) ? found : null;
        }

        /// <summary>ชิ้นฟิสิกส์ของหุ่นที่ไม่ใช่แขนขา (ลำตัว หัว ฯลฯ — ต่อ joint ถึงลำตัว) ซ่อนเมื่อโชว์โมเดล</summary>
        private void CollectBodyRenderers(Rigidbody torsoBody, HashSet<Rigidbody> limbBodies)
        {
            Transform root = robot.Root != null ? robot.Root : torso;
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
            {
                if (limbBodies.Contains(body) || !PhysicsChain.ReachesTorso(body, torsoBody)) continue;
                PhysicsChain.AddOwnRenderers(body, bodyPhysicsRenderers, riggedModel);
            }
        }

        private void SetModelShown(bool shown)
        {
            if (modelShown == shown) return;
            modelShown = shown;

            foreach (Renderer r in modelRenderers)
                if (r != null) r.forceRenderingOff = !shown;

            bool hideBody = shown && !showPhysicsParts;
            foreach (Renderer r in bodyPhysicsRenderers)
                if (r != null) r.forceRenderingOff = hideBody;
        }

        // ================================================================
        //  โซ่ฟิสิกส์ของแขน/ขาหนึ่งชิ้น: [ท่อนบน, ..., มือ/เท้า] ต่อกันด้วย joint ถึงลำตัว
        // ================================================================

        private class PhysicsChain
        {
            public Transform Tip;
            public Vector3 TipAnchor;              // ข้อมือ/ข้อเท้า ในแกนของ tip
            public Transform[] Segments;           // ท่อนจากโคนไปปลาย (ไม่รวม tip)
            public Vector3[] SegmentAnchors;       // ข้อต่อโคนของแต่ละท่อน ในแกนของท่อนนั้น
            public List<Rigidbody> Bodies = new List<Rigidbody>();

            public static PhysicsChain TryFind(RobotLimb limb, Rigidbody torsoBody)
            {
                Rigidbody tipBody = limb.GetComponent<Rigidbody>();
                if (tipBody == null) return null;

                var bodies = new List<Rigidbody>();
                var joints = new List<Joint>();
                Rigidbody current = tipBody;
                for (int safety = 0; safety < 8 && current != null && current != torsoBody; safety++)
                {
                    Joint link = FindLink(current);
                    if (link == null) return null;
                    bodies.Add(current);
                    joints.Add(link);
                    current = link.connectedBody;
                }
                if (current != torsoBody || bodies.Count < 2) return null;

                bodies.Reverse();
                joints.Reverse();

                int segmentCount = bodies.Count - 1;
                var chain = new PhysicsChain
                {
                    Tip = tipBody.transform,
                    TipAnchor = joints[segmentCount].anchor,
                    Segments = new Transform[segmentCount],
                    SegmentAnchors = new Vector3[segmentCount],
                    Bodies = bodies,
                };
                for (int i = 0; i < segmentCount; i++)
                {
                    chain.Segments[i] = bodies[i].transform;
                    chain.SegmentAnchors[i] = joints[i].anchor;
                }
                return chain;
            }

            public static Joint FindLink(Rigidbody body)
            {
                foreach (Joint joint in body.GetComponents<Joint>())
                    if (joint != null && joint.connectedBody != null) return joint;
                return null;
            }

            public static bool ReachesTorso(Rigidbody body, Rigidbody torsoBody)
            {
                for (int safety = 0; safety < 8 && body != null; safety++)
                {
                    if (body == torsoBody) return true;
                    Joint link = FindLink(body);
                    body = link != null ? link.connectedBody : null;
                }
                return false;
            }

            /// <summary>renderer ที่มองเห็นอยู่และเป็นของ body นี้เอง (ไม่ใช่ของชิ้นที่มี Rigidbody ของตัวเอง เช่นไอเทม)</summary>
            public static void AddOwnRenderers(Rigidbody body, List<Renderer> into, Transform exclude = null)
            {
                foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.GetComponentInParent<Rigidbody>() != body) continue;
                    if (exclude != null && r.transform.IsChildOf(exclude)) continue;
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) continue;
                    into.Add(r);
                }
            }

            /// <summary>ข้อต่อ index (0 = ไหล่/สะโพก ... สุดท้าย = ข้อมือ/ข้อเท้า) ตามตำแหน่งฟิสิกส์ตอนนี้</summary>
            public Vector3 Point(int index) =>
                index < Segments.Length
                    ? Segments[index].TransformPoint(SegmentAnchors[index])
                    : Tip.TransformPoint(TipAnchor);

            public Vector3 Wrist => Tip.TransformPoint(TipAnchor);
        }

        private static Vector3 PoleFromBind(Vector3[] points, float totalLength, Transform torso, Vector3 fallback)
        {
            int last = points.Length - 1;
            Vector3 bend = Vector3.ProjectOnPlane(points[Mathf.Max(1, last / 2)] - points[0], points[last] - points[0]);
            return bend.magnitude > totalLength * 0.02f
                ? torso.InverseTransformDirection(bend.normalized)
                : fallback.normalized;
        }

        private static void SetHidden(List<Renderer> renderers, bool hidden)
        {
            foreach (Renderer r in renderers)
                if (r != null) r.forceRenderingOff = hidden;
        }

        // ================================================================
        //  rig หนึ่งชิ้น
        // ================================================================

        private abstract class LimbRig
        {
            public RobotLimb Limb;
            protected PhysicsChain Chain;
            protected Vector3[] Points;
            protected float[] Lengths;
            protected float TotalLength;
            protected Vector3 PoleLocal;        // ทิศศอก/เข่าในแกนลำตัว
            protected bool Shown;

            public abstract void SetBonesShown(bool shown, bool keepPhysicsVisible);
            public abstract void Solve(Transform torso, PoleMode poleMode, int iterations, bool resetPose, bool alignEnd);
            /// <summary>ชิ้นหลุด/ตอนไม่ได้ใช้กระดูก — ให้ภาพของชิ้นที่หลุดตามชิ้นฟิสิกส์ (ค่าเริ่ม: ไม่ทำอะไร โชว์ชิ้นฟิสิกส์เอง)</summary>
            public virtual void UpdateDetached() { }
            /// <summary>ทิ้งภาพชิ้นหลุดที่สร้างไว้ (ปิดคอมโพเนนต์/ปิดซีน)</summary>
            public virtual void ClearDetached() { }
            public virtual void Destroy() => SetBonesShown(false, true);

            protected Vector3 Pole(Transform torso, PoleMode mode, Vector3 root, Vector3 goal) =>
                mode == PoleMode.PhysicsJoint && Points.Length > 2
                    ? Chain.Point(Points.Length / 2)
                    : (root + goal) * 0.5f + torso.TransformDirection(PoleLocal) * TotalLength;

            public void DrawGizmos()
            {
                if (!Shown || Points == null) return;
                Gizmos.color = Color.cyan;
                for (int i = 0; i < Points.Length - 1; i++) Gizmos.DrawLine(Points[i], Points[i + 1]);
                Gizmos.color = Color.yellow;
                if (Chain.Tip != null) Gizmos.DrawWireSphere(Chain.Wrist, TotalLength * 0.05f);
            }
        }

        /// <summary>โหมดไม่มีโมเดล: สำเนา mesh ของท่อนบน/ล่างไปไว้บนกระดูก มือ/เท้ายังเป็นชิ้นฟิสิกส์</summary>
        private class SegmentRig : LimbRig
        {
            private readonly Transform rigRoot;
            private readonly Transform[] bones;
            private readonly Quaternion[] bindRotation;   // การหมุนตอน bind เทียบลำตัว
            private readonly Vector3[] bindDirection;     // ทิศท่อนตอน bind ในแกนกระดูก
            private readonly Vector3[] originOffset;      // origin ของท่อน เทียบข้อต่อโคนท่อน (แกนกระดูก)
            private readonly Vector3[] bindPoints;        // ข้อต่อตอน bind ในแกนลำตัว
            private readonly List<Renderer> physicsRenderers = new List<Renderer>();
            private bool physicsHidden;

            public SegmentRig(RobotLimb limb, PhysicsChain chain, Transform torso, Vector3 fallbackPole)
            {
                Limb = limb;
                Chain = chain;
                int boneCount = chain.Segments.Length;
                Points = new Vector3[boneCount + 1];
                bindPoints = new Vector3[boneCount + 1];
                for (int i = 0; i <= boneCount; i++)
                {
                    Points[i] = chain.Point(i);
                    bindPoints[i] = torso.InverseTransformPoint(Points[i]);
                }

                // กระดูกเป็นพี่น้องกับท่อนฟิสิกส์ (parent เดียวกัน) → scale ไม่เพี้ยน; ตำแหน่งตั้งเป็น world ทุกเฟรม
                rigRoot = new GameObject($"BoneRig_{limb.Slot}").transform;
                rigRoot.SetParent(chain.Segments[0].parent, false);

                bones = new Transform[boneCount];
                bindRotation = new Quaternion[boneCount];
                bindDirection = new Vector3[boneCount];
                originOffset = new Vector3[boneCount];
                Lengths = new float[boneCount];

                for (int i = 0; i < boneCount; i++)
                {
                    Transform segment = chain.Segments[i];
                    Vector3 along = Points[i + 1] - Points[i];
                    Lengths[i] = along.magnitude;
                    TotalLength += Lengths[i];

                    var bone = new GameObject($"Bone_{segment.name}").transform;
                    bone.SetParent(rigRoot, false);
                    bone.localScale = segment.localScale;
                    bone.SetPositionAndRotation(segment.position, segment.rotation);
                    bones[i] = bone;

                    bindRotation[i] = Quaternion.Inverse(torso.rotation) * segment.rotation;
                    bindDirection[i] = Lengths[i] > 1e-6f ? Quaternion.Inverse(segment.rotation) * (along / Lengths[i]) : Vector3.up;
                    originOffset[i] = Quaternion.Inverse(segment.rotation) * (segment.position - Points[i]);

                    CloneRenderers(segment, bone);
                }

                PoleLocal = PoleFromBind(Points, TotalLength, torso, fallbackPole);
                rigRoot.gameObject.SetActive(false);
            }

            /// <summary>
            /// สำเนา MeshRenderer ที่มองเห็นอยู่ของท่อนนี้ไปไว้บนกระดูก (ไม่รวมชิ้นที่มี Rigidbody ของตัวเอง)
            /// สร้างลำดับ child ซ้ำแบบเดียวกับต้นฉบับ (local position/rotation/scale เดิม) → หน้าตาตรงเป๊ะแม้ scale ไม่เท่ากันทุกแกน
            /// </summary>
            private void CloneRenderers(Transform segment, Transform bone)
            {
                var sources = new List<Renderer>();
                PhysicsChain.AddOwnRenderers(segment.GetComponent<Rigidbody>(), sources);
                var mirrors = new Dictionary<Transform, Transform> { { segment, bone } };

                foreach (Renderer r in sources)
                {
                    if (!(r is MeshRenderer source)) continue;
                    MeshFilter filter = source.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;

                    Transform copy = Mirror(source.transform, mirrors);
                    copy.gameObject.layer = source.gameObject.layer;
                    copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var renderer = copy.gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = source.sharedMaterials;
                    renderer.shadowCastingMode = source.shadowCastingMode;
                    renderer.receiveShadows = source.receiveShadows;
                    renderer.renderingLayerMask = source.renderingLayerMask;

                    physicsRenderers.Add(source);
                }
            }

            private static Transform Mirror(Transform source, Dictionary<Transform, Transform> mirrors)
            {
                if (mirrors.TryGetValue(source, out Transform existing)) return existing;

                Transform parent = Mirror(source.parent, mirrors);
                var copy = new GameObject(source.name).transform;
                copy.SetParent(parent, false);
                copy.localPosition = source.localPosition;
                copy.localRotation = source.localRotation;
                copy.localScale = source.localScale;
                mirrors[source] = copy;
                return copy;
            }

            public override void SetBonesShown(bool shown, bool keepPhysicsVisible)
            {
                if (rigRoot != null && shown != Shown)
                {
                    rigRoot.gameObject.SetActive(shown);
                    Shown = shown;
                }

                bool hidePhysics = shown && !keepPhysicsVisible;
                if (hidePhysics == physicsHidden) return;
                SetHidden(physicsRenderers, hidePhysics);
                physicsHidden = hidePhysics;
            }

            public override void Solve(Transform torso, PoleMode poleMode, int iterations, bool resetPose, bool alignEnd)
            {
                if (Chain.Tip == null) return;

                for (int i = 0; i < Points.Length; i++) Points[i] = torso.TransformPoint(bindPoints[i]);
                Vector3 root = Points[0];
                Vector3 goal = Chain.Wrist;
                BoneChainSolver.Solve(Points, Lengths, root, goal, true, Pole(torso, poleMode, root, goal), iterations);

                for (int i = 0; i < bones.Length; i++)
                {
                    Quaternion rest = torso.rotation * bindRotation[i];
                    Vector3 wanted = Points[i + 1] - Points[i];
                    Quaternion rotation = wanted.sqrMagnitude > 1e-10f
                        ? Quaternion.FromToRotation(rest * bindDirection[i], wanted) * rest
                        : rest;
                    bones[i].SetPositionAndRotation(Points[i] + rotation * originOffset[i], rotation);
                }
            }

            public override void Destroy()
            {
                base.Destroy();
                if (rigRoot != null) UnityEngine.Object.Destroy(rigRoot.gameObject);
            }
        }

        /// <summary>โหมดโมเดลมีกระดูก: หมุนกระดูกของโมเดลให้ปลายไปที่ข้อมือ/ข้อเท้าฟิสิกส์ แล้วซ่อนชิ้นฟิสิกส์ทั้งโซ่</summary>
        private class SkinnedRig : LimbRig
        {
            private readonly Transform[] bones;
            private readonly Quaternion[] bindLocal;
            private readonly Quaternion endOffset;        // มือ/เท้าโมเดล เทียบชิ้นฟิสิกส์ ตอน bind
            private readonly Vector3 rootScale;
            private readonly List<Renderer> physicsRenderers = new List<Renderer>();
            private bool physicsHidden;
            // ชิ้นหลุด: สำเนาโมเดลที่เหลือแค่แขน/ขานี้ ตามชิ้นฟิสิกส์ที่หลุดไป (ชิ้นฟิสิกส์เป็นโมเดลเก่า ไม่ต้องโชว์)
            private readonly Transform modelRoot;
            private Transform proxyRoot;
            private Transform[] proxyBones;
            private readonly List<Transform> proxyCollapsed = new List<Transform>();
            private bool proxyUnavailable; // หากระดูกในสำเนาไม่ครบครั้งหนึ่งแล้ว ไม่ต้องสร้าง-ทิ้งซ้ำทุกเฟรม
            // IK บานพับของ nok: เป้า = ข้อมือ/ข้อเท้าฟิสิกส์ (null = ใช้ FABRIK เดิม)
            private readonly IKLimb hinge;
            private readonly Transform hingeTarget;

            public SkinnedRig(RobotLimb limb, PhysicsChain chain, Transform[] modelBones, Transform torso, Vector3 fallbackPole,
                              Transform model, bool useHinge, AnimationClip gripClip, bool matchLength)
            {
                if (matchLength)
                {
                    float modelLength = 0f, physicsLength = 0f;
                    for (int i = 0; i < modelBones.Length - 1; i++) modelLength += Vector3.Distance(modelBones[i].position, modelBones[i + 1].position);
                    for (int i = 0; i < chain.Segments.Length; i++) physicsLength += Vector3.Distance(chain.Point(i), chain.Point(i + 1));
                    if (modelLength > 1e-4f && physicsLength > 1e-4f) modelBones[0].localScale *= physicsLength / modelLength;
                }

                modelRoot = model;
                Limb = limb;
                Chain = chain;
                bones = modelBones;
                int n = bones.Length;
                Points = new Vector3[n];
                Lengths = new float[n - 1];
                bindLocal = new Quaternion[n];
                for (int i = 0; i < n; i++)
                {
                    bindLocal[i] = bones[i].localRotation;
                    Points[i] = bones[i].position;
                }
                for (int i = 0; i < n - 1; i++) TotalLength += Vector3.Distance(Points[i], Points[i + 1]);

                endOffset = Quaternion.Inverse(chain.Tip.rotation) * bones[n - 1].rotation;
                rootScale = bones[0].localScale;
                PoleLocal = PoleFromBind(Points, TotalLength, torso, fallbackPole);

                foreach (Rigidbody body in chain.Bodies) PhysicsChain.AddOwnRenderers(body, physicsRenderers);

                if (!useHinge || n != 3) return;
                // แกนอ้างอิง = ตัวโมเดล (หันหน้าตามหุ่นเสมอ) ไม่ใช่ลำตัวฟิสิกส์ที่แกนอาจไม่ตรงทิศหน้า
                hingeTarget = new GameObject($"{limb.Slot} hinge target") { hideFlags = HideFlags.HideAndDontSave }.transform;
                hingeTarget.SetPositionAndRotation(bones[2].position, bones[2].rotation);
                hinge = new IKLimb
                {
                    name = limb.Slot.ToString(), isArm = limb.Slot.IsArm(),
                    root = bones[0], mid = bones[1], tip = bones[2], target = hingeTarget, gripClip = gripClip,
                };
                // Init เล่นคลิปกำมือบนโมเดล — คลิปนี้ขยับกระดูกทั้งตัว (แขน ขา ลำตัว) จึงคืนท่าในซีนหลังอ่านเสร็จ
                Transform[] all = model.GetComponentsInChildren<Transform>(true);
                var pose = new (Vector3 pos, Quaternion rot, Vector3 scale)[all.Length];
                for (int i = 0; i < all.Length; i++) pose[i] = (all[i].localPosition, all[i].localRotation, all[i].localScale);
                hinge.Init(model, 0f, hinge.isArm ? new ShoulderLimit() : new HipLimit(), model.gameObject);
                for (int i = 0; i < all.Length; i++)
                {
                    all[i].SetLocalPositionAndRotation(pose[i].pos, pose[i].rot);
                    all[i].localScale = pose[i].scale;
                }
                if (gripClip != null) hinge.Grip = 1f; // หุ่นกำหมัดตลอด
            }

            public override void SetBonesShown(bool shown, bool keepPhysicsVisible)
            {
                if (shown != Shown)
                {
                    // ชิ้นหลุด: ยุบแขน/ขาของโมเดลทิ้ง (scale 0) แล้วโชว์สำเนาแขน/ขาของโมเดลที่ตามชิ้นที่หลุดแทน
                    if (bones[0] != null) bones[0].localScale = shown ? rootScale : Vector3.zero;
                    Shown = shown;
                    if (shown) DestroyProxy();
                }

                // หลุดจริงและมีสำเนาโมเดลแล้ว (สร้างใน UpdateDetached) → ซ่อนชิ้นฟิสิกส์ | ล้มทั้งตัว → ชิ้นฟิสิกส์ตามเดิม
                bool hidePhysics = (shown && !keepPhysicsVisible) || (!shown && proxyRoot != null);
                if (hidePhysics == physicsHidden) return;
                SetHidden(physicsRenderers, hidePhysics);
                physicsHidden = hidePhysics;
            }

            /// <summary>วางกระดูกของสำเนาตามข้อต่อของชิ้นที่หลุด: โคน → ข้อศอก/เข่า → ข้อมือ/ข้อเท้า แล้วหมุนมือ/เท้าตามชิ้นฟิสิกส์</summary>
            public override void ClearDetached() => DestroyProxy();

            public override void UpdateDetached()
            {
                // ไม่ใช้กระดูกเพราะล้มทั้งตัว (ชิ้นยังติด) → ไม่ต้องมีสำเนา
                if (Limb == null || Limb.IsAttached) { DestroyProxy(); return; }
                if (Chain.Tip == null || !EnsureProxy()) return;
                int n = proxyBones.Length;
                proxyBones[0].position = Chain.Point(0);
                // ผิวที่ผูกกับไหล่/สะโพกปนกับกระดูกที่ยุบ → ยุบไว้ที่โคนชิ้นนี้ ไม่ยืดกลับไปหาตัวหุ่น
                foreach (Transform collapsed in proxyCollapsed) collapsed.position = proxyBones[0].position;
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 current = proxyBones[i + 1].position - proxyBones[i].position;
                    Vector3 wanted = Chain.Point(Mathf.Min(i + 1, Chain.Segments.Length)) - proxyBones[i].position;
                    if (current.sqrMagnitude < 1e-10f || wanted.sqrMagnitude < 1e-10f) continue;
                    proxyBones[i].rotation = Quaternion.FromToRotation(current, wanted) * proxyBones[i].rotation;
                }
                proxyBones[n - 1].rotation = Chain.Tip.rotation * endOffset;
            }

            /// <summary>
            /// สร้างสำเนาโมเดลครั้งแรกที่ชิ้นนี้หลุด: ย้ายกระดูกโคนแขน/ขาไปเป็นลูกของรากสำเนา แล้วยุบกระดูกอื่นทั้งหมด (scale 0)
            /// เหลือ mesh เฉพาะชิ้นนี้ — ไม่ยืดผิวจากไหล่/สะโพกตามชิ้นที่ปลิวไปไกล
            /// </summary>
            private bool EnsureProxy()
            {
                if (proxyRoot != null) return true;
                if (proxyUnavailable || modelRoot == null || bones[0] == null) return false;

                GameObject clone = UnityEngine.Object.Instantiate(modelRoot.gameObject, null);
                clone.name = $"{modelRoot.name} (detached {Limb.Slot})";
                foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.Destroy(animator);
                proxyRoot = clone.transform;
                proxyRoot.SetPositionAndRotation(modelRoot.position, modelRoot.rotation);
                proxyRoot.localScale = modelRoot.lossyScale;

                proxyBones = new Transform[bones.Length];
                for (int i = 0; i < bones.Length; i++) proxyBones[i] = FindByPath(proxyRoot, RelativePath(modelRoot, bones[i]));
                if (Array.Exists(proxyBones, b => b == null))
                {
                    Debug.LogWarning($"[RobotBoneRig] {Limb.Slot}: หากระดูกในสำเนาโมเดลไม่ครบ ชิ้นที่หลุดจะโชว์ชิ้นฟิสิกส์แทน", modelRoot);
                    DestroyProxy();
                    proxyUnavailable = true;
                    return false;
                }

                // ตัวต้นแบบยุบชิ้นนี้ไปแล้วตอนสำเนา → คืนขนาดจริงในโลก (ขนาดพ่อเดิม × rootScale) หลังย้ายมาเป็นลูกของรากสำเนา
                Vector3 worldScale = Vector3.Scale(bones[0].parent != null ? bones[0].parent.lossyScale : Vector3.one, rootScale);
                proxyBones[0].SetParent(proxyRoot, true);
                Vector3 rootLossy = proxyRoot.lossyScale;
                proxyBones[0].localScale = new Vector3(worldScale.x / rootLossy.x, worldScale.y / rootLossy.y, worldScale.z / rootLossy.z);
                foreach (Transform child in proxyRoot)
                    if (child != proxyBones[0] && child.GetComponent<SkinnedMeshRenderer>() == null)
                    {
                        child.localScale = Vector3.zero;
                        proxyCollapsed.Add(child);
                    }
                foreach (SkinnedMeshRenderer skin in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.updateWhenOffscreen = true; // ชิ้นหลุดปลิวออกนอก bounds เดิม ไม่งั้นโดน cull หาย
                    skin.forceRenderingOff = false;
                }
                return true;
            }

            private void DestroyProxy()
            {
                if (proxyRoot != null) UnityEngine.Object.Destroy(proxyRoot.gameObject);
                proxyRoot = null;
                proxyBones = null;
                proxyCollapsed.Clear();
            }

            private static string RelativePath(Transform root, Transform t)
            {
                string path = t.name;
                for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
                return path;
            }

            private static Transform FindByPath(Transform root, string path) => root.Find(path);

            public override void Solve(Transform torso, PoleMode poleMode, int iterations, bool resetPose, bool alignEnd)
            {
                if (Chain.Tip == null) return;
                int n = bones.Length;

                if (resetPose)
                    for (int i = 0; i < n; i++) bones[i].localRotation = bindLocal[i];

                if (hinge != null)
                {
                    hingeTarget.SetPositionAndRotation(Chain.Wrist, alignEnd ? Chain.Tip.rotation * endOffset : bones[n - 1].rotation);
                    hinge.Solve(modelRoot.rotation);
                    hinge.ApplyFingers();
                    for (int i = 0; i < n; i++) Points[i] = bones[i].position; // ให้ gizmo วาดตามกระดูกจริง
                    return;
                }

                TotalLength = 0f;
                for (int i = 0; i < n; i++) Points[i] = bones[i].position;
                for (int i = 0; i < n - 1; i++)
                {
                    Lengths[i] = Vector3.Distance(Points[i], Points[i + 1]);
                    TotalLength += Lengths[i];
                }

                Vector3 root = Points[0];
                Vector3 goal = Chain.Wrist;
                BoneChainSolver.Solve(Points, Lengths, root, goal, true, Pole(torso, poleMode, root, goal), iterations);

                // ไล่จากโคน: ลูกขยับตามพ่อ จึงวัดทิศปัจจุบันใหม่ทุกท่อน
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 current = bones[i + 1].position - bones[i].position;
                    Vector3 wanted = Points[i + 1] - Points[i];
                    if (current.sqrMagnitude < 1e-10f || wanted.sqrMagnitude < 1e-10f) continue;
                    bones[i].rotation = Quaternion.FromToRotation(current, wanted) * bones[i].rotation;
                }

                if (alignEnd) bones[n - 1].rotation = Chain.Tip.rotation * endOffset;
            }

            public override void Destroy()
            {
                base.Destroy();
                DestroyProxy();
                if (hingeTarget != null) UnityEngine.Object.Destroy(hingeTarget.gameObject);
                if (bones[0] != null) bones[0].localScale = rootScale;
            }
        }
    }
}
