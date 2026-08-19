// TEMPORARY TEST HARNESS — delete Assets/_ArmFeelTest when tuning is done.
// Drives the hand target through a step input and measures how the arm responds,
// so control feel can be tuned against numbers instead of impressions.
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class ArmStepTest : MonoBehaviour
{
    [Tooltip("Seconds to hold the rest pose before stepping.")]
    public float settleTime = 1.5f;
    [Tooltip("Seconds to record after the step.")]
    public float recordTime = 2.0f;
    [Tooltip("How far sideways the target jumps, as a fraction of maxArmLength.")]
    public float stepFraction = 0.55f;

    private static readonly FieldInfo TargetField = typeof(PlayerHandMovement)
        .GetField("targetHandPosition", BindingFlags.NonPublic | BindingFlags.Instance);

    private class Probe
    {
        public PlayerHandCombat hand;
        public Vector3 restTarget, stepTarget;
        public float stepDistance;
        public List<float> err = new List<float>();   // distance from target, per physics step
        public bool done;
    }

    /// <summary>Result of the last completed run — read this instead of scraping the console.</summary>
    public static string LastReport = "(not run)";

    private readonly List<Probe> probes = new List<Probe>();
    private float t;
    private bool stepped, reported;

    private void Start()
    {
        if (TargetField == null)
        {
            Debug.LogError("[ArmStepTest] field 'targetHandPosition' not found — harness cannot run.");
            enabled = false;
            return;
        }

        foreach (var h in FindObjectsByType<PlayerHandCombat>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (h.handRb == null || h.pivotPoint == null) continue;
            // Silence the real input path: Update() early-returns when playerCamera is null,
            // so no UpdateHandTargetRpc can overwrite the target we are driving.
            h.playerCamera = null;
            Vector3 pivot = h.pivotPoint.TransformPoint(h.pivotOffset);
            float reach = h.maxArmLength;
            var p = new Probe { hand = h };
            // Rest: hanging below the shoulder. Step: same depth, swung sideways.
            p.restTarget = pivot + Vector3.down * (reach * 0.5f);
            p.stepTarget = p.restTarget + h.transform.right * (reach * stepFraction);
            p.stepDistance = Vector3.Distance(p.restTarget, p.stepTarget);
            probes.Add(p);
        }
        PrepareBodies();
        Debug.Log("[ArmStepTest] armed with " + probes.Count + " arm(s)");
    }

    /// <summary>
    /// Put the rig into the one state where arm response is actually measurable:
    ///
    ///  • Torso pinned (kinematic) and forced Standing. Otherwise the robot ragdolls the
    ///    instant Play starts — nobody is driving the legs — and PerformArmMovement drops
    ///    the hand spring to ragdollHandSpringScale, so we would be measuring a limp arm
    ///    on a body lying on the floor. A pinned torso is the closest stable stand-in for
    ///    "a robot the players are successfully holding upright".
    ///  • Arm chain un-frozen. The robot spawns with every rigidbody kinematic and asleep
    ///    because LobbyManager only unfreezes physics once the host presses Start, and this
    ///    harness never goes through the lobby.
    /// </summary>
    private void PrepareBodies()
    {
        foreach (var p in probes)
        {
            var torso = p.hand.torso;
            if (torso != null)
            {
                if (torso.IsServer) torso.currentState.Value = TorsoMovement.TorsoState.Standing;
                if (torso.torsoRb != null) torso.torsoRb.isKinematic = true;
            }

            // Walk hand -> ... -> torso and wake every link except the pinned torso.
            var rb = p.hand.handRb;
            for (int hop = 0; rb != null && hop < 8; hop++)
            {
                if (torso != null && rb == torso.torsoRb) break;
                if (rb.isKinematic) rb.isKinematic = false;
                if (rb.IsSleeping()) rb.WakeUp();
                var joints = rb.GetComponents<Joint>();
                rb = joints.Length > 0 ? joints[0].connectedBody : null;
            }
        }
    }

    private void FixedUpdate()
    {
        if (probes.Count == 0) return;
        PrepareBodies(); // TorsoMovement re-evaluates balance every step and would flip it back
        if (reported)
        {
            // Keep holding the step target so the steady state can be inspected live.
            foreach (var p in probes) TargetField.SetValue(p.hand, p.stepTarget);
            return;
        }
        t += Time.fixedDeltaTime;

        if (!stepped)
        {
            foreach (var p in probes) TargetField.SetValue(p.hand, p.restTarget);
            if (t >= settleTime) { stepped = true; t = 0f; }
            return;
        }

        foreach (var p in probes)
        {
            TargetField.SetValue(p.hand, p.stepTarget);
            p.err.Add(Vector3.Distance(p.hand.handRb.position, p.stepTarget));
        }

        if (t >= recordTime) { Report(); reported = true; }
    }

    private void Report()
    {
        var report = new System.Text.StringBuilder();
        float dt = Time.fixedDeltaTime;
        foreach (var p in probes)
        {
            int n = p.err.Count;
            if (n < 4) { Debug.Log("[ArmStepTest] " + p.hand.name + ": too few samples"); continue; }

            float d0 = p.stepDistance;
            float rise = -1f, settle = -1f, closest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (rise < 0f && p.err[i] <= d0 * 0.10f) rise = i * dt;      // reached 90% of the way
                if (p.err[i] < closest) closest = p.err[i];
            }
            // settle = last time it was still outside a 5% band
            for (int i = n - 1; i >= 0; i--)
                if (p.err[i] > d0 * 0.05f) { settle = (i + 1) * dt; break; }
            if (settle < 0f) settle = 0f;

            // overshoot: how far past the target it travelled (error grows again after closest approach)
            float overshoot = 0f;
            bool passed = false;
            for (int i = 0; i < n; i++)
            {
                if (!passed && p.err[i] <= d0 * 0.05f) passed = true;
                else if (passed) overshoot = Mathf.Max(overshoot, p.err[i]);
            }

            // steady state + jitter over the final 0.5 s
            int tail = Mathf.Max(1, Mathf.RoundToInt(0.5f / dt));
            float sum = 0f, sum2 = 0f;
            for (int i = n - tail; i < n; i++) { sum += p.err[i]; sum2 += p.err[i] * p.err[i]; }
            float mean = sum / tail;
            float jitter = Mathf.Sqrt(Mathf.Max(0f, sum2 / tail - mean * mean));

            report.AppendLine(string.Format(
                "{0} | step={1:F1}m | rise90={2} | settle5%={3} | overshoot={4:F2}m ({5:F0}%) | steadyErr={6:F2}m ({7:F0}%) | jitter={8:F3}m",
                p.hand.name, d0,
                rise < 0f ? "NEVER" : rise.ToString("F3") + "s",
                settle.ToString("F3") + "s",
                overshoot, 100f * overshoot / d0,
                mean, 100f * mean / d0,
                jitter));
        }
        // Geometry dump — if the hand never approaches the target, these say why.
        foreach (var p in probes)
        {
            Vector3 pivot = p.hand.pivotPoint.TransformPoint(p.hand.pivotOffset);
            report.AppendLine(string.Format(
                "   {0} geom: pivot->target={1:F1}m (reach limit {2:F1}m) | pivot->hand={3:F1}m | handPos={4} target={5} | torso={6}",
                p.hand.name,
                Vector3.Distance(pivot, p.stepTarget), p.hand.maxArmLength,
                Vector3.Distance(pivot, p.hand.handRb.position),
                p.hand.handRb.position.ToString("F1"), p.stepTarget.ToString("F1"),
                p.hand.torso == null ? "null" : p.hand.torso.currentState.Value.ToString()));
        }
        LastReport = report.ToString();
        Debug.Log("[ArmStepTest] DONE\n" + LastReport);
    }
}
