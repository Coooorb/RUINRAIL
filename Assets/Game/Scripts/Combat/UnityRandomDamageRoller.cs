using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    public sealed class UnityRandomDamageRoller : IDamageRoller
    {
        public int Roll(int minInclusive, int maxInclusive)
        {
            return Random.Range(minInclusive, maxInclusive + 1);
        }
    }
}
