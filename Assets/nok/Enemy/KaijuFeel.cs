using System;
using Nsc.Feel;
using Unity.Netcode;
using UnityEngine;

namespace NscGame.Enemy
{
    /// <summary>
    /// ฟีลไคจู (Q-20261028-kaiju-enemy-feel) วางคู่ EnemyController/EnemyHealth บนตัวราก
    /// - ก้าว: เท้าแตะพื้น → จอสั่น + ฝุ่น ตามค่า FeelConfig (ไม่มีเสียง — เสียงเท้าใน FeelConfig เป็นของหุ่นยนต์)
    /// - ง้างช้า ตีเร็ว: ท่าโจมตีเล่นช้าจนถึงจังหวะปล่อย แล้วเร็วขึ้น ระหว่างง้างตัวเรืองแดง + พื้นสั่นเบาๆ
    ///   (ใช้พารามิเตอร์ Animator "AttackSpeed" ที่ state โจมตีอ่านเป็นตัวคูณความเร็ว)
    /// - ทุบพื้น: ท่าที่ tag RootMotion (Jump Attack) ตอนลงพื้น → สั่นแรง + ซูมแวบ
    /// - โดนตี: ท่าเสียหลักถอยหนึ่งก้าว เล่นแค่ขา (Hit Layer) ตัวถอยตามท่าผ่าน KaijuRootMotion
    /// ภาพ/เสียงทำทุกเครื่องจากสถานะ Animator ที่ซิงก์อยู่แล้ว ส่วนเซถอยทำบน Server
    /// ⚠️ จังหวะดาเมจอยู่ที่ EnemyCombat (lightPunchHitDelay/kickHitDelay) ต้องตั้งให้ตรงกับจังหวะปล่อยของที่นี่
    /// </summary>
    [DefaultExecutionOrder(10)] // Update ต้องหลัง EnemyController ที่เขียน "Speed" ทุกเฟรม
    [RequireComponent(typeof(Animator))]
    public class KaijuFeel : NetworkBehaviour
    {
        [Serializable]
        public class Windup
        {
            public string state;           // ชื่อ state ใน Attack Layer
            public float strikeTime;       // วินาทีในคลิปที่หมัด/ตัวปล่อย
            public float windupSpeed = 0.4f;
            public float strikeSpeed = 1.1f;
            [Tooltip("VFX ตอนปล่อย — ต่อยเกิดที่มือที่ยื่นไปหน้าสุด, ท่าอื่นเกิดที่เท้า")]
            public GameObject strikeVfx;
            public bool strikeAtHand = true;
            public float vfxScale = 4f;
        }

        [Header("ก้าว")]
        [Tooltip("เท้าต่ำกว่าเท่านี้เหนือตัวราก = แตะพื้น (หน่วยโลก)")]
        [SerializeField] private float footContactHeight = 1.2f;
        [Tooltip("ความเร็วเท้าลงต่ำสุดที่นับเป็นก้าว (หน่วย/วิ)")]
        [SerializeField] private float minStepSpeed = 3f;
        [SerializeField] private float stepSpeedForFullShake = 15f;
        [Tooltip("ฝุ่นตอนก้าว — ว่าง = ใช้ฝุ่นใน FeelConfig (ของหุ่น)")]
        [SerializeField] private GameObject footstepDust;
        [SerializeField] private float footstepDustScale = 1.5f;
        [Tooltip("จอสั่นตอนก้าว = ค่าก้าวหุ่นใน FeelConfig คูณเท่านี้ (ตัวใหญ่ก้าวหนักกว่าหุ่น)")]
        [SerializeField] private float footstepShakeScale = 2f;

        [Header("ท่าเดินตามความเร็วจริง")]
        [Tooltip("ความเร็วพื้น (หน่วย/วิ) ที่ท่าเดินเล่นเต็ม = walkSpeed ของ EnemyController | ช้ากว่านี้ผสมท่ายืน, ยืนนิ่ง = ท่ายืน")]
        [SerializeField] private float fullWalkSpeed = 6.8f;
        [SerializeField] private string locomotionParameter = "Speed";

