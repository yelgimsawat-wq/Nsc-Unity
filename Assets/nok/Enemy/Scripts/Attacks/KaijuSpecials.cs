using System.Collections;
using Nsc.Combat;
using Nsc.Feel;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static NscGame.Enemy.KaijuUtil;

namespace NscGame.Enemy
{
    /// <summary>
    /// คอมโบพิเศษของแพนด้าไคจู (Q-20261028-kaiju-enemy-feel) — ใช้ตอนหุ่นอยู่ไกลกว่าระยะตีปกติ ผู้เล่นยืนห่างจะไม่ปลอดภัย
    /// 1) กลิ้งพุ่งชน: แนวแดงบนพื้นตามทางที่จะกลิ้ง → ขดตัวกลิ้งเลยตัวหุ่นไป (root motion ผ่าน KaijuRootMotion) ชนหุ่น = ดาเมจ + ล้ม
    /// 2) หันกลับหาหุ่น
    /// 3) ทุบพื้นปล่อยคลื่น: แนวแดงยาวไปหาหุ่น → ยกแขนทุบพื้น → คลื่นวิ่งไปตามแนว โดนหุ่น = ดาเมจ + ล้ม | หลบ = ก้าวออกด้านข้าง
    /// ใกล้เกิน (ต่ำกว่า rollRange.x): สุ่มกลิ้งหนีออกไป → หันกลับ → สุ่มทุบพื้นปล่อยคลื่น หรือกระโดดทับ (วงแดงที่จุดลงตั้งแต่เริ่มท่า)
    /// คลั่ง (เลือด ≤ enrageHpRatio): คำราม → กลิ้งใส่หุ่นรัว ๆ หลายลูก (เล็งใหม่ทุกลูก กลิ้งเสร็จกลิ้งต่อ)
    /// ทุบพื้น 3 ครั้ง: แต่ละครั้งปล่อยวงหนามขยายออกรอบตัวเต็มวง ไม่มีช่องหลบ — ต้องวิ่งออกนอกวงแดง (เตือนนาน + ทุบเต็มท่าทุกครั้ง)
    /// Server ตัดสิน (เลือกท่า, จับเวลา, ดาเมจ) ทุกเครื่องเล่นท่า/แนวเตือน/เอฟเฟกต์เองจาก RPC ครั้งเดียว
    /// ระหว่างท่าใช้สถานะ knockback เดิมของ EnemyController พัก AI + ล็อกการหัน (แนวที่เตือนต้องตรงกับทางจริง)
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class KaijuSpecials : NetworkBehaviour
    {
        private enum Special : byte { Roll, Pound, Roar }

        [Header("ทั่วไป")]
        [SerializeField] private int attackLayer = 2;
        [Tooltip("พักระหว่างท่าพิเศษ (วิ) สุ่มในช่วงนี้")]
        [SerializeField] private Vector2 cooldown = new Vector2(8f, 12f);
        [Tooltip("รอหลังเริ่มเกมก่อนใช้ท่าพิเศษครั้งแรก")]
        [SerializeField] private float firstDelay = 6f;
        [SerializeField] private Material warningMaterial;
        [SerializeField] private Color warningColor = new Color(1f, 0.1f, 0.05f, 0.45f);
        [SerializeField] private float recoverTime = 0.8f;

        [Header("กลิ้งพุ่งชน (state ต้องมี tag RootMotion)")]
        [SerializeField] private string rollState = "Attack_Roll";
        [Tooltip("ระยะหุ่น (แนวราบ) ที่จะเลือกกลิ้ง")]
        [SerializeField] private Vector2 rollRange = new Vector2(10f, 30f);
        [Tooltip("ระยะที่ท่ากลิ้งพาไปจริง = ความยาวแนวเตือน (วัดจาก Play)")]
        [SerializeField] private float rollLength = 25f;
        [SerializeField] private float rollWidth = 8f;
        [Tooltip("ช่วงเวลาในท่า (วิ) ที่ตัวกำลังกลิ้ง = ช่วงที่ชนแล้วโดน")]
        [SerializeField] private Vector2 rollHitWindow = new Vector2(0.7f, 2.7f);
        [SerializeField] private float rollDuration = 3.5f;
        [SerializeField] private float rollHitRadius = 7f;
        [SerializeField] private float rollDamage = 35f;

        [Tooltip("เสียงลมวืดตอนกลิ้ง เล่นซ้ำทุก rollSfxInterval วิ ตลอดช่วงกลิ้ง (ใช้เสียงลมเดียวกับหมัด)")]
        [SerializeField] private AudioClip rollSfx;
        [Tooltip("ข้ามช่วงเงียบต้นไฟล์ (วิ)")]
        [SerializeField] private float rollSfxStart = 0.2f;
        [SerializeField] private float rollSfxInterval = 0.4f;

        [Header("เส้นหมุนแบบการ์ตูนตอนกลิ้ง (โค้งสั้นข้างลูกบอล วิ่งไปทางหมุน แล้วจางหาย มีเส้นใหม่มาแทน)")]
        [Tooltip("ว่าง = Sprites/Default")]
        [SerializeField] private Material spinLineMaterial;
        [SerializeField] private Color spinLineColor = Color.white;
        [Tooltip("ความหนาเส้น = รัศมีโค้ง × ค่านี้ (ตัวใหญ่ขึ้นเส้นหนาตาม)")]
        [SerializeField] private float spinLineWidth = 0.12f;
        [Tooltip("รัศมีโค้ง = ระยะสะโพก→หัว (ตอนขดตัว) × ค่านี้")]
        [SerializeField] private float spinLineRadiusScale = 1.5f;
        [Tooltip("เส้นหนึ่งเกิด→วิ่ง→หายในกี่วินาที (สั้น = ดูหมุนเร็ว)")]
        [SerializeField] private float spinLineLife = 0.3f;

        [Header("ทุบพื้นปล่อยคลื่นแนวตรง (ต่อจากกลิ้ง)")]
        [SerializeField] private string poundState = "Attack_Pound";
        [Tooltip("หลังกลิ้งจบ ใช้เวลาหันกลับหาหุ่นเท่านี้ (วิ) ก่อนทุบ")]
        [SerializeField] private float turnTime = 0.6f;
        [Tooltip("วินาทีในท่าที่มือถึงพื้น = คลื่นออกตัว")]
        [SerializeField] private float poundImpact = 2.56f;
        [SerializeField] private float poundDuration = 5.3f;
        [SerializeField] private float waveLength = 60f;
        [SerializeField] private float waveWidth = 6f;
        [SerializeField] private float waveSpeed = 30f;
        [SerializeField] private float waveDamage = 30f;
        [SerializeField] private GameObject[] waveVfx;
        [SerializeField] private float waveVfxScale = 4f;
        [SerializeField] private float waveVfxSpacing = 4f;
        [Tooltip("หนามดินโผล่ไล่ตามหน้าคลื่นเป็นชุด ๆ (PixPlays EarthBlast)")]
        [SerializeField] private PixPlays.ElementalVFX.BaseVfx earthSpikes;
        [Tooltip("ขนาดหนาม (radius ของ PixPlays) — 16 ≈ ชุดหนึ่งยาว 10 หน่วย สูงราวเข่าแพนด้า")]
        [SerializeField] private float earthSpikesRadius = 16f;
        [SerializeField] private float earthSpikesSpacing = 10f;
        [Tooltip("หนามกี่แถวเรียงขวางคลื่น (แบ่งความกว้าง waveWidth เท่า ๆ กัน) — คลื่นกว้างต้องหลายแถวถึงเต็มแนว")]
        [SerializeField] private int earthSpikesLanes = 2;
        [Tooltip("แต่ละชุดค้างกี่วินาทีก่อนยุบ")]
        [SerializeField] private float earthSpikesLife = 1.5f;
        [Tooltip("เสียงมือทุบถึงพื้น")]
        [SerializeField] private AudioClip poundSfx;
        [Tooltip("เสียงดินแหลมพุ่ง (เล่นครั้งเดียวตอนคลื่นออก ให้เสียงหางยาวตามแนวหนามเอง)")]
        [SerializeField] private AudioClip spikesSfx;

        [Header("ใกล้เกิน: กลิ้งหนีออกไป แล้วสุ่มทุบพื้นระยะไกล หรือกระโดดทับ (ผู้เล่นต้องเดินหลบ)")]
        [Tooltip("ท่าพิเศษพร้อมแล้วหุ่นอยู่ใกล้กว่า rollRange.x → โอกาสกลิ้งหนี (0–1) ไม่ออกก็ลองใหม่ทุก closeRetry วิ")]
        [Range(0f, 1f)] [SerializeField] private float closeChance = 0.35f;
        [SerializeField] private float closeRetry = 2f;
        [Tooltip("หลังกลิ้งหนี โอกาสเลือกกระโดดทับ (ที่เหลือ = ทุบพื้นปล่อยคลื่น)")]
        [Range(0f, 1f)] [SerializeField] private float leapChance = 0.5f;

        [Header("คลั่ง: คำราม แล้วกลิ้งใส่หุ่นรัว ๆ (กลิ้งเสร็จกลิ้งต่อ)")]
        [Tooltip("เลือดเหลือเท่านี้ (สัดส่วน) = คลั่ง — ใช้ทันทีครั้งแรก")]
        [Range(0f, 1f)] [SerializeField] private float enrageHpRatio = 0.5f;
        [Tooltip("หลังคลั่งแล้ว โอกาสใช้ซ้ำทุกครั้งที่ท่าพิเศษพร้อม")]
        [Range(0f, 1f)] [SerializeField] private float rampageChance = 0.3f;
        [SerializeField] private string roarState = "Roar";
        [Tooltip("คำรามนานเท่านี้ (วิ) ก่อนเริ่มกลิ้ง")]
        [SerializeField] private float roarDuration = 3.2f;
        [Tooltip("วินาทีที่อ้าปากคำรามสุด = จอสั่น")]
        [SerializeField] private float roarShakeTime = 0.9f;
        [SerializeField] private AudioClip roarSfx;
        [SerializeField] private int rampageRolls = 3;

        [Header("ทุบพื้น 3 ครั้ง: วงหนามขยายออกเต็มวงทีละครั้ง | หลบ: วิ่งออกนอกวงแดง")]
        [Tooltip("โอกาสเลือกท่านี้ทุกครั้งที่ท่าพิเศษพร้อม (หุ่นต้องอยู่ในรัศมีวง)")]
        [Range(0f, 1f)] [SerializeField] private float ringChance = 0.25f;
        [SerializeField] private int ringCount = 3;
        [Tooltip("เตือนเพิ่ม (วิ) ก่อนเริ่มยกแขนทุบครั้งแรก — วงแดงขึ้นตั้งแต่ตอนนี้")]
        [SerializeField] private float ringWarnTime = 2.5f;
        [Tooltip("ห่างกันกี่วินาทีระหว่างทุบแต่ละครั้ง — ตั้ง ≥ poundImpact แล้วทุบซ้ำทุกครั้งเล่นท่าทุบเต็มท่า (ยกแขนนาน วิ่งออกทัน)")]
        [SerializeField] private float ringSlamGap = 2.8f;
        [Tooltip("วงเริ่มขยายจากรัศมีนี้ (ใต้ตัวแพนด้า — ยืนชิดตัวก็โดน)")]
        [SerializeField] private float ringStartRadius = 4f;
        [SerializeField] private float ringMaxRadius = 50f;
        [SerializeField] private float ringSpeed = 22f;
        [Tooltip("ความหนาหน้าวง — หุ่นที่อยู่ในช่วงนี้ตอนวงวิ่งผ่าน = โดน")]
        [SerializeField] private float ringThickness = 6f;
        [SerializeField] private float ringDamage = 25f;
        [Tooltip("หนามขึ้นเป็นวงทุก ๆ เท่านี้ของรัศมี (ใช้หนามชุดเดียวกับคลื่น earthSpikes ขนาดเท่าเดิม)")]
        [SerializeField] private float ringVfxStep = 12f;
        [Tooltip("ระยะห่างชุดหนามรอบวง (ตามเส้นรอบวง)")]
        [SerializeField] private float ringVfxSpacing = 16f;

        [Header("กระโดดทับ: ท่ากระโดดทุบ (KaijuFeel ทำวงเตือน/ง้างช้า/สั่น) ขยายระยะให้ลงตรงจุดที่หุ่นยืนตอนเริ่ม")]
        [SerializeField] private string leapState = "Attack_Kick";
        [Tooltip("ระยะที่ท่ากระโดดพาไปเองที่ scale 1 (วัดจาก Play) — ใช้คำนวณว่าต้องขยายกี่เท่า")]
        [SerializeField] private float leapClipDistance = 11.8f;
        [SerializeField] private float leapMaxDistance = 45f;
        [Tooltip("รัศมีวงเตือน = รัศมีที่โดน")]
        [SerializeField] private float leapRadius = 18f;
        [SerializeField] private float leapDamage = 35f;

        private Animator animator;
        private NavMeshAgent agent;
        private EnemyController controller;
        private EnemyHealth health;
        private KaijuRootMotion rootMotion;
        private KaijuFeel feel;
        private float nextSpecial;
        private bool running, enraged;
        public bool Running => running;
        private Transform warnOuter, warnInner;
        private Coroutine visual;
        private AudioSource spikesAudio, rollAudio;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            agent = GetComponent<NavMeshAgent>();
            controller = GetComponent<EnemyController>();
            health = GetComponent<EnemyHealth>();
            rootMotion = GetComponent<KaijuRootMotion>();
            feel = GetComponent<KaijuFeel>();
        }

