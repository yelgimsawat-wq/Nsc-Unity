using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// ระดับความละเอียดของภาพ — เรียงจากกากสุดไปสวยสุด
/// </summary>
public enum GraphicsPreset
{
    Potato = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Ultra = 4
}

/// <summary>
/// GraphicsQualityManager.cs
///
/// ⚠️ ทำไมต้องมีไฟล์นี้ — โปรเจกต์นี้มี Quality Level แค่ 2 อัน (Mobile / PC)
/// และ Mobile ถูก exclude จาก Standalone ด้วย พอบิลด์เป็น PC แล้ว
/// QualitySettings.names เหลือชื่อเดียว → QualitySettings.SetQualityLevel()
/// เลื่อนยังไงก็ไม่มีอะไรเปลี่ยน สไลเดอร์ในหน้า Settings เลย "ขยับได้แต่ไม่มีผล"
///
/// ตัวนี้เลยไม่แตะ Quality Level เลย แต่ยัดค่าจริงลง QualitySettings + URP asset
/// + กล้องทุกตัว ทำให้ผู้เล่นเห็นความต่างทันทีที่ลากสไลเดอร์ (ชัดสุดคือ Render Scale)
///
/// เกิดเองอัตโนมัติตั้งแต่ก่อนโหลดฉากแรก ไม่ต้องลากใส่ scene ไหนทั้งนั้น
/// </summary>
public class GraphicsQualityManager : MonoBehaviour
{
    // ================================================================
    //  SINGLETON / BOOTSTRAP
    // ================================================================

    public const string PrefsKey = "GraphicsPreset";

    public static GraphicsQualityManager Instance { get; private set; }

    /// <summary>ชื่อที่โชว์บน UI — index ตรงกับ GraphicsPreset</summary>
    public static readonly string[] PresetNames = { "Potato", "Low", "Medium", "High", "Ultra" };

    public static int PresetCount => PresetNames.Length;

    public GraphicsPreset CurrentPreset { get; private set; } = GraphicsPreset.High;

    /// <summary>ยิงทุกครั้งที่ระดับภาพเปลี่ยน — UI เอาไปอัปเดตป้ายได้</summary>
    public event Action<GraphicsPreset> OnPresetChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        GameObject go = new GameObject("[GraphicsQualityManager]");
        go.AddComponent<GraphicsQualityManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // โหลดค่าที่ผู้เล่นเลือกไว้ตั้งแต่เปิดเกม ไม่ต้องรอให้เปิดหน้า Settings ก่อน
        int saved = PlayerPrefs.GetInt(PrefsKey, (int)GraphicsPreset.High);
        Apply((GraphicsPreset)Mathf.Clamp(saved, 0, PresetCount - 1), save: false);

