using NUnit.Framework;
using RuinRail.Gameplay.Items;

namespace RuinRail.Tests
{
    public class AmmoReserveTests
    {
        [Test]
        public void Get_DefaultsToZero_ForUnsetAmmoType()
        {
            var reserve = new AmmoReserve();
            Assert.AreEqual(0, reserve.Get(AmmoType.Light));
        }

        [Test]
        public void Add_IncreasesReserve()
        {
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 30);
            Assert.AreEqual(30, reserve.Get(AmmoType.Light));
        }

        [Test]
        public void Consume_ReducesReserveByExactAmount_WhenSufficient()
        {
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 10);

            var consumed = reserve.Consume(AmmoType.Light, 6);

            Assert.AreEqual(6, consumed);
            Assert.AreEqual(4, reserve.Get(AmmoType.Light));
        }

        [Test]
        public void Consume_NeverGoesNegative_WhenRequestingMoreThanAvailable()
        {
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 5);

            var consumed = reserve.Consume(AmmoType.Light, 100);

            Assert.AreEqual(5, consumed, "Only the available amount should be consumed.");
            Assert.AreEqual(0, reserve.Get(AmmoType.Light));
        }

        [Test]
        public void Consume_FromZeroReserve_ConsumesNothing()
        {
            var reserve = new AmmoReserve();

            var consumed = reserve.Consume(AmmoType.Light, 5);

            Assert.AreEqual(0, consumed);
            Assert.AreEqual(0, reserve.Get(AmmoType.Light));
        }

        [Test]
        public void AmmoTypes_AreIndependent()
        {
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 10);
            reserve.Add(AmmoType.Medium, 20);

            Assert.AreEqual(10, reserve.Get(AmmoType.Light));
            Assert.AreEqual(20, reserve.Get(AmmoType.Medium));
            Assert.AreEqual(0, reserve.Get(AmmoType.Heavy));
            Assert.AreEqual(0, reserve.Get(AmmoType.Shells));
        }
    }
}