        [Header("ตัวย้วยแบบแรคดอล (ส่งแรงเข้าสปริงของ EnemyBodySway)")]
        [Tooltip("แรงเหวี่ยงตัวไปทางเท้าที่เพิ่งลง ทุกก้าว")]
        [SerializeField] private float stepSway = 8f;
        [Tooltip("แรงเฉื่อย: ออกตัวแล้วตัวบนค้างไปหลัง, เบรกแล้วหัวทิ่มไปหน้า, เลี้ยวแล้วเอียงออกนอกโค้ง")]
        [SerializeField] private float inertiaSway = 2.4f;
        [Tooltip("ตัดความเร่งที่เกินนี้ทิ้ง (วาร์ป/ตำแหน่งกระตุกจากเน็ต)")]
        [SerializeField] private float maxInertiaAccel = 40f;

        [Header("ง้างช้า ตีเร็ว")]
        [SerializeField] private int attackLayer = 2;
        [SerializeField] private string speedParameter = "AttackSpeed";
        [Tooltip("ท่าโจมตีทุกท่าเล่นช้าลงเท่านี้ (ดูหนัก) — เปลี่ยนแล้วต้องปรับ hit delay / barrageDuration ใน EnemyCombat = ค่าเดิม ÷ ค่านี้")]
        [Range(0.3f, 1f)] [SerializeField] private float attackPace = 0.7f;
        [SerializeField] private Windup[] windups =
        {
            new Windup { state = "Attack_Light", strikeTime = 0.28f, windupSpeed = 0.35f, strikeSpeed = 1.1f },
            new Windup { state = "Attack_Kick", strikeTime = 0.57f, windupSpeed = 0.6f, strikeSpeed = 1f },
        };
        [SerializeField] private Color telegraphColor = new Color(1f, 0.25f, 0.15f);
        [Range(0f, 1f)] [SerializeField] private float telegraphTint = 0.55f;
        [SerializeField] private float telegraphRumble = 0.15f;

        [Header("ทุบพื้น (state tag RootMotion)")]
        [SerializeField] private string slamTag = "RootMotion";
        [Tooltip("วินาทีในคลิปที่ลงพื้น")]
        [SerializeField] private float slamTime = 1.53f;
        [SerializeField] private float slamShake = 1.5f;
        [SerializeField] private float slamShakeDuration = 1.1f;
        [SerializeField] private float slamFovKick = 10f;
        [Tooltip("กล้องค้างแวบหนึ่งตอนกระแทก (hit-stop ฝั่งภาพ) แล้วค่อยสั่น")]
        [SerializeField] private float slamFreeze = 0.08f;
        [Tooltip("VFX ตอนลงพื้น (เกิดกลางเท้าสองข้างที่ระดับพื้น)")]
        [SerializeField] private GameObject[] slamVfx;
        [SerializeField] private float slamVfxScale = 6f;

        [Header("วงเตือนกระโดดทุบ: วงแดงตรงจุดจะลง วงในขยายจนเต็มตอนลงพื้น | ยืนในวงตอนลง = หุ่นล้ม")]
        [SerializeField] private Material slamWarningMaterial;
        [Tooltip("รัศมีวง = รัศมีที่โดน — ตั้งเท่า kickRadius ของ EnemyCombat")]
        [SerializeField] private float slamRadius = 14f;
        [Tooltip("ระยะที่ท่ากระโดดพาตัวไปเมื่อไม่มีอะไรขวาง (วัดจาก Play) ใช้เดาจุดลง")]
        [SerializeField] private float slamLunge = 17f;
        [SerializeField] private Color slamWarningColor = new Color(1f, 0.1f, 0.05f, 0.45f);

