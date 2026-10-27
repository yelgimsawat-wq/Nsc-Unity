using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// หน้าต่างช่วยเซ็ทอัพ BoneLimbIK แบบคลิกเดียว
/// เมนู: Tools > Bone Limb > Setup Wizard
/// </summary>
public class BoneLimbSetupWindow : EditorWindow
{
    private enum LimbSlot { None, LeftArm, RightArm, LeftLeg, RightLeg }
    private enum SetupMode { LinkToLobby, StandaloneTest }

    private Transform rootBone;
    private LimbSlot slot = LimbSlot.None;
    private SetupMode mode = SetupMode.LinkToLobby;
    private bool addTipCollider = true;
    private Transform[] chain;
    private Vector2 scroll;

    [MenuItem("Tools/Bone Limb/Setup Wizard")]
    public static void Open()
    {
        var w = GetWindow<BoneLimbSetupWindow>("Bone Limb Setup");
        w.minSize = new Vector2(360, 420);
        if (Selection.activeTransform != null) w.SetRoot(Selection.activeTransform);
    }

    private void SetRoot(Transform t)
    {
        rootBone = t;
        chain = t != null ? BoneLimbIK.BuildChain(t) : null;
        slot = GuessSlot(t);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "1) เลือกกระดูกโคน (ไหล่/สะโพก) ของโมเดล\n" +
            "2) เลือกว่าเป็นแขน/ขาข้างไหน\n" +
            "3) กด Setup\n\n" +
            "โมเดลต้องเป็นลูกของ Torso (ตัวหุ่น) เพื่อให้แขนขาเคลื่อนตามตัว",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        var newRoot = (Transform)EditorGUILayout.ObjectField("Root Bone", rootBone, typeof(Transform), true);
        if (EditorGUI.EndChangeCheck()) SetRoot(newRoot);

        if (GUILayout.Button("ใช้ตัวที่เลือกใน Hierarchy") && Selection.activeTransform != null)
            SetRoot(Selection.activeTransform);

        mode = (SetupMode)EditorGUILayout.EnumPopup(new GUIContent("Mode",
            "LinkToLobby = ต่อกับเกมจริง (LobbyManager/EZMovement)\nStandaloneTest = สร้างเป้าไว้ลากทดสอบเอง"), mode);
        slot = (LimbSlot)EditorGUILayout.EnumPopup(new GUIContent("Limb Slot",
            "ช่องใน LobbyManager ที่จะผูก (None = ไม่ผูก)"), slot);
        addTipCollider = EditorGUILayout.Toggle(new GUIContent("Add Tip Collider",
            "ใส่ Collider ที่ปลายแขน/เท้า ไว้ชนและจับของ"), addTipCollider);

        EditorGUILayout.Space();
        if (chain != null && chain.Length > 0)
        {
            EditorGUILayout.LabelField($"Bone Chain ({chain.Length} joints)  ยาวรวม {ChainLength(chain):0.00} m", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120));
            for (int i = 0; i < chain.Length; i++)
                EditorGUILayout.LabelField($"{i}. {chain[i].name}" + (i == chain.Length - 1 ? "   <- ปลาย (Tip)" : ""));
            EditorGUILayout.EndScrollView();

            if (chain.Length < 2)
                EditorGUILayout.HelpBox("ต้องมีกระดูกอย่างน้อย 2 ชิ้น (เลือกกระดูกโคนที่มีลูกต่อกัน)", MessageType.Warning);
            else if (mode == SetupMode.LinkToLobby && chain[0].GetComponentInParent<NetworkObject>() == null)
                EditorGUILayout.HelpBox("ไม่เจอ NetworkObject ในตัวพ่อ: Following เป็น NetworkBehaviour " +
                                        "ให้วางโมเดลไว้ใต้ RobotContainer/Torso ที่มี NetworkObject", MessageType.Warning);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(chain == null || chain.Length < 2))
        {
            if (GUILayout.Button("Setup", GUILayout.Height(32))) DoSetup();
        }
    }

    private void DoSetup()
    {
        Undo.SetCurrentGroupName("Bone Limb Setup");
        int group = Undo.GetCurrentGroup();

        Transform root = chain[0];
        Transform tip = chain[chain.Length - 1];
        float length = ChainLength(chain);

        // --- IK component on the root bone ---
        var ik = root.GetComponent<BoneLimbIK>();
        if (ik == null) ik = Undo.AddComponent<BoneLimbIK>(root.gameObject);
        Undo.RecordObject(ik, "Bone Limb Setup");
        ik.bones = chain;
        ik.resetPoseEachFrame = root.GetComponentInParent<Animator>() == null;

        // --- Pole (child of the body so it moves with the robot) ---
        if (ik.pole == null)
        {
            var poleGo = new GameObject(root.name + "_IKPole");
            Undo.RegisterCreatedObjectUndo(poleGo, "Create Pole");
            poleGo.transform.SetParent(root.parent, false);
            poleGo.transform.position = chain[chain.Length / 2].position + BendDirection() * length * 0.6f;
            ik.pole = poleGo.transform;
        }

        if (mode == SetupMode.LinkToLobby)
        {
            // Following on the tip: LobbyManager assigns targetPoint, EZMovement finds the "physical hand"
            var rb = tip.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(tip.gameObject);
            Undo.RecordObject(rb, "Bone Limb Setup");
            rb.isKinematic = true;
            rb.useGravity = false;

            if (addTipCollider && tip.GetComponent<Collider>() == null)
            {
                var col = Undo.AddComponent<SphereCollider>(tip.gameObject);
                float lastLen = Vector3.Distance(chain[chain.Length - 2].position, tip.position);
                float scale = Mathf.Max(tip.lossyScale.x, 1e-4f);
                col.radius = Mathf.Max(lastLen * 0.35f, 0.05f) / scale;
            }

            var following = tip.GetComponent<Following>();
            if (following == null) following = Undo.AddComponent<Following>(tip.gameObject);
            Undo.RecordObject(following, "Bone Limb Setup");
            following.pivotPoint = root;
            ik.linkedFollowing = following;

            if (slot != LimbSlot.None) AssignToLobby(following);

            Debug.Log($"[BoneLimb] Setup '{root.name}' เสร็จ: ความยาวแขน/ขา = {length:0.00} m " +
                      $"(แนะนำตั้ง Range ใน EZMovement/EZFootMovement ของ Player ประมาณ {length:0.0})", ik);
        }
        else
        {
            if (ik.target == null)
            {
                var targetGo = new GameObject(root.name + "_IKTarget");
                Undo.RegisterCreatedObjectUndo(targetGo, "Create Target");
                targetGo.transform.position = tip.position;
                ik.target = targetGo.transform;
                Selection.activeGameObject = targetGo;
            }
            Debug.Log($"[BoneLimb] Setup '{root.name}' (Standalone) เสร็จ: กด Play แล้วลาก '{ik.target.name}' ในหน้า Scene", ik);
        }

        EditorUtility.SetDirty(ik);
        Undo.CollapseUndoOperations(group);
    }

    private void AssignToLobby(Following following)
    {
        var lobby = Object.FindFirstObjectByType<LobbyManager>(FindObjectsInactive.Include);
        if (lobby == null)
        {
            Debug.LogWarning("[BoneLimb] ไม่เจอ LobbyManager ในซีน: ข้ามการผูกช่องแขน/ขา");
            return;
        }

        string field = slot switch
        {
            LimbSlot.LeftArm => "leftArm",
            LimbSlot.RightArm => "rightArm",
            LimbSlot.LeftLeg => "leftLeg",
            _ => "rightLeg",
        };

        var so = new SerializedObject(lobby);
        var prop = so.FindProperty(field);
        var old = prop.objectReferenceValue as Following;
        prop.objectReferenceValue = following;
        so.ApplyModifiedProperties();

        if (old != null && old != following)
            Debug.Log($"[BoneLimb] LobbyManager.{field}: เปลี่ยนจาก '{old.name}' (แขน/ขาฟิสิกส์เดิม) เป็น '{following.name}'. " +
                      "แขน/ขาเดิมยังอยู่ในซีน ถ้าไม่ใช้แล้วให้ปิด (SetActive false) เอง", old);
    }

    private Vector3 BendDirection()
    {
        Vector3 a = chain[0].position;
        Vector3 c = chain[chain.Length - 1].position;
        Vector3 mid = chain[chain.Length / 2].position;
        Vector3 bend = Vector3.ProjectOnPlane(mid - a, c - a);
        if (bend.magnitude > ChainLength(chain) * 0.02f) return bend.normalized;

        // แขน/ขาตรงเกินไป: เดาทิศจากตัวหุ่น (เข่าไปหน้า, ศอกไปหลัง)
        Transform reference = chain[0].parent != null ? chain[0].parent : chain[0];
        bool isArm = slot == LimbSlot.LeftArm || slot == LimbSlot.RightArm;
        return isArm ? -reference.forward : reference.forward;
    }

    private static float ChainLength(Transform[] c)
    {
        float len = 0f;
        for (int i = 0; i < c.Length - 1; i++) len += Vector3.Distance(c[i].position, c[i + 1].position);
        return len;
    }

    private static LimbSlot GuessSlot(Transform t)
    {
        if (t == null) return LimbSlot.None;
        string n = "";
        for (Transform p = t; p != null; p = p.parent) n += p.name.ToLowerInvariant() + "/";
        bool left = n.Contains("left") || n.Contains(".l/") || n.Contains("_l/");
        bool right = n.Contains("right") || n.Contains(".r/") || n.Contains("_r/");
        bool leg = n.Contains("leg") || n.Contains("foot") || n.Contains("ขา");
        bool arm = n.Contains("arm") || n.Contains("hand") || n.Contains("แขน");
        if (leg && left) return LimbSlot.LeftLeg;
        if (leg && right) return LimbSlot.RightLeg;
        if (arm && left) return LimbSlot.LeftArm;
        if (arm && right) return LimbSlot.RightArm;
        return LimbSlot.None;
    }
}