        public override void OnNetworkSpawn() => nextSpecial = Time.time + firstDelay;

        // ================================================================ Server: เลือกท่า

        private void Update()
        {
            if (!IsServer || running || controller == null || !Nsc.Match.GameplayGate.CanAct) return;
            if (controller.IsBusy || controller.CurrentState == EnemyState.Dead || Time.time < nextSpecial) return;
            Transform target = controller.PlayerTarget;
            if (target == null || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            Vector3 to = target.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > rollRange.y || to.sqrMagnitude < 0.01f) return;

            // คลั่งครั้งแรกทันทีที่เลือดถึงเกณฑ์ หลังจากนั้นสุ่มใช้ซ้ำ
            bool rage = health != null && health.GetHpPercent() <= enrageHpRatio;
            float pick = Random.value;
            if (rage && (!enraged || pick < rampageChance))
            {
                Debug.Log($"[KaijuSpecials] คลั่ง! คำราม → กลิ้งรัว {rampageRolls} ลูก (เลือด {health.GetHpPercent():P0})");
                StartCoroutine(Perform(Rampage()));
                return;
            }
            if (dist <= ringMaxRadius && pick < (rage ? rampageChance : 0f) + ringChance)
            {
                Debug.Log($"[KaijuSpecials] ทุบพื้น {ringCount} ครั้ง วงหนามขยาย — หุ่นห่าง {dist:F1}");
                StartCoroutine(Perform(RingSlam()));
                return;
            }
            if (dist >= rollRange.x)
            {
                Debug.Log($"[KaijuSpecials] เริ่มคอมโบ กลิ้ง→ทุบ — หุ่นห่าง {dist:F1}");
                StartCoroutine(Perform(Combo(to.normalized, false)));
                return;
            }
            // ใกล้เกิน: ทอยครั้งเดียวต่อรอบ ไม่ทอยทุกเฟรม
            if (Random.value > closeChance) { nextSpecial = Time.time + closeRetry; return; }
            Debug.Log($"[KaijuSpecials] หุ่นใกล้ {dist:F1} → กลิ้งหนี แล้วตีระยะไกล");
            StartCoroutine(Perform(Combo(-to.normalized, true)));
        }

