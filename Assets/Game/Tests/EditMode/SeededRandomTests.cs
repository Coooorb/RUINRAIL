using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rng;

namespace RuinRail.Tests
{
    public class SeededRandomTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalSequence()
        {
            var a = new SeededRandom(1234);
            var b = new SeededRandom(1234);

            var seqA = Enumerable.Range(0, 50).Select(_ => a.NextInt(1, 1000)).ToArray();
            var seqB = Enumerable.Range(0, 50).Select(_ => b.NextInt(1, 1000)).ToArray();

            CollectionAssert.AreEqual(seqA, seqB);
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new SeededRandom(1);
            var b = new SeededRandom(2);

            var seqA = Enumerable.Range(0, 20).Select(_ => a.NextInt(1, 1000)).ToArray();
            var seqB = Enumerable.Range(0, 20).Select(_ => b.NextInt(1, 1000)).ToArray();

            CollectionAssert.AreNotEqual(seqA, seqB);
        }

        [Test]
        public void NextInt_InclusiveRange_StaysInsideBounds_AndReachesBothEnds()
        {
            var rng = new SeededRandom(99);
            var values = Enumerable.Range(0, 2000).Select(_ => rng.NextInt(6, 10)).ToArray();

            Assert.IsTrue(values.All(v => v >= 6 && v <= 10));
            Assert.Contains(6, values);
            Assert.Contains(10, values);
        }

        [Test]
        public void NextInt_Exclusive_StaysInsideBounds()
        {
            var rng = new SeededRandom(7);
            var values = Enumerable.Range(0, 500).Select(_ => rng.NextInt(3)).ToArray();

            Assert.IsTrue(values.All(v => v >= 0 && v < 3));
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, values.Distinct().OrderBy(v => v));
        }

        [Test]
        public void NextFloat_StaysInUnitInterval()
        {
            var rng = new SeededRandom(5);
            Assert.IsTrue(Enumerable.Range(0, 500).Select(_ => rng.NextFloat()).All(f => f >= 0f && f < 1f));
        }

        [Test]
        public void Streams_AreIndependent_AndReproducibleFromRunSeedAndDepth()
        {
            var lootA = RngStreams.Derive(42, 3, RngStream.Loot);
            var lootB = RngStreams.Derive(42, 3, RngStream.Loot);
            var dungeon = RngStreams.Derive(42, 3, RngStream.Dungeon);
            var encounter = RngStreams.Derive(42, 3, RngStream.Encounter);
            var lootOtherDepth = RngStreams.Derive(42, 4, RngStream.Loot);

            // Consuming the dungeon stream must not perturb the loot stream.
            for (var i = 0; i < 10; i++) dungeon.NextInt(100);

            var seqA = Enumerable.Range(0, 20).Select(_ => lootA.NextInt(1, 1000)).ToArray();
            var seqB = Enumerable.Range(0, 20).Select(_ => lootB.NextInt(1, 1000)).ToArray();
            CollectionAssert.AreEqual(seqA, seqB);

            Assert.AreNotEqual(lootA.Seed, dungeon.Seed);
            Assert.AreNotEqual(lootA.Seed, encounter.Seed);
            Assert.AreNotEqual(dungeon.Seed, encounter.Seed);
            Assert.AreNotEqual(lootA.Seed, lootOtherDepth.Seed);
        }
    }
}
