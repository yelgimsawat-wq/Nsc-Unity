// AutoSplineAroundObject.cs
// วางไฟล์นี้ในโฟลเดอร์ชื่อ "Editor" ใน Assets ของโปรเจกต์ (เช่น Assets/Editor/AutoSplineAroundObject.cs)
// ต้องติดตั้ง package "com.unity.splines" ก่อน (Window > Package Manager > Unity Registry > Splines)
//
// วิธีใช้:
// 1. เลือก GameObject ที่มี mesh (มี MeshRenderer/MeshFilter) ในฉาก
// 2. เปิดเมนู Tools > Auto Spline > Generate Spiral Around Object
// 3. ปรับค่าพารามิเตอร์ใน window แล้วกด "Generate"
// 4. จะได้ GameObject ใหม่ชื่อ "AutoSmokeSpline" ที่มี SplineContainer พร้อม path วนรอบวัตถุแนบผิวให้อัตโนมัติ
//    เอาไปใช้กับ VFX Graph (Spline node) หรือ SplineFollower ต่อได้เลย

using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;

public class AutoSplineAroundObject : EditorWindow
{
    private GameObject targetObject;
    private int turns = 3;                 // จำนวนรอบที่ควันจะวนรอบวัตถุ (spiral)
    private int pointsPerTurn = 16;         // จำนวนจุดต่อ 1 รอบ ยิ่งเยอะยิ่งเนียนแต่จุดเยอะ
    private float surfaceOffset = 0.15f;    // ระยะห่างจากผิววัตถุ (หน่วยเดียวกับ scene)
    private float heightPadding = 0.1f;     // เผื่อขอบบน-ล่างเป็นสัดส่วนของความสูงวัตถุ (0.1 = เผื่อ 10%)
    private bool closeLoopIfSingleTurn = true; // ถ้า turns = 1 จะปิดเป็นวงลูปให้อัตโนมัติ

    // ช่วงความสูงที่จะสร้างควัน (0 = ล่างสุดวัตถุ, 1 = บนสุดวัตถุ)
    // ตัวอย่าง: อยากได้ควันแค่แถบขอบบน -> ตั้ง 0.85 - 1.0
    private float heightRangeMin = 0f;
    private float heightRangeMax = 1f;
    private LayerMask raycastMask = ~0;     // เลเยอร์ที่ยิงชนได้ ปกติปล่อย All ไว้ได้

