using NUnit.Framework;
using RuinRail.Core.Input;
using UnityEngine;

namespace RuinRail.Tests
{
    public class PlayerInputReaderTests
    {
        [Test]
        public void PlayerInputReader_StartsWithNeutralIntentAndCanEnableDisableDispose()
        {
            var reader = new PlayerInputReader();

            Assert.AreEqual(Vector2.zero, reader.Move);
            Assert.AreEqual(Vector2.zero, reader.Aim);
            Assert.IsFalse(reader.FireHeld);
            Assert.IsFalse(reader.SpecialHeld);

            Assert.DoesNotThrow(() => reader.Enable());
            Assert.DoesNotThrow(() => reader.Disable());
            Assert.DoesNotThrow(() => reader.Dispose());
        }
    }
}
