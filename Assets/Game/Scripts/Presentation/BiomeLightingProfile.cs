using RuinRail.Core;
using System;
using UnityEngine;

namespace RuinRail.Presentation
{
    /// <summary>
    /// art/102 lighting per biome: 2D lighting used sparingly for atmosphere; basic visibility never depends on dynamic
    /// lights, so the global light keeps every biome fully readable (intensity ≥ 1) and only the tint carries the mood.
    /// No post-processing is specified for V1, so none is enabled.
    ///
    /// The per-biome tints are V1 FINAL. TASK 179 had to record them as BLOCKED_REVIEW because the biome art they
    /// belong to did not exist yet; it now does, and each profile carries its authored ambient lean (art/106
    /// section 11) constrained by the readability floor below — see production/137.
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Presentation/Biome Lighting Profile", fileName = "Lighting_Biome")]
    public sealed class BiomeLightingProfile : ScriptableObject
    {
        /// <summary>art/102: basic visibility must not depend on lighting — the global light never dims below full.</summary>
        public const float MinimumGlobalIntensity = 1f;

        [SerializeField] private Biome _biome;
        [Tooltip("Global Light 2D colour; near-white keeps enemies, projectiles, loot, hazards and telegraphs readable (art/100).")]
        [SerializeField] private Color _globalLightColor = Color.white;
        [SerializeField] private float _globalLightIntensity = 1f;
        [Tooltip("Atmospheric point lights (lamps, fire, energy, boss effects) may be placed by rooms; purely decorative.")]
        [SerializeField] private bool _allowAtmosphericLights = true;
        [SerializeField] private bool _postProcessing;

        public Biome Biome => _biome;
        public Color GlobalLightColor => _globalLightColor;
        public float GlobalLightIntensity => _globalLightIntensity;
        public bool AllowAtmosphericLights => _allowAtmosphericLights;
        public bool PostProcessing => _postProcessing;

        /// <summary>Readability contract: full-intensity global light, no channel darker than 0.75 (V1 FINAL (TASK 179) floor), no post effects.</summary>
        public bool IsReadable => _globalLightIntensity >= MinimumGlobalIntensity && _globalLightColor.r >= 0.75f && _globalLightColor.g >= 0.75f && _globalLightColor.b >= 0.75f && !_postProcessing;

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only authoring of the biome's final ambient tint. Refuses anything that would break the readability
        /// contract, so lighting can never quietly become a visibility mechanic (art/102, art/106 section 11).
        /// </summary>
        public void EditorSetLighting(Color ambient, float intensity)
        {
            if (intensity < MinimumGlobalIntensity)
                throw new ArgumentOutOfRangeException(nameof(intensity), "Lighting is atmosphere, not a visibility requirement.");
            if (ambient.r < 0.75f || ambient.g < 0.75f || ambient.b < 0.75f)
                throw new ArgumentOutOfRangeException(nameof(ambient), "No channel may fall below the readability floor.");

            _globalLightColor = ambient;
            _globalLightIntensity = intensity;
            _postProcessing = false;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
