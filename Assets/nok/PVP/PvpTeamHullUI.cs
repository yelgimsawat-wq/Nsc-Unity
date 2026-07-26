using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NscGame.Pvp
{
    /// <summary>
    /// หลอดเลือดรวมของ "หุ่นทีมเราเอง" — ไม่ใช่ของฝั่งตรงข้าม
    ///
    /// ทำไมต้องมี:
    ///   HullRingUI เดิมโชว์เลือดของชิ้นส่วนที่ผู้เล่นคนนี้ถืออยู่ชิ้นเดียว
    ///   เวลาโดนต่อยลำตัวหรือแขนอีกข้าง วงแหวนจึงไม่ขยับ → ผู้เล่นเข้าใจว่า "โดนต่อยแล้วเลือดไม่ลด"
    ///   ตัวนี้บวกเลือดทุกชิ้นของหุ่นทั้งตัว โดนที่ไหนก็เห็น
    ///
    /// อ่านค่าจาก NetworkVariable ของ RobotHealth เดิมทั้งหมด ไม่มี state ของตัวเอง
    /// </summary>
    public class PvpTeamHullUI : MonoBehaviour
    {
        #region Inspector

        [Header("Refs")]
        [SerializeField] private GameObject root;
        [SerializeField] private Image fillImage;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private TMP_Text limbCountText;

        [Header("Colors")]
        [SerializeField] private Color fullColor = new Color(0.35f, 0.85f, 0.45f, 1f);
        [SerializeField] private Color lowColor  = new Color(0.90f, 0.25f, 0.25f, 1f);

        [Tooltip("ความเร็วที่หลอดไหลตามค่าจริง (0 = เปลี่ยนทันที)")]
        [SerializeField] private float lerpSpeed = 6f;

        #endregion

        private PvpRobotTeam myRobot;
        private PvpTeam boundTeam = PvpTeam.None;
        private float shownNormalized = 1f;

        private void OnEnable()
        {
            PvpRobotTeam.OnRegistryChanged += Rebind;
            Rebind();
        }

        private void OnDisable()
        {
            PvpRobotTeam.OnRegistryChanged -= Rebind;
        }

        /// <summary>
        /// ทีมของเราเปลี่ยนได้ตลอดตอนอยู่หน้าเลือกทีม จึงต้องเกาะใหม่เมื่อทีมไม่ตรง
        /// (เรียกทั้งจาก registry event และจาก Update)
        /// </summary>
        private void Rebind()
        {
            PvpTeamManager manager = PvpTeamManager.Instance;
            PvpTeam team = manager != null ? manager.LocalTeam : PvpTeam.None;

            if (team == boundTeam && myRobot != null) return;

            boundTeam = team;
            myRobot = team == PvpTeam.None ? null : PvpRobotTeam.FindByTeam(team);
        }

        private void Update()
        {
            Rebind();

            PvpTeamManager manager = PvpTeamManager.Instance;
            bool show = manager != null
                        && manager.MatchState != PvpMatchState.TeamSelect
                        && myRobot != null;

            if (root != null && root.activeSelf != show) root.SetActive(show);
            if (!show) return;

            if (!myRobot.TryGetTotalHp(out float current, out float max)) return;

            float target = Mathf.Clamp01(current / Mathf.Max(1f, max));

            shownNormalized = lerpSpeed > 0.01f
                ? Mathf.Lerp(shownNormalized, target, 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime))
                : target;

            if (fillImage != null)
            {
                fillImage.fillAmount = shownNormalized;
                fillImage.color = Color.Lerp(lowColor, fullColor, target);
            }

            if (valueText != null)
                valueText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";

            // จำนวนชิ้นที่ยังต่ออยู่ — ตัวชี้วัดตรงๆ ว่าใกล้แพ้แค่ไหน (หลุดครบ = แพ้)
            if (limbCountText != null)
                limbCountText.text = $"PARTS {myRobot.LimbsRemaining}/{myRobot.LimbsTotal}";
        }
    }
}
