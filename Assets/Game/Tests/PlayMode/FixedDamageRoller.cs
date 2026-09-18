using RuinRail.Gameplay.Combat;

namespace RuinRail.Tests
{
    internal sealed class FixedDamageRoller : IDamageRoller
    {
        public int FixedValue { get; set; } = 12;

        public int Roll(int minInclusive, int maxInclusive)
        {
            return FixedValue;
        }
    }
}
