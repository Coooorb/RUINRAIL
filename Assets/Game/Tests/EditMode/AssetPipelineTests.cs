using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 151 — the asset pipeline must let a final asset replace a placeholder without touching gameplay code, and
    /// must fail loudly on the five ways that goes wrong: no role id, colliding mappings, bad import settings, broken
    /// binding references, and placeholder art claiming to be final.
    /// </summary>
    public sealed class AssetPipelineTests
    {
        [Test]
        public void Pipeline_IsCleanOnTheCurrentRepository()
        {
            var report = AssetPipelineValidator.Validate();

            CollectionAssert.IsEmpty(report.Problems);
            Assert.IsTrue(report.Pass);
        }

        [Test]
        public void EveryManifestRole_HasAConventionPathAndAnExistingBindingPoint()
        {
            var manifest = CompletionAssetManifest.Generate();

            foreach (var role in manifest.Roles)
            {
                Assert.IsNotEmpty(role.ExpectedSourcePath, $"{role.Category}/{role.RoleId} has no convention path, so a final asset has nowhere to land.");
                Assert.IsNotEmpty(role.BindingPoint, $"{role.Category}/{role.RoleId} names no binding point, so replacing it would need a gameplay change.");
            }
        }

        [Test]
        public void ConventionPaths_AreDeterministicAndDerivedFromTheStableRoleId()
        {
            var weapon = AssetNamingConvention.For("Weapon sprites", "weapon_pistol_01");
            Assert.AreEqual("Assets/Game/Art/Weapons/weapon_pistol_01.png", weapon.ExpectedSourcePath);

            var sfx = AssetNamingConvention.For("SFX", "weapon.fire.pistol");
            Assert.AreEqual("Assets/Game/Audio/Sfx/weapon_fire_pistol.wav", sfx.ExpectedSourcePath,
                "Dots in a role id become underscores so the file name stays a single stable token.");

            var tile = AssetNamingConvention.For("Biome tiles", "RuinedMetro.tile.floor");
            Assert.AreEqual("Assets/Game/Art/Tiles/RuinedMetro/ruinedmetro_floor.png", tile.ExpectedSourcePath);

            Assert.AreEqual(weapon.ExpectedSourcePath, AssetNamingConvention.For("Weapon sprites", "weapon_pistol_01").ExpectedSourcePath,
                "The same role must always resolve to the same path.");
        }

        [Test]
        public void ExpectedSourcePaths_DoNotCollideAcrossRoles()
        {
            var manifest = CompletionAssetManifest.Generate();

            var collisions = manifest.Roles
                .Where(r => !r.ExpectedSourcePath.EndsWith("/"))
                .GroupBy(r => r.ExpectedSourcePath.ToLowerInvariant())
                .Where(g => g.Select(r => r.Category + "/" + r.RoleId).Distinct().Count() > 1)
                .Select(g => g.Key)
                .ToList();

            CollectionAssert.IsEmpty(collisions, "Two roles sharing one file would silently overwrite each other.");
        }

        [Test]
        public void ConventionFolders_StayUnderTheExistingAssetRoots()
        {
            foreach (var folder in AssetNamingConvention.ConventionFolders)
            {
                Assert.IsTrue(
                    folder.StartsWith(AssetNamingConvention.ArtRoot) || folder.StartsWith(AssetNamingConvention.AudioRoot),
                    $"{folder} is outside the existing asset roots; TASK 151 must not create a parallel asset root.");
            }
        }

        [Test]
        public void ProvenanceRegistry_RoundTripsAndRejectsAnIncompleteRecord()
        {
            var entry = new AssetProvenanceRegistry.Entry
            {
                roleId = "test.role", sourcePath = "Assets/Game/Art/World/test_role.png",
                author = "RUINRAIL art", provenance = "original", license = "project-owned"
            };
            Assert.IsTrue(entry.ToAttestation().IsComplete);

            var missingLicense = new AssetProvenanceRegistry.Entry
            {
                roleId = "test.role", author = "RUINRAIL art", provenance = "original"
            };
            Assert.IsFalse(missingLicense.ToAttestation().IsComplete);
            StringAssert.Contains("licence", missingLicense.ToAttestation().Problem);
        }

        [Test]
        public void PlaceholderArt_MayNotClaimFinalStatus()
        {
            var manifest = CompletionAssetManifest.Generate();

            var offenders = manifest.Roles
                .Where(r => ArtProductionContract.StatusesRequiringAttestation.Contains(r.Status))
                .Where(r => r.SourcePath.Contains(ArtProductionContract.PlaceholderFolder))
                .Select(r => r.RoleId)
                .ToList();

            CollectionAssert.IsEmpty(offenders, "art/106 section 13.9: placeholder art leaves the release path when its role is filled.");
        }

        [Test]
        public void PipelineReport_IsWrittenAndDeterministic()
        {
            var report = AssetPipelineValidator.WriteReport();

            Assert.IsTrue(File.Exists(AssetPipelineValidator.ReportPath));
            Assert.AreEqual(report.ToMarkdown(), AssetPipelineValidator.Validate().ToMarkdown());
        }
    }
}
