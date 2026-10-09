using System;
using System.Collections;
using Nsc.Limbs;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// ฝั่ง client: ผูกผู้เล่นเครื่องนี้เข้ากับแขนขาที่จองไว้ตอนแมตช์เริ่ม — กล้องตามชิ้นนั้น
    /// และเปิด input ของชิ้นนั้นชิ้นเดียว (ArmInput / LegInput) ใช้ร่วมกันทั้ง co-op และ PVP
    ///
    /// เปิด input เฉพาะชิ้นที่จองไว้ แทนการเช็ค "ชิ้นนี้ถูกเลือกในลอบบี้ไหม" ในโค้ดแขนขาทุกเฟรม
    /// (Host เป็นเจ้าของชิ้นที่ไม่มีใครเลือกโดย default — ดู ownership อย่างเดียวจะคุมชิ้นผิด)
    /// </summary>
    public class LimbControlBinder : MonoBehaviour
    {
        private const int MaxBindAttempts = 10;
        private const float BindRetryDelay = 0.2f;

        public static LimbControlBinder Current { get; private set; }

        private LimbSelection selection;
        private MatchSession session;
        private Robot localRobot;
        private LimbSlot localSlot;
        private Coroutine bindRoutine;

        public bool IsBound => localRobot != null;
        public Robot LocalRobot => localRobot;
        public LimbSlot LocalSlot => localSlot;
        public RobotLimb LocalLimb => localRobot != null ? localRobot.GetLimb(localSlot) : null;

        /// <summary>ยิงเมื่อผูกผู้เล่นเครื่องนี้กับแขนขาสำเร็จ (รวมการผูกใหม่หลังต่อกลับ)</summary>
        public event Action<Robot, LimbSlot> Bound;

        private void Awake() => Current = this;

        private void Start()
        {
            selection = LimbSelection.Current;
            session = MatchSession.Current;

            if (session != null) session.PhaseChanged += OnPhaseChanged;
            if (selection != null) selection.AssignmentsChanged += OnAssignmentsChanged;

            if (session != null && session.IsPlaying) RequestBind();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (session != null) session.PhaseChanged -= OnPhaseChanged;
            if (selection != null) selection.AssignmentsChanged -= OnAssignmentsChanged;
        }

        private void OnPhaseChanged(MatchPhase phase)
        {
            if (phase == MatchPhase.Playing) RequestBind();
        }

        /// <summary>ต่อกลับกลางเกมแล้วได้ชิ้นเดิมคืน → ผูกใหม่</summary>
        private void OnAssignmentsChanged()
        {
            if (session == null || !session.IsPlaying || selection == null) return;
            if (!selection.TryGetLocalAssignment(out Robot robot, out LimbSlot slot)) return;
            if (robot != localRobot || slot != localSlot) RequestBind();
        }

        private void RequestBind()
        {
            if (bindRoutine != null) StopCoroutine(bindRoutine);
            bindRoutine = StartCoroutine(BindWithRetry());
        }

        /// <summary>retry เพราะ PlayerObject ของเครื่องนี้อาจ spawn ไม่ทันจังหวะที่แมตช์เริ่ม</summary>
        private IEnumerator BindWithRetry()
        {
            for (int attempt = 0; attempt < MaxBindAttempts; attempt++)
            {
                if (TryBind()) break;
                yield return new WaitForSeconds(BindRetryDelay);
            }
            bindRoutine = null;
        }

        private bool TryBind()
        {
            if (selection == null) return true;
            // ไม่ได้จองอะไร (คนดู) = ไม่มีอะไรให้ผูก ถือว่าจบ
            if (!selection.TryGetLocalAssignment(out Robot robot, out LimbSlot slot)) return true;

            GameObject playerObject = LocalPlayerObject();
            if (playerObject == null) return false;

            RobotLimb limb = robot.GetLimb(slot);
            if (limb == null) return false;

            Camera playerCamera = playerObject.GetComponentInChildren<Camera>();
            if (playerCamera == null) playerCamera = Camera.main;

            PlayerCam orbit = playerObject.GetComponent<PlayerCam>();
            if (orbit != null) orbit.followTarget = limb.transform;

            Unbind();
            if (limb.TryGetComponent(out ArmInput arm)) arm.Bind(playerCamera);
            if (limb.TryGetComponent(out LegInput leg)) leg.Bind(playerCamera);

            localRobot = robot;
            localSlot = slot;
            Debug.Log($"[LimbControlBinder] เสียบกล้อง/คอนโทรลให้ {slot.DisplayName()} ของ '{robot.Root.name}' แล้ว");
            Bound?.Invoke(robot, slot);
            return true;
        }

        private void Unbind()
        {
            RobotLimb previous = LocalLimb;
            if (previous == null) return;
            if (previous.TryGetComponent(out ArmInput arm)) arm.Bind(null);
            if (previous.TryGetComponent(out LegInput leg)) leg.Bind(null);
        }

        private static GameObject LocalPlayerObject()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null) return null;

            if (network.LocalClient?.PlayerObject != null) return network.LocalClient.PlayerObject.gameObject;

            foreach (NetworkObject networkObject in network.SpawnManager.SpawnedObjectsList)
                if (networkObject.IsPlayerObject && networkObject.OwnerClientId == network.LocalClientId)
                    return networkObject.gameObject;
            return null;
        }
    }
}