        // ครอบทุกท่าพิเศษ: AI หยุดคิด (สถานะ knockback เดิมของ EnemyController) + ล็อกการหัน → เล่นท่า → คืน AI + พักท่า
        private IEnumerator Perform(IEnumerator body)
        {
            running = true;
            controller.ServerBeginKnockback();
            controller.ServerRotationLocked = true;  // หันตามเป้าไม่ได้ ทางจริงต้องตรงกับแนวเตือน
            agent.isStopped = true;
            agent.velocity = Vector3.zero;

            yield return body;

            yield return new WaitForSeconds(recoverTime);
            controller.ServerRotationLocked = false;
            controller.ServerEndKnockback();
            nextSpecial = Time.time + Random.Range(cooldown.x, cooldown.y);
            running = false;
        }

        // คำราม (จอสั่น) → กลิ้งใส่หุ่น rampageRolls ลูก เล็งใหม่ทุกลูก ตัดท้ายท่ากลิ้งให้ลูกต่อไปออกทันที
        private IEnumerator Rampage()
        {
            enraged = true;
            StartSpecialClientRpc(Special.Roar, transform.position, transform.forward);
            for (float t = 0f; t < roarDuration && !Dead(); t += Time.deltaTime) yield return null;

            if (rootMotion != null) rootMotion.PassThroughTarget = true;
            for (int i = 0; i < rampageRolls && !Dead(); i++)
            {
                Transform target = controller.PlayerTarget;
                if (target == null) break;
                Vector3 dir = Flat(target.position - transform.position);
                if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
                bool last = i == rampageRolls - 1;
                yield return Run(Special.Roll, dir.normalized, true, last ? -1f : rollHitWindow.y + 0.1f); // +0.1 ให้ภาพกลิ้งลูกก่อนเก็บเส้นหมุนเสร็จก่อน
            }
            if (rootMotion != null) rootMotion.PassThroughTarget = false;
        }

