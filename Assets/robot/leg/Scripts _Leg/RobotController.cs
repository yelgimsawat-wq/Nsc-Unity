using UnityEngine;
using UnityEngine.InputSystem;

namespace NscUnity.Movement
{
    /// <summary>
    /// Coordinates the movement of multiple legs and handles player input.
    /// Manages the logic of one leg cocking while the other pushes.
    /// </summary>
    public class RobotController : MonoBehaviour
    {
        [Header("Leg Configuration")]
        [SerializeField] private LegController leftLeg;
        [SerializeField] private LegController rightLeg;
        [SerializeField] private Transform rootTransform; // ตัวหุ่นที่จะเคลื่อนที่
        [SerializeField] private float walkSpeed = 2f;      // ความเร็วในการเดินแบบร่วมมือ
        [SerializeField] private bool invertMovement = false; // ติ๊กถูกถ้าหุ่นเดินถอยหลัง
        
        [Header("Balance Settings")]
        [SerializeField] private bool isFallen = false;       // สถานะการล้ม
        [SerializeField] private float fallTiltAngle = 60f;  // องศาการหงายหลัง (บวกคือหงาย)
        [SerializeField] private float fallSpeed = 2f;      // ความเร็วในการล้ม
        
        private Quaternion targetFallRotation;
        
        [Header("Input Action References")]
        [SerializeField] private InputActionProperty leftLegLiftAction;
        [SerializeField] private InputActionProperty leftLegPushAction;
        [SerializeField] private InputActionProperty leftLegRotateLeftAction;  // ปุ่มหมุนขาซ้ายไปทางซ้าย
        [SerializeField] private InputActionProperty leftLegRotateRightAction; // ปุ่มหมุนขาซ้ายไปทางขวา
        [SerializeField] private InputActionProperty rightLegLiftAction;
        [SerializeField] private InputActionProperty rightLegPushAction;
        [SerializeField] private InputActionProperty rightLegRotateLeftAction;  // ปุ่มหมุนขาขวาไปทางซ้าย
        [SerializeField] private InputActionProperty rightLegRotateRightAction; // ปุ่มหมุนขาขวาไปทางขวา

        private void OnEnable()
        {
            if (leftLegLiftAction.action != null) leftLegLiftAction.action.Enable();
            if (leftLegPushAction.action != null) leftLegPushAction.action.Enable();
            if (leftLegRotateLeftAction.action != null) leftLegRotateLeftAction.action.Enable();
            if (leftLegRotateRightAction.action != null) leftLegRotateRightAction.action.Enable();
            if (rightLegLiftAction.action != null) rightLegLiftAction.action.Enable();
            if (rightLegPushAction.action != null) rightLegPushAction.action.Enable();
            if (rightLegRotateLeftAction.action != null) rightLegRotateLeftAction.action.Enable();
            if (rightLegRotateRightAction.action != null) rightLegRotateRightAction.action.Enable();
        }

        private void OnDisable()
        {
            if (leftLegLiftAction.action != null) leftLegLiftAction.action.Disable();
            if (leftLegPushAction.action != null) leftLegPushAction.action.Disable();
            if (leftLegRotateLeftAction.action != null) leftLegRotateLeftAction.action.Disable();
            if (leftLegRotateRightAction.action != null) leftLegRotateRightAction.action.Disable();
            if (rightLegLiftAction.action != null) rightLegLiftAction.action.Disable();
            if (rightLegPushAction.action != null) rightLegPushAction.action.Disable();
            if (rightLegRotateLeftAction.action != null) rightLegRotateLeftAction.action.Disable();
            if (rightLegRotateRightAction.action != null) rightLegRotateRightAction.action.Disable();
        }

        private void Update()
        {
            if (!isFallen)
            {
                HandleLegInput();
                CheckBalance();
            }
            else
            {
                // ถ้าล้มแล้ว ให้ค่อยๆ เอียงตัวไปที่เป้าหมาย (หงายหลัง)
                if (rootTransform != null)
                {
                    rootTransform.localRotation = Quaternion.Slerp(
                        rootTransform.localRotation, 
                        targetFallRotation, 
                        Time.deltaTime * fallSpeed
                    );
                }
            }
        }

