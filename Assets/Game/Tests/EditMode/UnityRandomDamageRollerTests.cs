using NUnit.Framework;
using RuinRail.Gameplay.Combat;

namespace RuinRail.Tests
{
    public class UnityRandomDamageRollerTests
    {
        [Test]
        public void Roll_AlwaysReturnsIntegerWithinInclusiveRange()
        {
            var roller = new UnityRandomDamageRoller();

            for (var i = 0; i < 200; i++)
            {
                var damage = roller.Roll(12, 14);
                Assert.GreaterOrEqual(damage, 12);
                Assert.LessOrEqual(damage, 14);
            }
        }
    }
}