        // ทุบ ringCount ครั้ง ห่างกัน ringSlamGap — ทุกครั้งปล่อยวงหนามขยายจาก ringStartRadius ถึง ringMaxRadius
        private IEnumerator RingSlam()
        {
            Vector3 center = transform.position;
            StartRingsClientRpc(center);
            float start = Time.time;
            for (int k = 0; k < ringCount && !Dead(); k++)
            {
                while (Time.time - start < RingImpact(k) && !Dead()) yield return null;
                if (!Dead()) StartCoroutine(RingDamage(center));
            }
            float end = RingImpact(ringCount - 1) + (ringMaxRadius - ringStartRadius) / ringSpeed;
            while (Time.time - start < end && !Dead()) yield return null;
        }

        private float RingImpact(int k) => ringWarnTime + poundImpact + k * ringSlamGap;

        // หน้าวงวิ่งออก — หุ่นที่อยู่ในความหนาหน้าวงตอนวงผ่าน (และอยู่นอกกลางวงที่โล่ง) โดนครั้งเดียวต่อวง
        private IEnumerator RingDamage(Vector3 center)
        {
            var done = new System.Collections.Generic.HashSet<Robot>();
            for (float r = ringStartRadius; r < ringMaxRadius && !Dead(); r += ringSpeed * Time.deltaTime)
            {
                foreach (Robot robot in Robot.All)
                {
                    if (!Alive(robot) || done.Contains(robot)) continue;
                    Vector3 rel = Flat(robot.transform.position - center);
                    float d = rel.magnitude;
                    if (d >= ringStartRadius && d <= r && d > r - ringThickness)
                    {
                        done.Add(robot);
                        HitRobot(robot, ringDamage, Flat(robot.transform.position - center).normalized);
                    }
                }
                yield return null;
            }
        }

