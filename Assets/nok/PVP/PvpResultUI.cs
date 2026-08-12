using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NscGame.Pvp
{
    /// <summary>
    /// หน้าจอจบแมตช์ PVP — บอกว่า "ชนะ/แพ้" แล้วมีปุ่มออกกลับเมนู
    /// (โครงเดียวกับ GameFlowManager ของโหมดบอส แต่ไม่ใช่ NetworkBehaviour
    ///  เพราะผลแพ้ชนะ sync มาทาง PvpTeamManager อยู่แล้ว ไม่ต้องมี NetworkVariable ซ้ำ)
    ///
    /// ข้อความที่เห็นขึ้นกับ "ทีมของเครื่องตัวเอง":
    ///   ทีมเราชนะ → VICTORY / ทีมเราแพ้ → DEFEAT / ไม่ได้เลือกทีม (คนดู) → MATCH OVER
    ///
    /// ปุ่ม:
    ///   EXIT TO MENU — ทุกคนกดได้ ออกเฉพาะเครื่องตัวเอง (Host กด = ห้องสลาย คนอื่นเด้งตาม)
    ///   REMATCH      — เฉพาะ Host โหลดซีนเดิมซ้ำผ่าน NGO ทุกระบบรีเซ็ตเองหมด
    ///
    /// ⚠️ ไม่มีหลอดเลือดในนี้ เพราะเลือดใช้ PlayerHUD เดิม (HullRingUI + LimbStatusUI)
    ///    ที่ผูกกับ "หุ่นของเราเอง" ผ่าน LocalRobotBinder อยู่แล้ว
    /// </summary>
    public class PvpResultUI : MonoBehaviour
    {
        #region Inspector

        [Header("Result")]
        public GameObject resultPanel;
        public TextMeshProUGUI resultText;

        [Tooltip("บรรทัดรอง — บอกว่าทีมไหนชนะ (ปล่อยว่างได้)")]
        public TextMeshProUGUI subtitleText;

        [Tooltip("ใส่ไว้เพื่อให้ค่อยๆ จาง ๆ ขึ้น (ปล่อยว่าง = โผล่ทันที)")]
        public CanvasGroup resultGroup;

        [Tooltip("ตัวที่จะเด้งขึ้นมา (ปล่อยว่าง = เด้งทั้ง panel)\n" +
                 "ถ้าฉากมืดคลุมจอเป็นลูกของ panel ต้องชี้มาที่กล่องข้างในแทน ไม่งั้นฉากมืดจะย่อตามจนเห็นขอบจอ")]
        public Transform popTarget;

        [Header("Buttons")]
        [Tooltip("ออกกลับเมนูหลัก — ทุกคนกดได้")]
        public Button exitButton;

        [Tooltip("เล่นใหม่ — โชว์เฉพาะ Host")]
        public Button rematchButton;

        [Tooltip("ข้อความบอก client ว่ารอ Host กดเล่นใหม่ (ปล่อยว่างได้)")]
        public GameObject waitingForHostText;

        [Header("Colors")]
        public Color victoryColor = new Color(0.30f, 0.95f, 0.55f, 1f);
        public Color defeatColor = new Color(0.95f, 0.28f, 0.32f, 1f);

        [Header("Animation")]
        public float resultPopDuration = 0.35f;
        public float fadeDuration = 0.35f;

        #endregion

        private PvpTeamManager manager;
        private bool subscribed;
        private bool panelShown;
        private bool leaving;      // กันกดปุ่มออกรัวๆ ระหว่างรอ shutdown
        private Tween resultTween;
        private Tween fadeTween;

        private void Awake()
        {
            // GameEnded เป็น static ค้างข้ามซีนได้ — ถ้าเพิ่งเล่นโหมดบอสจบแล้วเข้า PVP ต่อ
            // ค่ามันจะยังเป็น true อยู่ ทำให้มือ/เท้าไม่ยอมล็อกเมาส์ทั้งแมตช์ ต้องล้างทุกครั้งที่โหลดซีน
            GameFlowManager.GameEnded = false;
        }

        private void Start()
        {
            HidePanelInstant();
            WireButtons();
            TrySubscribe();
        }

        private void Update()
        {
            // manager อาจ spawn ช้ากว่า UI — ลองเกาะจนกว่าจะติด
            if (!subscribed) TrySubscribe();

            // จบแมตช์แล้วบังคับเมาส์หลุดล็อกตลอด — กันสคริปต์มือ/เท้าแย่งล็อกกลับ
            // (เหมือนที่ GameFlowManager ทำในซีนบอส) ไม่งั้นกดปุ่มบน panel ไม่ได้
            if (panelShown && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void TrySubscribe()
        {
            if (subscribed) return;

            manager = PvpTeamManager.Instance;
            if (manager == null || !manager.IsSpawned) return;

            manager.OnWinnerDecided += ShowWinner;
            manager.OnMatchStateChanged += OnMatchStateChanged;
            subscribed = true;

            // เข้ามากลางคัน (join สาย/รีเฟรช UI) → sync สถานะปัจจุบันทันที
            if (manager.MatchState == PvpMatchState.Finished && manager.Winner != PvpTeam.None)
                ShowWinner(manager.Winner);
        }

        private void OnMatchStateChanged(PvpMatchState state)
        {
            if (state != PvpMatchState.Finished) HidePanelInstant();
        }

        // ================================================================
        //  แสดงผล
        // ================================================================

        private void ShowWinner(PvpTeam winner)
        {
            if (resultPanel == null) return;

            PvpTeam myTeam = manager != null ? manager.LocalTeam : PvpTeam.None;
            bool spectator = myTeam == PvpTeam.None;
            bool won = !spectator && myTeam == winner;

            if (resultText != null)
            {
                resultText.text = spectator ? "MATCH OVER" : (won ? "VICTORY" : "DEFEAT");
                resultText.color = spectator ? Color.white : (won ? victoryColor : defeatColor);
            }

            if (subtitleText != null)
            {
                subtitleText.text = $"{winner.DisplayName().ToUpperInvariant()} TEAM WINS";
                subtitleText.color = winner.DisplayColor();
            }

            // Host เห็นปุ่มเล่นใหม่ / client เห็นข้อความรอ Host แทน
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (rematchButton != null) rematchButton.gameObject.SetActive(isHost);
            if (waitingForHostText != null) waitingForHostText.SetActive(!isHost);

            resultPanel.SetActive(true);
            panelShown = true;

            // 🖱️ ปลดเมาส์ให้กดปุ่มได้ — GameEnded คือ flag กลางที่มือ/เท้าเช็คเพื่อหยุดล็อกกลับ
            GameFlowManager.GameEnded = true;
            UiFocus.Push(this);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            KillTween(ref resultTween);
            Transform t = popTarget != null ? popTarget : resultPanel.transform;
            t.localScale = Vector3.one * 0.8f;
            resultTween = t.DOScale(Vector3.one, Mathf.Max(0.01f, resultPopDuration))
                           .SetEase(Ease.OutBack)
                           .SetUpdate(true);

            if (resultGroup == null) return;

            resultGroup.alpha = 0f;
            resultGroup.blocksRaycasts = true;
            resultGroup.interactable = true;

            KillTween(ref fadeTween);
            fadeTween = resultGroup.DOFade(1f, Mathf.Max(0.01f, fadeDuration))
                                   .SetEase(Ease.OutCubic)
                                   .SetUpdate(true);
        }

        private void HidePanelInstant()
        {
            KillTween(ref resultTween);
            KillTween(ref fadeTween);

            if (resultGroup != null)
            {
                resultGroup.alpha = 0f;
                resultGroup.blocksRaycasts = false;
                resultGroup.interactable = false;
            }

            if (resultPanel != null) resultPanel.SetActive(false);

            if (!panelShown) return;

            panelShown = false;
            GameFlowManager.GameEnded = false;
            UiFocus.Pop(this);
        }

        // ================================================================
        //  ปุ่ม
        // ================================================================

        private void WireButtons()
        {
            if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);
            if (rematchButton != null) rematchButton.onClick.AddListener(OnRematchClicked);
        }

        /// <summary>
        /// ออกกลับเมนูหลัก — ใช้ตัวเดียวกับปุ่ม LEAVE MATCH ในเมนูระหว่างเล่น
        /// มันจัดการ shutdown NetworkManager + ออก session + โหลดซีนเมนูให้ครบ
        /// ⚠️ ถ้าเครื่องนี้เป็น Host ห้องจะสลาย คนอื่นถูกเด้งกลับเมนูตามโดยอัตโนมัติ
        /// </summary>
        private void OnExitClicked()
        {
            if (leaving) return;
            leaving = true;

            if (exitButton != null) exitButton.interactable = false;
            if (rematchButton != null) rematchButton.interactable = false;

            GameFlowManager.GameEnded = false;
            UiFocus.Pop(this);

            _ = ReturnToMenuOnHostLost.LeaveMatchAsync();
        }

        /// <summary>Host กดเล่นใหม่ — โหลดซีนเดิมซ้ำผ่าน NGO ทุกเครื่องตามมาพร้อมกัน</summary>
        private void OnRematchClicked()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (leaving || nm == null || !nm.IsListening || !nm.IsServer) return;

            GameFlowManager.GameEnded = false;
            UiFocus.Pop(this);

            string currentScene = gameObject.scene.name;
            Debug.Log($"[PVP] 🔄 Host เล่นใหม่: {currentScene}");
            nm.SceneManager.LoadScene(currentScene, LoadSceneMode.Single);
        }

        // ================================================================

        private static void KillTween(ref Tween tween)
        {
            if (tween != null && tween.IsActive()) tween.Kill(false);
            tween = null;
        }

        private void OnDestroy()
        {
            if (subscribed && manager != null)
            {
                manager.OnWinnerDecided -= ShowWinner;
                manager.OnMatchStateChanged -= OnMatchStateChanged;
            }

            if (exitButton != null) exitButton.onClick.RemoveListener(OnExitClicked);
            if (rematchButton != null) rematchButton.onClick.RemoveListener(OnRematchClicked);

            // panel ยังเปิดค้างตอนโดนทำลาย (เปลี่ยนซีน) — ต้องคืน flag กลางไม่งั้นซีนหน้าเมาส์เพี้ยน
            if (panelShown)
            {
                GameFlowManager.GameEnded = false;
                UiFocus.Pop(this);
            }

            KillTween(ref resultTween);
            KillTween(ref fadeTween);
        }
    }
}