        [Header("ต่อยรัว: VFX ทุกจังหวะที่มือเหวี่ยงเร็วสุด")]
        [SerializeField] private string barrageState = "Attack_Barrage";
        [SerializeField] private GameObject barrageVfx;
        [SerializeField] private float barrageVfxScale = 3f;
        [Tooltip("ความเร็วมือ (เทียบตัว) ต่ำสุดที่นับเป็นหมัด หน่วย/วิ")]
        [SerializeField] private float barrageMinHandSpeed = 12f;

        [Header("โดนตี: ท่าเสียหลักถอย (layer ที่ mask แค่ขา + Root, state tag RootMotion → KaijuRootMotion พาตัวถอยตามท่า)")]
        [SerializeField] private string hitLayer = "Hit Layer";
        [SerializeField] private string hitState = "HitReact";
        [Tooltip("โดนซ้ำระหว่างท่ายังไม่ถึงเท่านี้ (0-1) ไม่เริ่มท่าใหม่ — กันขากระตุกตอนโดนรัวๆ")]
        [Range(0f, 1f)] [SerializeField] private float hitRestartAfter = 0.35f;
        [Tooltip("ท่าโดนตีจบแล้วยืนตั้งหลักอีกเท่านี้ (วิ) ก่อน AI กลับมาเดิน/ตี")]
        [SerializeField] private float hitRecoverTime = 0.6f;

        private Animator animator;
        private EnemyHealth health;
        private EnemyController controller;
        private UnityEngine.AI.NavMeshAgent agent;
        private Coroutine recovering;
        private EnemyBodySway sway;
        private Vector3 prevPos, prevVel;
        private float groundSpeed;
        private int locomotionHash;
        private Transform leftFoot, rightFoot, leftToes, rightToes;
        private float leftPrevY, rightPrevY, leftFall, rightFall;
        private bool leftDown = true, rightDown = true;
        private int speedHash, slamTagHash, lastAttackState;
        private bool slammed, struck;
        private Transform[] hands;
        private Transform warnOuter, warnInner;
        private Vector3 slamPoint;
        private KaijuRootMotion rootMotion;
        private Vector3[] handPrevLocal;
        private float[] handPrevSpeed;
        private bool[] handRising;
        private float tint;
        private Renderer[] renderers;
        private Color[][] baseColors;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            animator = GetComponent<Animator>();
            health = GetComponent<EnemyHealth>();
            controller = GetComponent<EnemyController>();
            agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            sway = GetComponent<EnemyBodySway>();
            prevPos = transform.position;
            leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            rootMotion = GetComponent<KaijuRootMotion>();
            hands = new[] { animator.GetBoneTransform(HumanBodyBones.LeftHand), animator.GetBoneTransform(HumanBodyBones.RightHand) };
            handPrevLocal = new Vector3[2];
            handPrevSpeed = new float[2];
            handRising = new bool[2];
            speedHash = Animator.StringToHash(speedParameter);
            locomotionHash = Animator.StringToHash(locomotionParameter);
            slamTagHash = Animator.StringToHash(slamTag);

            renderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            baseColors = new Color[renderers.Length][];
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] mats = renderers[r].sharedMaterials;
                baseColors[r] = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                    baseColors[r][m] = mats[m] && mats[m].HasProperty(BaseColor) ? mats[m].GetColor(BaseColor) : Color.white;
            }
            block = new MaterialPropertyBlock();
        }

        public override void OnNetworkSpawn()
        {
            if (health != null) health.CurrentHp.OnValueChanged += OnHpChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (health != null) health.CurrentHp.OnValueChanged -= OnHpChanged;
        }

        // EnemyController ตั้ง "Speed" ตามสถานะ AI (เดิน = 0.5 แม้ agent ติดอยู่กับที่) → ทับด้วยความเร็วที่ตัวเคลื่อนจริง
        private void Update()
        {
            // ถอยหลัง (ทิศเคลื่อนสวนหน้า) = ค่าติดลบ → ท่าเดินถอยใน blend tree
            float sign = Vector3.Dot(prevVel, transform.forward) < -0.3f * prevVel.magnitude ? -1f : 1f;
            if (animator.enabled) animator.SetFloat(locomotionHash, sign * 0.5f * Mathf.Clamp01(groundSpeed / fullWalkSpeed));
        }

        private void LateUpdate()
        {
            if (!animator.enabled) { SetTint(0f); EndSlamWarning(); return; } // ตายแล้ว (ragdoll)

            Footstep(leftFoot, leftToes, ref leftPrevY, ref leftFall, ref leftDown);
            Footstep(rightFoot, rightToes, ref rightPrevY, ref rightFall, ref rightDown);
            Inertia();
            WindupAndSlam();
        }

        // ความเร่งของตัว (วัดจากตำแหน่ง ใช้ได้ทุกเครื่อง) → ตัวบนเหวี่ยงสวนทาง
        private void Inertia()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 vel = (transform.position - prevPos) / dt;
            vel.y = 0f;
            prevPos = transform.position;
            groundSpeed = Mathf.Lerp(groundSpeed, vel.magnitude, 1f - Mathf.Exp(-8f * dt));
            Vector3 accel = Vector3.ClampMagnitude((vel - prevVel) / dt, maxInertiaAccel);
            prevVel = vel;
            if (sway != null && accel.sqrMagnitude > 0.01f) sway.ApplyHitImpulse(-accel, accel.magnitude * inertiaSway * dt);
        }

        // ================================================================ ก้าว

        private void Footstep(Transform foot, Transform toes, ref float prevY, ref float fall, ref bool down)
        {
            if (!foot) return;
            float height = foot.position.y - transform.position.y;
            float speed = (prevY - foot.position.y) / Mathf.Max(Time.deltaTime, 1e-4f);
            prevY = foot.position.y;

            if (height > footContactHeight * 1.5f) down = false; // ยกเท้าพ้นพื้นแล้ว
            if (down) return;
            fall = Mathf.Max(fall, speed); // ความเร็วลงสูงสุดของก้าวนี้ = ก้าวหนักแค่ไหน
            // เท้าแตะพื้นจริง = ต่ำพอและหยุดลงแล้ว (ถ้านับตอนกำลังลง ฝุ่นจะขึ้นก่อนเท้าถึงพื้น)
            if (height > footContactHeight || speed > minStepSpeed * 0.3f || fall < minStepSpeed) return;

            down = true;
            float strength = Mathf.Clamp01(fall / stepSpeedForFullShake);
            fall = 0f;
            // น้ำหนักลงเท้านี้ → ตัวเหวี่ยงไปข้างเท้านั้นและยุบไปหน้านิดๆ
            float side = Mathf.Sign(Vector3.Dot(foot.position - transform.position, transform.right));
            if (sway != null) sway.ApplyHitImpulse(transform.right * side + transform.forward * 0.4f, stepSway * Mathf.Lerp(0.5f, 1f, strength));
            Vector3 sole = toes ? (foot.position + toes.position) * 0.5f : foot.position; // กลางฝ่าเท้า
            Stomp(new Vector3(sole.x, transform.position.y, sole.z), strength);
        }

        // จอสั่น + ฝุ่นตามค่าใน FeelConfig แต่ไม่เล่นเสียง (เสียงเท้าใน FeelConfig เป็นเสียงหุ่นยนต์)
        private void Stomp(Vector3 position, float strength)
        {
            FeelConfig config = FeelConfig.Instance;
            if (config == null) return;
            CameraShake.Add(position, config.footstepShake * footstepShakeScale * strength, config.footstepShakeDuration);
            GameObject prefab = footstepDust ? footstepDust : config.dustPrefab;
            if (prefab == null) return;
            float scale = footstepDust ? footstepDustScale : config.dustScale;

            // ฝุ่นก้อนเดียวใต้เท้าที่เหยียบ
            GameObject dust = Instantiate(prefab, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            dust.transform.localScale = Vector3.one * scale * Mathf.Lerp(0.7f, 1f, strength);
            float life = 0f;
            foreach (ParticleSystem ps in dust.GetComponentsInChildren<ParticleSystem>())
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = false;
                life = Mathf.Max(life, main.duration + main.startLifetime.constantMax);
                ps.Play();
            }
            Destroy(dust, Mathf.Max(1f, life));
        }

        // ================================================================ ง้าง / ทุบพื้น

        private void WindupAndSlam()
        {
            // กำลังเปลี่ยนเข้าท่าโจมตี = ท่านั้นเริ่มเล่นแล้ว ต้องนับเป็นท่าโจมตี ไม่งั้นช่วงง้างหลุดไปกับ transition
            bool entering = animator.IsInTransition(attackLayer);
            AnimatorStateInfo state = entering ? animator.GetNextAnimatorStateInfo(attackLayer) : animator.GetCurrentAnimatorStateInfo(attackLayer);
            AnimatorClipInfo[] clips = entering ? animator.GetNextAnimatorClipInfo(attackLayer) : animator.GetCurrentAnimatorClipInfo(attackLayer);
            float clipTime = clips.Length > 0 ? state.normalizedTime * clips[0].clip.length : 0f;

            if (state.fullPathHash != lastAttackState)
            {
                lastAttackState = state.fullPathHash; slammed = false; struck = false;
                // เริ่มวัดความเร็วมือใหม่ ไม่งั้นเฟรมแรกของท่าต่อยรัวนับเป็นหมัดหลอก
                for (int i = 0; i < hands.Length; i++)
                    if (hands[i]) { handPrevLocal[i] = transform.InverseTransformPoint(hands[i].position); handPrevSpeed[i] = 0f; handRising[i] = false; }
                EndSlamWarning();
                if (state.tagHash == slamTagHash) BeginSlamWarning();
            }

            float speed = attackPace, targetTint = 0f;
            bool aiming = false;
            foreach (Windup w in windups)
            {
                if (!state.IsName(w.state)) continue;
                bool winding = clipTime < w.strikeTime;
                speed = (winding ? w.windupSpeed : w.strikeSpeed) * attackPace;
                if (winding)
                {
                    aiming = true;
                    targetTint = telegraphTint;
                    CameraShake.Add(transform.position, telegraphRumble * Time.deltaTime * 10f, 0.15f);
                }
                else if (!struck && clips.Length > 0)
                {
                    struck = true;
                    if (w.strikeAtHand) SpawnVfx(w.strikeVfx, ForwardHand().position, Quaternion.LookRotation(transform.forward), w.vfxScale);
                    else SpawnVfx(w.strikeVfx, FeetOnGround(), Quaternion.identity, w.vfxScale);
                }
            }
            // ระหว่าง blend เข้าท่ามือวิ่งเร็วแต่ไม่ใช่หมัด → วัดต่อแต่ยังไม่นับ
            if (state.IsName(barrageState)) BarragePunches(!entering && clipTime > 0.15f);
            animator.SetFloat(speedHash, speed);
            SetTint(Mathf.MoveTowards(tint, targetTint, Time.deltaTime * 4f));
            if (warnOuter) UpdateSlamWarning(clipTime, aiming);

            if (!slammed && state.tagHash == slamTagHash && clipTime >= slamTime)
            {
                slammed = true;
                Vector3 at = FeetOnGround();
                EndSlamWarning();
                if (IsServer && Nsc.Match.GameplayGate.CanDamage)
                    foreach (Nsc.Robots.Robot robot in Nsc.Robots.Robot.All)
                        if (robot != null && FlatDistance(robot.transform.position, at) <= slamRadius)
                            robot.ServerKnockDown(Nsc.Robots.FallReason.KnockedDown);
                Stomp(at, 1f);
                if (slamVfx != null) foreach (GameObject v in slamVfx) SpawnVfx(v, at, Quaternion.identity, slamVfxScale);
                CameraShake.Freeze(slamFreeze);
                CameraShake.Add(at, slamShake, slamShakeDuration);
                CameraShake.KickFov(slamFovKick);
            }
        }

        // ต่อยรัว: ความเร็วมือเทียบตัว ขึ้นถึงยอดแล้วเริ่มลด = จังหวะหมัดเข้า → VFX ที่มือนั้น
        private void BarragePunches(bool count)
        {
            for (int i = 0; i < hands.Length; i++)
            {
                if (!hands[i]) continue;
                Vector3 local = transform.InverseTransformPoint(hands[i].position);
                float speed = Vector3.Scale(local - handPrevLocal[i], transform.lossyScale).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                handPrevLocal[i] = local;
                bool rising = speed > handPrevSpeed[i];
                if (count && handRising[i] && !rising && handPrevSpeed[i] >= barrageMinHandSpeed)
                    SpawnVfx(barrageVfx, hands[i].position, Quaternion.LookRotation(transform.forward), barrageVfxScale);
                handRising[i] = rising;
                handPrevSpeed[i] = speed;
            }
        }

        private Transform ForwardHand()
        {
            Transform best = hands[1] ? hands[1] : hands[0];
            foreach (Transform h in hands)
                if (h && Vector3.Dot(h.position - transform.position, transform.forward) > Vector3.Dot(best.position - transform.position, transform.forward)) best = h;
            return best;
        }

        // ================================================================ วงเตือนกระโดดทุบ (ภาพ ทุกเครื่อง)

        private void BeginSlamWarning()
        {
            if (slamWarningMaterial == null) return;
            slamPoint = PredictLanding();
            warnOuter = MakeDisc(0.5f);
            warnInner = MakeDisc(1f);
        }

        private Transform MakeDisc(float alphaScale)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "SlamWarning";
            Destroy(disc.GetComponent<Collider>());
            var r = disc.GetComponent<MeshRenderer>();
            r.sharedMaterial = slamWarningMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            Color c = slamWarningColor; c.a *= alphaScale;
            mpb.SetColor(BaseColor, c);
            r.SetPropertyBlock(mpb);
            return disc.transform;
        }

        private void UpdateSlamWarning(float clipTime, bool aiming)
        {
            if (aiming) slamPoint = PredictLanding(); // ตามเป้าจนกว่าจะกระโดด แล้วล็อกจุด
            float fill = Mathf.Clamp01(clipTime / slamTime);
            float size = slamRadius * 2f;
            warnOuter.SetPositionAndRotation(slamPoint + Vector3.up * 0.05f, Quaternion.identity);
            warnInner.SetPositionAndRotation(slamPoint + Vector3.up * 0.08f, Quaternion.identity);
            warnOuter.localScale = new Vector3(size, 0.01f, size);
            warnInner.localScale = new Vector3(size * fill, 0.01f, size * fill);
        }

        private void EndSlamWarning()
        {
            if (warnOuter) Destroy(warnOuter.gameObject);
            if (warnInner) Destroy(warnInner.gameObject);
            warnOuter = warnInner = null;
        }

        // จุดลง = พุ่งไปหน้าเต็มระยะท่า แต่ไม่เกินจุดที่ KaijuRootMotion หยุดหน้าหุ่น
        private Vector3 PredictLanding()
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float lunge = slamLunge;
            float stop = rootMotion != null ? rootMotion.StopNearTarget : 0f;
            foreach (Nsc.Robots.Robot robot in Nsc.Robots.Robot.All)
            {
                if (robot == null) continue;
                Vector3 to = robot.transform.position - transform.position; to.y = 0f;
                if (Vector3.Dot(to, forward) > 0f) lunge = Mathf.Min(lunge, Mathf.Max(0f, to.magnitude - stop));
            }
            Vector3 point = transform.position + forward * lunge;
            point.y = GroundY(point);
            return point;
        }

        private float GroundY(Vector3 at)
        {
            float best = float.MaxValue, y = transform.position.y;
            foreach (RaycastHit hit in Physics.RaycastAll(at + Vector3.up * 30f, Vector3.down, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.collider.attachedRigidbody == null && hit.distance < best) { best = hit.distance; y = hit.point.y; }
            return y;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        public override void OnDestroy()
        {
            EndSlamWarning();
            base.OnDestroy();
        }

        private Vector3 FeetOnGround()
        {
            Vector3 mid = (leftFoot && rightFoot) ? (leftFoot.position + rightFoot.position) * 0.5f : transform.position;
            return new Vector3(mid.x, transform.position.y, mid.z);
        }

        private static void SpawnVfx(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
        {
            if (prefab == null) return;
            GameObject vfx = Instantiate(prefab, position, rotation);
            vfx.transform.localScale *= scale;
            float life = 1f;
            foreach (ParticleSystem ps in vfx.GetComponentsInChildren<ParticleSystem>())
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = false;
                life = Mathf.Max(life, main.duration + main.startLifetime.constantMax);
            }
            Destroy(vfx, life + 0.5f);
        }

        private void SetTint(float value)
        {
            if (Mathf.Approximately(value, tint) && value == 0f) return;
            tint = value;
            for (int r = 0; r < renderers.Length; r++)
                for (int m = 0; m < baseColors[r].Length; m++)
                {
                    renderers[r].GetPropertyBlock(block, m);
                    block.SetColor(BaseColor, Color.Lerp(baseColors[r][m], telegraphColor, tint));
                    renderers[r].SetPropertyBlock(block, m);
                }
        }

        // ================================================================ โดนตี

        private void OnHpChanged(float previous, float current)
        {
            float damage = previous - current;
            if (damage <= 0f || current <= 0f) return;
            CameraShake.Add(transform.position, Mathf.Clamp01(damage / 50f) * 0.5f, 0.25f);
            PlayHitReact();
        }

        // ทุกเครื่องเล่นท่าเอง (เลือดซิงก์อยู่แล้ว) ส่วนการถอยจริงเกิดบน Server ผ่าน root motion
        private void PlayHitReact()
        {
            if (!animator.enabled) return;
            int layer = animator.GetLayerIndex(hitLayer);
            if (layer < 0) return;
            // กลางท่ากระโดดทุบห้ามแทรกท่าขา (ขาจะก้าวกลางอากาศ)
            AnimatorStateInfo attack = animator.IsInTransition(attackLayer) ? animator.GetNextAnimatorStateInfo(attackLayer) : animator.GetCurrentAnimatorStateInfo(attackLayer);
            if (attack.tagHash == slamTagHash) return;
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layer);
            if (current.IsName(hitState) && current.normalizedTime < hitRestartAfter) return;
            animator.CrossFadeInFixedTime(hitState, 0.12f, layer, 0f);

            if (IsServer && controller != null)
            {
                if (recovering != null) StopCoroutine(recovering);
                recovering = StartCoroutine(Recover(layer));
            }
        }

        // [Server] ระหว่างท่าโดนตี + ตั้งหลัก: AI หยุดคิด (ใช้สถานะ knockback เดิมของ EnemyController) และ agent หยุดเดิน
        // ตัวยังถอยตามท่าได้ เพราะ KaijuRootMotion ใช้ agent.Move ซึ่งไม่สน isStopped
        private System.Collections.IEnumerator Recover(int layer)
        {
            controller.ServerBeginKnockback();
            if (agent != null && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
            yield return null; // ให้ CrossFade เริ่มก่อนค่อยอ่านความยาวท่า
            AnimatorStateInfo hit = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer) : animator.GetCurrentAnimatorStateInfo(layer);
            yield return new WaitForSeconds(hit.length + hitRecoverTime);
            controller.ServerEndKnockback();
            recovering = null;
        }
    }
}
