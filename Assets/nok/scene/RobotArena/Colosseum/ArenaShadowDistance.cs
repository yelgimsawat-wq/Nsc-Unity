using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nsc.Arena
{
    // The colosseum is ~550 m across but the graphics presets (GraphicsQualityManager) only draw
    // shadows to 10-120 m. Stretch whatever the player's preset chose while this scene runs,
    // follow preset changes, and put the preset value back when the scene unloads.
    public class ArenaShadowDistance : MonoBehaviour
    {
        [Tooltip("Ultra preset 120 m x 3.75 = 450 m")]
        public float multiplier = 3.75f;
        public float maxDistance = 600f;

        private UniversalRenderPipelineAsset asset;
        private float presetDistance = -1f;

        private void OnEnable()
        {
            asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (GraphicsQualityManager.Instance != null)
                GraphicsQualityManager.Instance.OnPresetChanged += OnPresetChanged;
            Stretch();
        }

        private void OnDisable()
        {
            if (GraphicsQualityManager.Instance != null)
                GraphicsQualityManager.Instance.OnPresetChanged -= OnPresetChanged;
            if (asset != null && presetDistance >= 0f) asset.shadowDistance = presetDistance;
            presetDistance = -1f;
        }

        // The manager has just written the new preset's distance into the asset.
        private void OnPresetChanged(GraphicsPreset preset) => Stretch();

        private void Stretch()
        {
            if (asset == null) return;
            presetDistance = asset.shadowDistance;
            asset.shadowDistance = Mathf.Min(presetDistance * multiplier, maxDistance);
        }
    }
}
