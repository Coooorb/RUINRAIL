using RuinRail.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RuinRail.Presentation
{
    /// <summary>Applies a biome profile to the scene's global Light 2D (created if missing) and the camera's post-processing flag.</summary>
    public sealed class BiomeLightingApplier : MonoBehaviour
    {
        [SerializeField] private Light2D _globalLight;
        [SerializeField] private Camera _camera;

        public Light2D GlobalLight => _globalLight;
        public BiomeLightingProfile Applied { get; private set; }

        public void SetCamera(Camera camera) => _camera = camera;

        public void Apply(BiomeLightingProfile profile)
        {
            if (profile == null) return;
            if (_globalLight == null)
            {
                var go = new GameObject("GlobalLight2D");
                go.transform.SetParent(transform, false);
                _globalLight = go.AddComponent<Light2D>();
                _globalLight.lightType = Light2D.LightType.Global;
            }

            _globalLight.color = profile.GlobalLightColor;
            _globalLight.intensity = profile.GlobalLightIntensity;
            if (_camera != null)
            {
                var data = _camera.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = profile.PostProcessing;
            }

            Applied = profile;
        }
    }
}
