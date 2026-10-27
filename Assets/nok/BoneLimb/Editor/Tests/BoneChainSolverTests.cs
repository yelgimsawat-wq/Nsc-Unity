using Nsc.Limbs.Bones;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// ตัวคำนวณ IK ของระบบกระดูก (BoneChainSolver) เป็นคลาสธรรมดา — ทดสอบใน EditMode ได้ตรงๆ
/// </summary>
public class BoneChainSolverTests
{
    private const float Epsilon = 0.01f;

    // ขาตรงลงพื้น: สะโพก y=2, เข่า y=1, ข้อเท้า y=0
    private static Vector3[] StraightLeg() => new[] { new Vector3(0, 2, 0), new Vector3(0, 1, 0), Vector3.zero };
    private static readonly float[] LegLengths = { 1f, 1f };

    private static void AssertLengthsKept(Vector3[] p, float[] lengths)
    {
        for (int i = 0; i < lengths.Length; i++)
            Assert.AreEqual(lengths[i], Vector3.Distance(p[i], p[i + 1]), Epsilon, $"bone {i} length changed");
    }

    [Test]
    public void ReachableGoalIsReachedWithoutStretching()
    {
        Vector3 root = new Vector3(0, 2, 0);
        foreach (Vector3 goal in new[] { new Vector3(0.3f, 0.8f, 0.6f), new Vector3(-0.5f, 0.5f, -0.3f), new Vector3(0.2f, 1.5f, 1f) })
        {
            Vector3[] p = StraightLeg();
            BoneChainSolver.Solve(p, LegLengths, root, goal, true, new Vector3(0, 1, 5));

            Assert.Less(Vector3.Distance(p[2], goal), Epsilon, $"tip missed {goal}");
            Assert.Less(Vector3.Distance(p[0], root), 1e-4f, "root moved");
            AssertLengthsKept(p, LegLengths);
        }
    }

    [Test]
    public void KneeBendsTowardPole()
    {
        Vector3 root = new Vector3(0, 2, 0), goal = new Vector3(0, 0.9f, 0.3f);

        Vector3[] front = StraightLeg();
        BoneChainSolver.Solve(front, LegLengths, root, goal, true, new Vector3(0, 1, 5));
        Assert.Greater(front[1].z, 0.3f);

        Vector3[] back = StraightLeg();
        BoneChainSolver.Solve(back, LegLengths, root, goal, true, new Vector3(0, 1, -5));
        Assert.Less(back[1].z, 0.1f);
        AssertLengthsKept(back, LegLengths);
    }

    [Test]
    public void UnreachableGoalStretchesStraightTowardIt()
    {
        Vector3[] p = StraightLeg();
        BoneChainSolver.Solve(p, LegLengths, new Vector3(0, 2, 0), new Vector3(10, 2, 0), true, new Vector3(0, 1, 5));

        Assert.AreEqual(1f, p[1].x, Epsilon);
        Assert.AreEqual(2f, p[2].x, Epsilon);
        AssertLengthsKept(p, LegLengths);
    }

    [Test]
    public void FourBoneArmReachesGoal()
    {
        Vector3[] p = { Vector3.zero, new Vector3(0.5f, 0, 0), new Vector3(1f, 0, 0), new Vector3(1.4f, 0, 0), new Vector3(1.6f, 0, 0) };
        float[] lengths = { 0.5f, 0.5f, 0.4f, 0.2f };
        Vector3 goal = new Vector3(0.2f, -0.9f, 0.5f);

        BoneChainSolver.Solve(p, lengths, Vector3.zero, goal, false, Vector3.zero);

        Assert.Less(Vector3.Distance(p[4], goal), Epsilon);
        AssertLengthsKept(p, lengths);
    }
}
