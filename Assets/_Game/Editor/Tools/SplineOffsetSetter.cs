using UnityEngine;
using UnityEditor;
using UnityEngine.Splines;
using Unity.Mathematics;

public class SplineOffsetSetter : EditorWindow
{
    [MenuItem("Tools/Set Spline Offsets")]
    static void SetOffsets()
    {
        SplineContainer spline = FindObjectOfType<SplineContainer>();
        if (spline == null) return;

        foreach (GameObject obj in Selection.gameObjects)
        {
            SplineAnimate anim = obj.GetComponent<SplineAnimate>();
            if (anim == null) continue;

            Vector3 localPos = spline.transform.InverseTransformPoint(obj.transform.position);

            SplineUtility.GetNearestPoint(
                spline.Spline,
                (float3)localPos,
                out float3 nearest,
                out float t
            );

            Debug.Log($"{obj.name} | worldPos={obj.transform.position} | localPos={localPos} | t={t} | nearest={nearest}");

            Undo.RecordObject(anim, "Set Spline Offset");
            anim.Container = spline;
            anim.StartOffset = t;
            anim.Restart(false);

            EditorUtility.SetDirty(anim);
        }
    }
}