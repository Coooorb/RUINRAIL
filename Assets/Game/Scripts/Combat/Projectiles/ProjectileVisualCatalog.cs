using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Projectiles
{
    /// <summary>
    /// The one projectile-visual catalog (release content): every in-flight projectile presentation profile the game
    /// ships — sprite frames, frame timing, optional trail — the family default for every ranged weapon class, and
    /// the hostile defaults. A shot names its profile by stable id in <see cref="ProjectileSpawnData.VisualId"/>; a
    /// weapon resolves its id from its own override or its class default (<see cref="ResolveWeaponVisualId"/>), an
    /// enemy attack from its definition or the hostile default. Presentation only: nothing here touches damage, speed,
    /// collision, range or authority.
    /// </summary>
    [CreateAssetMenu(fileName = "ProjectileVisualCatalog", menuName = "RuinRail/Presentation/Projectile Visual Catalog")]
    public sealed class ProjectileVisualCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Profile
        {
            public string Id = string.Empty;
            /// <summary>In-flight sprite frames (authored pointing +X); one frame = static, more = looped animation.</summary>
            public Sprite[] Frames = Array.Empty<Sprite>();
            public float FrameSeconds = 0.06f;
            /// <summary>Optional trail sprite drawn behind the body along −velocity (rocket exhaust, tracer tail).</summary>
            public Sprite Trail;
            /// <summary>How far behind the body's pivot the trail's pivot sits (world units).</summary>
            public float TrailBack = 0.3f;
            /// <summary>
            /// Whole-number presentation scale of the sprite (1 = authored size). Boss projectiles draw at 2x so a
            /// boss's volley reads at gameplay distance; the hitbox is the attack's own and never follows this.
            /// </summary>
            public int Scale = 1;
            public int EffectiveScale => Scale < 1 ? 1 : Scale;
            public bool IsValid => !string.IsNullOrEmpty(Id) && Frames != null && Frames.Length > 0 && Frames.All(f => f != null);
        }

        [Serializable]
        public struct FamilyDefault
        {
            public WeaponClass Class;
            public string ProfileId;
        }

        [SerializeField] private List<Profile> _profiles = new();
        [SerializeField] private List<FamilyDefault> _familyDefaults = new();
        [SerializeField] private string _playerDefaultId = "proj_rifle";
        [SerializeField] private string _enemyDefaultId = "proj_enemy_round";

        /// <summary>The catalog the running game resolves through (set by the composition root; null = no projectile art bound).</summary>
        public static ProjectileVisualCatalog Active { get; set; }

        public IReadOnlyList<Profile> Profiles => _profiles;
        public IReadOnlyList<FamilyDefault> FamilyDefaults => _familyDefaults;
        public string PlayerDefaultId => _playerDefaultId;
        public string EnemyDefaultId => _enemyDefaultId;

        public Profile Find(string id) => string.IsNullOrEmpty(id) ? null : _profiles.FirstOrDefault(p => p != null && p.Id == id);

        public string FamilyDefaultFor(WeaponClass weaponClass)
        {
            foreach (var entry in _familyDefaults) if (entry.Class == weaponClass) return entry.ProfileId;
            return null;
        }

        /// <summary>The profile id a weapon's shots carry: its own override when authored, else its class family default.</summary>
        public static string ResolveWeaponVisualId(WeaponDefinition weapon)
        {
            if (weapon == null) return null;
            if (!string.IsNullOrEmpty(weapon.ProjectileVisualId)) return weapon.ProjectileVisualId;
            return Active != null ? Active.FamilyDefaultFor(weapon.WeaponClass) : null;
        }

        /// <summary>The profile a spawned projectile shows: its named profile, else the side's default; null when no catalog is active.</summary>
        public Profile Resolve(string visualId, DamageTeam team)
        {
            var named = Find(visualId);
            if (named != null && named.IsValid) return named;
            var fallback = Find(team == DamageTeam.Player ? _playerDefaultId : _enemyDefaultId);
            return fallback != null && fallback.IsValid ? fallback : null;
        }

        /// <summary>Everything the release path requires: valid profiles, a default for every ranged weapon class, the side defaults present.</summary>
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in _profiles)
            {
                if (profile == null || string.IsNullOrEmpty(profile.Id)) { problems.Add("a profile has no id"); continue; }
                if (!ids.Add(profile.Id)) problems.Add($"duplicate profile id '{profile.Id}'");
                if (!profile.IsValid) problems.Add($"profile '{profile.Id}' has no frames");
            }

            foreach (WeaponClass weaponClass in Enum.GetValues(typeof(WeaponClass)))
            {
                if (weaponClass == WeaponClass.Knife || weaponClass == WeaponClass.Spear) continue;
                var id = FamilyDefaultFor(weaponClass);
                if (string.IsNullOrEmpty(id)) problems.Add($"no family default for {weaponClass}");
                else if (Find(id) == null) problems.Add($"family default for {weaponClass} names unknown profile '{id}'");
            }

            if (Find(_playerDefaultId) == null) problems.Add($"player default '{_playerDefaultId}' is not a profile");
            if (Find(_enemyDefaultId) == null) problems.Add($"enemy default '{_enemyDefaultId}' is not a profile");
            return problems;
        }

#if UNITY_EDITOR
        /// <summary>Editor-only authoring seam for the art pipeline.</summary>
        public void EditorSet(List<Profile> profiles, List<FamilyDefault> familyDefaults, string playerDefaultId, string enemyDefaultId)
        {
            _profiles = profiles;
            _familyDefaults = familyDefaults;
            _playerDefaultId = playerDefaultId;
            _enemyDefaultId = enemyDefaultId;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
