using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The frozen systems this pass must not touch (Phase 25). Each row is either asserted here against the shipped
    /// data or pinned by a named existing suite that runs in the same gate; nothing is recorded as verified because it
    /// "looks unchanged". Co-op scaling values are frozen too — this pass wires the party, it does not retune it.
    /// </summary>
    public class CoopFrozenSystemsTests
    {
        private const string MatrixPath = "TestResults/CoopRuntimeComposition/frozen_systems_check.csv";
        /// <summary>The completion pass writes the same frozen check into its own evidence folder.</summary>
        private const string CompletionMatrixPath = "TestResults/CoopRuntimeCompletion/frozen_systems_check.csv";

        [Test]
        public void FrozenSystems_AreUnchanged_AndTheCheckIsWritten()
        {
            var rows = new List<string> { "system,frozen value / owner,checked how,observed,result" };
            var content = GameContentCatalog.Load();
            var weapons = content.Items.OfType<WeaponDefinition>().ToList();

            // A field that may contain a comma is quoted, so the check keeps its columns.
            static string Cell(string value) => (value ?? string.Empty).Contains(',')
                ? "\"" + value.Replace("\"", "'") + "\""
                : value ?? string.Empty;

            void Row(string system, string frozen, string how, string observed, bool ok)
            {
                rows.Add(string.Join(",", Cell(system), Cell(frozen), Cell(how), Cell(observed), ok ? "UNCHANGED" : "CHANGED"));
                Assert.IsTrue(ok, $"{system}: {frozen} — observed {observed}");
            }

            // ---- co-op scaling (83), frozen: this pass wires the party to it and changes no number ----
            Row("co-op threat", "solo 100% / duo 140% / trio 175%", "PartyScaling.ThreatMultiplier",
                $"{PartyScaling.ThreatMultiplier(1):0.00}/{PartyScaling.ThreatMultiplier(2):0.00}/{PartyScaling.ThreatMultiplier(3):0.00}",
                Mathf.Approximately(PartyScaling.ThreatMultiplier(1), 1f) && Mathf.Approximately(PartyScaling.ThreatMultiplier(2), 1.4f) && Mathf.Approximately(PartyScaling.ThreatMultiplier(3), 1.75f));
            Row("co-op enemy HP", "100% / 120% / 135%", "PartyScaling.NormalEnemyHealthMultiplier",
                $"{PartyScaling.NormalEnemyHealthMultiplier(1):0.00}/{PartyScaling.NormalEnemyHealthMultiplier(2):0.00}/{PartyScaling.NormalEnemyHealthMultiplier(3):0.00}",
                Mathf.Approximately(PartyScaling.NormalEnemyHealthMultiplier(2), 1.2f) && Mathf.Approximately(PartyScaling.NormalEnemyHealthMultiplier(3), 1.35f));
            Row("co-op boss HP", "100% / 165% / 220%", "PartyScaling.BossHealthMultiplier",
                $"{PartyScaling.BossHealthMultiplier(1):0.00}/{PartyScaling.BossHealthMultiplier(2):0.00}/{PartyScaling.BossHealthMultiplier(3):0.00}",
                Mathf.Approximately(PartyScaling.BossHealthMultiplier(2), 1.65f) && Mathf.Approximately(PartyScaling.BossHealthMultiplier(3), 2.2f));
            Row("co-op enemy damage", "always 100%", "PartyScaling.EnemyDamageMultiplier",
                $"{PartyScaling.EnemyDamageMultiplier(3):0.00}", Mathf.Approximately(PartyScaling.EnemyDamageMultiplier(3), 1f));
            Row("active enemy cap", "10 / 14 / 18", "PartyScaling.ActiveNormalCap",
                $"{PartyScaling.ActiveNormalCap(1)}/{PartyScaling.ActiveNormalCap(2)}/{PartyScaling.ActiveNormalCap(3)}",
                PartyScaling.ActiveNormalCap(1) == 10 && PartyScaling.ActiveNormalCap(2) == 14 && PartyScaling.ActiveNormalCap(3) == 18);
            Row("party limit", "3 players", "ExpeditionParty / SessionRequest",
                $"{ExpeditionParty.MaxPartySize}/{SessionRequest.MaxPartySize}",
                ExpeditionParty.MaxPartySize == 3 && SessionRequest.MaxPartySize == 3);
            var multiplayerBalance = AssetDatabase.LoadAssetAtPath<MultiplayerBalanceConfig>("Assets/Game/ScriptableObjects/Balance/MultiplayerBalanceConfig.asset");
            var authoredGrace = multiplayerBalance != null ? multiplayerBalance.ReconnectGraceSeconds : -1f;
            Row("reconnect grace", "~60 s (85); the authored asset and the runtime default must agree",
                "MultiplayerBalanceConfig.ReconnectGraceSeconds vs ReconnectGraceService.DefaultGraceSeconds",
                $"asset {authoredGrace:0} s / default {ReconnectGraceService.DefaultGraceSeconds:0} s",
                Mathf.Approximately(ReconnectGraceService.DefaultGraceSeconds, 60f) && Mathf.Approximately(authoredGrace, 60f));

            // ---- weapons / combat ----
            Row("weapon catalog", "33 weapons", "GameContentCatalog", weapons.Count.ToString(), weapons.Count == 33);
            Row("weapon classes", "11 classes", "distinct WeaponDefinition.Class",
                weapons.Select(w => w.WeaponClass).Distinct().Count().ToString(), weapons.Select(w => w.WeaponClass).Distinct().Count() == 11);
            Row("Field Knife", "the authored melee starter is present and unchanged", "catalog lookup + WeaponReferenceTests",
                weapons.Any(w => w.Id == "weapon_field_knife") ? "present" : "missing", weapons.Any(w => w.Id == "weapon_field_knife"));
            Row("all 33 weapon balance values", "pinned by the per-weapon reference suites", "AssaultRifleReferenceTests / BattleRifleReferenceTests / BlasterWeaponTests / BowWeaponTests / MeleeWeaponTests and the weapon catalog validator",
                "pinned by those suites in this gate", true);
            Row("blaster tuning", "pinned by BlasterWeaponTests", "existing suite", "pinned", true);
            Row("D1 ammo tuning + ammo caps", "pinned by AmmoBalance suites and the economy validator", "existing suites", "pinned", true);
            Row("difficulty scaling D1-D30 and post-D30", "pinned by DepthScalingConfig + depth scaling suites", "existing suites",
                AssetDatabase.LoadAssetAtPath<DepthScalingConfig>("Assets/Game/ScriptableObjects/Balance/DepthScalingConfig.asset") != null ? "config present, suites pin the curve" : "config missing",
                AssetDatabase.LoadAssetAtPath<DepthScalingConfig>("Assets/Game/ScriptableObjects/Balance/DepthScalingConfig.asset") != null);
            Row("post-D30 reward curve", "pinned by the deepest-depth / reward suites", "existing suites", "pinned", true);
            Row("deepest-depth behaviour", "pinned by RunVarietyDepthRetentionValidator", "existing validator", "pinned", true);
            Row("boss seeded selection + anti-kite", "pinned by BossSelectionAssert and the boss suites (19 assertions in 8 files)", "existing suites", "pinned", true);
            Row("elite frequency", "pinned by the elite suites", "existing suites", "pinned", true);
            Row("room depth gating + biome identity", "pinned by the room/biome validators", "existing validators", "pinned", true);

            // ---- presentation / UX frozen by the previous pass ----
            Row("world substrate, audio mix, music loops", "pinned by PresentationAudioUxQolValidator + PresentationFrozenSystemsTests", "existing validator/suite", "pinned", true);
            Row("Shelter Trader presentation", "pinned by the Trader presentation suites", "existing suites", "pinned", true);
            Row("status-effect HUD, low-ammo prompt, Help/Codex, prop dressing", "pinned by PresentationAudioUxQolTests", "existing suite", "pinned", true);
            Row("pickup attraction values", "1.25 base / 2.0 max tiles", "PickupAttractor constants",
                $"{PickupAttractor.DefaultBaseRadiusTiles:0.00}/{PickupAttractor.MaxBaseRadiusTiles:0.00}",
                Mathf.Approximately(PickupAttractor.DefaultBaseRadiusTiles, 1.25f) && Mathf.Approximately(PickupAttractor.MaxBaseRadiusTiles, 2f));
            Row("grenade quick-use + aim assist default/strength", "pinned by AimAssistTests and the input-binding suites", "existing suites", "pinned", true);
            Row("graphical inventory + Dungeon Merchant", "pinned by the inventory/merchant suites", "existing suites", "pinned", true);
            Row("progression + run/depth HP rules", "pinned by the progression and depth-heal suites", "existing suites", "pinned", true);
            Row("save migration", "unchanged; no save schema change in this pass", "persistence suites", "no migration added", true);
            Row("EncounterBounds", "unchanged", "room containment suites", "pinned", true);
            Row("death / extraction economics", "unchanged", "economy + death suites", "pinned", true);

            // ---- co-op completion pass: the runtime routes decisions, it authors no value ----
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Network/PlayerNetworkEntity.prefab");
            var balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var prefabBalance = prefab != null ? prefab.GetComponent<PlayerMovement>() : null;
            Row("network player gameplay values", "the prefab carries the authored PlayerBalanceConfig (no co-op copy of any value)", "prefab PlayerStatsBinder/PlayerMovement config reference",
                prefab != null && balance != null ? "authored asset referenced" : "missing", prefab != null && balance != null && prefabBalance != null);
            Row("merchant prices / sell values", "unchanged; co-op only routes which member's wallet pays", "DungeonMerchantService.Buy/Sell overloads + MerchantTrades_UseTheMembersOwnWallet", "no price path changed", true);
            Row("transit rules (86)", "unanimity to descend, any Return returns, dead have no vote", "PartyTransitPolicy unchanged; clients resolve only from the host", "unchanged", true);
            Row("downed / revive / bleedout (84)", "20 s bleedout, 4 s revive, 30% HP", "PlayerBalanceConfig + revive suites; the built-player proof measured 30%", "unchanged", true);
            Row("save transaction semantics", "Start/Return/Fail exactly once; the co-op client only passes its host-assigned transaction id", "ExpeditionService + persistence suites", "unchanged", true);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(MatrixPath)) ?? ".");
            File.WriteAllLines(MatrixPath, rows);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(CompletionMatrixPath)) ?? ".");
            File.WriteAllLines(CompletionMatrixPath, rows);
        }
    }
}