        private void HandleLegInput()
        {
            if (leftLegLiftAction.action == null || rightLegLiftAction.action == null) return;

            // อ่านค่า Input อิสระทั้ง 4 ปุ่ม
            bool holdLiftL = leftLegLiftAction.action.IsPressed();
            bool holdPushL = leftLegPushAction.action.IsPressed();
            bool holdLiftR = rightLegLiftAction.action.IsPressed();
            bool holdPushR = rightLegPushAction.action.IsPressed();

            // ค่าเริ่มต้นคือค้างอยู่ที่เดิม
            float leftT = leftLeg.GetNormalizedTime();
            float rightT = rightLeg.GetNormalizedTime();

            // ควบคุมขาซ้าย
            if (holdLiftL) leftT = 1f;
            else if (holdPushL) leftT = -1f;

            // หมุนขาซ้าย: ตรวจสอบปุ่มกดแยกซ้ายและขวา
            if (leftLeg.GetNormalizedTime() > 0.1f)
            {
                float rotateVal = 0;
                if (leftLegRotateLeftAction.action != null && leftLegRotateLeftAction.action.IsPressed()) rotateVal -= 1f;
                if (leftLegRotateRightAction.action != null && leftLegRotateRightAction.action.IsPressed()) rotateVal += 1f;
                
                if (rotateVal != 0) leftLeg.RotateLeg(rotateVal);
            }

            // ควบคุมขาขวา
            if (holdLiftR) rightT = 1f;
            else if (holdPushR) rightT = -1f;

            // หมุนขาขวา: ตรวจสอบปุ่มกดแยกซ้ายและขวา
            if (rightLeg.GetNormalizedTime() > 0.1f)
            {
                float rotateVal = 0;
                if (rightLegRotateLeftAction.action != null && rightLegRotateLeftAction.action.IsPressed()) rotateVal -= 1f;
                if (rightLegRotateRightAction.action != null && rightLegRotateRightAction.action.IsPressed()) rotateVal += 1f;
                
                if (rotateVal != 0) rightLeg.RotateLeg(rotateVal);
            }

            if (leftLeg != null) leftLeg.SetTargetValue(leftT);
            if (rightLeg != null) rightLeg.SetTargetValue(rightT);

            // --- ระบบเดินแบบร่วมมือ (Cooperative Walking) ---
            float leftNorm = leftLeg.GetNormalizedTime();
            float rightNorm = rightLeg.GetNormalizedTime();

            // นิยาม: ขาที่ "กำลังผลัก" (Actively Pushing) คือตั้งใจไป -1 และกำลังขยับอยู่
            bool leftActivelyPushing = leftLeg.GetTargetValue() == -1f && leftLeg.IsMovingAnimation();
            bool rightActivelyPushing = rightLeg.GetTargetValue() == -1f && rightLeg.IsMovingAnimation();

            // นิยาม: ขาที่ "ยกค้าง/ลอยอยู่" (Held Lifted) คือลอยพ้นพื้น (> 0.1)
            bool leftIsLifted = leftNorm > 0.1f;
            bool rightIsLifted = rightNorm > 0.1f;

            // เงื่อนไขการเดิน (ตามคำสั่งเสียง): 
            // 1. ขาซ้ายยกค้างไว้ + ขาขวากำลังผลัก
            // 2. ขาขวายกค้างไว้ + ขาซ้ายกำลังผลัก
            // *ถ้าขาที่ผลักหยุดนิ่งแล้ว หุ่นต้องไม่ขยับแม้ขาอีกข้างจะขยับยกขึ้นลงก็ตาม
            bool isWalking = (leftIsLifted && rightActivelyPushing) || (rightIsLifted && leftActivelyPushing);

            if (isWalking && rootTransform != null)
            {
                // --- ระบบเดินหน้าตรง (Zero-Slide Translation) ---
                // หุ่นจะเดินเฉพาะทิศหน้าตรง (forward) ของตัวหุ่นเท่านั้น
                Vector3 moveDir = rootTransform.forward;
                if (invertMovement) moveDir = -moveDir;

                rootTransform.Translate(moveDir * walkSpeed * Time.deltaTime, Space.World);
            }
        }

        private void CheckBalance()
        {
            float leftNorm = leftLeg.GetNormalizedTime();
            float rightNorm = rightLeg.GetNormalizedTime();
            
            // นิยามสถานะขา
            bool leftOnGround = leftNorm <= 0.1f;  // แตะพื้นคือท่ายืน (0) หรือท่าดัน (-1)
            bool rightOnGround = rightNorm <= 0.1f;
            
            bool leftPushing = leftNorm < -0.1f;
            bool rightPushing = rightNorm < -0.1f;

            // --- เงื่อนไขการล้มแบบเรียบง่าย (ตามคำสั่งเสียงล่าสุด) ---

            // 1. ล้มถ้าไม่มีขาข้างไหนแตะพื้นเลย (ยกขาพร้อมกัน)
            if (!leftOnGround && !rightOnGround)
            {
                TriggerFall("No legs on ground! (Both Lifted)");
                return;
            }

            // 2. ล้มถ้าพยายามดันพื้นสองข้างพร้อมกัน (ดันคู่) ลึกกว่า -0.3
            // ปรับตามคำสั่งเสียง: "เอา -0.3 ดีกว่า แล้วค่อยล้ม" เพื่อให้มี Buffer ไม่ล้มง่ายเกินไป
            if (leftNorm < -0.3f && rightNorm < -0.3f)
            {
                TriggerFall("Both legs pushing too deep! (Dual-Push < -0.3)");
                return;
            }
        }

        private void TriggerFall(string reason)
        {
            if (isFallen) return;
            isFallen = true;
            Debug.LogError($"Robot has Fallen! Reason: {reason}");

            // ตั้งเป้าหมายการล้มแบบ "หงายหลัง" (Backward)
            if (rootTransform != null)
            {
                targetFallRotation = rootTransform.localRotation * Quaternion.Euler(Vector3.right * fallTiltAngle);
            }
        }

        [ContextMenu("Reset Robot")]
        public void ResetRobot()
        {
            isFallen = false;
            if (rootTransform != null)
            {
                rootTransform.localRotation = Quaternion.identity;
            }
            Debug.Log("Robot Reset.");
        }

        // --- Networking Prepared Methods ---
        
        /// <summary>
        /// This would be called by a NetworkBehaviour on the Server to sync state to clients.
        /// Or used with NetworkVariables in Netcode for GameObjects.
        /// </summary>
        public void SyncLegState(float leftNormalized, float rightNormalized)
        {
            // This is where remote synchronization would happen
        }
    }
}