    [MenuItem("Tools/Auto Spline/Generate Spiral Around Object")]
    public static void ShowWindow()
    {
        GetWindow<AutoSplineAroundObject>("Auto Spline Around Object");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("สร้าง Spline วนรอบวัตถุแบบอัตโนมัติ (สำหรับ VFX ควัน/ไฟ)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);

        EditorGUILayout.Space();
        turns = EditorGUILayout.IntSlider("จำนวนรอบ (Turns)", turns, 1, 12);
        pointsPerTurn = EditorGUILayout.IntSlider("จุดต่อรอบ (Points/Turn)", pointsPerTurn, 4, 64);
        surfaceOffset = EditorGUILayout.FloatField("ระยะห่างผิว (Surface Offset)", surfaceOffset);
        heightPadding = EditorGUILayout.Slider("เผื่อขอบบน-ล่าง (Height Padding)", heightPadding, 0f, 0.5f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("ช่วงความสูงที่จะสร้างควัน (0 = ล่างสุด, 1 = บนสุด)");
        EditorGUILayout.MinMaxSlider(ref heightRangeMin, ref heightRangeMax, 0f, 1f);
        EditorGUILayout.LabelField($"   ช่วงที่เลือก: {heightRangeMin:F2} - {heightRangeMax:F2}");
        if (GUILayout.Button("แถบขอบบนสุด (Top Edge Only)"))
        {
            heightRangeMin = 0.85f;
            heightRangeMax = 1f;
        }

        if (turns == 1)
            closeLoopIfSingleTurn = EditorGUILayout.Toggle("ปิดเป็นวงลูป (Close Loop)", closeLoopIfSingleTurn);

        EditorGUILayout.Space();
        raycastMask = LayerMaskField("Raycast Layer Mask", raycastMask);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(targetObject == null))
        {
            if (GUILayout.Button("Generate", GUILayout.Height(32)))
            {
                Generate();
            }
        }

        if (targetObject == null)
            EditorGUILayout.HelpBox("เลือกวัตถุที่ต้องการให้ควันวนรอบก่อน", MessageType.Info);
    }

    // ตัว EditorGUILayout ไม่มี LayerMaskField ตรงๆ ในบาง Unity version เลยทำ helper เอง
    private LayerMask LayerMaskField(string label, LayerMask mask)
    {
        var layers = InternalEditorUtility.layers;
        int maskValue = 0;
        for (int i = 0; i < layers.Length; i++)
        {
            int layerIndex = LayerMask.NameToLayer(layers[i]);
            if (((1 << layerIndex) & mask.value) != 0)
                maskValue |= (1 << i);
        }
        maskValue = EditorGUILayout.MaskField(label, maskValue, layers);
        int result = 0;
        for (int i = 0; i < layers.Length; i++)
        {
            if ((maskValue & (1 << i)) != 0)
                result |= (1 << LayerMask.NameToLayer(layers[i]));
        }
        return result;
    }

    private void Generate()
    {
        // 1) รวม bounds ของทุก renderer ใน target (รองรับวัตถุที่มีลูกหลาน mesh หลายชิ้น)
        Renderer[] renderers = targetObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            EditorUtility.DisplayDialog("Auto Spline", "วัตถุนี้ไม่มี Renderer/Mesh ให้ยิง raycast หา", "OK");
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        // 2) ต้องมี Collider ถึงจะ raycast โดนผิวได้ — ถ้าไม่มีให้เติม MeshCollider ชั่วคราว
        List<Collider> tempColliders = new List<Collider>();
        MeshFilter[] meshFilters = targetObject.GetComponentsInChildren<MeshFilter>();
        foreach (var mf in meshFilters)
        {
            if (mf.GetComponent<Collider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                tempColliders.Add(mc);
            }
        }

        Vector3 center = bounds.center;
        float radius = bounds.extents.magnitude * 1.5f; // รัศมีเริ่มยิงจากด้านนอกเข้าหาศูนย์กลาง ต้องกว้างพอครอบวัตถุ

        // แมปช่วง heightRangeMin/Max (0-1) ไปเป็นความสูงจริงของวัตถุก่อน
        // แล้วค่อยเผื่อ padding เฉพาะฝั่งที่ชนขอบบนสุด/ล่างสุดจริงๆ ของวัตถุ (กันเผื่อ padding ดันเลยขอบวัตถุถ้าเลือกแค่แถบกลาง)
        float bottomY = Mathf.Lerp(bounds.min.y, bounds.max.y, heightRangeMin);
        float topY = Mathf.Lerp(bounds.min.y, bounds.max.y, heightRangeMax);
        if (heightRangeMin <= 0f) bottomY -= bounds.size.y * heightPadding;
        if (heightRangeMax >= 1f) topY += bounds.size.y * heightPadding;

        int totalPoints = turns * pointsPerTurn;
        List<float3> knotPositions = new List<float3>();

        for (int i = 0; i <= totalPoints; i++)
        {
            float t = (float)i / totalPoints;               // 0 -> 1 ตลอดทั้ง spiral
            float angle = t * turns * 360f * Mathf.Deg2Rad;   // มุมรอบแกน Y สะสมตามจำนวนรอบ
            float height = Mathf.Lerp(bottomY, topY, t);      // ไล่ความสูงจากล่างขึ้นบน

            Vector3 rayOrigin = new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                height,
                center.z + Mathf.Sin(angle) * radius
            );
            Vector3 rayDir = (new Vector3(center.x, height, center.z) - rayOrigin).normalized;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, radius * 2f, raycastMask))
            {
                Vector3 pos = hit.point + hit.normal * surfaceOffset;
                knotPositions.Add(new float3(pos.x, pos.y, pos.z));
            }
            else
            {
                // ยิงไม่โดน (เช่นวัตถุมีรูโหว่ตรงนั้น) ให้ fallback ไปแตะจุดกลางความสูงนั้นแทน ป้องกัน spline ขาด
                Vector3 fallback = new Vector3(center.x + Mathf.Cos(angle) * (bounds.extents.magnitude + surfaceOffset), height, center.z + Mathf.Sin(angle) * (bounds.extents.magnitude + surfaceOffset));
                knotPositions.Add(new float3(fallback.x, fallback.y, fallback.z));
            }
        }

        // 3) ลบ collider ชั่วคราวที่เติมไว้ (ถ้าอยากเก็บไว้ใช้ต่อ ให้ลบส่วนนี้ทิ้ง)
        foreach (var c in tempColliders)
            Object.DestroyImmediate(c);

        // 4) สร้าง GameObject + SplineContainer แล้วใส่ knot ที่ได้เข้าไป
        GameObject splineGO = new GameObject("AutoSmokeSpline");
        Undo.RegisterCreatedObjectUndo(splineGO, "Create Auto Spline");
        var container = splineGO.AddComponent<SplineContainer>();
        var spline = container.Spline;
        spline.Clear();

        foreach (var p in knotPositions)
        {
            var knot = new BezierKnot(p);
            spline.Add(knot);
        }

        // ปรับ tangent ให้โค้งลื่นอัตโนมัติทุกจุด (AutoSmooth)
        for (int i = 0; i < spline.Count; i++)
        {
            spline.SetTangentMode(i, TangentMode.AutoSmooth);
        }

        if (turns == 1 && closeLoopIfSingleTurn)
            spline.Closed = true;

        Selection.activeGameObject = splineGO;
        EditorUtility.DisplayDialog("Auto Spline", $"สร้าง spline เสร็จแล้ว: {knotPositions.Count} จุด\nดูผลได้ที่ GameObject 'AutoSmokeSpline' ใน Hierarchy", "OK");
    }
}