        // กล้องผู้เล่นเกิดทีหลังตอน Netcode spawn — ดักตอน URP จะเรนเดอร์กล้องแต่ละตัว
        // เลยจับได้ครบทุกตัวไม่ว่าจะเกิดตอนไหน โดยไม่ต้องไล่ FindObjects ทุกเฟรม
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Instance == this) Instance = null;
    }

    /// <summary>กล้องที่ตั้งค่าไปแล้ว — กันไม่ให้ยัดค่าซ้ำทุกเฟรม</summary>
    private readonly HashSet<Camera> configuredCameras = new HashSet<Camera>();

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam == null || configuredCameras.Contains(cam)) return;

        ConfigureCamera(cam, Presets[(int)CurrentPreset]);
        configuredCameras.Add(cam);
    }

    /// <summary>เปลี่ยนฉาก = กล้องเก่าตายหมด ล้างรายชื่อทิ้งไม่ให้ HashSet บวมไปเรื่อยๆ</summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        configuredCameras.Clear();
        ApplyToCameras(Presets[(int)CurrentPreset]);
    }

    // ================================================================
    //  ตารางค่าของแต่ละระดับ
    // ================================================================

    private struct PresetData
    {
        // --- URP asset ---
        public float renderScale;          // ตัวที่เห็นผลชัดที่สุด ต่ำ = ภาพแตก + เฟรมพุ่ง
        public int msaaSamples;            // 1 / 2 / 4 / 8
        public bool hdr;
        public bool opaqueTexture;
        public bool depthTexture;
        public int mainShadowRes;          // 256 / 512 / 1024 / 2048 / 4096
        public bool softShadows;
        public int softShadowQuality;      // URP SoftShadowQuality: 1=Low 2=Medium 3=High
        public bool additionalLightShadows;
        public int maxAdditionalLights;

        // --- QualitySettings ---
        public UnityEngine.ShadowQuality shadows;
        public float shadowDistance;
        public int shadowCascades;
        public int pixelLightCount;
        public float lodBias;
        public int textureMipmapLimit;     // 0 = เต็มความละเอียด, ยิ่งมากยิ่งเบลอ
        public AnisotropicFiltering aniso;
        public bool softParticles;
        public bool realtimeReflectionProbes;
        public int particleRaycastBudget;
        public SkinWeights skinWeights;

        // --- กล้อง ---
        public bool postProcessing;
        public AntialiasingMode cameraAA;
    }

    private static readonly PresetData[] Presets =
    {
        // ---------------- Potato : เอาเฟรมเป็นหลัก ภาพกากได้ไม่ว่า ----------------
        new PresetData
        {
            renderScale = 0.5f, msaaSamples = 1, hdr = false,
            opaqueTexture = false, depthTexture = false,
            mainShadowRes = 256, softShadows = false, softShadowQuality = 1,
            additionalLightShadows = false, maxAdditionalLights = 0,
            shadows = UnityEngine.ShadowQuality.Disable, shadowDistance = 10f, shadowCascades = 1,
            pixelLightCount = 0, lodBias = 0.35f, textureMipmapLimit = 2,
            aniso = AnisotropicFiltering.Disable, softParticles = false,
            realtimeReflectionProbes = false, particleRaycastBudget = 16,
            skinWeights = SkinWeights.OneBone,
            postProcessing = false, cameraAA = AntialiasingMode.None
        },

        // ---------------- Low ----------------
        new PresetData
        {
            renderScale = 0.7f, msaaSamples = 1, hdr = false,
            opaqueTexture = false, depthTexture = false,
            mainShadowRes = 512, softShadows = false, softShadowQuality = 1,
            additionalLightShadows = false, maxAdditionalLights = 2,
            shadows = UnityEngine.ShadowQuality.HardOnly, shadowDistance = 20f, shadowCascades = 1,
            pixelLightCount = 1, lodBias = 0.6f, textureMipmapLimit = 1,
            aniso = AnisotropicFiltering.Disable, softParticles = false,
            realtimeReflectionProbes = false, particleRaycastBudget = 64,
            skinWeights = SkinWeights.TwoBones,
            postProcessing = false, cameraAA = AntialiasingMode.None
        },

        // ---------------- Medium ----------------
        new PresetData
        {
            renderScale = 0.85f, msaaSamples = 2, hdr = true,
            opaqueTexture = false, depthTexture = true,
            mainShadowRes = 1024, softShadows = false, softShadowQuality = 1,
            additionalLightShadows = true, maxAdditionalLights = 4,
            shadows = UnityEngine.ShadowQuality.HardOnly, shadowDistance = 40f, shadowCascades = 2,
            pixelLightCount = 2, lodBias = 1f, textureMipmapLimit = 0,
            aniso = AnisotropicFiltering.Enable, softParticles = false,
            realtimeReflectionProbes = true, particleRaycastBudget = 256,
            skinWeights = SkinWeights.FourBones,
            postProcessing = true, cameraAA = AntialiasingMode.FastApproximateAntialiasing
        },

        // ---------------- High : ค่าเริ่มต้น ----------------
        new PresetData
        {
            renderScale = 1f, msaaSamples = 4, hdr = true,
            opaqueTexture = true, depthTexture = true,
            mainShadowRes = 2048, softShadows = true, softShadowQuality = 2,
            additionalLightShadows = true, maxAdditionalLights = 8,
            shadows = UnityEngine.ShadowQuality.All, shadowDistance = 70f, shadowCascades = 4,
            pixelLightCount = 4, lodBias = 1.5f, textureMipmapLimit = 0,
            aniso = AnisotropicFiltering.ForceEnable, softParticles = true,
            realtimeReflectionProbes = true, particleRaycastBudget = 1024,
            skinWeights = SkinWeights.FourBones,
            postProcessing = true, cameraAA = AntialiasingMode.SubpixelMorphologicalAntiAliasing
        },

        // ---------------- Ultra ----------------
        new PresetData
        {
            renderScale = 1f, msaaSamples = 8, hdr = true,
            opaqueTexture = true, depthTexture = true,
            mainShadowRes = 4096, softShadows = true, softShadowQuality = 3,
            additionalLightShadows = true, maxAdditionalLights = 16,
            shadows = UnityEngine.ShadowQuality.All, shadowDistance = 120f, shadowCascades = 4,
            pixelLightCount = 8, lodBias = 2.5f, textureMipmapLimit = 0,
            aniso = AnisotropicFiltering.ForceEnable, softParticles = true,
            realtimeReflectionProbes = true, particleRaycastBudget = 4096,
            skinWeights = SkinWeights.Unlimited,
            postProcessing = true, cameraAA = AntialiasingMode.SubpixelMorphologicalAntiAliasing
        }
    };

    // ================================================================
    //  PUBLIC API
    // ================================================================

    /// <summary>ใช้ระดับภาพนี้ทันที แล้วเซฟลง PlayerPrefs</summary>
    public void Apply(GraphicsPreset preset, bool save = true)
    {
        int index = Mathf.Clamp((int)preset, 0, PresetCount - 1);
        CurrentPreset = (GraphicsPreset)index;

        PresetData data = Presets[index];

        ApplyToQualitySettings(data);
        ApplyToUrpAsset(data);
        ApplyToCameras(data);

        if (save)
        {
            PlayerPrefs.SetInt(PrefsKey, index);
            PlayerPrefs.Save();
        }

        OnPresetChanged?.Invoke(CurrentPreset);

        Debug.Log($"[Graphics] ใช้ระดับภาพ {PresetNames[index]} — " +
                  $"RenderScale {data.renderScale:0.00}, MSAA {data.msaaSamples}x, " +
                  $"Shadow {data.shadows} @{data.shadowDistance}m, PostFX {data.postProcessing}");
    }

    /// <summary>รับค่าสไลเดอร์ 0-1 แล้วแปลงเป็นระดับภาพ — ใช้กับสไลเดอร์ช่วงไหนก็ได้</summary>
    public void ApplyNormalized(float t)
    {
        int index = Mathf.RoundToInt(Mathf.Clamp01(t) * (PresetCount - 1));
        Apply((GraphicsPreset)index);
    }

    /// <summary>ค่าสไลเดอร์ 0-1 ของระดับปัจจุบัน — เอาไปเซ็ตกลับให้สไลเดอร์ตอนโหลด</summary>
    public float CurrentNormalized => (int)CurrentPreset / (float)(PresetCount - 1);

    public string CurrentPresetName => PresetNames[(int)CurrentPreset];

    // ================================================================
    //  ตัวยัดค่าจริง
    // ================================================================

    private void ApplyToQualitySettings(PresetData d)
    {
        QualitySettings.shadows = d.shadows;
        QualitySettings.shadowDistance = d.shadowDistance;
        QualitySettings.shadowCascades = d.shadowCascades;
        QualitySettings.pixelLightCount = d.pixelLightCount;
        QualitySettings.lodBias = d.lodBias;
        QualitySettings.globalTextureMipmapLimit = d.textureMipmapLimit;
        QualitySettings.anisotropicFiltering = d.aniso;
        QualitySettings.softParticles = d.softParticles;
        QualitySettings.realtimeReflectionProbes = d.realtimeReflectionProbes;
        QualitySettings.particleRaycastBudget = d.particleRaycastBudget;
        QualitySettings.skinWeights = d.skinWeights;
        QualitySettings.antiAliasing = d.msaaSamples > 1 ? d.msaaSamples : 0;

        // เงาความละเอียดต่ำสุดตอนภาพกาก — ค่านี้ URP ไม่ได้ใช้ตรงๆ แต่ตั้งไว้ให้สอดคล้อง
        QualitySettings.shadowResolution = d.mainShadowRes >= 2048
            ? UnityEngine.ShadowResolution.VeryHigh
            : d.mainShadowRes >= 1024
                ? UnityEngine.ShadowResolution.High
                : d.mainShadowRes >= 512
                    ? UnityEngine.ShadowResolution.Medium
                    : UnityEngine.ShadowResolution.Low;
    }

    /// <summary>
    /// URP asset คือตัวที่คุม Render Scale / MSAA / เงา จริงๆ ใน URP
    /// ⚠️ property หลายตัวของ URP เป็น get-only เลยต้องยิงผ่าน field ที่ serialize ไว้
    /// ทำเป็น reflection แบบเงียบ — URP เปลี่ยนชื่อ field เมื่อไหร่ก็แค่ข้ามไป ไม่พังทั้งเกม
    /// </summary>
    private void ApplyToUrpAsset(PresetData d)
    {
        UniversalRenderPipelineAsset urp = GetActiveUrpAsset();
        if (urp == null)
        {
            Debug.LogWarning("[Graphics] ไม่เจอ URP asset ที่ใช้งานอยู่ — ข้ามการตั้งค่าฝั่ง URP");
            return;
        }

        RememberOriginalUrpState(urp);

        // property ที่ set ได้ตรงๆ
        urp.renderScale = d.renderScale;
        urp.msaaSampleCount = d.msaaSamples;
        urp.shadowDistance = d.shadowDistance;
        urp.shadowCascadeCount = d.shadowCascades;
        urp.supportsHDR = d.hdr;
        urp.supportsCameraOpaqueTexture = d.opaqueTexture;
        urp.supportsCameraDepthTexture = d.depthTexture;

        // ที่เหลือเป็น get-only → ยิงลง private field
        SetPrivateField(urp, "m_MainLightShadowmapResolution", d.mainShadowRes);
        SetPrivateField(urp, "m_AdditionalLightsShadowmapResolution", Mathf.Max(256, d.mainShadowRes / 2));
        SetPrivateField(urp, "m_SoftShadowsSupported", d.softShadows);
        SetPrivateField(urp, "m_SoftShadowQuality", d.softShadowQuality);
        SetPrivateField(urp, "m_AdditionalLightShadowsSupported", d.additionalLightShadows);
        SetPrivateField(urp, "m_AdditionalLightsPerObjectLimit", Mathf.Clamp(d.maxAdditionalLights, 0, 8));
    }

    /// <summary>Post-processing กับ AA เป็นของ "กล้อง" ไม่ใช่ของ URP asset — ต้องไล่ตั้งทีละตัว</summary>
    private void ApplyToCameras(PresetData d)
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Camera cam in cameras)
            ConfigureCamera(cam, d);
    }

    private void ConfigureCamera(Camera cam, PresetData d)
    {
        if (cam == null) return;

        UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
        if (camData == null) return;

        camData.renderPostProcessing = d.postProcessing;
        camData.antialiasing = d.cameraAA;

        // เงาแบบ per-camera ปิดไปเลยตอนภาพกาก ประหยัดกว่าลดความละเอียดเงาอย่างเดียว
        camData.renderShadows = d.shadows != UnityEngine.ShadowQuality.Disable;
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private static UniversalRenderPipelineAsset GetActiveUrpAsset()
    {
        // Quality Level แต่ละอันมี URP asset ของตัวเอง (Mobile_RPAsset / PC_RPAsset)
        // ตัวที่ทำงานอยู่จริงคือ currentRenderPipeline — ไม่ใช่ default เสมอไป
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset current)
            return current;

        if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset perLevel)
            return perLevel;

        return GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
    }

    // ================================================================
    //  กัน URP asset โดนแก้ค้างใน Editor
    // ================================================================

    /// <summary>
    /// ⚠️ URP asset เป็น ScriptableObject ในโฟลเดอร์ Assets — แก้ตอนรันไทม์ใน Editor แล้วค่าจะค้าง
    /// อยู่ในไฟล์จริง แปลว่าถ้าใครกดเล่นด้วย Potato ทีนึง PC_RPAsset.asset จะติด renderScale 0.5
    /// แล้วไหลเข้า git ไปทั้งทีม — เลยเก็บสภาพเดิมไว้ตอนแตะครั้งแรก แล้วคืนตอนออก Play Mode
    /// (ในบิลด์จริงไม่มีปัญหานี้ asset เป็นสำเนาในหน่วยความจำอยู่แล้ว)
    /// </summary>
    private void RememberOriginalUrpState(UniversalRenderPipelineAsset urp)
    {
#if UNITY_EDITOR
        if (originalUrpJson != null) return;

        originalUrpAsset = urp;
        originalUrpJson = JsonUtility.ToJson(urp);

        UnityEditor.EditorApplication.playModeStateChanged += RestoreUrpOnExitPlayMode;
#endif
    }

