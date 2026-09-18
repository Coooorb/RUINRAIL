using System;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Encounters
{
    /// <summary>
    /// Centralised endless-depth curves (59_DEPTH_SCALING). Anchors default to the approved checkpoints; between anchors
    /// values interpolate linearly, past the last anchor they hold (a documented cap). Attack-frequency and movement
    /// scaling stay slight and cap so deep enemies never become unreadable.
    /// </summary>
    [CreateAssetMenu(fileName = "DepthScalingConfig", menuName = "RuinRail/Balance/Depth Scaling Config")]
    public sealed class DepthScalingConfig : ScriptableObject
    {
        [Serializable]
        public struct Anchor
        {
            public int Depth;
            public int Percent;
        }

        [Serializable]
        public struct ChanceBand
        {
            public int FromDepth;
            public int Percent;
        }

        [SerializeField] private Anchor[] _healthPercent =
        {
            new() { Depth = 1, Percent = 100 }, new() { Depth = 2, Percent = 108 }, new() { Depth = 3, Percent = 116 }, new() { Depth = 5, Percent = 132 },
            new() { Depth = 10, Percent = 170 }, new() { Depth = 20, Percent = 235 }, new() { Depth = 30, Percent = 290 }, new() { Depth = 50, Percent = 380 }, new() { Depth = 100, Percent = 550 }
        };

        [SerializeField] private Anchor[] _damagePercent =
        {
            new() { Depth = 1, Percent = 100 }, new() { Depth = 5, Percent = 115 }, new() { Depth = 10, Percent = 130 }, new() { Depth = 20, Percent = 155 },
            new() { Depth = 30, Percent = 175 }, new() { Depth = 50, Percent = 205 }, new() { Depth = 100, Percent = 260 }
        };

        [Tooltip("59: attack frequency reaches only about +10% by very deep play, then caps (V1 FINAL (TASK 179) curve: linear to Depth 50).")]
        [SerializeField] private Anchor[] _attackSpeedPercent = { new() { Depth = 1, Percent = 100 }, new() { Depth = 50, Percent = 110 } };

        [Tooltip("59: movement scales only slightly (V1 FINAL (TASK 179): +5% by Depth 50, then caps).")]
        [SerializeField] private Anchor[] _movementSpeedPercent = { new() { Depth = 1, Percent = 100 }, new() { Depth = 50, Percent = 105 } };

        [SerializeField] private ChanceBand[] _eliteChancePercent =
        {
            new() { FromDepth = 1, Percent = 5 }, new() { FromDepth = 3, Percent = 10 }, new() { FromDepth = 6, Percent = 15 }, new() { FromDepth = 11, Percent = 20 }, new() { FromDepth = 21, Percent = 25 }
        };

        public Anchor[] HealthPercent => _healthPercent;
        public Anchor[] DamagePercent => _damagePercent;
        public Anchor[] AttackSpeedPercent => _attackSpeedPercent;
        public Anchor[] MovementSpeedPercent => _movementSpeedPercent;
        public ChanceBand[] EliteChancePercent => _eliteChancePercent;
    }
}
