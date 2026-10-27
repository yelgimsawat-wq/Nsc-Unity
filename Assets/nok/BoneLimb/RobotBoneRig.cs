using System.Collections.Generic;
using Nsc.Robots;
using UnityEngine;

namespace Nsc.Limbs.Bones
{
    /// <summary>
    /// แขนขาแบบ "กระดูก" — ท่อนแขน/ขาที่เห็นเป็นกระดูกแข็ง ต่อตายตัวกับลำตัว แล้วใช้ IK เอื้อมไปหามือ/เท้า
    /// แทนการโชว์ท่อนฟิสิกส์ที่ต่อกันด้วย joint (ซึ่งแกว่ง/ยืดเหมือนยาง)
    ///
    /// ไม่แตะเกมเพลย์เลย: ฟิสิกส์ทุกชิ้น (มือ เท้า ลำตัว joint collider) ทำงานเหมือนเดิมบน server
    /// คอมโพเนนต์นี้แค่ซ่อน renderer ของท่อนกลาง แล้ววาดสำเนาบนกระดูกแทน — รันเองในทุกเครื่อง ไม่มี RPC
    /// มือ/เท้า (ปลายโซ่) ยังเป็นชิ้นฟิสิกส์ตัวจริง เพราะเป็นตัวที่ชน ต่อย จับของ
    ///
    /// ใส่บน GameObject เดียวกับ Robot (Torso) — เมนู Tools ▸ NSC ▸ Bone Rig ▸ Add To Selected Robot
    /// ชิ้นที่หลุด (LimbAttachment) หรือตอนลำตัวล้ม (ragdoll) จะกลับไปโชว์ท่อนฟิสิกส์เหมือนเดิมอัตโนมัติ
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)] // หลังทุกอย่างขยับ transform เสร็จ
    public class RobotBoneRig : MonoBehaviour
    {
        public enum PoleMode
        {
            /// <summary>ศอก/เข่าชี้ทิศคงที่เทียบลำตัว — นิ่งที่สุด ดูเป็นหุ่น</summary>
            TorsoDirection,
            /// <summary>ศอก/เข่าชี้ตามข้อต่อฟิสิกส์ตัวจริง — ตามท่าฟิสิกส์มากกว่า แต่สั่นตามด้วย</summary>
            PhysicsJoint,
        }

        [Header("ใช้กับชิ้นไหน")]
        [SerializeField] private bool arms = true;
        [SerializeField] private bool legs = true;

        [Header("ทิศศอก / เข่า")]
        [SerializeField] private PoleMode poleMode = PoleMode.TorsoDirection;
        [Tooltip("ทิศศอกในแกนของลำตัว — ใช้เมื่อท่าเริ่มต้นของโมเดลแขนเหยียดตรง (T-pose)")]
        [SerializeField] private Vector3 armPoleDirection = new Vector3(0f, -1f, -1f);
        [Tooltip("ทิศเข่าในแกนของลำตัว — ใช้เมื่อท่าเริ่มต้นของโมเดลขาเหยียดตรง")]
        [SerializeField] private Vector3 legPoleDirection = new Vector3(0f, 0f, 1f);

        [Header("Behaviour")]
        [Tooltip("ตอนลำตัวล้ม (ragdoll) กลับไปโชว์ท่อนฟิสิกส์ ให้ดูล้มแบบตุ๊กตาเหมือนเดิม")]
        [SerializeField] private bool physicsLookWhenRagdoll = true;
        [Range(1, 20)] [SerializeField] private int iterations = 10;

        [Header("Debug")]
        [Tooltip("โชว์ท่อนฟิสิกส์ซ้อนไว้ด้วย (ดูว่ากระดูกตามฟิสิกส์ถูกไหม)")]
        [SerializeField] private bool showPhysicsParts;
        [SerializeField] private bool drawGizmos = true;

        private Robot robot;
        private Transform torso;
        private readonly List<LimbRig> rigs = new List<LimbRig>();

        public int RigCount => rigs.Count;

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
            foreach (LimbSlot slot in LimbSlots.All)
            {
                if (slot.IsArm() ? !arms : !legs) continue;
                RobotLimb limb = robot.GetLimb(slot);
                if (limb == null) continue;

                LimbRig rig = LimbRig.TryBuild(limb, robot.Torso.Body, slot.IsArm() ? armPoleDirection : legPoleDirection);
                if (rig != null) rigs.Add(rig);
                else Debug.LogWarning($"[RobotBoneRig] {slot}: หาโซ่ joint จากปลายไปถึงลำตัวไม่เจอ ข้ามชิ้นนี้", limb);
            }
        }

        private void LateUpdate()
        {
            bool ragdoll = physicsLookWhenRagdoll && robot.Torso.IsRagdoll;
            foreach (LimbRig rig in rigs)
            {
                bool useBones = !ragdoll && rig.Limb != null && rig.Limb.IsAttached;
                rig.SetBonesVisible(useBones, showPhysicsParts);
                if (useBones) rig.Solve(torso, poleMode, iterations);
            }
        }

        private void OnDisable()
        {
            foreach (LimbRig rig in rigs) rig.SetBonesVisible(false, true);
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

        // ================================================================
        //  หนึ่งชิ้น (แขนหรือขา)
        // ================================================================

        private class LimbRig
        {
            public RobotLimb Limb;

            private Transform tip;               // มือ/เท้าฟิสิกส์ (ปลายโซ่)
            private Vector3 tipAnchor;           // จุดข้อมือ/ข้อเท้า ในแกนของ tip
            private Transform[] segments;        // ท่อนฟิสิกส์จากโคนไปปลาย (ไม่รวม tip)
            private Vector3[] segmentAnchors;    // จุดข้อต่อของแต่ละท่อน ในแกนของท่อนนั้น

            private Transform rigRoot;
            private Transform[] bones;           // กระดูกต่อท่อน
            private Quaternion[] bindRotation;   // การหมุนตอน bind เทียบลำตัว
            private Vector3[] bindDirection;     // ทิศท่อนตอน bind ในแกนกระดูก
            private Vector3[] originOffset;      // จุด origin ของท่อน เทียบข้อต่อโคนท่อน (แกนกระดูก)
            private Vector3[] bindPoints;        // ข้อต่อตอน bind ในแกนลำตัว (ใช้เป็นท่าเริ่ม solver)
            private float[] lengths;
            private Vector3 poleLocal;           // ทิศศอก/เข่าในแกนลำตัว
            private float totalLength;

            private Vector3[] points;
            private readonly List<Renderer> physicsRenderers = new List<Renderer>();
            private bool bonesVisible;
            private bool physicsHidden;

            public static LimbRig TryBuild(RobotLimb limb, Rigidbody torsoBody, Vector3 fallbackPole)
            {
                Rigidbody tipBody = limb.GetComponent<Rigidbody>();
                if (tipBody == null) return null;

                // เดิน joint จากมือ/เท้าขึ้นไปจนเจอลำตัว: [tip, ..., ท่อนบนสุด]
                var chain = new List<Rigidbody>();
                var joints = new List<Joint>();
                Rigidbody current = tipBody;
                for (int safety = 0; safety < 8 && current != null && current != torsoBody; safety++)
                {
                    Joint link = FindLink(current);
                    if (link == null) return null;
                    chain.Add(current);
                    joints.Add(link);
                    current = link.connectedBody;
                }
                if (current != torsoBody || chain.Count < 2) return null;

                chain.Reverse();
                joints.Reverse();

                var rig = new LimbRig { Limb = limb, tip = tipBody.transform, tipAnchor = joints[joints.Count - 1].anchor };
                int boneCount = chain.Count - 1;
                rig.segments = new Transform[boneCount];
                rig.segmentAnchors = new Vector3[boneCount];
                for (int i = 0; i < boneCount; i++)
                {
                    rig.segments[i] = chain[i].transform;
                    rig.segmentAnchors[i] = joints[i].anchor;
                }

                rig.Bind(torsoBody.transform, fallbackPole);
                return rig;
            }

            private static Joint FindLink(Rigidbody body)
            {
                foreach (Joint joint in body.GetComponents<Joint>())
                    if (joint != null && joint.connectedBody != null) return joint;
                return null;
            }

            private Vector3 PhysicsPoint(int index) =>
                index < segments.Length
                    ? segments[index].TransformPoint(segmentAnchors[index])
                    : tip.TransformPoint(tipAnchor);

            private void Bind(Transform torso, Vector3 fallbackPole)
            {
                int boneCount = segments.Length;
                points = new Vector3[boneCount + 1];
                bindPoints = new Vector3[boneCount + 1];
                for (int i = 0; i <= boneCount; i++)
                {
                    points[i] = PhysicsPoint(i);
                    bindPoints[i] = torso.InverseTransformPoint(points[i]);
                }

                // กระดูกเป็นพี่น้องกับท่อนฟิสิกส์ (parent เดียวกัน) → scale ไม่เพี้ยน; ตำแหน่งตั้งเป็น world ทุกเฟรม
                rigRoot = new GameObject($"BoneRig_{Limb.Slot}").transform;
                rigRoot.SetParent(segments[0].parent, false);

                bones = new Transform[boneCount];
                bindRotation = new Quaternion[boneCount];
                bindDirection = new Vector3[boneCount];
                originOffset = new Vector3[boneCount];
                lengths = new float[boneCount];
                totalLength = 0f;

                for (int i = 0; i < boneCount; i++)
                {
                    Transform segment = segments[i];
                    Vector3 along = points[i + 1] - points[i];
                    lengths[i] = along.magnitude;
                    totalLength += lengths[i];

                    var bone = new GameObject($"Bone_{segment.name}").transform;
                    bone.SetParent(rigRoot, false);
                    bone.localScale = segment.localScale;
                    bone.SetPositionAndRotation(segment.position, segment.rotation);
                    bones[i] = bone;
                    originOffset[i] = Quaternion.Inverse(segment.rotation) * (segment.position - points[i]);

                    bindRotation[i] = Quaternion.Inverse(torso.rotation) * segment.rotation;
                    bindDirection[i] = lengths[i] > 1e-6f
                        ? Quaternion.Inverse(segment.rotation) * (along / lengths[i])
                        : Vector3.up;

                    CloneRenderers(segment, bone);
                }

                // ทิศศอก/เข่า: ใช้ท่าที่โมเดลงออยู่แล้ว ถ้าเหยียดตรงใช้ค่าที่ตั้งไว้
                Vector3 line = points[boneCount] - points[0];
                Vector3 bend = Vector3.ProjectOnPlane(points[Mathf.Max(1, boneCount / 2)] - points[0], line);
                poleLocal = bend.magnitude > totalLength * 0.02f
                    ? torso.InverseTransformDirection(bend.normalized)
                    : fallbackPole.normalized;

                rigRoot.gameObject.SetActive(false);
                bonesVisible = false;
            }

            /// <summary>
            /// สำเนา MeshRenderer ที่มองเห็นอยู่ของท่อนนี้ไปไว้บนกระดูก (ไม่รวมชิ้นที่มี Rigidbody ของตัวเอง)
            /// สร้างลำดับ child ซ้ำแบบเดียวกับต้นฉบับ (local position/rotation/scale เดิม) → หน้าตาตรงเป๊ะแม้ scale ไม่เท่ากันทุกแกน
            /// </summary>
            private void CloneRenderers(Transform segment, Transform bone)
            {
                Rigidbody owner = segment.GetComponent<Rigidbody>();
                var mirrors = new Dictionary<Transform, Transform> { { segment, bone } };

                foreach (MeshRenderer source in segment.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (source.GetComponentInParent<Rigidbody>() != owner) continue;
                    if (!source.enabled || !source.gameObject.activeInHierarchy || source.forceRenderingOff) continue;
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

            public void SetBonesVisible(bool visible, bool keepPhysicsVisible)
            {
                if (rigRoot != null && visible != bonesVisible)
                {
                    rigRoot.gameObject.SetActive(visible);
                    bonesVisible = visible;
                }

                bool hidePhysics = visible && !keepPhysicsVisible;
                if (hidePhysics == physicsHidden) return;
                foreach (Renderer r in physicsRenderers)
                    if (r != null) r.forceRenderingOff = hidePhysics;
                physicsHidden = hidePhysics;
            }

            public void Solve(Transform torso, PoleMode poleMode, int iterations)
            {
                if (tip == null) return;

                int n = points.Length;
                for (int i = 0; i < n; i++) points[i] = torso.TransformPoint(bindPoints[i]);

                Vector3 root = points[0];
                Vector3 goal = tip.TransformPoint(tipAnchor);
                Vector3 pole = poleMode == PoleMode.PhysicsJoint && n > 2
                    ? PhysicsPoint(n / 2)
                    : (root + goal) * 0.5f + torso.TransformDirection(poleLocal) * totalLength;

                BoneChainSolver.Solve(points, lengths, root, goal, true, pole, iterations);

                for (int i = 0; i < bones.Length; i++)
                {
                    Quaternion rest = torso.rotation * bindRotation[i];
                    Vector3 wanted = points[i + 1] - points[i];
                    Quaternion rotation = wanted.sqrMagnitude > 1e-10f
                        ? Quaternion.FromToRotation(rest * bindDirection[i], wanted) * rest
                        : rest;
                    bones[i].SetPositionAndRotation(points[i] + rotation * originOffset[i], rotation);
                }
            }

            public void Destroy()
            {
                SetBonesVisible(false, true);
                if (rigRoot != null) Object.Destroy(rigRoot.gameObject);
            }

            public void DrawGizmos()
            {
                if (!bonesVisible || points == null) return;
                Gizmos.color = Color.cyan;
                for (int i = 0; i < points.Length - 1; i++) Gizmos.DrawLine(points[i], points[i + 1]);
                Gizmos.color = Color.yellow;
                if (tip != null) Gizmos.DrawWireSphere(tip.TransformPoint(tipAnchor), totalLength * 0.05f);
            }
        }
    }
}
