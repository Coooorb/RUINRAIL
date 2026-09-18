using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Economy;

namespace RuinRail.Tests
{
    /// <summary>TASK 075: even party split of picked-up coins with a deterministic, rotating remainder policy.</summary>
    public class CoinDistributionTests
    {
        private static readonly string[] Duo = { "host", "p2" };
        private static readonly string[] Trio = { "host", "p2", "p3" };

        [Test]
        public void Solo_ReceivesTheFullAmount()
        {
            var shares = CoinDistribution.Split(37, new[] { "host" });
            Assert.AreEqual(1, shares.Count);
            Assert.AreEqual(37, shares[0].Amount);
            Assert.AreEqual("host", shares[0].ParticipantId);
        }

        [TestCase(10, 2, 0)]
        [TestCase(11, 2, 0)]
        [TestCase(11, 2, 1)]
        [TestCase(1, 3, 0)]
        [TestCase(2, 3, 2)]
        [TestCase(100, 3, 7)]
        [TestCase(31, 3, 5)]
        public void DuoAndTrioSplits_ConserveTheTotal_AndDifferByAtMostOneCoin(int total, int party, int sequence)
        {
            var ids = party == 2 ? Duo : Trio;
            var shares = CoinDistribution.Split(total, ids, sequence);

            Assert.AreEqual(party, shares.Count);
            Assert.AreEqual(total, shares.Sum(s => s.Amount), "Coins are conserved exactly.");
            Assert.LessOrEqual(shares.Max(s => s.Amount) - shares.Min(s => s.Amount), 1);
            CollectionAssert.AreEqual(ids, shares.Select(s => s.ParticipantId));
        }

        [Test]
        public void Remainder_GoesToConsecutiveParticipants_StartingAtSequenceModPartySize()
        {
            // 11 coins / 3 = 3 each + 2 leftover.
            CollectionAssert.AreEqual(new[] { 4, 4, 3 }, CoinDistribution.Split(11, Trio, 0).Select(s => s.Amount));
            CollectionAssert.AreEqual(new[] { 3, 4, 4 }, CoinDistribution.Split(11, Trio, 1).Select(s => s.Amount));
            CollectionAssert.AreEqual(new[] { 4, 3, 4 }, CoinDistribution.Split(11, Trio, 2).Select(s => s.Amount));
            CollectionAssert.AreEqual(new[] { 4, 4, 3 }, CoinDistribution.Split(11, Trio, 3).Select(s => s.Amount), "Rotation wraps.");

            // 7 coins / 2 = 3 each + 1 leftover.
            CollectionAssert.AreEqual(new[] { 4, 3 }, CoinDistribution.Split(7, Duo, 0).Select(s => s.Amount));
            CollectionAssert.AreEqual(new[] { 3, 4 }, CoinDistribution.Split(7, Duo, 1).Select(s => s.Amount));

            // No remainder: rotation changes nothing.
            CollectionAssert.AreEqual(new[] { 4, 4, 4 }, CoinDistribution.Split(12, Trio, 2).Select(s => s.Amount));
        }

        [Test]
        public void Split_IsDeterministic_ForTheSameInputs()
        {
            var a = CoinDistribution.Split(1234, Trio, 41).Select(s => (s.ParticipantId, s.Amount)).ToArray();
            var b = CoinDistribution.Split(1234, Trio, 41).Select(s => (s.ParticipantId, s.Amount)).ToArray();
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void Split_ReturnsNothing_ForNonPositiveAmountsOrEmptyRosters()
        {
            Assert.AreEqual(0, CoinDistribution.Split(0, Duo).Count);
            Assert.AreEqual(0, CoinDistribution.Split(-5, Duo).Count);
            Assert.AreEqual(0, CoinDistribution.Split(5, System.Array.Empty<string>()).Count);
            Assert.AreEqual(0, CoinDistribution.Split(5, null).Count);
        }

        [Test]
        public void PartyDistributor_CreditsEveryCarriedWallet_AndRotatesTheRemainderAcrossPickups()
        {
            var host = new CoinWallet(CoinDomain.Carried);
            var p2 = new CoinWallet(CoinDomain.Carried);
            var p3 = new CoinWallet(CoinDomain.Carried);
            var distributor = new PartyCoinDistributor(new CoinParticipant("host", host), new CoinParticipant("p2", p2), new CoinParticipant("p3", p3));
            var results = 0;
            distributor.Distributed += _ => results++;

            var first = distributor.Distribute(10, "pickup");
            var second = distributor.Distribute(10, "pickup");
            var third = distributor.Distribute(10, "pickup");

            Assert.AreEqual(30, host.Balance + p2.Balance + p3.Balance, "Every coin credited exactly once.");
            Assert.AreEqual(10, host.Balance);
            Assert.AreEqual(10, p2.Balance);
            Assert.AreEqual(10, p3.Balance);
            Assert.AreEqual(0, first.Sequence);
            Assert.AreEqual(2, third.Sequence);
            Assert.AreEqual(4, first.ShareFor("host"));
            Assert.AreEqual(3, second.ShareFor("host"));
            Assert.AreEqual(3, third.ShareFor("host"));
            Assert.AreEqual(3, results);
            Assert.AreEqual(10, first.Total);
        }

        [Test]
        public void PartyDistributor_SoloRoster_CreditsTheFullAmount_AndRejectsInvalidInput()
        {
            var host = new CoinWallet(CoinDomain.Carried);
            var distributor = new PartyCoinDistributor(new CoinParticipant("host", host));
            var result = distributor.Distribute(25, "pickup");
            Assert.AreEqual(25, host.Balance);
            Assert.AreEqual(25, result.ShareFor("host"));

            Assert.IsTrue(distributor.Distribute(0, "pickup").IsEmpty);
            Assert.AreEqual(25, host.Balance);

            var empty = new PartyCoinDistributor();
            Assert.IsTrue(empty.Distribute(5, "pickup").IsEmpty);
            Assert.Throws<System.ArgumentException>(() => new CoinParticipant("banked", new CoinWallet(CoinDomain.Banked)), "Pickups never credit Banked Coins.");
            Assert.Throws<System.ArgumentException>(() => new PartyCoinDistributor(new CoinParticipant("a", host), new CoinParticipant("a", new CoinWallet(CoinDomain.Carried))));
        }
    }
}
