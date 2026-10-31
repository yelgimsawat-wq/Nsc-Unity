using DG.Tweening;
using Nsc.Match;
using Nsc.Robots;
using NscGame.Enemy;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// HUD ของฉาก RobotArena (หุ่น IK ตัวเดียว ผู้เล่นคนละชิ้น) — สร้าง Canvas เองตอนเริ่ม ไม่ต้องวาง UI ในฉาก
    /// - การ์ดเลือด 4 ใบ (หนึ่งใบต่อหนึ่งชิ้น = ผู้เล่นหนึ่งคน): ชื่อผู้เล่น, หลอดเลือดแบ่งช่อง + หลอดดาเมจค้าง, หลุด/รอดึงกลับ
    /// - โดนตี: การ์ดสั่น + แฟลชแดง + เลขดาเมจ | ชิ้นของเราโดน = ขอบจอแดงวาบ
    /// - หุ่นล้ม: ขอบจอแดงเต้นเป็นจังหวะ + เสียงเตือน + ป้าย SYSTEM DOWN บอกให้กด Q ลุก (นับโหวตทุกคน)
    /// อ่านอย่างเดียว — เลือด/ล้ม/โหวตจริงอยู่ที่ LimbHealth / RobotIKControl (ซิงก์แล้วทุกเครื่อง)
    /// </summary>
    public class RobotArenaHud : MonoBehaviour
    {
        [Tooltip("ว่าง = หาหุ่น IK ในฉากเอง")]
        [SerializeField] private RobotIKControl control;
        [Tooltip("ฟอนต์หัวข้อ/ตัวเลข (Oswald)")]
        [SerializeField] private TMP_FontAsset headFont;
        [Tooltip("ฟอนต์ตัวเล็ก (Inter Bold)")]
        [SerializeField] private TMP_FontAsset bodyFont;

        [Header("สี")]
        [SerializeField] private Color accent = new Color(0.25f, 0.9f, 1f);
        [SerializeField] private Color warning = new Color(1f, 0.72f, 0.18f);
        [SerializeField] private Color danger = new Color(1f, 0.2f, 0.22f);
        [SerializeField] private Color panel = new Color(0.03f, 0.06f, 0.09f, 0.82f);

        [Header("เลือด")]
        [Tooltip("เลือดต่ำกว่านี้ (สัดส่วน) = หลอดแดงกะพริบ CRITICAL")]
        [Range(0f, 1f)] [SerializeField] private float lowHp = 0.3f;
        [Tooltip("จำนวนช่องในหลอดเลือด")]
        [SerializeField] private int segments = 10;

        [Header("โดนตี")]
        [Tooltip("ขอบจอแดงสูงสุดตอนชิ้นของเราโดนตี")]
        [Range(0f, 1f)] [SerializeField] private float hitVignette = 0.55f;

        [Header("หุ่นล้ม")]
        [Tooltip("ขอบจอแดงตอนนอน (เต้นตามจังหวะเตือน)")]
        [Range(0f, 1f)] [SerializeField] private float downVignette = 0.75f;
        [Tooltip("เสียงเตือนดังทุกกี่วินาที")]
        [SerializeField] private float alarmInterval = 0.9f;
        [Range(0f, 1f)] [SerializeField] private float alarmVolume = 0.25f;

        [Header("เพลงด่าน")]
        [Tooltip("เพลงต่อสู้ วนตลอดด่าน (ดังตาม Master Volume ในเมนู ESC)")]
        [SerializeField] private AudioClip music;
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.45f;
        [Tooltip("จบแมตช์แล้วเพลงเบาลงเหลือเท่านี้ (สัดส่วน) ให้เสียงชนะ/แพ้เด่น")]
        [Range(0f, 1f)] [SerializeField] private float musicEndedScale = 0.35f;

        private static readonly string[] SlotTags = { "L-ARM", "R-ARM", "L-LEG", "R-LEG" };
        private const float CardWidth = 330f, CardHeight = 104f, CardGap = 16f, MidGap = 56f;

        private class Card
        {
            public RectTransform rect;
            public Vector2 home;
            public HudShape frame, back, fill, trail, flash;
            public TMP_Text tag, hp, max, status, damage;
            public Text player;
            public LimbHealth health;
            public float shown = 1f, lag = 1f, lagHold, lastHp = -1f, flashT, damageSum, damageT;
            public int shownHp = -1, shownMax = -1;
            public string shownStatus, shownName;
            public Tween shake;
        }

        private readonly Card[] cards = new Card[4];
        private CanvasGroup root;
        private RawImage vignette;
        private Image redWash;
        private HudShape barTop, barBottom;
        private CanvasGroup warn;
        private RectTransform warnRect, keyRect;
        private TMP_Text warnTitle, warnKey;
        private TMP_Text warnSub, warnVotes;
        private readonly HudShape[] votePips = new HudShape[4];
        private AudioSource audioSource;
        private AudioClip alarmClip, onlineClip;
        private Font thaiFont;
        private Tween warnPop;

        private float myHit, downWeight, fallFlash, alarmTimer, nextNames;
        private bool wasDown;

        // หลอดเลือดบอส (บนกลางจอ)
        private const float BossBarWidth = 860f;
        private EnemyHealth boss;
        private CanvasGroup bossGroup;
        private RectTransform bossRect;
        private HudShape bossFill, bossLag, bossFlash;
        private TMP_Text bossHp, bossRage;
        private Vector2 bossHome;
        private float bossShown = 1f, bossLagValue = 1f, bossLagHold, bossLastHp = -1f, bossFlashT;
        private int bossShownHp = -1;
        private Tween bossShake;

        // หน้าชนะ/แพ้ (Canvas แยก กดปุ่มได้)
        private CanvasGroup result;
        private RectTransform resultTitleRect;
        private TMP_Text resultTitle, resultTime;
        private TMP_Text resultSub, resultWait;
        private HudShape resultBand;
        private GameObject hostButtons, clientButtons;
        private bool resultShown;
        private Sequence resultSeq;
        private AudioClip winClip, loseClip;
        private AudioSource musicSource;

        private void Awake()
        {
            thaiFont = Font.CreateDynamicFontFromOSFont(new[] { "Leelawadee UI", "Tahoma", "Arial" }, 32); // ฟอนต์ TMP ในโปรเจกต์ไม่มีอักษรไทย
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            alarmClip = Tones("HudAlarm", new[] { 880f, 660f }, 0.11f);
            onlineClip = Tones("HudOnline", new[] { 520f, 780f, 1040f }, 0.07f);
            winClip = Tones("HudWin", new[] { 523f, 659f, 784f, 1047f }, 0.13f);
            loseClip = Tones("HudLose", new[] { 392f, 330f, 262f, 196f }, 0.18f);
            Build();
            BuildResult();

            if (music)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.clip = music;
                musicSource.loop = true;
                musicSource.spatialBlend = 0f;
                musicSource.volume = 0f; // ค่อยๆ ดังขึ้นใน Update
                musicSource.priority = 0;
                musicSource.Play();
            }
        }

        private void OnDestroy()
        {
            foreach (Card c in cards)
                if (c != null && c.shake != null && c.shake.IsActive()) c.shake.Kill(false);
            if (warnPop != null && warnPop.IsActive()) warnPop.Kill(false);
            if (bossShake != null && bossShake.IsActive()) bossShake.Kill(false);
            if (resultSeq != null && resultSeq.IsActive()) resultSeq.Kill(false);
            if (resultShown) UiFocus.Pop(this);
            if (vignette && vignette.texture) Destroy(vignette.texture);
            foreach (AudioClip clip in new[] { alarmClip, onlineClip, winClip, loseClip })
                if (clip) Destroy(clip);
        }

        // ================================================================ ต่อเฟรม

        private void Update()
        {
            if (!control || (control.IsOnline && !control.IsLocalRobot)) control = LocalControl();
            bool show = control && (!control.IsOnline || (MatchSession.Current != null && MatchSession.Current.IsPlaying));
            float dt = Time.unscaledDeltaTime; // หยุดภาพตอนหมัดโดน (timeScale) — HUD ยังขยับ
            TickResult();
            if (musicSource) // เข้าด่าน = ดังขึ้นใน ~2 วิ | จบแมตช์ = เบาลง
                musicSource.volume = Mathf.MoveTowards(musicSource.volume, musicVolume * (resultShown ? musicEndedScale : 1f), dt * 0.25f);
            root.alpha = Mathf.MoveTowards(root.alpha, show ? 1f : 0f, dt * 4f);
            if (!show) { wasDown = false; return; }
            TickBoss(dt);

            if (Time.unscaledTime >= nextNames) { nextNames = Time.unscaledTime + 0.5f; RefreshNames(); }
            for (int i = 0; i < cards.Length && i < control.LimbCount; i++) TickCard(i, dt);
            TickDown(dt);
        }

        /// <summary>หุ่นของผู้เล่นเครื่องนี้ — ฉาก PVP มีหุ่นสองตัว HUD ต้องโชว์ฝั่งเราเท่านั้น (ยังไม่เลือกทีม = ตัวแรกที่เจอ)</summary>
        private static RobotIKControl LocalControl()
        {
            RobotIKControl first = null;
            foreach (RobotIKControl candidate in FindObjectsByType<RobotIKControl>(FindObjectsSortMode.None))
            {
                if (candidate.IsLocalRobot) return candidate;
                if (first == null) first = candidate;
            }
            return first;
        }

        private void TickCard(int i, float dt)
        {
            Card c = cards[i];
            if (!c.health && control.Robot) c.health = control.Robot.GetLimb((LimbSlot)i)?.Health;
            bool attached = control.IsLimbAttached(i), mine = control.IsLimbMine(i);
            float hp = c.health ? c.health.Hp : 0f, max = c.health ? c.health.MaxHp : 0f;
            float pct = c.health ? c.health.HpPercent : 1f;

            // เลือดลด (ฟื้นทีละ 2/วิ เพิ่มขึ้นทีละนิด) = โดนตี
            if (c.lastHp >= 0f && hp < c.lastHp - 0.5f) OnHit(c, c.lastHp - hp, mine);
            c.lastHp = hp;

            c.shown = Mathf.Lerp(c.shown, pct, 1f - Mathf.Exp(-18f * dt));
            if (pct >= c.lag) c.lag = pct;
            else if (Time.unscaledTime > c.lagHold) c.lag = Mathf.MoveTowards(c.lag, pct, 0.7f * dt); // หลอดดาเมจค้างแป๊บแล้วไหลตาม

            bool low = attached && pct <= lowHp;
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f);
            Color bar = pct > 0.6f ? accent : pct > lowHp ? warning : danger;
            if (low) bar.a = Mathf.Lerp(0.55f, 1f, blink);
            c.fill.color = bar;
            c.fill.Fill = attached ? c.shown : 0f;
            c.trail.Fill = attached ? c.lag : 0f;

            int hpInt = attached ? Mathf.CeilToInt(hp) : -1, maxInt = Mathf.CeilToInt(max);
            if (hpInt != c.shownHp) { c.shownHp = hpInt; c.hp.text = hpInt < 0 ? "--" : hpInt.ToString(); }
            if (maxInt != c.shownMax) { c.shownMax = maxInt; c.max.text = "/" + maxInt; }
            c.hp.color = attached ? (low ? danger : Color.white) : new Color(1f, 1f, 1f, 0.35f);

            string status;
            if (!attached)
            {
                float wait = control.ReattachCooldown(i);
                status = wait > 0f ? $"DETACHED  {wait:0.0}s" : mine ? "DETACHED  HOLD [R]" : "DETACHED";
            }
            else status = low ? "CRITICAL" : "";
            if (status != c.shownStatus) { c.shownStatus = status; c.status.text = status; }
            c.status.color = !attached ? new Color(danger.r, danger.g, danger.b, Mathf.Lerp(0.6f, 1f, blink)) : danger;

            c.flashT = Mathf.MoveTowards(c.flashT, 0f, dt * 3.5f);
            c.flash.color = new Color(danger.r, danger.g, danger.b, c.flashT * 0.55f);
            Color edge = !attached ? new Color(danger.r, danger.g, danger.b, 0.85f) : mine ? accent : new Color(1f, 1f, 1f, 0.14f);
            c.frame.color = Color.Lerp(edge, Color.white, c.flashT * 0.8f);
            c.back.color = attached ? panel : new Color(0.22f, 0.03f, 0.05f, 0.85f);
            c.tag.color = mine ? accent : new Color(0.75f, 0.82f, 0.88f);

            c.damageT = Mathf.MoveTowards(c.damageT, 0f, dt);
            c.damage.alpha = Mathf.Clamp01(c.damageT * 2f);
            c.damage.rectTransform.anchoredPosition = new Vector2(CardWidth - 40f, CardHeight + 6f + (1f - c.damageT) * 26f);
            if (c.damageT <= 0f) c.damageSum = 0f;
        }

        private void OnHit(Card c, float amount, bool mine)
        {
            c.flashT = 1f;
            c.lagHold = Time.unscaledTime + 0.45f;
            c.damageSum += amount;
            c.damageT = 1f;
            c.damage.text = "-" + Mathf.CeilToInt(c.damageSum);

            if (c.shake != null && c.shake.IsActive()) c.shake.Kill(false);
            c.rect.anchoredPosition = c.home;
            float power = Mathf.Clamp(amount / 40f, 0.4f, 1.6f);
            c.shake = c.rect.DOShakeAnchorPos(0.32f, new Vector2(14f, 8f) * power, 40, 90f, false, true)
                .SetUpdate(true)
                .OnComplete(() => c.rect.anchoredPosition = c.home);

            if (mine) myHit = Mathf.Clamp01(myHit + 0.35f + amount / 150f);
        }

        private void TickDown(float dt)
        {
            bool down = control.IsDown, gettingUp = control.IsGettingUp;
            if (down && !wasDown) OnFallen();
            if (!down && wasDown && audioSource) audioSource.PlayOneShot(onlineClip, alarmVolume);
            wasDown = down;

            downWeight = Mathf.MoveTowards(downWeight, down ? 1f : 0f, dt * (down ? 5f : 1.6f));
            float pulse = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.2f, alarmInterval));

            if (down)
            {
                alarmTimer -= dt;
                if (alarmTimer <= 0f)
                {
                    alarmTimer = alarmInterval;
                    if (audioSource) audioSource.PlayOneShot(alarmClip, alarmVolume);
                }
            }

            myHit = Mathf.MoveTowards(myHit, 0f, dt * 1.8f);
            fallFlash = Mathf.MoveTowards(fallFlash, 0f, dt * 1.5f);
            float downA = downWeight * downVignette * Mathf.Lerp(0.55f, 1f, pulse);
            vignette.color = new Color(danger.r, 0.02f, 0.03f, Mathf.Max(downA, myHit * hitVignette));
            redWash.color = new Color(0.9f, 0f, 0.02f, downWeight * 0.07f * pulse + fallFlash * 0.4f);
            Color bars = new Color(danger.r, danger.g, danger.b, downWeight * Mathf.Lerp(0.35f, 0.9f, pulse));
            barTop.color = bars;
            barBottom.color = bars;

            warn.alpha = downWeight;
            if (downWeight <= 0f) return;
            warnTitle.text = gettingUp ? "REBOOTING" : "SYSTEM DOWN";
            bool legs = control.LegsReady, voted = control.VotedStand;
            int need = Mathf.Max(1, control.StandVotesNeeded), votes = control.StandVotes;
            warnSub.text = !legs ? "LEG DETACHED — HOLD [R] TO REATTACH"
                         : voted ? "WAITING FOR TEAM TO PRESS [Q]"
                         : ""; // ปุ่ม Q บอกอยู่แล้ว
            keyRect.gameObject.SetActive(legs && !voted);
            keyRect.localScale = Vector3.one * (1f + 0.1f * pulse);
            warnKey.color = Color.Lerp(Color.white, danger, pulse * 0.4f);

            bool team = control.IsOnline && need > 1;
            for (int i = 0; i < votePips.Length; i++)
            {
                bool used = team && i < need, done = i < votes;
                votePips[i].gameObject.SetActive(used);
                if (used) votePips[i].color = done ? accent : new Color(1f, 1f, 1f, 0.18f);
            }
            if (team) // ช่องที่ใช้อยู่กลางจอ
                for (int i = 0; i < need && i < votePips.Length; i++)
                    votePips[i].rectTransform.anchoredPosition = new Vector2((i - (need - 1) * 0.5f) * 40f, -114f);
            warnVotes.text = team ? $"READY {votes}/{need}" : "";
        }

        private void OnFallen()
        {
            fallFlash = 1f;
            alarmTimer = 0f;
            if (warnPop != null && warnPop.IsActive()) warnPop.Kill(false);
            warnRect.localScale = Vector3.one * 1.25f;
            warnPop = warnRect.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        private void RefreshNames()
        {
            LimbSelection selection = LimbSelection.Current;
            Robot robot = control.Robot;
            var players = selection != null && robot != null && robot.IsSpawned ? selection.PlayersOn(robot.NetworkObjectId) : null;
            for (int i = 0; i < cards.Length && i < control.LimbCount; i++)
            {
                string name;
                if (!control.IsOnline) name = control.IsLimbMine(i) ? "YOU" : "—";
                else
                {
                    ulong id = control.LimbController(i);
                    name = id == LimbSelection.NoClient ? "— EMPTY —" : "P" + (id + 1);
                    if (players != null && id != LimbSelection.NoClient)
                        foreach (SelectionPlayer p in players)
                            if (p.clientId == id) { name = p.playerName.ToString(); break; }
                }
                if (control.IsLimbMine(i) && name != "YOU") name = "» " + name;
                if (name == cards[i].shownName) continue;
                cards[i].shownName = name;
                cards[i].player.text = name;
            }
            for (int i = 0; i < cards.Length && i < control.LimbCount; i++)
                cards[i].player.color = control.IsLimbMine(i) ? accent : new Color(1f, 1f, 1f, 0.85f);
        }

        // ================================================================ สร้าง UI

        private void Build()
        {
            var canvasGo = new GameObject("RobotArenaHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root = canvasGo.GetComponent<CanvasGroup>();
            root.alpha = 0f;
            root.interactable = false;
            root.blocksRaycasts = false;
            Transform c = canvasGo.transform;

            // ชั้นหลัง: ขอบจอแดง + จอแดงจาง + แถบเตือนบน/ล่าง
            redWash = New<Image>(c, "RedWash", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            redWash.color = Color.clear;
            vignette = New<RawImage>(c, "Vignette", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            vignette.texture = VignetteTexture();
            vignette.color = Color.clear;
            barTop = New<HudShape>(c, "AlertTop", new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -14f), Vector2.zero);
            barTop.slant = 0f;
            barTop.color = Color.clear;
            barBottom = New<HudShape>(c, "AlertBottom", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 14f));
            barBottom.slant = 0f;
            barBottom.color = Color.clear;

            // การ์ด 4 ใบกลางล่างจอ: แขนซ้าย-ขวา | ขาซ้าย-ขวา
            float total = CardWidth * 4f + CardGap * 2f + MidGap;
            float x = -total * 0.5f;
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = BuildCard(c, i, new Vector2(x, 34f));
                x += CardWidth + (i == 1 ? MidGap : CardGap);
            }

            BuildWarning(c);
            BuildBoss(c);
        }

        private Card BuildCard(Transform parent, int index, Vector2 pos)
        {
            var card = new Card();
            var go = new GameObject($"Card_{SlotTags[index]}", typeof(RectTransform));
            card.rect = (RectTransform)go.transform;
            card.rect.SetParent(parent, false);
            card.rect.anchorMin = card.rect.anchorMax = new Vector2(0.5f, 0f);
            card.rect.pivot = Vector2.zero;
            card.rect.sizeDelta = new Vector2(CardWidth, CardHeight);
            card.rect.anchoredPosition = card.home = pos;
            Transform t = card.rect;

            card.frame = Shape(t, "Frame", new Vector2(-3f, -3f), new Vector2(CardWidth + 6f, CardHeight + 6f), Color.white);
            card.back = Shape(t, "Back", Vector2.zero, new Vector2(CardWidth, CardHeight), panel);
            Shape(t, "Stripe", new Vector2(4f, CardHeight - 6f), new Vector2(CardWidth * 0.42f, 3f), new Color(1f, 1f, 1f, 0.08f));

            // แถวบน: ชื่อชิ้น | ชื่อผู้เล่น — แถวกลาง: สถานะ — แถวล่าง: หลอดเลือด + ตัวเลข
            card.tag = Tmp(t, "Tag", headFont, 30f, TextAlignmentOptions.BottomLeft, new Vector2(30f, 60f), new Vector2(110f, 38f));
            card.tag.text = SlotTags[index];
            card.tag.characterSpacing = 4f;
            card.player = Legacy(t, "Player", 20, TextAnchor.LowerRight, new Vector2(140f, 64f), new Vector2(CardWidth - 152f, 30f));
            card.player.text = "—";
            card.status = Tmp(t, "Status", bodyFont, 14f, TextAlignmentOptions.BottomLeft, new Vector2(24f, 42f), new Vector2(260f, 18f));
            card.status.characterSpacing = 3f;

            // หลอดเลือด: พื้น → หลอดดาเมจค้าง → หลอดจริง → ร่องแบ่งช่อง
            const float barX = 14f, barY = 14f, barH = 24f, barW = CardWidth - 120f;
            Shape(t, "BarBack", new Vector2(barX, barY), new Vector2(barW, barH), new Color(1f, 1f, 1f, 0.07f));
            card.trail = Shape(t, "BarTrail", new Vector2(barX, barY), new Vector2(barW, barH), new Color(1f, 0.92f, 0.85f, 0.85f));
            card.fill = Shape(t, "BarFill", new Vector2(barX, barY), new Vector2(barW, barH), accent);
            float span = barW - barH * 0.25f;
            for (int s = 1; s < Mathf.Max(1, segments); s++)
                Shape(t, "Gap", new Vector2(barX + span * s / segments - 1.5f, barY), new Vector2(3f + barH * 0.25f, barH), new Color(0.03f, 0.06f, 0.09f, 0.9f));

            card.hp = Tmp(t, "Hp", headFont, 36f, TextAlignmentOptions.BottomRight, new Vector2(CardWidth - 104f, 6f), new Vector2(66f, 44f));
            card.max = Tmp(t, "Max", bodyFont, 14f, TextAlignmentOptions.BottomLeft, new Vector2(CardWidth - 36f, 12f), new Vector2(40f, 20f));
            card.max.color = new Color(1f, 1f, 1f, 0.5f);

            card.flash = Shape(t, "Flash", Vector2.zero, new Vector2(CardWidth, CardHeight), Color.clear);
            card.damage = Tmp(t, "Damage", headFont, 30f, TextAlignmentOptions.Center, new Vector2(CardWidth - 40f, CardHeight + 6f), new Vector2(100f, 40f));
            card.damage.rectTransform.pivot = new Vector2(0.5f, 0f);
            card.damage.color = danger;
            card.damage.alpha = 0f;
            return card;
        }

        private void BuildWarning(Transform parent)
        {
            var go = new GameObject("DownWarning", typeof(RectTransform), typeof(CanvasGroup));
            warnRect = (RectTransform)go.transform;
            warnRect.SetParent(parent, false);
            warnRect.anchorMin = warnRect.anchorMax = new Vector2(0.5f, 0.66f);
            warnRect.sizeDelta = new Vector2(900f, 220f);
            warn = go.GetComponent<CanvasGroup>();
            warn.alpha = 0f;

            var plate = New<HudShape>(warnRect, "Plate", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400f, -24f), new Vector2(400f, 110f));
            plate.slant = 0.2f;
            plate.color = new Color(0.12f, 0f, 0.02f, 0.72f);
            var edge = New<HudShape>(warnRect, "Edge", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-373f, 104f), new Vector2(400f, 110f));
            edge.slant = 0f;
            edge.color = danger;

            warnTitle = Tmp(warnRect, "Title", headFont, 72f, TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(900f, 90f));
            Center(warnTitle.rectTransform, new Vector2(0f, 62f));
            warnTitle.characterSpacing = 18f;
            warnTitle.color = danger;
            warnTitle.fontStyle = FontStyles.Bold;

            warnSub = Tmp(warnRect, "Sub", bodyFont, 24f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(900f, 44f));
            warnSub.characterSpacing = 4f;
            Center(warnSub.rectTransform, new Vector2(0f, 2f));
            warnSub.color = Color.white;

            // ปุ่ม Q แบบคีย์แคป
            var key = new GameObject("KeyQ", typeof(RectTransform));
            keyRect = (RectTransform)key.transform;
            keyRect.SetParent(warnRect, false);
            Center(keyRect, new Vector2(0f, -62f));
            keyRect.sizeDelta = new Vector2(70f, 70f);
            var keyEdge = Shape(keyRect, "Edge", new Vector2(-3f, -3f), new Vector2(76f, 76f), Color.white);
            keyEdge.slant = 0f;
            var keyBack = Shape(keyRect, "Back", Vector2.zero, new Vector2(70f, 70f), new Color(0.08f, 0.02f, 0.03f, 0.95f));
            keyBack.slant = 0f;
            warnKey = Tmp(keyRect, "Q", headFont, 44f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(70f, 70f));
            Center(warnKey.rectTransform, Vector2.zero);
            warnKey.text = "Q";

            // โหวตลุก: ช่องละคน เติมเต็ม = กด Q แล้ว
            for (int i = 0; i < votePips.Length; i++)
            {
                votePips[i] = Shape(warnRect, "Vote", Vector2.zero, new Vector2(30f, 14f), Color.clear);
                Center(votePips[i].rectTransform, new Vector2((i - (votePips.Length - 1) * 0.5f) * 40f, -114f));
            }
            warnVotes = Tmp(warnRect, "Votes", bodyFont, 16f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(400f, 28f));
            warnVotes.characterSpacing = 4f;
            Center(warnVotes.rectTransform, new Vector2(0f, -140f));
            warnVotes.color = new Color(1f, 1f, 1f, 0.85f);
        }

        // ================================================================ หลอดเลือดบอส

        private void TickBoss(float dt)
        {
            if (!boss) boss = FindAnyObjectByType<EnemyHealth>();
            bool alive = boss && !boss.IsDead() && boss.isActiveAndEnabled;
            bossGroup.alpha = Mathf.MoveTowards(bossGroup.alpha, alive ? 1f : 0f, dt * 3f);
            if (!boss) return;

            float max = Mathf.Max(1f, boss.GetMaxHp()), hp = Mathf.Max(0f, boss.CurrentHp.Value), pct = hp / max;
            if (bossLastHp >= 0f && hp < bossLastHp - 0.5f)
            {
                bossFlashT = 1f;
                bossLagHold = Time.unscaledTime + 0.5f;
                if (bossShake != null && bossShake.IsActive()) bossShake.Kill(false);
                bossRect.anchoredPosition = bossHome;
                float power = Mathf.Clamp((bossLastHp - hp) / 40f, 0.3f, 1.4f);
                bossShake = bossRect.DOShakeAnchorPos(0.25f, new Vector2(10f, 6f) * power, 40, 90f, false, true)
                    .SetUpdate(true).OnComplete(() => bossRect.anchoredPosition = bossHome);
            }
            bossLastHp = hp;

            bossShown = Mathf.Lerp(bossShown, pct, 1f - Mathf.Exp(-14f * dt));
            if (pct >= bossLagValue) bossLagValue = pct;
            else if (Time.unscaledTime > bossLagHold) bossLagValue = Mathf.MoveTowards(bossLagValue, pct, 0.5f * dt);
            bossFill.Fill = bossShown;
            bossLag.Fill = bossLagValue;

            bool enraged = pct <= 0.5f; // KaijuSpecials.enrageHpRatio — ครึ่งหลังไคจูดุขึ้น
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
            bossFill.color = enraged ? Color.Lerp(danger, new Color(1f, 0.45f, 0.1f), blink * 0.5f) : new Color(0.95f, 0.25f, 0.2f);
            bossRage.alpha = enraged ? Mathf.Lerp(0.5f, 1f, blink) : 0f;
            bossFlashT = Mathf.MoveTowards(bossFlashT, 0f, dt * 4f);
            bossFlash.color = new Color(1f, 1f, 1f, bossFlashT * 0.45f);

            int hpInt = Mathf.CeilToInt(hp);
            if (hpInt != bossShownHp) { bossShownHp = hpInt; bossHp.text = $"{hpInt} <size=60%><alpha=#88>/ {Mathf.CeilToInt(max)}"; }
        }

        private void BuildBoss(Transform parent)
        {
            var go = new GameObject("BossBar", typeof(RectTransform), typeof(CanvasGroup));
            bossRect = (RectTransform)go.transform;
            bossRect.SetParent(parent, false);
            bossRect.anchorMin = bossRect.anchorMax = new Vector2(0.5f, 1f);
            bossRect.pivot = new Vector2(0.5f, 1f);
            bossRect.sizeDelta = new Vector2(BossBarWidth + 40f, 110f);
            bossRect.anchoredPosition = bossHome = new Vector2(0f, -28f);
            bossGroup = go.GetComponent<CanvasGroup>();
            bossGroup.alpha = 0f;
            Transform t = bossRect;

            const float x = 20f, y = 14f, h = 30f;
            Shape(t, "Back", new Vector2(0f, 0f), new Vector2(BossBarWidth + 40f, 62f), panel);
            Shape(t, "Edge", new Vector2(10f, 60f), new Vector2(BossBarWidth * 0.5f, 3f), danger);
            var name = Tmp(t, "Name", headFont, 34f, TextAlignmentOptions.BottomLeft, new Vector2(24f, 62f), new Vector2(500f, 44f));
            name.text = "PANDA KAIJU";
            name.characterSpacing = 10f;
            name.color = danger;
            bossRage = Tmp(t, "Rage", bodyFont, 16f, TextAlignmentOptions.BottomLeft, new Vector2(300f, 68f), new Vector2(200f, 24f));
            bossRage.text = "ENRAGED";
            bossRage.characterSpacing = 6f;
            bossRage.color = warning;
            bossRage.alpha = 0f;
            bossHp = Tmp(t, "Hp", headFont, 32f, TextAlignmentOptions.BottomRight, new Vector2(BossBarWidth - 240f, 62f), new Vector2(280f, 44f));
            bossHp.richText = true;

            Shape(t, "BarBack", new Vector2(x, y), new Vector2(BossBarWidth, h), new Color(1f, 1f, 1f, 0.07f));
            bossLag = Shape(t, "BarLag", new Vector2(x, y), new Vector2(BossBarWidth, h), new Color(1f, 0.9f, 0.8f, 0.85f));
            bossFill = Shape(t, "BarFill", new Vector2(x, y), new Vector2(BossBarWidth, h), danger);
            const int seg = 20;
            float span = BossBarWidth - h * 0.25f;
            for (int s = 1; s < seg; s++)
                Shape(t, "Gap", new Vector2(x + span * s / seg - 1.5f, y), new Vector2(3f + h * 0.25f, h), new Color(0.03f, 0.06f, 0.09f, 0.9f));
            Shape(t, "Half", new Vector2(x + span * 0.5f - 2f, y - 6f), new Vector2(4f + h * 0.25f, h + 12f), new Color(1f, 1f, 1f, 0.7f)); // ขีดครึ่ง = เริ่มดุ
            bossFlash = Shape(t, "Flash", new Vector2(x, y), new Vector2(BossBarWidth, h), Color.clear);
        }

        // ================================================================ ชนะ / แพ้

        private void TickResult()
        {
            MatchSession session = MatchSession.Current;
            bool ended = session != null && session.Phase == MatchPhase.Ended;
            if (ended && !resultShown) ShowResult(session.Result);
            if (!resultShown) return;
            bool host = NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
            hostButtons.SetActive(host);
            clientButtons.SetActive(!host);
            resultWait.gameObject.SetActive(!host);
        }

        private void ShowResult(MatchResult r)
        {
            resultShown = true;
            UiFocus.Push(this); // เมาส์เป็นของปุ่ม หุ่นไม่ขยับตามคลิก
            Color tone = r.victory ? accent : danger;
            resultTitle.text = r.victory ? "VICTORY" : "DEFEAT";
            resultTitle.color = tone;
            resultBand.color = new Color(tone.r * 0.25f, tone.g * 0.25f, tone.b * 0.25f, 0.85f);
            resultSub.text = r.victory ? "KAIJU NEUTRALIZED" : "ALL LIMBS LOST — ROBOT DESTROYED";
            int sec = Mathf.Max(0, Mathf.FloorToInt(r.time));
            resultTime.text = $"TIME  {sec / 60:00}:{sec % 60:00}";
            if (audioSource) audioSource.PlayOneShot(r.victory ? winClip : loseClip, alarmVolume * 1.6f);

            result.gameObject.SetActive(true);
            result.alpha = 0f;
            resultTitleRect.localScale = Vector3.one * 1.8f;
            resultTitle.characterSpacing = 60f;
            resultSeq = DOTween.Sequence().SetUpdate(true);
            resultSeq.Append(result.DOFade(1f, 0.35f).SetEase(Ease.OutCubic));
            resultSeq.Join(resultTitleRect.DOScale(Vector3.one, 0.55f).SetEase(Ease.OutBack));
            resultSeq.Join(DOTween.To(() => resultTitle.characterSpacing, v => resultTitle.characterSpacing = v, 24f, 0.6f).SetEase(Ease.OutCubic));
        }

        private void BuildResult()
        {
            var canvasGo = new GameObject("ArenaResult_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30; // เหนือ HUD ใต้เมนู ESC (40)
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            result = canvasGo.GetComponent<CanvasGroup>();
            Transform c = canvasGo.transform;

            Image dim = New<Image>(c, "Dim", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.color = new Color(0f, 0f, 0f, 0.7f);
            dim.raycastTarget = true; // บังคลิกไม่ให้ทะลุลงเกม
            resultBand = New<HudShape>(c, "Band", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -40f), new Vector2(0f, 190f));
            resultBand.slant = 0f;
            var lineTop = New<HudShape>(c, "LineTop", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 188f), new Vector2(0f, 192f));
            lineTop.slant = 0f;
            lineTop.color = new Color(1f, 1f, 1f, 0.25f);
            var lineBottom = New<HudShape>(c, "LineBottom", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -42f), new Vector2(0f, -38f));
            lineBottom.slant = 0f;
            lineBottom.color = new Color(1f, 1f, 1f, 0.25f);

            resultTitle = Tmp(c, "Title", headFont, 150f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(1600f, 180f));
            resultTitleRect = resultTitle.rectTransform;
            Center(resultTitleRect, new Vector2(0f, 90f));
            resultTitle.fontStyle = FontStyles.Bold;
            resultSub = Tmp(c, "Sub", headFont, 36f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(1400f, 50f));
            resultSub.characterSpacing = 10f;
            Center(resultSub.rectTransform, new Vector2(0f, -5f));
            resultSub.color = Color.white;
            resultTime = Tmp(c, "Time", bodyFont, 22f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(600f, 30f));
            Center(resultTime.rectTransform, new Vector2(0f, -80f));
            resultTime.characterSpacing = 8f;
            resultTime.color = new Color(1f, 1f, 1f, 0.7f);

            hostButtons = new GameObject("HostButtons", typeof(RectTransform));
            hostButtons.transform.SetParent(c, false);
            Center((RectTransform)hostButtons.transform, new Vector2(0f, -190f));
            AddButton(hostButtons.transform, "PLAY AGAIN", new Vector2(-180f, 0f), accent, () =>
            {
                if (MatchSession.Current != null) MatchSession.Current.ServerRestart();
            });
            AddButton(hostButtons.transform, "MAIN MENU", new Vector2(180f, 0f), new Color(0.75f, 0.82f, 0.88f), () =>
            {
                if (MatchSession.Current != null) MatchSession.Current.ServerReturnEveryoneToMenu();
            });

            clientButtons = new GameObject("ClientButtons", typeof(RectTransform));
            clientButtons.transform.SetParent(c, false);
            Center((RectTransform)clientButtons.transform, new Vector2(0f, -190f));
            AddButton(clientButtons.transform, "LEAVE", Vector2.zero, new Color(0.75f, 0.82f, 0.88f), () =>
            {
                if (SessionService.Instance != null) _ = SessionService.Instance.LeaveAsync();
            });
            resultWait = Tmp(c, "Wait", bodyFont, 18f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(800f, 30f));
            resultWait.characterSpacing = 6f;
            Center(resultWait.rectTransform, new Vector2(0f, -260f));
            resultWait.text = "WAITING FOR HOST...";
            resultWait.color = new Color(1f, 1f, 1f, 0.6f);

            canvasGo.SetActive(false);
        }

        // ปุ่มทรงเอียงแบบการ์ด HUD — ชี้แล้วสว่าง กดแล้วเด้ง
        private void AddButton(Transform parent, string label, Vector2 pos, Color tone, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Center(rt, pos);
            rt.sizeDelta = new Vector2(320f, 70f);
            Shape(rt, "Edge", new Vector2(-3f, -3f), new Vector2(326f, 76f), tone);
            HudShape back = Shape(rt, "Back", Vector2.zero, new Vector2(320f, 70f), panel);
            back.raycastTarget = true;
            TMP_Text text = Tmp(rt, "Label", headFont, 30f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(320f, 70f));
            text.text = label;
            text.characterSpacing = 8f;
            text.color = tone;
            var button = go.AddComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.colorMultiplier = 2f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                rt.DOKill();
                rt.localScale = Vector3.one;
                rt.DOPunchScale(Vector3.one * 0.06f, 0.2f, 8, 0.8f).SetUpdate(true);
                onClick();
            });
        }

        // ================================================================ ตัวช่วย

        private static T New<T>(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            T g = go.GetComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        // วางแบบมุมซ้ายล่างของการ์ด (pivot ซ้ายล่าง) ตำแหน่ง/ขนาดเป็นพิกเซลอ้างอิง
        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Center(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
        }

        private static HudShape Shape(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            HudShape s = New<HudShape>(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Place(s.rectTransform, pos, size);
            s.color = color;
            return s;
        }

        private static TMP_Text Tmp(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions align, Vector2 pos, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (font) text.font = font;
            text.fontSize = size;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;
            Place(text.rectTransform, pos, box);
            return text;
        }

        private Text Legacy(Transform parent, string name, int size, TextAnchor align, Vector2 pos, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = thaiFont;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            Place(text.rectTransform, pos, box);
            return text;
        }

        // ขอบจอทึบ กลางจอใส (วงรีตามสัดส่วนจอ)
        private static Texture2D VignetteTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HudVignette" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 1f, d)), 1.4f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // เสียงบี๊บสังเคราะห์ (ไม่ต้องมีไฟล์เสียง): โน้ตต่อกัน คลื่นกึ่งสี่เหลี่ยม เสียงเครื่องจักร
        private static AudioClip Tones(string name, float[] notes, float noteLength)
        {
            const int rate = 44100;
            int per = Mathf.RoundToInt(rate * noteLength);
            var data = new float[per * notes.Length];
            for (int n = 0; n < notes.Length; n++)
                for (int s = 0; s < per; s++)
                {
                    float t = (float)s / rate;
                    float env = Mathf.Min(1f, s / (rate * 0.004f)) * Mathf.Pow(1f - (float)s / per, 1.5f);
                    float sine = Mathf.Sin(2f * Mathf.PI * notes[n] * t);
                    data[n * per + s] = (Mathf.Sign(sine) * 0.35f + sine * 0.65f) * env * 0.8f;
                }
            AudioClip clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
