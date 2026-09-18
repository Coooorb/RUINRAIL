using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    [CreateAssetMenu(fileName = "PlayerBalanceConfig", menuName = "RuinRail/Player/Player Balance Config")]
    public sealed class PlayerBalanceConfig : ScriptableObject
    {
        [SerializeField] private float _moveSpeed = 5f;

        // Dash tuning (14 / 07, design update 2026-09-17): a ~15 % nerf of both recharge rate and distance from the
        // original 20 u/s × 0.18 s = 3.6 tiles @ 1.25 s — speed 20 → 17 (3.06 tiles over the unchanged 0.18 s) and
        // cooldown 1.25 → 1.25 / 0.85 = 1.4706 s (recharge rate × 0.85). The i-frame window is untouched.
        [SerializeField] private float _dashSpeed = 17f;
        [SerializeField] private float _dashDuration = 0.18f;
        [SerializeField] private float _dashCooldown = 1.4706f;
        [SerializeField] private float _dashIFrameDuration = 0.10f;

        [SerializeField] private int _maxHealth = 100;

        [Header("Downed (84_DOWNED_REVIVE_DEATH)")]
        [Tooltip("Approved initial bleedout: 20 s.")]
        [SerializeField, Min(0f)] private float _downedBleedoutSeconds = 20f;
        [Tooltip("V1 FINAL (TASK 179) tunable: 'can crawl slowly' has no approved number; fraction of normal move speed while Downed.")]
        [SerializeField, Range(0f, 1f)] private float _downedCrawlSpeedMultiplier = 0.35f;

        [Header("Standard Revive (84_DOWNED_REVIVE_DEATH)")]
        [Tooltip("Approved initial revive channel: 4 s.")]
        [SerializeField, Min(0f)] private float _reviveChannelSeconds = 4f;
        [Tooltip("Approved: revived player returns at 30% Max HP.")]
        [SerializeField, Range(1, 100)] private int _reviveHealthPercent = 30;
        [Tooltip("Approved initial revive protection: ~1.5 s.")]
        [SerializeField, Min(0f)] private float _reviveProtectionSeconds = 1.5f;
        [Tooltip("V1 FINAL (TASK 179) tunable: 'near the Downed player' has no approved number; tiles between reviver and target.")]
        [SerializeField, Min(0.1f)] private float _reviveRangeTiles = 1.5f;

        public float MoveSpeed => _moveSpeed;

        public float DashSpeed => _dashSpeed;
        public float DashDuration => _dashDuration;
        public float DashCooldown => _dashCooldown;
        public float DashIFrameDuration => _dashIFrameDuration;

        public int MaxHealth => _maxHealth;

        public float DownedBleedoutSeconds => _downedBleedoutSeconds;
        public float DownedCrawlSpeedMultiplier => _downedCrawlSpeedMultiplier;

        public float ReviveChannelSeconds => _reviveChannelSeconds;
        public int ReviveHealthPercent => _reviveHealthPercent;
        public float ReviveProtectionSeconds => _reviveProtectionSeconds;
        public float ReviveRangeTiles => _reviveRangeTiles;
    }
}
