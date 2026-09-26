using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// art/104 feedback tunables. Every number here is a V1 FINAL (TASK 179) default for the visual layer (the spec fixes the
    /// hierarchy — minimal for small guns, stronger for shotgun/rocket/boss slam — not the amounts); none affects gameplay.
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Presentation/Feedback Config", fileName = "FeedbackConfig")]
    public sealed class FeedbackConfig : ScriptableObject
    {
        [Header("Screen shake (pixels / seconds); scaled by the accessibility setting")]
        [SerializeField] private float _smallGunShakePixels = 0.5f;
        [SerializeField] private float _smallGunShakeSeconds = 0.05f;
        [SerializeField] private float _shotgunShakePixels = 2f;
        [SerializeField] private float _shotgunShakeSeconds = 0.12f;
        [SerializeField] private float _explosionShakePixels = 3f;
        [SerializeField] private float _explosionShakeSeconds = 0.2f;
        [SerializeField] private float _bossSlamShakePixels = 4f;
        [SerializeField] private float _bossSlamShakeSeconds = 0.25f;

        [Header("Hit flash")]
        [Tooltip("Tunable presentation: how long an enemy's damage tint holds; a new hit restarts it.")]
        [SerializeField] private float _hitFlashSeconds = 0.1f;
        [Tooltip("Tunable presentation: the red an enemy's body is multiplied by on an applied hit (white would be invisible).")]
        [SerializeField] private Color _hitFlashColor = new(1f, 0.38f, 0.38f, 1f);
        [SerializeField] private Color _staggerFlashColor = new(1f, 0.85f, 0.4f);
        [Tooltip("Tunable presentation: the local survivor's red tint (multiplied onto the body sprite) on every applied hit.")]
        [SerializeField] private Color _playerHitFlashColor = new(1f, 0.32f, 0.32f, 1f);
        [Tooltip("Tunable presentation: how long the survivor's damage tint holds; a new hit restarts it.")]
        [SerializeField, Min(0.02f)] private float _playerHitFlashSeconds = 0.12f;
        [Tooltip("Tunable presentation: a boss's tint for the weakest applied hit (subtle).")]
        [SerializeField] private Color _bossFlashWeakColor = new(1f, 0.72f, 0.72f, 1f);
        [Tooltip("Tunable presentation: a boss's tint at full strength — the clamp that keeps the sprite's detail readable.")]
        [SerializeField] private Color _bossFlashStrongColor = new(1f, 0.28f, 0.28f, 1f);
        [Tooltip("Tunable presentation: a single applied hit worth this share of the boss's max HP reaches the full-strength tint.")]
        [SerializeField, Range(0.005f, 0.5f)] private float _bossFlashFullAtHpFraction = 0.04f;
        [Tooltip("Tunable presentation: how long a boss's damage tint holds; a new hit restarts it.")]
        [SerializeField, Min(0.02f)] private float _bossFlashSeconds = 0.12f;

        [Header("Effect lifetimes (seconds)")]
        [SerializeField] private float _muzzleFlashSeconds = 0.05f;
        [SerializeField] private float _impactSeconds = 0.12f;
        [SerializeField] private float _explosionSeconds = 0.35f;
        [SerializeField] private float _meleeArcSeconds = 0.15f;
        [SerializeField] private float _staggerSeconds = 0.25f;
        [SerializeField] private float _healSeconds = 0.5f;
        [SerializeField] private float _statusSeconds = 0.4f;

        [Header("Damage numbers")]
        [SerializeField] private float _damageNumberSeconds = 0.7f;
        [SerializeField] private float _damageNumberRisePixels = 12f;
        [SerializeField] private int _damageNumberCapacity = 48;

        [Header("Telegraphs")]
        [SerializeField] private Color _enemyTelegraphColor = new(1f, 0.45f, 0.1f, 0.55f);
        [SerializeField] private Color _eliteBossTelegraphColor = new(1f, 0.1f, 0.1f, 0.7f);

        [Header("Loot glow")]
        [SerializeField] private float _legendaryGlowScale = 2.5f;
        [SerializeField] private float _rareGlowScale = 1.4f;

        public float SmallGunShakePixels => _smallGunShakePixels;
        public float SmallGunShakeSeconds => _smallGunShakeSeconds;
        public float ShotgunShakePixels => _shotgunShakePixels;
        public float ShotgunShakeSeconds => _shotgunShakeSeconds;
        public float ExplosionShakePixels => _explosionShakePixels;
        public float ExplosionShakeSeconds => _explosionShakeSeconds;
        public float BossSlamShakePixels => _bossSlamShakePixels;
        public float BossSlamShakeSeconds => _bossSlamShakeSeconds;
        public float HitFlashSeconds => _hitFlashSeconds;
        public Color HitFlashColor => _hitFlashColor;
        public Color StaggerFlashColor => _staggerFlashColor;
        public Color PlayerHitFlashColor => _playerHitFlashColor;
        public float PlayerHitFlashSeconds => _playerHitFlashSeconds;
        public Color BossFlashWeakColor => _bossFlashWeakColor;
        public Color BossFlashStrongColor => _bossFlashStrongColor;
        public float BossFlashFullAtHpFraction => _bossFlashFullAtHpFraction;
        public float BossFlashSeconds => _bossFlashSeconds;

        /// <summary>
        /// 0..1 strength of a boss's damage tint for one hit: the effective damage that hit applied as a share of the
        /// boss's max HP, against the share that reaches full strength. Non-decreasing in the damage and clamped, so a
        /// huge hit is the strongest tint and never more; relative to max HP, so it reads the same for every boss,
        /// depth and party size.
        /// </summary>
        public float BossFlashIntensity(int appliedDamage, int maxHealth)
        {
            if (appliedDamage <= 0 || maxHealth <= 0) return 0f;
            return Mathf.Clamp01(appliedDamage / (maxHealth * Mathf.Max(0.0001f, _bossFlashFullAtHpFraction)));
        }

        /// <summary>The boss tint for one hit: weak colour at the smallest hit, strong colour at (and beyond) full strength.</summary>
        public Color BossFlashColor(int appliedDamage, int maxHealth) =>
            Color.Lerp(_bossFlashWeakColor, _bossFlashStrongColor, BossFlashIntensity(appliedDamage, maxHealth));
        public float MuzzleFlashSeconds => _muzzleFlashSeconds;
        public float ImpactSeconds => _impactSeconds;
        public float ExplosionSeconds => _explosionSeconds;
        public float MeleeArcSeconds => _meleeArcSeconds;
        public float StaggerSeconds => _staggerSeconds;
        public float HealSeconds => _healSeconds;
        public float StatusSeconds => _statusSeconds;
        public float DamageNumberSeconds => _damageNumberSeconds;
        public float DamageNumberRisePixels => _damageNumberRisePixels;
        public int DamageNumberCapacity => _damageNumberCapacity;
        public Color EnemyTelegraphColor => _enemyTelegraphColor;
        public Color EliteBossTelegraphColor => _eliteBossTelegraphColor;
        public float LegendaryGlowScale => _legendaryGlowScale;
        public float RareGlowScale => _rareGlowScale;

        /// <summary>art/104 ordering: small gun &lt; shotgun &lt; explosion ≤ boss slam.</summary>
        public bool IsOrdered => _smallGunShakePixels < _shotgunShakePixels && _shotgunShakePixels < _explosionShakePixels && _explosionShakePixels <= _bossSlamShakePixels;
    }
}
