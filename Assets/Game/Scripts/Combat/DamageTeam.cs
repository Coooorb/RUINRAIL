using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>Who an actor fights for. Player-sourced attacks never damage Players (friendly fire is OFF); environmental hazards may.</summary>
    public enum DamageTeam
    {
        Enemy,
        Player,
        Neutral
    }

}
