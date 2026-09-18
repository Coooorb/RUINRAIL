using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>
    /// Physics layers for actor bodies. Enemy and player bodies are solid against the world (walls, obstacles, sealed
    /// sockets, door blockers — all on Default) but not against each other: the approved feel is that a player is never
    /// pinned or shoved by a crowd, and before enemies had bodies at all the two never collided. Hurtboxes stay on
    /// Default so every hit path and aim query sees them.
    /// </summary>
    public static class CombatLayers
    {
        public const int Enemy = 8;
        public const int Player = 9;
        private static bool _applied;

        /// <summary>Configures the layer matrix once per process (idempotent).</summary>
        public static void Apply()
        {
            if (_applied) return;
            _applied = true;
            Physics2D.IgnoreLayerCollision(Enemy, Player, true);
        }

        public static void TagPlayerBody(GameObject player)
        {
            Apply();
            player.layer = Player;
        }

        public static void TagEnemyBody(GameObject enemy)
        {
            Apply();
            enemy.layer = Enemy;
        }
    }
}
