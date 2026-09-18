using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;
using UnityEditor;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 147 — build configuration: approved scene order, present scene files, no test scenes, release options, and the content catalog the build ships.</summary>
    public sealed class ReleaseBuildToolTests
    {
        [Test]
        public void BuildConfiguration_ApprovedSceneOrder_NoTestScenes_ReleaseOptions()
        {
            CollectionAssert.AreEqual(new[] { "Bootstrap", "MainMenu", "Base", "Dungeon" }, ReleaseBuildTool.SceneOrder);
            foreach (var path in ReleaseBuildTool.ScenePaths) Assert.IsTrue(File.Exists(path), path);
            Assert.IsFalse(ReleaseBuildTool.ScenePaths.Any(p => p.Contains("_Test")));
            CollectionAssert.AreEqual(ReleaseBuildTool.ScenePaths, EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), "Build settings match the approved order.");
            Assert.IsFalse(EditorUserBuildSettings.development, "Release builds are non-development.");
            var catalog = GameContentCatalogBuilder.Build();
            CollectionAssert.IsEmpty(catalog.Problems());
            Assert.IsTrue(File.Exists(GameContentCatalogBuilder.CatalogPath));
        }
    }
}
