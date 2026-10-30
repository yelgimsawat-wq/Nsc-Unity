using System.Collections;
using DG.Tweening;
using Nsc.BoneLimb;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// หน้าโหลดตอนเปลี่ยนซีน (สไตล์หุ่นยนต์ เข้าชุดกับ HUD) — สร้างตัวเองตอนเปิดเกม อยู่ข้ามซีน (DontDestroyOnLoad)
/// - ซีนที่โหลดผ่าน Netcode (Host กดเริ่มแมพ / รีสตาร์ต / กลับเมนูทั้งห้อง): ฟัง NetworkSceneManager.OnSceneEvent เอง
///   ขึ้นทุกเครื่องพร้อมกัน ปิดเมื่อทุกเครื่องโหลดเสร็จ (LoadEventCompleted)
/// - ซีนที่โหลดเครื่องเดียว (ออกห้อง/หลุดกลับเมนู): เรียก LoadingScreen.LoadLocal(ชื่อซีน)
/// ขึ้นอย่างน้อย minShowTime วิ ไม่วาบ — ระหว่างซีนใหม่เปิดตัว (เฟรมค้าง) จอจะหยุดนิ่งแต่ยังบังภาพไว้
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    private static LoadingScreen instance;

    private const float MinShowTime = 1.4f, FadeIn = 0.25f, FadeOut = 0.45f, NetTimeout = 20f;

    private static readonly Color Accent = new Color(0.25f, 0.9f, 1f);
    private static readonly Color Danger = new Color(1f, 0.2f, 0.22f);
    private static readonly Color Back = new Color(0.015f, 0.025f, 0.04f, 1f);

    private static readonly string[] StatusLines =
    {
        "SYNCING LIMB CONTROLLERS", "CALIBRATING SERVOS", "LINKING PILOTS", "CHARGING REACTOR CORE",
        "LOADING ARENA DATA", "WARMING UP HYDRAULICS", "SCANNING HOSTILES",
    };
    private static readonly string[] Tips =
    {
        "HOLD SHIFT (ARM) TO GUARD — BLOCK RIGHT BEFORE A HIT TO PARRY",
        "KNOCKED DOWN? EVERY PILOT MUST PRESS Q TO STAND UP",
        "LIMB DETACHED? WAIT A MOMENT, THEN HOLD R TO PULL IT BACK",
        "GROUND SLAMS HIT EVERY LIMB WHILE YOU ARE DOWN — GET UP FAST",
        "BOTH LEG PILOTS HOLD F = BRACE, IMMUNE TO KNOCKDOWN",
        "BOTH ARMS HIT THE SAME TARGET AT ONCE = DOUBLE IMPACT BONUS",
        "STEP LEFT-RIGHT IN RHYTHM TO WALK FASTER",
    };

    private CanvasGroup group;
    private RectTransform ringOuter, ringInner, scan;
    private HudShape bar, barGlow;
    private TMP_Text title, percent, status, sceneLabel;
    private TMP_Text tip;
    private Tween fade;

    private bool showing, waitingNetwork;
    private float shownAt, progress, display, nextStatus, nextTip;
    private int statusIndex, tipIndex;
    private AsyncOperation op;
    private NetworkSceneManager hooked;

    // ================================================================ ใช้งาน

    /// <summary>เปิดหน้าโหลด (ถ้ายังไม่เปิด) พร้อมชื่อปลายทาง</summary>
    public static void Show(string sceneName) => Instance.Open(sceneName);

    /// <summary>โหลดซีนเครื่องนี้เครื่องเดียวแบบมีหน้าโหลด (แทน SceneManager.LoadScene)</summary>
    public static void LoadLocal(string sceneName) => Instance.StartCoroutine(Instance.LoadLocalRoutine(sceneName));

    private static LoadingScreen Instance
    {
        get
        {
            if (instance == null) Create();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("[LoadingScreen]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LoadingScreen>();
    }

    private static TMP_FontAsset headFont;

    private void Awake()
    {
        // ฟอนต์หัวข้อเดียวกับเมนู/HUD (Oswald) — เมนูโหลดไว้ในหน่วยความจำแล้วตอนเปิดเกม ไม่มี = ฟอนต์ตั้งต้นของ TMP
        foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (f.name.StartsWith("Oswald")) { headFont = f; break; }
        Build();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (hooked != null) hooked.OnSceneEvent -= OnSceneEvent;
        if (fade != null && fade.IsActive()) fade.Kill(false);
        if (instance == this) instance = null;
    }

    // ================================================================ เปิด / ปิด

    private void Open(string sceneName)
    {
        sceneLabel.text = DisplayName(sceneName);
        if (showing) return;
        showing = true;
        shownAt = Time.unscaledTime;
        progress = display = 0f;
        nextStatus = nextTip = 0f;
        tipIndex = Random.Range(0, Tips.Length);
        group.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        if (fade != null && fade.IsActive()) fade.Kill(false);
        fade = group.DOFade(1f, FadeIn).SetEase(Ease.OutCubic).SetUpdate(true);
    }

    private void Close()
    {
        if (!showing) return;
        showing = waitingNetwork = false;
        op = null;
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        float wait = MinShowTime - (Time.unscaledTime - shownAt);
        progress = 1f;
        if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
        yield return new WaitForSecondsRealtime(0.25f); // ให้หลอดวิ่งถึง 100% ก่อน
        if (showing) yield break; // มีการโหลดใหม่ระหว่างรอ
        if (fade != null && fade.IsActive()) fade.Kill(false);
        group.blocksRaycasts = false;
        fade = group.DOFade(0f, FadeOut).SetEase(Ease.InCubic).SetUpdate(true)
            .OnComplete(() => { if (!showing) group.gameObject.SetActive(false); });
    }

    private IEnumerator LoadLocalRoutine(string sceneName)
    {
        Open(sceneName);
        yield return new WaitForSecondsRealtime(FadeIn); // ให้จอดำทับก่อนเริ่มโหลด (โหลดแล้วเฟรมค้าง)
        op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (op == null) { Close(); yield break; }
        while (!op.isDone) yield return null;
        Close();
    }

    // ================================================================ Netcode

    private void HookNetwork()
    {
        NetworkManager nm = NetworkManager.Singleton;
        NetworkSceneManager sm = nm != null ? nm.SceneManager : null;
        if (sm == hooked) return;
        if (hooked != null) hooked.OnSceneEvent -= OnSceneEvent;
        hooked = sm;
        if (hooked != null) hooked.OnSceneEvent += OnSceneEvent;
    }

    private void OnSceneEvent(SceneEvent e)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || e.LoadSceneMode != LoadSceneMode.Single) return;
        switch (e.SceneEventType)
        {
            case SceneEventType.Load when e.ClientId == nm.LocalClientId:
                Open(e.SceneName);
                waitingNetwork = true;
                op = e.AsyncOperation;
                break;
            case SceneEventType.LoadComplete when e.ClientId == nm.LocalClientId:
                progress = Mathf.Max(progress, 0.92f); // เครื่องเราเสร็จ รอเพื่อน
                break;
            case SceneEventType.LoadEventCompleted:
                Close();
                break;
        }
    }

    // ซีนเปลี่ยนแบบไม่ผ่านหน้าโหลด (เช่นโหลดตรงจากโค้ดอื่น) ก็ปิดให้ — โหลดผ่านเน็ตรอ LoadEventCompleted
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (showing && !waitingNetwork && op == null && mode == LoadSceneMode.Single) Close();
    }

    // ================================================================ ต่อเฟรม

    private void Update()
    {
        HookNetwork();
        if (!group.gameObject.activeSelf) return;

        float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        if (showing && waitingNetwork && t - shownAt > NetTimeout) Close(); // เพื่อนหลุดกลางโหลด — ไม่ค้างจอดำ

        // หลอด: จริงจาก AsyncOperation (0..0.9) + ค่อยๆ คืบเองไม่ให้นิ่ง ไม่เกิน 90% จนกว่าจะเสร็จ
        float real = op != null ? Mathf.Clamp01(op.progress / 0.9f) * 0.9f : 0f;
        float creep = 0.9f * (1f - Mathf.Exp(-(t - shownAt) * 0.6f));
        progress = Mathf.Max(progress, Mathf.Max(real, Mathf.Min(creep, 0.88f)));
        display = Mathf.MoveTowards(display, progress, dt * 1.6f);
        bar.Fill = display;
        barGlow.Fill = display;
        barGlow.color = new Color(Accent.r, Accent.g, Accent.b, 0.25f + 0.2f * Mathf.Sin(t * 6f));
        percent.text = Mathf.FloorToInt(display * 100f) + "%";

        ringOuter.localRotation = Quaternion.Euler(0f, 0f, -t * 90f);
        ringInner.localRotation = Quaternion.Euler(0f, 0f, t * 160f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3f);
        title.color = Color.Lerp(Color.white, Accent, pulse * 0.6f);
        scan.anchoredPosition = new Vector2(0f, Mathf.Repeat(t * 260f, 1300f) - 650f);

        if (t >= nextStatus)
        {
            nextStatus = t + 0.7f;
            statusIndex = (statusIndex + 1) % StatusLines.Length;
            status.text = "> " + StatusLines[statusIndex] + "...";
        }
        if (t >= nextTip)
        {
            nextTip = t + 3.5f;
            tipIndex = (tipIndex + 1) % Tips.Length;
            tip.text = "TIP  ·  " + Tips[tipIndex];
        }
    }

    private static string DisplayName(string scene) => scene switch
    {
        "RobotArena" => "PVP  //  ARENA",
        "-MenuNOk" => "MAIN MENU",
        _ => scene.ToUpperInvariant(),
    };

    // ================================================================ สร้าง UI

    private void Build()
    {
        var canvasGo = new GameObject("LoadingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = canvasGo.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        Transform c = canvasGo.transform;

        // พื้นดำ + เส้นตาราง + เส้นสแกนวิ่ง
        Stretch(Graphic<Image>(c, "Back", Back));
        for (int i = 1; i < 12; i++)
        {
            Image line = Graphic<Image>(c, "GridV", new Color(Accent.r, Accent.g, Accent.b, 0.035f));
            Place(line.rectTransform, new Vector2(i / 12f, 0.5f), Vector2.zero, new Vector2(2f, 2000f));
        }
        for (int i = 1; i < 7; i++)
        {
            Image line = Graphic<Image>(c, "GridH", new Color(Accent.r, Accent.g, Accent.b, 0.035f));
            Place(line.rectTransform, new Vector2(0.5f, i / 7f), Vector2.zero, new Vector2(4000f, 2f));
        }
        Image scanLine = Graphic<Image>(c, "Scan", new Color(Accent.r, Accent.g, Accent.b, 0.06f));
        scan = scanLine.rectTransform;
        Place(scan, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 90f));

        // มุมกรอบ 4 มุม
        foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) })
        {
            Vector2 inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);
            Image h = Graphic<Image>(c, "CornerH", Accent);
            Place(h.rectTransform, corner, inward * new Vector2(110f, 40f), new Vector2(140f, 4f));
            Image v = Graphic<Image>(c, "CornerV", Accent);
            Place(v.rectTransform, corner, inward * new Vector2(40f, 110f), new Vector2(4f, 140f));
        }

        // วงหมุนกลางจอ: วงนอกช่องๆ หมุนตามเข็ม วงในหมุนสวน
        ringOuter = Ring(c, "RingOuter", 24, 150f, new Vector2(16f, 34f), Accent, 0.85f);
        ringInner = Ring(c, "RingInner", 8, 92f, new Vector2(26f, 12f), Danger, 1f);
        Place(ringOuter, new Vector2(0.5f, 0.56f), Vector2.zero, Vector2.zero);
        Place(ringInner, new Vector2(0.5f, 0.56f), Vector2.zero, Vector2.zero);

        title = Tmp(c, "Title", 96f, new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(900f, 120f));
        title.text = "LOADING";
        title.characterSpacing = 22f;
        title.fontStyle = FontStyles.Bold;

        sceneLabel = Tmp(c, "Scene", 34f, new Vector2(0.5f, 0.56f), new Vector2(0f, -230f), new Vector2(900f, 50f));
        sceneLabel.color = Danger;
        sceneLabel.characterSpacing = 12f;

        // หลอดโหลดแบบ HUD (เอียง + แบ่งช่อง)
        const float barW = 980f, barH = 26f, barY = 210f;
        HudShape frame = Shape(c, "BarFrame", new Vector2(barW + 8f, barH + 8f), new Color(1f, 1f, 1f, 0.15f));
        Place(frame.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, barY), new Vector2(barW + 8f, barH + 8f));
        HudShape barBack = Shape(c, "BarBack", new Vector2(barW, barH), new Color(0f, 0f, 0f, 0.8f));
        Place(barBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, barY), new Vector2(barW, barH));
        barGlow = Shape(c, "BarGlow", new Vector2(barW, barH + 14f), Accent);
        Place(barGlow.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, barY), new Vector2(barW, barH + 14f));
        bar = Shape(c, "Bar", new Vector2(barW, barH), Accent);
        Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, barY), new Vector2(barW, barH));
        bar.Fill = barGlow.Fill = 0f;
        const int segments = 24;
        for (int s = 1; s < segments; s++)
        {
            HudShape gap = Shape(c, "Gap", new Vector2(4f + barH * 0.25f, barH), Back);
            float x = -barW * 0.5f + (barW - barH * 0.25f) * s / segments + (4f + barH * 0.25f) * 0.5f - 2f;
            Place(gap.rectTransform, new Vector2(0.5f, 0f), new Vector2(x, barY), new Vector2(4f + barH * 0.25f, barH));
        }

        percent = Tmp(c, "Percent", 40f, new Vector2(0.5f, 0f), new Vector2(barW * 0.5f - 50f, barY + 48f), new Vector2(160f, 50f));
        percent.alignment = TextAlignmentOptions.Right;
        status = Tmp(c, "Status", 20f, new Vector2(0.5f, 0f), new Vector2(-barW * 0.5f + 300f, barY + 44f), new Vector2(600f, 30f));
        status.alignment = TextAlignmentOptions.Left;
        status.color = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
        status.characterSpacing = 4f;

        tip = Tmp(c, "Tip", 22f, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1600f, 40f));
        tip.characterSpacing = 6f;
        tip.color = new Color(1f, 1f, 1f, 0.7f);

        canvasGo.SetActive(false);
    }

    private static RectTransform Ring(Transform parent, string name, int count, float radius, Vector2 size, Color color, float fillShare)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        for (int i = 0; i < count; i++)
        {
            float a = i * 360f / count;
            HudShape seg = Shape(rt, "Seg", size, color);
            seg.slant = 0.3f;
            seg.rectTransform.anchorMin = seg.rectTransform.anchorMax = seg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            seg.rectTransform.sizeDelta = size;
            seg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, a);
            seg.rectTransform.anchoredPosition = Quaternion.Euler(0f, 0f, a) * Vector3.up * radius;
            seg.color = new Color(color.r, color.g, color.b, color.a * Mathf.Lerp(0.15f, fillShare, (float)i / count)); // หางจาง = รู้สึกหมุน
        }
        return rt;
    }

    private static T Graphic<T>(Transform parent, string name, Color color) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        go.transform.SetParent(parent, false);
        T g = go.GetComponent<T>();
        g.color = color;
        g.raycastTarget = false;
        return g;
    }

    private static HudShape Shape(Transform parent, string name, Vector2 size, Color color)
    {
        HudShape s = Graphic<HudShape>(parent, name, color);
        s.rectTransform.sizeDelta = size;
        return s;
    }

    private static TMP_Text Tmp(Transform parent, string name, float size, Vector2 anchor, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (headFont) text.font = headFont;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.color = Color.white;
        Place(text.rectTransform, anchor, pos, box);
        return text;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(Graphic g)
    {
        RectTransform rt = g.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
