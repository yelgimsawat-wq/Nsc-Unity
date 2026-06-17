using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(VerletRope))]
public class RopeRenderer : MonoBehaviour
{
    [Min(3)] [SerializeField] private int m_RopeSegmentSides;

    [Header("Taper Settings")]
    [Tooltip("ทำให้ปลายเชือกฝั่งที่ยึดติด (node แรก) เรียวลง")]
    [SerializeField] private bool m_TaperStart = false;
    [Tooltip("ทำให้ปลายเชือกฝั่งปลายอิสระ (node สุดท้าย) เรียวลง")]
    [SerializeField] private bool m_TaperEnd = true;
    [Tooltip("จำนวน node ที่ใช้ไล่ความเรียว นับจากปลายเชือกเข้ามา")]
    [Min(1)] [SerializeField] private int m_TaperNodeCount = 5;
    [Tooltip("ความโค้งของการไล่เรียว: 1 = เรียวแบบเส้นตรง, มากกว่า 1 = เรียวช้าตอนแรกแล้วแหลมเร็วใกล้ปลาย, น้อยกว่า 1 = แหลมเร็วตั้งแต่ต้น")]
    [Range(0.1f, 5f)] [SerializeField] private float m_TaperCurvePower = 1.5f;

    private MeshFilter m_MeshFilter;
    private MeshRenderer m_MeshRenderer;
    private Mesh m_RopeMesh;
    private VerletRope m_Rope;
    private Vector3[] m_Vertices;
    private int[] m_Triangles;
    private float[] m_NodeRadii;

    private float m_Angle;
    private int m_NodeCount;
    private bool m_IsInitialized;

    private void Awake()
    {
        m_MeshFilter = GetComponent<MeshFilter>();
        m_MeshRenderer = GetComponent<MeshRenderer>();

        m_RopeMesh = new Mesh();
        m_Angle = ((m_RopeSegmentSides - 2) * 180) / m_RopeSegmentSides;
        m_IsInitialized = false;
    }

    private void Start()
    {
        m_Rope = GetComponent<VerletRope>();
        m_NodeCount = m_Rope.GetNodeCount();
        m_Vertices = new Vector3[m_NodeCount * m_RopeSegmentSides];
        m_Triangles = new int[m_RopeSegmentSides * (m_NodeCount - 1) * 6];
        m_NodeRadii = new float[m_NodeCount];
    }

    // private void OnDrawGizmos()
    // {
    //     if (!Application.isPlaying)
    //         return;
    //
    //     if (m_Vertices is null || m_Triangles is null)
    //         return;
    //
    //
    //     foreach (var vert in m_Vertices)
    //     {
    //         Gizmos.color = Color.white;
    //         Gizmos.DrawSphere(vert, 0.01f);
    //     }
    //
    //     for (int i = 0; i < m_Triangles.Length - 3; i += 3)
    //     {
    //         Gizmos.DrawLine(m_Vertices[m_Triangles[i]], m_Vertices[m_Triangles[i + 1]]);
    //         Gizmos.DrawLine(m_Vertices[m_Triangles[i + 1]], m_Vertices[m_Triangles[i + 2]]);
    //         Gizmos.DrawLine(m_Vertices[m_Triangles[i + 2]], m_Vertices[m_Triangles[i]]);
    //     }
    // }

    public void RenderRope(VerletNode[] nodes, float radius)
    {
        if (m_Vertices is null || m_Triangles is null)
            return;

        ComputeNodeRadii(nodes.Length, radius);
        ComputeVertices(nodes);

        if (!m_IsInitialized)
        {
            ComputeTriangles();
            m_IsInitialized = true;
        }

        SetupMeshFilter();
    }

    // คำนวณรัศมีของแต่ละ node ล่วงหน้า โดยไล่ค่าลงจาก baseRadius ไปจนเหลือ 0
    // ที่ node ปลายสุด (ตามฝั่งที่เลือกเปิดไว้) ทำให้วงแหวน vertex ของ node นั้น
    // ยุบมารวมเป็นจุดเดียว มองเห็นเป็นปลายแหลม
    private void ComputeNodeRadii(int nodeCount, float baseRadius)
    {
        for (int i = 0; i < nodeCount; i++)
        {
            var radius = baseRadius;

            if (m_TaperStart)
            {
                var t = Mathf.Clamp01((float)i / m_TaperNodeCount);
                radius *= Mathf.Pow(t, m_TaperCurvePower);
            }

            if (m_TaperEnd)
            {
                var distFromEnd = (nodeCount - 1) - i;
                var t = Mathf.Clamp01((float)distFromEnd / m_TaperNodeCount);
                radius *= Mathf.Pow(t, m_TaperCurvePower);
            }

            m_NodeRadii[i] = radius;
        }
    }

    private void ComputeVertices(VerletNode[] nodes)
    {
        var angle = (360f / m_RopeSegmentSides) * Mathf.Deg2Rad;

        for (int i = 0; i < m_Vertices.Length; i++)
        {
            var nodeindex = i / m_RopeSegmentSides;
            var sign = nodeindex == nodes.Length - 1 ? -1 : 1;

            var currNodePosition = nodes[nodeindex].Position;
            var normalOfPlane =
                (sign * nodes[nodeindex].Position + -sign * nodes[nodeindex + (nodeindex == nodes.Length - 1 ? -1 : 1)].Position)
                .normalized;

            var u = Vector3.Cross(normalOfPlane, Vector3.forward).normalized;
            var v = Vector3.Cross(u, normalOfPlane).normalized;

            var nodeRadius = m_NodeRadii[nodeindex];

            m_Vertices[i] = currNodePosition + nodeRadius * (float)Math.Cos(angle * (i % m_RopeSegmentSides)) * u +
                            nodeRadius * (float)Math.Sin(angle * (i % m_RopeSegmentSides)) * v;
        }
    }

    private void ComputeTriangles()
    {
        var tn = 0;

        for (int i = 0; i < m_Vertices.Length - m_RopeSegmentSides; i++)
        {
            var nexti = (i + 1) % m_RopeSegmentSides == 0 ? i - m_RopeSegmentSides + 1 : i + 1;

            m_Triangles[tn] = i;
            m_Triangles[tn + 1] = nexti + m_RopeSegmentSides;
            m_Triangles[tn + 2] = i + m_RopeSegmentSides;

            m_Triangles[tn + 3] = i;
            m_Triangles[tn + 4] = nexti;
            m_Triangles[tn + 5] = nexti + m_RopeSegmentSides;

            tn += 6;
        }
    }

    private void SetupMeshFilter()
    {
        for (int i = 0; i < m_Vertices.Length; i++)
        {
            m_Vertices[i] -= transform.position;
        }

        m_RopeMesh.Clear();
        m_RopeMesh.vertices = m_Vertices;
        m_RopeMesh.triangles = m_Triangles;

        m_MeshFilter.mesh = m_RopeMesh;
        m_MeshFilter.mesh.RecalculateBounds();
        m_MeshFilter.mesh.RecalculateNormals();
    }
}