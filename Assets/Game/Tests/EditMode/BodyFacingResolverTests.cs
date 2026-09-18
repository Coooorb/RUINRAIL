using NUnit.Framework;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Tests
{
    public class BodyFacingResolverTests
    {
        [TestCase(1f, 0f, BodyFacing8.E)]
        [TestCase(1f, 1f, BodyFacing8.NE)]
        [TestCase(0f, 1f, BodyFacing8.N)]
        [TestCase(-1f, 1f, BodyFacing8.NW)]
        [TestCase(-1f, 0f, BodyFacing8.W)]
        [TestCase(-1f, -1f, BodyFacing8.SW)]
        [TestCase(0f, -1f, BodyFacing8.S)]
        [TestCase(1f, -1f, BodyFacing8.SE)]
        public void Resolve_ReturnsExpectedOctant(float x, float y, BodyFacing8 expected)
        {
            var result = BodyFacingResolver.Resolve(new Vector2(x, y));
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void Resolve_QuantizesAngleNear0DegreesToEast()
        {
            var direction = new Vector2(Mathf.Cos(10f * Mathf.Deg2Rad), Mathf.Sin(10f * Mathf.Deg2Rad));
            Assert.AreEqual(BodyFacing8.E, BodyFacingResolver.Resolve(direction));
        }

        [Test]
        public void Resolve_QuantizesAngleNear45DegreesToNorthEast()
        {
            var direction = new Vector2(Mathf.Cos(40f * Mathf.Deg2Rad), Mathf.Sin(40f * Mathf.Deg2Rad));
            Assert.AreEqual(BodyFacing8.NE, BodyFacingResolver.Resolve(direction));
        }
    }
}
