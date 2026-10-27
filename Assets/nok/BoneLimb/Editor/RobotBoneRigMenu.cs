using Nsc.Limbs.Bones;
using Nsc.Robots;
using UnityEditor;
using UnityEngine;

/// <summary>
/// เมนูเซ็ทอัพระบบกระดูก: Tools ▸ NSC ▸ Bone Rig
/// เลือกหุ่น (RobotContainer หรือชิ้นไหนก็ได้ในหุ่น) ในซีนหรือในหน้า Prefab แล้วกด Add
/// </summary>
public static class RobotBoneRigMenu
{
    private const string AddPath = "Tools/NSC/Bone Rig/Add To Selected Robot";
    private const string RemovePath = "Tools/NSC/Bone Rig/Remove From Selected Robot";

    [MenuItem(AddPath)]
    private static void Add()
    {
        Robot robot = SelectedRobot();
        if (robot.GetComponent<RobotBoneRig>() != null)
        {
            Debug.Log($"[BoneRig] '{robot.name}' มี RobotBoneRig อยู่แล้ว", robot);
            return;
        }

        Undo.AddComponent<RobotBoneRig>(robot.gameObject);
        Debug.Log($"[BoneRig] ใส่ RobotBoneRig ที่ '{robot.name}' แล้ว — กด Play: ท่อนแขน/ขาจะเป็นกระดูกแข็ง " +
                  "(ฟิสิกส์ไม่เปลี่ยน) ปรับทิศศอก/เข่าได้ใน Inspector", robot);
    }

    [MenuItem(RemovePath)]
    private static void Remove()
    {
        Robot robot = SelectedRobot();
        var rig = robot.GetComponent<RobotBoneRig>();
        if (rig != null) Undo.DestroyObjectImmediate(rig);
    }

    [MenuItem(AddPath, true)]
    [MenuItem(RemovePath, true)]
    private static bool HasRobot() => SelectedRobot() != null;

    private static Robot SelectedRobot()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null) return null;
        Robot robot = go.GetComponentInParent<Robot>(true);
        return robot != null ? robot : go.GetComponentInChildren<Robot>(true);
    }
}