#if UNITY_EDITOR
    private UniversalRenderPipelineAsset originalUrpAsset;
    private string originalUrpJson;

    private void RestoreUrpOnExitPlayMode(UnityEditor.PlayModeStateChange state)
    {
        if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;

        UnityEditor.EditorApplication.playModeStateChanged -= RestoreUrpOnExitPlayMode;

        if (originalUrpAsset != null && !string.IsNullOrEmpty(originalUrpJson))
        {
            JsonUtility.FromJsonOverwrite(originalUrpJson, originalUrpAsset);
            Debug.Log("[Graphics] คืนค่า URP asset กลับสภาพเดิมแล้ว (ออก Play Mode)");
        }

        originalUrpAsset = null;
        originalUrpJson = null;
    }
#endif

    private static readonly Dictionary<string, FieldInfo> fieldCache = new Dictionary<string, FieldInfo>();

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null) return;

        if (!fieldCache.TryGetValue(fieldName, out FieldInfo field))
        {
            field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            fieldCache[fieldName] = field;
        }

        if (field == null) return; // URP เวอร์ชันนี้ไม่มี field นี้ — ข้ามไปเงียบๆ

        try
        {
            // field เก็บเป็น enum (เช่น ShadowResolution) แต่เราส่ง int มา — แปลงให้ก่อน
            object converted = field.FieldType.IsEnum
                ? Enum.ToObject(field.FieldType, value)
                : Convert.ChangeType(value, field.FieldType);

            field.SetValue(target, converted);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Graphics] ตั้งค่า {fieldName} ไม่สำเร็จ: {e.Message}");
        }
    }
}
