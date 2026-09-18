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
        [SerializeField] private float _hitFlashSeconds = 0.06f;
        [SerializeField] private Color _hitFlashColor = Color.white;
        [SerializeField] private Color _staggerFlashColor = new(1f, 0.85f, 0.4f);

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
