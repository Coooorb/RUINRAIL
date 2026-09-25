using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Shared identity for every authored weapon: an equipment item (Id, rarity affix pool, Legendary mechanic)
    /// tagged with its approved weapon class so loot, pricing and affix pools can key on it.
    /// </summary>
    public abstract class WeaponDefinition : EquipmentItemDefinition
    {
        [SerializeField] private WeaponClass _weaponClass;
        [SerializeField, Min(0f)] private float _knockback;
        [SerializeField, Min(0f)] private float _staggerPower;
        [Tooltip("Optional in-flight projectile visual profile id (ProjectileVisualCatalog); empty = the class family default.")]
        [SerializeField] private string _projectileVisualId = string.Empty;

        public WeaponClass WeaponClass => _weaponClass;

        /// <summary>Per-weapon projectile visual override (presentation only); empty means the weapon-class family default.</summary>
        public string ProjectileVisualId => _projectileVisualId;

#if UNITY_EDITOR
        /// <summary>Editor-only binding used by the art pipeline. Never called at runtime.</summary>
        public void EditorSetProjectileVisualId(string id)
        {
            _projectileVisualId = id ?? string.Empty;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>Knockback points per hit before the wielder's Knockback stat (23_WEAPON_FRAMEWORK). 0 = none authored.</summary>
        public float Knockback => _knockback;

        /// <summary>Stagger pressure per hit before the wielder's Stagger Power stat. 0 = none authored.</summary>
        public float StaggerPower => _staggerPower;
    }
}
