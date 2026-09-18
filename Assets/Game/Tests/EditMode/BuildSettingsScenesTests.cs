using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace RuinRail.Tests
{
    public class BuildSettingsScenesTests
    {
        [Test]
        public void BuildSettings_ContainsApprovedScenesInOrder()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.AreEqual(4, scenes.Length, "Expected exactly the four approved bootstrap scenes in Build Settings.");

            var expectedNames = new[] { "Bootstrap", "MainMenu", "Base", "Dungeon" };
            for (var i = 0; i < expectedNames.Length; i++)
            {
                Assert.IsTrue(scenes[i].enabled, $"Scene at index {i} should be enabled.");
                var sceneName = Path.GetFileNameWithoutExtension(scenes[i].path);
                Assert.AreEqual(expectedNames[i], sceneName, $"Scene at index {i} should be {expectedNames[i]}.");
            }
        }
    }
}
