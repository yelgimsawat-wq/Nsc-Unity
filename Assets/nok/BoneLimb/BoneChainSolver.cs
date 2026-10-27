using UnityEngine;

namespace Nsc.Limbs.Bones
{
    /// <summary>
    /// ตัวคำนวณ IK แบบ FABRIK สำหรับโซ่กระดูกกี่ท่อนก็ได้ — คลาสธรรมดา ไม่ผูก scene/เครือข่าย ทดสอบใน EditMode ได้ตรงๆ
    ///
    /// points[0] = โคน (ไหล่/สะโพก), points[n-1] = ปลาย (ข้อมือ/ข้อเท้า)
    /// lengths[i] = ความยาวท่อน i (points[i] → points[i+1]) ไม่ยืดไม่หด
    /// points ที่ส่งเข้ามาคือท่าเริ่มต้น (เช่นท่า bind) แล้วถูกเขียนทับด้วยคำตอบ
    /// </summary>
    public static class BoneChainSolver
    {
        public static void Solve(Vector3[] points, float[] lengths, Vector3 root, Vector3 goal,
                                 bool usePole, Vector3 pole, int iterations = 10, float tolerance = 0.001f)
        {
            int n = points.Length;
            if (n < 2 || lengths.Length < n - 1) return;

            float total = 0f;
            for (int i = 0; i < n - 1; i++) total += lengths[i];
            if (total < 1e-6f) return;

            Vector3 toGoal = goal - root;
            if (toGoal.sqrMagnitude >= total * total)
            {
                // เอื้อมไม่ถึง: ยืดตรงไปทางเป้า
                Vector3 dir = toGoal.normalized;
                points[0] = root;
                for (int i = 1; i < n; i++) points[i] = points[i - 1] + dir * lengths[i - 1];
                return;
            }

            // เริ่มจากท่าเดิมแต่ย้ายโคนมาที่ root ก่อน
            Vector3 shift = root - points[0];
            for (int i = 0; i < n; i++) points[i] += shift;

            for (int iter = 0; iter < iterations; iter++)
            {
                // Backward: วางปลายที่เป้า ไล่กลับหาโคน
                points[n - 1] = goal;
                for (int i = n - 2; i >= 0; i--)
                    points[i] = points[i + 1] + SafeDir(points[i] - points[i + 1], toGoal) * lengths[i];

                // Forward: ยึดโคนกลับ ไล่ไปหาปลาย
                points[0] = root;
                for (int i = 1; i < n; i++)
                    points[i] = points[i - 1] + SafeDir(points[i] - points[i - 1], toGoal) * lengths[i - 1];

                if ((points[n - 1] - goal).sqrMagnitude < tolerance * tolerance) break;
            }

            if (usePole) ApplyPole(points, pole);
        }

        /// <summary>หมุนข้อกลาง (ศอก/เข่า) รอบเส้นที่ต่อข้อข้างเคียง ให้หันไปทาง pole — ความยาวท่อนไม่เปลี่ยน</summary>
        private static void ApplyPole(Vector3[] points, Vector3 pole)
        {
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector3 a = points[i - 1];
                Vector3 axis = points[i + 1] - a;
                if (axis.sqrMagnitude < 1e-10f) continue;
                axis.Normalize();

                Vector3 toPole = Vector3.ProjectOnPlane(pole - a, axis);
                Vector3 toJoint = Vector3.ProjectOnPlane(points[i] - a, axis);
                if (toPole.sqrMagnitude < 1e-10f || toJoint.sqrMagnitude < 1e-10f) continue;

                float angle = Vector3.SignedAngle(toJoint, toPole, axis);
                points[i] = Quaternion.AngleAxis(angle, axis) * (points[i] - a) + a;
            }
        }

        private static Vector3 SafeDir(Vector3 v, Vector3 fallback)
        {
            float m = v.magnitude;
            if (m > 1e-6f) return v / m;
            return fallback.sqrMagnitude > 1e-12f ? fallback.normalized : Vector3.up;
        }
    }
}
