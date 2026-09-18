using NUnit.Framework;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Tests
{
    public class PlayerBalanceConfigTests
    {
        [Test]
        public void MoveSpeed_DefaultsToAPositiveValue()
        {
            var config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            try
            {
                Assert.Greater(config.MoveSpeed, 0f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DashDuration_DefaultsToApprovedV1Value()
        {
            var config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            try
            {
                Assert.AreEqual(0.18f, config.DashDuration, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DashCooldown_DefaultsToApprovedV1Value()
        {
            var config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            try
            {
                Assert.AreEqual(1.4706f, config.DashCooldown, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DashSpeed_DefaultsToAPositiveValue()
        {
            var config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            try
            {
                Assert.Greater(config.DashSpeed, 0f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DashIFrameDuration_DefaultsToCorrectedV1Value()
        {
            var config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            try
            {
                Assert.AreEqual(0.10f, config.DashIFrameDuration, 0.001f);
                Assert.Less(config.DashIFrameDuration, config.DashDuration,
                    "The default iFrame window must end strictly before the default dash movement ends.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
