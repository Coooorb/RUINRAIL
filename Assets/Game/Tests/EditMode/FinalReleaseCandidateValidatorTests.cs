using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// Final release audit, Phase 21/22: the release-candidate gate passes on the real repository (and writes the frozen
    /// release snapshot), and a deliberately broken fixture fails every rule it is meant to protect.
    /// </summary>
    public sealed class FinalReleaseCandidateValidatorTests
    {
        private const string SnapshotPath = "TestResults/FinalReleaseAudit/frozen_release_snapshot.csv";

        [Test]
        public void RealProject_PassesTheReleaseCandidateGate_AndWritesTheFrozenSnapshot()
        {
            var report = FinalReleaseCandidateValidator.WriteReport();
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath) ?? ".");
            File.WriteAllText(SnapshotPath, FinalReleaseSnapshot.ToCsv(FinalReleaseSnapshot.Collect()));
            var failures = report.Lines.Where(l => !l.Pass).Select(l => $"{l.Rule} / {l.Subject}: {string.Join("; ", l.Problems)}").ToList();
            CollectionAssert.IsEmpty(failures, "the release-candidate contract must hold on the real repository");
            Assert.Greater(report.Lines.Count, 40);
            Assert.IsTrue(File.Exists(FinalReleaseCandidateValidator.ReportPath));
        }

        [Test]
        public void Snapshot_CoversEveryFrozenFamily_AndRoundTripsThroughTheBaselineFormat()
        {
            var rows = FinalReleaseSnapshot.Collect();
            foreach (var prefix in new[] { "player.max_health", "player.dash", "ammo.cap.", "starter.kit", "loot.supply_chest.light_ammo", "weapon.", "weapon.field_knife", "blaster.heat.",
                         "boss.hp.", "depth.scaling.D", "reward.D", "elite.frequency", "rooms.depth_gated", "biome.weights", "progression.max_level", "progression.attribute_cap",
                         "inventory.backpack_size", "party.max_players", "coop.scaling", "player.revive", "pickup.attraction", "pickup.magnetic_coil_bonus", "aim_assist",
                         "count.audio_events", "count.rooms", "count.items", "count.weapons", "count.enemies_elites_bosses" })
                Assert.IsTrue(rows.Any(r => r.Key.StartsWith(prefix)), "snapshot family missing: " + prefix);
            Assert.AreEqual(33 + 1, rows.Count(r => r.Key.StartsWith("weapon.")), "33 weapon fingerprints plus the Field Knife row");
            Assert.AreEqual(3, rows.Count(r => r.Key.StartsWith("blaster.heat.")));
            Assert.AreEqual(6, rows.Count(r => r.Key.StartsWith("boss.hp.")));
            var parsed = FinalReleaseSnapshot.ParseCsv(FinalReleaseSnapshot.ToCsv(rows));
            Assert.AreEqual(rows.Count, parsed.Count);
            foreach (var row in rows) Assert.AreEqual(row.Value, parsed[row.Key], row.Key);
        }

        [Test]
        public void DeliberatelyBrokenFixture_FailsEveryReleaseRule()
        {
            var f = FinalReleaseCandidateValidator.Collect();
            Assert.IsTrue(FinalReleaseCandidateValidator.Validate(f).Pass, "the unbroken facts pass (precondition)");

            f.BuildSettingsScenes = f.BuildSettingsScenes.Reverse().Concat(new[] { "Assets/Game/Scenes/_TestScene.unity" }).ToArray();
            f.SubValidators["ContentCountValidator"] = false;
            f.SubValidators.Remove("CoopRuntimeCompositionValidator");
            f.MigrationChainUnbroken = false;
            f.DeepestDepthField = false;
            f.StarterKit = f.StarterKit.Where(k => !k.StartsWith("weapon_field_knife")).ToArray();
            f.RegisteredNetworkPrefabs = new string[0];
            f.ReplicatedDefinitionIds = f.ReplicatedDefinitionIds.Concat(f.ReplicatedDefinitionIds.Take(1)).ToArray();
            f.AudioListenerSites = 2;
            f.UiFontName = "LegacyRuntime";
            f.Screens["WeaponCacheView"] = false;
            f.ActionsWithoutGamepadGlyph = new[] { "QuickGrenade" };
            f.ActionsWithoutRebind = new[] { "Pause", "Interact" };
            f.Snapshot = new Dictionary<string, string>(f.Snapshot);
            f.Snapshot["weapon.field_knife"] = "melee Knife dmg 99-99";
            f.Snapshot["loot.supply_chest.light_ammo"] = "1-2 weight 4";
            f.Snapshot["blaster.heat.weapon_redline"] = "heat/shot 1";
            f.SmokeManifest = f.SmokeManifest.Where(r => r.Scenario != "returning" && r.Path != "boss").ToList();
            f.SupersededBanners["production/AUDIO_EVENT_AUDIT.md"] = false;
            f.RemoteMirrorComposesImpactPassives = false;
            f.RemoteMirrorDerivesFromMirroredEquipment = false;
            f.ClientRigExcludesImpactPassives = false;
            f.ImpactMechanicsOnMemberRig = new[] { "anchored" };
            f.ClientToHostPassiveKinds = new[] { "req.passive" };
            f.MissingRemoteConsequenceProofs = new[] { "exo_lock: no built-player host/client proof step" };
            f.InputDocBlanketRebindClaim = true;
            f.FixedBindings = f.FixedBindings.Concat(new[] { "Keyboard&Mouse:Dash" }).ToArray();

            var report = FinalReleaseCandidateValidator.Validate(f);
            Assert.IsFalse(report.Pass);
            foreach (var rule in new[] { "scenes", "pass validator", "save", "starter kit", "network registration", "enemy network registration", "audio listener", "ui font",
                         "ui screen", "input actions", "frozen snapshot", "frozen D1 ammo", "frozen Field Knife", "frozen blasters", "release smoke manifest", "superseded status",
                         "reactive passives", "rebinding contract" })
                Assert.IsTrue(report.Fails(rule), "the broken fixture must fail rule: " + rule);
            foreach (var subject in new[] { "remote member composition", "host derives the member's equipped passive", "host-authoritative, applied once", "remote-client consequence proofs" })
                Assert.IsTrue(report.Lines.Any(l => l.Rule == "reactive passives" && l.Subject == subject && !l.Pass), "the broken fixture must fail reactive passives / " + subject);

            // A missing baseline is a failure, never a silent pass.
            f.Baseline = null;
            Assert.IsTrue(FinalReleaseCandidateValidator.Validate(f).Fails("frozen snapshot"));
        }
    }
}