        // คอมโบ: กลิ้ง (เลยตัวหุ่น หรือหนีออกจากหุ่น) → หันกลับ → ทุบพื้นปล่อยคลื่น / กระโดดทับ (เฉพาะหลังกลิ้งหนี)
        private IEnumerator Combo(Vector3 dir, bool away)
        {
            if (rootMotion != null) rootMotion.PassThroughTarget = true; // กลิ้งเลยหุ่น ไม่หยุดหน้าหุ่น
            yield return Run(Special.Roll, dir, !away); // กลิ้งหนีไม่ชน — หุ่นอยู่ข้างหลังตอนออกตัว
            if (rootMotion != null) rootMotion.PassThroughTarget = false;

            Transform target = controller.PlayerTarget;
            if (!Dead() && target != null)
            {
                Vector3 back = Flat(target.position - transform.position);
                if (back.sqrMagnitude < 0.01f) back = -dir;
                Quaternion from = transform.rotation, to = Quaternion.LookRotation(back.normalized);
                for (float t = 0f; t < turnTime && !Dead(); t += Time.deltaTime)
                {
                    transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / turnTime));
                    yield return null;
                }
                if (!Dead())
                {
                    if (away && Random.value < leapChance) yield return Leap();
                    else yield return Run(Special.Pound, back.normalized);
                }
            }
        }

        private IEnumerator Run(Special special, Vector3 dir, bool rollHits = true, float rollEnd = -1f)
        {
            transform.rotation = Quaternion.LookRotation(dir);
            Vector3 origin = transform.position;
            StartSpecialClientRpc(special, origin, dir);

            float start = Time.time;
            bool hit = false;
            if (special == Special.Roll)
            {
                float end = rollEnd > 0f ? rollEnd : rollDuration;
                while (Time.time - start < end && !Dead())
                {
                    float t = Time.time - start;
                    if (rollHits && !hit && t >= rollHitWindow.x && t <= rollHitWindow.y)
                        foreach (Robot robot in Robot.All)
                            if (Alive(robot) && Flat(robot.transform.position - transform.position).magnitude <= rollHitRadius)
                            {
                                hit = true;
                                HitRobot(robot, rollDamage, dir);
                            }
                    yield return null;
                }
            }
            else
            {
                while (Time.time - start < poundImpact && !Dead()) yield return null;
                if (!Dead())
                {
                    CameraShakeClientRpc(origin);
                    // คลื่นวิ่งไปตามแนว โดนหุ่นที่หน้าคลื่นผ่าน (ครั้งเดียวต่อท่า)
                    float travelled = 0f;
                    while (travelled < waveLength && !Dead())
                    {
                        travelled = Mathf.Min(waveLength, travelled + waveSpeed * Time.deltaTime);
                        if (!hit)
                            foreach (Robot robot in Robot.All)
                            {
                                if (!Alive(robot)) continue;
                                Vector3 rel = Flat(robot.transform.position - origin);
                                float along = Vector3.Dot(rel, dir);
                                float side = (rel - dir * along).magnitude;
                                if (along >= 0f && along <= travelled && side <= waveWidth * 0.5f)
                                {
                                    hit = true;
                                    HitRobot(robot, waveDamage, dir);
                                }
                            }
                        yield return null;
                    }
                    foreach (Robot robot in Robot.All)
                        if (Alive(robot))
                        {
                            Vector3 rel = Flat(robot.transform.position - origin);
                            float along = Vector3.Dot(rel, dir);
                            Debug.Log($"[KaijuSpecials] คลื่นจบ โดน={hit} — {robot.name} ตามแนว {along:F1}/{waveLength} ห่างแนว {(rel - dir * along).magnitude:F1}/{waveWidth * 0.5f}");
                        }
                }
                while (Time.time - start < poundDuration && !Dead()) yield return null;
            }
        }

        // กระโดดทับ: ล็อกจุดลง = ที่หุ่นยืนตอนเริ่ม (ไกลสุด leapMaxDistance) ขยาย root motion ให้ไปถึง
        // วงเตือนขึ้นที่จุดนั้นตั้งแต่เริ่มง้าง (ง้างช้าของ KaijuFeel) → ลงพื้นตาม SlamTime ของท่า = ดาเมจในรัศมี
        private IEnumerator Leap()
        {
            Transform target = controller.PlayerTarget;
            if (target == null) yield break;
            Vector3 to = Flat(target.position - transform.position);
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            float dist = Mathf.Min(to.magnitude, leapMaxDistance);
            Vector3 land = transform.position + dir * dist;
            land.y = GroundY(land);
            transform.rotation = Quaternion.LookRotation(dir);

            float baseScale = rootMotion != null ? rootMotion.JumpDistanceScale : 1f;
            if (rootMotion != null)
            {
                rootMotion.PassThroughTarget = true; // ลงทับตัวหุ่น ไม่หยุดหน้าหุ่น
                rootMotion.JumpDistanceScale = dist / Mathf.Max(0.1f, leapClipDistance);
            }
            StartLeapClientRpc(dir, land);
            Debug.Log($"[KaijuSpecials] กระโดดทับ ระยะ {dist:F1} (×{dist / leapClipDistance:F1}) รัศมี {leapRadius}");

            float start = Time.time;
            bool entered = false;
            while (!Dead() && Time.time - start < 8f)
            {
                bool inLeap = LeapClipTime(out float clipTime);
                if (inLeap) entered = true;
                else if (entered) break; // ท่าจบ/โดนตัดก่อนลง
                if (inLeap && feel != null && clipTime >= feel.SlamTime)
                {
                    foreach (Robot robot in Robot.All)
                        if (Alive(robot) && Flat(robot.transform.position - transform.position).magnitude <= leapRadius)
                            HitRobot(robot, leapDamage, dir);
                    break;
                }
                yield return null;
            }
            while (!Dead() && LeapClipTime(out _) && Time.time - start < 8f) yield return null; // รอท่าจบก่อนคืน AI

            if (rootMotion != null)
            {
                rootMotion.JumpDistanceScale = baseScale;
                rootMotion.PassThroughTarget = false;
            }
        }

        private bool LeapClipTime(out float clipTime)
        {
            bool entering = animator.IsInTransition(attackLayer);
            AnimatorStateInfo s = entering ? animator.GetNextAnimatorStateInfo(attackLayer) : animator.GetCurrentAnimatorStateInfo(attackLayer);
            AnimatorClipInfo[] clips = entering ? animator.GetNextAnimatorClipInfo(attackLayer) : animator.GetCurrentAnimatorClipInfo(attackLayer);
            clipTime = clips.Length > 0 ? s.normalizedTime * clips[0].clip.length : 0f;
            return s.IsName(leapState);
        }

        private bool Dead() => controller.CurrentState == EnemyState.Dead || (health != null && health.CurrentHp.Value <= 0f);

        private static bool Alive(Robot robot) => robot != null && robot.isActiveAndEnabled;


        // ดาเมจลงชิ้นที่ใกล้สุดผ่าน DamageRouter (ด่านเดียวกับ EnemyCombat: ทีม/เริ่มเกมแล้ว) + หุ่นล้ม
        private void HitRobot(Robot robot, float damage, Vector3 dir)
        {
            LimbHealth target = null;
            float best = float.MaxValue;
            foreach (RobotLimb limb in robot.AttachedLimbs())
            {
                if (limb.Health == null) continue;
                float d = Vector3.Distance(transform.position, limb.Health.transform.position);
                if (d < best) { best = d; target = limb.Health; }
            }
            bool applied = target != null && DamageRouter.TryApply(target, new DamageInfo(damage, target.transform.position, dir, DamageSource.EnemyMelee, Team.Enemy));
            Debug.Log($"[KaijuSpecials] 💥 โดน {robot.name} ({(target ? target.name : "ไม่มีชิ้น")}) dmg {damage} ลงจริง={applied}");
            if (Nsc.Match.GameplayGate.CanDamage) robot.ServerKnockDown(FallReason.KnockedDown);
        }

        // ================================================================ ทุกเครื่อง: ท่า + แนวเตือน + คลื่น

        [ClientRpc]
        private void StartSpecialClientRpc(Special special, Vector3 origin, Vector3 dir)
        {
            transform.rotation = Quaternion.LookRotation(dir);
            string state = special == Special.Roll ? rollState : special == Special.Roar ? roarState : poundState;
            animator.CrossFadeInFixedTime(state, 0.15f, attackLayer, 0f);
            // หยุดแค่ภาพของท่าก่อน (host มี Run ของ server วิ่งอยู่ด้วย ห้าม StopAllCoroutines)
            if (visual != null) StopCoroutine(visual);
            EndWarning();
            visual = StartCoroutine(special switch
            {
                Special.Roll => RollVisual(origin, dir),
                Special.Roar => RoarVisual(origin),
                _ => PoundVisual(origin, dir),
            });
        }

        [ClientRpc]
        private void StartRingsClientRpc(Vector3 center)
        {
            if (visual != null) StopCoroutine(visual);
            EndWarning();
            visual = StartCoroutine(RingsVisual(center));
        }

        [ClientRpc]
        private void StartLeapClientRpc(Vector3 dir, Vector3 land)
        {
            transform.rotation = Quaternion.LookRotation(dir);
            if (feel != null) feel.SetSlamOverride(land, leapRadius);
            if (visual != null) StopCoroutine(visual);
            animator.CrossFadeInFixedTime(leapState, 0.15f, attackLayer, 0f);
        }

        [ClientRpc]
        private void CameraShakeClientRpc(Vector3 at)
        {
            CameraShake.Add(at, 1.2f, 0.6f);
            CameraShake.KickFov(6f);
        }

        private IEnumerator RoarVisual(Vector3 at)
        {
            if (roarSfx != null && TryGetComponent(out AudioSource source)) source.PlayOneShot(roarSfx);
            yield return new WaitForSeconds(roarShakeTime);
            CameraShake.Add(at, 1.6f, 1.2f);
            CameraShake.KickFov(8f);
        }

        // วงแดงเต็มวง = เขตที่วงหนามวิ่งผ่าน (ไม่มีที่ปลอดภัยในวง) — เตือน ringWarnTime ก่อนเริ่มท่า
        // ทุบซ้ำ: ย้อนท่าทุบไปก่อนจังหวะกระแทก (gap ≥ poundImpact = เล่นใหม่ทั้งท่า)
        private IEnumerator RingsVisual(Vector3 center)
        {
            center.y = GroundY(center);
            Color c = warningColor;
            c.a *= 0.6f;
            warnOuter = MakeWarningShape(PrimitiveType.Cylinder, warningMaterial, c);
            warnOuter.SetPositionAndRotation(center + Vector3.up * 0.05f, Quaternion.identity);
            warnOuter.localScale = new Vector3(ringMaxRadius * 2f, 0.01f, ringMaxRadius * 2f);

            float start = Time.time;
            AudioSource source = GetComponent<AudioSource>();
            while (Time.time - start < ringWarnTime) yield return null;
            animator.CrossFadeInFixedTime(poundState, 0.15f, attackLayer, 0f);
            for (int k = 0; k < ringCount; k++)
            {
                float impact = RingImpact(k);
                if (k > 0)
                {
                    // ย้อนท่าทุบไปก่อนจังหวะกระแทก lead วิ (คิดตามความเร็วท่าตอนนี้) ให้ยกแขนแล้วทุบลงพอดี
                    // gap ≥ poundImpact → lead = ทั้งท่า: เล่นใหม่ตั้งแต่ต้น โดยเริ่มให้มือถึงพื้นตรงจังหวะหนามขึ้น
                    float lead = Mathf.Min(ringSlamGap, poundImpact);
                    while (Time.time - start < impact - lead) yield return null;
                    float speed = Mathf.Max(0.1f, animator.GetFloat("AttackSpeed"));
                    animator.CrossFadeInFixedTime(poundState, 0.1f, attackLayer, (poundImpact - lead) * speed);
                }
                while (Time.time - start < impact) yield return null;
                CameraShake.Add(center, 1.2f, 0.6f);
                CameraShake.KickFov(6f);
                if (source != null && poundSfx != null) source.PlayOneShot(poundSfx);
                StartCoroutine(RingSpikes(center));
            }
            float end = RingImpact(ringCount - 1) + (ringMaxRadius - ringStartRadius) / ringSpeed;
            while (Time.time - start < end) yield return null;
            EndWarning();
        }

        private const int RingSpawnsPerFrame = 6;

        // หนามดินขึ้นเป็นวงตามหน้าวงที่ขยาย (ตรงกับ RingDamage) — ชุดหนามชี้ออกนอกวง ขนาดเท่าหนามคลื่น
        // (EarthSlamSpikesAoe ขยายหนามตามขนาดวง วงกว้างแล้วหนามยักษ์บังจอ จึงไม่ใช้)
        private IEnumerator RingSpikes(Vector3 center)
        {
            if (earthSpikes == null) yield break;
            for (float r = ringStartRadius; r <= ringMaxRadius; r += ringVfxStep)
            {
                int count = Mathf.Max(6, Mathf.RoundToInt(2f * Mathf.PI * r / ringVfxSpacing));
                for (int i = 0; i < count; i++)
                {
                    // กระจายการสร้างไปหลายเฟรม — สร้างทั้งวงในเฟรมเดียวเคยกระตุก 200ms
                    if (i > 0 && i % RingSpawnsPerFrame == 0) yield return null;
                    float a = (i + Random.value * 0.3f) / count * Mathf.PI * 2f;
                    Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 p = center + outward * r;
                    p.y = GroundY(p);
                    Instantiate(earthSpikes).Play(new PixPlays.ElementalVFX.VfxData(p, p + outward, earthSpikesLife * 0.8f, earthSpikesRadius));
                }
                yield return new WaitForSeconds(ringVfxStep / ringSpeed);
            }
        }


        private IEnumerator RollVisual(Vector3 origin, Vector3 dir)
        {
            BeginWarning(origin, dir, rollLength, rollWidth);
            var spin = new KaijuSpinLines(transform, animator, spinLineMaterial, spinLineColor, spinLineWidth, spinLineRadiusScale, spinLineLife);
            float start = Time.time, nextWhoosh = rollHitWindow.x;
            while (Time.time - start < rollHitWindow.y)
            {
                float t = Time.time - start;
                UpdateWarning(origin, dir, rollLength, rollWidth, Mathf.Clamp01(t / rollHitWindow.x));
                spin.Update(t >= rollHitWindow.x, t);
                if (rollSfx != null && t >= nextWhoosh)
                {
                    PlayFrom(gameObject, ref rollAudio, rollSfx, rollSfxStart, 1f, Random.Range(0.9f, 1.1f));
                    nextWhoosh = t + rollSfxInterval;
                }
                yield return null;
            }
            EndWarning();
            spin.Destroy();
        }

        private IEnumerator PoundVisual(Vector3 origin, Vector3 dir)
        {
            BeginWarning(origin, dir, waveLength, waveWidth);
            float start = Time.time;
            while (Time.time - start < poundImpact)
            {
                UpdateWarning(origin, dir, waveLength, waveWidth, (Time.time - start) / poundImpact);
                yield return null;
            }
            if (TryGetComponent(out AudioSource source) && poundSfx != null) source.PlayOneShot(poundSfx);
            PlayFrom(gameObject, ref spikesAudio, spikesSfx, 0f); // ช่องเสียงแยก ไว้หรี่ทีหลัง (PlayOneShot หยุดกลางทางไม่ได้)
            // คลื่น: เอฟเฟกต์ที่พื้นทีละจุดตามความเร็วคลื่น แนวเตือนหดตามหน้าคลื่น
            float travelled = 0f, nextVfx = 0f, nextSpikes = 0f;
            while (travelled < waveLength)
            {
                travelled = Mathf.Min(waveLength, travelled + waveSpeed * Time.deltaTime);
                while (nextVfx <= travelled)
                {
                    Vector3 p = origin + dir * nextVfx;
                    p.y = GroundY(p);
                    foreach (GameObject v in waveVfx) SpawnVfx(v, p, waveVfxScale);
                    nextVfx += waveVfxSpacing;
                }
                while (earthSpikes != null && nextSpikes <= travelled)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    for (int lane = 0; lane < earthSpikesLanes; lane++)
                    {
                        Vector3 p = origin + dir * nextSpikes + side * (((lane + 0.5f) / earthSpikesLanes - 0.5f) * waveWidth);
                        p.y = GroundY(p);
                        // PixPlays หันหนามไปทาง target − source แล้วขยายตาม radius
                        Instantiate(earthSpikes).Play(new PixPlays.ElementalVFX.VfxData(p, p + dir, earthSpikesLife, earthSpikesRadius));
                    }
                    nextSpikes += earthSpikesSpacing;
                }
                UpdateWarning(origin + dir * travelled, dir, waveLength - travelled, waveWidth, 1f);
                yield return null;
            }
            EndWarning();
            // เสียงดินเงียบสนิทพอดีตอนหนามชุดสุดท้ายหายไป (ชุดสุดท้ายเพิ่งเกิดตอนคลื่นถึงปลาย)
            if (spikesAudio != null && spikesAudio.isPlaying)
            {
                float fade = Mathf.Min(0.4f, earthSpikesLife);
                yield return new WaitForSeconds(earthSpikesLife - fade);
                for (float t = 0f; t < fade; t += Time.deltaTime)
                {
                    spikesAudio.volume = 1f - t / fade;
                    yield return null;
                }
                spikesAudio.Stop();
            }
        }

        // ================================================================ แนวเตือน (กล่องแบนบนพื้น: ชั้นนอกจาง ชั้นในยาวขึ้นจนเต็มตอนโดน)

        private void BeginWarning(Vector3 origin, Vector3 dir, float length, float width)
        {
            EndWarning();
            if (warningMaterial == null) return;
            warnOuter = MakeStrip(0.5f);
            warnInner = MakeStrip(1f);
            UpdateWarning(origin, dir, length, width, 0f);
        }

        private Transform MakeStrip(float alphaScale)
        {
            Color c = warningColor;
            c.a *= alphaScale;
            return MakeWarningShape(PrimitiveType.Cube, warningMaterial, c);
        }

        private void UpdateWarning(Vector3 from, Vector3 dir, float length, float width, float fill)
        {
            if (!warnOuter) return;
            float y = GroundY(from) + 0.06f;
            Quaternion rot = Quaternion.LookRotation(dir);
            Vector3 a = new Vector3(from.x, y, from.z);
            warnOuter.SetPositionAndRotation(a + dir * (length * 0.5f), rot);
            warnOuter.localScale = new Vector3(width, 0.02f, Mathf.Max(0.01f, length));
            float l = length * Mathf.Clamp01(fill);
            warnInner.SetPositionAndRotation(a + dir * (l * 0.5f) + Vector3.up * 0.02f, rot);
            warnInner.localScale = new Vector3(width, 0.02f, Mathf.Max(0.01f, l));
        }

        private void EndWarning()
        {
            if (warnOuter) Destroy(warnOuter.gameObject);
            if (warnInner) Destroy(warnInner.gameObject);
            warnOuter = warnInner = null;
        }

        private float GroundY(Vector3 at) => KaijuUtil.GroundY(at, transform, transform.position.y);

        private static void SpawnVfx(GameObject prefab, Vector3 position, float scale)
        {
            if (prefab == null) return;
            GameObject vfx = Instantiate(prefab, position, Quaternion.identity);
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

        public override void OnDestroy()
        {
            EndWarning();
            base.OnDestroy();
        }
    }
}
