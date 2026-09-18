using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;
using RuinRail.Presentation.Animation;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 138 — the final gate can list every outstanding external animation asset; the rules match art/103.</summary>
    public sealed class AnimationAssetAuditTests
    {
        [Test]
        public void Rules_MatchArt103()
        {
            Assert.AreEqual(8, AnimationRules.MinFps);
            Assert.AreEqual(12, AnimationRules.MaxFps);
            Assert.AreEqual(8, AnimationRules.ClampFps(1));
            Assert.AreEqual(12, AnimationRules.ClampFps(60));
            CollectionAssert.AreEqual(new[] { "Idle", "Walk", "Dash", "Downed", "GetUp", "Death" }, AnimationRules.PlayerClipKeys, "Player baseline: Idle, Walk, Dash, Downed, Revive/Get Up, Death.");
            CollectionAssert.Contains(AnimationRules.EnemyClipKeys, "Telegraph");
            Assert.AreEqual(8, AnimationRules.AllFacings.Length);
        }

        [Test]
        public void Audit_ListsPlayerNineEnemiesSixElitesSixBossesAnd33WeaponSprites_AsBlockedUntilArtArrives()
        {
            var report = AnimationAssetAudit.WriteReport();
            Assert.AreEqual(1 + 9 + 6 + 6, report.Actors.Count);
            Assert.AreEqual(1, report.Actors.Count(a => a.Kind == "Player"));
            Assert.AreEqual(9, report.Actors.Count(a => a.Kind == "Enemy"));
            Assert.AreEqual(6, report.Actors.Count(a => a.Kind == "Elite"));
            Assert.AreEqual(6, report.Actors.Count(a => a.Kind == "Boss"));
            Assert.AreEqual(33, report.WeaponSpritesRequired);
            Assert.AreEqual(6 * 8, report.Actors.First(a => a.Kind == "Player").Required);
            Assert.AreEqual(6 * 8, report.Actors.First(a => a.Kind == "Boss").Required);
            // Final art has landed, so this now asserts completeness rather than the old outstanding state.
            foreach (var actor in report.Actors)
            {
                Assert.IsTrue(actor.HasSet, actor.ActorId + ": no CharacterAnimationSet is bound.");
                Assert.AreEqual(0, actor.Missing, actor.ActorId + ": clip roles are still unresolved.");
            }

            Assert.AreEqual(33, report.WeaponSpritesPresent, "All 33 weapon world sprites resolve at their convention paths.");
            Assert.IsTrue(report.AllPresent, "22 sets x 48 clip roles plus 33 weapon sprites are all present.");
            StringAssert.Contains("COMPLETE", report.ToMarkdown());
            Assert.IsTrue(File.Exists(AnimationAssetAudit.ReportPath));
            Assert.AreEqual(report.ToMarkdown(), AnimationAssetAudit.Audit().ToMarkdown(), "Deterministic.");
        }
    }
}
