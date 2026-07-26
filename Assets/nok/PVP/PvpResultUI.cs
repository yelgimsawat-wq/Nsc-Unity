using DG.Tweening;
using TMPro;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// ป้ายประกาศผู้ชนะตอนจบแมตช์ — เท่านี้จริงๆ
    ///
    /// ⚠️ ไม่มีหลอดเลือดในนี้ เพราะเลือดใช้ PlayerHUD เดิม (HullRingUI + LimbStatusUI)
    ///    ที่ผูกกับ "หุ่นของเราเอง" ผ่าน LocalRobotBinder อยู่แล้ว
    ///    และดีไซน์ที่ตกลงกันคือผู้เล่นไม่เห็นเลือดฝั่งตรงข้าม ต้องดูสภาพหุ่นศัตรูจากในเกมเอา
    /// </summary>
    public class PvpResultUI : MonoBehaviour
    {
        #region Inspector

        [Header("Result")]
        public GameObject resultPanel;
        public TextMeshProUGUI resultText;

        [Header("Animation")]
        public float resultPopDuration = 0.35f;

        #endregion

        private PvpTeamManager manager;
        private bool subscribed;
        private Tween resultTween;

        private void Start()
        {
            if (resultPanel != null) resultPanel.SetActive(false);
            TrySubscribe();
        }

        private void Update()
        {
            // manager อาจ spawn ช้ากว่า UI — ลองเกาะจนกว่าจะติด
            if (!subscribed) TrySubscribe();
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
            if (state != PvpMatchState.Finished && resultPanel != null)
                resultPanel.SetActive(false);
        }

        private void ShowWinner(PvpTeam winner)
        {
            if (resultPanel == null || resultText == null) return;

            resultPanel.SetActive(true);
            resultText.text = $"{winner.DisplayName()} TEAM WINS!";
            resultText.color = winner.DisplayColor();

            KillTween(ref resultTween);

            Transform t = resultPanel.transform;
            t.localScale = Vector3.one * 0.8f;

            resultTween = t.DOScale(Vector3.one, Mathf.Max(0.01f, resultPopDuration))
                           .SetEase(Ease.OutBack)
                           .SetUpdate(true);
        }

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
            KillTween(ref resultTween);
        }
    }
}
